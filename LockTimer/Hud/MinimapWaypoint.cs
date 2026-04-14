using System.Numerics;
using DeadworksManaged.Api;

namespace LockTimer.Hud;

/// <summary>
/// Emits a minimap-only ping at a per-player target location, re-sending
/// periodically so the client marker stays alive.
/// </summary>
public sealed class MinimapWaypoint
{
    // Client ping markers expire after ~2s; resend with a safety margin to
    // avoid flicker.
    private const long ResendIntervalMs = 1500;

    // Matches PingCommonData.entity_index proto default: "no entity".
    private const uint NoEntityIndex = 16777215;

    // High bits tag the id as ours (ASCII "LT"); low bits are the slot so
    // each player gets a stable id and new pings replace the old one.
    private const uint PingIdPrefix = 0x4C540000u;

    private readonly Dictionary<int, long> _lastSentAt = new();

    public void Remove(int slot) => _lastSentAt.Remove(slot);

    public void Clear() => _lastSentAt.Clear();

    public void Tick(int slot, Vector3? target, long nowMs)
    {
        if (target is null) return;
        if (_lastSentAt.TryGetValue(slot, out var last) && nowMs - last < ResendIntervalMs)
            return;

        SendPing(slot, target.Value);
        _lastSentAt[slot] = nowMs;
    }

    private static void SendPing(int slot, Vector3 location)
    {
        var msg = new CCitadelUserMsg_MapPing
        {
            PingData = new PingCommonData
            {
                PingMessageId = PingIdPrefix | (uint)slot,
                PingLocation = new CMsgVector { X = location.X, Y = location.Y, Z = location.Z },
                EntityIndex = NoEntityIndex,
                SenderPlayerSlot = -1,
            },
            PingMarkerAndSoundInfo = ChatMsgPingMarkerInfo.KEpingMarkerInfoOnlyMiniMap,
            IsMinimapPing = true,
        };

        NetMessages.Send(msg, RecipientFilter.Single(slot));
    }
}
