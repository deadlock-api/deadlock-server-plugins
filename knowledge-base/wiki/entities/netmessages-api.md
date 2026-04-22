---
title: NetMessages API
type: entity
sources:
  - raw/notes/2026-04-22-deadworks-netmessages-api.md
  - ../deadworks/managed/DeadworksManaged.Api/NetMessages/
  - ../deadworks/examples/plugins/ChatRelayPlugin/ChatRelayPlugin.cs
related:
  - "[[plugin-api-surface]]"
  - "[[protobuf-pipeline]]"
  - "[[examples-index]]"
  - "[[events-surface]]"
created: 2026-04-22
updated: 2026-04-22
confidence: high
---

# NetMessages API

Protobuf message **sending** and **hooking** for Source 2 network
messages. Used for HUD announcements, chat relay, client-facing UI
messages, etc.

## Send

```csharp
public static void Send<T>(T message, RecipientFilter recipients)
    where T : IMessage<T>
```

`NetMessageRegistry.GetMessageId<T>()` resolves the ID; throws
`InvalidOperationException` if unregistered. Serialization is
`message.ToByteArray()`.

```csharp
var msg = new CCitadelUserMsg_HudGameAnnouncement {
    TitleLocstring = "ROLL THE DICE",
    DescriptionLocstring = "Piano Strike"
};
NetMessages.Send(msg, RecipientFilter.Single(caller.EntityIndex - 1));
```

## Hook

```csharp
public static IHandle HookOutgoing<T>(Func<OutgoingMessageContext<T>, HookResult>);
public static IHandle HookIncoming<T>(Func<IncomingMessageContext<T>, HookResult>);
```

Handler returns `HookResult.Handled` to **suppress** the message
(drop the engine's send, or ignore the incoming). `Continue` passes
through. `IHandle.Cancel()` unhooks; paired `UnhookOutgoing<T>` /
`UnhookIncoming<T>` also available but require the exact same delegate
instance.

## Attribute-driven auto-registration

Preferred over manual `HookOutgoing<T>()` in `OnLoad`. Mark a method
with `[NetMessageHandler]` and take the appropriate context type:

```csharp
[NetMessageHandler]
public HookResult OnChatMsgOutgoing(OutgoingMessageContext<CCitadelUserMsg_ChatMsg> ctx)
{ ... }
```

The host reflectively scans for these methods and registers them via
`PluginRegistrationTracker`, so they're unregistered automatically on
plugin unload.

## `NetMessageRegistry` — runtime reflection (NOT source generation)

ID-to-type map built on first `EnsureInitialized()`:

1. Scan the assembly for `IMessage` types, build
   `Dictionary<string, Type>` keyed by `MessageDescriptor.Name`
   (e.g. `"CCitadelUserMsg_ChatMsg"`).
2. Iterate every `FileDescriptor` once, scanning its `EnumTypes`.
3. Known enum names → name-mapping rules (table below). For each enum
   value, derive the corresponding proto type name and
   `Register(type, value.Number)`.

### Enum → proto name mapping rules

(`NetMessageRegistry.cs:99-113`)

| Enum | Value prefix | Message name prefix |
|------|--------------|---------------------|
| `NET_Messages` | `net_` | `CNETMsg_` |
| `CitadelUserMessageIds` | `k_EUserMsg_` | `CCitadelUserMsg_` |
| `ECitadelClientMessages` | `CITADEL_CM_` | `CCitadelClientMsg_` |
| `EBaseUserMessages` | `UM_` | `CUserMessage` |
| `EBaseEntityMessages` | `EM_` | `CEntityMessage` |
| `CLC_Messages` | `clc_` | `CCLCMsg_` |
| `SVC_Messages`, `SVC_Messages_LowFrequency` | `svc_` | `CSVCMsg_` |
| `Bidirectional_Messages` (+LowFrequency) | `bi_` | `CBidirMsg_` |
| `EBaseGameEvents` | `GE_` | `CMsg` |
| `ETEProtobufIds` | `TE_` | `CMsgTE` |

Manual override: `NetMessageRegistry.RegisterManual<T>(int id)` — bypass
the enum discovery to register custom types or override a mapping.

## `RecipientFilter` — 64-bit mask

```csharp
public struct RecipientFilter { public ulong Mask; }
```

- `RecipientFilter.All` — `ulong.MaxValue` (all 64 bits)
- `RecipientFilter.Single(slot)` — `1UL << slot`
- `Add(slot)`, `Remove(slot)`, `HasRecipient(slot)`

## Contexts

`OutgoingMessageContext<T>`:
- `T Message { get; }` — protobuf message (mutable fields)
- `RecipientFilter Recipients { get; set; }` — **settable**, hook can
  retarget
- `int MessageId { get; }`

`IncomingMessageContext<T>`:
- `T Message { get; }`
- `int SenderSlot { get; }`
- `int MessageId { get; }`

## Chat rebroadcast pattern (ChatRelayPlugin)

Deadlock's chat UI shows portraits only for the first 12 slots. To make
chat from slots 12+ visible, hook outgoing `CCitadelUserMsg_ChatMsg`,
build one message per recipient with `PlayerSlot` set to the recipient
(making the client think it came from a visible portrait), and re-send:

```csharp
if (senderSlot < 12) return HookResult.Continue;   // normal path

_rebroadcasting = true;
try {
    for (int slot = 0; slot < 64; slot++) {
        if ((originalMask & (1UL << slot)) == 0) continue;
        var msg = new CCitadelUserMsg_ChatMsg {
            PlayerSlot = slot,
            Text = slot == senderSlot ? text : $"[{senderName}]: {text}",
            AllChat = allChat,
            LaneColor = laneColor
        };
        NetMessages.Send(msg, RecipientFilter.Single(slot));
    }
} finally {
    _rebroadcasting = false;
}
return HookResult.Stop;
```

**`_rebroadcasting` guard is load-bearing** — the re-sent messages pass
through the same outgoing hook. Without the guard, it's an infinite
rebroadcast loop.

## Docker CI dependency

See [[protobuf-pipeline]] for the `Google.Protobuf` PackageReference
requirement. Plugins that send or hook net messages have protobuf types
in their public surface, so the consumer csproj must include:

```xml
<PackageReference Include="Google.Protobuf" Version="..."
                  Private="false" ExcludeAssets="runtime" />
```

Local dev resolves transitively; Docker CI links against the published
`DeadworksManaged.Api.dll` which does NOT ship `Google.Protobuf.dll`.
Symptom without this: CS0311 + cascade of CS0246 errors.
