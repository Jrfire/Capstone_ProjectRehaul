using System.Collections.Generic;
using Mirror;
using UnityEngine;

/// <summary>
/// Santelmo FSM brain: a floating fire orb that roams the whole map on the NavMesh (Wandering). When it spots a player it
/// stops and stares (Gazing); a player who stays in sight for <see cref="SantelmoProfile.gazeSeconds"/> falls into a
/// trance (<see cref="PlayerTrance"/>). Weapon hits stun it (Stunned), then it wanders on.
/// </summary>
[RequireComponent(typeof(EnemyMotor))]
public sealed class SantelmoBrain : EnemyBrain, ITranceSource
{
    [SerializeField] private SantelmoProfile profile;

    [SyncVar] private NetworkIdentity gazeTarget;
    [SyncVar] private float gazeProgress;

    private readonly Dictionary<NetworkIdentity, double> ignoredUntil = new Dictionary<NetworkIdentity, double>();
    [SyncVar] private double empoweredUntil;

    public SantelmoProfile Profile => profile;
    /// <summary>The player it is staring at (synced), or null.</summary>
    public NetworkIdentity GazeTarget => gazeTarget;
    /// <summary>Synced gaze progress (0..1) toward entrancing <see cref="GazeTarget"/>.</summary>
    public float GazeProgress => gazeProgress;
    // The stared-at player hears the chase SFX; the chase theme plays in the trance pocket instead.
    public override NetworkIdentity ChasedPlayer => gazeTarget;
    public override bool PlaysChaseTheme => false;
    public override bool PlaysChaseSound => false;
    /// <summary>The EnemyAudio Chase Theme, heard by the tranced player inside the pocket dimension.</summary>
    public AudioClip PocketTheme => TryGetComponent(out EnemyAudio enemyAudio) ? enemyAudio.ChaseTheme : null;
    protected override Vector2 FallbackHomeHalfSize => profile.fallbackHomeHalfSize;

    public SantelmoWanderingState Wandering { get; private set; }
    public SantelmoGazingState Gazing { get; private set; }
    public SantelmoStunnedState Stunned { get; private set; }
    public SantelmoInvestigatingState Investigating { get; private set; }

    public bool IsBloodMoon => MoonSky.BloodMoon;
    /// <summary>Seconds of gaze that entrance a player (shorter during a blood moon).</summary>
    public float GazeSeconds
    {
        get
        {
            float baseSeconds = IsBloodMoon ? Mathf.Min(profile.bloodMoonGazeSeconds, profile.gazeSeconds) : profile.gazeSeconds;
            return IsEmpowered ? baseSeconds * profile.fireCoreGazeMultiplier : baseSeconds;
        }
    }
    public float WanderSpeed => (IsBloodMoon ? profile.bloodMoonWanderSpeed : profile.wanderSpeed) * SpeedMultiplier;
    public float InvestigateSpeed =>
        (IsBloodMoon ? Mathf.Max(profile.investigateSpeed, profile.bloodMoonWanderSpeed) : profile.investigateSpeed) * SpeedMultiplier;

    /// <summary>True while a molotov fire's core empowers it (and for Fire Empower Seconds afterwards): faster, shorter gaze.</summary>
    public bool IsEmpowered => NetworkTime.time < empoweredUntil;
    private float SpeedMultiplier => IsEmpowered ? profile.fireCoreSpeedMultiplier : 1f;

    /// <summary>The burning molotov fire it is drawn to (nearest within Fire Attraction Range), or null.</summary>
    public FireZone AttractingFire
    {
        get
        {
            FireZone fire = FireZone.FindNearest(transform.position);
            if (fire == null || (profile.fireAttractionRange > 0f &&
                                 (fire.transform.position - transform.position).sqrMagnitude > profile.fireAttractionRange * profile.fireAttractionRange))
                return null;
            return fire;
        }
    }

