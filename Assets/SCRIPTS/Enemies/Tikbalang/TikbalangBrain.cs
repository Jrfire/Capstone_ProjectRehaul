using System.Collections.Generic;
using System.Linq;
using Mirror;
using UnityEngine;

/// <summary>
/// Tikbalang FSM brain. States: Idle (guard home block), Searching (investigate a heard noise), Chase (locked on the
/// first spotted player for a limited time), Returning (walk home ignoring players) and Stunned.
/// </summary>
[RequireComponent(typeof(EnemyMotor))]
public sealed class TikbalangBrain : EnemyBrain
{
    [SerializeField] private TikbalangProfile profile;

    [SyncVar] private NetworkIdentity chaseTarget;

    public TikbalangProfile Profile => profile;
    /// <summary>The player being chased (synced so clients can react, e.g. with fear effects later).</summary>
    public NetworkIdentity ChaseTarget => chaseTarget;
    public override NetworkIdentity ChasedPlayer => chaseTarget;
    public bool IsChasing => chaseTarget != null;

    public TikbalangIdleState Idle { get; private set; }
    public TikbalangSearchingState Searching { get; private set; }
    public TikbalangChaseState Chase { get; private set; }
    public TikbalangReturningState Returning { get; private set; }
    public TikbalangStunnedState Stunned { get; private set; }

    protected override Vector2 FallbackHomeHalfSize => profile.fallbackHomeHalfSize;

    protected override void Awake()
    {
        base.Awake();
        if (profile == null)
        {
            Debug.LogWarning($"'{name}' has no Tikbalang Profile; using default values.", this);
            profile = ScriptableObject.CreateInstance<TikbalangProfile>();
        }
        Idle = new TikbalangIdleState(this);
        Searching = new TikbalangSearchingState(this);
        Chase = new TikbalangChaseState(this);
        Returning = new TikbalangReturningState(this);
        Stunned = new TikbalangStunnedState(this);
        ApplyNormalVision();
    }

    protected override EnemyState CreateInitialState() => Idle;

    /// <summary>Server: seconds until the Tikbalang moves to another home block (counts down in every state).</summary>
    public float HomeChangeTimer { get; set; } = -1f;

    /// <summary>True while the blood moon makes it hunt across the map and scramble its target's controls.</summary>
    public bool IsBloodMoon => MoonSky.BloodMoon;

    /// <summary>Server: Time.time when this Tikbalang next flashes its jumpscare (blood moon only).</summary>
    public float NextScareTime { get; set; } = -1f;
    /// <summary>Server: number of blood-moon jumpscare flashes sent.</summary>
    public int ScareCount { get; private set; }
    /// <summary>Server: the player it last scrambled and until when their controls stay inverted.</summary>
    public NetworkIdentity ScrambledPlayer { get; private set; }
    public float ScrambledUntil { get; private set; }
    /// <summary>Server: number of relocations to the most visited block.</summary>
    public int RelocationCount { get; private set; }
    /// <summary>Server: Time.time its current home was assigned (the unvisited timer never starts earlier).</summary>
    public float HomeAssignedTime { get; private set; }
    private int trackedHome = -1;
    private float relocationCheckTimer;

    /// <summary>True while its last scramble target still has inverted controls; it moves slower meanwhile.</summary>
    public bool IsTargetScrambled => ScrambledPlayer != null && Time.time < ScrambledUntil;

    /// <summary>Applies the scrambled-target slowdown to <paramref name="baseSpeed"/>.</summary>
    public float SpeedFor(float baseSpeed) => IsTargetScrambled ? baseSpeed * profile.scrambledSpeedMultiplier : baseSpeed;

    protected override void ServerTick(float deltaTime)
    {
        MapManager map = MapManager.Active;
        if (map != null && map.IsBuilt)
            BlockVisitTracker.Tick(map);
        if (HomeBlockIndex != trackedHome)
        {
            trackedHome = HomeBlockIndex;
            HomeAssignedTime = Time.time;
        }
        TickRelocation(deltaTime);
        TickBloodMoonScare();

        if (profile.homeChangeSeconds <= 0f)
            return;
        if (HomeChangeTimer < 0f)
            HomeChangeTimer = profile.homeChangeSeconds;
        HomeChangeTimer -= deltaTime;
        if (HomeChangeTimer <= 0f)
        {
            HomeChangeTimer = profile.homeChangeSeconds;
            TryChangeHomeBlock();
        }
    }

