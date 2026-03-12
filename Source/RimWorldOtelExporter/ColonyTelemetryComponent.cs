using System;
using System.Collections.Generic;
using Google.Protobuf.WellKnownTypes;
using OpenTelemetry.Proto.Metrics.V1;
using OpenTelemetry.Proto.Logs.V1;
using RimWorldOtelExporter.Collectors;
using RimWorldOtelExporter.Transport;
using Verse;

namespace RimWorldOtelExporter
{
    /// <summary>
    /// GameComponent that acts as the collection heartbeat.
    /// Survives map transitions, so it is more reliable than MapComponent for continuous export.
    /// </summary>
    public class ColonyTelemetryComponent : GameComponent
    {
        private const string ScopeName = "rimworld-telemetry";

        private float _lastExportRealtime = -999f;
        private OpenTelemetry.Proto.Resource.V1.Resource? _cachedResource;

        public ColonyTelemetryComponent(Game game) { }

        public override void GameComponentTick()
        {
            var settings = OtelExporterMod.Settings;
            float now = UnityEngine.Time.realtimeSinceStartup;

            if (now - _lastExportRealtime < settings.ExportIntervalSeconds) return;
            _lastExportRealtime = now;

            if (OtelExporterCore.Queue == null) return;

            TryExport(settings);
        }

        private void TryExport(OtelExporterSettings settings)
        {
            try
            {
                // Rebuild resource once per map load (colony name may change on new game)
                if (_cachedResource == null)
                    _cachedResource = BuildResource(settings);

                long ts = DateTimeToUnixNanos(DateTime.UtcNow);
                string modVersion = OtelExporterMod.Instance.Content.ModMetaData.ModVersion?.ToString() ?? "0.0.0";

                var metrics = new List<Metric>();
                var logs = new List<LogRecord>();

                if (settings.EnableColonists)
                    ColonistCollector.Collect(metrics, ts);

                if (settings.EnableResources)
                    ResourceCollector.Collect(metrics, ts);

                if (settings.EnableInfrastructure)
                    InfrastructureCollector.Collect(metrics, ts);

                if (settings.EnableWorld)
                    WorldCollector.Collect(metrics, ts);

                // Metrics batch
                if (metrics.Count > 0)
                {
                    byte[] metricBytes = OtlpSerializer.SerializeMetrics(
                        _cachedResource, ScopeName, modVersion, metrics);
                    OtelExporterCore.Queue.Enqueue(settings.OtlpEndpoint + "/v1/metrics", metricBytes);
                }

                // Logs batch (queued log records from Harmony patches)
                if (LogBuffer.Drain(out var pending) && pending.Count > 0)
                {
                    byte[] logBytes = OtlpSerializer.SerializeLogs(
                        _cachedResource, ScopeName, modVersion, pending);
                    OtelExporterCore.Queue.Enqueue(settings.OtlpEndpoint + "/v1/logs", logBytes);
                }
            }
            catch (Exception ex)
            {
                Log.Warning($"[OtelExporter] Export cycle error: {ex.Message}");
            }
        }

        public override void StartedNewGame()
        {
            _cachedResource = null; // force rebuild with new colony name/seed
            LogBuffer.EnqueueLifecycle();
        }

        public override void LoadedGame()
        {
            _cachedResource = null;
            LogBuffer.EnqueueLifecycle();
        }

        private static OpenTelemetry.Proto.Resource.V1.Resource BuildResource(OtelExporterSettings _)
        {
            string colonyName = "";
            string seed = "";
            string storyteller = "";
            string difficulty = "";

            try
            {
                colonyName = RimWorld.Faction.OfPlayer?.Name ?? "Unknown";
                seed = Find.World?.info?.seedString ?? "0";
                storyteller = Find.Storyteller?.def?.label ?? "Unknown";
                difficulty = Find.Storyteller?.difficulty?.label ?? "Unknown";
            }
            catch { /* may not be fully loaded yet */ }

            string modVersion = OtelExporterMod.Instance.Content.ModMetaData.ModVersion?.ToString() ?? "0.0.0";

            return OtlpSerializer.BuildResource(new ResourceAttributes
            {
                ModVersion = modVersion,
                ColonyName = colonyName,
                MapSeed = seed,
                StorytellerName = storyteller,
                DifficultyLabel = difficulty
            });
        }

        private static long DateTimeToUnixNanos(DateTime dt) =>
            (long)(dt - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalMilliseconds * 1_000_000L;
    }
}
