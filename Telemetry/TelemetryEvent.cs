using System;
using System.Collections.Generic;

namespace GeneticsArtifact.Telemetry
{
    internal sealed class TelemetryEvent
    {
        public string EventName { get; }
        public DateTime TimestampUtc { get; }
        public Dictionary<string, object> Properties { get; }

        public TelemetryEvent(string eventName, Dictionary<string, object> properties)
        {
            EventName = eventName;
            TimestampUtc = DateTime.UtcNow;
            Properties = properties ?? new Dictionary<string, object>();
        }
    }
}
