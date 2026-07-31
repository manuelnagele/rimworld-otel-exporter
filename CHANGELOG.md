# Changelog

## v0.3.0-rc1 (release candidate)

Focus: make the mod load cleanly on RimWorld 1.6, keep telemetry flowing during an
all-day session, and add the "act now" signals that make a second-screen companion
genuinely useful.

### Fixed (mod would previously throw a red error at load)
- **MentalBreakPatch** targeted `Pawn_MindState.TryStartMentalState`, which does not exist —
  the method is on `MentalStateHandler`. Harmony threw inside `PatchAll()`, aborting the whole
  event-patch batch. Retargeted and now reads the pawn via the handler's private field.
- **TradePatch** postfix bound `ITrader trader`, but Harmony matches postfix params by name and
  the real argument is `newTrader` — a second throw in the same `PatchAll()`. Renamed; gift-mode
  caravans are no longer logged as trades.
- Harmony patches now apply per-class with isolation, so one bad target can never take down the
  rest of the event pipeline again.
- `About.xml` no longer claims 1.5 (the DLL uses 1.6-only APIs and hard-fails on 1.5).
- `service.version` / `mod.version` now report the real version instead of `0.0.0`.
- Research progress now divides by `CostApparent` (honors research multipliers), not `baseCost`.
- Food "days remaining" now counts only colony mouths (colonists, slaves, prisoners, tamed
  animals) — no longer inflated by raiders and visitors.
- `rimworld_colonists_total{free}` is now current-map scoped to match the per-colonist series.

### Added — reliability
- Export heartbeat moved to `GameComponentUpdate` so metrics and events keep flowing **while the
  game is paused** (raids, mental breaks and deaths usually happen while paused).
- Automatic circuit-breaker recovery: after going offline the exporter re-probes every ~3 min
  instead of latching until you reopen Mod Settings.
- Settings now surface real failure state (last error, consecutive failures, offline + auto-retry
  countdown, queue depth) and a **Test connection** button that POSTs a tiny payload and shows the
  HTTP status/response body.
- Per-save **campaign identity**: a persistent GUID is emitted as `service.instance.id` and as a
  `campaign_id` / `colony_name` datapoint label on every metric and log, so multiple colonies no
  longer collide and are filterable in PromQL/LogQL.

### Added — telemetry (14 new metrics)
- `rimworld_colony_health_score` (+ `_component{component}`) — the at-a-glance 0–100 hero score.
- `rimworld_colonist_mood_margin{name}` and `rimworld_colonists_near_break_total{severity}`.
- `rimworld_colonists_bleeding_total`, `rimworld_colony_bleed_rate_total`,
  `rimworld_colonists_tend_needed_total`, `rimworld_colonists_losing_immunity_total`.
- `rimworld_colonists_hungry_total{level}`.
- `rimworld_medicine_total{tier}`, `rimworld_food_meals_total`.
- `rimworld_colonists_combat_ready_total`, `rimworld_raid_active`.

### Dashboards
- Rebuilt the **Command Center** hub for a second screen: in-game date/season header, a colony-
  health hero tile, a row of high-contrast background-colored danger tiles (raid, fire, near-break,
  bleeding, needs-doctor, downed, food days), story time-series, supplies/power, and a live event feed.
- Fixed the broken `rimworld_temperature_room` query; drilldowns now use the emitted `_avg`/`_min`.
- **Per-run separation:** every series/log carries a readable-unique `run` label (`"<colony> #<id>"`);
  the dashboards single-select one **$run** so the board shows exactly one playthrough and stays clean
  even if two colonies share a name.
- **Daily colony map:** a 🗺 Colony Map panel on the hub shows a full-map screenshot (produced by the
  Progress Renderer mod, uploaded to a public `latest.png` by `scripts/upload-map-screenshot.sh`, and
  pointed at via the `$map_image_url` dashboard variable — no plugin, cache-busted per refresh).
  See `docs/map-screenshot.md`.
- Colony-scoped every query and Loki annotation by the selected run.
- Loki annotations/logs no longer use `| json` on a plain-text body — they filter `event_type`
  (and `colony_name`) as structured metadata.
- Cross-dashboard nav links; deleted the stale v1 `rimworld-colony.json`.

### Ops
- `alloy/config.alloy` (+ `.env.example`) for the local Alloy relay → Grafana Cloud path.
- `grafana/alerts.yaml` — a 9-rule Grafana alert pack (raid, near-break, bleeding, tend-needed,
  food, fire, out-of-medicine, immunity race, low battery).
- Stopped tracking the build `.pdb`; added serializer/identity unit tests (16 total).
