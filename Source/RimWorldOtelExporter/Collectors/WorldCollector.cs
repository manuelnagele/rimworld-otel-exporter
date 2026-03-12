using System.Collections.Generic;
using HarmonyLib;
using OpenTelemetry.Proto.Metrics.V1;
using RimWorld;
using RimWorldOtelExporter.Transport;
using Verse;
using static RimWorldOtelExporter.Transport.OtlpSerializer;

namespace RimWorldOtelExporter.Collectors
{
    public static class WorldCollector
    {
        public static void Collect(List<Metric> metrics, long timestampNanos)
        {
            var map = Find.CurrentMap;
            if (map == null) return;

            CollectThreats(metrics, map, timestampNanos);
            CollectDiplomacy(metrics, timestampNanos);
            CollectSeason(metrics, map, timestampNanos);
            CollectAnimals(metrics, map, timestampNanos);
            CollectGameDate(metrics, map, timestampNanos);
            CollectCombatState(metrics, map, timestampNanos);
            CollectMapConditions(metrics, map, timestampNanos);
            CollectResearch(metrics, timestampNanos);
            CollectWorld(metrics, timestampNanos);
        }

        private static void CollectThreats(List<Metric> metrics, Map map, long ts)
        {
            try
            {
                float threatPoints = StorytellerUtility.DefaultThreatPointsNow(map);
                metrics.Add(GaugeDouble("rimworld_storyteller_threat_points", threatPoints, ts));
            }
            catch { }
        }

        private static void CollectDiplomacy(List<Metric> metrics, long ts)
        {
            try
            {
                foreach (var faction in Find.FactionManager.AllFactions)
                {
                    if (faction == null || faction.IsPlayer || faction.Hidden) continue;

                    string factionType = GetFactionType(faction);
                    metrics.Add(GaugeLong("rimworld_faction_goodwill", faction.PlayerGoodwill, ts, new[]
                    {
                        Attr("faction_name", faction.Name ?? faction.def.defName),
                        Attr("faction_type", factionType)
                    }));
                }
            }
            catch { }
        }

        private static void CollectSeason(List<Metric> metrics, Map map, long ts)
        {
            try
            {
                Season season = GenLocalDate.Season(map.Tile);
                // Use temperature-based check: growing when avg outdoor temp > 6°C (plant minimum)
                // This is more accurate than Spring/Summer for tropical and polar biomes
                float avgTemp = map.mapTemperature.OutdoorTemp;
                bool growing = avgTemp > 6f;
                metrics.Add(GaugeLong("rimworld_growing_season_active", growing ? 1L : 0L, ts));
                // Also emit the season label as a separate gauge for Grafana annotation
                metrics.Add(GaugeLong("rimworld_game_season", (long)season, ts,
                    new[] { Attr("season_label", season.Label()) }));
            }
            catch { }
        }

        private static void CollectAnimals(List<Metric> metrics, Map map, long ts)
        {
            try
            {
                var tamed = new Dictionary<string, int>();
                var wild = new Dictionary<string, (int count, bool predator)>();

                foreach (var pawn in map.mapPawns.AllPawnsSpawned)
                {
                    if (pawn?.RaceProps == null || pawn.RaceProps.Humanlike) continue;

                    if (pawn.Faction == Faction.OfPlayer)
                    {
                        string def = pawn.def.defName;
                        tamed.TryGetValue(def, out int n);
                        tamed[def] = n + 1;
                    }
                    else if (pawn.Faction == null || pawn.Faction.HostileTo(Faction.OfPlayer) == false)
                    {
                        string def = pawn.def.defName;
                        bool predator = pawn.RaceProps.predator;
                        if (!wild.ContainsKey(def)) wild[def] = (0, predator);
                        wild[def] = (wild[def].count + 1, predator);
                    }
                }

                foreach (var kv in tamed)
                    metrics.Add(GaugeLong("rimworld_animals_tamed", kv.Value, ts, new[] { Attr("animal_def", kv.Key) }));

                foreach (var kv in wild)
                    metrics.Add(GaugeLong("rimworld_animals_wild", kv.Value.count, ts, new[]
                    {
                        Attr("animal_def", kv.Key),
                        Attr("is_predator", kv.Value.predator ? "true" : "false")
                    }));
            }
            catch { }
        }

