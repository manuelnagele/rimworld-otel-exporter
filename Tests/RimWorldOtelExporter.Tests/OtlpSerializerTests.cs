using System.Collections.Generic;
using NUnit.Framework;
using OpenTelemetry.Proto.Common.V1;
using OpenTelemetry.Proto.Logs.V1;
using OpenTelemetry.Proto.Metrics.V1;
using RimWorldOtelExporter.Transport;
using static RimWorldOtelExporter.Transport.OtlpSerializer;

namespace RimWorldOtelExporter.Tests
{
    [TestFixture]
    public class OtlpSerializerTests
    {
        private static readonly ResourceAttributes TestAttrs = new ResourceAttributes
        {
            ModVersion = "1.0.0",
            ColonyName = "New Icesheet",
            MapSeed = "abc123",
            StorytellerName = "Cassandra Classic",
            DifficultyLabel = "Rough"
        };

        // ── Resource ────────────────────────────────────────────────────────

        [Test]
        public void BuildResource_ContainsAllRequiredAttributes()
        {
            var resource = BuildResource(TestAttrs);

            AssertAttr(resource.Attributes, "service.name", "rimworld-colony");
            AssertAttr(resource.Attributes, "service.version", "1.0.0");
            AssertAttr(resource.Attributes, "colony.name", "New Icesheet");
            AssertAttr(resource.Attributes, "map.seed", "abc123");
            AssertAttr(resource.Attributes, "storyteller.name", "Cassandra Classic");
            AssertAttr(resource.Attributes, "difficulty.label", "Rough");
            AssertAttr(resource.Attributes, "mod.version", "1.0.0");
        }

        // ── Gauge double ─────────────────────────────────────────────────────

        [Test]
        public void GaugeDouble_SetsNameAndValue()
        {
            var m = GaugeDouble("rimworld_colonist_mood", 0.75, 1_000_000_000L, new[]
            {
                Attr("name", "Emilia"),
                Attr("pawn_id", "Thing_Human1")
            });

            Assert.AreEqual("rimworld_colonist_mood", m.Name);
            Assert.IsNotNull(m.Gauge);
            Assert.AreEqual(1, m.Gauge.DataPoints.Count);

            var dp = m.Gauge.DataPoints[0];
            Assert.AreEqual(0.75, dp.AsDouble, 0.0001);
            Assert.AreEqual(1_000_000_000UL, dp.TimeUnixNano);
        }

        [Test]
        public void GaugeDouble_AttachesAttributes()
        {
            var m = GaugeDouble("test_metric", 1.0, 0L, new[]
            {
                Attr("name", "Alice"),
                Attr("pawn_id", "id123")
            });

            var dp = m.Gauge.DataPoints[0];
            AssertAttr(dp.Attributes, "name", "Alice");
            AssertAttr(dp.Attributes, "pawn_id", "id123");
        }

        // ── Gauge long ──────────────────────────────────────────────────────

        [Test]
        public void GaugeLong_SetsIntValue()
        {
            var m = GaugeLong("rimworld_colonists_total", 12L, 0L, new[]
            {
                Attr("colonist_type", "free")
            });

            Assert.AreEqual(12L, m.Gauge.DataPoints[0].AsInt);
            AssertAttr(m.Gauge.DataPoints[0].Attributes, "colonist_type", "free");
        }

        // ── Metric serialization ─────────────────────────────────────────────

        [Test]
        public void SerializeMetrics_ProducesNonEmptyBytes()
        {
            var resource = BuildResource(TestAttrs);
            var metrics = new List<Metric>
            {
                GaugeDouble("rimworld_colonist_mood", 0.8, 0L),
                GaugeLong("rimworld_colonists_total", 5L, 0L, new[] { Attr("colonist_type", "free") })
            };

            byte[] bytes = SerializeMetrics(resource, "rimworld-telemetry", "1.0.0", metrics);

            Assert.IsNotNull(bytes);
            Assert.Greater(bytes.Length, 0);
        }

