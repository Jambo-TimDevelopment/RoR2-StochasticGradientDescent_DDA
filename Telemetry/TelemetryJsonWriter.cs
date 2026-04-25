using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace GeneticsArtifact.Telemetry
{
    internal static class TelemetryJsonWriter
    {
        public static string BuildPostHogBatch(string apiKey, IReadOnlyList<TelemetryEvent> events)
        {
            var sb = new StringBuilder(4096);
            sb.Append('{');
            WritePropertyName(sb, "api_key");
            WriteString(sb, apiKey);
            sb.Append(',');
            WritePropertyName(sb, "historical_migration");
            sb.Append("false");
            sb.Append(',');
            WritePropertyName(sb, "batch");
            sb.Append('[');

            for (int i = 0; i < events.Count; i++)
            {
                if (i > 0) sb.Append(',');
                WriteEvent(sb, events[i]);
            }

            sb.Append(']');
            sb.Append('}');
            return sb.ToString();
        }

        private static void WriteEvent(StringBuilder sb, TelemetryEvent telemetryEvent)
        {
            sb.Append('{');
            WritePropertyName(sb, "event");
            WriteString(sb, telemetryEvent.EventName);
            sb.Append(',');
            WritePropertyName(sb, "timestamp");
            WriteString(sb, telemetryEvent.TimestampUtc.ToString("O", CultureInfo.InvariantCulture));
            sb.Append(',');
            WritePropertyName(sb, "properties");
            WriteDictionary(sb, telemetryEvent.Properties);
            sb.Append('}');
        }

        private static void WriteDictionary(StringBuilder sb, Dictionary<string, object> values)
        {
            sb.Append('{');
            bool first = true;
            foreach (var pair in values)
            {
                if (!first) sb.Append(',');
                first = false;
                WritePropertyName(sb, pair.Key);
                WriteValue(sb, pair.Value);
            }
            sb.Append('}');
        }

        private static void WritePropertyName(StringBuilder sb, string name)
        {
            WriteString(sb, name);
            sb.Append(':');
        }

        private static void WriteValue(StringBuilder sb, object value)
        {
            if (value == null)
            {
                sb.Append("null");
                return;
            }

            if (value is string s)
            {
                WriteString(sb, s);
                return;
            }

            if (value is bool b)
            {
                sb.Append(b ? "true" : "false");
                return;
            }

            if (value is int || value is long || value is short || value is byte)
            {
                sb.Append(Convert.ToString(value, CultureInfo.InvariantCulture));
                return;
            }

            if (value is float f)
            {
                WriteFloat(sb, f);
                return;
            }

            if (value is double d)
            {
                WriteDouble(sb, d);
                return;
            }

            WriteString(sb, Convert.ToString(value, CultureInfo.InvariantCulture));
        }

        private static void WriteFloat(StringBuilder sb, float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
            {
                sb.Append('0');
                return;
            }

            sb.Append(value.ToString("R", CultureInfo.InvariantCulture));
        }

        private static void WriteDouble(StringBuilder sb, double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                sb.Append('0');
                return;
            }

            sb.Append(value.ToString("R", CultureInfo.InvariantCulture));
        }

        private static void WriteString(StringBuilder sb, string value)
        {
            sb.Append('"');
            if (!string.IsNullOrEmpty(value))
            {
                for (int i = 0; i < value.Length; i++)
                {
                    char c = value[i];
                    switch (c)
                    {
                        case '\\':
                            sb.Append("\\\\");
                            break;
                        case '"':
                            sb.Append("\\\"");
                            break;
                        case '\n':
                            sb.Append("\\n");
                            break;
                        case '\r':
                            sb.Append("\\r");
                            break;
                        case '\t':
                            sb.Append("\\t");
                            break;
                        default:
                            if (c < 32)
                            {
                                sb.Append("\\u");
                                sb.Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                            }
                            else
                            {
                                sb.Append(c);
                            }
                            break;
                    }
                }
            }
            sb.Append('"');
        }
    }
}
