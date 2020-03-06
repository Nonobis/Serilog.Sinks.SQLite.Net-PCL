using SQLite;
using System;
using System.Collections.Generic;
using System.Text;

namespace Serilog.Sinks.SQLite.NetPCL
{
    internal class Logs
    {
        [PrimaryKey]
        [AutoIncrement]
        public int Id { get; set; }

        public DateTime? Timestamp { get; set; }

        [MaxLength(10)]
        public string Level { get; set; }

        public string Exception { get; set; }

        public string RenderedMessage { get; set; }

        public string Properties { get; set; }
    }
}
