using System;
using RimWorld;
using UnityEngine;
using Verse;
using RimWorldOtelExporter.Transport;

namespace RimWorldOtelExporter
{
    public class OtelExporterSettings : ModSettings
    {
        public string OtlpEndpoint = "http://localhost:4318";
        public string AuthHeader = "";
        public string OrgId = "anonymous";
        public int ExportIntervalSeconds = 15;

        public bool EnableColonists = true;
        public bool EnableResources = true;
        public bool EnableInfrastructure = true;
        public bool EnableEvents = true;
        public bool EnableWorld = true;

        // Status (not persisted, runtime only)
        [Unsaved] public DateTime LastExportTime = DateTime.MinValue;
        [Unsaved] public bool LastExportSuccess = false;
        [Unsaved] public int LastPayloadBytes = 0;
        [Unsaved] public string LastError = "";
        [Unsaved] public string TestStatus = "";
        [Unsaved] public bool TestRunning = false;

        public override void ExposeData()
        {
            Scribe_Values.Look(ref OtlpEndpoint, "otlpEndpoint", "http://localhost:4318");
            Scribe_Values.Look(ref AuthHeader, "authHeader", "");
            Scribe_Values.Look(ref OrgId, "orgId", "anonymous");
            Scribe_Values.Look(ref ExportIntervalSeconds, "exportIntervalSeconds", 15);
            Scribe_Values.Look(ref EnableColonists, "enableColonists", true);
            Scribe_Values.Look(ref EnableResources, "enableResources", true);
            Scribe_Values.Look(ref EnableInfrastructure, "enableInfrastructure", true);
            Scribe_Values.Look(ref EnableEvents, "enableEvents", true);
            Scribe_Values.Look(ref EnableWorld, "enableWorld", true);
            base.ExposeData();
        }
    }

    public class OtelExporterMod : Mod
    {
        public static OtelExporterSettings Settings = null!;
        public static OtelExporterMod Instance = null!;

        public OtelExporterMod(ModContentPack content) : base(content)
        {
            Instance = this;
            Settings = GetSettings<OtelExporterSettings>();
            OtelExporterCore.Init();
        }

        public override void DoSettingsWindowContents(Rect inRect)
        {
            var listing = new Listing_Standard();
            listing.Begin(inRect);

            listing.Label($"RimWorld OTel Exporter  v{ModInfo.Version}");
            listing.GapLine();

            listing.Label("OTLP Endpoint (base URL, e.g. http://localhost:4318)");
            Settings.OtlpEndpoint = listing.TextEntry(Settings.OtlpEndpoint);

            listing.Label("Authorization Header (e.g. Bearer glc_eyJ...) — leave blank for a local Alloy relay");
            Settings.AuthHeader = listing.TextEntry(Settings.AuthHeader);

            listing.Label("Org ID (X-Scope-OrgID)");
            Settings.OrgId = listing.TextEntry(Settings.OrgId);

            listing.Label($"Export Interval (seconds): {Settings.ExportIntervalSeconds}");
            Settings.ExportIntervalSeconds = (int)listing.Slider(Settings.ExportIntervalSeconds, 5, 120);

            listing.GapLine();
            listing.Label("Enable categories:");
            listing.CheckboxLabeled("Colonists", ref Settings.EnableColonists);
            listing.CheckboxLabeled("Resources & Economy", ref Settings.EnableResources);
            listing.CheckboxLabeled("Infrastructure", ref Settings.EnableInfrastructure);
            listing.CheckboxLabeled("Events (Harmony patches)", ref Settings.EnableEvents);
            listing.CheckboxLabeled("World & Threats", ref Settings.EnableWorld);

            listing.GapLine();

            // Test connection — validates endpoint/auth/tenant without waiting for the export cycle.
            if (Settings.TestRunning)
            {
                listing.Label("  Testing connection…");
            }
            else if (listing.ButtonText("Test connection"))
            {
                // Persist current field values first so the test uses what's on screen.
                Settings.Write();
                OtelExporterCore.RunConnectionTest(Settings);
            }
            if (!string.IsNullOrEmpty(Settings.TestStatus))
                listing.Label("  " + Settings.TestStatus);

            listing.GapLine();
            listing.Label("Export status:");
            DrawStatus(listing);

            listing.End();
        }

        private void DrawStatus(Listing_Standard listing)
        {
            if (Settings.LastExportTime == DateTime.MinValue && string.IsNullOrEmpty(Settings.LastError))
            {
                listing.Label("  No export yet.");
            }
            else if (Settings.LastExportSuccess)
            {
                double ago = (DateTime.UtcNow - Settings.LastExportTime).TotalSeconds;
                listing.Label($"  OK — last export {ago:F0}s ago ({Settings.LastPayloadBytes} bytes)");
            }
            else
            {
                listing.Label($"  FAILED: {Settings.LastError}");
            }

            var q = OtelExporterCore.Queue;
            if (q != null)
            {
                if (q.IsOffline)
                {
                    double wait = (q.NextRetryUtc - DateTime.UtcNow).TotalSeconds;
                    listing.Label($"  Paused after {q.ConsecutiveFailures} failures — auto-retry in {Math.Max(0, wait):F0}s (or close this window to retry now).");
                }
                else if (q.ConsecutiveFailures > 0)
                {
                    listing.Label($"  Retrying — {q.ConsecutiveFailures} consecutive failure(s).");
                }
                if (q.QueueDepth > 0)
                    listing.Label($"  Pending payloads in queue: {q.QueueDepth}");
            }
        }

        /// <summary>
        /// Called when the settings window closes (Accept). Re-apply auth/tenant headers and clear
        /// the circuit breaker here rather than every draw frame (the old per-frame reconfigure
        /// raced the export thread).
        /// </summary>
        public override void WriteSettings()
        {
            base.WriteSettings();
            OtelExporterCore.Sender?.Configure(Settings.AuthHeader, Settings.OrgId);
            OtelExporterCore.Queue?.ResetCircuitBreaker();
        }

        public override string SettingsCategory() => "RimWorld OTel Exporter";
    }
}
