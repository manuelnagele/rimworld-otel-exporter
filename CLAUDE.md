# RimWorld OpenTelemetry Exporter — Project Guide for Claude

## Project Overview

A C# RimWorld mod that exports real-time colony telemetry to a Grafana/Mimir/Loki observability stack via OpenTelemetry Protocol (OTLP/HTTP). Players get a live Grafana dashboard showing colonist mood, health, skills, resource stockpiles, power grids, threats, and game events as they happen.

**Full spec**: `rimworld-colony-dashboard-todos.jsx` (React dashboard UI that lists all 104 todos)

---

## Architecture

```
RimWorld Game Process
  └── GameComponent (ColonyTelemetryComponent)  ← collection heartbeat
        ├── Collectors/                          ← per-subsystem metric readers
        │     ├── ColonistCollector
        │     ├── ResourceCollector
        │     ├── InfrastructureCollector
        │     ├── ThreatCollector
        │     └── WorldCollector
        ├── HarmonyPatches/                      ← event hooks → OTLP logs
        │     ├── IncidentPatch
        │     ├── DeathPatch
        │     ├── ResearchPatch
        │     ├── MentalBreakPatch
        │     ├── TradePatch
        │     └── RelationsPatch
        └── Transport/                           ← OTLP HTTP sender
              ├── OtlpHttpSender
              ├── OtlpSerializer               ← Google.Protobuf OTLP builders
              └── ExportQueue                  ← ConcurrentQueue + background Thread
```

**Key constraint**: NEVER use the full OpenTelemetry .NET SDK — it conflicts with Unity/Mono. Use raw OTLP/HTTP with Google.Protobuf and hand-generated proto classes.

---

## Folder Structure

```
rimworld-otel-exporter/
├── About/
│   └── About.xml                    ← mod metadata (name, author, supportedVersions, deps)
├── Assemblies/                      ← compiled DLLs go here (git-ignored except .gitkeep)
├── Defs/                            ← empty for now (no custom ThingDefs needed)
├── Source/
│   └── RimWorldOtelExporter/
│       ├── RimWorldOtelExporter.csproj
│       ├── ModCore.cs               ← Mod class + ModSettings
│       ├── ColonyTelemetryComponent.cs  ← GameComponent (collection heartbeat)
│       ├── Collectors/
│       │   ├── ColonistCollector.cs
│       │   ├── ResourceCollector.cs
│       │   ├── InfrastructureCollector.cs
│       │   ├── ThreatCollector.cs
│       │   └── WorldCollector.cs
│       ├── HarmonyPatches/
│       │   ├── OtelExporterMod.cs   ← HarmonyPatchAll entry point
│       │   ├── IncidentPatch.cs
│       │   ├── DeathPatch.cs
│       │   ├── ResearchPatch.cs
│       │   ├── MentalBreakPatch.cs
│       │   ├── TradePatch.cs
│       │   └── RelationsPatch.cs
│       ├── Transport/
│       │   ├── OtlpHttpSender.cs    ← HttpClient POST to /v1/metrics, /v1/logs
│       │   ├── OtlpSerializer.cs    ← builds ExportMetricsServiceRequest etc.
│       │   ├── ExportQueue.cs       ← ConcurrentQueue + background Thread
│       │   └── Proto/               ← generated C# from opentelemetry-proto .proto files
│       └── Models/
│           ├── MetricDataPoint.cs
│           └── LogRecord.cs
├── Tests/
│   └── RimWorldOtelExporter.Tests/
│       ├── RimWorldOtelExporter.Tests.csproj
│       ├── OtlpSerializerTests.cs
│       └── OtlpHttpSenderTests.cs
├── grafana/
│   └── colony-overview.json         ← exported dashboard JSON
├── RimWorldOtelExporter.sln
├── CLAUDE.md                        ← this file
└── README.md
```

---

## Build

```bash
# Set RimWorld install dir (macOS default path used if not set)
export RIMWORLD_DIR="/Applications/RimWorld.app/Contents/MacOS"

# Build (outputs to Assemblies/)
cd Source/RimWorldOtelExporter
dotnet build -c Release

# Run tests (no RimWorld needed)
cd Tests/RimWorldOtelExporter.Tests
dotnet test
```

**Target framework**: .NET Framework 4.7.2 (`net472`) — RimWorld runs Unity 2022 with Mono.

