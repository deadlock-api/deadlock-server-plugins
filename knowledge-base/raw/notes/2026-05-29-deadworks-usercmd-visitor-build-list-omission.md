---
date: 2026-05-29
task: make dev failed to link deadworks.exe (undefined usercmd-visitor symbols)
files:
  - ../deadworks/docker/build-native.sh
---

`make dev` (which runs `docker compose ... up --build` → `docker/build-native.sh`
inside the sibling `../deadworks` repo) failed at the **native C++ link** step, not
the C# plugin compile. Symptoms: `lld-20: error: undefined symbol:
deadworks::hooks::SetUsercmdNativeMode` (and Get/Set Usercmd Mount/Button/Field
mask, `ProcessUsercmdVisitors`), referenced from `NativeCallbacks.obj` and
`ProcessUsercmds.obj`.

Root cause: deadworks commit `1b94487 "Add mounted usercmd visitor path"` added a
new file `deadworks/src/Core/UsercmdVisitorRuntime.cpp` (defines those symbols in
`namespace deadworks::hooks`) plus callers, but **the native build uses an explicit
source list** in `docker/build-native.sh` (the `for f in \ ... ` block, ~lines
140-172) and the new file was never added to it. So the definitions were never
compiled → undefined-symbol link errors. Exact same class of bug as the earlier
deadworks fix `1b14271 "fix docker build: add missing InitializeHeroOnPawn.cpp"`.

Fix: add `${SRC}/Core/UsercmdVisitorRuntime.cpp \` to the list (placed after
`${SRC}/Core/Deadworks.cpp`). Rebuild links cleanly; `deadworks.exe` produced,
image builds end-to-end.

Gotcha for next agent: the build script is NOT a glob — adding any new `.cpp` under
`deadworks/src` requires editing this explicit list. The edit lives in the
`../deadworks` sibling repo (clean checkout on `1b94487`), so it must be committed
THERE, not in deadlock-server-plugins, or it resurfaces on a fresh clone.
Relates to [[docker-build]] / [[plugin-build-pipeline]].
