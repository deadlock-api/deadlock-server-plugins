---
date: 2026-04-21
task: session extract — server-plugins 6b481873
files: [/home/manuel/.claude/projects/-home-manuel-deadlock-deadlock-server-plugins/6b481873-de64-4910-b889-83599fac9194.jsonl]
---

Session context: earlier design exploration for a **Rust-based** plugin system injected into the Wine/Proton-hosted Deadlock server. Predates the current C#/Deadworks architecture on main. Findings are about the Wine injection substrate, which is still relevant to any native-DLL plugin path.

## Deadworks runtime

- This repo's Docker infra runs the Deadlock dedicated server under Proton/Wine (not native Linux). Plugin injection therefore happens at the Wine/Win32 layer, not via a Linux `.so`. Any native-code plugin must ship as a Windows `.dll` and be loaded inside the Wine process (assistant turn A#129).
- `AppInit_DLLs` registry auto-injection **does not work reliably with Proton's Wine** — tested and failed during this session (A#423, A#482, A#615). The fallback chosen was an explicit loader exe that `LoadLibraryA`s `plugin_loader.dll`, which then scans a `plugins/` directory and loads each plugin DLL. Same caveat applies to any future C#/native hybrid: don't rely on `AppInit_DLLs`.
- Wine launched via `proton run` hijacks `argv` to launch `steam.exe` rather than the requested binary (A#451). For test harnesses and side-processes, invoke Wine **directly**, not through the Proton wrapper.
- Wine from Proton needs `WINEDLLPATH` pointed at Proton's DLL directories or it fails with "cannot find kernel32.dll" (A#462, A#465). A **fresh** `WINEPREFIX` is also required — prefixes from the `compatdata` volume were observed to be corrupt/broken after prior runs (A#473, A#476).

## Plugin build & deployment

- Cross-compile target for Rust-side native plugins: `x86_64-pc-windows-gnu` via `gcc-mingw-w64-x86-64` + `rustup target add x86_64-pc-windows-gnu` (A#236). This is the toolchain any future native-plugin crate should assume.
- `nono` sandbox blocks `rustup` from writing to `~/.rustup`. Required allow flags for plugin dev: `--allow /home/manuel/.rustup --allow /home/manuel/.cargo --allow /home/manuel/deadlock/deadlock-server-plugins` (A#232, A#236).
- `windows-sys` gotchas encountered (A#292): `BOOL` was removed in newer versions; `HMODULE` is now `*mut c_void`; `WIN32_FIND_DATAA::cFileName` is `[i8]` not `[u8]`. Also, `HMODULE` (being `*mut c_void`) is **not** `Send`, so passing it across a thread boundary in a `plugin_entry!` macro requires casting to `usize` first (A#314).
- Docker multi-stage layout chosen: Rust builder stage compiles all plugin DLLs and bakes them directly into the final image — no external `plugins/` output directory, no volume mount for compiled artifacts (A#171, A#188). `Cargo.lock` must be in `COPY` steps for reproducible builds (A#550 item 7).
- Repo layout convention landed on (A#618, A#648): production crates under `crates/` (`plugin-sdk`, `plugin-loader`), test-only crates under `tests/` (`test-harness`, `test-plugin`). Test crates are **not** workspace members intended for release; they exist purely to verify the injection chain end-to-end.
- Loader copy bug worth remembering (A#550 item 4, A#615): if you copy all built DLLs into `plugins/`, `plugin_loader.dll` ends up inside its own scan directory and tries to load itself — exclude it from the copy step.
- Injection chain verified by the harness test (A#476, A#482): `test-harness.exe` → `LoadLibraryA("plugin_loader.dll")` → loader scans `plugins/` → `LoadLibraryA` on each entry → plugin DLL's `DllMain`/`plugin_entry!` runs. Marker-file-on-disk is the assertion mechanism.
- Test orchestrator `tests/test-injection.sh` reuses the `proton` and `compatdata` Docker volumes so Proton is only downloaded once; subsequent runs are fast (A#415, A#482).
