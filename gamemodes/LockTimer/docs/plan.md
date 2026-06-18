# LockTimer Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a minimalist speedrun-timer Deadworks plugin that lets any player define a start/end AABB zone by crosshair raycast, times runs between them, persists PB records to local SQLite, and renders zone edges as glowing particles.

**Architecture:** Layered services inside a single `` net10.0 class library. Pure-logic layers (`TimerEngine`, `Zone`, repositories, `TimeFormatter`) live in a test-friendly bubble that never references `DeadworksManaged.Api`, so they can be exercised by xUnit. The plugin shell (`LockTimerPlugin`) is the only file that touches the Deadworks API, forwarding frame ticks and chat commands into the pure layers.

**Tech Stack:** C# / .NET 10, DeadworksManaged.Api (vendored at `deadworks/managed/DeadworksManaged.Api/`), Microsoft.Data.Sqlite, xUnit for tests, System.Numerics for math.

**Spec:** `docs/superpowers/specs/2026-04-12-locktimer-design.md`

---

## Orientation for the implementing engineer

This repo lives at `<workspace>/Plugins/LockTimer/` inside a parent workspace that also contains a vendored clone of the Deadworks source at `<workspace>/deadworks/` and a reference plugin at `<workspace>/Boilerplate/`. Those sibling directories are NOT tracked by this repo — they're workspace-only references. Paths below are relative to the parent workspace so you can navigate from either location.

Before touching any file, read these:

1. `Plugins/LockTimer/docs/spec.md` — this repo's own approved design; everything here derives from it.
2. `Boilerplate/Boilerplate.cs` + `Boilerplate/Boilerplate.csproj` — the canonical "hello world" Deadworks plugin. LockTimer's csproj mirrors it.
3. `Boilerplate/GameEvents.md` — the full lifecycle / event reference.
4. `deadworks/managed/DeadworksManaged.Api/DeadworksPluginBase.cs` — hook signatures.
5. `deadworks/managed/DeadworksManaged.Api/Trace/TraceSystem.cs` — crosshair raycast API.
6. `deadworks/managed/DeadworksManaged.Api/ParticleSystem.cs` — particle builder.
7. `deadworks/managed/DeadworksManaged.Api/Entities/PlayerEntities.cs` lines 341–370 — `EyePosition`, `ViewAngles`.
8. `deadworks/managed/DeadworksManaged.Api/Server.cs` — `Server.MapName`, `AddSearchPath`.
9. `deadworks/managed/DeadworksManaged.Api/Events/ChatCommandAttribute.cs` — command attribute.
10. `deadworks/managed/plugins/DeathmatchPlugin/DeathmatchPlugin.cs` — a real example using both `[ChatCommand]` and `[GameEventHandler]`.

**Where things are deployed at runtime:**
- Plugin DLL + deps → `F:\SteamLibrary\steamapps\common\Deadlock\game\bin\win64\managed\plugins\`
- DB file → `…\plugins\LockTimer\locktimer.db` (auto-created)

**Never reference `DeadworksManaged.Api`** from `LockTimer.Tests/`. If you're tempted to, refactor so the tested code takes plain values (Vector3, int, string) instead.

---

## File structure (target)

```
.                                ← repo root (github.com/Oskar-Sterner/lock-timer)
├── .gitignore
├── README.md
├── LockTimer.csproj
├── LockTimerPlugin.cs
├── Commands/
│   └── ChatCommands.cs
├── Zones/
│   ├── Zone.cs
│   ├── ZoneKind.cs
│   ├── ZoneRepository.cs
│   ├── ZoneEditor.cs
│   └── ZoneRenderer.cs
├── Timing/
│   ├── RunState.cs
│   ├── PlayerRun.cs
│   ├── TimerEngine.cs
│   └── FinishedRun.cs
├── Records/
│   ├── Record.cs
│   ├── RecordRepository.cs
│   └── TimeFormatter.cs
├── Data/
│   ├── LockTimerDb.cs
│   └── Migrations/
│       └── 001_initial.sql
├── docs/
│   ├── spec.md
│   └── plan.md
└── LockTimer.Tests/
    ├── LockTimer.Tests.csproj
    ├── ZoneTests.cs
    ├── LockTimerDbTests.cs
    ├── ZoneRepositoryTests.cs
    ├── RecordRepositoryTests.cs
    ├── TimeFormatterTests.cs
    └── TimerEngineTests.cs
```

---

# Phase 1 — Scaffold

Goal: a green `dotnet build` for both projects, plugin loads empty, tests run.

## Task 1.1 — Create plugin csproj

**Files:**
- Create: `LockTimer.csproj`

- [ ] **Step 1: Write the csproj**

Mirror Boilerplate.csproj but add SQLite. Paste verbatim:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <RootNamespace>LockTimer</RootNamespace>
    <AssemblyName>LockTimer</AssemblyName>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <EnableDynamicLoading>true</EnableDynamicLoading>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
  </PropertyGroup>

  <ItemGroup>
    <Reference Include="DeadworksManaged.Api">
      <HintPath>F:\SteamLibrary\steamapps\common\Deadlock\game\bin\win64\managed\DeadworksManaged.Api.dll</HintPath>
      <Private>false</Private>
      <ExcludeAssets>runtime</ExcludeAssets>
    </Reference>
    <Reference Include="Google.Protobuf">
      <HintPath>F:\SteamLibrary\steamapps\common\Deadlock\game\bin\win64\managed\Google.Protobuf.dll</HintPath>
      <Private>false</Private>
    </Reference>
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.Data.Sqlite" Version="9.0.0" />
  </ItemGroup>

  <ItemGroup>
    <EmbeddedResource Include="Data\Migrations\*.sql" />
  </ItemGroup>

  <Target Name="DeployToGame" AfterTargets="Build">
    <ItemGroup>
      <DeployFiles Include="$(OutputPath)LockTimer.dll;$(OutputPath)LockTimer.pdb" />
      <DeployFiles Include="$(OutputPath)Microsoft.Data.Sqlite.dll" />
      <DeployFiles Include="$(OutputPath)SQLitePCLRaw.core.dll" />
      <DeployFiles Include="$(OutputPath)SQLitePCLRaw.provider.e_sqlite3.dll" />
      <DeployFiles Include="$(OutputPath)SQLitePCLRaw.batteries_v2.dll" />
      <DeployFiles Include="$(OutputPath)runtimes\win-x64\native\e_sqlite3.dll" />
    </ItemGroup>
    <Copy SourceFiles="@(DeployFiles)"
          DestinationFolder="F:\SteamLibrary\steamapps\common\Deadlock\game\bin\win64\managed\plugins"
          SkipUnchangedFiles="false"
          Retries="0"
          ContinueOnError="WarnAndContinue" />
  </Target>

</Project>
```

- [ ] **Step 2: Commit**

```bash
git add LockTimer.csproj
git commit -m "feat(locktimer): scaffold plugin csproj with sqlite deps"
```

Note: if `Microsoft.Data.Sqlite 9.0.0` fails to restore on net10.0, try `10.0.0` then the latest stable. The `runtimes\win-x64\native\e_sqlite3.dll` path comes from the bundle package and is what the plugin loads at runtime.

---

## Task 1.2 — Create empty plugin shell

**Files:**
- Create: `LockTimerPlugin.cs`

- [ ] **Step 1: Write the empty shell**

```csharp
using DeadworksManaged.Api;

namespace LockTimer;

public class LockTimerPlugin : DeadworksPluginBase
{
    public override string Name => "LockTimer";

    public override void OnLoad(bool isReload)
    {
        Console.WriteLine($"[{Name}] {(isReload ? "Reloaded" : "Loaded")}.");
    }

    public override void OnUnload()
    {
        Console.WriteLine($"[{Name}] Unloaded.");
    }
}
```

- [ ] **Step 2: Verify it builds**

Run from repo root: `dotnet build "LockTimer.csproj"`
Expected: `Build succeeded.` with 0 warnings 0 errors.

If DeadworksManaged.Api path is wrong for your machine, check `Boilerplate/Boilerplate.csproj` — same hint path.

- [ ] **Step 3: Commit**

```bash
git add LockTimerPlugin.cs
git commit -m "feat(locktimer): add empty plugin shell"
```

---

## Task 1.3 — Create test project

**Files:**
- Create: `LockTimer.Tests/LockTimer.Tests.csproj`

