# Daily colony map on the dashboard

The Command Center hub has a **🗺 Colony Map** panel that shows a full-map screenshot,
refreshed once per in-game day. The pipeline is intentionally decoupled from the mod:

```
Progress Renderer mod        upload-map-screenshot.sh          Grafana "Colony Map" panel
(renders full map → PNG) ──► (uploads newest → latest.png) ──► <img src=${map_image_url}>
        on the RimWorld PC            on the RimWorld PC              anywhere (phone, laptop)
```

Because you'll view the dashboard **remotely**, the image has to live at a public URL — a
local file server on your gaming PC wouldn't be reachable. GitHub is the easiest free host (no
bucket or credentials), but any host that gives a stable HTTPS URL works.

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

## 2. Get a public URL for the image

The uploader just needs a **stable HTTPS URL it can overwrite daily** (e.g. `…/latest.png`). Pick one
host below; each has a ready `UPLOAD_CMD` in `scripts/uploader.env.example`.

### GitHub — recommended (free, no bucket/credentials)

Use a **dedicated public repo** with a single *amended* commit, so git history never grows:

```bash
gh repo create rimworld-map --public --clone && cd rimworld-map
cp any.png latest.png && git add latest.png && git commit -qm map && git push -u origin main
gh auth setup-git      # lets a background `git push` authenticate over HTTPS (no SSH agent needed)
```

→ URL: `https://raw.githubusercontent.com/<you>/rimworld-map/main/latest.png`

The uploader then runs `cp … && git commit --amend && git push -f`, keeping the repo at exactly one
commit forever. **No-git variant:** publish as a **release asset** instead
(`gh release upload map latest.png --clobber`) — nothing enters git history and it only needs `gh`
(the most robust option for a headless launcher). URL:
`https://github.com/<you>/rimworld-map/releases/download/map/latest.png`.

### Object storage — Cloudflare R2 / S3 / Backblaze B2

Create a public bucket, configure `rclone` once (`rclone config` → type **S3**, provider **Cloudflare**),
and the image lives at `https://pub-xxxx.r2.dev/latest.png`. Free tiers are plenty.

### Your own homelab / server

Already run a box with a reverse proxy + TLS? Just `rsync`/`scp` the file into a served folder.

> **Notes:** all of these are HTTPS (required to embed on Grafana Cloud) and world-readable — don't
> include anything sensitive. `raw.githubusercontent.com`/Pages cache ~5–10 min, which is fine for a
> daily image (and the panel cache-busts with `?t=${__to}`). Avoid Imgur/ImgBB (a new URL per upload)
> and committing to your *main* project repo (history bloat — use the dedicated repo or a release asset).

## 3. Run the uploader

`scripts/upload-map-screenshot.sh` polls the Progress Renderer folder and runs your `UPLOAD_CMD` on
the newest PNG whenever it changes. Configure it once via a file:

```bash
mkdir -p ~/.config/rimworld-map
cp scripts/uploader.env.example ~/.config/rimworld-map/uploader.env
# edit that file: set SRC_DIR, and pick one UPLOAD_CMD (GitHub is uncommented by default)

chmod +x scripts/upload-map-screenshot.sh
scripts/upload-map-screenshot.sh          # foreground; Ctrl-C to stop. For auto-start see step 5.
```

`$SRC_FILE` is set to the newest PNG for each upload. You can also skip the config file and pass
`SRC_DIR=… UPLOAD_CMD=… scripts/upload-map-screenshot.sh` inline.

## 4. Point Grafana at it

Open the **Command Center** dashboard → the **Map image URL** variable (top of the dashboard) →
set it to your public URL, e.g. `https://pub-xxxx.r2.dev/rimworld/latest.png`. Save.

The Colony Map panel renders `<img src="${map_image_url}?t=${__to}">` — the `?t=` is the dashboard
end-time, so every dashboard refresh re-fetches the image and you always see the latest day.

## 5. Run it automatically (launcher)

So you don't have to start the uploader by hand each session. Both launchers read config from
`~/.config/rimworld-map/uploader.env` (step 3); run the `sed` from the repo root so `__REPO_DIR__`
resolves to the correct absolute path.

**macOS (launchd):**

```bash
sed "s|__REPO_DIR__|$PWD|" scripts/com.rimworld.map-uploader.plist \
  > ~/Library/LaunchAgents/com.rimworld.map-uploader.plist
launchctl load ~/Library/LaunchAgents/com.rimworld.map-uploader.plist
# logs: tail -f /tmp/rimworld-map-uploader.log
# stop: launchctl unload ~/Library/LaunchAgents/com.rimworld.map-uploader.plist
```

**Linux (systemd user service):**

```bash
mkdir -p ~/.config/systemd/user
sed "s|__REPO_DIR__|$PWD|" scripts/rimworld-map-uploader.service \
  > ~/.config/systemd/user/rimworld-map-uploader.service
systemctl --user daemon-reload && systemctl --user enable --now rimworld-map-uploader
# logs: journalctl --user -u rimworld-map-uploader -f
# (optional, so it runs even when you're logged out:  loginctl enable-linger "$USER")
```

On a background launcher there's no SSH agent, so for the GitHub path use the **release-asset**
`UPLOAD_CMD` or run `gh auth setup-git` (HTTPS) — both authenticate from the stored `gh` token.

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
