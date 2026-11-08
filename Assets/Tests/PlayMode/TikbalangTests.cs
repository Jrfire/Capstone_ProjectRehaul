#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using Mirror;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem;
using UnityEngine.TestTools;

public sealed class TikbalangTests : InputTestFixture
{
    private const string PlayerPath = "Assets/PREFABS/NetworkPlayer.prefab";
    private const string TikbalangPath = "Assets/PREFABS/Enemies/Tikbalang.prefab";

    private readonly List<GameObject> created = new List<GameObject>();
    private GameObject transportObject;
    private GameObject localPlayer;
    private GameObject remotePlayer;
    private GameObject ground;
    private TikbalangProfile profile;
    private NavMeshDataInstance navMesh;
    private Transport previousTransport;
    private bool previousListen;

    public override void Setup()
    {
        base.Setup();
        previousTransport = Transport.active;
        previousListen = NetworkServer.listen;
        NoiseSystem.ClearPulses();
        profile = ScriptableObject.CreateInstance<TikbalangProfile>();
        profile.walkSpeed = 4f;
        profile.chaseSpeed = 7f;
        profile.idleWaitSeconds = 0.3f;
        profile.lookAroundSeconds = 0.4f;
        profile.searchTimeoutSeconds = 10f;
        profile.chaseDurationSeconds = 1.5f;
        profile.repathInterval = 0.1f;
        profile.defaultStunSeconds = 0.3f;
    }

    public override void TearDown()
    {
        if (transportObject != null)
        {
            NetworkClient.Disconnect();
            NetworkClient.Shutdown();
            NetworkServer.Shutdown();
            Object.DestroyImmediate(transportObject);
        }
        foreach (GameObject item in created)
        {
            if (item != null)
                Object.DestroyImmediate(item);
        }
        created.Clear();
        foreach (GameObject item in new[] { localPlayer, remotePlayer, ground })
        {
            if (item != null)
                Object.DestroyImmediate(item);
        }
        if (navMesh.valid)
            NavMesh.RemoveNavMeshData(navMesh);
        navMesh = default;
        Object.DestroyImmediate(profile);
        NoiseSystem.ClearPulses();
        PlayerStamina.TravelPaused = false;
        PlayerPersistence.ResetLocalPlayerState();
        Transport.active = previousTransport;
        NetworkServer.listen = previousListen;
        base.TearDown();
    }

    [Test]
    public void NoiseIsHeardWhenTheHearingDomeAndNoiseDomeIntersect()
    {
        Assert.That(NoiseSystem.Overlaps(Vector3.zero, 8f, new Vector3(13.9f, 0f, 0f), 6f), Is.True);
        Assert.That(NoiseSystem.Overlaps(Vector3.zero, 8f, new Vector3(14.1f, 0f, 0f), 6f), Is.False);

        NoiseSystem.EmitPulse(new Vector3(10f, 0f, 0f), 3f);
        NoiseSystem.EmitPulse(new Vector3(-12f, 0f, 0f), 5f);
        Assert.That(NoiseSystem.TryHear(Vector3.zero, 8f, null, out Vector3 origin), Is.True);
        Assert.That(origin, Is.EqualTo(new Vector3(10f, 0f, 0f)), "The closest audible noise wins.");
        Assert.That(NoiseSystem.TryHear(Vector3.zero, 8f, point => point.x < 0f, out origin), Is.True);
        Assert.That(origin.x, Is.EqualTo(-12f), "Filters (e.g. the home block) reject noises.");
        Assert.That(NoiseSystem.TryHear(Vector3.zero, 1f, null, out _), Is.False);
    }

    [Test]
    public void ClearingAnEntranceObstacleStopsItCarvingTheNavMesh()
    {
        var obstacle = new GameObject("Test Entrance Obstacle");
        created.Add(obstacle);
        obstacle.AddComponent<EntranceObstacle>();
        var carver = new GameObject("NavMesh Obstacle").AddComponent<NavMeshObstacle>();
        carver.transform.SetParent(obstacle.transform, false);
        carver.carving = true;

        Assert.That(obstacle.GetComponent<EntranceObstacle>().TryUseKey(), Is.True);
        Assert.That(carver.enabled, Is.False, "The NavMesh fills in once the entrance opens.");
        Assert.That(obstacle.activeSelf, Is.False);
    }

