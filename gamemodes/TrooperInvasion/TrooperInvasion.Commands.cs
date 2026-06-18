using DeadworksManaged.Api;

namespace TrooperInvasion;

public partial class TrooperInvasionPlugin
{
    private static readonly string[] _helpLines = {
        "[TI] !help — show this message",
        "[TI] !hero <name> — swap hero (fuzzy match)",
        "[TI] !stuck / !suicide — kill yourself to respawn",
        "[TI] !wave — show current wave",
        "[TI] !startwaves — arm wave engine + begin scheduler",
        "[TI] !stopwaves — halt scheduler (and close spawn window)",
        "[TI] !nextwave — trigger one wave immediately (dev)",
        "[TI] !voteskip — vote to end the current round (>20% of players required)",
        "[TI] !feedback <message> — send feedback to the server admins",
    };

    [Command("help", Description = "Show available TrooperInvasion commands")]
    public void CmdHelp(CCitadelPlayerController caller)
    {
        foreach (var line in _helpLines)
            Chat.PrintToChat(caller.Slot, line);
    }

    [Command("wave", Description = "Show current round and wave")]
    public void CmdWave(CCitadelPlayerController caller)
    {
        Chat.PrintToChat(caller.Slot, $"[TI] Round {_roundNum} Wave {_waveNum}/{WaveTuning.RoundLength}{(_modeOver ? " (MODE OVER)" : "")}");
    }

    [Command("startwaves", Description = "Manually arm the wave scheduler")]
    public void CmdStartWaves(CCitadelPlayerController caller)
    {
        if (_modeOver) throw new CommandException("[TI] Mode is over.");
        if (_wavesActive) throw new CommandException("[TI] Waves already running.");
        ArmWaves();
    }

    [Command("stopwaves", Description = "Pause the auto wave scheduler")]
    public void CmdStopWaves(CCitadelPlayerController caller)
    {
        DisarmWaves("manual !stopwaves");
        Chat.PrintToChatAll("[TI] Wave scheduler halted.");
    }

    [Command("nextwave", Description = "Trigger the next wave immediately (dev)")]
    public void CmdNextWave(CCitadelPlayerController caller)
    {
        if (_modeOver)
            throw new CommandException("[TI] Mode is over.");
        bool wasActive = _wavesActive;
        _wavesActive = true;
        RunWave();
        _wavesActive = wasActive;
    }

    [Command("voteskip", Description = "Vote to end the current round early (>20% of players)")]
    public void CmdVoteSkip(CCitadelPlayerController caller)
    {
        Console.WriteLine($"[TI] !voteskip from slot {caller.Slot} (modeOver={_modeOver}, wavesActive={_wavesActive}, round={_roundNum}, wave={_waveNum})");

        if (_modeOver) throw new CommandException("[TI] Mode is over — wait for the next round.");
        if (!_wavesActive) throw new CommandException("[TI] No active round to skip (intermission or pre-arm).");

        bool isFirst = _voteSkipSlots.Count == 0;
        if (!_voteSkipSlots.Add(caller.Slot))
            throw new CommandException("[TI] You already voted to skip this round.");

        int humans = Math.Max(1, HumanPlayerCount());
        int votes = _voteSkipSlots.Count;
        string who = caller.PlayerName ?? "A player";

        if (isFirst)
            Chat.PrintToChatAll($"[TI] {who} wants to skip round {_roundNum} — type !voteskip to agree (>{VoteSkipPercent}% needed)");

        int percent = votes * 100 / humans;
        Chat.PrintToChatAll($"[TI] Vote skip: {votes}/{humans} ({percent}%)");
        Console.WriteLine($"[TI] vote tally: {votes}/{humans} ({percent}%) threshold>{VoteSkipPercent}%");

        if (votes * 100 > humans * VoteSkipPercent)
        {
            Chat.PrintToChatAll($"[TI] Vote skip passed — ending round {_roundNum}");
            Console.WriteLine($"[TI] Vote skip threshold reached — finishing round {_roundNum}");
            // Clear immediately so DisarmWaves doesn't re-process a tallied ballot.
            _voteSkipSlots.Clear();
            // Voteskip drops leftover troopers so the intermission isn't crowded.
            CullAllTroopers();
            FinishRound();
        }
    }
}