---

## Critical Technical Decisions

### Transport
- Use `System.Net.HttpClient` (available in .NET 4.7.2/Mono)
- POST to `{endpoint}/v1/metrics` for metrics, `{endpoint}/v1/logs` for logs
- Content-Type: `application/x-protobuf`
- Add `Authorization` header from settings if configured
- Add `X-Scope-OrgID` header for Mimir/Loki multi-tenant routing

### Serialization
- Use `Google.Protobuf` NuGet (Mono-safe, standalone)
- Generate C# classes from opentelemetry-proto .proto files: `metrics.proto`, `logs.proto`, `common.proto`, `resource.proto`
- Bundle `Google.Protobuf.dll` in `Assemblies/`

### Threading
- Use `ConcurrentQueue<(string endpoint, byte[] data)>` for work items
- Use a dedicated `Thread` (NOT `Task`/threadpool — Mono threadpool is unreliable under Unity)
- Background thread wakes on interval, drains queue, sends payloads
- ALL exceptions caught in background thread; write to `Log.Warning`, never throw to game

### Collection Timing
- `GameComponent.GameComponentTick()` as heartbeat
- Throttle with `UnityEngine.Time.realtimeSinceStartup` — only collect when `(now - lastExport) >= intervalSeconds`
- Default interval: 15 seconds
- Build single `ExportMetricsServiceRequest` batch per export cycle (all metrics in one POST)

### Backoff / Circuit Breaker
- Track consecutive failures
- Retry after `2^n` seconds (cap 60s)
- After 10 consecutive failures: switch to offline mode, stop attempting
- Reset on mod settings re-save or map reload
- Show last export status in mod settings UI

---

## Mod Settings (ModCore.cs)

Fields stored as XML via `Verse.Scribe`:
- `otlpEndpoint` — default `http://localhost:4318`
- `authHeader` — Bearer token or API key
- `orgId` — `X-Scope-OrgID` value (default `anonymous`)
- `exportIntervalSeconds` — default `15`
- `enableColonists` — bool toggle
- `enableResources` — bool toggle
- `enableInfrastructure` — bool toggle
- `enableEvents` — bool toggle (Harmony patches)
- `enableWorld` — bool toggle

Settings UI shows: last export time, success/fail status, payload size in bytes.

---

## OTLP Resource Attributes (set once per export payload)

```
service.name         = "rimworld-colony"
service.version      = <modVersion>
colony.name          = Faction.OfPlayer.Name
map.seed             = <seed string>
storyteller.name     = StorytellerDef.label
difficulty.label     = DifficultyDef.label
```

---

## Metric Naming Convention

`rimworld_<subsystem>_<metric>_<unit>` (unit omitted when obvious)

| Metric | Type | Labels | Description |
|--------|------|--------|-------------|
| `rimworld_colonists_total` | Gauge | `colonist_type` (free/prisoner/slave/guest) | Colonist count by type |
| `rimworld_colonist_mood` | Gauge | `name`, `pawn_id` | Mood 0.0–1.0 per colonist |
| `rimworld_colonist_mood_break_threshold_minor` | Gauge | — | Minor break threshold constant |
| `rimworld_colonist_mood_break_threshold_major` | Gauge | — | Major break threshold constant |
| `rimworld_colonist_mood_break_threshold_extreme` | Gauge | — | Extreme break threshold constant |
| `rimworld_colonist_health` | Gauge | `name`, `pawn_id` | Health summary % |
| `rimworld_colonist_pain` | Gauge | `name`, `pawn_id` | Pain level |
| `rimworld_colonist_hediff_count` | Gauge | `name`, `category` | Hediff count by category (Injury/Disease/Addiction/Implant/Chronic) |
| `rimworld_colonist_skill` | Gauge | `name`, `skill` | Skill level 0–20 |
| `rimworld_colonist_need` | Gauge | `name`, `need` | Need level 0.0–1.0 |
| `rimworld_colonist_thoughts_negative_total` | Gauge | `name` | Count of active negative thoughts |
| `rimworld_resource_stockpile` | Gauge | `item_def`, `item_label`, `item_category` | Stockpile quantity |
| `rimworld_colony_wealth` | Gauge | `wealth_type` (items/buildings/pawns/total) | Wealth by category |
| `rimworld_food_days_remaining` | Gauge | — | Estimated days of food |
| `rimworld_trade_silver_total` | Gauge | — | Silver on hand |
| `rimworld_power_grid_production_watts` | Gauge | `grid_id` | Power production |
| `rimworld_power_grid_consumption_watts` | Gauge | `grid_id` | Power consumption |
| `rimworld_power_grid_battery_stored_wh` | Gauge | `grid_id` | Battery stored energy |
| `rimworld_power_grid_battery_max_wh` | Gauge | `grid_id` | Battery max energy |
| `rimworld_temperature_outdoor` | Gauge | — | Outdoor temp °C |
| `rimworld_temperature_room` | Gauge | `room_role` | Room temp °C by role |
| `rimworld_buildings_total` | Gauge | `building_def`, `building_category` | Building count |
| `rimworld_beds_total` | Gauge | — | Bed count |
| `rimworld_fire_count` | Gauge | — | Active fires on map |
| `rimworld_filth_count` | Gauge | — | Filth items on map |
| `rimworld_storyteller_threat_points` | Gauge | — | Current threat budget |
| `rimworld_faction_goodwill` | Gauge | `faction_name`, `faction_type` | Diplomatic standing -100 to 100 |
| `rimworld_growing_season_active` | Gauge | — | 1 if growing season, 0 if not |
| `rimworld_animals_tamed` | Gauge | `animal_def` | Tamed animal count by species |
| `rimworld_animals_wild` | Gauge | `animal_def`, `is_predator` | Wild animal count by species |

