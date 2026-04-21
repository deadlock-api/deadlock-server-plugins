---
date: 2026-04-21
task: session extract — server-plugins 51b5680e
files: [/home/manuel/.claude/projects/-home-manuel-deadlock-deadlock-server-plugins/51b5680e-dabe-428d-9fa7-2130f9faebc7.jsonl]
---

Session is from an earlier, now-abandoned Rust incarnation of this repo (git branch `master`, workspace of Cargo crates producing `cdylib` DLLs). Current repo is C#/.NET. Findings below document that prior architecture.

## Source 2 engine

- `CGameEntitySystem` is reached via the `GameResourceServiceServerV001` interface exported by `engine2.dll`; the entity-system pointer lives at interface `+0x58`. Derived by community RE, stable across CS2/Deadlock builds (plugins/entity-plugin/src/lib.rs:40, 557-560).
- Entity identity list inside `CGameEntitySystem` is a chunked array: chunk pointer at `+0x10`, 512 entities per chunk, each `CEntityIdentity` is `0x78` (120) bytes, `MAX_ENTITIES = 16384` (lib.rs:40-52). Because the list offset drifts, session added a `probe_entity_list_offset` that scans candidate offsets `[0x10, 0x18, 0x20, 0x08, 0x28, 0x30, 0x38, 0x40, 0x48, 0x50, 0x58, 0x60, 0x78]` and validates via alignment + pointer sanity (lib.rs:351-385).
- Entity name path: `CEntityInstance+0x10` -> `CEntityIdentity`; `CEntityIdentity+0x18` -> `m_designerName` C-string (lib.rs:440-466).
- Position write path: `entity + m_CBodyComponent` -> `CBodyComponent`; `+ m_pSceneNode` -> `CGameSceneNode`; `+ m_vecAbsOrigin` -> `Vector` (lib.rs:469-494). All three offsets resolved dynamically, never hardcoded.
- Schema system: `SchemaSystem_001` from `schemasystem.dll`. Vtable indices used: `FindTypeScopeForModule` = 13, `CSchemaSystemTypeScope::FindDeclaredClass` = 2 (lib.rs:61-62). `SchemaClassInfoData_t` layout: name @ `+0x08`, size @ `+0x18` (i32), field_count @ `+0x1C` (i16), fields array @ `+0x28`. `SchemaClassFieldData_t`: name @ `+0x00`, type @ `+0x08`, offset @ `+0x10` (lib.rs:191-230).
- `scan_for_field_offset` prefers binary-scanning `server.dll` for the class name string then matching `SchemaClassInfoData_t` by pointer-equality at `+0x08`, falling back to the vtable call — safer under Wine where vtable dispatch into `schemasystem.dll` sometimes crashes (lib.rs:255-344, resolve_offset wrapper wraps the schema call in `std::panic::catch_unwind`).

## Deadlock game systems

- Schema field names resolved at runtime on Deadlock: `CBaseEntity::m_CBodyComponent`, `CBodyComponent::m_pSceneNode`, `CGameSceneNode::m_vecAbsOrigin`, `CBaseEntity::m_iHealth`, `CBaseEntity::m_iMaxHealth` (lib.rs:596-600). Health fields are `Option<i32>` — resolution may fail without fatal error.
- Sanity clamp for health reads: `health in [0,100000]`, `max_health in (0,100000]`, `health <= max_health` (lib.rs:517-521). Helps reject garbage when offsets mis-resolve.

## Deadworks runtime

- Plugin loader relies on Windows `CreateInterface` convention: `GetModuleHandleA(module) + GetProcAddress("CreateInterface")` yields an `unsafe extern "C" fn(*const u8, *mut i32) -> *mut u8`. Module names passed as null-terminated byte slices, e.g. `b"engine2.dll\0"`, `b"schemasystem.dll\0"` (plugin-common/src/engine.rs).
- Plugins run under Wine (injected into a Windows build served from Linux/Docker). Consequences captured in session: `IsBadReadPtr` is unreliable under Wine so the plugin validates a full entity region in one call before dereferencing individual fields (lib.rs:506-510); `tokio::net::UnixStream` is `#[cfg(unix)]` only and therefore unusable for a `x86_64-pc-windows-gnu` cdylib — session switched to TCP (`127.0.0.1:<port>`) for plugin-host IPC (assistant note at msg 237).
- `plugin_sdk::plugin_entry!` macro is the canonical plugin entrypoint: emits `DllMain`, and on `DLL_PROCESS_ATTACH` spawns `on_attach` on a fresh `std::thread` to avoid loader-lock deadlock; `on_detach` runs inline on `DLL_PROCESS_DETACH` (crates/plugin-sdk/src/lib.rs:1-47). Macro re-exports `windows_sys` so plugins don't need their own dep line for `Win32::Foundation::HMODULE`.
- Initial IPC was file-based: poll `Z:\tmp\set_position_cmd.txt` every 100ms, clear after read, write results to `Z:\tmp\set_position_result.txt`. `Z:\` is the Wine-mapped Linux FS (lib.rs:626, 855, 881-892). This was replaced mid-session by a TCP JSON protocol.
- `plugin-common::socket::PluginSocket` (crates/plugin-common/src/socket.rs): newline-delimited JSON over `TcpStream`, reader+writer split into two tokio tasks with unbounded mpsc channels. Multiple plugins connect independently to the same host address; each registers on connect (name/version) and exchanges correlated request/response messages.
- `plugin-common::logging::init_tracing` opens the log file in append mode and wraps it in a `Mutex<File>` as the `tracing_subscriber` writer; `with_ansi(false)` and `try_init()` so double-init (across multiple plugin DLLs) is a no-op (crates/plugin-common/src/logging.rs).

## Plugin build & deployment

- Workspace (before refactor): `crates/plugin-sdk`, `crates/plugin-loader`, `plugins/entity-plugin`, `tools/injector`, `tests/test-plugin`, `tests/test-harness`. After refactor: add `crates/plugin-common` with modules `memory`, `engine`, `schema`, `logging`, `types`, `socket` (commit `64ce9a4`, +1111/-631).
- Workspace uses edition 2024, resolver "3". Only `plugin-sdk` and `windows-sys` (with the full Win32 feature surface for Foundation/SystemServices/LibraryLoader/ProcessStatus/Threading/ToolHelp/Debug/Memory/Security) are workspace-level deps initially; refactor added `serde`, `serde_json`, `tokio` (features `rt`, `net`, `io-util`, `sync`, `macros` only — no `rt-multi-thread`), `tracing`, `tracing-subscriber`.
- Per-plugin `Cargo.toml` must declare `[lib] crate-type = ["cdylib"]` and use `workspace = true` deps. Plugins selectively enable extra windows-sys features (entity-plugin enables `Win32_System_ProcessStatus` for `GetModuleInformation`).
- Repo also ships `.cargo/`, `mise.toml`, `Dockerfile`, `docker-compose.yaml`, `docker-compose.test.yaml`, `entrypoint.sh`, `rust-toolchain.toml` — Docker-based dev loop for the Wine-hosted server.