- [ ] **Step 1: Write the test csproj**

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
    <IsTestProject>true</IsTestProject>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.11.1" />
    <PackageReference Include="xunit" Version="2.9.2" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.8.2" />
    <PackageReference Include="Microsoft.Data.Sqlite" Version="9.0.0" />
  </ItemGroup>

  <ItemGroup>
    <Compile Include="..\Zones\Zone.cs" Link="Src\Zone.cs" />
    <Compile Include="..\Zones\ZoneKind.cs" Link="Src\ZoneKind.cs" />
    <Compile Include="..\Zones\ZoneRepository.cs" Link="Src\ZoneRepository.cs" />
    <Compile Include="..\Records\Record.cs" Link="Src\Record.cs" />
    <Compile Include="..\Records\RecordRepository.cs" Link="Src\RecordRepository.cs" />
    <Compile Include="..\Records\TimeFormatter.cs" Link="Src\TimeFormatter.cs" />
    <Compile Include="..\Data\LockTimerDb.cs" Link="Src\LockTimerDb.cs" />
    <Compile Include="..\Timing\RunState.cs" Link="Src\RunState.cs" />
    <Compile Include="..\Timing\PlayerRun.cs" Link="Src\PlayerRun.cs" />
    <Compile Include="..\Timing\TimerEngine.cs" Link="Src\TimerEngine.cs" />
    <Compile Include="..\Timing\FinishedRun.cs" Link="Src\FinishedRun.cs" />
  </ItemGroup>

  <ItemGroup>
    <EmbeddedResource Include="..\Data\Migrations\*.sql" LinkBase="Data\Migrations" />
  </ItemGroup>

</Project>
```

**Why `<Compile Include="..\...">` instead of a `ProjectReference`?** LockTimer.csproj references the full `DeadworksManaged.Api.dll` from the Deadlock install. Adding a project reference from the test project would transitively require the test runner to resolve that DLL — which is fine until CI runs on a machine without Deadlock installed. Source-including only the pure-logic files keeps the test project self-contained and enforces the "no Deadworks types in the tested layer" invariant at compile time: if you try to `using DeadworksManaged.Api;` in a file that lives in the test project's include list, this project stops compiling.

- [ ] **Step 2: Commit**

```bash
git add LockTimer.Tests/LockTimer.Tests.csproj
git commit -m "feat(locktimer): scaffold test project"
```

Tests won't run yet because the referenced files don't exist. That's fine — they come in Phase 2.

---

# Phase 2 — Data layer

Goal: Zone math, DB, two repositories, TimeFormatter — all unit-tested with no Deadworks dependency.

## Task 2.1 — Zone kind enum + Zone record + Contains

**Files:**
- Create: `Zones/ZoneKind.cs`
- Create: `Zones/Zone.cs`
- Create: `LockTimer.Tests/ZoneTests.cs`

- [ ] **Step 1: Create ZoneKind.cs**

```csharp
namespace LockTimer.Zones;

public enum ZoneKind
{
    Start = 0,
    End = 1,
}
```

- [ ] **Step 2: Write failing tests in ZoneTests.cs**

```csharp
using System.Numerics;
using LockTimer.Zones;
using Xunit;

namespace LockTimer.Tests;

public class ZoneTests
{
    private static Zone Box(Vector3 min, Vector3 max)
        => new(ZoneKind.Start, "test_map", min, max, UpdatedAtUnix: 0);

    [Fact]
    public void Contains_point_at_center_is_true()
    {
        var z = Box(new(0, 0, 0), new(100, 100, 100));
        Assert.True(z.Contains(new(50, 50, 50)));
    }

    [Fact]
    public void Contains_point_exactly_on_corner_is_true()
    {
        var z = Box(new(0, 0, 0), new(100, 100, 100));
        Assert.True(z.Contains(new(0, 0, 0)));
        Assert.True(z.Contains(new(100, 100, 100)));
    }

    [Fact]
    public void Contains_point_just_outside_each_axis_is_false()
    {
        var z = Box(new(0, 0, 0), new(100, 100, 100));
        Assert.False(z.Contains(new(-0.01f, 50, 50)));
        Assert.False(z.Contains(new(50, 100.01f, 50)));
        Assert.False(z.Contains(new(50, 50, -0.01f)));
    }

    [Fact]
    public void Contains_handles_negative_coordinates()
    {
        var z = Box(new(-100, -100, -100), new(-50, -50, -50));
        Assert.True(z.Contains(new(-75, -75, -75)));
        Assert.False(z.Contains(new(0, 0, 0)));
    }

    [Fact]
    public void From_two_corners_normalizes_min_max()
    {
        var z = Zone.FromCorners(ZoneKind.End, "m", new(100, 0, 50), new(0, 100, 0), updatedAtUnix: 0);
        Assert.Equal(new Vector3(0, 0, 0), z.Min);
        Assert.Equal(new Vector3(100, 100, 50), z.Max);
    }
}
```

- [ ] **Step 3: Create Zone.cs**

```csharp
using System.Numerics;

namespace LockTimer.Zones;

public sealed record Zone(
    ZoneKind Kind,
    string Map,
    Vector3 Min,
    Vector3 Max,
    long UpdatedAtUnix)
{
    public bool Contains(Vector3 p) =>
        p.X >= Min.X && p.X <= Max.X &&
        p.Y >= Min.Y && p.Y <= Max.Y &&
        p.Z >= Min.Z && p.Z <= Max.Z;

    public bool IsZeroVolume =>
        Min.X == Max.X || Min.Y == Max.Y || Min.Z == Max.Z;

    public static Zone FromCorners(ZoneKind kind, string map, Vector3 a, Vector3 b, long updatedAtUnix) =>
        new(kind, map,
            Min: new Vector3(MathF.Min(a.X, b.X), MathF.Min(a.Y, b.Y), MathF.Min(a.Z, b.Z)),
            Max: new Vector3(MathF.Max(a.X, b.X), MathF.Max(a.Y, b.Y), MathF.Max(a.Z, b.Z)),
            UpdatedAtUnix: updatedAtUnix);
}
```

- [ ] **Step 4: Run tests**

Run: `dotnet test LockTimer.Tests/LockTimer.Tests.csproj --filter FullyQualifiedName~ZoneTests`
Expected: 5 passed, 0 failed.

- [ ] **Step 5: Commit**

```bash
git add Zones/ZoneKind.cs Zones/Zone.cs LockTimer.Tests/ZoneTests.cs
git commit -m "feat(locktimer): add Zone record with AABB containment"
```

---

## Task 2.2 — LockTimerDb + migration + tests

**Files:**
- Create: `Data/Migrations/001_initial.sql`
- Create: `Data/LockTimerDb.cs`
- Create: `LockTimer.Tests/LockTimerDbTests.cs`

- [ ] **Step 1: Create the migration SQL**

```sql
CREATE TABLE IF NOT EXISTS zones (
    map        TEXT    NOT NULL,
    kind       INTEGER NOT NULL,
    min_x      REAL    NOT NULL,
    min_y      REAL    NOT NULL,
    min_z      REAL    NOT NULL,
    max_x      REAL    NOT NULL,
    max_y      REAL    NOT NULL,
    max_z      REAL    NOT NULL,
    updated_at INTEGER NOT NULL,
    PRIMARY KEY (map, kind)
);

CREATE TABLE IF NOT EXISTS records (
    steam_id    INTEGER NOT NULL,
    map         TEXT    NOT NULL,
    time_ms     INTEGER NOT NULL,
    player_name TEXT    NOT NULL,
    achieved_at INTEGER NOT NULL,
    PRIMARY KEY (steam_id, map)
);

CREATE INDEX IF NOT EXISTS idx_records_top ON records (map, time_ms);
```

- [ ] **Step 2: Write failing tests**

```csharp
using Microsoft.Data.Sqlite;
using LockTimer.Data;
using Xunit;

namespace LockTimer.Tests;

public class LockTimerDbTests
{
    [Fact]
    public void Open_in_memory_applies_schema()
    {
        using var db = LockTimerDb.OpenInMemory();

        using var cmd = db.Connection.CreateCommand();
        cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' ORDER BY name;";
        var tables = new List<string>();
        using var r = cmd.ExecuteReader();
        while (r.Read()) tables.Add(r.GetString(0));

        Assert.Contains("zones", tables);
        Assert.Contains("records", tables);
    }

    [Fact]
    public void Open_is_idempotent()
    {
        using var db = LockTimerDb.OpenInMemory();
        // Running migration again must not throw
        db.ApplySchema();
    }
}
```

- [ ] **Step 3: Create LockTimerDb.cs**

```csharp
using System.Reflection;
using Microsoft.Data.Sqlite;

