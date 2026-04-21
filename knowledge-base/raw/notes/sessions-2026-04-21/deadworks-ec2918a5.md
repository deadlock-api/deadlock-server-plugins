---
date: 2026-04-21
task: session extract — deadworks ec2918a5
files: [/home/manuel/.claude/projects/-home-manuel-deadlock-deadworks/ec2918a5-872a-442b-8a37-2f69a5a2e5d9.jsonl]
---

## Deadworks runtime

- Managed-side protos (`managed/DeadworksManaged.Api.csproj`) historically lived under `managed/protos/` as a duplicated subset of the sourcesdk deadlock protos. Session deleted `managed/protos/` and pointed `<Protobuf>` items directly at `sourcesdk/thirdparty/game_protobufs/deadlock/` — the sourcesdk's `netmessages.proto` does not import `source2_steam_stats.proto`, so that managed-only proto was dead weight.
- The native deadworks DLL consumes 14 C++ protos (not 15): `base_gcmessages`, `citadel_clientmessages`, `citadel_gcmessages_common`, `citadel_usermessages`, `gameevents`, `gcsdk_gcmessages`, `netmessages`, `network_connection`, `networkbasetypes`, `networksystem_protomessages`, `steammessages`, `steammessages_steamlearn.steamworkssdk`, `steammessages_unified_base.steamworkssdk`, `valveextensions`. `source2_steam_stats` was previously checked in but is not used by any deadworks source and not in the deadlock proto dir.

## Plugin build & deployment

- Pre-session state: two divergent proto pipelines. `sourcesdk/` (CMake/Docker/Linux) already ran `protoc` at build time via `SOURCESDK_COMPILE_PROTOBUF=ON` (generator at `cmake/sourcesdk/proto/generate.cmake`). `deadworks/deadworks.vcxproj` (MSVC/Windows) instead consumed ~30 pre-compiled `.pb.cc/.pb.h` files checked into top-level `protobuf/` (~367K LOC). Docker `docker/Dockerfile` ALSO consumed that pre-compiled dir via `COPY protobuf protobuf` — Linux build was not actually running protoc on the game protos despite the CMake wiring.
- `protoc.exe` ships inside the sourcesdk submodule at `sourcesdk/devtools/bin/win64/protoc.exe`; protobuf headers at `sourcesdk/thirdparty/protobuf/src/`. After this session, `local.props.example`'s `ProtobufIncludeDir` can be removed — includes resolve via the submodule path.
- New `deadworks/protobuf.targets` MSBuild file imported after `Microsoft.Cpp.targets` in the vcxproj. Defines `ProtocExe`, `ProtobufSdkInclude`, `ProtoSrcDir` (= `sourcesdk\thirdparty\game_protobufs\deadlock`), `ProtoOutDir` (= `$(IntDir)protobuf\`), and a `GenerateProtobufs` target with `BeforeTargets="ClCompile"` using `Inputs/Outputs` for incremental regeneration. `.pb.cc` files must also be added to ClCompile at build time since they're not statically listed in the vcxproj anymore.
- vcxproj `AdditionalIncludeDirectories`: replaced `$(ProjectDir)..\protobuf` with `$(ProtoOutDir)`. All static `ClCompile Include="..\protobuf\*.pb.cc"` + `ClInclude` entries removed from both vcxproj and `.filters`.
- Docker Linux build (`docker/Dockerfile`) now downloads host-native Linux `protoc` via release zip (arg `PROTOC_VERSION=3.21.12`, `github.com/protocolbuffers/protobuf/releases/download/v${PROTOC_VERSION}/protoc-${PROTOC_VERSION}-linux-x86_64.zip`) unzipped into `/usr/local`. The Windows protobuf library build (3.21.8 via xwin/clang-cl cross-compile) is retained for `libprotobuf.lib` linkage. `COPY protobuf protobuf` removed.
- `docker/build-native.sh` gained `PROTO_SRC="sourcesdk/thirdparty/game_protobufs/deadlock"` + `PROTO_OUT="generated/protobuf"`; `PROJECT_INCLUDES` swapped `/Iprotobuf` → `/I${PROTO_OUT}` on both the compile and the final link-stage include lists. Protos now generated at build time inside the container.
- Removed: `protobuf/` dir, `managed/protos/`, `scripts/update-protos.py`, `.github/workflows/update-protos.yml`. The update-protos workflow was pulling from a separate GameTracking-Deadlock repo; that source is abandoned in favor of the sourcesdk submodule as the single source of truth. `protobuf/` added to `.gitignore`.
- Gotcha: Windows protobuf runtime build (3.21.8) and host Linux `protoc` (3.21.12) are deliberately different versions. Runtime/generator ABI compat within the 3.21.x line is relied upon; bumping either independently may break.
