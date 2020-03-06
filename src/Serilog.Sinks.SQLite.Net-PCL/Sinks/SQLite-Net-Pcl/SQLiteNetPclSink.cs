// Copyright 2020 Serilog Contributors
// 
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
// 
//     http://www.apache.org/licenses/LICENSE-2.0
// 
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Serilog.Core;
using Serilog.Debugging;
using Serilog.Events;
using Serilog.Sinks.SQLite.NetPCL;
using SQLite;

namespace Serilog.Sinks.SQLite.Net.Pcl
{

    internal class SQLiteNetPclSink : BatchProvider, ILogEventSink
    {
        private readonly string _databasePath;
        private readonly IFormatProvider _formatProvider;
        private readonly bool _storeTimestampInUtc;
        private readonly uint _maxDatabaseSize;
        private readonly bool _rollOver;
        private readonly TimeSpan? _retentionPeriod;
        private readonly Timer _retentionTimer;
        private const long BytesPerMb = 1_048_576;
        private const long MaxSupportedPages = 5_242_880;
        private const long MaxSupportedPageSize = 4096;
        private const long MaxSupportedDatabaseSize = unchecked(MaxSupportedPageSize * MaxSupportedPages) / 1048576;
        private static SemaphoreSlim semaphoreSlim = new SemaphoreSlim(1, 1);

        public SQLiteNetPclSink(
            string sqlLiteDbPath,
            IFormatProvider formatProvider,
            bool storeTimestampInUtc,
            TimeSpan? retentionPeriod,
            TimeSpan? retentionCheckInterval,
            uint batchSize = 100,
            uint maxDatabaseSize = 10,
            bool rollOver = true) : base(batchSize: (int)batchSize, maxBufferSize: 100_000)
        {
            _databasePath = sqlLiteDbPath;
            _formatProvider = formatProvider;
            _storeTimestampInUtc = storeTimestampInUtc;
            _maxDatabaseSize = maxDatabaseSize;
            _rollOver = rollOver;

            if (maxDatabaseSize > MaxSupportedDatabaseSize)
            {
                throw new Exception($"Database size greater than {MaxSupportedDatabaseSize} MB is not supported");
            }

            InitializeDatabase();

            if (_rollOver)
            {
                RollDatabase();
            }

            if (retentionPeriod.HasValue)
            {
                // impose a min retention period of 15 minute
                var retentionCheckMinutes = 15;
                if (retentionCheckInterval.HasValue)
                {
                    retentionCheckMinutes = Math.Max(retentionCheckMinutes, retentionCheckInterval.Value.Minutes);
                }

                // impose multiple of 15 minute interval
                retentionCheckMinutes = (retentionCheckMinutes / 15) * 15;

                _retentionPeriod = new[] { retentionPeriod, TimeSpan.FromMinutes(30) }.Max();

                // check for retention at this interval - or use retentionPeriod if not specified
                _retentionTimer = new Timer(
                    (x) => { ApplyRetentionPolicy(); },
                    null,
                    TimeSpan.FromMinutes(0),
                    TimeSpan.FromMinutes(retentionCheckMinutes));
            }
        }

        private void RollDatabase()
        {
            try
            {
                using (var dnConn = GetSqLiteConnection())
                {
                    if (new FileInfo(_databasePath).Length > _maxDatabaseSize * 1024 * 1024)
                    {
                        var dbExtension = Path.GetExtension(_databasePath);
                        var newFilePath = Path.Combine(Path.GetDirectoryName(_databasePath) ?? "Logs",
                            $"{Path.GetFileNameWithoutExtension(_databasePath)}-{DateTime.Now:yyyyMMdd_hhmmss.ff}{dbExtension}");
                        File.Copy(_databasePath, newFilePath, true);
                        dnConn.DeleteAll<Logs>();
                        SelfLog.WriteLine($"Rolling database to {newFilePath}");
                    }
                }
            }
            catch (Exception e)
            {
                SelfLog.WriteLine(e.Message);
            }
        }

        #region ILogEvent implementation