        [Test]
        public void SerializeMetrics_IsDeserializable()
        {
            var resource = BuildResource(TestAttrs);
            var metrics = new List<Metric>
            {
                GaugeDouble("rimworld_colonist_mood", 0.65, 1_000_000L),
                GaugeLong("rimworld_colonists_total", 8L, 1_000_000L, new[] { Attr("colonist_type", "free") }),
            };

            byte[] bytes = SerializeMetrics(resource, "rimworld-telemetry", "1.0.0", metrics);

            var request = OpenTelemetry.Proto.Collector.Metrics.V1.ExportMetricsServiceRequest.Parser.ParseFrom(bytes);
            Assert.AreEqual(1, request.ResourceMetrics.Count);

            var rm = request.ResourceMetrics[0];
            AssertAttr(rm.Resource.Attributes, "service.name", "rimworld-colony");
            AssertAttr(rm.Resource.Attributes, "colony.name", "New Icesheet");

            Assert.AreEqual(1, rm.ScopeMetrics.Count);
            Assert.AreEqual("rimworld-telemetry", rm.ScopeMetrics[0].Scope.Name);
            Assert.AreEqual(2, rm.ScopeMetrics[0].Metrics.Count);
        }

        // ── Log records ──────────────────────────────────────────────────────

        [Test]
        public void BuildLogRecord_SetsBodyAndSeverity()
        {
            var lr = BuildLogRecord("Test body", SeverityNumber.Warn, 12345L, new[]
            {
                Attr("event_type", "incident"),
                Attr("incident.def", "RaidEnemy")
            });

            Assert.AreEqual("Test body", lr.Body.StringValue);
            Assert.AreEqual(SeverityNumber.Warn, lr.SeverityNumber);
            Assert.AreEqual("WARN", lr.SeverityText);
            Assert.AreEqual(12345UL, lr.TimeUnixNano);
            AssertAttr(lr.Attributes, "event_type", "incident");
            AssertAttr(lr.Attributes, "incident.def", "RaidEnemy");
        }

        [Test]
        public void SerializeLogs_IsDeserializable()
        {
            var resource = BuildResource(TestAttrs);
            var logs = new List<LogRecord>
            {
                BuildLogRecord("Raid incoming!", SeverityNumber.Warn, 9999L, new[]
                {
                    Attr("event_type", "incident"),
                    Attr("incident.points", 350.0)
                })
            };

            byte[] bytes = SerializeLogs(resource, "rimworld-telemetry", "1.0.0", logs);
            var request = OpenTelemetry.Proto.Collector.Logs.V1.ExportLogsServiceRequest.Parser.ParseFrom(bytes);

            Assert.AreEqual(1, request.ResourceLogs.Count);
            var rl = request.ResourceLogs[0];
            Assert.AreEqual(1, rl.ScopeLogs[0].LogRecords.Count);
            Assert.AreEqual("Raid incoming!", rl.ScopeLogs[0].LogRecords[0].Body.StringValue);
        }

        // ── Attr helpers ─────────────────────────────────────────────────────

        [Test]
        public void Attr_StringValue()
        {
            var kv = Attr("key", "value");
            Assert.AreEqual("key", kv.Key);
            Assert.AreEqual("value", kv.Value.StringValue);
        }

        [Test]
        public void Attr_LongValue()
        {
            var kv = Attr("count", 42L);
            Assert.AreEqual(42L, kv.Value.IntValue);
        }

        [Test]
        public void Attr_DoubleValue()
        {
            var kv = Attr("ratio", 0.5);
            Assert.AreEqual(0.5, kv.Value.DoubleValue, 0.0001);
        }

        [Test]
        public void Attr_BoolValue()
        {
            Assert.IsTrue(Attr("flag", true).Value.BoolValue);
            Assert.IsFalse(Attr("flag", false).Value.BoolValue);
        }

        // ── Helper ───────────────────────────────────────────────────────────

        private static void AssertAttr(
            Google.Protobuf.Collections.RepeatedField<KeyValue> attrs,
            string key,
            string expectedValue)
        {
            foreach (var kv in attrs)
                if (kv.Key == key)
                {
                    Assert.AreEqual(expectedValue, kv.Value.StringValue, $"Attribute '{key}' value mismatch");
                    return;
                }
            Assert.Fail($"Attribute '{key}' not found in collection");
        }
    }
}