        private static void CollectGameDate(List<Metric> metrics, Map map, long ts)
        {
            try
            {
                int year = GenDate.Year(Find.TickManager.TicksAbs, Find.WorldGrid.LongLatOf(map.Tile).x);
                int dayOfYear = GenDate.DayOfYear(Find.TickManager.TicksAbs, Find.WorldGrid.LongLatOf(map.Tile).x);
                int dayOfQuadrum = GenDate.DayOfQuadrum(Find.TickManager.TicksAbs, Find.WorldGrid.LongLatOf(map.Tile).x);
                int quadrum = (int)GenDate.Quadrum(Find.TickManager.TicksAbs, Find.WorldGrid.LongLatOf(map.Tile).x);

                metrics.Add(GaugeLong("rimworld_game_year", year, ts));
                metrics.Add(GaugeLong("rimworld_game_day_of_year", dayOfYear, ts));
                metrics.Add(GaugeLong("rimworld_game_quadrum", quadrum + 1, ts)); // 1-4
                metrics.Add(GaugeLong("rimworld_game_day_of_quadrum", dayOfQuadrum + 1, ts)); // 1-15
            }
            catch { }
        }

        private static void CollectCombatState(List<Metric> metrics, Map map, long ts)
        {
            try
            {
                int hostile = 0, downed = 0, mentalBreak = 0, drafted = 0, inspired = 0;

                foreach (var pawn in map.mapPawns.AllPawnsSpawned)
                {
                    if (pawn == null) continue;

                    // Hostile pawns actively on map
                    if (pawn.HostileTo(Faction.OfPlayer) && !pawn.Dead)
                        hostile++;
                }

                foreach (var pawn in map.mapPawns.FreeColonistsSpawned)
                {
                    if (pawn == null) continue;
                    if (pawn.Downed) downed++;
                    if (pawn.InMentalState) mentalBreak++;
                    if (pawn.Drafted) drafted++;
                    try { if (pawn.mindState?.inspirationHandler?.Inspired == true) inspired++; } catch { }
                }

                metrics.Add(GaugeLong("rimworld_hostile_pawns_on_map", hostile, ts));
                metrics.Add(GaugeLong("rimworld_colonist_downed_total", downed, ts));
                metrics.Add(GaugeLong("rimworld_colonist_mental_state_active", mentalBreak, ts));
                metrics.Add(GaugeLong("rimworld_colonist_drafted_total", drafted, ts));
                metrics.Add(GaugeLong("rimworld_colonist_inspired_total", inspired, ts));
            }
            catch { }
        }

        private static void CollectMapConditions(List<Metric> metrics, Map map, long ts)
        {
            try
            {
                var conditions = map.gameConditionManager?.ActiveConditions;
                if (conditions == null) return;

                foreach (var cond in conditions)
                {
                    if (cond?.def == null) continue;
                    // Each active condition emitted as a gauge=1 with condition name as label
                    metrics.Add(GaugeLong("rimworld_map_condition_active", 1L, ts, new[]
                    {
                        Attr("condition", cond.def.defName),
                        Attr("condition_label", cond.def.label ?? cond.def.defName)
                    }));
                }
            }
            catch { }
        }

        private static void CollectResearch(List<Metric> metrics, long ts)
        {
            try
            {
                var manager = Find.ResearchManager;
                if (manager == null) return;

                // Count completed projects
                int completed = 0;
                foreach (var proj in DefDatabase<ResearchProjectDef>.AllDefs)
                    if (proj.IsFinished) completed++;
                metrics.Add(GaugeLong("rimworld_research_completed_total", completed, ts));

                // Current project progress
                var current = Traverse.Create(manager).Field("currentProj").GetValue<ResearchProjectDef>();
                if (current != null)
                {
                    float progress = manager.GetProgress(current) / current.baseCost;
                    metrics.Add(GaugeDouble("rimworld_research_progress", progress, ts, new[]
                    {
                        Attr("research_def", current.defName),
                        Attr("research_label", current.label ?? current.defName)
                    }));
                }
            }
            catch { }
        }

        private static void CollectWorld(List<Metric> metrics, long ts)
        {
            try
            {
                int caravans = Find.WorldObjects.Caravans.Count;
                metrics.Add(GaugeLong("rimworld_caravan_count", caravans, ts));
            }
            catch { }

            try
            {
                int settlements = 0;
                foreach (var obj in Find.WorldObjects.AllWorldObjects)
                    if (obj?.Faction == Faction.OfPlayer && obj is RimWorld.Planet.Settlement)
                        settlements++;
                metrics.Add(GaugeLong("rimworld_colony_settlements_total", settlements, ts));
            }
            catch { }
        }

        private static string GetFactionType(Faction faction)
        {
            string cat = faction.def?.categoryTag ?? "";
            if (cat.Contains("Tribe")) return "Tribe";
            if (cat.Contains("Empire")) return "Empire";
            if (cat.Contains("Pirate") || cat.Contains("Raider")) return "Pirate";
            if (cat.Contains("Mech")) return "Mechanoid";
            return "Outlander";
        }
    }
}