namespace LockTimer.Data;

public sealed class LockTimerDb : IDisposable
{
    public SqliteConnection Connection { get; }

    private LockTimerDb(SqliteConnection conn)
    {
        Connection = conn;
    }

    public static LockTimerDb Open(string path)
    {
        var cs = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Private,
        }.ToString();

        var conn = new SqliteConnection(cs);
        conn.Open();
        var db = new LockTimerDb(conn);
        db.ApplyPragmas();
        db.ApplySchema();
        return db;
    }

    public static LockTimerDb OpenInMemory()
    {
        var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        var db = new LockTimerDb(conn);
        db.ApplySchema();
        return db;
    }

    public void ApplySchema()
    {
        var sql = LoadEmbeddedSql("001_initial.sql");
        using var cmd = Connection.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    private void ApplyPragmas()
    {
        using var cmd = Connection.CreateCommand();
        cmd.CommandText = "PRAGMA journal_mode = WAL;";
        cmd.ExecuteNonQuery();
    }

    private static string LoadEmbeddedSql(string name)
    {
        var asm = typeof(LockTimerDb).Assembly;
        var resourceName = asm.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith(name, StringComparison.Ordinal))
            ?? throw new InvalidOperationException($"Embedded SQL '{name}' not found.");
        using var s = asm.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded SQL stream '{resourceName}' was null.");
        using var r = new StreamReader(s);
        return r.ReadToEnd();
    }

    public void Dispose() => Connection.Dispose();
}
```

- [ ] **Step 4: Run tests**

Run: `dotnet test LockTimer.Tests/LockTimer.Tests.csproj --filter FullyQualifiedName~LockTimerDbTests`
Expected: 2 passed.

If you get `Could not load file or assembly 'SQLitePCLRaw...`, ensure the test csproj references `Microsoft.Data.Sqlite` (it does per Task 1.3).

- [ ] **Step 5: Commit**

```bash
git add Data LockTimer.Tests/LockTimerDbTests.cs
git commit -m "feat(locktimer): add SQLite connection and schema migration"
```

---

## Task 2.3 — ZoneRepository

**Files:**
- Create: `Zones/ZoneRepository.cs`
- Create: `LockTimer.Tests/ZoneRepositoryTests.cs`

- [ ] **Step 1: Write failing tests**

```csharp
using System.Numerics;
using LockTimer.Data;
using LockTimer.Zones;
using Xunit;

namespace LockTimer.Tests;

public class ZoneRepositoryTests
{
    private static (LockTimerDb db, ZoneRepository repo) Make()
    {
        var db = LockTimerDb.OpenInMemory();
        var repo = new ZoneRepository(db.Connection);
        return (db, repo);
    }

    [Fact]
    public void Upsert_inserts_new_zone()
    {
        var (db, repo) = Make();
        using var _ = db;

        var z = new Zone(ZoneKind.Start, "m1", new(0, 0, 0), new(1, 1, 1), UpdatedAtUnix: 100);
        repo.Upsert(z);

        var loaded = repo.GetForMap("m1");
        Assert.Single(loaded);
        Assert.Equal(ZoneKind.Start, loaded[0].Kind);
        Assert.Equal(new Vector3(1, 1, 1), loaded[0].Max);
    }

    [Fact]
    public void Upsert_replaces_existing_zone_of_same_kind()
    {
        var (db, repo) = Make();
        using var _ = db;

        repo.Upsert(new Zone(ZoneKind.Start, "m1", new(0, 0, 0), new(1, 1, 1), 100));
        repo.Upsert(new Zone(ZoneKind.Start, "m1", new(5, 5, 5), new(6, 6, 6), 200));

        var loaded = repo.GetForMap("m1");
        Assert.Single(loaded);
        Assert.Equal(new Vector3(5, 5, 5), loaded[0].Min);
        Assert.Equal(200, loaded[0].UpdatedAtUnix);
    }

    [Fact]
    public void GetForMap_isolates_by_map()
    {
        var (db, repo) = Make();
        using var _ = db;

        repo.Upsert(new Zone(ZoneKind.Start, "m1", new(0, 0, 0), new(1, 1, 1), 100));
        repo.Upsert(new Zone(ZoneKind.End, "m2", new(0, 0, 0), new(1, 1, 1), 100));

        Assert.Single(repo.GetForMap("m1"));
        Assert.Single(repo.GetForMap("m2"));
        Assert.Empty(repo.GetForMap("m3"));
    }

    [Fact]
    public void Delete_removes_all_zones_for_map()
    {
        var (db, repo) = Make();
        using var _ = db;

        repo.Upsert(new Zone(ZoneKind.Start, "m1", new(0, 0, 0), new(1, 1, 1), 100));
        repo.Upsert(new Zone(ZoneKind.End,   "m1", new(2, 2, 2), new(3, 3, 3), 100));
        repo.DeleteForMap("m1");

        Assert.Empty(repo.GetForMap("m1"));
    }
}
```

- [ ] **Step 2: Implement ZoneRepository**

```csharp
using System.Numerics;
using Microsoft.Data.Sqlite;

namespace LockTimer.Zones;

public sealed class ZoneRepository
{
    private readonly SqliteConnection _conn;

    public ZoneRepository(SqliteConnection connection)
    {
        _conn = connection;
    }

    public void Upsert(Zone zone)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = @"
INSERT INTO zones (map, kind, min_x, min_y, min_z, max_x, max_y, max_z, updated_at)
VALUES (@map, @kind, @minx, @miny, @minz, @maxx, @maxy, @maxz, @ua)
ON CONFLICT(map, kind) DO UPDATE SET
    min_x = excluded.min_x,
    min_y = excluded.min_y,
    min_z = excluded.min_z,
    max_x = excluded.max_x,
    max_y = excluded.max_y,
    max_z = excluded.max_z,
    updated_at = excluded.updated_at;";
        cmd.Parameters.AddWithValue("@map",  zone.Map);
        cmd.Parameters.AddWithValue("@kind", (int)zone.Kind);
        cmd.Parameters.AddWithValue("@minx", zone.Min.X);
        cmd.Parameters.AddWithValue("@miny", zone.Min.Y);
        cmd.Parameters.AddWithValue("@minz", zone.Min.Z);
        cmd.Parameters.AddWithValue("@maxx", zone.Max.X);
        cmd.Parameters.AddWithValue("@maxy", zone.Max.Y);
        cmd.Parameters.AddWithValue("@maxz", zone.Max.Z);
        cmd.Parameters.AddWithValue("@ua",   zone.UpdatedAtUnix);
        cmd.ExecuteNonQuery();
    }

    public List<Zone> GetForMap(string map)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = @"
SELECT kind, min_x, min_y, min_z, max_x, max_y, max_z, updated_at
FROM zones
WHERE map = @map
ORDER BY kind;";
        cmd.Parameters.AddWithValue("@map", map);

        var list = new List<Zone>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            list.Add(new Zone(
                Kind: (ZoneKind)r.GetInt32(0),
                Map:  map,
                Min:  new Vector3((float)r.GetDouble(1), (float)r.GetDouble(2), (float)r.GetDouble(3)),
                Max:  new Vector3((float)r.GetDouble(4), (float)r.GetDouble(5), (float)r.GetDouble(6)),
                UpdatedAtUnix: r.GetInt64(7)));
        }
        return list;
    }

    public void DeleteForMap(string map)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "DELETE FROM zones WHERE map = @map;";
        cmd.Parameters.AddWithValue("@map", map);
        cmd.ExecuteNonQuery();
    }
}
```

- [ ] **Step 3: Run tests**

Run: `dotnet test LockTimer.Tests/LockTimer.Tests.csproj --filter FullyQualifiedName~ZoneRepositoryTests`
Expected: 4 passed.

- [ ] **Step 4: Commit**

```bash
git add Zones/ZoneRepository.cs LockTimer.Tests/ZoneRepositoryTests.cs
git commit -m "feat(locktimer): add ZoneRepository with per-map upsert"
```

---

## Task 2.4 — Record, TimeFormatter, and their tests

**Files:**
- Create: `Records/Record.cs`
- Create: `Records/TimeFormatter.cs`
- Create: `LockTimer.Tests/TimeFormatterTests.cs`

- [ ] **Step 1: Create Record.cs**

```csharp
namespace LockTimer.Records;

public sealed record Record(
    long SteamId,
    string Map,
    int TimeMs,
    string PlayerName,
    long AchievedAtUnix);
```

