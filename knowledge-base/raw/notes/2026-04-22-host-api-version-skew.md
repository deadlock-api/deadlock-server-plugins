---
date: 2026-04-22
task: Diagnose instant-crash on any [Command] chat invocation (`!startwaves`)
files: [TrooperInvasion/TrooperInvasion.cs, ../deadworks/managed/PluginLoader.ChatCommands.cs, ../deadworks/managed/Commands/CommandRegistration.cs, ../deadworks/managed/DeadworksManaged.Api/Events/ChatCommandContext.cs]
---

Any `!cmd` chat message crashed `deadworks.exe` instantly with only a minidump
— no managed exception visible in stdout. The plugin's `[Command]` handler
never ran (verified with a file-flushing breadcrumb log — only the `OnLoad`
breadcrumb appeared, never `startwaves:enter`). So the crash was inside the
DeadworksManaged host's chat dispatch, before the handler was invoked.

**Root cause: host/Api version skew in the deployed `managed/` folder.**
The two halves of DeadworksManaged are independently built DLLs:

- `DeadworksManaged.Api.dll` — plugin-facing API, referenced by every plugin csproj
- `DeadworksManaged.dll` — the host that loads plugins and dispatches events

Both must be built from the same sibling `deadworks` source tree. On this
machine the game's `C:\Program Files (x86)\Steam\steamapps\common\Deadlock\game\bin\win64\managed\`
had:

- `DeadworksManaged.Api.dll` dated Apr 22 (new — the plugin-facing API csproj was
  rebuilt as a side-effect of working on plugins)
- `DeadworksManaged.dll` dated Apr 20 (stale — the host hadn't been rebuilt with
  the same source generation)

`ChatCommandContext`'s constructor had been changed from `(ChatMessage, string,
string[])` → `(ChatMessage, string, string[], char prefix)` between those two
dates. The deployed host (Apr 20) still IL-references the 3-arg ctor; the newer
Api dll only exposes the 4-arg version. First chat command invocation →
`System.MissingMethodException: Method not found: 'Void
DeadworksManaged.Api.ChatCommandContext..ctor(DeadworksManaged.Api.ChatMessage,
System.String, System.String[])'` thrown from `PluginLoader.DispatchChatMessage`
→ unhandled → native-crash-with-minidump.

**Fix**: rebuild the host from the sibling repo and deploy the *entire*
`bin/Debug/net10.0/` contents (not just `DeadworksManaged.dll` — the host has
runtime deps on `Microsoft.Extensions.Logging.Abstractions.dll`,
`Microsoft.Extensions.Options.dll`, `OpenTelemetry.dll`, etc. that the first
copy-pass missed and caused a *second* crash at boot with
`Could not load file or assembly 'Microsoft.Extensions.Logging.Abstractions,
Version=9.0.0.0'`).

    dotnet build /mnt/c/Users/raima/RiderProjects/deadworks/managed/DeadworksManaged.csproj -c Debug
    cp /mnt/c/Users/raima/RiderProjects/deadworks/managed/bin/Debug/net10.0/*.{dll,pdb,json} \
       "/mnt/c/Program Files (x86)/Steam/steamapps/common/Deadlock/game/bin/win64/managed/"

**Diagnostic gotcha**: managed exception messages in Windows minidumps are
stored as UTF-16, not UTF-8. `strings /tmp/dump.mdmp | grep ...` misses them.
Use `strings -e l -n 15 /tmp/dump.mdmp | grep -i 'method not found\|missing'`
(`-e l` = 16-bit little-endian). The full `MissingMethodException.Message` with
the exact method signature is recoverable that way without needing WinDbg.

**Generalisation**: every `[Command]`-using plugin is affected simultaneously
when the host/Api pair drifts — not plugin-specific. Symptoms: every chat
command crashes, OnLoad still logs, file breadcrumbs confirm handler never
entered. Diff `managed/` file timestamps before blaming the plugin.
