import { useState } from "react";

const PHASES = [
  {
    id: "mod",
    label: "Phase 1",
    title: "C# RimWorld Mod",
    subtitle: "In-process telemetry producer",
    color: "#e85d2f",
    icon: "⚙️",
    sections: [
      {
        title: "Project Scaffold",
        items: [
          {
            id: "mod-1",
            text: "Create mod folder structure: About/, Assemblies/, Source/, Defs/",
            detail: "Standard RimWorld mod layout. About.xml must declare mod name, author, supportedVersions (1.5, 1.6), and dependency on HarmonyLib (brrainz.harmony). Add a preview PNG (640x360) for Steam Workshop.",
          },
          {
            id: "mod-2",
            text: "Set up C# Class Library project targeting .NET Framework 4.7.2",
            detail: "RimWorld runs on Unity 2022 with Mono. Reference Assembly-CSharp.dll and UnityEngine.CoreModule.dll from the RimWorld install dir. Set CopyLocal=false on all RimWorld refs so they don't get bundled into your output assembly.",
          },
          {
            id: "mod-3",
            text: "Add Lib.Harmony NuGet reference; decide OTLP transport strategy",
            detail: "Lib.Harmony is required for event hooks. For OTLP transport: avoid the full OpenTelemetry .NET SDK — it brings in many DLLs that conflict with Unity's Mono runtime. Instead write a focused raw OTLP/HTTP sender (see OTLP Sender section). Bundle only what you write plus Google.Protobuf.dll if needed.",
          },
          {
            id: "mod-4",
            text: "Implement ModSettings: OTLP endpoint URL, auth header value, export interval, per-category toggles",
            detail: "Use Verse.ModSettings + Mod class for the in-game settings UI. Fields: otlpEndpoint (default http://localhost:4318), authHeader (Bearer token or API key — whatever your gateway requires), exportIntervalSeconds (default 15), plus bool toggles for Colonists/Resources/Infrastructure/Events/World. Stored as XML via Scribe.",
          },
        ],
      },
      {
        title: "OTLP HTTP Sender",
        items: [
          {
            id: "mod-5",
            text: "Implement OtlpHttpSender: POST to /v1/metrics and /v1/logs using System.Net.HttpClient",
            detail: "HttpClient is available in .NET 4.7.2/Mono. POST to {endpoint}/v1/metrics for metrics, {endpoint}/v1/logs for log events. Content-Type: application/x-protobuf. Add Authorization header from settings if configured. This is the only network code in the mod.",
          },
          {
            id: "mod-6",
            text: "Implement Protobuf serialization using Google.Protobuf + generated classes from opentelemetry-proto",
            detail: "Add Google.Protobuf NuGet — it is standalone and well-tested on Mono. Bundle Google.Protobuf.dll in Assemblies/. Generate C# classes from opentelemetry-proto .proto files: metrics.proto, logs.proto, common.proto, resource.proto. These give you strongly-typed OTLP message builders without reinventing binary encoding.",
          },
          {
            id: "mod-7",
            text: "Build OTLP Resource with service attributes — set once per export, attached to every payload",
            detail: "Resource attributes: service.name=rimworld-colony, service.version=<modVersion>, colony.name=Faction.OfPlayer.Name, map.seed=<seed string>, storyteller.name=StorytellerDef.label, difficulty.label=DifficultyDef.label. These become filterable dimensions in Mimir/Grafana — essential for separating multiple campaigns.",
          },
          {
            id: "mod-8",
            text: "Implement ConcurrentQueue + dedicated background Thread for non-blocking export",
            detail: "Game tick thread must NEVER block on HTTP. Push serialized byte[] payloads onto a ConcurrentQueue<(string endpoint, byte[] data)>. A separate background Thread (not Task — Mono threadpool behavior under Unity is unreliable) wakes on interval, drains the queue, sends each payload. Catches all exceptions and writes to Log.Warning — never throws to game.",
          },
          {
            id: "mod-9",
            text: "Implement exponential backoff + circuit breaker for unreachable endpoint",
            detail: "Track consecutive failures. On failure: retry after 2^n seconds (cap 60s). After 10 consecutive failures, switch to offline mode — stop attempting until mod settings are re-saved or map reloads. Show last export status in mod settings UI. Prevents log spam when gateway is down during play.",
          },
        ],
      },
      {
        title: "Core Collection Engine",
        items: [
          {
            id: "mod-10",
            text: "Implement GameComponent (ColonyTelemetryComponent) as the collection heartbeat",
            detail: "Override GameComponentTick(). Throttle using UnityEngine.Time.realtimeSinceStartup — only collect when (now - lastExport) >= intervalSeconds. On trigger: run all enabled collectors, build a single ExportMetricsServiceRequest batch, serialize, enqueue. GameComponents survive map transitions, making them more reliable than MapComponents for this.",
          },
          {
            id: "mod-11",
            text: "Implement MetricBatch builder: accumulate all datapoints into one OTLP request per export cycle",
            detail: "One ExportMetricsServiceRequest per cycle containing all metrics under one InstrumentationScope (name=rimworld-telemetry, version=modVersion). Use Gauge for current values (mood, stockpile, temp). Use Sum(monotonic=true, cumulative) for counters (deaths, raids). Batching into one POST is more efficient and easier to debug than one request per metric.",
          },
        ],
      },
      {
        title: "Colonist Metrics",
        items: [
          {
            id: "mod-12",
            text: "rimworld_colonists_total — gauge by colonist_type (free / prisoner / slave / guest)",
            detail: "PawnsFinder.AllMapsWorldAndTemporary_AliveOrDead filtered by faction and conditions. colonist_type label separates free colonists from prisoners etc. This is the top-line colony count.",
          },
          {
            id: "mod-13",
            text: "rimworld_colonist_mood — per-colonist gauge with name + pawn_id labels",
            detail: "pawn.needs.mood.CurLevel (0.0–1.0). Labels: name=pawn.LabelShort, pawn_id=pawn.ThingID (stable unique ID across saves). Also emit rimworld_colonist_mood_break_threshold_minor/major/extreme as constant gauges so Grafana can draw threshold reference lines without hardcoding values.",
          },
          {
            id: "mod-14",
            text: "rimworld_colonist_health — health summary % + pain level per colonist",
            detail: "pawn.health.summaryHealth.SummaryHealthPercent and pawn.health.hediffSet.PainTotal. Also emit rimworld_colonist_hediff_count{name, category} where category = Injury | Disease | Addiction | Implant | Chronic.",
          },
          {
            id: "mod-15",
            text: "rimworld_colonist_skill{name, skill} — skill level (0–20) per pawn per skill",
            detail: "Iterate pawn.skills.skills. Emit level and passion (0/1/2) as a separate attribute. Watching skill growth over a multi-year campaign reveals character development over time in Grafana.",
          },
          {
            id: "mod-16",
            text: "rimworld_colonist_need{name, need} — food/rest/recreation/comfort/joy (0–1)",
            detail: "pawn.needs.AllNeeds filtered to need.ShowOnNeedList. Systemic issues (everyone low on recreation) show up as a depressed column on the grouped bar chart.",
          },
          {
            id: "mod-17",
            text: "rimworld_colonist_thoughts_negative_total{name} — count of active negative thoughts",
            detail: "pawn.needs.mood.thoughts.memories.Memories.Count(m => m.MoodOffset() < 0). Peaks in this metric precede mental breaks — the early warning signal the mood gauge alone doesn't give you.",
          },
        ],
      },
      {
        title: "Resource & Economy Metrics",
        items: [
          {
            id: "mod-18",
            text: "rimworld_resource_stockpile{item_def, item_label, item_category} — all stored resources",
            detail: "map.resourceCounter.GetCount(ThingDef) for each def where CountAsResource==true. item_category from def.thingCategories: RawFood / Meals / Metal / Medicine / Textile / Manufactured. The colony economy snapshot — every item tracked over time.",
          },
          {
            id: "mod-19",
            text: "rimworld_colony_wealth{wealth_type} — items / buildings / pawns / total",
            detail: "map.wealthWatcher.WealthItems, WealthBuildings, WealthPawns, WealthTotal. Colony wealth is the most important single number — it directly scales incoming raid difficulty. Trend over time tells the whole colony story.",
          },
          {
            id: "mod-20",
            text: "rimworld_food_days_remaining — estimated days of food at current consumption rate",
            detail: "Sum nutrition across all food items, divide by (colonist_count × pawn.needs.food.FoodFallPerTick × TicksPerDay). Include animals in consumption estimate. Set Grafana threshold annotations at 7 and 3 days — this gauge should feel alarming when low.",
          },
          {
            id: "mod-21",
            text: "rimworld_trade_silver_total — silver on hand",
            detail: "resourceCounter.GetCount(ThingDefOf.Silver). Simple but important economic liquidity signal.",
          },
        ],
      },
      {
        title: "Infrastructure Metrics",
        items: [
          {
            id: "mod-22",
            text: "rimworld_power_grid{grid_id} — production_w, consumption_w, battery_stored_wh, battery_max_wh",
            detail: "Iterate map.powerNetManager.AllNets. Per PowerNet: sum CompPowerTrader.PowerOutput (positive=producer, negative=consumer). Sum CompPowerBattery.StoredEnergy and MaxEnergyStored. Detecting consumption > production predicts blackouts before they cascade.",
          },
          {
            id: "mod-23",
            text: "rimworld_temperature_outdoor and rimworld_temperature_room{room_role}",
            detail: "Outdoor: map.mapTemperature.OutdoorTemp. Rooms: sample by Room.Role.defName (Bedroom, Kitchen, FreezerRoom, HospitalRoom). Freezer drifting above 0°C warrants a Grafana alert rule.",
          },
          {
            id: "mod-24",
            text: "rimworld_buildings_total{building_def, building_category} and rimworld_beds_total",
            detail: "Iterate map.listerBuildings.allBuildingsColonist. Group by def.designationCategory.defName. Track beds separately for housing ratio panel.",
          },
          {
            id: "mod-25",
            text: "rimworld_fire_count and rimworld_filth_count — map hazard indicators",
            detail: "map.listerThings.ThingsOfDef(ThingDefOf.Fire).Count. Filth: things where def.category == ThingCategory.Filth. Any fire > 0 should trigger a critical Grafana annotation.",
          },
        ],
      },
      {
        title: "Threat & World Metrics",
        items: [
          {
            id: "mod-26",
            text: "rimworld_storyteller_threat_points — current threat budget",
            detail: "StorytellerUtility.DefaultThreatPointsNow(map). The single most interesting metric to graph over time — grows with wealth, dips after raids. The shape of this curve narrates the entire playthrough.",
          },
          {
            id: "mod-27",
            text: "rimworld_faction_goodwill{faction_name, faction_type} — diplomatic standing (-100 to 100)",
            detail: "Find.FactionManager.AllFactions where !IsPlayer && !Hidden. faction.PlayerGoodwill. faction_type = Outlander | Tribe | Empire | Pirate | Mechanoid. Declining goodwill predicts future hostility.",
          },
          {
            id: "mod-28",
            text: "rimworld_growing_season_active bool gauge + season label metric",
            detail: "PlantUtility.GrowingSeasonNow(map.Tile). GenLocalDate.Season(map.Tile).Label(). Critical context for interpreting food stockpile rate-of-change graphs.",
          },
          {
            id: "mod-29",
            text: "rimworld_animals_tamed{animal_def} and rimworld_animals_wild{animal_def, is_predator}",
            detail: "Tamed: player-faction non-humanlike pawns. Wild: all non-player animal pawns on map. Predator flag from RaceProps.predator. Tracks farm herds and surface threat levels.",
          },
        ],
      },
      {
        title: "Event Hooks → OTLP Logs",
        items: [
          {
            id: "mod-30",
            text: "Harmony postfix on IncidentWorker.TryExecuteWorker → emit incident log record",
            detail: "IncidentParms carries points, faction, target map. LogRecord body: human-readable summary. Severity: WARN for hostile (RaidEnemy, Infestation, MechCluster), INFO for neutral (Wanderer, Eclipse, TraderArrival). Attributes: incident.def, incident.points, incident.faction_name. These become Grafana annotations overlaid on every metric panel simultaneously.",
          },
          {
            id: "mod-31",
            text: "Harmony postfix on Pawn.Kill → emit colonist/named-animal death log",
            detail: "Filter to player-faction pawns and HasName animals. Attributes: pawn.name, pawn.age_years, death.cause (dinfo?.Def?.defName ?? 'unknown'), death.killer_faction, death.weapon_def. Deaths on the Grafana timeline are the emotional core of the dashboard.",
          },
          {
            id: "mod-32",
            text: "Harmony postfix on ResearchManager.FinishProject → emit research completion log",
            detail: "Attributes: research.def, research.label, research.cost_total. Research milestones (advanced fabrication, ship parts, neural supercharge) are key campaign waypoints worth marking.",
          },
          {
            id: "mod-33",
            text: "Harmony postfix on MentalStateHandler.TryStartMentalState → emit mental break log",
            detail: "Attributes: pawn.name, mental_state.def (Berserk/Tantrum/WanderPsychotic/Catatonic/etc), pawn.mood_at_break (snapshot of mood level at time of break). Cross-reference with mood history to validate early-warning thresholds.",
          },
          {
            id: "mod-34",
            text: "Harmony postfix on TradeSession.SetupWith → emit trade event log",
            detail: "Attributes: trader.name, trader.faction, trader.kind (Orbital/Caravan/Settlement). Marks trade windows on the Grafana timeline — visible against silver stockpile graphs.",
          },
          {
            id: "mod-35",
            text: "Emit game lifecycle log on map load: game version, mod version, scenario, seed",
            detail: "From GameComponent constructor or StaticConstructorOnStartup. Single INFO record: game.version, mod.version, scenario.name, map.seed, storyteller.name. Acts as session start marker and documents the data provenance.",
          },
          {
            id: "mod-36",
            text: "Harmony postfix on Pawn_RelationsTracker.AddDirectRelation → emit relationship event log",
            detail: "Filter to significant PawnRelationDefs: Spouse, Lover, Bond (animal), Rival, Fiance. Attributes: pawn_a, pawn_b, relation_type. Social colony dynamics visible as timeline annotations.",
          },
        ],
      },
    ],
  },
  {
    id: "otlp",
    label: "Phase 2",
    title: "OTLP Gateway Connection",
    subtitle: "Wire mod to your existing gateway",
    color: "#2f8be8",
    icon: "🔌",
    sections: [
      {
        title: "Gateway Auth & Connectivity",
        items: [
          {
            id: "otlp-1",
            text: "Confirm OTLP HTTP endpoint URL and auth method (Bearer token / API key / mTLS)",
            detail: "Get exact endpoints from your infra: typically https://<host>/otlp/v1/metrics and /otlp/v1/logs. Most gateways accept Authorization: Bearer <token> or a custom header. Confirm HTTP/Protobuf (port 4318) vs gRPC (port 4317) — HTTP/Protobuf is simpler from .NET Mono and avoids gRPC lib conflicts.",
          },
          {
            id: "otlp-2",
            text: "Confirm Mimir tenant/org ID — add X-Scope-OrgID header to metric requests",
            detail: "Grafana Mimir in multi-tenant mode requires X-Scope-OrgID on every request. Even single-tenant deployments often require it set to 'anonymous'. Check with your infra team. Goes in ModSettings alongside the auth token.",
          },
          {
            id: "otlp-3",
            text: "Confirm Loki tenant header requirement for OTLP log ingestion",
            detail: "Same pattern as Mimir: X-Scope-OrgID. If your gateway routes metrics and logs to different backends, you may need separate metric endpoint and log endpoint fields in mod settings.",
          },
          {
            id: "otlp-4",
            text: "Smoke test with curl before wiring the mod — validate auth and routing end-to-end",
            detail: "curl -X POST https://<gateway>/otlp/v1/metrics -H 'Authorization: Bearer <token>' -H 'Content-Type: application/x-protobuf' -H 'X-Scope-OrgID: <id>'. A 200/204 confirms the gateway accepts OTLP. Use otel-cli (go install github.com/equinix-labs/otel-cli@latest) for a quick smoke test with a real valid OTLP payload. Far easier to debug now than after writing the mod.",
          },
          {
            id: "otlp-5",
            text: "Decide: direct gateway from mod vs local Alloy sidecar as relay",
            detail: "Option A — Direct: mod POSTs to remote gateway with auth credentials stored in mod settings XML on disk. Simple, no extra process. Fine for personal use. Option B — Local Alloy relay: mod sends to unauthenticated localhost:4318, Alloy injects auth and forwards. Better if you want to share the mod publicly (users configure Alloy, not the mod). Either works — choose based on your preference.",
          },
        ],
      },
      {
        title: "If Using Local Alloy Relay (Optional)",
        items: [
          {
            id: "otlp-6",
            text: "Write minimal config.alloy: OTLP receiver → prometheusremotewrite to Mimir + Loki exporter",
            detail: "otelcol.receiver.otlp on localhost:4318 (HTTP only). otelcol.exporter.prometheusremotewrite to your Mimir remote_write endpoint with auth headers. otelcol.exporter.loki to your Loki push endpoint. This is ~30 lines of Alloy config — commit it to the repo for reproducibility.",
          },
          {
            id: "otlp-7",
            text: "Add attribute enrichment in Alloy: inject host.name and os.type automatically",
            detail: "otelcol.processor.attributes adds host.name=env(COMPUTERNAME) and os.type. Useful for distinguishing sessions across multiple gaming machines without changing mod settings.",
          },
          {
            id: "otlp-8",
            text: "Run Alloy as a background service on the gaming machine",
            detail: "Linux: systemctl --user enable alloy. Windows: Task Scheduler entry triggered on login, or sc.exe to register as a service. Mod settings default endpoint stays http://localhost:4318. No auth config needed in the mod itself.",
          },
        ],
      },
      {
        title: "OTLP Schema Design",
        items: [
          {
            id: "otlp-9",
            text: "Define and document metric naming convention: rimworld_<subsystem>_<metric>_<unit>",
            detail: "Examples: rimworld_colonist_mood (ratio 0-1), rimworld_colony_wealth_silver, rimworld_resource_stockpile_units, rimworld_power_grid_production_watts. Consistent names make PromQL readable and prevent schema churn. Write this as a table in the README before writing any metric collection code.",
          },
          {
            id: "otlp-10",
            text: "Separate resource attributes (per-export) from metric attributes (per-datapoint)",
            detail: "Resource (set once, applies to entire payload): service.name, colony.name, storyteller.name, difficulty, map.seed, mod.version. Metric datapoint labels: pawn.name, item.def, faction.name, grid.id. High-cardinality values like pawn names must be datapoint attrs, not resource attrs — putting them on the Resource explodes Mimir's label index.",
          },
          {
            id: "otlp-11",
            text: "Define Loki label schema: keep labels low-cardinality, put details in log line fields",
            detail: "Loki LABELS (stream selectors, must be low cardinality): service_name=rimworld-colony, event_type=incident|death|research|trade|mental_break|lifecycle, severity=INFO|WARN. Log LINE structured fields (not indexed): pawn_name, incident_def, incident_points, research_label. Never put pawn names or item defs as Loki labels — that creates thousands of streams and kills performance.",
          },
          {
            id: "otlp-12",
            text: "Add mod.version as resource attribute for schema evolution traceability",
            detail: "When you rename a metric or add a label in mod v2.0, old data keeps the old schema, new data has the new schema. mod.version lets you write Grafana queries that handle both: label_replace() or conditional expressions. Without this, schema changes break historical panels.",
          },
        ],
      },
    ],
  },
  {
    id: "dashboard",
    label: "Phase 3",
    title: "Grafana Dashboard",
    subtitle: "Colony narrative in panels",
    color: "#2fe87a",
    icon: "📊",
    sections: [
      {
        title: "Dashboard Foundation",
        items: [
          {
            id: "dash-1",
            text: "Create dashboard variables: $colony, $map, $colonist",
            detail: "$colony: label_values(rimworld_colonists_total, colony_name) — switches between campaigns. $map: label_values({colony_name=$colony}, map_seed). $colonist: label_values(rimworld_colonist_mood{colony_name=$colony}, name) — for per-pawn drill-down rows. All panels use {colony_name=$colony} as base filter.",
          },
          {
            id: "dash-2",
            text: "Configure Loki annotation query — overlay game events on every time series panel",
            detail: "Dashboard Settings > Annotations > Add annotation using Loki datasource. Query: {service_name=\"rimworld-colony\", event_type=~\"incident|death\"}. This draws vertical lines across ALL time series panels simultaneously — raids visible on the wealth graph, deaths on the mood graph. Color by event_type: incidents=red, deaths=darkred. This single feature makes the dashboard feel like a colony chronicle.",
          },
          {
            id: "dash-3",
            text: "Structure rows: Overview / Colonists / Economy / Infrastructure / Threats / Events",
            detail: "Use collapsible rows. Overview always expanded — the top stat panels (colonist count, wealth, threat points, food days) visible without scrolling. Other rows collapse for focused views during play.",
          },
        ],
      },
      {
        title: "Overview Row",
        items: [
          {
            id: "dash-4",
            text: "Stat: Colonist count with sparkline + threshold colors",
            detail: "sum(rimworld_colonists_total{colony_name=$colony, colonist_type='free'}). Sparkline mode. Green >5, yellow 3–5, red <3.",
          },
          {
            id: "dash-5",
            text: "Stat: Colony Wealth with delta vs 1 hour ago",
            detail: "rimworld_colony_wealth{wealth_type='total'}. Show value + change arrow. Wealth growth rate is the fundamental campaign health signal.",
          },
          {
            id: "dash-6",
            text: "Stat: Threat Points — color-coded danger level",
            detail: "rimworld_storyteller_threat_points. Thresholds: green <200, yellow 200–500, orange 500–1000, red >1000. Directly proportional to next raid size — making it visible is genuinely useful during play.",
          },
          {
            id: "dash-7",
            text: "Stat: Food Days Remaining — most urgent survival metric",
            detail: "rimworld_food_days_remaining. Red <3, orange <7, green >14. Large, visible. Should feel alarming at low values.",
          },
          {
            id: "dash-8",
            text: "Time series: Colony Wealth over full campaign — THE colony story graph",
            detail: "rimworld_colony_wealth{wealth_type='total'} over max time range with raid + death annotations. Wealth dips from raids, steps up from production gains. This single panel narrates the entire playthrough. Set time range to 'since campaign start'.",
          },
          {
            id: "dash-9",
            text: "Logs panel (Loki): live event feed — last 20 game events, color-coded",
            detail: "{service_name=\"rimworld-colony\"} | json. Color by event_type. Shows raids, deaths, research, trade as they happen. The heartbeat of the dashboard during active play.",
          },
        ],
      },
      {
        title: "Colonists Row",
        items: [
          {
            id: "dash-10",
            text: "Heatmap: colonist mood over time — rows=colonists, color=mood level",
            detail: "Heatmap panel. Y-axis: name label values. Color: red (0.0) → yellow (0.5) → green (1.0). Shows who's been miserable for days vs thriving. 'Everyone red after a raid' is a striking visual pattern.",
          },
          {
            id: "dash-11",
            text: "Bar gauge: current mood per colonist, sorted ascending (worst first)",
            detail: "rimworld_colonist_mood{colony_name=$colony} sorted by value ascending. Threshold markers at minor/major/extreme break levels. Instantly identifies who's closest to snapping.",
          },
          {
            id: "dash-12",
            text: "Table: colonist skills matrix — colonists as rows, skills as columns, heatmap coloring",
            detail: "Grafana 'Prepare time series' transform + Organize fields. Color 0–20 scale (red=0, green=20). Colony competency gaps (no one skilled in Medicine) visible immediately.",
          },
          {
            id: "dash-13",
            text: "Time series: negative thought count per colonist — mental break early warning",
            detail: "rimworld_colonist_thoughts_negative_total by name. Peaks precede mental breaks. Post-mortem insight: 'Emilia accumulated 12 negative thoughts over 3 days before her breakdown'.",
          },
          {
            id: "dash-14",
            text: "Bar chart: colonist needs snapshot — all colonists per need category",
            detail: "rimworld_colonist_need{colony_name=$colony} grouped by need. Systemic issues show as a depressed column (everyone low on recreation = build a rec room).",
          },
          {
            id: "dash-15",
            text: "Stat row: total active injuries / diseases / addictions colony-wide",
            detail: "sum(rimworld_colonist_hediff_count) by category. Rising addiction count = drug policy review needed.",
          },
        ],
      },
      {
        title: "Economy Row",
        items: [
          {
            id: "dash-16",
            text: "Time series: key resources over campaign — steel, components, food, medicine, gold",
            detail: "rimworld_resource_stockpile{item_def=~'Steel|ComponentIndustrial|MealSimple|MedicineHerbal|Gold'}. Multiple lines with legend showing current values. The resource graph IS the economic history of the colony.",
          },
          {
            id: "dash-17",
            text: "Time series: resource rates — rate(stockpile[1h]) for food and medicine",
            detail: "Negative rate = consuming faster than producing. The moment food rate turns negative is when to act. Overlaid on stockpile graph as secondary axis.",
          },
          {
            id: "dash-18",
            text: "Pie chart: wealth composition — items vs buildings vs pawns",
            detail: "rimworld_colony_wealth by wealth_type. High item-wealth relative to building-wealth = more vulnerable to raids carrying off goods.",
          },
          {
            id: "dash-19",
            text: "Table: full resource inventory — all items, current count, 1h rate, sparkline",
            detail: "All rimworld_resource_stockpile values. Sortable columns. The detailed ledger view for deep inventory dives.",
          },
        ],
      },
      {
        title: "Infrastructure Row",
        items: [
          {
            id: "dash-20",
            text: "Time series: power grid — production vs consumption vs battery, fill areas",
            detail: "Production and consumption as filled area series on same axis. Battery as secondary line. The moment consumption exceeds production is a crossover event immediately visible.",
          },
          {
            id: "dash-21",
            text: "Time series: temperature — outdoor + freezer + bedrooms over time",
            detail: "rimworld_temperature_outdoor and rimworld_temperature_room by role. Freezer drift above 0°C = alert. Outdoor temp with growing season threshold shading.",
          },
          {
            id: "dash-22",
            text: "Stat: active fires — CRITICAL visual at > 0",
            detail: "rimworld_fire_count. Any fire should visually dominate the dashboard — bright red, large text. Consider a Grafana alert rule → desktop notification.",
          },
          {
            id: "dash-23",
            text: "Gauge: beds-to-colonists ratio",
            detail: "rimworld_beds_total / rimworld_colonists_total{colonist_type='free'}. Below 1.0 = floor sleeping = mood penalties.",
          },
        ],
      },
      {
        title: "Threats & Events Row",
        items: [
          {
            id: "dash-24",
            text: "Time series: threat points over full campaign with incident annotations",
            detail: "The central narrative panel. Threat points rise → raid annotation → dip → rise again. The rhythm of a RimWorld colony. Overlay faction goodwill lines for diplomatic context.",
          },
          {
            id: "dash-25",
            text: "Bar chart: incident frequency by type from Loki (count_over_time per incident.def)",
            detail: "count_over_time({event_type=\"incident\"}[$__range]) grouped by incident_def. What defines this colony's threat profile? Mostly raids? Infestations? Mechanoid clusters?",
          },
          {
            id: "dash-26",
            text: "Time series: faction goodwill over campaign",
            detail: "rimworld_faction_goodwill by faction_name. Lines crossing zero = turning hostile. A long-term trading partner slowly going red is great dashboard storytelling.",
          },
          {
            id: "dash-27",
            text: "Logs panel: death log (Loki, event_type=death) — the colony memorial",
            detail: "{event_type=\"death\"} | json | line_format '{{.pawn_name}} — {{.death_cause}} ({{.death_killer_faction}})'. The emotional record of every colonist and named animal lost.",
          },
          {
            id: "dash-28",
            text: "Stat: total colonist deaths (campaign cumulative)",
            detail: "count_over_time({event_type=\"death\"}[$__range]). The number that makes this game feel real.",
          },
        ],
      },
      {
        title: "Research & World Row",
        items: [
          {
            id: "dash-29",
            text: "Logs panel: research timeline (Loki, event_type=research)",
            detail: "{event_type=\"research\"} | json | line_format '✓ {{.research_label}} ({{.research_cost}} pts)'. Tech progression arc from primitive → industrial → spacer made visible.",
          },
          {
            id: "dash-30",
            text: "Time series: colonist population over campaign with death annotations",
            detail: "rimworld_colonists_total over max range. Drops = deaths. Steps up = recruitment. The population graph is a biography of the colony.",
          },
          {
            id: "dash-31",
            text: "Bar chart: tamed animal population by species",
            detail: "rimworld_animals_tamed by animal_def. 30 bison vs 30 muffalo tells different colony character stories.",
          },
          {
            id: "dash-32",
            text: "Info stat row: season, biome, growing days remaining, storyteller, difficulty",
            detail: "Label panels from label_values() queries on resource attributes. Non-numeric context that makes other panels interpretable — food rate of -5/day means different things in a tropical vs ice sheet biome.",
          },
        ],
      },
    ],
  },
  {
    id: "ops",
    label: "Phase 4",
    title: "Packaging & Docs",
    subtitle: "Shareable and maintainable",
    color: "#c82fe8",
    icon: "📦",
    sections: [
      {
        title: "Mod Distribution",
        items: [
          {
            id: "ops-1",
            text: "Publish to Steam Workshop — description links to GitHub and Grafana dashboard import",
            detail: "Development Mode > Mod Manager > Advanced > Upload. Description explains the mod, links GitHub repo, and includes the grafana.com dashboard import ID or JSON so others can get the companion dashboard in one click.",
          },
          {
            id: "ops-2",
            text: "GitHub release: compiled DLL, changelog, SHA256 checksums",
            detail: "For GOG/Linux/non-Steam players. Bundle: YourMod.dll, Google.Protobuf.dll if used, About.xml. Never bundle Assembly-CSharp.dll or UnityEngine dlls.",
          },
          {
            id: "ops-3",
            text: "Write metric dictionary in README: every metric name, type, labels, description",
            detail: "A Markdown table: | Metric | Type | Labels | Unit | Description |. This is the PromQL query reference. Without it, building Grafana panels is guesswork.",
          },
          {
            id: "ops-4",
            text: "Export Grafana dashboard JSON and commit to repo",
            detail: "Dashboard > Share > Export > Save to file. Commit to /grafana/colony-overview.json. Tag dashboard JSON version alongside mod version for compatibility tracking.",
          },
        ],
      },
      {
        title: "Reliability & Testing",
        items: [
          {
            id: "ops-5",
            text: "Extract OtlpHttpSender to standalone test project — unit test serialization outside RimWorld",
            detail: "OTLP serialization logic must be testable without launching the game. NUnit tests verifying: correct Protobuf encoding, all resource attributes present, metric names match dictionary, auth header set when configured, batch contains expected datapoint count.",
          },
          {
            id: "ops-6",
            text: "Test with common mod combinations: Combat Extended, Vanilla Expanded, Biotech, Anomaly",
            detail: "CE changes pawn health internals — verify hediff iteration doesn't crash. VE adds hundreds of ThingDefs — verify resourceCounter iteration null-checks all def lookups. Anomaly adds new pawn types — verify colonist filters correctly exclude entities/undead/husks.",
          },
          {
            id: "ops-7",
            text: "Add telemetry status indicator in mod settings: last export time, success/fail, payload size",
            detail: "'Last export: 3s ago ✓ (2.1 KB)' or 'FAILED — connection refused'. Saves enormous debugging time when users misconfigure the endpoint or auth header.",
          },
          {
            id: "ops-8",
            text: "Profile tick performance with RimWorld dev tools — verify < 0.1ms overhead when not exporting",
            detail: "Enable Development Mode, use the tick profiler. The GameComponentTick path when not on export interval should be negligible. Export is async so doesn't block ticks. Test with large colony: 20+ colonists, 2000+ item stockpile.",
          },
          {
            id: "ops-9",
            text: "Add per-savegame campaign ID to separate multiple playthroughs in metrics",
            detail: "Expose a 'Campaign ID' setting (auto-generated UUID on new game, persisted via Scribe). Emit it as a resource attribute (e.g. campaign_id) on every OTLP export. This allows the Grafana $colony variable to distinguish multiple simultaneous or sequential saves — colony wealth from Run A and Run B won't merge into the same time series. Surface it in Mod Settings so users can rename it (e.g. 'iron-man-2025') for readable dashboard labels.",
          },
          {
            id: "ops-10",
            text: "Verify Grafana dashboard visualizations with common sense strategies",
            detail: "Walk through each panel with a real (or replayed) session and confirm: (1) Stat panels match in-game values at the same moment. (2) Time series trends make intuitive sense — wealth rises after mining, drops after raids. (3) Mood dips are visible in the chart at the same tick a mental break fires. (4) Annotations from Loki align with the metric spikes they caused. (5) Food days countdown tracks reality — compare to manual calculation. (6) Power balance goes negative exactly when a blackout happens. (7) Threat points curve rises with wealth and resets after a raid. Document any panel that consistently misleads or needs query adjustment.",
          },
        ],
      },
    ],
  },
];