    /// <summary>
    /// Server: picks another block that could host it at spawn (not a spawn block or its neighbour, reachable from
    /// spawn) and is openly reachable from its current block and makes it home. Idle/Searching/Returning head there
    /// at once (Idle wanders into it); a Chase or Stun keeps going and Returning then walks to the new home.
    /// </summary>
    [Server]
    public bool TryChangeHomeBlock()
    {
        MapManager map = MapManager.Active;
        if (map == null || !map.IsBuilt || HomeBlockIndex < 0)
            return false;
        List<int> candidates = EnemySpawner.FindCandidateBlocks(map, out _);
        bool[] open = map.FindOpenlyReachable(HomeBlockIndex);
        candidates.RemoveAll(block => block == HomeBlockIndex || !open[block]);
        if (candidates.Count == 0)
            return false;
        AssignHomeBlock(map, candidates[Random.Range(0, candidates.Count)]);
        if (CurrentState != Chase && CurrentState != Stunned)
            ChangeState(Idle);
        return true;
    }

    /// <summary>
    /// Server: when no player has been in its home block for the profile's Unvisited Respawn Seconds, reappears hidden in
    /// the most visited eligible block (not a spawn block, openly reachable from spawn, not its own or another
    /// Tikbalang's home). If players are still in that block it waits for them to leave.
    /// </summary>
    private void TickRelocation(float deltaTime)
    {
        if (profile.unvisitedRespawnSeconds <= 0f || IsBloodMoon || HomeBlockIndex < 0)
            return;
        if (CurrentState == Chase || CurrentState == Stunned)
            return;
        relocationCheckTimer -= deltaTime;
        if (relocationCheckTimer > 0f)
            return;
        relocationCheckTimer = 1f;
        float lastVisit = Mathf.Max(BlockVisitTracker.LastOccupiedTime(HomeBlockIndex), HomeAssignedTime);
        if (Time.time - lastVisit < profile.unvisitedRespawnSeconds)
            return;
        TryRelocateToVisitedBlock();
    }

    /// <summary>The most visited block it may relocate to, or -1 (none visited or none eligible).</summary>
    public int FindRelocationBlock()
    {
        MapManager map = MapManager.Active;
        if (map == null || !map.IsBuilt)
            return -1;
        bool[] reachable = map.FindOpenlyReachableFromSpawn();
        var taken = new HashSet<int>(FindObjectsByType<TikbalangBrain>(FindObjectsSortMode.None)
            .Where(other => other != this).Select(other => other.HomeBlockIndex));
        foreach (int block in BlockVisitTracker.RankedBlocks())
        {
            if (block < 0 || block >= map.BlockCount || block == HomeBlockIndex || taken.Contains(block))
                continue;
            if (map.GetRole(block) == MapBlockRole.Spawn || !reachable[block])
                continue;
            return block;
        }
        return -1;
    }

    /// <summary>Server: relocates now if a block qualifies and is empty of players; false otherwise.</summary>
    [Server]
    public bool TryRelocateToVisitedBlock()
    {
        int block = FindRelocationBlock();
        if (block < 0 || BlockVisitTracker.IsOccupied(block))
            return false;
        MapManager map = MapManager.Active;
        if (!TryTeleportHidden(new[] { EnemyHomeArea.FromBlock(map.GetBlock(block)) }, profile.teleportMinPlayerDistance, null))
            return false;
        AssignHomeBlock(map, block);
        trackedHome = block;
        HomeAssignedTime = Time.time;
        HomeChangeTimer = profile.homeChangeSeconds;
        RelocationCount++;
        ChangeState(Idle);
        return true;
    }

    private void TickBloodMoonScare()
    {
        if (!IsBloodMoon)
        {
            NextScareTime = -1f;
            return;
        }
        if (NextScareTime < 0f)
            NextScareTime = Time.time + Random.Range(profile.scareMinSeconds, Mathf.Max(profile.scareMinSeconds, profile.scareMaxSeconds));
        if (Time.time < NextScareTime)
            return;
        NextScareTime = Time.time + Random.Range(profile.scareMinSeconds, Mathf.Max(profile.scareMinSeconds, profile.scareMaxSeconds));
        PlayerPersistence target = ChasedPersistence ?? FindNearestHuntable();
        if (target != null)
            ScarePlayer(target);
    }

    /// <summary>Server: flashes its jumpscare on <paramref name="player"/>'s screen and inverts their A/D and mouse look (both axes).</summary>
    [Server]
    public void ScarePlayer(PlayerPersistence player)
    {
        if (player == null || player.connectionToClient == null)
            return;
        ScareCount++;
        ScrambledPlayer = player.netIdentity;
        ScrambledUntil = Time.time + profile.scrambleSeconds;
        TargetScare(player.connectionToClient, profile.scareFlashSeconds, profile.scrambleSeconds);
    }

    [TargetRpc]
    private void TargetScare(NetworkConnectionToClient target, float flashSeconds, float scrambleSeconds)
    {
        JumpscareFlash.Show(netIdentity.assetId, flashSeconds);
        ControlInversion.Begin(scrambleSeconds);
    }

