using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using OpenTelemetry.Proto.Logs.V1;
using RimWorldOtelExporter.Transport;
using Verse;

namespace RimWorldOtelExporter
{
    /// <summary>
    /// Thread-safe buffer for log records emitted by Harmony patches.
    /// Harmony patches run on the game thread; the export cycle drains this buffer.
    /// </summary>
    public static class LogBuffer
    {
        private static readonly ConcurrentQueue<LogRecord> _queue = new ConcurrentQueue<LogRecord>();

        public static void Enqueue(LogRecord record) => _queue.Enqueue(record);

        public static bool Drain(out List<LogRecord> records)
        {
            records = new List<LogRecord>();
            while (_queue.TryDequeue(out var r))
                records.Add(r);
            return records.Count > 0;
        }

        public static void EnqueueLifecycle()
        {
            try
            {
                string gameVersion = VersionControl.CurrentVersionString ?? "unknown";
                string modVersion = OtelExporterMod.Instance.Content.ModMetaData.ModVersion?.ToString() ?? "0.0.0";
                string scenario = Find.Scenario?.name ?? "unknown";
                string seed = Find.World?.info?.seedString ?? "0";
                string storyteller = Find.Storyteller?.def?.label ?? "unknown";

                var attrs = new[]
                {
                    OtlpSerializer.Attr("event_type", "lifecycle"),
                    OtlpSerializer.Attr("game.version", gameVersion),
                    OtlpSerializer.Attr("mod.version", modVersion),
                    OtlpSerializer.Attr("scenario.name", scenario),
                    OtlpSerializer.Attr("map.seed", seed),
                    OtlpSerializer.Attr("storyteller.name", storyteller),
                };

                var record = OtlpSerializer.BuildLogRecord(
                    $"Session start — {scenario} / {storyteller}",
                    SeverityNumber.Info,
                    DateTimeToNanos(DateTime.UtcNow),
                    attrs);

                Enqueue(record);
            }
            catch (Exception ex)
            {
                Log.Warning($"[OtelExporter] Failed to emit lifecycle log: {ex.Message}");
            }
        }

        private static long DateTimeToNanos(DateTime dt) =>
            (long)(dt - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalMilliseconds * 1_000_000L;
    }
}
