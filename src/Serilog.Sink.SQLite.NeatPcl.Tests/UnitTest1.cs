using Serilog;
using System;
using Xunit;

namespace Serilog.Sink.SQLite.NeatPcl.Tests
{
    public class UnitTest1
    {
        public UnitTest1()
        {
            Log.Logger = new LoggerConfiguration().WriteTo.SQLiteNetPcl(@".\Test.db3", Serilog.Events.LogEventLevel.Debug, null, true, TimeSpan.FromDays(7), TimeSpan.FromHours(1),
                null, 100, 10, true).CreateLogger();
        }

        [Fact]
        public void LogDebug()
        {
            Log.Debug("Write a Debug log...");
        }

        [Fact]
        public void LogInformation()
        {
            Log.Information("Write a Information log...");
        }

        [Fact]
        public void LogWarning()
        {
            Log.Warning("Write a Warning log...");
        }

        [Fact]
        public void LogError()
        {
            Log.Error("Write a Erro log...");
        }
    }
}