    [UnityTest]
    public IEnumerator VisionIsANarrowOccludedConeThatWidensWhileSearching()
    {
        SpawnPlayers();
        yield return null;
        TikbalangBrain brain = CreateTikbalang(Vector3.zero, 0f, spawn: false);
        EnemySenses senses = brain.Senses;
        PlayerPersistence player = localPlayer.GetComponent<PlayerPersistence>();
        Place(remotePlayer, new Vector3(45f, 0f, -45f));

        Assert.That(senses.VisionAngle, Is.EqualTo(40f));
        Assert.That(senses.HearingRadius, Is.EqualTo(8f));
        Place(localPlayer, new Vector3(0f, 0f, 10f));
        Assert.That(senses.CanSee(player), Is.True);
        Assert.That(senses.FindVisiblePlayer(), Is.EqualTo(player));
        Place(localPlayer, new Vector3(0f, 0f, -10f));
        Assert.That(senses.CanSee(player), Is.False, "Players behind it are not seen.");
        Place(localPlayer, new Vector3(0f, 0f, 25f));
        Assert.That(senses.CanSee(player), Is.False, "Players beyond the vision range are not seen.");

        Place(localPlayer, new Vector3(6f, 0f, 10f));
        Assert.That(senses.CanSee(player), Is.False, "About 31 degrees off-axis is outside the narrow 40-degree cone.");
        brain.ApplySearchingVision();
        Assert.That(senses.VisionAngle, Is.EqualTo(80f));
        Assert.That(senses.CanSee(player), Is.True, "The searching cone is wider.");
        brain.ApplyNormalVision();

        Place(localPlayer, new Vector3(0f, 0f, 10f));
        GameObject wall = Block(new Vector3(0f, 2f, 5f), new Vector3(4f, 4f, 0.5f), carve: false);
        Physics.SyncTransforms();
        Assert.That(senses.CanSee(player), Is.False, "Walls block sight.");
        Object.DestroyImmediate(wall);
        Physics.SyncTransforms();
        Assert.That(senses.CanSee(player), Is.True);
    }

    [UnityTest]
    public IEnumerator IdleWandersInsideItsHomeAndStandsAtEachPoint()
    {
        SpawnArena();
        Place(localPlayer, new Vector3(45f, 0f, 45f));
        Place(remotePlayer, new Vector3(-45f, 0f, 45f));
        TikbalangBrain brain = CreateTikbalang(Vector3.zero, 0f, homeHalfSize: 6f);
        EnemyPresentation presentation = brain.Presentation;

        yield return WaitUntil(() => brain.Motor.IsMoving, 2f, "Idle did not walk to a wander point.");
        Assert.That(brain.StateName, Is.EqualTo("Idle"));
        Assert.That(presentation.AnimationState, Is.EqualTo(EnemyAnimationState.Moving));
        Assert.That(brain.Motor.Agent.speed, Is.EqualTo(profile.walkSpeed));
        Vector3 velocity = brain.Motor.Velocity;
        velocity.y = 0f;
        Assert.That(Mathf.Abs(Mathf.DeltaAngle(presentation.FacingYaw, Quaternion.LookRotation(velocity).eulerAngles.y)), Is.LessThan(10f),
            "The displayed direction follows the movement direction.");

        yield return WaitUntil(() => brain.Idle.IsWaiting, 6f, "It never stopped at its wander point.");
        yield return null;
        Assert.That(brain.Home.Contains(brain.transform.position), Is.True);
        Assert.That(presentation.AnimationState, Is.EqualTo(EnemyAnimationState.Idle), "Standing uses the Idle animation.");
        Assert.That(brain.Motor.Velocity.magnitude, Is.LessThan(0.01f), "Stops instantly (snappy).");

        yield return WaitUntil(() => brain.Motor.IsMoving, 2f, "It did not pick a new point after waiting.");
        for (int i = 0; i < 30; i++)
        {
            yield return null;
            Assert.That(brain.Home.Contains(brain.transform.position), Is.True, "Idle stays inside its home block.");
        }
    }

