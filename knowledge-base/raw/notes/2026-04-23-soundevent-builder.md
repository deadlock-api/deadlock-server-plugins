---
date: 2026-04-23
task: scan ../deadworks for new knowledge — SoundEvent builder API
files:
  - ../deadworks/managed/DeadworksManaged.Api/Sounds/SoundEvent.cs
  - ../deadworks/managed/DeadworksManaged.Api/Sounds/SoundEventField.cs
  - ../deadworks/managed/DeadworksManaged.Api/Sounds/Sounds.cs
commits:
  - c0f977b ("add apis for running soundevents on players")
---

`[[plugin-api-surface]]` currently describes Sounds as
*"`Sounds.Play`, `Sounds.PlayAt`; v0.4.5 adds single-player target path"*.
That's a significant understatement — commit `c0f977b` (2026-04-22)
added a full **builder API** with per-field parameters, GUID-based
sound lifecycle, and stop-by-name.

## Builder API

```csharp
new SoundEvent("UI.SomeSoundName") {
        Volume = 0.8f,           // shortcut for SetFloat("public.volume", ...)
        Pitch  = 1.2f,           // shortcut for SetFloat("public.pitch", ...)
        SourceEntityIndex = -1,  // -1 = play at listener's own position
        StartTime = 0.0f,
    }
    .SetFloat("my.custom_param", 5.0f)
    .SetUInt32("foo", 42)
    .SetFloat3("origin", x, y, z)
    .SetBool("loop", true)
    .Emit(RecipientFilter.Single(playerSlot));  // returns uint GUID
```

Field setters are fluent (all return `this`) and typed:
`SetBool / SetInt32 / SetUInt32 / SetUInt64 / SetFloat / SetFloat3`.
Read-back getters: `HasField`, `TryGetFloat/Int32/Bool`.

## GUID-addressable sound lifecycle

`.Emit()` returns a `uint GUID` allocated by
`NativeInterop.TakeSoundEventGuid()`. With it:

- `soundEvent.SetParams(guid, recipients)` — live-update params of a
  playing sound (e.g., volume fade, panning).
- `SoundEvent.Stop(guid, recipients)` — stop a specific instance.
- `SoundEvent.StopByName(name, sourceEntityIndex, recipients)` — stop
  all playing instances of that named event on that source entity.

## Wire format (non-obvious)

Per-field SOS-packed-params format is hand-rolled in
`PackFields()` (`SoundEvent.cs:186-214`):

```
[4B LE field-name hash][1B type][1B payload size][1B pad=0][N B LE payload]
```

Field name hash is `MurmurHash2.HashLowerCase(fieldName,
SosHashSeeds.FieldName)` — lowercased, murmur2. Soundevent name
itself is hashed with `SosHashSeeds.SoundeventName`. Message is
`CMsgSosStartSoundEvent` (`SetParams`→`CMsgSosSetSoundEventParams`,
`Stop`→`CMsgSosStopSoundEvent`, `StopByName`→`CMsgSosStopSoundEventHash`).

## `RecipientFilter` targeting

`.Emit(RecipientFilter.Single(slot))` — one player (the "single-player
target path" the wiki mentions in passing).
`.Emit(RecipientFilter.All)` — everyone (via `NetMessages.Send`).
Also `RecipientFilter.Team`, custom filters via `NetMessages`.

## Helper shorthands

`Sounds.Play(name, recipients, volume=1f, pitch=1f)` — listener-local
play.
`Sounds.PlayAt(name, sourceEntityIndex, recipients, volume=1f, pitch=1f)`
— entity-anchored (spatial).

Both return the same GUID `SoundEvent.Emit` does — you can stop or
mutate helper-launched sounds later.

## What's non-obvious from the wiki

1. The **builder exists** with arbitrary SOS param setting — not just
   volume/pitch. `[[plugin-api-surface]]` should mention `SoundEvent`
   as a builder, not just a helper class.
2. **GUID round-trip** — sounds can be addressed after they start.
3. **MurmurHash2 hash matters** — field names are hashed
   lowercase with specific seeds. Custom sound events in `.vsndevts`
   need to match the hash on the client.
4. **v0.4.5 wiki note undersells this** — commit `c0f977b` is
   post-v0.4.5 (2026-04-22), so the "v0.4.5 adds single-player target
   path" line is probably conflating the helper path with the builder
   addition. Worth reconciling.