    protected override void Awake()
    {
        base.Awake();
        if (profile == null)
        {
            Debug.LogWarning($"'{name}' has no Santelmo Profile; using default values.", this);
            profile = ScriptableObject.CreateInstance<SantelmoProfile>();
        }
        Wandering = new SantelmoWanderingState(this);
        Gazing = new SantelmoGazingState(this);
        Stunned = new SantelmoStunnedState(this);
        Investigating = new SantelmoInvestigatingState(this);
        ApplyVision();
    }

    protected override EnemyState CreateInitialState() => Wandering;

    public void ApplyVision()
    {
        Senses.EyeHeight = profile.eyeHeight;
        Senses.VisionRange = profile.visionRange;
        Senses.VisionAngle = profile.visionAngle;
        Senses.HearingRadius = 0f;
    }

    /// <summary>True if the Santelmo is ignoring <paramref name="player"/> (escaped or recently entranced).</summary>
    public bool IsIgnoring(PlayerPersistence player) =>
        player != null && ignoredUntil.TryGetValue(player.netIdentity, out double until) && NetworkTime.time < until;

    [Server]
    public void Ignore(PlayerPersistence player, float seconds)
    {
        if (player != null && seconds > 0f)
            ignoredUntil[player.netIdentity] = NetworkTime.time + seconds;
    }

    public bool TrySpotPlayer(out PlayerPersistence player)
    {
        player = Senses.FindVisiblePlayer(candidate => !IsIgnoring(candidate) && !PlayerTrance.IsInTrance(candidate));
        return player != null;
    }

    [Server]
    public void StartGaze(PlayerPersistence player)
    {
        Gazing.Target = player;
        ChangeState(Gazing);
    }

    /// <summary>Server: drifts to where <paramref name="escaped"/> was last seen, still watching for any player.</summary>
    [Server]
    public void Investigate(PlayerPersistence escaped, Vector3 lastKnown)
    {
        Investigating.Begin(escaped, lastKnown);
        ChangeState(Investigating);
    }

    internal void SetGaze(NetworkIdentity target, float progress)
    {
        gazeTarget = target;
        gazeProgress = Mathf.Clamp01(progress);
    }

    /// <summary>Server: random reachable NavMesh point in a random block openly reachable from where it hovers.</summary>
    public bool TryGetWanderPoint(out Vector3 point)
    {
        MapManager map = MapManager.Active;
        if (Motor != null && map != null && map.IsBuilt && map.BlockCount > 0)
        {
            if (!map.TryGetBlockIndex(transform.position, out int current))
                current = -1;
            bool[] open = current >= 0 ? map.FindOpenlyReachable(current) : map.FindOpenlyReachableFromSpawn();
            var blocks = new List<int>();
            for (int i = 0; i < map.BlockCount; i++)
            {
                if (open[i] && i != current)
                    blocks.Add(i);
            }
            if (blocks.Count == 0 && current >= 0)
                blocks.Add(current);
            for (int attempt = 0; attempt < 4 && blocks.Count > 0; attempt++)
            {
                int block = blocks[Random.Range(0, blocks.Count)];
                if (Motor.TryRandomPoint(EnemyHomeArea.FromBlock(map.GetBlock(block)), 20, true, out point))
                    return true;
            }
        }
        return TryGetHomePoint(out point);
    }

    [Server]
    public void Stun(float seconds = -1f)
    {
        Stunned.Duration = seconds >= 0f ? seconds : profile.defaultStunSeconds;
        ChangeState(Stunned);
    }

    public override bool ServerTakeWeaponHit(ItemDefinition weapon, NetworkIdentity attacker)
    {
        if (!isServer)
            return false;
        Stun(weapon != null && weapon.StunSeconds > 0f ? weapon.StunSeconds : -1f);
        return true;
    }

    /// <summary>Fire does not hurt or repel it: inside a fire's core it is empowered instead.</summary>
    protected override void ServerReactToFire(FireZone fire, bool inCore)
    {
        if (!inCore)
            return;
        bool wasEmpowered = IsEmpowered;
        empoweredUntil = NetworkTime.time + profile.fireEmpowerSeconds;
        // Speed up a walk already in progress.
        if (!wasEmpowered && Motor != null && Motor.IsActive)
            Motor.Agent.speed *= profile.fireCoreSpeedMultiplier;
    }
}
