using System.Collections.Generic;
using OpenTelemetry.Proto.Metrics.V1;
using RimWorld;
using RimWorldOtelExporter.Transport;
using Verse;
using static RimWorldOtelExporter.Transport.OtlpSerializer;

namespace RimWorldOtelExporter.Collectors
{
    public static class InfrastructureCollector
    {
        public static void Collect(List<Metric> metrics, long timestampNanos)
        {
            var map = Find.CurrentMap;
            if (map == null) return;

            CollectPower(metrics, map, timestampNanos);
            CollectTemperature(metrics, map, timestampNanos);
            CollectBuildings(metrics, map, timestampNanos);
            CollectHazards(metrics, map, timestampNanos);
        }

        private static void CollectPower(List<Metric> metrics, Map map, long ts)
        {
            try
            {
                int gridId = 0;
                foreach (var net in map.powerNetManager.AllNetsListForReading)
                {
                    float production = 0f, consumption = 0f, stored = 0f, maxStored = 0f;

                    foreach (var comp in net.powerComps)
                    {
                        float output = comp.PowerOutput;
                        if (output > 0) production += output;
                        else consumption += -output;
                    }

                    foreach (var battery in net.batteryComps)
                    {
                        stored += battery.StoredEnergy;
                        maxStored += battery.Props.storedEnergyMax;
                    }

                    var gridAttrs = new[] { Attr("grid_id", gridId) };
                    metrics.Add(GaugeDouble("rimworld_power_grid_production_watts", production, ts, gridAttrs));
                    metrics.Add(GaugeDouble("rimworld_power_grid_consumption_watts", consumption, ts, gridAttrs));
                    metrics.Add(GaugeDouble("rimworld_power_grid_battery_stored_wh", stored, ts, gridAttrs));
                    metrics.Add(GaugeDouble("rimworld_power_grid_battery_max_wh", maxStored, ts, gridAttrs));
                    gridId++;
                }
            }
            catch { }
        }

        private static void CollectTemperature(List<Metric> metrics, Map map, long ts)
        {
            try
            {
                float outdoor = map.mapTemperature.OutdoorTemp;
                metrics.Add(GaugeDouble("rimworld_temperature_outdoor", outdoor, ts));
            }
            catch { }

            try
            {
                // Aggregate min/avg temperature and impressiveness per room role
                var roleTemps = new Dictionary<string, (float sum, float min, int count)>();
                var roleImpress = new Dictionary<string, (float sum, int count)>();

                foreach (var room in map.regionGrid.AllRooms)
                {
                    if (room?.Role == null || room.IsHuge) continue;
                    string role = room.Role.defName;
                    float temp = room.Temperature;

                    if (!roleTemps.ContainsKey(role))
                        roleTemps[role] = (0f, float.MaxValue, 0);
                    var t = roleTemps[role];
                    roleTemps[role] = (t.sum + temp, temp < t.min ? temp : t.min, t.count + 1);

                    try
                    {
                        float impress = room.GetStat(RoomStatDefOf.Impressiveness);
                        if (!roleImpress.ContainsKey(role))
                            roleImpress[role] = (0f, 0);
                        var i = roleImpress[role];
                        roleImpress[role] = (i.sum + impress, i.count + 1);
                    }
                    catch { }
                }

                foreach (var kv in roleTemps)
                {
                    var (sum, min, count) = kv.Value;
                    float avg = sum / count;
                    metrics.Add(GaugeDouble("rimworld_temperature_room_min", min, ts, new[] { Attr("room_role", kv.Key) }));
                    metrics.Add(GaugeDouble("rimworld_temperature_room_avg", avg, ts, new[] { Attr("room_role", kv.Key) }));
                }

                foreach (var kv in roleImpress)
                {
                    float avg = kv.Value.sum / kv.Value.count;
                    metrics.Add(GaugeDouble("rimworld_room_impressiveness", avg, ts, new[] { Attr("room_role", kv.Key) }));
                }
            }
            catch { }
        }

        private static void CollectBuildings(List<Metric> metrics, Map map, long ts)
        {
            try
            {
                var counts = new Dictionary<string, (int count, string category)>();
                int bedCount = 0;

                foreach (var building in map.listerBuildings.allBuildingsColonist)
                {
                    if (building?.def == null) continue;

                    string defName = building.def.defName;
                    string category = building.def.designationCategory?.defName ?? "Misc";

                    if (!counts.ContainsKey(defName))
                        counts[defName] = (0, category);
                    counts[defName] = (counts[defName].count + 1, category);

                    if (building is Building_Bed) bedCount++;
                }

                foreach (var kv in counts)
                {
                    metrics.Add(GaugeLong("rimworld_buildings_total", kv.Value.count, ts, new[]
                    {
                        Attr("building_def", kv.Key),
                        Attr("building_category", kv.Value.category)
                    }));
                }

                metrics.Add(GaugeLong("rimworld_beds_total", bedCount, ts));
            }
            catch { }
        }

        private static void CollectHazards(List<Metric> metrics, Map map, long ts)
        {
            try
            {
                int fires = map.listerThings.ThingsOfDef(ThingDefOf.Fire).Count;
                metrics.Add(GaugeLong("rimworld_fire_count", fires, ts));
            }
            catch { }

            try
            {
                int filth = 0;
                foreach (var thing in map.listerThings.ThingsInGroup(ThingRequestGroup.Filth))
                    if (thing?.def?.category == ThingCategory.Filth) filth++;
                metrics.Add(GaugeLong("rimworld_filth_count", filth, ts));
            }
            catch { }
        }
    }
}