---

## Loki Log Schema

**Stream labels** (low-cardinality only):
- `service_name` = `rimworld-colony`
- `event_type` = `incident` | `death` | `research` | `trade` | `mental_break` | `relationship` | `lifecycle`
- `severity` = `INFO` | `WARN`

**Log line structured fields** (NOT Loki labels — put in attributes):
- Incidents: `incident_def`, `incident_points`, `incident_faction_name`
- Deaths: `pawn_name`, `pawn_age_years`, `death_cause`, `death_killer_faction`, `death_weapon_def`
- Research: `research_def`, `research_label`, `research_cost_total`
- Mental breaks: `pawn_name`, `mental_state_def`, `pawn_mood_at_break`
- Trade: `trader_name`, `trader_faction`, `trader_kind`
- Relationships: `pawn_a`, `pawn_b`, `relation_type`
- Lifecycle: `game_version`, `mod_version`, `scenario_name`, `map_seed`, `storyteller_name`

**NEVER put pawn names or item defs as Loki stream labels** — creates thousands of streams.

---

## Harmony Patch Targets

| Patch | Target Method | Trigger |
|-------|--------------|---------|
| IncidentPatch | `IncidentWorker.TryExecuteWorker` | Postfix |
| DeathPatch | `Pawn.Kill` | Postfix |
| ResearchPatch | `ResearchManager.FinishProject` | Postfix |
| MentalBreakPatch | `MentalStateHandler.TryStartMentalState` | Postfix |
| TradePatch | `TradeSession.SetupWith` | Postfix |
| RelationsPatch | `Pawn_RelationsTracker.AddDirectRelation` | Postfix |

Filter `DeathPatch` to player-faction pawns and `HasName` animals only.
Filter `RelationsPatch` to significant `PawnRelationDef`s: Spouse, Lover, Bond, Rival, Fiance.

---

## Proto File Generation

Download `opentelemetry-proto` repo and generate C# from:
- `opentelemetry/proto/metrics/v1/metrics.proto`
- `opentelemetry/proto/logs/v1/logs.proto`
- `opentelemetry/proto/common/v1/common.proto`
- `opentelemetry/proto/resource/v1/resource.proto`

```bash
# Install protoc and grpc tools, then:
protoc --csharp_out=Source/RimWorldOtelExporter/Transport/Proto \
  --proto_path=opentelemetry-proto \
  opentelemetry/proto/metrics/v1/metrics.proto \
  opentelemetry/proto/logs/v1/logs.proto \
  opentelemetry/proto/common/v1/common.proto \
  opentelemetry/proto/resource/v1/resource.proto
```

Place generated `.cs` files in `Source/RimWorldOtelExporter/Transport/Proto/`.

---

## Grafana Setup

### Dashboard Variables
- `$colony`: `label_values(rimworld_colonists_total, colony_name)`
- `$map`: `label_values({colony_name=$colony}, map_seed)`
- `$colonist`: `label_values(rimworld_colonist_mood{colony_name=$colony}, name)`

