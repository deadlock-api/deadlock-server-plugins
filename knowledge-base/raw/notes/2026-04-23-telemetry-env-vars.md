---
date: 2026-04-23
task: scan ../deadworks for new knowledge — telemetry stack details
files:
  - ../deadworks/managed/Telemetry/DeadworksTelemetry.cs
  - ../deadworks/managed/Telemetry/DeadworksMetrics.cs
  - ../deadworks/managed/Telemetry/DeadworksTracing.cs
  - ../deadworks/managed/Telemetry/NativeEngineLoggerProvider.cs
  - ../deadworks/managed/Telemetry/PluginLoggerRegistry.cs
  - ../deadworks/managed/DeadworksManaged.Api/Logging/LogResolver.cs
  - ../deadworks/managed/DeadworksConfig.cs
commits:
  - 224d660 ("add structured logging, metrics, and traces via Microsoft.Extensions.Logging + OpenTelemetry")
---

The `[[deadworks-runtime]]` wiki page currently attributes the telemetry
rework to commit `deb8ff2`. That's wrong — the actual commit is
**`224d660`** (2026-04-14). The `deb8ff2` SHA doesn't exist in
`../deadworks/`.

## Telemetry config — env vars override JSONC

All telemetry settings live under a `telemetry:` block in
`deadworks.jsonc` (`DeadworksConfig.cs`). Env vars take precedence over
JSONC values when both set, checked by
`DeadworksTelemetry.Initialize()` via `GetEnvString` / `GetEnvBool`
(`DeadworksTelemetry.cs:31-36`):

| Env var | JSONC key | Default | Notes |
|---------|-----------|---------|-------|
| `DEADWORKS_TELEMETRY_ENABLED` | `enabled` | `false` | master gate |
| `DEADWORKS_OTLP_ENDPOINT` | `otlp_endpoint` | `http://localhost:4317` | |
| `DEADWORKS_OTLP_PROTOCOL` | `otlp_protocol` | `grpc` | `http/protobuf` also accepted |
| `DEADWORKS_SERVICE_NAME` | `service_name` | `deadworks-server` | |
| `DEADWORKS_LOG_LEVEL` | `log_level` | `Information` | standard `LogLevel` enum names |

JSONC-only (no env override):
- `export_interval_ms` = 15000
- `enable_traces` = true
- `enable_metrics` = true
- `trace_sampling_ratio` = 1.0 (1.0 disables sampling; <1.0 wires `TraceIdRatioBasedSampler`)

## Dual-sink logging

Logger factory always adds `NativeEngineLoggerProvider` (writes to the
Source 2 game console via an unmanaged callback pointer — the same
channel legacy `Console.WriteLine` used). OTLP log exporter is added
**only** when `DEADWORKS_TELEMETRY_ENABLED=true`. So you always get
engine-console logs; OTLP is opt-in.

`NativeEngineLogger` formats messages as
`[{category}] {prefix}: {message}` where prefix maps:
- `Trace→trce`, `Debug→dbug`, `Information→info`,
- `Warning→warn`, `Error→fail`, `Critical→crit`.

## Plugin-facing access

`DeadworksPluginBase.Logger` (protected property) returns the plugin's
`ILogger`, resolved via `LogResolver.Resolve = PluginLoggerRegistry.Get`.
Category name is **`Plugin.{plugin.Name}`** (`PluginLoggerRegistry.cs:22`).
Throws `InvalidOperationException` if accessed outside OnLoad..OnUnload.

Plugins can also create their own categorized loggers indirectly — but
`DeadworksTelemetry.CreateLogger` is `internal`, so the only first-class
entrypoint is `this.Logger` on `DeadworksPluginBase`.

## Metric instruments (19 on `Meter("Deadworks.Server")`)

Plugin lifecycle: `deadworks.plugins.loaded`, `.unloaded`, `.load_errors` (Counter),
`.load_duration_ms` (Histogram), `.active` (ObservableGauge).

Players: `deadworks.players.connections_total`, `.disconnections_total`,
`.connections_rejected` (Counter), `.count` (ObservableGauge).

Frame: `deadworks.frame.duration_ms` (Histogram).

Timer: `deadworks.timers.tasks_per_frame` (Histogram), `.errors` (Counter).

Heartbeat: `deadworks.heartbeat.sent_total`, `.failed_total` (Counter),
`.duration_ms` (Histogram).

Events/Chat/Commands: `deadworks.events.dispatched_total`,
`.handler_errors`, `deadworks.chat.messages_total`,
`deadworks.commands.dispatched_total` (all Counter).

## Tracing

Single `ActivitySource` — `Deadworks.Server`. Spans are created **only**
for infrequent lifecycle events (plugin load, client connect, heartbeat),
never per frame — per-frame activities would flood the exporter.

## Shutdown

`DeadworksTelemetry.Shutdown()` disposes tracer, meter, and logger
providers in that order. Called from host shutdown path. No plugin
action needed.

## Gotcha

`NativeEngineLoggerProvider` needs `NativeLogCallback.Callback` to be
set *before* `DeadworksTelemetry.Initialize()` runs, otherwise the
native-sink is skipped (check at `DeadworksTelemetry.cs:64-68`). The
bootstrap order is enforced by the host, but if a plugin somehow
triggered `Initialize` during `OnLoad` before the native callback
arrived, logs would silently drop.
