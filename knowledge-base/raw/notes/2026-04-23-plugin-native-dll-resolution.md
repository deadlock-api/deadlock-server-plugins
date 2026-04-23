---
date: 2026-04-23
task: scan ../deadworks for new knowledge
files:
  - ../deadworks/managed/PluginLoader.cs
commits:
  - f9a876c ("fix: resolve native DLLs for plugins in isolated AssemblyLoadContext")
---

Upstream commit `f9a876c` (2026-04-14) adds a `LoadUnmanagedDll` override
to `PluginLoadContext` in `../deadworks/managed/PluginLoader.cs:39-48`,
using the plugin's `AssemblyDependencyResolver` to resolve native libraries
from its own `runtimes/<rid>/native/` directory:

```csharp
protected override nint LoadUnmanagedDll(string unmanagedDllName)
{
    var path = _resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
    if (path != null)
        return NativeLibrary.Load(path);
    return nint.Zero;
}
```

**Why it matters for plugin authors:** before this fix, plugins that
bundled native dependencies (example in commit message:
`Microsoft.Data.Sqlite` / `e_sqlite3.dll`) failed at runtime with
`DllNotFoundException` because the CLR's default unmanaged resolver only
probes the process directory (`…/game/bin/win64/`), not the plugin's own
`deps.json`-indexed native directory.

Post-fix, managed assembly resolution (`Load`) uses `_sharedAssemblies` first
(host-identity types like `IDeadworksPlugin`) then falls through to the
per-plugin resolver. Native resolution (`LoadUnmanagedDll`) goes straight
through the per-plugin resolver — there's no "shared native" mechanism.

Existing wiki coverage in `[[deadworks-plugin-loader]]` describes the
managed `Load` override but not `LoadUnmanagedDll`. The `[[deadworks-runtime]]`
page similarly omits it. Worth mentioning when documenting what plugin
authors can bundle.

Not currently exercised by any plugin in `deadlock-server-plugins/` —
none reference SQLite or other native packages. Useful future capability
(local SQLite DB for per-server stats, for instance).
