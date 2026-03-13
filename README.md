# RimWorld OpenTelemetry Exporter

A RimWorld mod that streams live colony telemetry to your Grafana stack. Watch colonist mood, resource stockpiles, power grids, threat levels, and every raid, death, and research milestone — all in real time on a Grafana dashboard.

---

## What This Is

RimWorld tracks an enormous amount of state: colonist moods trending toward mental breaks, food supplies dwindling, power grids balancing on a knife's edge, threat budgets quietly growing until the next raid. Normally you only notice these things when they become crises.

This mod exports that state as OpenTelemetry metrics and logs, continuously, while you play. A companion Grafana dashboard turns your colony into a living observability target — the same way you'd monitor a production system, but for a sci-fi colony sim.

**What you can do with it:**
- See the entire arc of a campaign on a single wealth graph, with raids and deaths annotated on the timeline
- Spot colonists approaching mental breaks before they happen (mood + negative thought trends)
- Get alerted when the freezer drifts above 0°C or food drops below 3 days
- Review every death, trade, and research milestone as a log stream
- Compare campaigns across saves using colony name and map seed as dimensions

---

## Architecture

```
┌─────────────────────────────────────────────┐
│              RimWorld Process               │
│                                             │
│  GameComponent (heartbeat, every 15s)       │
│    ├── ColonistCollector                    │
│    ├── ResourceCollector                    │
│    ├── InfrastructureCollector              │
│    ├── ThreatCollector                      │
│    └── WorldCollector                       │
│                                             │
│  Harmony Patches (event-driven)             │
│    ├── Raids / Incidents                    │
│    ├── Colonist deaths                      │
│    ├── Research completions                 │
│    ├── Mental breaks                        │
│    ├── Trade sessions                       │
│    └── Relationship events                  │
│                                             │
│  Background Thread                          │
│    └── OtlpHttpSender ──────────────────────┼──► OTLP endpoint
└─────────────────────────────────────────────┘        │
                                                        │
                                          ┌─────────────▼──────────────┐
                                          │   Grafana Agent / Alloy    │
                                          │   (optional local relay)   │
                                          └──────┬──────────┬──────────┘
                                                 │          │
                                          ┌──────▼──┐  ┌───▼────┐
                                          │  Mimir  │  │  Loki  │
                                          │(metrics)│  │ (logs) │
                                          └──────┬──┘  └───┬────┘
                                                 │          │
                                          ┌──────▼──────────▼──────────┐
                                          │       Grafana Dashboard     │
                                          └────────────────────────────┘
```

Metrics flow as OTLP/HTTP Protobuf to `/v1/metrics`. Game events (raids, deaths, research, etc.) flow as OTLP logs to `/v1/logs`. Both are batched into a single export every 15 seconds (configurable).

The game tick thread never blocks on network I/O — a dedicated background thread drains a `ConcurrentQueue` and handles all HTTP calls.

---

## What Gets Exported

### Metrics (45+ gauges, collected every export cycle)

| Category | Metrics |
|----------|---------|
| **Colonists** | Total count by type (free/prisoner/slave/guest), mood, health %, pain, age, hediff counts by category (injury/disease/addiction/implant/chronic), skill levels with passion, need levels, negative thought count, mood break thresholds (minor/major/extreme) |
| **Resources** | Stockpile quantity per item def with category, colony wealth by type (items/buildings/pawns/total), food days remaining, silver on hand |
| **Infrastructure** | Power production/consumption/battery per grid, outdoor temp, room min & avg temp by role, room impressiveness by role, building counts by def & category, bed count, fire count, filth count |
| **Threats** | Storyteller threat points, faction goodwill per faction |
| **Combat** | Hostile pawns on map, downed colonists, colonists in mental state, drafted colonists, inspired colonists |
| **World** | Growing season active, game date (year/quadrum/day), tamed & wild animal counts by species, active map conditions (toxic fallout/volcanic winter/etc.), research completed count, current research project progress, caravan count, colony settlement count |

### Logs (event-driven, via Harmony patches)

| Event | Trigger | Severity |
|-------|---------|----------|
| Raid / Incident | Any IncidentWorker execution | WARN (hostile), INFO (neutral) |
| Colonist death | Any player-faction pawn killed | WARN |
| Research complete | Any research project finished | INFO |
| Mental break | Any colonist mental state start | WARN |
| Trade session | Any trader interaction | INFO |
| Relationship formed | Spouse, Lover, Bond, Rival, Fiance | INFO |
| Session start | Map load / new game | INFO |

---

## What's Being Built

The project is split into four phases:

### Phase 1 — C# RimWorld Mod ✅
The mod itself. A `GameComponent` fires every 15 seconds, runs all metric collectors, serializes to OTLP Protobuf, and enqueues for background HTTP delivery. Harmony patches intercept game events and emit log records. No full OpenTelemetry SDK — just `Google.Protobuf` and a focused raw OTLP/HTTP sender to avoid Mono/Unity runtime conflicts.

