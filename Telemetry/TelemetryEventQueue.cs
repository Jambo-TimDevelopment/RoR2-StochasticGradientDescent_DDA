using System.Collections.Generic;

namespace GeneticsArtifact.Telemetry
{
    internal static class TelemetryEventQueue
    {
        private const int DefaultBatchSize = 32;

        private static readonly Queue<TelemetryEvent> Events = new Queue<TelemetryEvent>(128);
        private static List<TelemetryEvent> _lastBatch;

        public static int Count => Events.Count;

        public static void Enqueue(TelemetryEvent telemetryEvent)
        {
            if (telemetryEvent == null) return;

            int maxQueueSize = ConfigManager.telemetryMaxQueueSize?.Value ?? 512;
            maxQueueSize = UnityEngine.Mathf.Clamp(maxQueueSize, 32, 5000);

            while (Events.Count >= maxQueueSize)
            {
                Events.Dequeue();
            }

            Events.Enqueue(telemetryEvent);
        }

        public static bool TryDequeueBatchAsPostHogJson(string projectToken, out string payload, out int batchCount)
        {
            payload = "";
            batchCount = 0;

            if (string.IsNullOrWhiteSpace(projectToken) || Events.Count == 0)
            {
                return false;
            }

            int count = UnityEngine.Mathf.Min(DefaultBatchSize, Events.Count);
            batchCount = count;
            _lastBatch = new List<TelemetryEvent>(count);

            for (int i = 0; i < count; i++)
            {
                _lastBatch.Add(Events.Dequeue());
            }

            payload = TelemetryJsonWriter.BuildPostHogBatch(projectToken.Trim(), _lastBatch);

            return !string.IsNullOrEmpty(payload);
        }

        public static string DequeueBatchAsPostHogJson(string projectToken)
        {
            return TryDequeueBatchAsPostHogJson(projectToken, out string payload, out _) ? payload : "";
        }

        public static void MarkLastBatchSent()
        {
            _lastBatch = null;
        }

        public static void RestoreLastBatchForRetry()
        {
            if (_lastBatch == null || _lastBatch.Count == 0)
            {
                return;
            }

            int restoreCount = _lastBatch.Count;
            for (int i = 0; i < _lastBatch.Count; i++)
            {
                Events.Enqueue(_lastBatch[i]);
            }

            _lastBatch = null;
        }
    }
}
