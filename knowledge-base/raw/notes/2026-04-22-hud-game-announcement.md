---
date: 2026-04-22
task: Fire HUD toasts for round boundaries in TrooperInvasion
files: [TrooperInvasion/TrooperInvasion.cs, ../deadworks/examples/plugins/TagPlugin/TagPlugin.cs]
---

**HUD announcement toast pattern.** Shows a centered title + description
banner on every player's HUD — distinct from chat scroll. Canonical use
from `TagPlugin.cs:342-346`:

```csharp
var announcement = new CCitadelUserMsg_HudGameAnnouncement {
    TitleLocstring = "TAGGED!",
    DescriptionLocstring = $"{taggerName} caught {taggedName}!"
};
NetMessages.Send(announcement, RecipientFilter.All);
```

Both strings accept plain text; `Locstring` naming suggests the engine
will try a localization-lookup first and fall back to the literal on
cache miss. Plain English strings work fine in practice.

**csproj impact: none.** The generated proto type is transitively
available via `DeadworksManaged.Api`; no `Google.Protobuf` package
reference is needed in the consuming plugin's csproj (TagPlugin itself
has no package reference and uses this pattern). Contrast with plugins
that call `NetMessages.Send<T>` on a *custom* proto type — those DO need
`Google.Protobuf` referenced. Pre-generated Citadel message types are
free to use.

**Targeting.** `RecipientFilter.All` for global announcements;
`RecipientFilter.Single(slot)` for a specific player.

TrooperInvasion uses it for round-boundary events only (round armed,
round cleared, victory, defeat). Per-wave announcements stay as chat so
HUD isn't spammed every 5-20s at high player counts. Encapsulated as
`AnnounceHud(title, description)` helper.

Also documented field list from `CitadelUsermessages.cs`:
`TitleLocstring`, `DescriptionLocstring`, `Classname`,
`DialogVariableName`, `DialogVariableLocstring`. The last three are for
replacing `%var%`-style tokens in localization strings — not needed for
plain-text messages.
