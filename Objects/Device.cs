using System;
using System.Collections.Generic;
using System.Text;
using Azure;
using Azure.Data.Tables;

namespace joseevillasmil.IOT.Server.Objects
{
    public class Device : ITableEntity
    {
        public string PartitionKey { get; set; }
        public string RowKey { get; set; }
        public DateTimeOffset? Timestamp { get; set; }
        public ETag ETag { get; set; }
        public string Status { get; set; }
        public DateTime UpdatedAt { get; set; }
    }
}