- [ ] **Step 2: Write failing TimeFormatter tests**

```csharp
using LockTimer.Records;
using Xunit;

namespace LockTimer.Tests;

public class TimeFormatterTests
{
    [Theory]
    [InlineData(0,        "0:00:00.000")]
    [InlineData(1,        "0:00:00.001")]
    [InlineData(999,      "0:00:00.999")]
    [InlineData(1_000,    "0:00:01.000")]
    [InlineData(60_000,   "0:01:00.000")]
    [InlineData(83_456,   "0:01:23.456")]
    [InlineData(3_600_000,"1:00:00.000")]
    [InlineData(3_723_456,"1:02:03.456")]
    public void FormatTime_matches_expected(int ms, string expected)
    {
        Assert.Equal(expected, TimeFormatter.FormatTime(ms));
    }
}
```

- [ ] **Step 3: Implement TimeFormatter**

```csharp
namespace LockTimer.Records;

public static class TimeFormatter
{
    public static string FormatTime(int ms)
    {
        if (ms < 0) ms = 0;
        int totalSec = ms / 1000;
        int millis   = ms % 1000;
        int hours    = totalSec / 3600;
        int minutes  = (totalSec % 3600) / 60;
        int seconds  = totalSec % 60;
        return $"{hours}:{minutes:D2}:{seconds:D2}.{millis:D3}";
    }
}
```

- [ ] **Step 4: Run tests**

Run: `dotnet test LockTimer.Tests/LockTimer.Tests.csproj --filter FullyQualifiedName~TimeFormatterTests`
Expected: 8 passed.

- [ ] **Step 5: Commit**

```bash
git add Records LockTimer.Tests/TimeFormatterTests.cs
git commit -m "feat(locktimer): add Record and TimeFormatter"
```

---

## Task 2.5 — RecordRepository with UpsertIfFaster

**Files:**
- Create: `Records/RecordRepository.cs`
- Create: `LockTimer.Tests/RecordRepositoryTests.cs`

- [ ] **Step 1: Write failing tests**

```csharp
using LockTimer.Data;
using LockTimer.Records;
using Xunit;

namespace LockTimer.Tests;

public class RecordRepositoryTests
{
    private static (LockTimerDb db, RecordRepository repo) Make()
    {
        var db = LockTimerDb.OpenInMemory();
        var repo = new RecordRepository(db.Connection);
        return (db, repo);
    }

    [Fact]
    public void First_submission_is_new_pb_with_null_previous()
    {
        var (db, repo) = Make();
        using var _ = db;

        var result = repo.UpsertIfFaster(steamId: 1, map: "m1", timeMs: 10_000,
            playerName: "alice", nowUnix: 100);

        Assert.True(result.Changed);
        Assert.Null(result.PreviousMs);
    }

    [Fact]
    public void Faster_submission_updates_pb_and_reports_previous()
    {
        var (db, repo) = Make();
        using var _ = db;

        repo.UpsertIfFaster(1, "m1", 10_000, "alice", 100);
        var result = repo.UpsertIfFaster(1, "m1",  9_000, "alice", 200);

        Assert.True(result.Changed);
        Assert.Equal(10_000, result.PreviousMs);

        var pb = repo.GetPb(1, "m1");
        Assert.NotNull(pb);
        Assert.Equal(9_000, pb!.TimeMs);
    }

    [Fact]
    public void Slower_submission_reports_unchanged_with_previous()
    {
        var (db, repo) = Make();
        using var _ = db;

        repo.UpsertIfFaster(1, "m1",  9_000, "alice", 100);
        var result = repo.UpsertIfFaster(1, "m1", 12_000, "alice", 200);

        Assert.False(result.Changed);
        Assert.Equal(9_000, result.PreviousMs);
    }

    [Fact]
    public void Top_returns_fastest_first_across_players()
    {
        var (db, repo) = Make();
        using var _ = db;

        repo.UpsertIfFaster(1, "m1", 10_000, "alice", 100);
        repo.UpsertIfFaster(2, "m1",  8_000, "bob",   100);
        repo.UpsertIfFaster(3, "m1", 15_000, "carol", 100);

        var top = repo.GetTop("m1", limit: 10);
        Assert.Equal(3, top.Count);
        Assert.Equal("bob",   top[0].PlayerName);
        Assert.Equal("alice", top[1].PlayerName);
        Assert.Equal("carol", top[2].PlayerName);
    }

    [Fact]
    public void GetPb_returns_null_when_missing()
    {
        var (db, repo) = Make();
        using var _ = db;

        Assert.Null(repo.GetPb(42, "nowhere"));
    }
}
```

- [ ] **Step 2: Implement RecordRepository**

```csharp
using Microsoft.Data.Sqlite;

namespace LockTimer.Records;

public readonly record struct UpsertResult(bool Changed, int? PreviousMs);

public sealed class RecordRepository
{
    private readonly SqliteConnection _conn;

    public RecordRepository(SqliteConnection connection)
    {
        _conn = connection;
    }

    public UpsertResult UpsertIfFaster(long steamId, string map, int timeMs, string playerName, long nowUnix)
    {
        using var tx = _conn.BeginTransaction();

        int? previous = null;
        using (var read = _conn.CreateCommand())
        {
            read.Transaction = tx;
            read.CommandText = "SELECT time_ms FROM records WHERE steam_id = @sid AND map = @map;";
            read.Parameters.AddWithValue("@sid", steamId);
            read.Parameters.AddWithValue("@map", map);
            var o = read.ExecuteScalar();
            if (o is long l) previous = (int)l;
        }

        bool changed;
        if (previous is null)
        {
            using var ins = _conn.CreateCommand();
            ins.Transaction = tx;
            ins.CommandText = @"
INSERT INTO records (steam_id, map, time_ms, player_name, achieved_at)
VALUES (@sid, @map, @t, @n, @at);";
            ins.Parameters.AddWithValue("@sid", steamId);
            ins.Parameters.AddWithValue("@map", map);
            ins.Parameters.AddWithValue("@t",   timeMs);
            ins.Parameters.AddWithValue("@n",   playerName);
            ins.Parameters.AddWithValue("@at",  nowUnix);
            ins.ExecuteNonQuery();
            changed = true;
        }
        else if (timeMs < previous)
        {
            using var upd = _conn.CreateCommand();
            upd.Transaction = tx;
            upd.CommandText = @"
UPDATE records
SET time_ms = @t, player_name = @n, achieved_at = @at
WHERE steam_id = @sid AND map = @map;";
            upd.Parameters.AddWithValue("@sid", steamId);
            upd.Parameters.AddWithValue("@map", map);
            upd.Parameters.AddWithValue("@t",   timeMs);
            upd.Parameters.AddWithValue("@n",   playerName);
            upd.Parameters.AddWithValue("@at",  nowUnix);
            upd.ExecuteNonQuery();
            changed = true;
        }
        else
        {
            changed = false;
        }

        tx.Commit();
        return new UpsertResult(changed, previous);
    }

    public Record? GetPb(long steamId, string map)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = @"
SELECT steam_id, map, time_ms, player_name, achieved_at
FROM records
WHERE steam_id = @sid AND map = @map;";
        cmd.Parameters.AddWithValue("@sid", steamId);
        cmd.Parameters.AddWithValue("@map", map);

        using var r = cmd.ExecuteReader();
        if (!r.Read()) return null;
        return new Record(
            SteamId: r.GetInt64(0),
            Map:     r.GetString(1),
            TimeMs:  r.GetInt32(2),
            PlayerName:    r.GetString(3),
            AchievedAtUnix: r.GetInt64(4));
    }

    public List<Record> GetTop(string map, int limit)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = @"
SELECT steam_id, map, time_ms, player_name, achieved_at
FROM records
WHERE map = @map
ORDER BY time_ms ASC
LIMIT @lim;";
        cmd.Parameters.AddWithValue("@map", map);
        cmd.Parameters.AddWithValue("@lim", limit);

        var list = new List<Record>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            list.Add(new Record(
                SteamId: r.GetInt64(0),
                Map:     r.GetString(1),
                TimeMs:  r.GetInt32(2),
                PlayerName:    r.GetString(3),
                AchievedAtUnix: r.GetInt64(4)));
        }
        return list;
    }
}
```

- [ ] **Step 3: Run tests**

Run: `dotnet test LockTimer.Tests/LockTimer.Tests.csproj --filter FullyQualifiedName~RecordRepositoryTests`
Expected: 5 passed.

