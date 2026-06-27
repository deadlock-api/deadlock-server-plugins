using System.Numerics;
using DeadworksManaged.Api;

namespace CaptureTheFlag;

// The flag is a prop wearing the game's "Idol" (Soul/Sand Urn) model
// (models/props_gameplay/idol_urn/idol_urn.vmdl) — so it looks like the urn objective. We
// drive it with the existing CTF state machine (hybrid): proximity pickup + base-zone
// scoring in CaptureTheFlag.Flag.cs remain authoritative; the prop is purely the visual
// carry object that rides the tracked flag position.
//
// IMPORTANT — why not the real idol pickup entity (CCitadelItemPickupIdol):
// that schema class is what demos network, but spawning it needs the entity *factory*
// `citadel_item_pickup_idol`, which is only registered once the objective system loads. On
// an -insecure dedicated server it never is, so CreateByDesignerName → native
// CEntitySystem::CreateEntityByName hits an unregistered factory and HARD-CRASHES the server
// (a native fail-fast that managed try/catch CANNOT catch). So we only ever create from an
// allowlist of classnames known to have a registered factory.
public partial class CaptureTheFlagPlugin
{
    // Only these are safe to pass to CreateByName here — each has a guaranteed engine factory.
    // Never add an entity whose factory is gated behind match/objective init (it will crash).
    private static readonly HashSet<string> SafeFlagClasses = new()
    {
        "prop_dynamic",
        "prop_dynamic_override",
        "prop_physics_override",
    };
    private const string DefaultFlagClass = "prop_dynamic";

    private const string IdolModelPath = "models/props_gameplay/idol_urn/idol_urn.vmdl";
    private const string MidBossDesigner = "npc_mid_boss";

    // The REAL idol pickup entity (schema class CCitadelItemPickupIdol). Carries the urn model,
    // the minimap objective marker, and native carry behaviour. The earlier crash creating it
    // was the too-early OnStartupServer timing (which also crashed prop_dynamic) — retried here
    // from the deferred, post-map-load TryInitWorld it may simply work. If the factory isn't
    // registered it will hard-crash; that's the accepted experiment.
    private const string NativeIdolClass = "citadel_item_pickup_idol";

    private int _idolEnt = -1;
    private bool _idolIsNative;
    private bool _idolWarned;

    public override void OnPrecacheResources()
    {
        // C++ item-pickup entities reference their model directly; precache it so a mid-match
        // spawn doesn't fall back to the error model / isn't missing client-side.
        Precache.AddResource(IdolModelPath);
        Console.WriteLine($"[CTF] OnPrecacheResources: precached '{IdolModelPath}'");
    }

    // Log every idol/cash-in/mid-boss entity already present so the live run reveals what the
    // map actually contains (native idol spawn point, per-team cash-in drop-offs, a stray mid boss).
    private void LogIdolEnvironment()
    {
        foreach (var ent in Entities.All)
        {
            var name = ent.DesignerName;
            if (name.Contains("idol") || name.Contains("cashin") || name == MidBossDesigner)
                Console.WriteLine($"[CTF] idol-env: {name} team={ent.TeamNum} pos={ent.Position:F0}");
        }
    }

    private void SpawnIdol(Vector3 pos)
    {
        if (!Config.UseIdolEntity) return;
        RemoveIdol();

        // First try the REAL idol entity from this safe (deferred) timing. If it spawns we get
        // the urn model + minimap marker for free; if it returns null we fall back to the prop.
        if (Config.UseNativeIdol && TrySpawnNativeIdol(pos))
            return;

        string cls = string.IsNullOrWhiteSpace(Config.IdolClassName) ? DefaultFlagClass : Config.IdolClassName;
        if (!SafeFlagClasses.Contains(cls))
        {
            // Guard against a server-killing fail-fast: refuse any classname not known to have a
            // registered factory (e.g. the native idol pickup citadel_item_pickup_idol).
            Console.WriteLine($"[CTF] Refusing to spawn flag via unvetted class '{cls}' — would risk a native crash. Allowed: {string.Join(", ", SafeFlagClasses)}.");
            return;
        }

        var ekv = new CEntityKeyValues();
        ekv.SetVector("origin", pos);
        ekv.SetVector("angles", Vector3.Zero);
        ekv.SetString("model", IdolModelPath);
        ekv.SetInt("teamnumber", 0);

        var idol = CBaseEntity.CreateByDesignerName(cls);
        if (idol == null)
        {
            if (!_idolWarned) { _idolWarned = true; Console.WriteLine($"[CTF] WARNING: flag prop '{cls}' create returned null — flag has no visible entity (mode still plays via the state machine)."); }
            return;
        }

        idol.Spawn(ekv);
        idol.SetModel(IdolModelPath);
        idol.Teleport(pos);

        _idolEnt = idol.EntityIndex;
        _idolIsNative = false;
        Console.WriteLine($"[CTF] flag prop spawned via '{cls}' ent={_idolEnt} model='{idol.ModelName}' pos={pos:F0}");
    }

