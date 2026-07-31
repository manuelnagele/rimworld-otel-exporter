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

            CollectStockpileAndFood(metrics, map, timestampNanos);
            CollectWealth(metrics, map, timestampNanos);
            CollectMedicine(metrics, map, timestampNanos);
            CollectSilver(metrics, map, timestampNanos);
        }

        /// <summary>
        /// Single pass over ThingDefs — emits stockpile counts and simultaneously accumulates
        /// food nutrition + meal count (previously scanned the whole DefDatabase twice per cycle).
        /// </summary>
        private static void CollectStockpileAndFood(List<Metric> metrics, Map map, long ts)
        {
            var counter = map.resourceCounter;
            float totalNutrition = 0f;
            long mealCount = 0;

            foreach (var def in DefDatabase<ThingDef>.AllDefs)
            {
                if (!def.CountAsResource) continue;

                int count;
                try { count = counter.GetCount(def); }
                catch { continue; }

                if (count <= 0) continue;

                metrics.Add(GaugeLong("rimworld_resource_stockpile", count, ts, new[]
                {
                    Attr("item_def", def.defName),
                    Attr("item_label", def.label ?? def.defName),
                    Attr("item_category", GetItemCategory(def))
                }));

                if (def.IsIngestible && def.ingestible != null)
                {
                    if (def.ingestible.CachedNutrition > 0)
                        totalNutrition += count * def.ingestible.CachedNutrition;

                    var pref = (int)def.ingestible.preferability;
                    if (pref >= (int)FoodPreferability.MealAwful && pref <= (int)FoodPreferability.MealLavish)
                        mealCount += count;
                }
            }

            metrics.Add(GaugeLong("rimworld_food_meals_total", mealCount, ts));
            CollectFoodDays(metrics, map, ts, totalNutrition);
        }

        private static void CollectFoodDays(List<Metric> metrics, Map map, long ts, float totalNutrition)
        {
            try
            {
                float consumptionPerTick = 0f;
                // Only mouths that eat from colony stores: colonists, slaves, prisoners, tamed animals.
                // (The old code summed ALL humanlikes on map, including raiders and visitors.)
                AddMouths(map.mapPawns.FreeColonistsSpawned, ref consumptionPerTick);
                AddMouths(map.mapPawns.PrisonersOfColonySpawned, ref consumptionPerTick);
                try { AddMouths(map.mapPawns.SlavesOfColonySpawned, ref consumptionPerTick); } catch { }
                AddMouths(map.mapPawns.SpawnedColonyAnimals, ref consumptionPerTick);

                float daysRemaining = consumptionPerTick > 0
                    ? totalNutrition / (consumptionPerTick * GenDate.TicksPerDay)
                    : 999f;

                metrics.Add(GaugeDouble("rimworld_food_days_remaining", Math.Round(daysRemaining, 1), ts));
            }
            catch { }
        }

        private static void AddMouths(List<Pawn> pawns, ref float consumptionPerTick)
        {
            if (pawns == null) return;
            foreach (var pawn in pawns)
                if (pawn?.needs?.food != null)
                    consumptionPerTick += pawn.needs.food.FoodFallPerTick;
        }

        private static void CollectWealth(List<Metric> metrics, Map map, long ts)
        {
            // Do NOT call ForceRecount() — it's expensive and the game updates wealth on its own cadence.
            metrics.Add(GaugeDouble("rimworld_colony_wealth", map.wealthWatcher.WealthItems, ts, new[] { Attr("wealth_type", "items") }));
            metrics.Add(GaugeDouble("rimworld_colony_wealth", map.wealthWatcher.WealthBuildings, ts, new[] { Attr("wealth_type", "buildings") }));
            metrics.Add(GaugeDouble("rimworld_colony_wealth", map.wealthWatcher.WealthPawns, ts, new[] { Attr("wealth_type", "pawns") }));
            metrics.Add(GaugeDouble("rimworld_colony_wealth", map.wealthWatcher.WealthTotal, ts, new[] { Attr("wealth_type", "total") }));
        }

        private static void CollectMedicine(List<Metric> metrics, Map map, long ts)
        {
            EmitMedicine(metrics, map, ts, "herbal", ThingDefOf.MedicineHerbal);
            EmitMedicine(metrics, map, ts, "industrial", ThingDefOf.MedicineIndustrial);
            EmitMedicine(metrics, map, ts, "ultratech", ThingDefOf.MedicineUltratech);
        }

        private static void EmitMedicine(List<Metric> metrics, Map map, long ts, string tier, ThingDef def)
        {
            if (def == null) return;
            try
            {
                int count = map.resourceCounter.GetCount(def);
                metrics.Add(GaugeLong("rimworld_medicine_total", count, ts, new[] { Attr("tier", tier) }));
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
