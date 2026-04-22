---
date: 2026-04-22
task: scan deadworks NetMessages API
files:
  - ../deadworks/managed/DeadworksManaged.Api/NetMessages/NetMessages.cs
  - ../deadworks/managed/DeadworksManaged.Api/NetMessages/NetMessageRegistry.cs
  - ../deadworks/managed/DeadworksManaged.Api/NetMessages/NetMessageHandlerAttribute.cs
  - ../deadworks/managed/DeadworksManaged.Api/NetMessages/NetMessageDirection.cs
  - ../deadworks/managed/DeadworksManaged.Api/NetMessages/IncomingMessageContext.cs
  - ../deadworks/managed/DeadworksManaged.Api/NetMessages/OutgoingMessageContext.cs
  - ../deadworks/managed/DeadworksManaged.Api/NetMessages/RecipientFilter.cs
  - ../deadworks/examples/plugins/ChatRelayPlugin/ChatRelayPlugin.cs
---

# NetMessages — sending and hooking protobuf messages

## Send

```csharp
public static void Send<T>(T message, RecipientFilter recipients)
    where T : IMessage<T>
```

- `NetMessageRegistry.GetMessageId<T>()` resolves the message ID
- Throws `InvalidOperationException` if the type has no registered ID
- Serializes with `message.ToByteArray()` (protobuf), invokes host callback

## Hooking

```csharp
public static IHandle HookOutgoing<T>(Func<OutgoingMessageContext<T>, HookResult>)
public static IHandle HookIncoming<T>(Func<IncomingMessageContext<T>, HookResult>)
```

Return `HookResult.Handled` to **suppress** the message (drop the engine's
send or ignore incoming). `HookResult.Continue` passes through.

`IHandle.Cancel()` unhooks. Paired `UnhookOutgoing<T>` / `UnhookIncoming<T>`
also available but require the exact same delegate instance.

**Scan-based auto-registration:** `[NetMessageHandler]` attribute — host
reflectively scans for methods taking `OutgoingMessageContext<T>` /
`IncomingMessageContext<T>` and registers them. See the `ChatRelayPlugin`
example:

```csharp
[NetMessageHandler]
public HookResult OnChatMsgOutgoing(OutgoingMessageContext<CCitadelUserMsg_ChatMsg> ctx)
{ ... }
```

This is the preferred pattern over manual `HookOutgoing<T>()` in
`OnLoad` — attribute-driven handlers unregister automatically on plugin
unload via `PluginRegistrationTracker`.

## NetMessageRegistry — RUNTIME reflection, not source-gen

**Correction to earlier scan summary:** `NetMessageRegistry` does NOT use a
source generator. It builds the type↔id map at runtime on first
`EnsureInitialized()`:

1. Scan the assembly for all `IMessage` types, build
   `Dictionary<string, Type>` keyed by `MessageDescriptor.Name`
   (e.g. `"CCitadelUserMsg_ChatMsg"`).
2. Visit every `FileDescriptor` once, iterate its `EnumTypes`.
3. Known enum names → name-mapping rules. For each enum value, derive the
   corresponding proto type name and `Register(type, value.Number)`.

Enum-to-proto name mapping table (NetMessageRegistry.cs:99-113):

| Enum | Value prefix | Message name prefix |
|------|--------------|---------------------|
| `NET_Messages` | `net_` | `CNETMsg_` |
| `CitadelUserMessageIds` | `k_EUserMsg_` | `CCitadelUserMsg_` |
| `ECitadelClientMessages` | `CITADEL_CM_` | `CCitadelClientMsg_` |
| `EBaseUserMessages` | `UM_` | `CUserMessage` |
| `EBaseEntityMessages` | `EM_` | `CEntityMessage` |
| `CLC_Messages` | `clc_` | `CCLCMsg_` |
| `SVC_Messages`, `SVC_Messages_LowFrequency` | `svc_` | `CSVCMsg_` |
| `Bidirectional_Messages`, `Bidirectional_Messages_LowFrequency` | `bi_` | `CBidirMsg_` |
| `EBaseGameEvents` | `GE_` | `CMsg` |
| `ETEProtobufIds` | `TE_` | `CMsgTE` |

Manual override: `NetMessageRegistry.RegisterManual<T>(int id)` — bypass
enum discovery to register custom types or override a mapping.

## RecipientFilter — 64-bit mask

```csharp
public struct RecipientFilter { public ulong Mask; }
```

- `RecipientFilter.All` — `ulong.MaxValue` (all 64 bits)
- `RecipientFilter.Single(slot)` — `1UL << slot`
- `Add(slot)`, `Remove(slot)`, `HasRecipient(slot)`

## Contexts

`OutgoingMessageContext<T>`:
- `T Message { get; }` — mutable (it's a protobuf message)
- `RecipientFilter Recipients { get; set; }` — **settable**; hook can
  redirect a message to different players
- `int MessageId { get; }`

`IncomingMessageContext<T>`:
- `T Message { get; }` — mutable
- `int SenderSlot { get; }` — who sent it
- `int MessageId { get; }`

## Chat rebroadcast idiom (ChatRelayPlugin — defeats 12-player UI limit)

Deadlock's chat UI only shows portraits for the first 12 slots. To make
chat from slots 12+ visible, `ChatRelayPlugin` hooks outgoing
`CCitadelUserMsg_ChatMsg`, builds a new message per recipient with the
sender name in the text, and re-sends:

```csharp
if (senderSlot < 12) return HookResult.Continue;  // normal path
_rebroadcasting = true;
try {
    for (int slot = 0; slot < 64; slot++) {
        if ((originalMask & (1UL << slot)) == 0) continue;
        var msg = new CCitadelUserMsg_ChatMsg {
            PlayerSlot = slot,   // key trick: slot IS the recipient
            Text = slot == senderSlot ? text : $"[{senderName}]: {text}",
            ...
        };
        NetMessages.Send(msg, RecipientFilter.Single(slot));
    }
} finally { _rebroadcasting = false; }
return HookResult.Stop;
```

The `_rebroadcasting` guard prevents the rehook loop when the re-sent
messages pass through the same outgoing hook.

## Docker/protobuf dependency — still relevant

Plugins that call `NetMessages.Send<T>` must declare
`<PackageReference Include="Google.Protobuf" ... Private="false" ExcludeAssets="runtime" />`
in their csproj for Docker CI to compile (already on wiki under
protobuf-pipeline). The managed `Api` exposes protobuf types in its public
surface, so consumer csprojs need the package to see them at compile time,
even though the DLL itself ships via Deadworks.
