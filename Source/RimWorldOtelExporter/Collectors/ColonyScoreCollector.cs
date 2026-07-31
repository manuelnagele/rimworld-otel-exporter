using System;
using System.Collections.Generic;
using OpenTelemetry.Proto.Metrics.V1;
using RimWorldOtelExporter.Transport;
using static RimWorldOtelExporter.Transport.OtlpSerializer;

namespace RimWorldOtelExporter.Collectors
{
    /// <summary>
    /// Derives a single 0–100 "is my colony OK right now" score plus its component sub-scores.
    /// Pure function of metrics already collected this cycle — no extra game-state reads — so it
    /// must run AFTER the other collectors have populated the list.
    /// </summary>
    public static class ColonyScoreCollector
    {
        public static void Collect(List<Metric> metrics, long ts)
        {
            // Nothing to score before colonist metrics exist (e.g. category disabled / no map).
            if (!Has(metrics, "rimworld_colonist_mood") && !Has(metrics, "rimworld_food_days_remaining"))
                return;

            try
            {
                float colonists = Math.Max(1f, (float)First(metrics, "rimworld_colonists_total", 1));
                float foodDays = (float)First(metrics, "rimworld_food_days_remaining", 999);
                float avgMood = (float)Avg(metrics, "rimworld_colonist_mood", 0.5);
                float breakMinor = (float)First(metrics, "rimworld_colonist_mood_break_threshold_minor", 0.35);
                float downed = (float)First(metrics, "rimworld_colonist_downed_total", 0);
                float hostile = (float)First(metrics, "rimworld_hostile_pawns_on_map", 0);
                float combatReady = Math.Max(1f, (float)First(metrics, "rimworld_colonists_combat_ready_total", colonists));
                float fire = (float)First(metrics, "rimworld_fire_count", 0);
                float bleeding = (float)First(metrics, "rimworld_colonists_bleeding_total", 0);
                float losingImmunity = (float)First(metrics, "rimworld_colonists_losing_immunity_total", 0);

                float foodScore = Clamp01(foodDays / 6f);
                float moodScore = Clamp01((avgMood - breakMinor) / 0.3f);
                float safetyScore = Clamp01(
                    1f
                    - 0.7f * Clamp01(hostile / combatReady)
                    - 0.5f * Clamp01(downed / colonists)
                    - Math.Min(0.3f, fire * 0.1f));
                float healthScore = Clamp01(1f - (bleeding + losingImmunity) / colonists);

                float overall = 100f * (0.25f * foodScore + 0.25f * moodScore + 0.30f * safetyScore + 0.20f * healthScore);

                metrics.Add(GaugeDouble("rimworld_colony_health_score", Math.Round(overall, 1), ts));
                metrics.Add(GaugeDouble("rimworld_colony_health_score_component", Math.Round(foodScore * 100f, 1), ts, new[] { Attr("component", "food") }));
                metrics.Add(GaugeDouble("rimworld_colony_health_score_component", Math.Round(moodScore * 100f, 1), ts, new[] { Attr("component", "mood") }));
                metrics.Add(GaugeDouble("rimworld_colony_health_score_component", Math.Round(safetyScore * 100f, 1), ts, new[] { Attr("component", "safety") }));
                metrics.Add(GaugeDouble("rimworld_colony_health_score_component", Math.Round(healthScore * 100f, 1), ts, new[] { Attr("component", "health") }));
            }
            catch { }
        }

        private static bool Has(List<Metric> metrics, string name)
        {
            foreach (var m in metrics) if (m.Name == name) return true;
            return false;
        }

        private static double First(List<Metric> metrics, string name, double fallback)
        {
            foreach (var m in metrics)
                if (m.Name == name && m.Gauge != null && m.Gauge.DataPoints.Count > 0)
                    return Value(m.Gauge.DataPoints[0]);
            return fallback;
        }

        private static double Avg(List<Metric> metrics, string name, double fallback)
        {
            double sum = 0; int n = 0;
            foreach (var m in metrics)
                if (m.Name == name && m.Gauge != null)
                    foreach (var dp in m.Gauge.DataPoints) { sum += Value(dp); n++; }
            return n > 0 ? sum / n : fallback;
        }

        private static double Value(NumberDataPoint dp) =>
            dp.ValueCase == NumberDataPoint.ValueOneofCase.AsInt ? dp.AsInt : dp.AsDouble;

        private static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);
    }
}