- [ ] **Step 4: Commit**

```bash
git add Records/RecordRepository.cs LockTimer.Tests/RecordRepositoryTests.cs
git commit -m "feat(locktimer): add RecordRepository with PB upsert semantics"
```

---

# Phase 3 — Timer engine

Goal: pure FSM that takes (slot, position) ticks and emits transitions. Zero Deadworks dependencies. Fully unit-tested.

## Task 3.1 — RunState, PlayerRun, FinishedRun types

**Files:**
- Create: `Timing/RunState.cs`
- Create: `Timing/PlayerRun.cs`
- Create: `Timing/FinishedRun.cs`

- [ ] **Step 1: Create the types**

RunState.cs:
```csharp
namespace LockTimer.Timing;

public enum RunState
{
    Idle,
    InStart,
    Running,
    Finished,
}
```

PlayerRun.cs:
```csharp
namespace LockTimer.Timing;

public sealed class PlayerRun
{
    public RunState State { get; set; } = RunState.Idle;
    public long StartTickMs { get; set; }
}
```

FinishedRun.cs:
```csharp
namespace LockTimer.Timing;

public readonly record struct FinishedRun(int Slot, int ElapsedMs);
```

- [ ] **Step 2: Commit**

```bash
git add Timing/RunState.cs Timing/PlayerRun.cs Timing/FinishedRun.cs
git commit -m "feat(locktimer): add timing state types"
```

No tests yet — pure data types, exercised in Task 3.2.

---

## Task 3.2 — TimerEngine FSM + tests

**Files:**
- Create: `Timing/TimerEngine.cs`
- Create: `LockTimer.Tests/TimerEngineTests.cs`

The engine is pure: callers pass in the current monotonic clock (ms) and the player's world position. No `Environment.TickCount64`, no singletons — the plugin shell injects wall-clock values. This makes FSM tests deterministic.

- [ ] **Step 1: Write failing tests**

```csharp
using System.Numerics;
using LockTimer.Timing;
using LockTimer.Zones;
using Xunit;

namespace LockTimer.Tests;

public class TimerEngineTests
{
    private static Zone StartZone() =>
        new(ZoneKind.Start, "m", new(0, 0, 0),    new(100, 100, 100),  UpdatedAtUnix: 0);
    private static Zone EndZone() =>
        new(ZoneKind.End,   "m", new(1000, 0, 0), new(1100, 100, 100), UpdatedAtUnix: 0);

    private static TimerEngine MakeEngine()
    {
        var e = new TimerEngine();
        e.SetZones(StartZone(), EndZone());
        return e;
    }

    [Fact]
    public void Idle_player_far_from_both_zones_stays_idle()
    {
        var e = MakeEngine();
        var f = e.Tick(slot: 0, position: new Vector3(500, 50, 50), nowTickMs: 0);

        Assert.Null(f);
        Assert.Equal(RunState.Idle, e.GetRun(0).State);
    }

    [Fact]
    public void Entering_start_transitions_to_InStart()
    {
        var e = MakeEngine();
        e.Tick(0, new Vector3(50, 50, 50), nowTickMs: 0);

        Assert.Equal(RunState.InStart, e.GetRun(0).State);
    }

    [Fact]
    public void Leaving_start_transitions_to_Running_with_start_tick()
    {
        var e = MakeEngine();
        e.Tick(0, new Vector3(50, 50, 50),   nowTickMs: 1000);
        e.Tick(0, new Vector3(500, 50, 50),  nowTickMs: 1500);

        var run = e.GetRun(0);
        Assert.Equal(RunState.Running, run.State);
        Assert.Equal(1500, run.StartTickMs);
    }

    [Fact]
    public void Entering_end_while_running_emits_finished_with_elapsed()
    {
        var e = MakeEngine();
        e.Tick(0, new Vector3(50, 50, 50),    nowTickMs: 1000);
        e.Tick(0, new Vector3(500, 50, 50),   nowTickMs: 2000); // Running
        var finished = e.Tick(0, new Vector3(1050, 50, 50), nowTickMs: 8000);

        Assert.NotNull(finished);
        Assert.Equal(0, finished!.Value.Slot);
        Assert.Equal(6000, finished.Value.ElapsedMs);
        Assert.Equal(RunState.Idle, e.GetRun(0).State); // flushed to Idle same tick
    }

    [Fact]
    public void Re_entering_start_while_running_resets_to_InStart()
    {
        var e = MakeEngine();
        e.Tick(0, new Vector3(50, 50, 50),   0);
        e.Tick(0, new Vector3(500, 50, 50),  500);  // Running
        e.Tick(0, new Vector3(50, 50, 50),   1000); // back in start

        Assert.Equal(RunState.InStart, e.GetRun(0).State);
    }

    [Fact]
    public void Missing_zones_skip_all_transitions()
    {
        var e = new TimerEngine();
        var f = e.Tick(0, new Vector3(50, 50, 50), 0);

        Assert.Null(f);
        Assert.Equal(RunState.Idle, e.GetRun(0).State);
    }

    [Fact]
    public void ResetAll_returns_every_player_to_idle()
    {
        var e = MakeEngine();
        e.Tick(0, new Vector3(50, 50, 50), 0);
        e.Tick(1, new Vector3(500, 50, 50), 0);

        e.ResetAll();

        Assert.Equal(RunState.Idle, e.GetRun(0).State);
        Assert.Equal(RunState.Idle, e.GetRun(1).State);
    }

    [Fact]
    public void Remove_evicts_player_state()
    {
        var e = MakeEngine();
        e.Tick(0, new Vector3(50, 50, 50), 0);

        e.Remove(0);

        // Fresh GetRun creates a new Idle run
        Assert.Equal(RunState.Idle, e.GetRun(0).State);
    }

    [Fact]
    public void Per_player_state_is_isolated()
    {
        var e = MakeEngine();

        e.Tick(0, new Vector3(50, 50, 50),  0);
        e.Tick(1, new Vector3(500, 50, 50), 0);

        Assert.Equal(RunState.InStart, e.GetRun(0).State);
        Assert.Equal(RunState.Idle,    e.GetRun(1).State);
    }
}
```

- [ ] **Step 2: Implement TimerEngine**

```csharp
using System.Numerics;
using LockTimer.Zones;

namespace LockTimer.Timing;

public sealed class TimerEngine
{
    private readonly Dictionary<int, PlayerRun> _runs = new();
    private Zone? _start;
    private Zone? _end;

    public void SetZones(Zone? start, Zone? end)
    {
        _start = start;
        _end   = end;
    }

    public void Remove(int slot) => _runs.Remove(slot);

    public void ResetAll()
    {
        foreach (var run in _runs.Values)
        {
            run.State       = RunState.Idle;
            run.StartTickMs = 0;
        }
    }

    public PlayerRun GetRun(int slot)
    {
        if (!_runs.TryGetValue(slot, out var run))
        {
            run = new PlayerRun();
            _runs[slot] = run;
        }
        return run;
    }

    public FinishedRun? Tick(int slot, Vector3 position, long nowTickMs)
    {
        if (_start is null || _end is null) return null;

        var run    = GetRun(slot);
        bool inStart = _start.Contains(position);
        bool inEnd   = _end.Contains(position);

        switch (run.State)
        {
            case RunState.Idle:
                if (inStart) run.State = RunState.InStart;
                return null;

            case RunState.InStart:
                if (!inStart)
                {
                    run.State       = RunState.Running;
                    run.StartTickMs = nowTickMs;
                }
                return null;

            case RunState.Running:
                if (inStart)
                {
                    run.State       = RunState.InStart;
                    run.StartTickMs = 0;
                    return null;
                }
                if (inEnd)
                {
                    long elapsed = nowTickMs - run.StartTickMs;
                    if (elapsed < 0) elapsed = 0;
                    if (elapsed > int.MaxValue) elapsed = int.MaxValue;
                    run.State       = RunState.Idle;
                    run.StartTickMs = 0;
                    return new FinishedRun(slot, (int)elapsed);
                }
                return null;

            case RunState.Finished:
                run.State = RunState.Idle;
                return null;

            default:
                return null;
        }
    }
}
```

- [ ] **Step 3: Run tests**

Run: `dotnet test LockTimer.Tests/LockTimer.Tests.csproj --filter FullyQualifiedName~TimerEngineTests`
Expected: 9 passed.

- [ ] **Step 4: Commit**

```bash
git add Timing/TimerEngine.cs LockTimer.Tests/TimerEngineTests.cs
git commit -m "feat(locktimer): add pure-logic TimerEngine FSM"
```

