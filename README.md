# Serilog.Sinks.SQLite.Net-PCL
A lightweight high performance Serilog sink that writes to SQLite database using SQLiteNetPcl (Xamarin, NetStandard 2.0 & 2.1).

Perfect for stocking logs in a sqlite database in your Xamarin.Forms project :)

## Getting started
Install [Serilog.Sinks.SQLite.Net-PCL](https://www.nuget.org/packages/Serilog.Sinks.SQLite.Net-PCL) from NuGet

Configure logger by calling `WriteTo.SQLiteNetPcl()`

```C#
var logger = new LoggerConfiguration()
    .WriteTo.SQLiteNetPcl(@"Logs\log.db")
    .CreateLogger();
    
logger.Information("This informational message will be written to SQLite database");