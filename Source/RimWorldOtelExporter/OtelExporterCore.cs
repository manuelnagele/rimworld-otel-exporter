using System;
using System.Collections.Generic;
using System.Threading;
using OpenTelemetry.Proto.Metrics.V1;
using RimWorldOtelExporter.Transport;

namespace RimWorldOtelExporter
{
    /// <summary>Singleton lifetime management for transport objects.</summary>
    public static class OtelExporterCore
    {
        public static OtlpHttpSender? Sender { get; private set; }
        public static ExportQueue? Queue { get; private set; }

        public static void Init()
        {
            Sender = new OtlpHttpSender();
            Sender.Configure(OtelExporterMod.Settings.AuthHeader, OtelExporterMod.Settings.OrgId);

            Queue = new ExportQueue(Sender);
            Queue.OnWarning += msg => Verse.Log.Warning(msg);
            Queue.OnExportSuccess += (endpoint, bytes) =>
            {
                var s = OtelExporterMod.Settings;
                s.LastExportTime = DateTime.UtcNow;
                s.LastExportSuccess = true;
                s.LastPayloadBytes = bytes;
                s.LastError = "";
            };
            Queue.OnExportFailure += (endpoint, err) =>
            {
                var s = OtelExporterMod.Settings;
                s.LastExportSuccess = false;
                s.LastError = err;
            };
        }

        /// <summary>
        /// Fire a tiny one-off metrics POST on a background thread and report the HTTP result to
        /// the settings UI. Lets the player validate endpoint/auth/tenant before a run without
        /// waiting for (or misreading) the normal export cadence.
        /// </summary>
        public static void RunConnectionTest(OtelExporterSettings s)
        {
            s.TestRunning = true;
            s.TestStatus = "Testing…";

            var thread = new Thread(() =>
            {
                // Use a dedicated sender so we never mutate the shared export client's headers
                // while the background export thread might be posting.
                using var sender = new OtlpHttpSender();
                try
                {
                    sender.Configure(s.AuthHeader, s.OrgId);

                    var resource = OtlpSerializer.BuildResource(new ResourceAttributes
                    {
                        ModVersion = ModInfo.Version,
                        ColonyName = "connection-test",
                    });
                    long ts = (long)(DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc))
                        .TotalMilliseconds * 1_000_000L;
                    var metrics = new List<Metric>
                    {
                        OtlpSerializer.GaugeLong("rimworld_connection_test", 1, ts)
                    };
                    byte[] data = OtlpSerializer.SerializeMetrics(resource, "rimworld-telemetry", ModInfo.Version, metrics);

                    string url = (s.OtlpEndpoint?.TrimEnd('/') ?? "") + "/v1/metrics";
                    var result = sender.SendWithResult(url, data);
                    s.TestStatus = result.Ok
                        ? $"OK ({result.StatusCode}) → {url}"
                        : $"FAILED ({result.StatusCode}): {result.Detail}";
                }
                catch (Exception ex)
                {
                    s.TestStatus = "FAILED: " + ex.Message;
                }
                finally
                {
                    s.TestRunning = false;
                }
            })
            { IsBackground = true, Name = "OtelExporter.ConnTest" };
            thread.Start();
        }

        public static void Shutdown()
        {
            Queue?.Dispose();
            Sender?.Dispose();
            Queue = null;
            Sender = null;
        }
    }
}