    [UnityTest]
    public IEnumerator HearingANoiseInsideHomeStartsASearchThatLooksAroundAndReturnsToIdle()
    {
        SpawnArena();
        Place(localPlayer, new Vector3(45f, 0f, -45f));
        Place(remotePlayer, new Vector3(-45f, 0f, -45f));
        TikbalangBrain brain = CreateTikbalang(Vector3.zero, 0f, homeHalfSize: 8f);
        yield return WaitUntil(() => brain.StateName == "Idle", 1f, "The Tikbalang did not start in Idle.");

        NoiseSystem.EmitPulse(new Vector3(20f, 0f, 0f), 15f, 0.5f);
        for (int i = 0; i < 5; i++)
        {
            yield return null;
            Assert.That(brain.StateName, Is.EqualTo("Idle"), "Noises outside the home block are ignored.");
        }

        NoiseSystem.ClearPulses();
        Vector3 origin = new Vector3(5f, 0f, 0f);
        NoiseSystem.EmitPulse(origin, 6f, 0.3f);
        yield return WaitUntil(() => brain.StateName == "Searching", 1f, "A noise inside home did not start a search.");
        Assert.That(brain.Senses.VisionAngle, Is.EqualTo(profile.searchingVisionAngle));
        Assert.That(brain.Searching.Origin, Is.EqualTo(origin));

        yield return WaitUntil(() => brain.Searching.IsLookingAround, 5f, "It did not reach the noise origin.");
        Vector3 flat = brain.transform.position - origin;
        flat.y = 0f;
        Assert.That(flat.magnitude, Is.LessThan(1.5f));
        var yaws = new HashSet<int>();
        while (brain.StateName == "Searching")
        {
            yaws.Add(Mathf.RoundToInt(brain.transform.eulerAngles.y) % 360);
            yield return null;
        }
        Assert.That(yaws.Count, Is.GreaterThanOrEqualTo(3), "It looks around in snapped turns.");
        Assert.That(brain.StateName, Is.EqualTo("Idle"), "Nothing was spotted, so it goes back to Idle.");
        Assert.That(brain.Senses.VisionAngle, Is.EqualTo(profile.visionAngle));
    }

    [UnityTest]
    public IEnumerator ASprintingPlayersNoiseDomeIsHeard()
    {
        Keyboard keyboard = InputSystem.AddDevice<Keyboard>();
        profile.walkSpeed = 0f;
        SpawnArena();
        Place(remotePlayer, new Vector3(-45f, 0f, -45f));
        Place(localPlayer, new Vector3(0f, 0f, -3f), 180f);
        TikbalangBrain brain = CreateTikbalang(Vector3.zero, 0f, homeHalfSize: 20f);
        PlayerNoise noise = localPlayer.GetComponent<PlayerNoise>();
        yield return WaitUntil(() => brain.StateName == "Idle", 1f, "The Tikbalang did not start in Idle.");
        Assert.That(noise.IsMakingNoise, Is.False, "Walking players are silent.");

        Press(keyboard.leftShiftKey);
        Press(keyboard.wKey);
        string firstReaction = null;
        float timeout = Time.realtimeSinceStartup + 2f;
        while (firstReaction == null && Time.realtimeSinceStartup < timeout)
        {
            yield return null;
            if (brain.StateName != "Idle")
                firstReaction = brain.StateName;
        }
        Assert.That(noise.IsMakingNoise, Is.True, "Sprinting emits a noise dome.");
        Assert.That(noise.NoiseRadius, Is.EqualTo(6f));
        Release(keyboard.leftShiftKey);
        Release(keyboard.wKey);
        Assert.That(firstReaction, Is.EqualTo("Searching"), "Hearing (not sight) triggered the reaction.");
        Vector3 offset = brain.Searching.Origin - localPlayer.transform.position;
        offset.y = 0f;
        Assert.That(offset.magnitude, Is.LessThan(2f), "It investigates where the sprinting player was heard.");
    }

