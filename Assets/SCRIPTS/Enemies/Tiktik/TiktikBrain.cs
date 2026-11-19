using System.Collections.Generic;
using Mirror;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Tiktik FSM brain. It picks one random victim (Waiting, hidden), then stalks them from rooftops (Stalking: appears
/// on a roof near the victim, eyes them for a few seconds, vanishes and reappears elsewhere). <see
/// cref="TiktikProfile.attackAfterSeconds"/> after the pick it attacks (Attacking): it fades in behind the victim or
/// out of their sight and rushes them, freezing while the victim's flashlight is on it, hitting every few seconds,
/// until the attack time ends; then it fades away and picks another victim. Everyone sees it; its opacity flickers
/// like the White Lady's (local on every peer). The fear effect and chase music only run while attacking.
/// </summary>
[RequireComponent(typeof(EnemyMotor))]
public sealed class TiktikBrain : EnemyBrain
{
    private static readonly Collider[] overlapBuffer = new Collider[256];

    [SerializeField] private TiktikProfile profile;

    [SyncVar] private bool visible;
    [SyncVar] private float fadeSeconds;
    [SyncVar] private bool attacking;
    [SyncVar] private NetworkIdentity victim;
    [SyncVar] private float banish;

    private readonly List<GameObject> visuals = new List<GameObject>();
    private readonly List<Collider> bodyColliders = new List<Collider>();
    private readonly List<Vector3> visiblePerches = new List<Vector3>();
    private readonly List<Vector3> hiddenPerches = new List<Vector3>();
    private DistanceFadeSprite fade;
    private EnemyFearSource fearSource;
    private bool fearSourceWasEnabled;
    private bool visualsShown = true;
    private float presence;
    private float flickerSeed;
    private float dropoutUntil;
    private NetworkIdentity lastVictim;
    private Vector3? lastPerch;
    private double repelUntil;

    public TiktikProfile Profile => profile;
    /// <summary>The chosen victim (synced), or null.</summary>
    public NetworkIdentity Victim => victim;
    /// <summary>Synced: the server wants it shown (it fades toward this on every peer).</summary>
    public bool IsVisible => visible;
    /// <summary>Synced: true during the attack (fear effect, chase music, flashlight freeze).</summary>
    public bool IsAttacking => attacking;
    /// <summary>Local 0..1 fade-in progress toward <see cref="IsVisible"/> (before the flicker).</summary>
    public float Presence => presence;
    /// <summary>Local final opacity multiplier (presence x flicker x (1 - banish)).</summary>
    public float Opacity { get; private set; }
    /// <summary>Synced 0..1: how far the victim's flashlight has faded it away (1 = gone, it teleports).</summary>
    public float Banish => banish;
    public bool IsRepelled => NetworkTime.time < repelUntil;
    public override bool IsPresent => visible || presence > 0f;
    // Only the attacked victim hears the chase SFX/theme, and only while attacking.
    public override NetworkIdentity ChasedPlayer => attacking ? victim : null;
    protected override Vector2 FallbackHomeHalfSize => profile.fallbackHomeHalfSize;
    /// <summary>Server time the current victim was picked.</summary>
    public double VictimPickTime { get; private set; }
    public float SinceVictimPicked => victim != null ? (float)(NetworkTime.time - VictimPickTime) : 0f;

    public TiktikWaitingState Waiting { get; private set; }
    public TiktikStalkingState Stalking { get; private set; }
    public TiktikAttackingState Attacking { get; private set; }

    protected override void Awake()
    {
        base.Awake();
        if (profile == null)
        {
            Debug.LogWarning($"'{name}' has no Tiktik Profile; using default values.", this);
            profile = ScriptableObject.CreateInstance<TiktikProfile>();
        }
        foreach (Collider body in GetComponents<Collider>())
            bodyColliders.Add(body);
        if (Presentation.WorldSprites != null)
            visuals.Add(Presentation.WorldSprites.gameObject);
        foreach (BlobShadow shadow in GetComponentsInChildren<BlobShadow>(true))
            visuals.Add(shadow.gameObject);
        fade = GetComponent<DistanceFadeSprite>();
        fearSource = GetComponent<EnemyFearSource>();
        fearSourceWasEnabled = fearSource != null && fearSource.enabled;
        flickerSeed = Random.value * 100f;
        Senses.HearingRadius = 0f;

        Waiting = new TiktikWaitingState(this);
        Stalking = new TiktikStalkingState(this);
        Attacking = new TiktikAttackingState(this);
        ApplyPresence();
    }

    protected override EnemyState CreateInitialState() => Waiting;

    public void ApplyProfile(TiktikProfile value)
    {
        if (value != null)
            profile = value;
    }

    // ---------------------------------------------------------------- presentation (every peer)

    private void LateUpdate()
    {
        float target = visible ? 1f : 0f;
        presence = fadeSeconds <= 0f ? target : Mathf.MoveTowards(presence, target, Time.deltaTime / fadeSeconds);
        ApplyPresence();
    }

    private void ApplyPresence()
    {
        bool show = visible || presence > 0f;
        if (show != visualsShown)
        {
            visualsShown = show;
            foreach (GameObject visual in visuals)
            {
                if (visual != null)
                    visual.SetActive(show);
            }
            foreach (Collider body in bodyColliders)
            {
                if (body != null)
                    body.enabled = show;
            }
            if (show)
                Presentation.SnapNow();
        }

        Opacity = show ? presence * Flicker() * (1f - Mathf.Clamp01(banish)) : 0f;
        if (fade != null)
            fade.OpacityMultiplier = Opacity;
        else if (Presentation.WorldSprites != null)
        {
            Color color = Presentation.WorldSprites.color;
            color.a = Opacity;
            Presentation.WorldSprites.color = color;
        }
        if (fearSource != null)
        {
            bool fear = fearSourceWasEnabled && attacking && presence > 0.05f;
            if (fearSource.enabled != fear)
                fearSource.enabled = fear;
        }
    }

    private float Flicker()
    {
        // Harsher, faster flicker the further the flashlight has faded it.
        float lit = Mathf.Clamp01(banish);
        float speed = profile.flickerSpeed * (1f + lit * 3f);
        float drift = Mathf.PerlinNoise(flickerSeed, Time.time * speed);
        float wave = 0.5f + 0.5f * Mathf.Sin(Time.time * speed * 2.3f + flickerSeed);
        float minAlpha = Mathf.Lerp(profile.flickerMinAlpha, profile.litFlickerMinAlpha, lit);
        float alpha = Mathf.Lerp(minAlpha, 1f, Mathf.Clamp01(drift * 0.7f + wave * 0.3f));
        float dropouts = Mathf.Lerp(profile.flickerDropoutsPerSecond, profile.litDropoutsPerSecond, lit);
        if (Time.time >= dropoutUntil && dropouts > 0f && Random.value < dropouts * Time.deltaTime)
            dropoutUntil = Time.time + profile.flickerDropoutSeconds;
        return Time.time < dropoutUntil ? 0f : alpha;
    }

    // ---------------------------------------------------------------- server

    /// <summary>Server: fades in (true) or out over <paramref name="seconds"/> on every peer (0 = instantly).</summary>
    [Server]
    public void SetVisible(bool value, float seconds)
    {
        fadeSeconds = Mathf.Max(0f, seconds);
        visible = value;
    }

    [Server]
    internal void SetAttacking(bool value) => attacking = value;

    [Server]
    internal void SetBanish(float value) => banish = Mathf.Clamp01(value);

    internal PlayerPersistence VictimPlayer =>
        victim != null && victim.TryGetComponent(out PlayerPersistence player) && player.isActiveAndEnabled ? player : null;

    /// <summary>A living player that is not aboard the jeepney or in a trance.</summary>
    public static bool IsValidVictim(PlayerPersistence player) =>
        player != null && player.isActiveAndEnabled && !player.IsAboard && !player.IsInTrance &&
        !(player.TryGetComponent(out PlayerHealth health) && health.IsDead);

    public bool VictimIsValid => IsValidVictim(VictimPlayer);

    public bool IsBloodMoon => MoonSky.BloodMoon;
    /// <summary>Server: harmless clone rushes sent to victims (blood moon).</summary>
    public int FakeRushCount { get; private set; }
    private float nextFakeRush = -1f;

    /// <summary>Server (attack state): during a blood moon, now and then sends the victim a harmless clone rush.</summary>
    internal void TickBloodMoonClone(PlayerPersistence target, float deltaTime)
    {
        if (!IsBloodMoon || target == null)
        {
            nextFakeRush = -1f;
            return;
        }
        Vector2 range = profile.bloodMoonCloneIntervalSeconds;
        if (nextFakeRush < 0f)
            nextFakeRush = Random.Range(Mathf.Min(range.x, range.y), Mathf.Max(range.x, range.y)) * 0.5f;
        nextFakeRush -= deltaTime;
        if (nextFakeRush > 0f)
            return;
        nextFakeRush = Random.Range(Mathf.Min(range.x, range.y), Mathf.Max(range.x, range.y));
        SendFakeRush(target);
    }

    internal void ResetBloodMoonClone() => nextFakeRush = -1f;

    /// <summary>Server: makes a harmless clone rush <paramref name="target"/> from where they are looking.</summary>
    [Server]
    public void SendFakeRush(PlayerPersistence target)
    {
        if (target == null || target.connectionToClient == null)
            return;
        FakeRushCount++;
        TargetFakeRush(target.connectionToClient, profile.cloneSpawnDistance, profile.cloneRushSpeed);
    }

    [TargetRpc]
    private void TargetFakeRush(NetworkConnectionToClient target, float distance, float speed) =>
        TiktikFakeClone.Spawn(this, distance, speed);

    /// <summary>Server: picks a random valid victim, preferring someone other than the previous one.</summary>
    [Server]
    public bool TryPickVictim()
    {
        if (IsRepelled)
        {
            victim = null;
            return false;
        }
        var candidates = new List<PlayerPersistence>();
        foreach (PlayerPersistence player in PlayerPersistence.ActivePlayers)
        {
            if (IsValidVictim(player))
                candidates.Add(player);
        }
        if (candidates.Count > 1 && lastVictim != null)
            candidates.RemoveAll(player => player.netIdentity == lastVictim);
        if (candidates.Count == 0)
        {
            victim = null;
            return false;
        }
        victim = candidates[Random.Range(0, candidates.Count)].netIdentity;
        lastVictim = victim;
        VictimPickTime = NetworkTime.time;
        return true;
    }

    [Server]
    internal void ClearVictim() => victim = null;

    /// <summary>
    /// Server: drops its victim, fades out and waits hidden for <paramref name="seconds"/>; afterwards it picks a different
    /// victim (the previous one is skipped when anyone else is valid) and stalks again.
    /// </summary>
    [Server]
    public void CeaseHuntFor(float seconds)
    {
        repelUntil = NetworkTime.time + Mathf.Max(0f, seconds);
        bool wasShown = visible;
        ChangeState(Waiting);
        SetBanish(0f);
        if (wasShown)
            SetVisible(false, profile.attackFadeOutSeconds);
    }

    /// <summary>A thrown bawang that hits it stops the hunt for <paramref name="seconds"/>.</summary>
    public override bool ServerTakeGarlic(NetworkIdentity thrower, float seconds)
    {
        if (!isServer || !visible)
            return false;
        CeaseHuntFor(seconds);
        return true;
    }

    /// <summary>Like a bawang, a molotov fire near it while hunting makes it give up its victim.</summary>
    protected override void ServerReactToFire(FireZone fire, bool inCore)
    {
        if (!IsRepelled && visible && (CurrentState == Attacking || CurrentState == Stalking))
            CeaseHuntFor(fire.RepelSeconds);
    }

    /// <summary>Server: the victim's eye position and view direction (camera yaw plus synced flashlight pitch).</summary>
    public static void GetView(PlayerPersistence player, out Vector3 eye, out Vector3 forward)
    {
        Camera camera = player.GetComponentInChildren<Camera>(true);
        eye = camera != null ? camera.transform.position : player.transform.position + Vector3.up * 1.6f;
        float pitch = player.TryGetComponent(out PlayerFlashlight flashlight) ? flashlight.SyncedPitch : 0f;
        forward = Quaternion.Euler(pitch, player.transform.eulerAngles.y, 0f) * Vector3.forward;
    }

    public Vector3 BodyCenter => transform.position + Vector3.up * 0.8f;

    /// <summary>Server: true while <paramref name="player"/> points a lit flashlight at it with nothing in between.</summary>
    public bool IsLitBy(PlayerPersistence player)
    {
        if (player == null || !player.TryGetComponent(out PlayerFlashlight flashlight) || !flashlight.IsOn)
            return false;
        GetView(player, out Vector3 eye, out Vector3 forward);
        Vector3 toBody = BodyCenter - eye;
        if (toBody.sqrMagnitude > profile.litRange * profile.litRange || Vector3.Angle(forward, toBody) > profile.litHalfAngle)
            return false;
        return EnemySenses.IsSegmentClear(eye, BodyCenter, Senses.OcclusionMask, player.transform, transform);
    }

    /// <summary>True if <paramref name="player"/> could see <paramref name="point"/> right now (in front and unoccluded).</summary>
    public bool CanBeSeenFrom(PlayerPersistence player, Vector3 point, float halfAngle)
    {
        GetView(player, out Vector3 eye, out Vector3 forward);
        Vector3 body = point + Vector3.up * 0.8f;
        Vector3 flatForward = new Vector3(forward.x, 0f, forward.z);
        Vector3 flatTo = new Vector3(body.x - eye.x, 0f, body.z - eye.z);
        if (flatForward.sqrMagnitude > 0.0001f && flatTo.sqrMagnitude > 0.0001f && Vector3.Angle(flatForward, flatTo) > halfAngle)
            return false;
        return EnemySenses.IsSegmentClear(eye, body, Senses.OcclusionMask, player.transform, transform);
    }

    /// <summary>
    /// Server: a rooftop near <paramref name="player"/> to stalk from (top of a building-sized collider, on its edge
    /// toward the player), preferring roofs the player can see; else a block corner near them.
    /// </summary>
    public bool TryFindPerch(PlayerPersistence player, out Vector3 perch)
    {
        visiblePerches.Clear();
        hiddenPerches.Clear();
        Vector3 origin = player.transform.position;
        GetView(player, out Vector3 eye, out _);
        int count = Physics.OverlapSphereNonAlloc(origin, profile.perchSearchRadius, overlapBuffer, Senses.OcclusionMask,
            QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
        {
            Collider candidate = overlapBuffer[i];
            if (!IsPerchCollider(candidate, origin))
                continue;
            Bounds bounds = candidate.bounds;
            float inset = profile.perchEdgeInset;
            float x = Mathf.Clamp(origin.x, bounds.min.x + Mathf.Min(inset, bounds.extents.x), bounds.max.x - Mathf.Min(inset, bounds.extents.x));
            float z = Mathf.Clamp(origin.z, bounds.min.z + Mathf.Min(inset, bounds.extents.z), bounds.max.z - Mathf.Min(inset, bounds.extents.z));
            if (!Physics.Raycast(new Vector3(x, bounds.max.y + 1f, z), Vector3.down, out RaycastHit hit, bounds.size.y + 2f,
                    Senses.OcclusionMask, QueryTriggerInteraction.Ignore) ||
                hit.normal.y < 0.6f || hit.point.y < origin.y + profile.perchMinHeight * 0.8f)
                continue;
            AddPerch(hit.point, origin, eye, player);
        }

        if (visiblePerches.Count == 0 && hiddenPerches.Count == 0)
            AddCornerPerches(origin, eye, player);
        return PickPerch(visiblePerches, out perch) || PickPerch(hiddenPerches, out perch);
    }

    private bool IsPerchCollider(Collider candidate, Vector3 origin)
    {
        Bounds bounds = candidate.bounds;
        if (bounds.size.y < profile.perchMinHeight || bounds.size.x < profile.perchMinWidth || bounds.size.z < profile.perchMinWidth)
            return false;
        if (bounds.max.y < origin.y + profile.perchMinHeight)
            return false;
        Transform t = candidate.transform;
        return t.GetComponentInParent<PlayerPersistence>() == null && t.GetComponentInParent<EnemyBrain>() == null &&
               t.GetComponentInParent<EntranceObstacle>() == null;
    }

    private void AddPerch(Vector3 point, Vector3 origin, Vector3 eye, PlayerPersistence player)
    {
        Vector3 flat = point - origin;
        flat.y = 0f;
        if (flat.magnitude < profile.perchMinDistance || flat.magnitude > profile.perchSearchRadius)
            return;
        if (lastPerch.HasValue && (lastPerch.Value - point).sqrMagnitude < 9f)
            return;
        if (EnemySenses.IsSegmentClear(eye, point + Vector3.up * 0.8f, Senses.OcclusionMask, player.transform, transform))
            visiblePerches.Add(point);
        else
            hiddenPerches.Add(point);
    }

    private void AddCornerPerches(Vector3 origin, Vector3 eye, PlayerPersistence player)
    {
        MapManager map = MapManager.Active;
        var areas = new List<EnemyHomeArea>();
        if (map != null && map.IsBuilt && map.TryGetBlockIndex(origin, out int index))
        {
            areas.Add(EnemyHomeArea.FromBlock(map.GetBlock(index)));
            foreach (int neighbour in map.GetNeighbours(index))
                areas.Add(EnemyHomeArea.FromBlock(map.GetBlock(neighbour)));
        }
        else
            areas.Add(EnemyHomeArea.FromBox(origin, new Vector2(profile.perchMinDistance + 4f, profile.perchMinDistance + 4f)));

        foreach (EnemyHomeArea area in areas)
        {
            float hx = Mathf.Max(0f, area.HalfSize.x - profile.cornerInset);
            float hz = Mathf.Max(0f, area.HalfSize.y - profile.cornerInset);
            foreach (Vector2 sign in new[] { new Vector2(-1f, -1f), new Vector2(1f, -1f), new Vector2(-1f, 1f), new Vector2(1f, 1f) })
            {
                var local = new Vector3(sign.x * hx, 0f, sign.y * hz);
                Vector3 corner = area.Block != null ? area.Block.transform.TransformPoint(local) : area.Center + local;
                Vector3 top = new Vector3(corner.x, origin.y + 40f, corner.z);
                Vector3 ground = Physics.Raycast(top, Vector3.down, out RaycastHit hit, 80f, Senses.OcclusionMask,
                    QueryTriggerInteraction.Ignore) && hit.collider.transform.GetComponentInParent<PlayerPersistence>() == null
                    ? hit.point
                    : new Vector3(corner.x, origin.y, corner.z);
                AddPerch(ground, origin, eye, player);
            }
        }
    }

    private bool PickPerch(List<Vector3> perches, out Vector3 perch)
    {
        if (perches.Count == 0)
        {
            perch = default;
            return false;
        }
        perch = perches[Random.Range(0, perches.Count)];
        lastPerch = perch;
        return true;
    }

    /// <summary>
    /// Server: a NavMesh point <see cref="TiktikProfile.attackSpawnMinDistance"/>..Max from the victim that the victim
    /// cannot see right now (behind them or behind cover; skipped when <paramref name="requireHidden"/> is false) with
    /// a walkable path to them.
    /// </summary>
    public bool TryFindAttackSpawn(PlayerPersistence player, bool requireHidden, out Vector3 point) =>
        TryFindAttackSpawn(player, requireHidden, profile.attackSpawnMinDistance, profile.attackSpawnMaxDistance, null, 0f, out point);

    /// <summary>
    /// Server: like <see cref="TryFindAttackSpawn(PlayerPersistence, bool, out Vector3)"/> within
    /// <paramref name="minDistance"/>..<paramref name="maxDistance"/>, at least <paramref name="avoidDistance"/> from
    /// <paramref name="avoid"/> (e.g. where it just vanished).
    /// </summary>
    public bool TryFindAttackSpawn(PlayerPersistence player, bool requireHidden, float minDistance, float maxDistance,
        Vector3? avoid, float avoidDistance, out Vector3 point)
    {
        Vector3 origin = player.transform.position;
        float yaw = player.transform.eulerAngles.y;
        for (int attempt = 0; attempt < 48; attempt++)
        {
            // Mostly behind the victim; some anywhere (cover may hide those).
            float angle = attempt % 3 == 2 ? Random.Range(0f, 360f) : yaw + 180f + Random.Range(-70f, 70f);
            float distance = Random.Range(minDistance, Mathf.Max(minDistance, maxDistance));
            Vector3 guess = origin + Quaternion.Euler(0f, angle, 0f) * Vector3.forward * distance;
            if (!NavMesh.SamplePosition(guess, out NavMeshHit hit, 2f, Motor.AreaMask))
                continue;
            Vector3 flat = hit.position - origin;
            flat.y = 0f;
            if (flat.magnitude < minDistance * 0.75f)
                continue;
            if (avoid.HasValue)
            {
                Vector3 shift = hit.position - avoid.Value;
                shift.y = 0f;
                if (shift.magnitude < avoidDistance)
                    continue;
            }
            if (requireHidden && CanBeSeenFrom(player, hit.position, 70f))
                continue;
            if (!Motor.HasCompletePath(hit.position, origin))
                continue;
            point = hit.position;
            return true;
        }
        point = default;
        return false;
    }

    /// <summary>
    /// Server: a random NavMesh spot to reappear at after the flashlight banished it: in the victim's block or an
    /// openly reachable neighbouring block (never behind an uncleared barricade),
    /// <see cref="TiktikProfile.relocateMinDistance"/>..Max from the victim, outside the rear cone
    /// (<see cref="TiktikProfile.relocateRearHalfAngle"/>), at least <paramref name="minShift"/> from
    /// <paramref name="vanishedAt"/>, hidden from the victim when <paramref name="requireHidden"/>, with a path to them.
    /// <paramref name="anyBlock"/> drops the block restriction (a ring around the victim, still never past an uncleared
    /// barricade because the path must be complete); <paramref name="allowRear"/> drops the rear cone. The minimum
    /// distance is never relaxed, so it can never land unfairly close.
    /// </summary>
    public bool TryFindRelocateSpot(PlayerPersistence player, bool requireHidden, Vector3 vanishedAt, float minShift,
        out Vector3 point, bool anyBlock = false, bool allowRear = false)
    {
        Vector3 origin = player.transform.position;
        GetView(player, out _, out Vector3 forward);
        var back = new Vector3(-forward.x, 0f, -forward.z);
        float minDistance = profile.relocateMinDistance;
        float maxDistance = Mathf.Max(minDistance, profile.relocateMaxDistance);

        var areas = new List<EnemyHomeArea>();
        MapManager map = MapManager.Active;
        if (!anyBlock && map != null && map.IsBuilt && map.TryGetBlockIndex(origin, out int index))
        {
            bool[] reachable = map.FindOpenlyReachable(index);
            areas.Add(EnemyHomeArea.FromBlock(map.GetBlock(index)));
            foreach (int neighbour in map.GetNeighbours(index))
            {
                if (neighbour >= 0 && neighbour < reachable.Length && reachable[neighbour])
                    areas.Add(EnemyHomeArea.FromBlock(map.GetBlock(neighbour)));
            }
        }

        for (int attempt = 0; attempt < 64; attempt++)
        {
            Vector3 guess;
            if (areas.Count > 0)
            {
                EnemyHomeArea area = areas[Random.Range(0, areas.Count)];
                var local = new Vector3(Random.Range(-area.HalfSize.x, area.HalfSize.x), 0f,
                    Random.Range(-area.HalfSize.y, area.HalfSize.y));
                guess = area.Block != null ? area.Block.transform.TransformPoint(local) : area.Center + local;
            }
            else
                guess = origin + Quaternion.Euler(0f, Random.Range(0f, 360f), 0f) * Vector3.forward * Random.Range(minDistance, maxDistance);
            if (!NavMesh.SamplePosition(guess, out NavMeshHit hit, 2f, Motor.AreaMask))
                continue;
            Vector3 flat = hit.position - origin;
            flat.y = 0f;
            float distance = flat.magnitude;
            if (distance < minDistance || distance > maxDistance)
                continue;
            if (!allowRear && back.sqrMagnitude > 0.0001f && Vector3.Angle(back, flat) < profile.relocateRearHalfAngle)
                continue;
            Vector3 shift = hit.position - vanishedAt;
            shift.y = 0f;
            if (shift.magnitude < minShift)
                continue;
            if (requireHidden && CanBeSeenFrom(player, hit.position, 70f))
                continue;
            if (!Motor.HasCompletePath(hit.position, origin))
                continue;
            point = hit.position;
            return true;
        }
        point = default;
        return false;
    }

    /// <summary>Server: moves (hidden) to a perch and switches the NavMesh agent off.</summary>
    [Server]
    public void PerchAt(Vector3 point, Vector3 lookAt)
    {
        Motor.Suspend();
        transform.position = point;
        LookTarget = lookAt;
        Locomotion.FaceTowards(lookAt);
        Presentation.SetMotion(false, transform.eulerAngles.y);
        Presentation.SnapNow();
    }

    /// <summary>Server: moves to a NavMesh point and re-enables the agent.</summary>
    [Server]
    public bool LandAt(Vector3 point)
    {
        bool landed = Motor.Resume(point);
        Presentation.SnapNow();
        return landed;
    }

    public override bool ServerTakeWeaponHit(ItemDefinition weapon, NetworkIdentity attacker)
    {
        if (!isServer || !attacking || !visible || CurrentState != Attacking)
            return false;
        Attacking.Stun(weapon != null && weapon.StunSeconds > 0f ? weapon.StunSeconds : profile.defaultStunSeconds);
        return true;
    }
}
