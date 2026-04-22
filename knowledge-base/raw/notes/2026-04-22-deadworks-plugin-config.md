---
date: 2026-04-22
task: scan deadworks config system
files:
  - ../deadworks/managed/DeadworksManaged.Api/Config/IConfig.cs
  - ../deadworks/managed/DeadworksManaged.Api/Config/PluginConfigAttribute.cs
  - ../deadworks/managed/DeadworksManaged.Api/Config/ConfigResolver.cs
  - ../deadworks/managed/DeadworksManaged.Api/Config/ConfigExtensions.cs
  - ../deadworks/managed/ConfigManager.cs
---

# Plugin configuration system

## Plugin side

Declare a writeable property on the plugin class marked with
`[PluginConfig]`:

```csharp
[PluginConfig]
public ScourgeConfig Config { get; set; } = new();
```

The config type can optionally implement `IConfig`, which is just a marker
requiring a `void Validate()` method:

```csharp
public class ScourgeConfig : IConfig {
    public float DurationSeconds { get; set; } = 15f;
    public void Validate() {
        if (DurationSeconds < 0.1f) DurationSeconds = 0.1f;
        // ...
    }
}
```

**`IConfig` is optional.** Without it, no validation pass runs. With it,
`Validate()` is called post-deserialization. If `Validate()` throws:
- First load: falls back to `Activator.CreateInstance(configType)` (defaults)
- Reload: reload returns `false`, **existing config stays in place**

## Host side — config path resolution

Config key is derived from the plugin's **C# class name** (not folder name):

```csharp
private static string GetConfigKey(IDeadworksPlugin plugin) =>
    plugin.GetType().Name;
```

File path: `configs/<ClassName>/<ClassName>.jsonc` relative to the managed
DLL dir (`managed/..`). Example: `ScourgePlugin` → `configs/ScourgePlugin/ScourgePlugin.jsonc`.

First load behaviour: if the file doesn't exist, the host **creates a
default config file** (serialized default-constructed instance) with a
header comment `// Configuration for <Name>\n{...}`. This is nice — plugins
always see a file they can edit.

## JSON options

```csharp
new JsonSerializerOptions {
    ReadCommentHandling = JsonCommentHandling.Skip,
    WriteIndented = true,
    PropertyNameCaseInsensitive = true
}
```

**Comments are stripped on read** — that's why the file extension is
`.jsonc`, not `.json`. `PropertyNameCaseInsensitive = true` means
`[JsonPropertyName]` is NOT required for camelCase JSON → PascalCase
properties — but the `TagPlugin` example uses `[JsonPropertyName]` anyway
for explicit control over nested types.

## Hot-reload flow

Trigger: `dw_reloadconfig` console command. Host flow:
1. Re-parse the file (errors → abort reload, keep old config)
2. Run `Validate()` if implementing `IConfig` (throws → abort reload)
3. Assign new instance to the `[PluginConfig]` property via reflection
4. Call `plugin.OnConfigReloaded()` — swallow exceptions with a log line

Canonical plugin response (`AutoRestartPlugin.cs:35-39`):

```csharp
public override void OnConfigReloaded() {
    _restartSequence?.Cancel();
    StartRestartSequence();
}
```

Cancel any timers that depended on old config values, re-read
`this.Config`, re-schedule.

## Extensions available on IDeadworksPlugin

```csharp
this.ReloadConfig()   // trigger reload programmatically
this.GetConfigPath()  // returns the .jsonc path, or null if missing
```

Both throw if the config system isn't initialized
(`ConfigResolver.ReloadConfig == null`).

## LockTimer implication

This repo's LockTimer stores its zone definitions in a
`zones.yaml` file, NOT via the `[PluginConfig]` system — LockTimer has its
own YAML loader. If migrating to the Deadworks config system, the config
class would be JSONC and the file would move to
`configs/LockTimerPlugin/LockTimerPlugin.jsonc`. (Not a suggestion — just
a note on how it would map.)