    // Attempt to create the REAL idol pickup. Returns true if it spawned. WARNING: if the engine
    // has no factory for NativeIdolClass this fail-fasts the server (uncatchable) — accepted risk.
    private bool TrySpawnNativeIdol(Vector3 pos)
    {
        Console.WriteLine($"[CTF] attempting NATIVE idol '{NativeIdolClass}' (deferred timing)…");
        var idol = CBaseEntity.CreateByDesignerName(NativeIdolClass);
        if (idol == null)
        {
            Console.WriteLine($"[CTF] native idol '{NativeIdolClass}' returned null — falling back to prop.");
            return false;
        }

        var ekv = new CEntityKeyValues();
        ekv.SetVector("origin", pos);
        ekv.SetVector("angles", Vector3.Zero);
        ekv.SetInt("teamnumber", 0);
        ekv.SetString("model", IdolModelPath);
        idol.Spawn(ekv);
        // The native idol normally gets its model from m_IdolParams during objective init, which
        // we bypass — so it spawns with models/null.vmdl. Set the urn model explicitly.
        idol.SetModel(IdolModelPath);
        idol.Teleport(pos);

        _idolEnt = idol.EntityIndex;
        _idolIsNative = true;
        Console.WriteLine($"[CTF] NATIVE idol spawned ent={_idolEnt} class='{idol.Classname}' model='{idol.ModelName}' pos={pos:F0}");
        return true;
    }

    // Held above the carrier's head so it doesn't clip into them.
    private static readonly Vector3 IdolCarryOffset = new(0, 0, 90);
    // Lifts the idle urn off the floor — its model origin is at its center, so placing it exactly
    // on the ground buries the bottom half.
    private static readonly Vector3 IdolGroundClearance = new(0, 0, 45);
    private const float IdolCarriedScale = 0.5f;   // smaller while carried
    private const float IdolIdleScale = 2.0f;      // bigger while uncaptured
    private float _idolScale = -1f;

    // Drives the idol every engine tick (called from the 1-tick clock timer): teleports it to the
    // tracked flag position (smoothly tracing the carrier; the 4-tick poll stuttered) and applies
    // the carried/idle scale. It's teleported EVERY tick even when idle, because a freshly-spawned
    // idol that never moves isn't transmitted/rendered to clients (it only drew once carried).
    private void TickIdol()
    {
        if (_idolEnt < 0 || !_worldInit) return;
        var idol = CBaseEntity.FromIndex(_idolEnt);
        if (idol == null || !idol.IsValid) { _idolEnt = -1; return; }

        Vector3 target;
        float scale;
        if (IsCarried)
        {
            var carrier = CBaseEntity.FromIndex<CCitadelPlayerPawn>(_carrierEnt);
            if (carrier != null && carrier.IsAlive) _flagPos = carrier.Position; // ground pos for drop
            target = _flagPos + IdolCarryOffset;
            scale = IdolCarriedScale;
        }
        else
        {
            target = _flagPos + IdolGroundClearance;
            scale = IdolIdleScale;
        }

        idol.Teleport(target);
        if (scale != _idolScale) { idol.SetScale(scale); _idolScale = scale; }
    }

    private void RemoveIdol()
    {
        if (_idolEnt < 0) return;
        CBaseEntity.FromIndex(_idolEnt)?.Remove();
        _idolEnt = -1;
        _idolScale = -1f;
    }
}
