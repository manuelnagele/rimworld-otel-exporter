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
                s.LastExportTime = System.DateTime.UtcNow;
                s.LastExportSuccess = true;
                s.LastPayloadBytes = bytes;
                s.LastError = "";
            };
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
