---
title: Plugin Configuration & Hot-Reload
type: entity
sources:
  - raw/notes/2026-04-22-deadworks-plugin-config.md
  - ../deadworks/managed/DeadworksManaged.Api/Config/IConfig.cs
  - ../deadworks/managed/DeadworksManaged.Api/Config/PluginConfigAttribute.cs
  - ../deadworks/managed/DeadworksManaged.Api/Config/ConfigResolver.cs
  - ../deadworks/managed/DeadworksManaged.Api/Config/ConfigExtensions.cs
  - ../deadworks/managed/ConfigManager.cs
related:
  - "[[plugin-api-surface]]"
  - "[[deadworks-runtime]]"
  - "[[examples-index]]"
  - "[[lock-timer]]"
created: 2026-04-22
updated: 2026-04-22
confidence: high
---

# Plugin Configuration & Hot-Reload

The `[PluginConfig]` / `IConfig` pattern. Plugins declare a writeable
property; the host loads JSONC from `configs/<ClassName>/<ClassName>.jsonc`
at load and on `dw_reloadconfig`.

## Plugin declaration

```csharp
[PluginConfig]
public ScourgeConfig Config { get; set; } = new();
```

- `[PluginConfigAttribute]` — simple marker, single use per class
- Property MUST be `get; set;` (writeable — host assigns via reflection)
- Initializer `= new()` provides defaults if the file is missing or
  parse fails

## Config type (`IConfig` is optional)

```csharp
public class ScourgeConfig : IConfig {
    public float DurationSeconds { get; set; } = 15f;
    public int DamageIntervalMs { get; set; } = 200;

    public void Validate() {
        if (DurationSeconds < 0.1f) DurationSeconds = 0.1f;
        if (DamageIntervalMs < 50)  DamageIntervalMs = 50;
    }
}
```

Without `IConfig`, no validation step runs. With it, `Validate()` is
called post-deserialize. Behaviour on `Validate()` throw:

- **First load**: fall back to `Activator.CreateInstance(configType)`
  (pure defaults)
- **Reload**: return `false` from `dw_reloadconfig`, **keep existing
  config in place**

**Style convention seen in examples: clamp in-place rather than throw.**
A bad config becomes a corrected-in-memory config rather than an error.

## File layout

Config path: `configs/<ClassName>/<ClassName>.jsonc` relative to
`managed/..` (i.e. `game/bin/win64/configs/`).

Critical: the key is the **plugin's C# class name**, NOT the folder name.

| Plugin class | Config path |
|--------------|-------------|
| `ScourgePlugin` | `configs/ScourgePlugin/ScourgePlugin.jsonc` |
| `ItemRotationPlugin` | `configs/ItemRotationPlugin/ItemRotationPlugin.jsonc` |
| `AutoRestartPlugin` | `configs/AutoRestartPlugin/AutoRestartPlugin.jsonc` |

This differs from `gamemodes.json` keys, which match the on-disk
**plugin folder name** (see [[plugin-build-pipeline]]). Two different
naming conventions; keep them straight.

### First-load auto-creation

If the file doesn't exist, the host creates a default file:

```
// Configuration for <Plugin.Name>
{
  // ... serialized default-constructed instance, indented ...
}
```

`JsonCommentHandling.Skip` lets the header comment coexist with valid
JSON — that's why the file extension is `.jsonc`, not `.json`.

### JSON options

```csharp
new JsonSerializerOptions {
    ReadCommentHandling = JsonCommentHandling.Skip,
    WriteIndented = true,
    PropertyNameCaseInsensitive = true
}
```

**`PropertyNameCaseInsensitive = true`** means `[JsonPropertyName]` is
NOT strictly required for camelCase JSON → PascalCase properties.
TagPlugin uses `[JsonPropertyName]` anyway for explicit control over
nested types — your call.

## Hot-reload flow (`dw_reloadconfig`)

1. Host re-reads the file; parse failure → abort reload (old config
   stays)
2. Run `Validate()` if implementing `IConfig`; throw → abort reload
3. Assign new instance to the `[PluginConfig]` property via reflection
4. Call `plugin.OnConfigReloaded()`; exceptions swallowed with a log line

## Plugin response: `OnConfigReloaded`

Override on `IDeadworksPlugin` / `DeadworksPluginBase`. Cancel anything
depending on old config values, re-read `this.Config`, re-schedule.

Canonical pattern (`AutoRestartPlugin`):

```csharp
public override void OnConfigReloaded() {
    _restartSequence?.Cancel();
    StartRestartSequence();
}
```

## Extensions on `IDeadworksPlugin`

```csharp
this.ReloadConfig()   // trigger reload programmatically; returns bool
this.GetConfigPath()  // returns the .jsonc path, or null if missing
```

Both throw `InvalidOperationException` if the config system isn't
initialized (shouldn't happen during normal plugin lifecycle, but
matters if called from static constructors).

## Not everything uses `[PluginConfig]`

This repo's [[lock-timer]] plugin stores zone definitions in its own
`zones.yaml` file via a YAML loader — not through this system. That's
fine; `[PluginConfig]` is opt-in. If migrated, the config class would be
JSONC and the file would move to `configs/LockTimerPlugin/LockTimerPlugin.jsonc`.