        public void Emit(LogEvent logEvent)
        {
            PushEvent(logEvent);
        }

        #endregion

        private void InitializeDatabase()
        {
            using (var conn = GetSqLiteConnection())
            {
                conn.CreateTable<Logs>();
            }
        }

        public SQLiteConnection GetSqLiteConnection()
        {
            var dbConnection = new SQLiteConnection(_databasePath, SQLiteOpenFlags.ReadWrite | SQLiteOpenFlags.Create | SQLiteOpenFlags.SharedCache, false);
            return dbConnection;
        }

        private void ApplyRetentionPolicy()
        {
            try
            {
                using (var dbConn = GetSqLiteConnection())
                {
                    var epoch = DateTimeOffset.Now.Subtract(_retentionPeriod.Value);
                    SelfLog.WriteLine("Deleting log entries older than {0}", epoch);
                    var items = new List<Logs>();
                    if (_storeTimestampInUtc)
                        items = dbConn.Table<Logs>().ToList()?.Where(p => p.Timestamp.HasValue && p.Timestamp.Value <= epoch.ToUniversalTime())?.ToList();
                    else
                        items = dbConn.Table<Logs>().ToList()?.Where(p => p.Timestamp.HasValue && p.Timestamp.Value <= epoch)?.ToList();
                    if (items?.Count > 0)
                    {
                        DeleteAllEvents(dbConn, items, true);
                        SelfLog.WriteLine($"'{items.Count}' detected...");
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex.Message);
            }
        }

        private bool DeleteAllEvents(SQLiteConnection dbConn, ICollection<Logs> items, bool runWithTransaction = true)
        {
            if (items?.Count > 0)
            {
                if (runWithTransaction)
                    dbConn.BeginTransaction();
                try
                {
                    items?.ToList().ForEach(currentItem =>
                    {
                        SelfLog.WriteLine($"Trying to purge item '{currentItem.Id}'");
                        if (dbConn.Delete<Logs>(currentItem.Id) < 1)
                        {
                            SelfLog.WriteLine($"Error deleting log n°'{currentItem.Id}");
                        }
                    });
                    if (runWithTransaction)
                        dbConn.Commit();
                    SelfLog.WriteLine($"{items?.Count} records deleted");
                }
                catch (Exception ex)
                {
                    if (runWithTransaction)
                    {
                        Log.Error("Rollback", ex);
                        dbConn.Rollback();
                    }
                    else
                    {
                        Log.Error(ex.Message);
                    }
                    return false;
                }
            }
            return true;
        }

        protected override async Task<bool> WriteLogEventAsync(ICollection<LogEvent> logEventsBatch)
        {
            if ((logEventsBatch == null) || (logEventsBatch.Count == 0))
                return true;

            await semaphoreSlim.WaitAsync().ConfigureAwait(false);

            try
            {
                using (var dbConn = GetSqLiteConnection())
                {
                    if (logEventsBatch?.Count > 0)
                    {
                        dbConn.BeginTransaction();
                        try
                        {
                            var items = new List<Logs>();
                            logEventsBatch?.ToList().ForEach(currentLogEvent =>
                            {
                                items.Add(new Logs()
                                {
                                    Timestamp = _storeTimestampInUtc ? currentLogEvent.Timestamp.UtcDateTime : currentLogEvent.Timestamp.DateTime,
                                    RenderedMessage = currentLogEvent.MessageTemplate.Text,
                                    Level = currentLogEvent.Level.ToString(),
                                    Exception = currentLogEvent.Exception?.ToString() ?? string.Empty,
                                    Properties = currentLogEvent.Properties.Count > 0 ? currentLogEvent.Properties.Json() : string.Empty
                                });
                            });
                            if (items?.Count > 0)
                            {
                                dbConn.InsertAll(items, true);
                                dbConn.Commit();
                            }
                        }
                        catch (Exception ex)
                        {
                            Log.Error("Rollback", ex);
                            dbConn.Rollback();
                            return false;
                        }
                    }
                }
                return true;
            }
            finally
            {
                semaphoreSlim.Release();
            }
        }
    }
}