const totalItems = PHASES.flatMap(p => p.sections.flatMap(s => s.items)).length;

export default function App() {
  const [completed, setCompleted] = useState({});
  const [expanded, setExpanded] = useState({});
  const [activePhase, setActivePhase] = useState("mod");
  const [expandedSections, setExpandedSections] = useState({});

  const toggle = (id) => setCompleted(c => ({ ...c, [id]: !c[id] }));
  const toggleDetail = (id) => setExpanded(e => ({ ...e, [id]: !e[id] }));
  const toggleSection = (key) => setExpandedSections(s => ({ ...s, [key]: s[key] === false ? true : false }));

  const completedCount = Object.values(completed).filter(Boolean).length;
  const progress = Math.round((completedCount / totalItems) * 100);
  const phase = PHASES.find(p => p.id === activePhase);
  const phaseItems = phase.sections.flatMap(s => s.items);
  const phaseCompleted = phaseItems.filter(i => completed[i.id]).length;
  const phaseProgress = Math.round((phaseCompleted / phaseItems.length) * 100);

  return (
    <div style={{ minHeight: "100vh", background: "#0d0f12", color: "#e8e2d4", fontFamily: "'Courier New', 'Lucida Console', monospace", fontSize: 13 }}>
      {/* Header */}
      <div style={{ borderBottom: "1px solid #2a2d32", padding: "20px 28px 16px", display: "flex", alignItems: "flex-start", justifyContent: "space-between", gap: 24, flexWrap: "wrap" }}>
        <div>
          <div style={{ display: "flex", alignItems: "center", gap: 8, marginBottom: 4 }}>
            {[["RimWorld","#e85d2f"],["→","#3a3d42"],["OTLP","#2f8be8"],["→","#3a3d42"],["Mimir / Loki","#2fe87a"]].map(([t,c],i) => (
              <span key={i} style={{ fontSize: 11, letterSpacing: t==="→"?0:2, color: c, textTransform: "uppercase", fontWeight: t==="→"?400:700 }}>{t}</span>
            ))}
          </div>
          <h1 style={{ margin: 0, fontSize: 20, fontWeight: 700, color: "#f0ead8", letterSpacing: -0.5 }}>Colony Dashboard Implementation</h1>
          <p style={{ margin: "4px 0 0", color: "#6b7280", fontSize: 11, letterSpacing: 1 }}>{completedCount} / {totalItems} tasks · existing Grafana + Mimir + Loki</p>
        </div>
        <div style={{ minWidth: 180 }}>
          <div style={{ display: "flex", justifyContent: "space-between", marginBottom: 6 }}>
            <span style={{ color: "#6b7280", fontSize: 10, letterSpacing: 1, textTransform: "uppercase" }}>Overall</span>
            <span style={{ color: "#f0ead8", fontSize: 12, fontWeight: 700 }}>{progress}%</span>
          </div>
          <div style={{ height: 6, background: "#1e2025", borderRadius: 3, overflow: "hidden" }}>
            <div style={{ height: "100%", width: `${progress}%`, background: "linear-gradient(90deg,#e85d2f,#2f8be8,#2fe87a)", transition: "width 0.4s", borderRadius: 3 }} />
          </div>
          <div style={{ display: "flex", gap: 3, marginTop: 8 }}>
            {PHASES.map(p => {
              const pi = p.sections.flatMap(s => s.items);
              const pp = pi.length ? Math.round((pi.filter(i => completed[i.id]).length / pi.length) * 100) : 0;
              return (
                <div key={p.id} style={{ flex: 1 }}>
                  <div style={{ height: 3, background: "#1e2025", borderRadius: 2, overflow: "hidden" }}>
                    <div style={{ height: "100%", width: `${pp}%`, background: p.color, transition: "width 0.4s" }} />
                  </div>
                  <div style={{ color: p.color, fontSize: 9, marginTop: 2, textAlign: "center", opacity: 0.7 }}>{p.label}</div>
                </div>
              );
            })}
          </div>
        </div>
      </div>

      {/* Tabs */}
      <div style={{ display: "flex", borderBottom: "1px solid #2a2d32", padding: "0 28px", overflowX: "auto" }}>
        {PHASES.map(p => {
          const pi = p.sections.flatMap(s => s.items);
          const pc = pi.filter(i => completed[i.id]).length;
          const isActive = activePhase === p.id;
          return (
            <button key={p.id} onClick={() => setActivePhase(p.id)} style={{
              background: "none", border: "none", borderBottom: isActive ? `2px solid ${p.color}` : "2px solid transparent",
              color: isActive ? p.color : "#6b7280", padding: "12px 20px", cursor: "pointer",
              fontSize: 11, fontFamily: "inherit", letterSpacing: 1, textTransform: "uppercase",
              fontWeight: isActive ? 700 : 400, whiteSpace: "nowrap", display: "flex", alignItems: "center", gap: 6,
            }}>
              <span>{p.icon}</span><span>{p.label}</span>
              <span style={{ background: isActive ? p.color+"22":"#1e2025", color: isActive?p.color:"#4b5563", borderRadius: 3, padding: "1px 5px", fontSize: 10 }}>{pc}/{pi.length}</span>
            </button>
          );
        })}
      </div>

      {/* Content */}
      <div style={{ padding: "20px 28px", maxWidth: 900 }}>
        <div style={{ marginBottom: 20, display: "flex", alignItems: "center", gap: 12 }}>
          <div>
            <div style={{ display: "flex", alignItems: "center", gap: 8 }}>
              <span style={{ fontSize: 22 }}>{phase.icon}</span>
              <h2 style={{ margin: 0, fontSize: 17, fontWeight: 700, color: phase.color }}>{phase.title}</h2>
            </div>
            <p style={{ margin: "2px 0 0 30px", color: "#6b7280", fontSize: 11 }}>{phase.subtitle}</p>
          </div>
          <div style={{ marginLeft: "auto", textAlign: "right" }}>
            <div style={{ fontSize: 10, color: "#6b7280", letterSpacing: 1, textTransform: "uppercase", marginBottom: 4 }}>Phase Progress</div>
            <div style={{ display: "flex", alignItems: "center", gap: 8 }}>
              <div style={{ width: 100, height: 4, background: "#1e2025", borderRadius: 2, overflow: "hidden" }}>
                <div style={{ height: "100%", width: `${phaseProgress}%`, background: phase.color, transition: "width 0.4s" }} />
              </div>
              <span style={{ color: phase.color, fontSize: 12, fontWeight: 700 }}>{phaseProgress}%</span>
            </div>
          </div>
        </div>

        {phase.sections.map((section, si) => {
          const key = `${phase.id}-${si}`;
          const isOpen = expandedSections[key] !== false;
          const sc = section.items.filter(i => completed[i.id]).length;
          const allDone = sc === section.items.length;
          return (
            <div key={si} style={{ marginBottom: 12 }}>
              <button onClick={() => toggleSection(key)} style={{
                width: "100%", background: "#13161a",
                border: `1px solid ${allDone ? phase.color+"44" : "#2a2d32"}`,
                borderRadius: isOpen ? "6px 6px 0 0" : 6,
                padding: "10px 14px", cursor: "pointer", color: "#e8e2d4",
                fontFamily: "inherit", display: "flex", alignItems: "center", gap: 10, textAlign: "left",
              }}>
                <span style={{ color: isOpen ? phase.color : "#6b7280", fontSize: 10, fontWeight: 700 }}>{isOpen?"▼":"▶"}</span>
                <span style={{ fontWeight: 600, fontSize: 12, flex: 1 }}>{section.title}</span>
                <span style={{ fontSize: 10, color: allDone ? phase.color : "#6b7280", fontWeight: 700 }}>{sc}/{section.items.length}{allDone && " ✓"}</span>
              </button>
              {isOpen && (
                <div style={{ border: "1px solid #2a2d32", borderTop: "none", borderRadius: "0 0 6px 6px", overflow: "hidden" }}>
                  {section.items.map((item, ii) => {
                    const done = completed[item.id];
                    const showDetail = expanded[item.id];
                    return (
                      <div key={item.id} style={{ borderBottom: ii < section.items.length-1 ? "1px solid #1e2125" : "none", background: done?"#0f1a14":"#0d1014", transition: "background 0.2s" }}>
                        <div onClick={() => toggle(item.id)} style={{ display: "flex", alignItems: "flex-start", gap: 12, padding: "10px 14px", cursor: "pointer" }}>
                          <div style={{
                            width: 16, height: 16, border: `1.5px solid ${done?phase.color:"#3a3d42"}`,
                            borderRadius: 3, marginTop: 1, flexShrink: 0,
                            background: done?phase.color+"22":"transparent",
                            display: "flex", alignItems: "center", justifyContent: "center", transition: "all 0.2s",
                          }}>
                            {done && <span style={{ color: phase.color, fontSize: 10 }}>✓</span>}
                          </div>
                          <div style={{ flex: 1, color: done?"#6b7280":"#e8e2d4", textDecoration: done?"line-through":"none", lineHeight: 1.5, fontSize: 12.5 }}>{item.text}</div>
                          <button onClick={e => { e.stopPropagation(); toggleDetail(item.id); }} style={{
                            background: "none", border: "none", cursor: "pointer",
                            color: showDetail?phase.color:"#3a3d42", fontFamily: "inherit",
                            fontSize: 10, padding: "2px 4px", flexShrink: 0, marginTop: 1, transition: "color 0.2s",
                          }}>
                            {showDetail?"▲ less":"▼ detail"}
                          </button>
                        </div>
                        {showDetail && (
                          <div style={{ padding: "0 14px 12px 42px", color: "#8b9aaa", fontSize: 11.5, lineHeight: 1.65, borderTop: "1px solid #1e2125", paddingTop: 10 }}>
                            <span style={{ color: phase.color+"aa", marginRight: 6 }}>→</span>{item.detail}
                          </div>
                        )}
                      </div>
                    );
                  })}
                </div>
              )}
            </div>
          );
        })}
      </div>

      <div style={{ borderTop: "1px solid #1e2125", padding: "12px 28px", display: "flex", justifyContent: "space-between", color: "#3a3d42", fontSize: 10, letterSpacing: 0.5 }}>
        <span>RIMWORLD → OTLP → MIMIR / LOKI</span>
        <span>Click to complete · ▼ detail for implementation notes</span>
      </div>
    </div>
  );
}
