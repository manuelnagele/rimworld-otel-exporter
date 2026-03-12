using System;
using System.Collections.Generic;
using Google.Protobuf.Collections;
using OpenTelemetry.Proto.Metrics.V1;
using RimWorld;
using RimWorldOtelExporter.Transport;
using Verse;
using static RimWorldOtelExporter.Transport.OtlpSerializer;

namespace RimWorldOtelExporter.Collectors
{
    public static class ColonistCollector
    {
        public static void Collect(List<Metric> metrics, long timestampNanos)
        {
            var map = Find.CurrentMap;
            if (map == null) return;

            // rimworld_colonists_total by colonist_type
            int free = 0, prisoner = 0, slave = 0, guest = 0;
            foreach (var pawn in PawnsFinder.AllMapsCaravansAndTravelingTransportPods_Alive_FreeColonists)
                free++;
            foreach (var pawn in map.mapPawns.PrisonersOfColony)
                prisoner++;
            try { foreach (var pawn in map.mapPawns.SlavesOfColonySpawned) slave++; } catch { }
            foreach (var pawn in map.mapPawns.AnyColonyHumanlike)
            {
                if (pawn.guest?.IsGuest == true) guest++;
            }

            metrics.Add(GaugeLong("rimworld_colonists_total", free, timestampNanos, new[] { Attr("colonist_type", "free") }));
            metrics.Add(GaugeLong("rimworld_colonists_total", prisoner, timestampNanos, new[] { Attr("colonist_type", "prisoner") }));
            metrics.Add(GaugeLong("rimworld_colonists_total", slave, timestampNanos, new[] { Attr("colonist_type", "slave") }));
            metrics.Add(GaugeLong("rimworld_colonists_total", guest, timestampNanos, new[] { Attr("colonist_type", "guest") }));

            // Per-colonist metrics
            var colonists = map.mapPawns.FreeColonists;

            // Emit mood break thresholds once (they're constant per colonist but we emit colony-wide averages)
            float minorThreshold = 0f, majorThreshold = 0f, extremeThreshold = 0f;
            int threshCount = 0;

            foreach (var pawn in colonists)
            {
                if (pawn?.needs?.mood == null) continue;

                string name = pawn.LabelShort ?? pawn.ThingID;
                string pawnId = pawn.ThingID;

                var pawnAttrs = new[] { Attr("name", name), Attr("pawn_id", pawnId) };

                // rimworld_colonist_mood
                metrics.Add(GaugeDouble("rimworld_colonist_mood", pawn.needs.mood.CurLevel, timestampNanos, pawnAttrs));

                // rimworld_colonist_health
                if (pawn.health?.summaryHealth != null)
                    metrics.Add(GaugeDouble("rimworld_colonist_health", pawn.health.summaryHealth.SummaryHealthPercent, timestampNanos, pawnAttrs));

                // rimworld_colonist_pain
                if (pawn.health?.hediffSet != null)
                    metrics.Add(GaugeDouble("rimworld_colonist_pain", pawn.health.hediffSet.PainTotal, timestampNanos, pawnAttrs));

                // rimworld_colonist_hediff_count by category
                CollectHediffs(metrics, pawn, name, pawnId, timestampNanos);

                // rimworld_colonist_skill
                if (pawn.skills?.skills != null)
                {
                    foreach (var skill in pawn.skills.skills)
                    {
                        metrics.Add(GaugeLong("rimworld_colonist_skill", skill.Level, timestampNanos, new[]
                        {
                            Attr("name", name),
                            Attr("pawn_id", pawnId),
                            Attr("skill", skill.def.defName),
                            Attr("passion", (int)skill.passion)
                        }));
                    }
                }

                // rimworld_colonist_need
                if (pawn.needs?.AllNeeds != null)
                {
                    foreach (var need in pawn.needs.AllNeeds)
                    {
                        if (!need.ShowOnNeedList) continue;
                        metrics.Add(GaugeDouble("rimworld_colonist_need", need.CurLevel, timestampNanos, new[]
                        {
                            Attr("name", name),
                            Attr("pawn_id", pawnId),
                            Attr("need", need.def.defName)
                        }));
                    }
                }

                // rimworld_colonist_thoughts_negative_total
                try
                {
                    int negThoughts = 0;
                    var memories = pawn.needs?.mood?.thoughts?.memories?.Memories;
                    if (memories != null)
                        foreach (var m in memories)
                            if (m.MoodOffset() < 0) negThoughts++;
                    metrics.Add(GaugeLong("rimworld_colonist_thoughts_negative_total", negThoughts, timestampNanos, new[]
                    {
                        Attr("name", name),
                        Attr("pawn_id", pawnId)
                    }));
                }
                catch { }

                // Accumulate break thresholds for colony-wide constants
                try
                {
                    minorThreshold += pawn.mindState.mentalBreaker.BreakThresholdMinor;
                    majorThreshold += pawn.mindState.mentalBreaker.BreakThresholdMajor;
                    extremeThreshold += pawn.mindState.mentalBreaker.BreakThresholdExtreme;
                    threshCount++;
                }
                catch { }
            }

            // Emit break thresholds as colony averages (for Grafana reference lines)
            if (threshCount > 0)
            {
                metrics.Add(GaugeDouble("rimworld_colonist_mood_break_threshold_minor", minorThreshold / threshCount, timestampNanos));
                metrics.Add(GaugeDouble("rimworld_colonist_mood_break_threshold_major", majorThreshold / threshCount, timestampNanos));
                metrics.Add(GaugeDouble("rimworld_colonist_mood_break_threshold_extreme", extremeThreshold / threshCount, timestampNanos));
            }
        }

        private static void CollectHediffs(List<Metric> metrics, Pawn pawn, string name, string pawnId, long ts)
        {
            if (pawn.health?.hediffSet?.hediffs == null) return;

            int injury = 0, disease = 0, addiction = 0, implant = 0, chronic = 0;

            foreach (var hediff in pawn.health.hediffSet.hediffs)
            {
                if (hediff is Hediff_Injury) injury++;
                else if (hediff is Hediff_Addiction) addiction++;
                else if (hediff is Hediff_AddedPart || hediff is Hediff_ImplantWithLevel) implant++;
                else if (hediff.def?.chronic == true) chronic++;
                else if (hediff.Visible && hediff.def?.makesSickThought == true) disease++;
            }

            var baseAttrs = new[] { Attr("name", name), Attr("pawn_id", pawnId) };

            metrics.Add(GaugeLong("rimworld_colonist_hediff_count", injury, ts, new[] { Attr("name", name), Attr("pawn_id", pawnId), Attr("category", "Injury") }));
            metrics.Add(GaugeLong("rimworld_colonist_hediff_count", disease, ts, new[] { Attr("name", name), Attr("pawn_id", pawnId), Attr("category", "Disease") }));
            metrics.Add(GaugeLong("rimworld_colonist_hediff_count", addiction, ts, new[] { Attr("name", name), Attr("pawn_id", pawnId), Attr("category", "Addiction") }));
            metrics.Add(GaugeLong("rimworld_colonist_hediff_count", implant, ts, new[] { Attr("name", name), Attr("pawn_id", pawnId), Attr("category", "Implant") }));
            metrics.Add(GaugeLong("rimworld_colonist_hediff_count", chronic, ts, new[] { Attr("name", name), Attr("pawn_id", pawnId), Attr("category", "Chronic") }));
        }
    }
}