    /// <summary>Nearest living, huntable player (not aboard, tranced or dead), or null.</summary>
    public PlayerPersistence FindNearestHuntable()
    {
        PlayerPersistence best = null;
        float bestDistance = float.PositiveInfinity;
        foreach (PlayerPersistence player in PlayerPersistence.ActivePlayers)
        {
            if (player == null || !player.isActiveAndEnabled || player.IsAboard || player.IsInTrance ||
                (player.TryGetComponent(out PlayerHealth health) && health.IsDead))
                continue;
            float distance = (player.transform.position - transform.position).sqrMagnitude;
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = player;
            }
        }
        return best;
    }

    /// <summary>Blood moon: a random NavMesh point in the nearest huntable player's block (else home).</summary>
    public bool TryGetHuntPoint(out Vector3 point)
    {
        MapManager map = MapManager.Active;
        PlayerPersistence prey = FindNearestHuntable();
        if (prey != null && map != null && map.IsBuilt && map.TryGetBlockIndex(prey.transform.position, out int block) &&
            Motor.TryRandomPoint(EnemyHomeArea.FromBlock(map.GetBlock(block)), 30, true, out point))
            return true;
        return TryGetHomePoint(out point);
    }

    public void ApplyNormalVision()
    {
        Senses.EyeHeight = profile.eyeHeight;
        Senses.VisionRange = profile.visionRange;
        Senses.VisionAngle = profile.visionAngle;
        Senses.HearingRadius = profile.hearingRadius;
    }

    public void ApplySearchingVision()
    {
        ApplyNormalVision();
        Senses.VisionAngle = profile.searchingVisionAngle;
    }

    /// <summary>A visible player standing inside the home block (the guard rule for starting a chase).</summary>
    public bool TrySpotPlayerInHome(out PlayerPersistence player)
    {
        player = Senses.FindVisiblePlayer(candidate => Home.Contains(candidate.transform.position));
        return player != null;
    }

    public bool TrySpotAnyPlayer(out PlayerPersistence player)
    {
        player = Senses.FindVisiblePlayer();
        return player != null;
    }

    /// <summary>A noise whose origin lies inside the home block; noises elsewhere are ignored.</summary>
    public bool TryHearInHome(out Vector3 origin) => Senses.TryHear(point => Home.Contains(point), out origin);

    /// <summary>Any noise it can hear, wherever it comes from (blood moon).</summary>
    public bool TryHearAny(out Vector3 origin) => Senses.TryHear(_ => true, out origin);

    [Server]
    public void StartSearch(Vector3 origin)
    {
        Searching.SetOrigin(origin);
        if (CurrentState != Searching)
            ChangeState(Searching);
    }

    [Server]
    public void StartChase(PlayerPersistence player)
    {
        if (NearbyFire != null)
            return; // it will not chase into a molotov fire
        chaseTarget = player.netIdentity;
        ChangeState(Chase);
    }

    /// <summary>Server: stops the chase and drops the target, then walks home after <paramref name="seconds"/>.</summary>
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

    /// <summary>A molotov fire breaks off the chase: it gives up and walks back to its home block.</summary>
    protected override void ServerReactToFire(FireZone fire, bool inCore)
    {
        if (CurrentState == Chase)
            ChangeState(Returning);
    }

    [Server]
    public void EndChase()
    {
        ChangeState(Returning);
    }

    internal void ClearChaseTarget() => chaseTarget = null;

    internal PlayerPersistence ChasedPersistence =>
        chaseTarget != null && chaseTarget.TryGetComponent(out PlayerPersistence player) && player.isActiveAndEnabled ? player : null;

    /// <summary>
    /// Teleports to a hidden spot in the block next to the target's block that is closest to the target (the target's
    /// own block is the last resort).
    /// </summary>
    [Server]
    public bool TryTeleportNear(PlayerPersistence target)
    {
        MapManager map = MapManager.Active;
        if (map == null || !map.IsBuilt || !map.TryGetBlockIndex(target.transform.position, out int targetBlock))
            return false;

        Vector3 targetPosition = target.transform.position;
        // Only blocks the target can walk to without passing a closed barricade, so the enemy is never sealed in.
        bool[] open = map.FindOpenlyReachable(targetBlock);
        IEnumerable<EnemyHomeArea> areas = map.GetNeighbours(targetBlock)
            .Where(index => open[index])
            .OrderBy(index => (map.GetBlock(index).transform.position - targetPosition).sqrMagnitude)
            .Select(index => EnemyHomeArea.FromBlock(map.GetBlock(index)))
            .Append(EnemyHomeArea.FromBlock(map.GetBlock(targetBlock)));
        if (!TryTeleportHidden(areas, profile.teleportMinPlayerDistance, targetPosition))
            return false;
        Motor.FaceTowards(targetPosition);
        return true;
    }
}