---

# Phase 4 — Zone editor, commands, and shell wiring

Now we start touching DeadworksManaged.Api. Nothing in this phase is unit-tested — tests would require the game. We trade test coverage for type-checking against the real API.

## Task 4.1 — ZoneEditor (pending points + raycast)

**Files:**
- Create: `Zones/ZoneEditor.cs`

- [ ] **Step 1: Create ZoneEditor**

```csharp
using System.Numerics;
using DeadworksManaged.Api;
using LockTimer.Timing;

namespace LockTimer.Zones;

public sealed class ZoneEditor
{
    private readonly ZoneRepository _zones;
    private readonly TimerEngine _engine;

    private Vector3? _pendingStart1;
    private Vector3? _pendingStart2;
    private Vector3? _pendingEnd1;
    private Vector3? _pendingEnd2;

    public ZoneEditor(ZoneRepository zones, TimerEngine engine)
    {
        _zones  = zones;
        _engine = engine;
    }

    public EditResult CaptureStart1(CCitadelPlayerPawn pawn) => Capture(pawn, ref _pendingStart1, "start p1");
    public EditResult CaptureStart2(CCitadelPlayerPawn pawn) => Capture(pawn, ref _pendingStart2, "start p2");
    public EditResult CaptureEnd1(CCitadelPlayerPawn pawn)   => Capture(pawn, ref _pendingEnd1,   "end p1");
    public EditResult CaptureEnd2(CCitadelPlayerPawn pawn)   => Capture(pawn, ref _pendingEnd2,   "end p2");

    private EditResult Capture(CCitadelPlayerPawn pawn, ref Vector3? slot, string label)
    {
        var eye    = pawn.EyePosition;
        var angles = pawn.ViewAngles;

        var trace = CGameTrace.Create();
        Trace.SimpleTraceAngles(
            eye, angles,
            RayType_t.Line, RnQueryObjectSet.All,
            MaskTrace.Solid, MaskTrace.Empty, MaskTrace.Empty,
            CollisionGroup.Always, ref trace,
            filterEntity: pawn,
            maxDistance: 8192f);

        if (!trace.DidHit)
            return EditResult.Miss($"no surface hit within 8192u for {label}");

        var hit = eye + ComputeForward(angles) * (trace.Fraction * 8192f);
        slot = hit;
        return EditResult.Ok($"{label} set at ({hit.X:F1}, {hit.Y:F1}, {hit.Z:F1})");
    }

    private static Vector3 ComputeForward(Vector3 angles)
    {
        float pitch = angles.X * MathF.PI / 180f;
        float yaw   = angles.Y * MathF.PI / 180f;
        return new Vector3(
            MathF.Cos(pitch) * MathF.Cos(yaw),
            MathF.Cos(pitch) * MathF.Sin(yaw),
            -MathF.Sin(pitch));
    }

    public SaveResult SaveZones(string map, long nowUnix)
    {
        var missing = new List<string>();
        if (_pendingStart1 is null) missing.Add("start1");
        if (_pendingStart2 is null) missing.Add("start2");
        if (_pendingEnd1   is null) missing.Add("end1");
        if (_pendingEnd2   is null) missing.Add("end2");
        if (missing.Count > 0)
            return SaveResult.Failure($"need 4 points — missing: {string.Join(", ", missing)}");

        var start = Zone.FromCorners(ZoneKind.Start, map, _pendingStart1!.Value, _pendingStart2!.Value, nowUnix);
        var end   = Zone.FromCorners(ZoneKind.End,   map, _pendingEnd1!.Value,   _pendingEnd2!.Value,   nowUnix);

        if (start.IsZeroVolume) return SaveResult.Failure("start zone has zero volume");
        if (end.IsZeroVolume)   return SaveResult.Failure("end zone has zero volume");

        _zones.Upsert(start);
        _zones.Upsert(end);
        _engine.SetZones(start, end);

        _pendingStart1 = _pendingStart2 = _pendingEnd1 = _pendingEnd2 = null;

        return SaveResult.Success(start, end);
    }

    public void DeleteZones(string map)
    {
        _zones.DeleteForMap(map);
        _engine.SetZones(null, null);
        _engine.ResetAll();
    }

    public PendingStatus GetPendingStatus()
        => new(_pendingStart1.HasValue, _pendingStart2.HasValue, _pendingEnd1.HasValue, _pendingEnd2.HasValue);
}

public readonly record struct EditResult(bool Ok, string Message)
{
    public static EditResult Ok(string m)   => new(true,  m);
    public static EditResult Miss(string m) => new(false, m);
}

public readonly record struct SaveResult(bool Ok, string Message, Zone? Start, Zone? End)
{
    public static SaveResult Success(Zone s, Zone e) => new(true,  $"zones saved for {s.Map}", s, e);
    public static SaveResult Failure(string m)       => new(false, m, null, null);
}

public readonly record struct PendingStatus(bool Start1, bool Start2, bool End1, bool End2);
```

- [ ] **Step 2: Verify plugin builds**

Run: `dotnet build LockTimer.csproj`
Expected: 0 errors. You may see 0 warnings thanks to `TreatWarningsAsErrors`. If the `Trace.SimpleTraceAngles` signature in your local DeadworksManaged.Api.dll differs from `deadworks/managed/DeadworksManaged.Api/Trace/TraceSystem.cs`, fix the call site — the source in `deadworks/` is authoritative.

- [ ] **Step 3: Commit**

```bash
git add Zones/ZoneEditor.cs
git commit -m "feat(locktimer): add ZoneEditor with crosshair raycast capture"
```

---

## Task 4.2 — ZoneRenderer (corner + edge-midpoint markers)

**Files:**
- Create: `Zones/ZoneRenderer.cs`

**API constraint.** `CParticleSystem.Builder.WithDataCP` in the vendored DeadworksManaged.Api (`deadworks/managed/DeadworksManaged.Api/ParticleSystem.cs` lines 45–82) only stores ONE data control point per particle — `_dataCP` and `_dataCPValue` are single scalar fields, not a dict. That rules out two-CP beam effects (CP0=start, CP1=end) unless we extend the API. To ship without touching the vendored repo, the renderer spawns a static marker particle at each of the 8 corners of the AABB *plus* one at the midpoint of each of the 12 edges — 20 particles per zone total. That gives a clearly visible glowing outline without needing beam primitives.

**Particle effect path.** The chosen effect is `"particles/ui_mouseactions/ping_world.vpcf"` — a compact glowing sprite that ships with Deadlock and works as a static position marker. If that path doesn't resolve at runtime (the particle system silently does nothing), swap to any other single-point particle you can find under `game/citadel/pak01_dir/particles/ui/` or `particles/generic_gameplay/`. This is the one knob to tune after first in-game load.

- [ ] **Step 1: Create ZoneRenderer**

```csharp
using System.Drawing;
using System.Numerics;
using DeadworksManaged.Api;

namespace LockTimer.Zones;

public sealed class ZoneRenderer
{
    // Static marker particle. See Task 4.2 notes for swap candidates if this
    // doesn't render visibly on first in-game load.
    private const string MarkerParticle = "particles/ui_mouseactions/ping_world.vpcf";

    private readonly Dictionary<ZoneKind, List<CParticleSystem>> _spawned = new();

    public void Render(Zone zone)
    {
        Clear(zone.Kind);

        var color = zone.Kind == ZoneKind.Start ? Color.LimeGreen : Color.Red;
        var markers = new List<CParticleSystem>(20);

        foreach (var point in OutlinePoints(zone.Min, zone.Max))
        {
            var p = CParticleSystem
                .Create(MarkerParticle)
                .AtPosition(point)
                .WithTint(color, tintCP: 0)
                .StartActive(true)
                .Spawn();
            if (p is not null) markers.Add(p);
        }

        _spawned[zone.Kind] = markers;
    }

    public void Clear(ZoneKind kind)
    {
        if (!_spawned.TryGetValue(kind, out var list)) return;
        foreach (var p in list) p.Destroy();
        list.Clear();
        _spawned.Remove(kind);
    }

    public void ClearAll()
    {
        foreach (var list in _spawned.Values)
            foreach (var p in list) p.Destroy();
        _spawned.Clear();
    }

    private static IEnumerable<Vector3> OutlinePoints(Vector3 min, Vector3 max)
    {
        // 8 corners
        var c000 = new Vector3(min.X, min.Y, min.Z);
        var c100 = new Vector3(max.X, min.Y, min.Z);
        var c010 = new Vector3(min.X, max.Y, min.Z);
        var c110 = new Vector3(max.X, max.Y, min.Z);
        var c001 = new Vector3(min.X, min.Y, max.Z);
        var c101 = new Vector3(max.X, min.Y, max.Z);
        var c011 = new Vector3(min.X, max.Y, max.Z);
        var c111 = new Vector3(max.X, max.Y, max.Z);

        yield return c000; yield return c100; yield return c010; yield return c110;
        yield return c001; yield return c101; yield return c011; yield return c111;

        // 12 edge midpoints — makes the outline readable even on large zones
        yield return Vector3.Lerp(c000, c100, 0.5f);
        yield return Vector3.Lerp(c100, c110, 0.5f);
        yield return Vector3.Lerp(c110, c010, 0.5f);
        yield return Vector3.Lerp(c010, c000, 0.5f);
        yield return Vector3.Lerp(c001, c101, 0.5f);
        yield return Vector3.Lerp(c101, c111, 0.5f);
        yield return Vector3.Lerp(c111, c011, 0.5f);
        yield return Vector3.Lerp(c011, c001, 0.5f);
        yield return Vector3.Lerp(c000, c001, 0.5f);
        yield return Vector3.Lerp(c100, c101, 0.5f);
        yield return Vector3.Lerp(c110, c111, 0.5f);
        yield return Vector3.Lerp(c010, c011, 0.5f);
    }
}
```

