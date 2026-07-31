using System;
using System.Collections.Generic;
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

        // Stable per-save identity, persisted with the save. Distinguishes campaigns in the TSDB.
        private string? _campaignId;

        public ColonyTelemetryComponent(Game game) { }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref _campaignId, "otelCampaignId", null);
        }

        /// <summary>Lazily creates and persists a per-save GUID the first time it is needed.</summary>
        private string CampaignId
        {
            get
            {
                if (string.IsNullOrEmpty(_campaignId))
                    _campaignId = Guid.NewGuid().ToString("N");
                return _campaignId!;
            }
        }

        /// <summary>
        /// Runs every frame, INCLUDING while the game is paused — so the dashboard keeps updating
        /// during raids, mental breaks and deaths (which usually happen while paused). Cadence is
        /// still wall-clock gated to ExportIntervalSeconds via realtimeSinceStartup.
        /// (GameComponentTick stops firing when the game is paused, which froze the live view.)
        /// </summary>
        public override void GameComponentUpdate()
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
                // Rebuild resource once per map load (colony name/seed may change on new game).
                if (_cachedResource == null)
                    _cachedResource = BuildResource();
                var resource = _cachedResource; // non-null local

                long ts = DateTimeToUnixNanos(DateTime.UtcNow);
                string colonyLabel = RimWorld.Faction.OfPlayer?.Name ?? "Unknown";

                // A readable-yet-unique per-run label so the dashboard can single-select ONE run and
                // stay clean even if two colonies share a name (the short campaign id disambiguates).
                string shortId = CampaignId.Length >= 6 ? CampaignId.Substring(0, 6) : CampaignId;
                string runLabel = $"{colonyLabel} #{shortId}";

                // Low-cardinality identity labels attached to every datapoint/log so campaigns
                // are filterable in PromQL/LogQL (resource attrs alone are not per-series labels).
                var common = new[]
                {
                    OtlpSerializer.Attr("campaign_id", CampaignId),
                    OtlpSerializer.Attr("colony_name", colonyLabel),
                    OtlpSerializer.Attr("run", runLabel),
                };

                var metrics = new List<Metric>();

                if (settings.EnableColonists)
                    ColonistCollector.Collect(metrics, ts);

                if (settings.EnableResources)
                    ResourceCollector.Collect(metrics, ts);

                if (settings.EnableInfrastructure)
                    InfrastructureCollector.Collect(metrics, ts);

                if (settings.EnableWorld)
                    WorldCollector.Collect(metrics, ts);

                // Derived at-a-glance score — cheap, always emitted when any category is on.
                ColonyScoreCollector.Collect(metrics, ts);

                // Metrics batch
                if (metrics.Count > 0)
                {
                    OtlpSerializer.AddCommonAttributes(metrics, common);
                    byte[] metricBytes = OtlpSerializer.SerializeMetrics(
                        resource, ScopeName, ModInfo.Version, metrics);
                    OtelExporterCore.Queue?.Enqueue(Join(settings.OtlpEndpoint, "/v1/metrics"), metricBytes);
                }

                // Logs batch (queued log records from Harmony patches)
                if (LogBuffer.Drain(out var pending) && pending.Count > 0)
                {
                    OtlpSerializer.AddCommonAttributes(pending, common);
                    byte[] logBytes = OtlpSerializer.SerializeLogs(
                        resource, ScopeName, ModInfo.Version, pending);
                    OtelExporterCore.Queue?.Enqueue(Join(settings.OtlpEndpoint, "/v1/logs"), logBytes);
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

        private OpenTelemetry.Proto.Resource.V1.Resource BuildResource()
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
                difficulty = Find.Storyteller?.difficultyDef?.label ?? "Unknown";
            }
            catch { /* may not be fully loaded yet */ }

            return OtlpSerializer.BuildResource(new ResourceAttributes
            {
                ModVersion = ModInfo.Version,
                ColonyName = colonyName,
                MapSeed = seed,
                StorytellerName = storyteller,
                DifficultyLabel = difficulty,
                CampaignId = CampaignId,
                InstanceId = CampaignId, // reuse the campaign GUID as the OTLP service.instance.id
            });
        }

        /// <summary>Join an endpoint base with an OTLP path, tolerating a trailing slash.</summary>
        private static string Join(string endpoint, string path) =>
            (endpoint?.TrimEnd('/') ?? "") + path;

        private static long DateTimeToUnixNanos(DateTime dt) =>
            (long)(dt - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalMilliseconds * 1_000_000L;
    }
}
