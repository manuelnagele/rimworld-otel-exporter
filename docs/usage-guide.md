# Using the RimWorld OpenTelemetry Exporter

This guide walks you through setting up the mod end-to-end: installing it, connecting it to a metrics backend, and reading your colony data in Grafana.

---

## What the mod does

Once running, the mod silently collects data from your game every 15 seconds and streams it to a Grafana dashboard:

- **Colonist panel** — mood, health, skills, needs, and negative thought counts for every colonist, updated live
- **Resource panel** — every stockpiled item, wealth breakdown, food days remaining, silver
- **Infrastructure panel** — power grids (production vs consumption vs battery), room temperatures, fire count
- **Threats panel** — the storyteller's current threat budget, faction goodwill, growing season
- **Event log** — every raid, death, mental break, research completion, and trade as a log entry, overlaid on every graph as an annotation

The mod produces no sound, no in-game UI, and has negligible performance impact (< 0.1 ms per tick when idle).

---

## Prerequisites

| Requirement | Where to get it |
|-------------|-----------------|
| RimWorld 1.5 or 1.6 | Steam / GOG |
| [Harmony mod](https://steamcommunity.com/sharedfiles/filedetails/?id=2009463077) | Steam Workshop |
| A Grafana + metrics backend | See options below |

For the backend you have two options:

- **Grafana Cloud** (free tier, nothing to self-host — recommended for getting started)
- **Self-hosted** Grafana + Mimir + Loki (more control, requires running your own stack)

---

## Step 1 — Set up a backend

### Option A: Grafana Cloud (easiest)

1. Sign up at [grafana.com](https://grafana.com) — the free tier is sufficient
2. In your Grafana Cloud portal, go to **My Account → Stack → Details**
3. Note your **Prometheus remote_write URL** — it looks like:
   `https://prometheus-prod-XX-prod-XX-X.grafana.net/api/prom/push`
   You need the OTLP version instead:
   `https://otlp-gateway-prod-XX-prod-XX-X.grafana.net/otlp`
4. Go to **Security → API Keys → Create API key** with role `MetricsPublisher`
5. Copy the key — your auth header will be `Bearer <that key>`
6. Note your **Loki endpoint** and whether it needs a separate key

> **Tip:** The Grafana Cloud free tier gives you 10,000 active series and 50 GB of logs — more than enough for a single campaign.

### Option B: Local Grafana Agent / Alloy (relay mode)

If you run your own Grafana stack, or want to keep credentials off your gaming machine, use [Grafana Alloy](https://grafana.com/docs/alloy/latest/) as a local relay. The mod sends to unauthenticated `localhost:4318`; Alloy handles auth and forwarding.

1. Install Alloy on your gaming machine:
   - **macOS:** `brew install grafana/grafana/alloy`
   - **Windows:** download the installer from the Grafana releases page
2. Create a config file (save as `config.alloy`):

```alloy
otelcol.receiver.otlp "rimworld" {
  http { endpoint = "localhost:4318" }
}

otelcol.exporter.prometheus "mimir" {
  forward_to = [prometheus.remote_write.mimir.receiver]
}

prometheus.remote_write "mimir" {
  endpoint {
    url = "https://<your-mimir-endpoint>/api/prom/push"
    basic_auth {
      username = "<your-username>"
      password = "<your-api-key>"
    }
  }
}

otelcol.exporter.loki "loki" {
  forward_to = [loki.write.default.receiver]
}

loki.write "default" {
  endpoint {
    url = "https://<your-loki-endpoint>/loki/api/v1/push"
    basic_auth {
      username = "<your-username>"
      password = "<your-api-key>"
    }
  }
}

otelcol.processor.batch "default" {
  output {
    metrics = [otelcol.exporter.prometheus.mimir.input]
    logs    = [otelcol.exporter.loki.loki.input]
  }
}
```

3. Run Alloy:
   - **macOS:** `alloy run config.alloy`
   - **Windows:** `alloy run config.alloy` (or register as a service via Task Scheduler)
4. Leave the mod's endpoint at the default `http://localhost:4318` — no auth config needed in the mod

---

## Step 2 — Install the mod

### From Steam Workshop

1. Subscribe to the mod on the Steam Workshop (link TBD once published)
2. Launch RimWorld — the mod appears in your mod list
3. Enable it and make sure **Harmony** is above it in the load order
4. Restart if prompted

### Manual installation

1. Download the latest release from [GitHub Releases](../../releases)
2. Unzip into your RimWorld mods folder:
   - **macOS:** `~/Library/Application Support/Rimworld/Mods/`
   - **Windows:** `%USERPROFILE%\AppData\LocalLow\Ludeon Studios\RimWorld by Ludeon Studios\Mods\`
   - **Linux:** `~/.config/unity3d/Ludeon Studios/RimWorld by Ludeon Studios/Mods/`
3. The folder structure should look like:
   ```
   Mods/
   └── RimWorldOtelExporter/
       ├── About/
       │   └── About.xml
       └── Assemblies/
           ├── RimWorldOtelExporter.dll
           ├── Google.Protobuf.dll
           ├── System.Memory.dll
           ├── System.Buffers.dll
           └── System.Runtime.CompilerServices.Unsafe.dll
   ```
   The three `System.*` DLLs are Mono polyfills required at runtime — they must be present alongside the main DLL.
4. Enable the mod in-game with Harmony above it in load order

---

## Step 3 — Configure the mod in-game

1. Launch RimWorld and load or start a game
2. Go to **Options (the gear icon) → Mod Settings → RimWorld OTel Exporter**

You will see these settings:

| Setting | What to enter |
|---------|--------------|
| **OTLP Endpoint** | `https://otlp-gateway-prod-XX.grafana.net/otlp` (Grafana Cloud) or `http://localhost:4318` (Alloy relay) |
| **Authorization Header** | `Bearer glc_eyJ...` (your Grafana Cloud API key) — leave blank if using Alloy relay |
| **Org ID** | Your Grafana Cloud org ID, or `anonymous` for single-tenant self-hosted |
| **Export Interval** | How often to send data (default 15s, range 5–120s) |
| **Enable Colonists** | Toggle colonist mood/health/skill metrics |
| **Enable Resources** | Toggle stockpile/wealth metrics |
| **Enable Infrastructure** | Toggle power/temperature/building metrics |
| **Enable Events** | Toggle Harmony patches (raids, deaths, mental breaks, etc.) |
| **Enable World** | Toggle threat points, animals, growing season |

3. Click **Accept** (or close the settings window) to apply
4. Watch the **Export status** line at the bottom of the settings — within 15 seconds it should show:
   ```
   Last export: 8s ago  (4.2 KB)
   ```
   If it shows `FAILED`, see the troubleshooting section below.

---

## Step 4 — Import the Grafana dashboard

1. In Grafana, click the **+** icon → **Import dashboard**
2. Upload `grafana/colony-overview.json` from the mod's GitHub repo
3. On the import screen:
   - Set **Prometheus datasource** to your Mimir / Prometheus datasource
   - Set **Loki datasource** to your Loki datasource
4. Click **Import**

The dashboard opens with 6 collapsible rows. Set the **time range** to cover your full campaign (e.g. "Last 30 days") to see the whole colony arc.

### Setting up variables

At the top of the dashboard, set:
- **$colony** — select your colony name (populated automatically once data is flowing)
- **$colonist** — select a specific colonist for the drill-down panels

---

## What you're looking at

### Overview row (always visible)
- **Colonist count** — free colonists, with sparkline and colour threshold (red < 3)
- **Colony Wealth** — total wealth with trend arrow vs 1 hour ago
- **Threat Points** — the storyteller's current raid budget; turns red above 1000
- **Food Days** — estimated days of food remaining; turns red below 3
- **Wealth over time** — the campaign story graph; raid and death annotations overlay it

### Colonists row
- The **mood heatmap** shows every colonist's mood history — rows are colonists, colours go red (miserable) → green (happy). A colony-wide red band after a raid is striking.
- The **bar gauge** shows current mood sorted worst-first. Break threshold lines are drawn so you can see who is close.
- The **negative thoughts time series** is your early-warning panel — peaks here precede mental breaks by hours of real time.

### Economy row
- Watch **steel, components, and medicine** — three lines that tell you whether you're building, repairing, or falling behind.
- The **rate panel** (`rate(stockpile[1h])`) shows the direction: negative food rate means you're consuming faster than producing.

### Infrastructure row
- The **power panel** fills area for production and consumption — the moment the lines cross is a blackout event.
- The **freezer temperature** panel has an alert threshold at 0°C — above that, food spoils.
- **Fire count** is large and red at any value > 0.

### Threats row
- **Threat points over time** is the most interesting metric to watch over a long campaign — it rises with wealth, dips after each raid, then climbs again. The shape of this curve narrates the whole playthrough.
- The **death log** panel at the bottom is the colony memorial: every colonist and named animal lost, with cause and killer.

### Events (Loki annotations)
Raids, deaths, and mental breaks appear as **vertical lines across every panel** simultaneously — so you can see a raid on the wealth graph, the mood graph, the threat graph, all at once. This is what makes the dashboard feel like a living record of your colony.

---

## Tips

**Use the full time range.** Set "Last 90 days" or a custom range from your campaign start date to get the complete colony arc on the wealth and population graphs.

**Watch threat points, not just raids.** The threat budget climbing steadily is more interesting than the raid itself. A long plateau above 1000 means a big one is coming.

**The negative thoughts panel is the best panel.** Set up a Grafana alert on `rimworld_colonist_thoughts_negative_total > 8` for any colonist and you'll get a desktop notification before the mental break happens.

**Compare campaigns.** Use the `$colony` variable to switch between saves. Colony wealth curves from different campaigns on the same graph tell very different stories.

**Name your animals.** Named animals appear in the death log when they're killed. Unnamed animals don't — which is correct, losing a random elk isn't a story moment, but losing Bond the husky is.

---

## Troubleshooting

### "FAILED — connection refused"
The endpoint isn't reachable. Check:
- Is Alloy running (if using relay mode)?
- Is the OTLP endpoint URL correct? It should end in `/otlp`, not `/otlp/v1/metrics`
- Is there a firewall blocking port 4318 (Alloy) or 443 (Grafana Cloud)?

### "FAILED — 401 Unauthorized"
The auth header is wrong. In Grafana Cloud:
- The header must be `Bearer <token>` with the full token including `glc_`
- Make sure you copied the whole token — they're long

### "FAILED — 403 Forbidden"
Your API key was created with insufficient permissions. It needs at least `MetricsPublisher` role.

### No data in Grafana after a successful export
- Check the Org ID matches your Grafana Cloud stack — it's the numeric ID, not your username
- Confirm your Grafana datasource is pointing at the right Mimir/Prometheus URL
- Wait one full export cycle (up to 15 seconds) and then hard-refresh Grafana

### The mod went offline (circuit breaker)
If the endpoint was unreachable for 10 consecutive exports, the mod enters offline mode to prevent log spam. Re-open Mod Settings and click Accept — this resets the circuit breaker without restarting the game.

### Metrics are missing for some colonists
Some metrics require the colonist to be spawned on the current map. Colonists in caravans or on world travel are excluded from per-colonist panels but counted in the total.

### Performance concern
The export is fully asynchronous — the game tick never waits for HTTP. If you see any performance impact, increase the export interval to 60 seconds in Mod Settings.

---

## Data privacy note

All data is sent from your machine directly to whatever endpoint you configure. Nothing is sent to the mod author. The OTLP endpoint and auth token are stored in plain text in your RimWorld save data folder at:

- **macOS:** `~/Library/Application Support/Rimworld/Config/`
- **Windows:** `%USERPROFILE%\AppData\LocalLow\Ludeon Studios\RimWorld by Ludeon Studios\Config\`

Don't share your config file publicly if you've entered real API keys.