    [UnityTest]
    public IEnumerator ChaseLocksOntoTheFirstPlayerThenReturnsHomeIgnoringPlayers()
    {
        profile.walkSpeed = 0f;
        SpawnArena();
        Place(remotePlayer, new Vector3(-45f, 0f, -45f));
        Place(localPlayer, new Vector3(0f, 0f, 4f));
        TikbalangBrain brain = CreateTikbalang(Vector3.zero, 0f, homeHalfSize: 6f);
        var states = new List<string>();

        yield return WaitUntil(() => brain.StateName == "Chase", 1f, "Spotting a player inside home did not start a chase.");
        Assert.That(brain.ChaseTarget, Is.EqualTo(localPlayer.GetComponent<NetworkIdentity>()));
        yield return WaitUntil(() => brain.Motor.Agent.speed == profile.chaseSpeed, 0.5f, "Chasing does not use the chase speed.");
        profile.walkSpeed = 4f;

        Place(localPlayer, new Vector3(0f, 0f, 14f));
        Place(remotePlayer, new Vector3(0.5f, 0f, 2.5f));
        for (int i = 0; i < 20; i++)
        {
            yield return null;
            Assert.That(brain.ChaseTarget, Is.EqualTo(localPlayer.GetComponent<NetworkIdentity>()),
                "It keeps chasing the first player, even outside its home and with another player closer.");
        }

        float timeout = Time.realtimeSinceStartup + 8f;
        while (Time.realtimeSinceStartup < timeout && !states.Contains("Idle"))
        {
            if (states.Count == 0 || states[states.Count - 1] != brain.StateName)
                states.Add(brain.StateName);
            yield return null;
        }
        if (states[states.Count - 1] != brain.StateName)
            states.Add(brain.StateName);
        CollectionAssert.AreEqual(new[] { "Chase", "Returning", "Idle" }, states.GetRange(0, Mathf.Min(3, states.Count)),
            "The chase times out, then it walks home ignoring the visible player before resuming Idle.");
        Assert.That(brain.Home.Contains(brain.transform.position), Is.True);
    }

    [UnityTest]
    public IEnumerator ChaseContactRemovesOneHeartThenPausesBeforeResuming()
    {
        profile.walkSpeed = 0f;
        profile.chaseDurationSeconds = 20f;
        SpawnArena();
        Place(remotePlayer, new Vector3(-45f, 0f, -45f));
        Place(localPlayer, new Vector3(0f, 0f, 4f));
        PlayerHealth health = localPlayer.GetComponent<PlayerHealth>();
        TikbalangBrain brain = CreateTikbalang(Vector3.zero, 0f, homeHalfSize: 6f);
        yield return WaitUntil(() => brain.StateName == "Chase", 1f, "The chase did not start.");
        yield return WaitUntil(() => brain.Chase.HitCount == 1, 3f, "Touching the chased player did not hurt them.");
        float hitTime = Time.time;
        Assert.That(health.CurrentHealth, Is.EqualTo(4), "A touch removes 2 health (one heart).");
        Assert.That(brain.Chase.IsPausedAfterHit, Is.True);

        while (Time.time - hitTime < profile.hitPauseSeconds - 0.15f)
        {
            yield return null;
            Assert.That(brain.Chase.HitCount, Is.EqualTo(1), "No extra hits land during the pause.");
            Assert.That(brain.Motor.IsMoving, Is.False, "It stands still after a hit.");
            Assert.That(brain.StateName, Is.EqualTo("Chase"));
        }

        Place(localPlayer, new Vector3(0f, 0f, 8f));
        yield return WaitUntil(() => !brain.Chase.IsPausedAfterHit, 0.5f, "The pause did not end.");
        yield return WaitUntil(() => brain.Motor.IsMoving, 1f, "The chase did not resume after the pause.");
        Assert.That(Time.time - hitTime, Is.GreaterThanOrEqualTo(profile.hitPauseSeconds - 0.05f));
        yield return WaitUntil(() => brain.Chase.HitCount == 2, 3f, "The resumed chase did not land a second hit.");
        Assert.That(health.CurrentHealth, Is.EqualTo(2));
    }

    [Test]
    public void DefaultProfileChasesForOneMinuteAndHitsForOneHeart()
    {
        var defaults = AssetDatabase.LoadAssetAtPath<TikbalangProfile>("Assets/PREFABS/Enemies/Profiles/Tikbalang_Default.asset");
        Assert.That(defaults.chaseDurationSeconds, Is.EqualTo(60f));
        Assert.That(defaults.contactDamage, Is.EqualTo(2));
        Assert.That(defaults.hitPauseSeconds, Is.EqualTo(1.5f));
        Assert.That(defaults.attackRange, Is.GreaterThan(defaults.catchDistance), "It can touch the player it stops next to.");
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(TikbalangPath);
        Assert.That(prefab.GetComponentInChildren<SpriteRenderer>(true).GetComponent<ReducedPixelation>(), Is.Not.Null,
            "Its sprite renders with finer pixelation.");
    }