### Loki Annotation (overlay events on ALL panels)
- Datasource: Loki
- Query: `{service_name="rimworld-colony", event_type=~"incident|death"}`
- Color by `event_type`: incidents=red, deaths=darkred

### Key Panels
- Colony wealth over full campaign + raid/death annotations = THE colony story graph
- Colonist mood heatmap (rows=colonists, color=mood level)
- Threat points time series with incident annotations
- Death log panel = colony memorial

---

## Testing

Tests in `Tests/RimWorldOtelExporter.Tests/` reference `Transport/` and `Models/` source directly without RimWorld assembly deps. They must cover:
- Correct Protobuf encoding
- All resource attributes present
- Metric names match dictionary above
- Auth header set when configured
- Batch contains expected datapoint count

Run: `dotnet test Tests/RimWorldOtelExporter.Tests/`

---

## Implementation Order (Recommended)

**Phase 1 — Core transport (testable without RimWorld)**
1. Download opentelemetry-proto and generate C# proto classes
2. Implement `OtlpSerializer` (builds OTLP Protobuf messages)
3. Implement `OtlpHttpSender` (HttpClient POST)
4. Implement `ExportQueue` (ConcurrentQueue + background Thread)
5. Write unit tests for all transport layer components

**Phase 2 — Mod scaffold**
6. `ModCore.cs` — `Mod` class + `ModSettings`
7. `ColonyTelemetryComponent.cs` — `GameComponent` heartbeat

**Phase 3 — Metric collectors (one at a time)**
8. `ColonistCollector` (mood, health, skills, needs, thoughts)
9. `ResourceCollector` (stockpile, wealth, food, silver)
10. `InfrastructureCollector` (power, temperature, buildings, fires)
11. `ThreatCollector` (threat points, faction goodwill)
12. `WorldCollector` (animals, growing season)

**Phase 4 — Harmony event patches**
13. `IncidentPatch`, `DeathPatch`, `ResearchPatch`, `MentalBreakPatch`, `TradePatch`, `RelationsPatch`

**Phase 5 — Grafana**
14. Build dashboard JSON (dashboard variables, annotation, all 32 panels)
15. Commit to `grafana/colony-overview.json`

**Phase 6 — Ops**
16. Write README with metric dictionary
17. Add Steam Workshop description + GitHub release workflow

---

## Complete Todo Checklist

### Phase 1: C# RimWorld Mod

#### Project Scaffold
- [ ] mod-1: Create mod folder structure: About/, Assemblies/, Source/, Defs/
- [ ] mod-2: Set up C# Class Library project targeting .NET Framework 4.7.2
- [ ] mod-3: Add Lib.Harmony NuGet reference; decide OTLP transport strategy
- [ ] mod-4: Implement ModSettings: OTLP endpoint URL, auth header value, export interval, per-category toggles

#### OTLP HTTP Sender
- [ ] mod-5: Implement OtlpHttpSender: POST to /v1/metrics and /v1/logs using System.Net.HttpClient
- [ ] mod-6: Implement Protobuf serialization using Google.Protobuf + generated classes from opentelemetry-proto
- [ ] mod-7: Build OTLP Resource with service attributes — set once per export, attached to every payload
- [ ] mod-8: Implement ConcurrentQueue + dedicated background Thread for non-blocking export
- [ ] mod-9: Implement exponential backoff + circuit breaker for unreachable endpoint

#### Core Collection Engine
- [ ] mod-10: Implement GameComponent (ColonyTelemetryComponent) as the collection heartbeat
- [ ] mod-11: Implement MetricBatch builder: accumulate all datapoints into one OTLP request per export cycle

#### Colonist Metrics
- [ ] mod-12: rimworld_colonists_total — gauge by colonist_type (free / prisoner / slave / guest)
- [ ] mod-13: rimworld_colonist_mood — per-colonist gauge with name + pawn_id labels
- [ ] mod-14: rimworld_colonist_health — health summary % + pain level per colonist
- [ ] mod-15: rimworld_colonist_skill{name, skill} — skill level (0–20) per pawn per skill
- [ ] mod-16: rimworld_colonist_need{name, need} — food/rest/recreation/comfort/joy (0–1)
- [ ] mod-17: rimworld_colonist_thoughts_negative_total{name} — count of active negative thoughts