**36 items** — complete. See [v0.1.0 release](https://github.com/manuelnagele/rimworld-otel-exporter/releases/tag/v0.1.0).

### Phase 1.5 — Expanded Metrics ✅ (new in v0.2.0)
Extended all five collectors with 15+ additional metrics: game date (year/quadrum/day), full combat state (hostile pawns, downed, drafted, inspired, mental breaks), active map conditions as labeled gauges, research progress tracking, world exploration state (caravans, settlements), per-role room temperature (min & avg) and impressiveness, per-colonist age. See [v0.2.0 release](https://github.com/manuelnagele/rimworld-otel-exporter/releases/tag/v0.2.0).

### Phase 2 — OTLP Gateway Connection
Configuring and validating the connection between the mod and your Grafana infrastructure. Covers auth (Bearer token / API key), Mimir/Loki tenant headers, smoke testing with `curl`/`otel-cli`, and the choice between direct gateway or local Alloy relay.

**12 items** covering: endpoint auth, tenant config, schema design decisions, metric naming convention, Loki label strategy.

### Phase 3 — Grafana Dashboard ✅ (new in v0.2.0)
A production-ready dashboard suite of four files. Loki annotations overlay game events on every time series panel simultaneously — raids visible on the wealth graph, deaths on the mood graph. Dashboard variables let you switch between campaigns and drill into individual colonists.

**4 dashboards** — complete. See `grafana/` directory.

### Phase 4 — Packaging & Docs
Steam Workshop publication, campaign ID separation for multi-save tracking, dashboard verification against in-game reality, testing against common mod combinations (Combat Extended, Vanilla Expanded, Biotech, Anomaly), and performance profiling.

**10 items** covering: distribution, reliability, and usability validation.

---

## How to Enable It

### Prerequisites

- RimWorld 1.5 or 1.6
- [Harmony mod](https://steamcommunity.com/sharedfiles/filedetails/?id=2009463077) installed and enabled
- An OTLP-compatible metrics/logs backend — either:
  - **Grafana Cloud** (free tier works), or
  - **Self-hosted** Mimir + Loki + Grafana stack

### Option A — Direct to Gateway

The mod sends directly to your remote OTLP endpoint. Credentials are stored in mod settings XML on disk.

1. Install the mod (Steam Workshop or manual — see [Releases](../../releases))
2. In-game: **Options → Mod Settings → RimWorld OTel Exporter**
3. Set **OTLP Endpoint** — e.g. `https://otlp-gateway-prod-us-east-0.grafana.net/otlp`
4. Set **Auth Header** — e.g. `Bearer glc_eyJ...` (Grafana Cloud token)
5. Set **Org ID** — your Mimir/Loki tenant ID (or `anonymous` for single-tenant)
6. Toggle which categories to export
7. Click **Apply** — the mod will attempt its first export within 15 seconds

### Option B — Local Alloy Relay (Recommended for Shared Use)

Run [Grafana Alloy](https://grafana.com/docs/alloy/latest/) locally. The mod sends to unauthenticated `localhost:4318`; Alloy injects auth and forwards to your gateway. Users configure Alloy, not the mod.

1. Install Alloy on your gaming machine
2. Use the `config.alloy` from this repo (see `alloy/config.alloy` once created)
3. In mod settings, leave **OTLP Endpoint** as `http://localhost:4318` (default)
4. Alloy handles auth, routing, and attribute enrichment automatically

### Grafana Dashboard

Once metrics are flowing:

1. In Grafana: **Dashboards → Import**
2. Upload from the `grafana/` directory in this repo — start with `rimworld-colony-v2.json` (main hub), then import the three drilldowns
3. Select your Mimir/Prometheus datasource for metrics and Loki datasource for logs
4. Set the `$colony` variable to your colony name

| File | Purpose |
|------|---------|
| `rimworld-colony-v2.json` | **Colony Command Center** — main hub, all vital signs on one screen |
| `rimworld-colonist.json` | Per-colonist drilldown — full mood/health/skills/needs history |
| `rimworld-economy.json` | Economy deep-dive — stockpile trends, wealth composition, food runway |
| `rimworld-threats.json` | Combat & threats — threat budget, faction goodwill, event log |

---

## Building from Source

Requires .NET SDK 8+ and RimWorld installed.

```bash
# Clone
git clone git@github.com:manuelnagele/rimworld-otel-exporter.git
cd rimworld-otel-exporter

# macOS Steam (default — no env var needed)
# Windows Steam: set RIMWORLD_DIR="C:\Program Files (x86)\Steam\steamapps\common\RimWorld"
# Linux Steam:   export RIMWORLD_DIR="$HOME/.steam/steam/steamapps/common/RimWorld"

# Build mod DLL → outputs to Assemblies/
cd Source/RimWorldOtelExporter
dotnet build -c Release

# Run tests (no RimWorld install needed)
cd ../../Tests/RimWorldOtelExporter.Tests
dotnet test
```

The build automatically copies all required runtime DLLs to `Assemblies/`:

| DLL | Purpose |
|-----|---------|
| `RimWorldOtelExporter.dll` | The mod |
| `Google.Protobuf.dll` | OTLP Protobuf serialization |
| `System.Memory.dll` | Span/Memory polyfills for Mono |
| `System.Buffers.dll` | Buffer polyfills for Mono |
| `System.Runtime.CompilerServices.Unsafe.dll` | Required by System.Memory on Mono |

---

## Project Status

- [x] Project structure and build setup
- [x] OTLP transport layer (serializer, sender, queue with backoff + circuit breaker)
- [x] ModSettings UI (endpoint, auth, interval, per-category toggles, live status)
- [x] GameComponent heartbeat
- [x] Metric collectors — 45+ metrics across colonists, resources, infrastructure, combat, world
- [x] Harmony event patches — incidents, deaths, research, mental breaks, trades, relationships
- [x] Unit tests (12/12 passing)
- [x] Grafana dashboard suite — Colony Command Center + 3 drilldowns (`grafana/`)
- [x] v0.1.0 release (Phase 1 — mod core)
- [x] v0.2.0 release (expanded metrics + dashboard suite)
- [ ] Per-save campaign ID for multi-playthrough separation
- [ ] Dashboard verification against in-game reality
- [ ] Steam Workshop publication

See [`CLAUDE.md`](CLAUDE.md) for the full implementation checklist and technical reference.
