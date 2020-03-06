using Serilog.Debugging;
using SQLite;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Serilog.Sinks.SQLite.NetPCL
{
    public static class SqlConnectionExtensions
    {
        public static bool WriteAll<T>(this SQLiteConnection dbConn, ICollection<T> items, bool runWithTransaction)
        {
            if (items?.Count > 0)
            {
                if (runWithTransaction)
                    dbConn.BeginTransaction();
                try
                {
                    dbConn.InsertAll(items, runWithTransaction);
                    if (runWithTransaction)
                        dbConn.Commit();
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
    }
}