    [UnityTest]
    public IEnumerator StunDropsTheChaseAndThenWalksHome()
    {
        profile.walkSpeed = 0f;
        SpawnArena();
        Place(remotePlayer, new Vector3(-45f, 0f, -45f));
        Place(localPlayer, new Vector3(0f, 0f, 5f));
        TikbalangBrain brain = CreateTikbalang(Vector3.zero, 0f, homeHalfSize: 6f);
        yield return WaitUntil(() => brain.StateName == "Chase", 1f, "The chase did not start.");
        Place(localPlayer, new Vector3(45f, 0f, 45f));

        brain.Stun(0.3f);
        Assert.That(brain.StateName, Is.EqualTo("Stunned"));
        Assert.That(brain.ChaseTarget, Is.Null);
        yield return null;
        Assert.That(brain.Motor.IsMoving, Is.False);
        yield return WaitUntil(() => brain.StateName == "Returning" || brain.StateName == "Idle", 2f, "It stayed stunned.");
    }

    [UnityTest]
    public IEnumerator PlayerWeaponHitsStunTheTikbalangAndDropItsChase()
    {
        profile.walkSpeed = 0f;
        SpawnArena();
        Place(remotePlayer, new Vector3(-45f, 0f, -45f));
        Place(localPlayer, new Vector3(0f, 0f, 5f));
        TikbalangBrain brain = CreateTikbalang(Vector3.zero, 0f, homeHalfSize: 6f);
        yield return WaitUntil(() => brain.StateName == "Chase", 1f, "The chase did not start.");

        Vector3 body = brain.GetComponent<Collider>().bounds.center;
        Vector3 standAt = new Vector3(body.x, 0f, body.z + 2f);
        Place(localPlayer, standAt, 180f);
        ItemDefinition stick = ScriptableObject.CreateInstance<ItemDefinition>();
        try
        {
            stick.Configure("stick", "Stick", null, ItemSlotKind.Active);
            stick.ConfigureWeapon(2.5f, 0.4f, breaks: false);
            Vector3 origin = standAt + Vector3.up * 1.6f;
            GameObject hit = localPlayer.GetComponent<PlayerItemUse>().ServerAttack(stick, origin, (body - origin).normalized);
            Assert.That(hit, Is.SameAs(brain.gameObject), "The swing hits the Tikbalang.");
            Assert.That(brain.StateName, Is.EqualTo("Stunned"));
            Assert.That(brain.ChaseTarget, Is.Null, "A stunned Tikbalang drops its chase.");
            yield return null;
            Assert.That(brain.Motor.IsMoving, Is.False);
            yield return WaitUntil(() => brain.StateName == "Returning" || brain.StateName == "Idle", 2f,
                "It stayed stunned longer than the weapon's stun time.");
        }
        finally
        {
            Object.DestroyImmediate(stick);
        }
    }

    [UnityTest]
    public IEnumerator SpriteDirectionFollowsMovementWhileItsEyesStayOnTheTarget()
    {
        profile.walkSpeed = 0f;
        profile.catchDistance = 3f;
        SpawnArena();
        Place(remotePlayer, new Vector3(-45f, 0f, -45f));
        Block(new Vector3(0.5f, 0.5f, 3f), new Vector3(8f, 1f, 0.5f), carve: true);
        for (int i = 0; i < 5; i++)
            yield return null;
        Place(localPlayer, new Vector3(0f, 0f, 8f));
        TikbalangBrain brain = CreateTikbalang(Vector3.zero, 0f, homeHalfSize: 12f);
        yield return WaitUntil(() => brain.StateName == "Chase", 1f, "The player behind the low wall was not spotted.");

        bool sawSidestep = false;
        float timeout = Time.realtimeSinceStartup + 4f;
        while (Time.realtimeSinceStartup < timeout && brain.StateName == "Chase" && !sawSidestep)
        {
            yield return null;
            if (!brain.Motor.IsMoving)
                continue;
            Vector3 toPlayer = localPlayer.transform.position - brain.transform.position;
            float eyeYaw = Quaternion.LookRotation(new Vector3(toPlayer.x, 0f, toPlayer.z)).eulerAngles.y;
            Assert.That(Mathf.Abs(Mathf.DeltaAngle(brain.transform.eulerAngles.y, eyeYaw)), Is.LessThan(8f), "Its eyes stay locked on the target.");
            sawSidestep = Mathf.Abs(Mathf.DeltaAngle(brain.Presentation.FacingYaw, eyeYaw)) > 30f;
        }
        Assert.That(sawSidestep, Is.True, "Walking around the wall shows the sidestep direction, not the look direction.");
    }

