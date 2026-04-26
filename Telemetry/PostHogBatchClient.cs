using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace GeneticsArtifact.Telemetry
{
    internal static class PostHogBatchClient
    {
        public static IEnumerator FlushAllQueuedEvents(float maxSeconds = 8f, int maxBatches = 64)
        {
            if (!ConfigManager.telemetryEnabled.Value)
            {
                yield break;
            }

            float startedAt = Time.realtimeSinceStartup;
            int batches = 0;

            while (TelemetryEventQueue.Count > 0)
            {
                if (batches >= maxBatches)
                {
                    GeneticsArtifactPlugin.geneticLogSource?.LogWarning("[Telemetry] PostHog flush stopped: maxBatches reached (" + maxBatches + "). Remaining=" + TelemetryEventQueue.Count);
                    yield break;
                }

                if (Time.realtimeSinceStartup - startedAt > maxSeconds)
                {
                    GeneticsArtifactPlugin.geneticLogSource?.LogWarning("[Telemetry] PostHog flush stopped: timeout (" + maxSeconds + "s). Remaining=" + TelemetryEventQueue.Count);
                    yield break;
                }

                int before = TelemetryEventQueue.Count;
                yield return FlushQueuedEvents();
                batches++;

                // If a batch failed, it is restored. Avoid busy-looping on repeated failures.
                if (TelemetryEventQueue.Count >= before)
                {
                    yield break;
                }

                // Yield one frame between batches so quit flows can breathe.
                yield return null;
            }
        }

        public static IEnumerator FlushQueuedEvents()
        {
            if (!ConfigManager.telemetryEnabled.Value)
            {
                yield break;
            }

            string projectToken = TelemetryBuildSecrets.PostHogProjectToken;
            if (string.IsNullOrWhiteSpace(projectToken))
            {
                yield break;
            }

            if (!TelemetryEventQueue.TryDequeueBatchAsPostHogJson(projectToken, out string payload, out int batchCount))
            {
                yield break;
            }
            if (string.IsNullOrEmpty(payload))
            {
                yield break;
            }

            string host = NormalizeHost(TelemetryBuildSecrets.PostHogHost);
            byte[] body = Encoding.UTF8.GetBytes(payload);

            using (var request = new UnityWebRequest(host + "/batch/", "POST"))
            {
                request.uploadHandler = new UploadHandlerRaw(body);
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Content-Type", "application/json");
                request.timeout = 5;

                yield return request.SendWebRequest();

                if (request.result == UnityWebRequest.Result.ConnectionError ||
                    request.result == UnityWebRequest.Result.ProtocolError ||
                    request.result == UnityWebRequest.Result.DataProcessingError)
                {
                    TelemetryEventQueue.RestoreLastBatchForRetry();
                    GeneticsArtifactPlugin.geneticLogSource?.LogWarning("[Telemetry] PostHog flush failed: " + request.error);
                }
                else
                {
                    TelemetryEventQueue.MarkLastBatchSent();
                }
            }
        }

        private static string NormalizeHost(string host)
        {
            if (string.IsNullOrWhiteSpace(host))
            {
                return "https://us.i.posthog.com";
            }

            host = host.Trim();
            while (host.EndsWith("/"))
            {
                host = host.Substring(0, host.Length - 1);
            }

            return host;
        }

    }
}