#### Resource & Economy Metrics
- [ ] mod-18: rimworld_resource_stockpile{item_def, item_label, item_category} — all stored resources
- [ ] mod-19: rimworld_colony_wealth{wealth_type} — items / buildings / pawns / total
- [ ] mod-20: rimworld_food_days_remaining — estimated days of food at current consumption rate
- [ ] mod-21: rimworld_trade_silver_total — silver on hand

#### Infrastructure Metrics
- [ ] mod-22: rimworld_power_grid{grid_id} — production_w, consumption_w, battery_stored_wh, battery_max_wh
- [ ] mod-23: rimworld_temperature_outdoor and rimworld_temperature_room{room_role}
- [ ] mod-24: rimworld_buildings_total{building_def, building_category} and rimworld_beds_total
- [ ] mod-25: rimworld_fire_count and rimworld_filth_count — map hazard indicators

#### Threat & World Metrics
- [ ] mod-26: rimworld_storyteller_threat_points — current threat budget
- [ ] mod-27: rimworld_faction_goodwill{faction_name, faction_type} — diplomatic standing (-100 to 100)
- [ ] mod-28: rimworld_growing_season_active bool gauge + season label metric
- [ ] mod-29: rimworld_animals_tamed{animal_def} and rimworld_animals_wild{animal_def, is_predator}

#### Event Hooks → OTLP Logs
- [ ] mod-30: Harmony postfix on IncidentWorker.TryExecuteWorker → emit incident log record
- [ ] mod-31: Harmony postfix on Pawn.Kill → emit colonist/named-animal death log
- [ ] mod-32: Harmony postfix on ResearchManager.FinishProject → emit research completion log
- [ ] mod-33: Harmony postfix on MentalStateHandler.TryStartMentalState → emit mental break log
- [ ] mod-34: Harmony postfix on TradeSession.SetupWith → emit trade event log
- [ ] mod-35: Emit game lifecycle log on map load: game version, mod version, scenario, seed
- [ ] mod-36: Harmony postfix on Pawn_RelationsTracker.AddDirectRelation → emit relationship event log

### Phase 2: OTLP Gateway Connection

#### Gateway Auth & Connectivity
- [ ] otlp-1: Confirm OTLP HTTP endpoint URL and auth method (Bearer token / API key / mTLS)
- [ ] otlp-2: Confirm Mimir tenant/org ID — add X-Scope-OrgID header to metric requests
- [ ] otlp-3: Confirm Loki tenant header requirement for OTLP log ingestion
- [ ] otlp-4: Smoke test with curl before wiring the mod — validate auth and routing end-to-end
- [ ] otlp-5: Decide: direct gateway from mod vs local Alloy sidecar as relay

#### If Using Local Alloy Relay (Optional)
- [ ] otlp-6: Write minimal config.alloy: OTLP receiver → prometheusremotewrite to Mimir + Loki exporter
- [ ] otlp-7: Add attribute enrichment in Alloy: inject host.name and os.type automatically
- [ ] otlp-8: Run Alloy as a background service on the gaming machine

#### OTLP Schema Design
- [ ] otlp-9: Define and document metric naming convention: rimworld_<subsystem>_<metric>_<unit>
- [ ] otlp-10: Separate resource attributes (per-export) from metric attributes (per-datapoint)
- [ ] otlp-11: Define Loki label schema: keep labels low-cardinality, put details in log line fields
- [ ] otlp-12: Add mod.version as resource attribute for schema evolution traceability

### Phase 3: Grafana Dashboard

#### Dashboard Foundation
- [ ] dash-1: Create dashboard variables: $colony, $map, $colonist
- [ ] dash-2: Configure Loki annotation query — overlay game events on every time series panel
- [ ] dash-3: Structure rows: Overview / Colonists / Economy / Infrastructure / Threats / Events

#### Overview Row
- [ ] dash-4: Stat: Colonist count with sparkline + threshold colors
- [ ] dash-5: Stat: Colony Wealth with delta vs 1 hour ago
- [ ] dash-6: Stat: Threat Points — color-coded danger level
- [ ] dash-7: Stat: Food Days Remaining — most urgent survival metric
- [ ] dash-8: Time series: Colony Wealth over full campaign — THE colony story graph
- [ ] dash-9: Logs panel (Loki): live event feed — last 20 game events, color-coded

