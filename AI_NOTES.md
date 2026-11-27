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

## Development Notes

- Integrator can be compile-checked on Linux (`~/.dotnet`, net9.0-windows + `EnableWindowsTargeting`)
- GUI cannot be run on Linux — user tests on AW1 (Windows gaming PC)
- Web: `ALLOW_DEV_LOGIN=1` env var enables `/dev-login` for local testing without Discord