Future upgrade path (out of scope for this plan, but documented here so nobody re-derives it): to render true glowing edges, extend `CParticleSystem.Builder` in `deadworks/managed/DeadworksManaged.Api/ParticleSystem.cs` to accept a `Dictionary<int, Vector3>` of data CPs instead of a single scalar, then change `Spawn()` to write each CP via the schema array accessor. At that point this renderer can switch to one particle per edge with CP0/CP1 set to the endpoints.

- [ ] **Step 2: Verify build**

Run: `dotnet build LockTimer.csproj`
Expected: 0 errors. If `Builder.WithDataCP` or `.Spawn()` signatures differ from `deadworks/managed/DeadworksManaged.Api/ParticleSystem.cs`, adjust the chain.

- [ ] **Step 3: Commit**

```bash
git add Zones/ZoneRenderer.cs
git commit -m "feat(locktimer): add ZoneRenderer for AABB edge particles"
```

---

## Task 4.3 — ChatCommands

**Files:**
- Create: `Commands/ChatCommands.cs`

- [ ] **Step 1: Create ChatCommands.cs**

```csharp
using DeadworksManaged.Api;
using LockTimer.Records;
using LockTimer.Timing;
using LockTimer.Zones;

namespace LockTimer.Commands;

public sealed class ChatCommands
{
    private readonly ZoneEditor _editor;
    private readonly ZoneRenderer _renderer;
    private readonly RecordRepository _records;
    private readonly TimerEngine _engine;

    public ChatCommands(
        ZoneEditor editor,
        ZoneRenderer renderer,
        RecordRepository records,
        TimerEngine engine)
    {
        _editor   = editor;
        _renderer = renderer;
        _records  = records;
        _engine   = engine;
    }

    private static CCitadelPlayerPawn? PawnOf(ChatMessage msg)
        => msg.Player?.Pawn?.As<CCitadelPlayerPawn>();

    private static void Reply(ChatMessage msg, string text)
        => Chat.SayToPlayer(msg.Player, $"[LockTimer] {text}");

    [ChatCommand("!start1")]
    public void OnStart1(ChatMessage msg)
    {
        var pawn = PawnOf(msg); if (pawn is null) return;
        Reply(msg, _editor.CaptureStart1(pawn).Message);
    }

    [ChatCommand("!start2")]
    public void OnStart2(ChatMessage msg)
    {
        var pawn = PawnOf(msg); if (pawn is null) return;
        Reply(msg, _editor.CaptureStart2(pawn).Message);
    }

    [ChatCommand("!end1")]
    public void OnEnd1(ChatMessage msg)
    {
        var pawn = PawnOf(msg); if (pawn is null) return;
        Reply(msg, _editor.CaptureEnd1(pawn).Message);
    }

    [ChatCommand("!end2")]
    public void OnEnd2(ChatMessage msg)
    {
        var pawn = PawnOf(msg); if (pawn is null) return;
        Reply(msg, _editor.CaptureEnd2(pawn).Message);
    }

    [ChatCommand("!savezones")]
    public void OnSaveZones(ChatMessage msg)
    {
        var result = _editor.SaveZones(Server.MapName, DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        Reply(msg, result.Message);
        if (!result.Ok) return;

        _renderer.Render(result.Start!);
        _renderer.Render(result.End!);
    }

    [ChatCommand("!delzones")]
    public void OnDelZones(ChatMessage msg)
    {
        _editor.DeleteZones(Server.MapName);
        _renderer.ClearAll();
        Reply(msg, $"zones cleared for {Server.MapName}");
    }

    [ChatCommand("!zones")]
    public void OnZonesStatus(ChatMessage msg)
    {
        var p = _editor.GetPendingStatus();
        Reply(msg, $"pending: start1={p.Start1} start2={p.Start2} end1={p.End1} end2={p.End2}");
    }

    [ChatCommand("!pb")]
    public void OnPb(ChatMessage msg)
    {
        var sid = (long)(msg.Player?.SteamId ?? 0);
        var pb  = _records.GetPb(sid, Server.MapName);
        Reply(msg, pb is null
            ? "no PB yet"
            : $"your PB on {Server.MapName}: {TimeFormatter.FormatTime(pb.TimeMs)}");
    }

    [ChatCommand("!top")]
    public void OnTop(ChatMessage msg)
    {
        var top = _records.GetTop(Server.MapName, limit: 10);
        if (top.Count == 0)
        {
            Reply(msg, $"no records on {Server.MapName} yet");
            return;
        }
        for (int i = 0; i < top.Count; i++)
        {
            var r = top[i];
            Reply(msg, $"{i + 1}. {r.PlayerName} {TimeFormatter.FormatTime(r.TimeMs)}");
        }
    }

    [ChatCommand("!reset")]
    public void OnReset(ChatMessage msg)
    {
        var slot = msg.Player?.Slot ?? -1;
        if (slot < 0) return;
        _engine.Remove(slot);
        Reply(msg, "run reset");
    }
}
```

Notes about API shapes:
- `ChatMessage.Player` and `.SteamId` / `.Slot` / `.Pawn` come from the real `ChatMessage` record in DeadworksManaged.Api. If your version differs, adjust the accessors — the source is in `deadworks/managed/DeadworksManaged.Api/Events/ChatMessage.cs`.
- `Chat.SayToPlayer(...)` is the expected helper per other example plugins; if it's named differently, grep `Chat.Say` in `deadworks/managed/plugins/` for the correct call and substitute.

- [ ] **Step 2: Verify build**

Run: `dotnet build LockTimer.csproj`
Expected: 0 errors.

- [ ] **Step 3: Commit**

```bash
git add Commands/ChatCommands.cs
git commit -m "feat(locktimer): add chat commands for zones and records"
```

---

# Phase 5 — Plugin shell integration

Wire everything into `LockTimerPlugin` so the Deadworks loader can actually run it.

## Task 5.1 — Expand LockTimerPlugin

**Files:**
- Modify: `LockTimerPlugin.cs`

- [ ] **Step 1: Rewrite the plugin shell**