#### Colonists Row
- [ ] dash-10: Heatmap: colonist mood over time — rows=colonists, color=mood level
- [ ] dash-11: Bar gauge: current mood per colonist, sorted ascending (worst first)
- [ ] dash-12: Table: colonist skills matrix — colonists as rows, skills as columns, heatmap coloring
- [ ] dash-13: Time series: negative thought count per colonist — mental break early warning
- [ ] dash-14: Bar chart: colonist needs snapshot — all colonists per need category
- [ ] dash-15: Stat row: total active injuries / diseases / addictions colony-wide

#### Economy Row
- [ ] dash-16: Time series: key resources over campaign — steel, components, food, medicine, gold
- [ ] dash-17: Time series: resource rates — rate(stockpile[1h]) for food and medicine
- [ ] dash-18: Pie chart: wealth composition — items vs buildings vs pawns
- [ ] dash-19: Table: full resource inventory — all items, current count, 1h rate, sparkline

#### Infrastructure Row
- [ ] dash-20: Time series: power grid — production vs consumption vs battery, fill areas
- [ ] dash-21: Time series: temperature — outdoor + freezer + bedrooms over time
- [ ] dash-22: Stat: active fires — CRITICAL visual at > 0
- [ ] dash-23: Gauge: beds-to-colonists ratio

#### Threats & Events Row
- [ ] dash-24: Time series: threat points over full campaign with incident annotations
- [ ] dash-25: Bar chart: incident frequency by type from Loki (count_over_time per incident.def)
- [ ] dash-26: Time series: faction goodwill over campaign
- [ ] dash-27: Logs panel: death log (Loki, event_type=death) — the colony memorial
- [ ] dash-28: Stat: total colonist deaths (campaign cumulative)

#### Research & World Row
- [ ] dash-29: Logs panel: research timeline (Loki, event_type=research)
- [ ] dash-30: Time series: colonist population over campaign with death annotations
- [ ] dash-31: Bar chart: tamed animal population by species
- [ ] dash-32: Info stat row: season, biome, growing days remaining, storyteller, difficulty

### Phase 4: Packaging & Docs

#### Mod Distribution
- [ ] ops-1: Publish to Steam Workshop — description links to GitHub and Grafana dashboard import
- [ ] ops-2: GitHub release: compiled DLL, changelog, SHA256 checksums
- [ ] ops-3: Write metric dictionary in README: every metric name, type, labels, description
- [ ] ops-4: Export Grafana dashboard JSON and commit to repo

#### Reliability & Testing
- [ ] ops-5: Extract OtlpHttpSender to standalone test project — unit test serialization outside RimWorld
- [ ] ops-6: Test with common mod combinations: Combat Extended, Vanilla Expanded, Biotech, Anomaly
- [ ] ops-7: Add telemetry status indicator in mod settings: last export time, success/fail, payload size
- [ ] ops-8: Profile tick performance with RimWorld dev tools — verify < 0.1ms overhead when not exporting

---

## Common RimWorld API Reference

```csharp
// Colonists
PawnsFinder.AllMapsWorldAndTemporary_AliveOrDead
pawn.Faction == Faction.OfPlayer
pawn.needs.mood.CurLevel          // 0.0–1.0
pawn.needs.mood.thoughts.memories.Memories
pawn.health.summaryHealth.SummaryHealthPercent
pawn.health.hediffSet.PainTotal
pawn.skills.skills                // List<SkillRecord>
pawn.needs.AllNeeds               // List<Need>
pawn.ThingID                      // stable unique ID

// Resources
map.resourceCounter.GetCount(ThingDef)
map.wealthWatcher.WealthItems / WealthBuildings / WealthPawns / WealthTotal

// Power
map.powerNetManager.AllNets       // IEnumerable<PowerNet>
// per PowerNet: sum CompPowerTrader.PowerOutput, CompPowerBattery.StoredEnergy

// Temperature
map.mapTemperature.OutdoorTemp
room.Temperature                  // per Room

// Buildings
map.listerBuildings.allBuildingsColonist

// Threats
StorytellerUtility.DefaultThreatPointsNow(map)
Find.FactionManager.AllFactions

// Season
PlantUtility.GrowingSeasonNow(map.Tile)
GenLocalDate.Season(map.Tile).Label()
```
