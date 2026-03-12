using System;
using System.Collections.Generic;
using OpenTelemetry.Proto.Metrics.V1;
using RimWorld;
using RimWorldOtelExporter.Transport;
using Verse;
using static RimWorldOtelExporter.Transport.OtlpSerializer;

namespace RimWorldOtelExporter.Collectors
{
    public static class ResourceCollector
    {
        public static void Collect(List<Metric> metrics, long timestampNanos)
        {
            var map = Find.CurrentMap;
            if (map == null) return;

            CollectStockpile(metrics, map, timestampNanos);
            CollectWealth(metrics, map, timestampNanos);
            CollectFood(metrics, map, timestampNanos);
            CollectSilver(metrics, map, timestampNanos);
        }

        private static void CollectStockpile(List<Metric> metrics, Map map, long ts)
        {
            var counter = map.resourceCounter;
            foreach (var def in DefDatabase<ThingDef>.AllDefs)
            {
                if (!def.CountAsResource) continue;

                int count;
                try { count = counter.GetCount(def); }
                catch { continue; }

                if (count <= 0) continue;

                string category = GetItemCategory(def);
                metrics.Add(GaugeLong("rimworld_resource_stockpile", count, ts, new[]
                {
                    Attr("item_def", def.defName),
                    Attr("item_label", def.label ?? def.defName),
                    Attr("item_category", category)
                }));
            }
        }

        private static void CollectWealth(List<Metric> metrics, Map map, long ts)
        {
            try { map.wealthWatcher.ForceRecount(); } catch { }

            metrics.Add(GaugeDouble("rimworld_colony_wealth", map.wealthWatcher.WealthItems, ts, new[] { Attr("wealth_type", "items") }));
            metrics.Add(GaugeDouble("rimworld_colony_wealth", map.wealthWatcher.WealthBuildings, ts, new[] { Attr("wealth_type", "buildings") }));
            metrics.Add(GaugeDouble("rimworld_colony_wealth", map.wealthWatcher.WealthPawns, ts, new[] { Attr("wealth_type", "pawns") }));
            metrics.Add(GaugeDouble("rimworld_colony_wealth", map.wealthWatcher.WealthTotal, ts, new[] { Attr("wealth_type", "total") }));
        }

        private static void CollectFood(List<Metric> metrics, Map map, long ts)
        {
            try
            {
                float totalNutrition = 0f;
                foreach (var def in DefDatabase<ThingDef>.AllDefs)
                {
                    if (def.IsIngestible && def.CountAsResource)
                    {
                        int count = map.resourceCounter.GetCount(def);
                        if (count > 0 && def.ingestible?.CachedNutrition > 0)
                            totalNutrition += count * def.ingestible.CachedNutrition;
                    }
                }

                float totalConsumptionPerTick = 0f;
                foreach (var pawn in map.mapPawns.FreeColonistsAndPrisoners)
                {
                    if (pawn.needs?.food != null)
                        totalConsumptionPerTick += pawn.needs.food.FoodFallPerTick;
                }
                // Include tamed animals
                foreach (var pawn in map.mapPawns.PawnsInFaction(Faction.OfPlayer))
                {
                    if (!pawn.RaceProps.Humanlike && pawn.needs?.food != null)
                        totalConsumptionPerTick += pawn.needs.food.FoodFallPerTick * 0.5f; // animals eat less managed food
                }

                float daysRemaining = totalConsumptionPerTick > 0
                    ? totalNutrition / (totalConsumptionPerTick * GenDate.TicksPerDay)
                    : 999f;

                metrics.Add(GaugeDouble("rimworld_food_days_remaining", Math.Round(daysRemaining, 1), ts));
            }
            catch { }
        }

        private static void CollectSilver(List<Metric> metrics, Map map, long ts)
        {
            try
            {
                int silver = map.resourceCounter.GetCount(ThingDefOf.Silver);
                metrics.Add(GaugeLong("rimworld_trade_silver_total", silver, ts));
            }
            catch { }
        }

        private static string GetItemCategory(ThingDef def)
        {
            if (def.thingCategories == null) return "Other";
            foreach (var cat in def.thingCategories)
            {
                if (cat == null) continue;
                string n = cat.defName;
                if (n.Contains("Food") || n.Contains("Meal") || n.Contains("RawFood")) return "Food";
                if (n.Contains("Metal") || n.Contains("Stone")) return "Metal";
                if (n.Contains("Medicine") || n.Contains("Drug")) return "Medicine";
                if (n.Contains("Textile") || n.Contains("Fabric") || n.Contains("Leather")) return "Textile";
                if (n.Contains("Manufactured") || n.Contains("Industrial") || n.Contains("Component")) return "Manufactured";
            }
            return "Other";
        }
    }
}
