using System.Collections.Generic;
using Google.Protobuf;
using OpenTelemetry.Proto.Collector.Metrics.V1;
using OpenTelemetry.Proto.Collector.Logs.V1;
using OpenTelemetry.Proto.Common.V1;
using OpenTelemetry.Proto.Logs.V1;
using OpenTelemetry.Proto.Metrics.V1;
using OpenTelemetry.Proto.Resource.V1;

namespace RimWorldOtelExporter.Transport
{
    /// <summary>
    /// Builds OTLP Protobuf request objects from raw metric/log data.
    /// </summary>
    public static class OtlpSerializer
    {
        public static byte[] SerializeMetrics(
            Resource resource,
            string scopeName,
            string scopeVersion,
            IEnumerable<Metric> metrics)
        {
            var request = new ExportMetricsServiceRequest();
            var rm = new ResourceMetrics { Resource = resource };
            var sm = new ScopeMetrics
            {
                Scope = new InstrumentationScope
                {
                    Name = scopeName,
                    Version = scopeVersion
                }
            };

            foreach (var m in metrics)
                sm.Metrics.Add(m);

            rm.ScopeMetrics.Add(sm);
            request.ResourceMetrics.Add(rm);
            return request.ToByteArray();
        }

        public static byte[] SerializeLogs(
            Resource resource,
            string scopeName,
            string scopeVersion,
            IEnumerable<LogRecord> logRecords)
        {
            var request = new ExportLogsServiceRequest();
            var rl = new ResourceLogs { Resource = resource };
            var sl = new ScopeLogs
            {
                Scope = new InstrumentationScope
                {
                    Name = scopeName,
                    Version = scopeVersion
                }
            };

            foreach (var lr in logRecords)
                sl.LogRecords.Add(lr);

            rl.ScopeLogs.Add(sl);
            request.ResourceLogs.Add(rl);
            return request.ToByteArray();
        }

        // ── Resource builder ─────────────────────────────────────────────────

        public static Resource BuildResource(ResourceAttributes attrs)
        {
            var resource = new Resource();
            resource.Attributes.Add(Attr("service.name", "rimworld-colony"));
            resource.Attributes.Add(Attr("service.version", attrs.ModVersion));
            resource.Attributes.Add(Attr("colony.name", attrs.ColonyName));
            resource.Attributes.Add(Attr("map.seed", attrs.MapSeed));
            resource.Attributes.Add(Attr("storyteller.name", attrs.StorytellerName));
            resource.Attributes.Add(Attr("difficulty.label", attrs.DifficultyLabel));
            resource.Attributes.Add(Attr("mod.version", attrs.ModVersion));
            return resource;
        }

        // ── Metric builders ──────────────────────────────────────────────────

        public static Metric GaugeDouble(
            string name,
            double value,
            long timestampNanos,
            IEnumerable<KeyValue>? attrs = null)
        {
            var dp = new NumberDataPoint
            {
                AsDouble = value,
                TimeUnixNano = (ulong)timestampNanos
            };
            if (attrs != null)
                foreach (var kv in attrs) dp.Attributes.Add(kv);

            var metric = new Metric { Name = name };
            metric.Gauge = new Gauge();
            metric.Gauge.DataPoints.Add(dp);
            return metric;
        }

        public static Metric GaugeLong(
            string name,
            long value,
            long timestampNanos,
            IEnumerable<KeyValue>? attrs = null)
        {
            var dp = new NumberDataPoint
            {
                AsInt = value,
                TimeUnixNano = (ulong)timestampNanos
            };
            if (attrs != null)
                foreach (var kv in attrs) dp.Attributes.Add(kv);

            var metric = new Metric { Name = name };
            metric.Gauge = new Gauge();
            metric.Gauge.DataPoints.Add(dp);
            return metric;
        }

        // ── Log record builder ───────────────────────────────────────────────

        public static LogRecord BuildLogRecord(
            string body,
            SeverityNumber severity,
            long timestampNanos,
            IEnumerable<KeyValue>? attrs = null)
        {
            var lr = new LogRecord
            {
                Body = new AnyValue { StringValue = body },
                SeverityNumber = severity,
                SeverityText = severity >= SeverityNumber.Warn ? "WARN" : "INFO",
                TimeUnixNano = (ulong)timestampNanos
            };
            if (attrs != null)
                foreach (var kv in attrs) lr.Attributes.Add(kv);
            return lr;
        }

        // ── KeyValue helpers ─────────────────────────────────────────────────

        public static KeyValue Attr(string key, string value) =>
            new KeyValue { Key = key, Value = new AnyValue { StringValue = value ?? "" } };

        public static KeyValue Attr(string key, long value) =>
            new KeyValue { Key = key, Value = new AnyValue { IntValue = value } };

        public static KeyValue Attr(string key, double value) =>
            new KeyValue { Key = key, Value = new AnyValue { DoubleValue = value } };

        public static KeyValue Attr(string key, bool value) =>
            new KeyValue { Key = key, Value = new AnyValue { BoolValue = value } };
    }

    public class ResourceAttributes
    {
        public string ModVersion = "0.0.0";
        public string ColonyName = "Unknown";
        public string MapSeed = "0";
        public string StorytellerName = "Unknown";
        public string DifficultyLabel = "Unknown";
    }
}