    [UnityTest]
    public IEnumerator SpritesUseTheViewersCameraForDirectionAndMoveInSnappySteps()
    {
        SpawnArena();
        Place(remotePlayer, new Vector3(-45f, 0f, -45f));
        Place(localPlayer, new Vector3(0f, 0f, -6f));
        TikbalangBrain brain = CreateTikbalang(Vector3.zero, 0f, homeHalfSize: 6f, enableBrain: false);
        EnemyPresentation presentation = brain.Presentation;
        yield return null;

        Assert.That(presentation.WorldSprites.sprite, Is.Not.Null, "Assigned frames (or a placeholder silhouette) are shown.");
        Assert.That(presentation.WorldSprites.sprite.bounds.size.y * presentation.WorldSprites.transform.lossyScale.y,
            Is.InRange(1.5f, 4f), "The Tikbalang stands roughly player-sized or taller in the world.");
        var expected = new Dictionary<float, CharacterDirection>
        {
            { 0f, CharacterDirection.North },
            { 180f, CharacterDirection.South },
            { 90f, CharacterDirection.East },
            { 270f, CharacterDirection.West }
        };
        foreach (KeyValuePair<float, CharacterDirection> pair in expected)
        {
            presentation.SetMotion(false, pair.Key);
            yield return null;
            Assert.That(presentation.DisplayedDirection, Is.EqualTo(pair.Value), $"Yaw {pair.Key}");
        }
        Transform viewer = localPlayer.GetComponentInChildren<Camera>().transform;
        Quaternion billboard = PlayerVision.BillboardRotation(viewer.position, presentation.WorldSprites.transform.position, viewer.eulerAngles.y);
        Assert.That(Quaternion.Angle(presentation.WorldSprites.transform.rotation, billboard), Is.LessThan(0.5f),
            "The sprite billboards toward the viewer's camera.");

        presentation.SnapNow();
        float snappedAt = Time.time;
        Vector3 spriteBefore = presentation.WorldSprites.transform.position;
        brain.transform.position += new Vector3(0.5f, 0f, 0f);
        yield return null;
        if (Time.time - snappedAt < 1f / presentation.SnapsPerSecond)
            Assert.That(presentation.WorldSprites.transform.position, Is.EqualTo(spriteBefore), "The sprite waits for the next step.");
        yield return new WaitForSeconds(0.15f);
        Assert.That(presentation.WorldSprites.transform.position.x, Is.EqualTo(brain.transform.position.x).Within(0.0001f));

        brain.transform.position += new Vector3(10f, 0f, 0f);
        yield return null;
        Assert.That(presentation.WorldSprites.transform.position.x, Is.EqualTo(brain.transform.position.x).Within(0.0001f),
            "Teleports are shown immediately.");
    }

    [UnityTest]
    public IEnumerator TeleportsOnlyToSpotsHiddenFromEveryPlayer()
    {
        profile.walkSpeed = 0f;
        SpawnArena();
        Place(localPlayer, new Vector3(0f, 0f, 0f));
        Place(remotePlayer, new Vector3(2f, 0f, 0f));
        Block(new Vector3(0f, 2.5f, -10f), new Vector3(14f, 5f, 1f), carve: false);
        TikbalangBrain brain = CreateTikbalang(new Vector3(30f, 0f, 30f), 0f, homeHalfSize: 3f);
        yield return WaitUntil(() => brain.Motor.IsActive, 1f, "The agent did not activate.");
        Physics.SyncTransforms();

        EnemyHomeArea open = EnemyHomeArea.FromBox(new Vector3(0f, 0f, 15f), new Vector2(3f, 3f));
        EnemyHomeArea hidden = EnemyHomeArea.FromBox(new Vector3(0f, 0f, -15f), new Vector2(3f, 3f));
        Vector3 before = brain.transform.position;
        Assert.That(brain.TryTeleportHidden(new[] { open }, 10f, Vector3.zero), Is.False, "Every spot in the open area is visible.");
        Assert.That(brain.transform.position, Is.EqualTo(before));

        Assert.That(brain.TryTeleportHidden(new[] { open, hidden }, 10f, Vector3.zero), Is.True);
        Assert.That(hidden.Contains(brain.transform.position), Is.True, "It falls through to the next area with cover.");
        Assert.That(brain.IsHiddenFromPlayers(brain.transform.position, 10f), Is.True);
        yield return null;
    }

