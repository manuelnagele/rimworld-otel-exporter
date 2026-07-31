# Daily colony map on the dashboard

The Command Center hub has a **🗺 Colony Map** panel that shows a full-map screenshot,
refreshed once per in-game day. The pipeline is intentionally decoupled from the mod:

```
Progress Renderer mod        upload-map-screenshot.sh          Grafana "Colony Map" panel
(renders full map → PNG) ──► (uploads newest → latest.png) ──► <img src=${map_image_url}>
        on the RimWorld PC            on the RimWorld PC              anywhere (phone, laptop)
```

Because you'll view the dashboard **remotely**, the image has to live at a public URL — a
local file server on your gaming PC wouldn't be reachable. The steps below use object storage
(Cloudflare R2 is free and simple), but any host that gives a stable public URL works.

---

## 1. Render the map — Progress Renderer

Install **[Progress Renderer](https://steamcommunity.com/sharedfiles/filedetails/?id=1608237956)**
from the Steam Workshop (or GitHub), enable it, then in its mod settings:

- **Rendering interval:** every `1` day (or `1` per season if you want lighter output)
- **File format:** PNG
- **Encode quality / pixel size:** whatever looks good — ~10–20 px/cell gives a crisp map
- Note the **output folder**. Defaults:
  - macOS: `~/Library/Application Support/RimWorld/RenderProgress`
  - Windows: `%USERPROFILE%\AppData\LocalLow\Ludeon Studios\RimWorld by Ludeon Studios\RenderProgress`
  - Linux: `~/.config/unity3d/Ludeon Studios/RimWorld by Ludeon Studios/RenderProgress`

Progress Renderer writes one timestamped PNG per render into that folder.

> This mod (RimWorld OTel Exporter) already emits the in-game date (`rimworld_game_*`) so the
> dashboard shows which day the current map corresponds to.

## 2. Get a public URL — Cloudflare R2 (example)

1. Create an R2 bucket (e.g. `rimworld-map`) and enable **public access** (r2.dev URL, or attach a
   custom domain). You'll get a base URL like `https://pub-xxxx.r2.dev`.
2. Configure `rclone` once: `rclone config` → new remote `r2`, type **S3**, provider **Cloudflare**,
   with your R2 access key/secret and endpoint. (`aws s3` or Backblaze B2 work the same way.)
3. Your image will live at `https://pub-xxxx.r2.dev/rimworld/latest.png`.

Set a short cache lifetime on the object so browsers pick up the daily change (R2: a
`Cache-Control: public, max-age=60` on upload; the panel also cache-busts via the URL — see step 4).

## 3. Run the uploader

`scripts/upload-map-screenshot.sh` polls the Progress Renderer folder and uploads the newest PNG
to a stable `latest.png` whenever it changes:

```bash
chmod +x scripts/upload-map-screenshot.sh

SRC_DIR="$HOME/Library/Application Support/RimWorld/RenderProgress" \
UPLOAD_CMD='rclone copyto "$SRC_FILE" r2:rimworld-map/rimworld/latest.png --header-upload "Cache-Control: public, max-age=60"' \
INTERVAL=60 \
scripts/upload-map-screenshot.sh
```

Leave it running while you play (background it, or wrap in a `launchd`/`systemd`/Task Scheduler job).
`$SRC_FILE` is set to the newest PNG for each upload; swap `UPLOAD_CMD` for `aws s3 cp`, `scp`, etc.

## 4. Point Grafana at it

Open the **Command Center** dashboard → the **Map image URL** variable (top of the dashboard) →
set it to your public URL, e.g. `https://pub-xxxx.r2.dev/rimworld/latest.png`. Save.

The Colony Map panel renders `<img src="${map_image_url}?t=${__to}">` — the `?t=` is the dashboard
end-time, so every dashboard refresh re-fetches the image and you always see the latest day.

---

## Alternatives

- **No object storage:** expose the folder with a tunnel instead — run a static server
  (`python3 -m http.server 8080` in the render folder) plus `cloudflared tunnel --url http://localhost:8080`,
  and set the Map image URL to the tunnel URL. No account/bucket needed.
- **Nicer panel:** install the **Volkov Labs "Business Image"** panel plugin and point it at the same
  URL if you want zoom/rotation controls instead of the plain Text panel.

## Caveat

The panel always shows the **currently-active colony's** latest map (whatever Progress Renderer last
wrote). It is not tied to the `$run` selector's history, so if you switch `$run` to an older campaign
the map still shows the colony you're currently playing.
