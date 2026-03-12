using System;
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

            listing.Label("OTLP Endpoint");
            Settings.OtlpEndpoint = listing.TextEntry(Settings.OtlpEndpoint);

            listing.Label("Authorization Header (e.g. Bearer glc_eyJ...)");
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
            listing.Label("Export status:");

            if (Settings.LastExportTime == DateTime.MinValue)
            {
                listing.Label("  No export yet.");
            }
            else
            {
                double ago = (DateTime.UtcNow - Settings.LastExportTime).TotalSeconds;
                string status = Settings.LastExportSuccess
                    ? $"  Last export: {ago:F0}s ago  ({Settings.LastPayloadBytes} bytes)"
                    : $"  FAILED: {Settings.LastError}";
                listing.Label(status);
            }

            listing.End();

            if (GUI.changed)
            {
                OtelExporterCore.Sender?.Configure(Settings.AuthHeader, Settings.OrgId);
                OtelExporterCore.Queue?.ResetCircuitBreaker();
            }
        }

        public override string SettingsCategory() => "RimWorld OTel Exporter";
    }
}
