namespace RimWorldOtelExporter
{
    /// <summary>
    /// Single source of truth for the mod version. Used for OTLP service.version / mod.version
    /// so telemetry never reports "0.0.0" (RimWorld does not populate ModMetaData.ModVersion
    /// from About.xml by default). Bump this on every release.
    /// </summary>
    public static class ModInfo
    {
        public const string Version = "0.3.0-rc1";
    }
}
