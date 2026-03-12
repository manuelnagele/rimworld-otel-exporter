using System.Collections.Generic;
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
                bool growing = season == Season.Spring || season == Season.Summer;
                metrics.Add(GaugeLong("rimworld_growing_season_active", growing ? 1L : 0L, ts));
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