    private void SpawnArena()
    {
        SpawnPlayers();
        var sources = new List<NavMeshBuildSource>();
        NavMeshBuilder.CollectSources(ground.transform, ~0, NavMeshCollectGeometry.PhysicsColliders, 0,
            new List<NavMeshBuildMarkup>(), sources);
        NavMeshData data = NavMeshBuilder.BuildNavMeshData(NavMesh.GetSettingsByID(0), sources,
            new Bounds(Vector3.zero, new Vector3(110f, 20f, 110f)), Vector3.zero, Quaternion.identity);
        navMesh = NavMesh.AddNavMeshData(data);
    }

    private void SpawnPlayers()
    {
        Assert.That(NetworkServer.active || NetworkClient.active, Is.False, "Run these tests without an active NetworkManager.");
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPath);
        transportObject = new GameObject("Tikbalang Test Transport");
        Transport.active = transportObject.AddComponent<TelepathyTransport>();
        NetworkServer.listen = false;
        NetworkServer.Listen(4);
        NetworkClient.RegisterPrefab(prefab);
        NetworkClient.ConnectHost();
        NetworkServer.localConnection.isAuthenticated = true;
        NetworkClient.connection.isAuthenticated = true;
        HostMode.InvokeOnConnected();
        Assert.That(NetworkClient.Ready(), Is.True);

        ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
        ground.name = "Tikbalang Test Ground";
        ground.transform.position = new Vector3(0f, -0.5f, 0f);
        ground.transform.localScale = new Vector3(100f, 1f, 100f);
        Physics.SyncTransforms();

        localPlayer = Object.Instantiate(prefab, new Vector3(40f, 0f, 40f), Quaternion.identity);
        remotePlayer = Object.Instantiate(prefab, new Vector3(-40f, 0f, 40f), Quaternion.identity);
        Assert.That(NetworkServer.AddPlayerForConnection(NetworkServer.localConnection, localPlayer), Is.True);
        NetworkServer.Spawn(remotePlayer);
    }

    private TikbalangBrain CreateTikbalang(Vector3 position, float yaw, float homeHalfSize = 6f, bool spawn = true, bool enableBrain = true)
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(TikbalangPath);
        GameObject enemy = Object.Instantiate(prefab, position, Quaternion.Euler(0f, yaw, 0f));
        created.Add(enemy);
        var brain = enemy.GetComponent<TikbalangBrain>();
        var settings = new SerializedObject(brain);
        settings.FindProperty("profile").objectReferenceValue = profile;
        settings.ApplyModifiedPropertiesWithoutUndo();
        brain.ApplyNormalVision();
        brain.AssignHomeArea(position, new Vector2(homeHalfSize, homeHalfSize));
        brain.enabled = enableBrain;
        if (spawn)
            NetworkServer.Spawn(enemy);
        Physics.SyncTransforms();
        return brain;
    }

    private GameObject Block(Vector3 position, Vector3 size, bool carve)
    {
        GameObject block = GameObject.CreatePrimitive(PrimitiveType.Cube);
        block.name = "Test Wall";
        block.transform.position = position;
        block.transform.localScale = size;
        if (carve)
        {
            var obstacle = block.AddComponent<NavMeshObstacle>();
            obstacle.carving = true;
            obstacle.carveOnlyStationary = true;
            obstacle.carvingTimeToStationary = 0f;
        }
        created.Add(block);
        Physics.SyncTransforms();
        return block;
    }

    private static void Place(GameObject player, Vector3 position, float yaw = 0f)
    {
        var controller = player.GetComponent<CharacterController>();
        controller.enabled = false;
        player.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));
        controller.enabled = true;
        Physics.SyncTransforms();
    }

    private static IEnumerator WaitUntil(System.Func<bool> condition, float seconds, string message)
    {
        float timeout = Time.realtimeSinceStartup + seconds;
        while (!condition() && Time.realtimeSinceStartup < timeout)
            yield return null;
        Assert.That(condition(), Is.True, message);
    }
}
#endif
