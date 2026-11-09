using System.Collections.Generic;
using Mirror;
using UnityEngine;

/// <summary>
/// Santelmo FSM brain: a floating fire orb that roams the whole map on the NavMesh (Wandering). When it spots a player it
/// stops and stares (Gazing); a player who stays in sight for <see cref="SantelmoProfile.gazeSeconds"/> falls into a
/// trance (<see cref="PlayerTrance"/>). Weapon hits stun it (Stunned), then it wanders on.
/// </summary>
[RequireComponent(typeof(EnemyMotor))]
public sealed class SantelmoBrain : EnemyBrain
{
    [SerializeField] private SantelmoProfile profile;

    [SyncVar] private NetworkIdentity gazeTarget;
    [SyncVar] private float gazeProgress;

    private readonly Dictionary<NetworkIdentity, double> ignoredUntil = new Dictionary<NetworkIdentity, double>();

    public SantelmoProfile Profile => profile;
    /// <summary>The player it is staring at (synced), or null.</summary>
    public NetworkIdentity GazeTarget => gazeTarget;
    /// <summary>Synced gaze progress (0..1) toward entrancing <see cref="GazeTarget"/>.</summary>
    public float GazeProgress => gazeProgress;
    // The stared-at player hears the chase SFX/theme.
    public override NetworkIdentity ChasedPlayer => gazeTarget;
    protected override Vector2 FallbackHomeHalfSize => profile.fallbackHomeHalfSize;

    public SantelmoWanderingState Wandering { get; private set; }
    public SantelmoGazingState Gazing { get; private set; }
    public SantelmoStunnedState Stunned { get; private set; }

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
}
