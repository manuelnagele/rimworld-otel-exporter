using System;
using System.Collections.Generic;
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

            // rimworld_colonists_total by colonist_type — scoped to the current map so it
            // reconciles with the per-colonist series (which are current-map only).
            int free = map.mapPawns.FreeColonists?.Count ?? 0;
            int prisoner = 0, slave = 0, guest = 0;
            foreach (var pawn in map.mapPawns.PrisonersOfColony)
                prisoner++;
            try { foreach (var pawn in map.mapPawns.SlavesOfColonySpawned) slave++; } catch { }
            foreach (var pawn in map.mapPawns.AllHumanlikeSpawned)
            {
                if (pawn.guest?.GuestStatus == GuestStatus.Guest) guest++;
            }

            metrics.Add(GaugeLong("rimworld_colonists_total", free, timestampNanos, new[] { Attr("colonist_type", "free") }));
            metrics.Add(GaugeLong("rimworld_colonists_total", prisoner, timestampNanos, new[] { Attr("colonist_type", "prisoner") }));
            metrics.Add(GaugeLong("rimworld_colonists_total", slave, timestampNanos, new[] { Attr("colonist_type", "slave") }));
            metrics.Add(GaugeLong("rimworld_colonists_total", guest, timestampNanos, new[] { Attr("colonist_type", "guest") }));

            var colonists = map.mapPawns.FreeColonists ?? new List<Pawn>();

            // Colony-wide accumulators emitted after the loop (low-cardinality "act now" signals).
            float minorThreshold = 0f, majorThreshold = 0f, extremeThreshold = 0f;
            int threshCount = 0;
            int nearBreakMinor = 0, nearBreakMajor = 0;
            int bleeding = 0, tendNeeded = 0, losingImmunity = 0;
            float bleedRateSum = 0f;
            int hungry = 0, urgentlyHungry = 0, starving = 0;

            foreach (var pawn in colonists)
            {
                if (pawn?.needs?.mood == null) continue;

                string name = pawn.LabelShort ?? pawn.ThingID;
                string pawnId = pawn.ThingID;

                var pawnAttrs = new[] { Attr("name", name), Attr("pawn_id", pawnId) };

                // rimworld_colonist_age_years
                if (pawn.ageTracker != null)
                    metrics.Add(GaugeLong("rimworld_colonist_age_years", pawn.ageTracker.AgeBiologicalYears, timestampNanos, pawnAttrs));

                // rimworld_colonist_mood
                float mood = pawn.needs.mood.CurLevel;
                metrics.Add(GaugeDouble("rimworld_colonist_mood", mood, timestampNanos, pawnAttrs));

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
                        if (skill?.def == null) continue; // modded skill records can be malformed
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
                        if (need?.def == null || !need.ShowOnNeedList) continue;
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
                    metrics.Add(GaugeLong("rimworld_colonist_thoughts_negative_total", negThoughts, timestampNanos, pawnAttrs));
                }
                catch { }

                // Mood vs the pawn's OWN minor break threshold — crosses 0 at the danger point.
                try
                {
                    var breaker = pawn.mindState.mentalBreaker;
                    float minor = breaker.BreakThresholdMinor;
                    float major = breaker.BreakThresholdMajor;
                    metrics.Add(GaugeDouble("rimworld_colonist_mood_margin", mood - minor, timestampNanos, pawnAttrs));

                    minorThreshold += minor;
                    majorThreshold += major;
                    extremeThreshold += breaker.BreakThresholdExtreme;
                    threshCount++;

                    if (mood <= minor + 0.05f) nearBreakMinor++;
                    if (mood <= major + 0.03f) nearBreakMajor++;
                }
                catch { }

                // Life-or-death health signals (post-raid / plague).
                try
                {
                    if (pawn.health?.hediffSet != null)
                    {
                        float bleed = pawn.health.hediffSet.BleedRateTotal;
                        if (bleed > 0f) { bleeding++; bleedRateSum += bleed; }
                    }
                    if (HealthAIUtility.ShouldBeTendedNowByPlayer(pawn)) tendNeeded++;
                    if (IsLosingImmunityRace(pawn)) losingImmunity++;
                }
                catch { }

                // Hunger state — a pawn can be Starving despite a full freezer (hauling/cook stall).
                try
                {
                    switch (pawn.needs?.food?.CurCategory)
                    {
                        case HungerCategory.Hungry: hungry++; break;
                        case HungerCategory.UrgentlyHungry: urgentlyHungry++; break;
                        case HungerCategory.Starving: starving++; break;
                    }
                }
                catch { }
            }

            // Colony-wide break thresholds (Grafana reference lines).
            if (threshCount > 0)
            {
                metrics.Add(GaugeDouble("rimworld_colonist_mood_break_threshold_minor", minorThreshold / threshCount, timestampNanos));
                metrics.Add(GaugeDouble("rimworld_colonist_mood_break_threshold_major", majorThreshold / threshCount, timestampNanos));
                metrics.Add(GaugeDouble("rimworld_colonist_mood_break_threshold_extreme", extremeThreshold / threshCount, timestampNanos));
            }

            // "Act now" colony counters (the flagship second-screen / alert signals).
            metrics.Add(GaugeLong("rimworld_colonists_near_break_total", nearBreakMinor, timestampNanos, new[] { Attr("severity", "minor") }));
            metrics.Add(GaugeLong("rimworld_colonists_near_break_total", nearBreakMajor, timestampNanos, new[] { Attr("severity", "major") }));
            metrics.Add(GaugeLong("rimworld_colonists_bleeding_total", bleeding, timestampNanos));
            metrics.Add(GaugeDouble("rimworld_colony_bleed_rate_total", bleedRateSum, timestampNanos));
            metrics.Add(GaugeLong("rimworld_colonists_tend_needed_total", tendNeeded, timestampNanos));
            metrics.Add(GaugeLong("rimworld_colonists_losing_immunity_total", losingImmunity, timestampNanos));
            metrics.Add(GaugeLong("rimworld_colonists_hungry_total", hungry, timestampNanos, new[] { Attr("level", "hungry") }));
            metrics.Add(GaugeLong("rimworld_colonists_hungry_total", urgentlyHungry, timestampNanos, new[] { Attr("level", "urgent") }));
            metrics.Add(GaugeLong("rimworld_colonists_hungry_total", starving, timestampNanos, new[] { Attr("level", "starving") }));
        }

        /// <summary>True if the pawn has any immunizable disease whose immunity is behind its severity.</summary>
        private static bool IsLosingImmunityRace(Pawn pawn)
        {
            var hediffs = pawn.health?.hediffSet?.hediffs;
            var immunity = pawn.health?.immunity;
            if (hediffs == null || immunity == null) return false;

            foreach (var hediff in hediffs)
            {
                if (hediff?.def == null) continue;
                if (!hediff.def.PossibleToDevelopImmunityNaturally()) continue;
                if (!immunity.ImmunityRecordExists(hediff.def)) continue;
                if (immunity.GetImmunity(hediff.def, false) < hediff.Severity) return true;
            }
            return false;
        }

        private static void CollectHediffs(List<Metric> metrics, Pawn pawn, string name, string pawnId, long ts)
        {
            if (pawn.health?.hediffSet?.hediffs == null) return;

            int injury = 0, disease = 0, addiction = 0, implant = 0, chronic = 0;

            foreach (var hediff in pawn.health.hediffSet.hediffs)
            {
                if (hediff?.def == null) continue;
                if (hediff is Hediff_Injury) injury++;
                else if (hediff is Hediff_Addiction) addiction++;
                else if (hediff is Hediff_AddedPart || hediff is Hediff_Implant) implant++;
                else if (hediff.def?.chronic == true) chronic++;
                else if (hediff.Visible && hediff.def?.makesSickThought == true) disease++;
            }

            metrics.Add(GaugeLong("rimworld_colonist_hediff_count", injury, ts, new[] { Attr("name", name), Attr("pawn_id", pawnId), Attr("category", "Injury") }));
            metrics.Add(GaugeLong("rimworld_colonist_hediff_count", disease, ts, new[] { Attr("name", name), Attr("pawn_id", pawnId), Attr("category", "Disease") }));
            metrics.Add(GaugeLong("rimworld_colonist_hediff_count", addiction, ts, new[] { Attr("name", name), Attr("pawn_id", pawnId), Attr("category", "Addiction") }));
            metrics.Add(GaugeLong("rimworld_colonist_hediff_count", implant, ts, new[] { Attr("name", name), Attr("pawn_id", pawnId), Attr("category", "Implant") }));
            metrics.Add(GaugeLong("rimworld_colonist_hediff_count", chronic, ts, new[] { Attr("name", name), Attr("pawn_id", pawnId), Attr("category", "Chronic") }));
        }
    }
}
