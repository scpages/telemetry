# JSON Fetcher over HTTP — Touch Portal plugin

A small, zero-dependency Touch Portal plugin that **polls an HTTP endpoint, extracts
any values from the JSON by path, and exposes each as a live Touch Portal state** you
can show on a button. Written for the SCTelemetry Integrator's local HTTP server, but
works with any JSON-over-HTTP endpoint.

## Install

Download `JsonHttpFetcher.tpp` from the repo's
[Releases](../../releases), then in Touch Portal: **Settings → Import plug-in…** →
pick the file. It appears as **JSON Fetcher over HTTP**.

## Configure

**Settings → Plug-ins → JSON Fetcher over HTTP**

| Setting | Meaning |
|---|---|
| **Endpoint URL** | the HTTP URL returning JSON (e.g. `http://127.0.0.1:8080`) |
| **Refresh Seconds** | poll interval (1–3600) |
| **Values** | one `Label=json.path` per line (space/comma/`;` also work) |

`Values` maps JSON paths to named states. Dots for nesting, numbers for array index:

```
Balance=actor.state.uec_balance
Health=actor.vitals.health.current
Heart=actor.vitals.heart_rate
```

Each becomes a state `jsonhttpfetcher.<label>` under **States of Plug-ins → JSON Fetcher**.

## Show a value on a button

Buttons don't evaluate plugin states in static text, and an "on change" event won't
fire for values that rarely change — so use the **Dynamic Text Updater** event:

1. Button → **On Event** → add **Dynamic Text Updater**.
2. Click **`+`** → **States of Plug-ins → JSON Fetcher → <label>** (inserts the token).
3. Mix in static text, e.g. `${value:jsonhttpfetcher.balance} aUEC`.
4. Save, then refresh the page on the device.

> The desktop editor shows the raw `${value:…}` token; the real value renders on the
> connected device.

## Build locally

```sh
dotnet publish JsonHttpFetcher.csproj -c Release -r win-x64 --self-contained \
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish
```

Then package the `.tpp` (a zip with a single top-level `JsonHttpFetcher/` folder):

```
JsonHttpFetcher/
├─ entry.tp
└─ JsonHttpFetcher.exe
```

CI (`.github/workflows/release.yml`) does this automatically on a `v*` tag and
attaches `JsonHttpFetcher.tpp` to the GitHub Release.

## Notes

- Talks to Touch Portal over its local socket `127.0.0.1:12136` (pair → `createState`
  per value → `stateUpdate` each poll). States are created at runtime because TP does
  not reliably surface states declared statically in `entry.tp`.
- `HttpClient` uses `UseProxy = false` so a Windows system/WPAD proxy doesn't break
  localhost requests.
- Writes `JsonHttpFetcher.log` next to the exe (plugin folder) for troubleshooting.
- Version is a single integer in both `Program.cs` (`Version`) and `entry.tp`
  (`version`); bump both together.
