# AI Notes — SCTelemetry

Notes for AI assistants working in this repo. Not part of the product.

## Inspiration / Reference

**TheBanjolorian/7hills-link** (`~/git/TheBanjolorian/7hills-link`) was used as reference when building this project.
- Also a Star Citizen gRPC telemetry client (Python, desktop app, streams to 7hills.space)
- The integrator's protobuf schema extraction logic was ported from its `protoset.py`
- Key idea: reads API definitions from `StarCitizen.exe` at runtime so it stays working after patches

## Repo Layout

```
integrator/   — SCTelemetry Integrator (.NET 9, Windows, zero NuGet, single-exe release)
web/          — SCTelemetry Web (Node.js, zero deps, full website)
TouchPortal/  — JSON Fetcher over HTTP plugin for Touch Portal
docs/         — screenshots
```

## Key Facts

- gRPC endpoint: `127.0.0.1:50051` (game-local only, never exposed to network)
- Releases built by GitHub Actions on `v*` tag push → single-file `SCTelemetry-Integrator.exe` (win-x64)
- Web: Discord OAuth2 login; config in JSON file; telemetry data in-memory only (gone on restart)
- Web live at: `sctelemetry.dodoslav.eu` (reverse-proxied from AS2 `127.0.0.4:8080`)
- Schema extraction ported from 7hills-link's approach; cache in `%LOCALAPPDATA%\ScTelemetry`

## gRPC API — read + write

Three methods on `sc.game.telemetry.v1.GameTelemetryService`:

| Method | Kind | Implemented |
|---|---|---|
| `TelemetryStream` | server-streaming | yes — `GameClient.PullFrameAsync` |
| `ListTriggerableActions` | unary | yes — `GameClient.ListActionsRawAsync` → `Worker.ListActionsJsonAsync` |
| `TriggerGameAction` | unary | yes — `GameClient.TriggerActionRawAsync` → `Worker.TriggerActionAsync` |

**Trigger actions require** `server.game.telemetry.trigger.actions.enabled = 1` in `user.cfg` + game restart.

**LocalServer routes** (when local server is enabled):

| Method | Path | Description |
|---|---|---|
| GET | `/` | JSON index of all available endpoints |
| GET | `/api/v1/telemetry` | Latest cached decoded frame; `?path=a.b.c` for a single value |
| POST | `/api/v1/telemetry` | Pull a fresh frame from the game right now; returns the new frame |
| GET | `/api/v1/actions` | `ListTriggerableActions` response as JSON |
| POST | `/api/v1/action` | `{ "action": "v_deploy_landing_system", "hold_ms": 0 }` → `{ "status": "OK" }` |

**Status values** from `TriggerGameAction`: `OK`, `FILTERED`, `MAP_INACTIVE`, `FAILED`

**Full action list** (112 actions, Nov 2026 PTU) in `~/git/TheBanjolorian/7hills-link/docs/TELEMETRY.md` §4.

## Event Log (JSON Lines)

When enabled, every telemetry pull and action trigger is appended to a `.jsonl` file — one JSON object per line:

```json
{"ts":"2026-11-27T14:30:00.123+01:00","source":"interval","status":"OK","res":"{...frame...}"}
{"ts":"2026-11-27T14:30:05.456+01:00","source":"POST /api/v1/action","req":"{\"action\":\"v_deploy\"}","status":"OK","res":"{\"status\":\"OK\"}"}
```

**`source` values:**

| source | Trigger |
|---|---|
| `interval` | automatic pull from the interval loop |
| `UI pull` | "Pull now" button in the app |
| `POST /api/v1/telemetry` | HTTP client called the force-pull endpoint |
| `POST /api/v1/action` | HTTP client triggered a game action |

**Config keys** (INI file):

| Key | Default | Description |
|---|---|---|
| `event_log` | `false` | Enable/disable |
| `event_log_path` | `%AppData%\SCTelemetry\events.jsonl` | File path (empty = default) |
| `event_log_max_mb` | `10` | Delete and restart when file exceeds this size |

Configured in the **Log** tab of the UI. The enable toggle is live; path/size require Save.

## Development Notes

- Integrator can be compile-checked on Linux (`~/.dotnet`, net9.0-windows + `EnableWindowsTargeting`)
- GUI cannot be run on Linux — user tests on AW1 (Windows gaming PC)
- Web: `ALLOW_DEV_LOGIN=1` env var enables `/dev-login` for local testing without Discord