```csharp
using System.IO;
using DeadworksManaged.Api;
using LockTimer.Commands;
using LockTimer.Data;
using LockTimer.Records;
using LockTimer.Timing;
using LockTimer.Zones;

namespace LockTimer;

public class LockTimerPlugin : DeadworksPluginBase
{
    public override string Name => "LockTimer";

    private LockTimerDb? _db;
    private ZoneRepository? _zones;
    private RecordRepository? _records;
    private ZoneRenderer? _renderer;
    private TimerEngine? _engine;
    private ZoneEditor? _editor;
    private ChatCommands? _commands;

    public override void OnLoad(bool isReload)
    {
        try
        {
            var dir = Path.Combine(AppContext.BaseDirectory, "LockTimer");
            Directory.CreateDirectory(dir);
            var dbPath = Path.Combine(dir, "locktimer.db");

            _db       = LockTimerDb.Open(dbPath);
            _zones    = new ZoneRepository(_db.Connection);
            _records  = new RecordRepository(_db.Connection);
            _renderer = new ZoneRenderer();
            _engine   = new TimerEngine();
            _editor   = new ZoneEditor(_zones, _engine);
            _commands = new ChatCommands(_editor, _renderer, _records, _engine);

            PluginRegistry.RegisterChatCommands(this, _commands);

            Console.WriteLine($"[{Name}] {(isReload ? "Reloaded" : "Loaded")}. DB: {dbPath}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[{Name}] OnLoad failed: {ex}");
        }
    }

    public override void OnUnload()
    {
        try
        {
            _renderer?.ClearAll();
            _db?.Dispose();
            Console.WriteLine($"[{Name}] Unloaded.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[{Name}] OnUnload failed: {ex}");
        }
    }

    public override void OnStartupServer()
    {
        try
        {
            if (_zones is null || _engine is null || _renderer is null) return;

            _renderer.ClearAll();
            _engine.ResetAll();

            var map = Server.MapName;
            if (string.IsNullOrEmpty(map)) return;

            var zones = _zones.GetForMap(map);
            var start = zones.FirstOrDefault(z => z.Kind == ZoneKind.Start);
            var end   = zones.FirstOrDefault(z => z.Kind == ZoneKind.End);
            _engine.SetZones(start, end);

            if (start is not null) _renderer.Render(start);
            if (end   is not null) _renderer.Render(end);

            Console.WriteLine($"[{Name}] Loaded {zones.Count} zone(s) for {map}.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[{Name}] OnStartupServer failed: {ex}");
        }
    }

    public override void OnClientDisconnect(ClientDisconnectedEvent args)
    {
        _engine?.Remove(args.Slot);
    }

    public override void OnGameFrame(bool simulating, bool firstTick, bool lastTick)
    {
        if (!simulating || _engine is null || _records is null) return;

        try
        {
            long now = Environment.TickCount64;

            foreach (var player in Players.All)
            {
                if (player.IsBot) continue;
                var pawn = player.Pawn?.As<CCitadelPlayerPawn>();
                if (pawn is null) continue;

                var finished = _engine.Tick(player.Slot, pawn.Position, now);
                if (finished is null) continue;

                OnRunFinished(player, finished.Value);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[{Name}] OnGameFrame failed: {ex}");
        }
    }

    private void OnRunFinished(CCitadelPlayerController player, FinishedRun run)
    {
        if (_records is null) return;

        long steamId = (long)player.SteamId;
        var result = _records.UpsertIfFaster(
            steamId: steamId,
            map: Server.MapName,
            timeMs: run.ElapsedMs,
            playerName: player.PlayerName,
            nowUnix: DateTimeOffset.UtcNow.ToUnixTimeSeconds());

        var formatted = TimeFormatter.FormatTime(run.ElapsedMs);
        string msg;
        if (result.Changed && result.PreviousMs is null)
            msg = $"[LockTimer] {player.PlayerName} finished in {formatted} (new PB!)";
        else if (result.Changed)
            msg = $"[LockTimer] {player.PlayerName} finished in {formatted} " +
                  $"(new PB! prev {TimeFormatter.FormatTime(result.PreviousMs!.Value)})";
        else
            msg = $"[LockTimer] {player.PlayerName} finished in {formatted} " +
                  $"(pb {TimeFormatter.FormatTime(result.PreviousMs!.Value)})";

        Chat.SayToAll(msg);
    }
}
```

API reality-check items — if your DeadworksManaged.Api version differs from the source at `deadworks/managed/DeadworksManaged.Api/`, these are the call sites to adjust:

- `Players.All` — enumeration of connected controllers. Check `deadworks/managed/DeadworksManaged.Api/Entities/Players.cs` for the actual method.
- `player.Pawn?.As<CCitadelPlayerPawn>()` — if `Pawn` isn't a direct property, fetch via `NativeInterop.GetPlayerPawn(slot)` or similar.
- `PluginRegistry.RegisterChatCommands(this, _commands)` — chat command registration entry point. If commands on the plugin class itself are auto-registered but a separate class needs explicit registration, check how `DeathmatchPlugin` handles it. You may need to move command methods onto the plugin class and forward to `_commands`, or expose a registry call that the plugin loader supports.
- `Chat.SayToAll` / `Chat.SayToPlayer` — actual names in the referenced DeadworksManaged.Api version. Grep `Chat.Say` in `../../deadworks/managed/plugins/` for the canonical invocation.
- `CCitadelPlayerController.SteamId` and `.PlayerName` — confirm in `deadworks/managed/DeadworksManaged.Api/Entities/PlayerEntities.cs`. If `SteamId` lives on `Player`/controller differently, adapt.

If any of these resist a clean fix, the simplest workaround is moving `[ChatCommand]` methods directly onto `LockTimerPlugin` (Deadworks plugin loader scans the plugin class itself for attributes — see `PluginLoader.Events.cs`) and have each command delegate to the stored service fields. That refactor is safe and doesn't change behavior — keep it in the same commit.

- [ ] **Step 2: Verify plugin builds**

Run: `dotnet build LockTimer.csproj`
Expected: 0 errors, 0 warnings. Iterate on the API-reality items above until it builds clean.

- [ ] **Step 3: Commit**

```bash
git add LockTimerPlugin.cs
git commit -m "feat(locktimer): wire plugin shell — zones, timer, records, commands"
```

---

# Phase 6 — Polish

## Task 6.1 — Add plugin README with manual smoke checklist

**Files:**
- Create: `README.md`

- [ ] **Step 1: Write README**

```markdown
# LockTimer

Minimalist speedrun-timer plugin for Deadlock (Deadworks managed).

## Commands

| Command | Effect |
|---|---|
| `!start1` / `!start2` | Capture corner 1 / 2 of the start zone at crosshair hit |
| `!end1` / `!end2` | Capture corner 1 / 2 of the end zone |
| `!savezones` | Persist both zones for the current map and render edges |
| `!delzones` | Remove zones for the current map |
| `!zones` | Show which pending corners are staged |
| `!pb` | Show your PB on the current map |
| `!top` | Show top-10 times on the current map |
| `!reset` | Reset your own run state |

Timer starts when your feet leave the start zone and stops when they enter the end zone. Re-entering start while running resets you to InStart.

## Database

SQLite file at `…/managed/plugins/LockTimer/locktimer.db`. PB records only (one row per `steam_id, map`).

## Manual smoke checklist

After building and loading:

- [ ] 40 marker particles (20 per zone: 8 corners + 12 edge midpoints) spawn on `!savezones`
- [ ] Walking from start to end records a time in chat
- [ ] Beating your PB shows `(new PB! prev …)` message
- [ ] Slower run shows `(pb …)` message, no DB change
- [ ] `!delzones` removes particles and clears the DB rows
- [ ] Disconnect mid-run, reconnect — no stale state
- [ ] Map change mid-run abandons the run cleanly
```

- [ ] **Step 2: Commit**

```bash
git add README.md
git commit -m "docs(locktimer): add README with commands and smoke checklist"
```

---

## Task 6.2 — Final build-and-test clean pass

- [ ] **Step 1: Full solution build**

Run: `dotnet build LockTimer.csproj LockTimer.Tests/LockTimer.Tests.csproj`
Expected: 0 errors, 0 warnings across both projects.

- [ ] **Step 2: Full test run**

Run: `dotnet test LockTimer.Tests/LockTimer.Tests.csproj`
Expected: 33 passed (5 ZoneTests + 2 LockTimerDbTests + 4 ZoneRepositoryTests + 8 TimeFormatterTests + 5 RecordRepositoryTests + 9 TimerEngineTests).

- [ ] **Step 3: Confirm no stray markers left**

Run: `grep -rn "TODO\|FIXME\|XXX" .`
Expected: no matches. (The particle path in `ZoneRenderer.cs` is a documented constant, not a TODO.)

- [ ] **Step 4: Commit if anything touched**

```bash
git status
git commit -am "chore(locktimer): final clean pass" # only if there are changes
```

---

# Task summary

| Phase | Tasks | Tests |
|---|---|---|
| 1. Scaffold | 1.1, 1.2, 1.3 | — |
| 2. Data layer | 2.1, 2.2, 2.3, 2.4, 2.5 | 24 |
| 3. Timer engine | 3.1, 3.2 | 9 |
| 4. Editor/renderer/commands | 4.1, 4.2, 4.3 | — |
| 5. Shell integration | 5.1 | — |
| 6. Polish | 6.1, 6.2 | — |

Total: 15 tasks, 33 unit tests, ~12 commits.
