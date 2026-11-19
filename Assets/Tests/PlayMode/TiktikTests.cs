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

public sealed class TiktikTests : InputTestFixture
{
    private const string PlayerPath = "Assets/PREFABS/NetworkPlayer.prefab";
    private const string TiktikPath = "Assets/PREFABS/Enemies/Tiktik.prefab";

    private readonly List<GameObject> created = new List<GameObject>();
    private GameObject transportObject;
    private GameObject localPlayer;
    private GameObject remotePlayer;
    private GameObject ground;
    private TiktikProfile profile;
    private NavMeshDataInstance navMesh;
    private Transport previousTransport;
    private bool previousListen;

    public override void Setup()
    {
        base.Setup();
        previousTransport = Transport.active;
        previousListen = NetworkServer.listen;
        NoiseSystem.ClearPulses();
        profile = ScriptableObject.CreateInstance<TiktikProfile>();
        profile.firstPerchDelayMin = 0.1f;
        profile.firstPerchDelayMax = 0.1f;
        profile.attackAfterSeconds = 100f;
        profile.perchVisibleMin = 0.6f;
        profile.perchVisibleMax = 0.6f;
        profile.perchGapMin = 0.3f;
        profile.perchGapMax = 0.3f;
        profile.perchFadeSeconds = 0.2f;
        profile.revealDelay = 0.1f;
        profile.attackFadeInSeconds = 0.2f;
        profile.attackFadeOutSeconds = 0.2f;
        profile.attackSpawnMinDistance = 6f;
        profile.attackSpawnMaxDistance = 9f;
        profile.rushSpeed = 8f;
        profile.attackIntervalSeconds = 0.6f;
        profile.attackDurationSeconds = 60f;
        profile.litFadeSeconds = 5f;
        profile.relocateFadeInSeconds = 0.2f;
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
    public void PrefabIsANavMeshEnemyWithTwoAnimationsAndAFlickerFade()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(TiktikPath);
        var brain = prefab.GetComponent<TiktikBrain>();
        Assert.That(brain, Is.Not.Null);
        Assert.That(brain.Profile, Is.Not.Null);
        Assert.That(prefab.GetComponent<NavMeshAgent>(), Is.Not.Null);
        Assert.That(prefab.GetComponent<DistanceFadeSprite>(), Is.Not.Null, "Same distance fade as the Tikbalang.");
        Assert.That(prefab.GetComponent<EnemyAudio>(), Is.Not.Null);
        Assert.That(prefab.GetComponent<EnemyJumpscare>(), Is.Not.Null);
        Assert.That(prefab.GetComponent<EnemyLoadingMiniature>(), Is.Not.Null);
        Assert.That(prefab.GetComponent<EnemyFearSource>(), Is.Not.Null);
        EnemySpriteDefinition sprites = prefab.GetComponent<EnemyPresentation>().Sprites;
        Assert.That(sprites, Is.Not.Null);
        CharacterAnimation idle = sprites.GetAnimation(EnemyAnimationState.Idle);
        CharacterAnimation moving = sprites.GetAnimation(EnemyAnimationState.Moving);
        Assert.That(idle.IsReady && moving.IsReady, Is.True);
        Assert.That(idle.FramesPerDirection, Is.EqualTo(1), "Idle is the first frame of each sheet.");
        Assert.That(moving.FramesPerDirection, Is.EqualTo(4));
        Assert.That(idle.GetFrame(CharacterDirection.South, 0d, true), Is.EqualTo(moving.GetFrame(CharacterDirection.South, 0d, true)));
    }

    [UnityTest]
    public IEnumerator StartsHiddenPicksAVictimAndPerchesOnARoofEyeingThem()
    {
        SpawnArena(withRemote: false);
        GameObject building = CreateBuilding(new Vector3(0f, 0f, 14f), new Vector3(6f, 5f, 6f));
        Place(localPlayer, Vector3.zero);
        TiktikBrain brain = CreateTiktik(new Vector3(-20f, 0f, -20f));
        yield return null;

        Assert.That(brain.IsVisible, Is.False, "It starts hidden.");
        Assert.That(brain.Presentation.WorldSprites.gameObject.activeSelf, Is.False);
        Assert.That(brain.GetComponent<Collider>().enabled, Is.False, "Hidden bodies cannot be hit or aimed at.");

        yield return WaitUntil(() => brain.Victim == localPlayer.GetComponent<NetworkIdentity>(), 2f, "It picks a victim.");
        yield return WaitUntil(() => brain.CurrentState == brain.Stalking && brain.IsVisible, 3f, "It appears to stalk them.");
        Assert.That(brain.transform.position.y, Is.EqualTo(5f).Within(0.2f), "It perches on the roof.");
        Bounds roof = building.GetComponent<Collider>().bounds;
        Assert.That(roof.Contains(brain.transform.position - Vector3.up * 0.1f), Is.True);
        Assert.That(brain.Motor.Suspended, Is.True, "The NavMesh agent is off while perched.");
        Assert.That(brain.LookTarget.HasValue, Is.True, "It eyes its victim.");

        yield return WaitUntil(() => brain.Presence >= 1f, 1f, "It fades in.");
        Assert.That(brain.Presentation.WorldSprites.gameObject.activeSelf, Is.True);
        Assert.That(brain.Opacity, Is.LessThanOrEqualTo(1f));
        Assert.That(brain.GetComponent<EnemyFearSource>().enabled, Is.False, "No panic before the attack.");
        Assert.That(brain.ChasedPlayer, Is.Null, "No chase music before the attack.");
        Assert.That(brain.IsAttacking, Is.False);

        int perches = brain.Stalking.PerchCount;
        yield return WaitUntil(() => !brain.IsVisible, 2f, "It disappears after a few seconds.");
        yield return WaitUntil(() => brain.Stalking.PerchCount > perches, 3f, "... and reappears.");
    }

    [UnityTest]
    public IEnumerator FallsBackToBlockCornersWithoutRooftops()
    {
        SpawnArena(withRemote: false);
        Place(localPlayer, Vector3.zero);
        TiktikBrain brain = CreateTiktik(new Vector3(-20f, 0f, -20f));
        yield return WaitUntil(() => brain.CurrentState == brain.Stalking && brain.IsVisible, 3f, "It still appears.");
        Vector3 offset = brain.transform.position - localPlayer.transform.position;
        Assert.That(Mathf.Abs(offset.x), Is.GreaterThan(profile.perchMinDistance * 0.5f), "At a corner of the area ...");
        Assert.That(Mathf.Abs(offset.z), Is.GreaterThan(profile.perchMinDistance * 0.5f), "... not in the open.");
    }

    [UnityTest]
    public IEnumerator AttacksFromOutOfSightFreezesInTheFlashlightAndHitsAtIntervals()
    {
        profile.attackAfterSeconds = 0.3f;
        SpawnArena(withRemote: false);
        Place(localPlayer, Vector3.zero);
        var flashlight = localPlayer.GetComponent<PlayerFlashlight>();
        if (!flashlight.IsOn)
            flashlight.Toggle();
        var health = localPlayer.GetComponent<PlayerHealth>();
        TiktikBrain brain = CreateTiktik(new Vector3(-20f, 0f, -20f));

        yield return WaitUntil(() => brain.CurrentState == brain.Attacking && brain.IsVisible, 4f, "The attack starts.");
        Assert.That(brain.IsAttacking, Is.True);
        Assert.That(brain.ChasedPlayer, Is.EqualTo(localPlayer.GetComponent<NetworkIdentity>()), "Chase music starts now.");
        Vector3 toSpawn = brain.Attacking.SpawnPoint - localPlayer.transform.position;
        toSpawn.y = 0f;
        Assert.That(Vector3.Angle(localPlayer.transform.forward, toSpawn), Is.GreaterThan(70f), "It appears out of sight.");
        Assert.That(brain.Presence, Is.LessThan(1f), "It fades in.");
        yield return WaitUntil(() => brain.GetComponent<EnemyFearSource>().enabled, 1f, "The fear effect runs while attacking.");

        // Turn to it with the flashlight on: it stops.
        Vector3 look = brain.transform.position - localPlayer.transform.position;
        Place(localPlayer, Vector3.zero, Quaternion.LookRotation(new Vector3(look.x, 0f, look.z)).eulerAngles.y);
        yield return WaitUntil(() => brain.Attacking.Current == TiktikAttackingState.Phase.Rushing && brain.Attacking.IsFrozen,
            2f, "The flashlight holds it still.");
        Vector3 frozenAt = brain.transform.position;
        yield return new WaitForSeconds(0.5f);
        Assert.That(Vector3.Distance(brain.transform.position, frozenAt), Is.LessThan(0.3f));
        Assert.That(health.CurrentHealth, Is.EqualTo(6));

        // Looking away lets it rush and hit, one heart at a time.
        Place(localPlayer, Vector3.zero, localPlayer.transform.eulerAngles.y + 180f);
        yield return WaitUntil(() => brain.Attacking.HitCount >= 1, 3f, "It rushes the player once they look away.");
        Assert.That(health.CurrentHealth, Is.EqualTo(4), "Each hit takes one heart.");
        float firstHit = Time.time;
        yield return WaitUntil(() => brain.Attacking.HitCount >= 2, 3f, "It keeps attacking.");
        Assert.That(Time.time - firstHit, Is.GreaterThanOrEqualTo(profile.attackIntervalSeconds - 0.05f), "Hits are spaced out.");
        Assert.That(health.CurrentHealth, Is.EqualTo(2));
    }

    [UnityTest]
    public IEnumerator FlashlightFadesItAwayThenItTeleportsCloseAndRushesAgain()
    {
        profile.attackAfterSeconds = 0.3f;
        profile.litFadeSeconds = 0.8f;
        profile.relocateMinDistance = 10f;
        profile.relocateMaxDistance = 16f;
        SpawnArena(withRemote: false);
        Place(localPlayer, Vector3.zero);
        var flashlight = localPlayer.GetComponent<PlayerFlashlight>();
        if (!flashlight.IsOn)
            flashlight.Toggle();
        var health = localPlayer.GetComponent<PlayerHealth>();
        TiktikBrain brain = CreateTiktik(new Vector3(-20f, 0f, -20f));
        yield return WaitUntil(() => brain.CurrentState == brain.Attacking && brain.Attacking.Current == TiktikAttackingState.Phase.Rushing,
            4f, "The attack starts.");

        Vector3 look = brain.transform.position - localPlayer.transform.position;
        float facing = Quaternion.LookRotation(new Vector3(look.x, 0f, look.z)).eulerAngles.y;
        Place(localPlayer, Vector3.zero, facing);
        yield return WaitUntil(() => brain.Banish > 0.4f, 2f, "Held in the light, it fades away.");
        Assert.That(brain.Attacking.IsFrozen, Is.True, "It stops while lit.");
        Assert.That(brain.Opacity, Is.LessThanOrEqualTo(1f - brain.Banish + 0.01f), "Its opacity drops with the fade.");
        Assert.That(health.CurrentHealth, Is.EqualTo(6));

        yield return WaitUntil(() => brain.Attacking.Banishments >= 1, 2f, "Fully faded, it teleports.");
        yield return WaitUntil(() => brain.Attacking.Current == TiktikAttackingState.Phase.Rushing && brain.IsVisible, 2f,
            "It reappears and rushes again.");
        Assert.That(brain.CurrentState, Is.EqualTo(brain.Attacking), "The attack continues.");
        Assert.That(brain.Banish, Is.Zero);
        Vector3 shift = brain.Attacking.SpawnPoint - brain.Attacking.VanishedAt;
        shift.y = 0f;
        Assert.That(shift.magnitude, Is.GreaterThanOrEqualTo(profile.relocateMinShift - 0.1f), "A different place ...");
        Vector3 toPlayer = brain.Attacking.SpawnPoint - localPlayer.transform.position;
        toPlayer.y = 0f;
        Assert.That(toPlayer.magnitude, Is.InRange(profile.relocateMinDistance - 0.1f, profile.relocateMaxDistance + 0.1f),
            "... a fair distance from the victim, not right on top of them ...");
        Vector3 back = -localPlayer.transform.forward;
        back.y = 0f;
        Assert.That(Vector3.Angle(back, toPlayer), Is.GreaterThanOrEqualTo(profile.relocateRearHalfAngle - 0.5f),
            "... and never directly behind them.");

        // Out of the light it reaches the player and hits.
        yield return WaitUntil(() => brain.Attacking.HitCount >= 1 || brain.Attacking.Banishments >= 2, 6f, "It rushes the victim again.");
        if (brain.Attacking.HitCount >= 1)
            Assert.That(health.CurrentHealth, Is.EqualTo(4));
    }

    [UnityTest]
    public IEnumerator RelaxedRelocationSearchesNeverLandCloserThanTheMinimum()
    {
        profile.relocateMinDistance = 10f;
        profile.relocateMaxDistance = 16f;
        SpawnArena(withRemote: false);
        Place(localPlayer, Vector3.zero);
        TiktikBrain brain = CreateTiktik(new Vector3(-20f, 0f, -20f));
        yield return null;

        var victim = localPlayer.GetComponent<PlayerPersistence>();
        Vector3 back = -localPlayer.transform.forward;
        back.y = 0f;
        for (int i = 0; i < 40; i++)
        {
            bool allowRear = i % 2 == 1;
            Assert.That(brain.TryFindRelocateSpot(victim, false, new Vector3(50f, 0f, 50f), 0f, out Vector3 spot,
                anyBlock: true, allowRear: allowRear), Is.True);
            Vector3 toSpot = spot - localPlayer.transform.position;
            toSpot.y = 0f;
            Assert.That(toSpot.magnitude, Is.InRange(profile.relocateMinDistance - 0.1f, profile.relocateMaxDistance + 0.1f),
                "Even the late fallbacks keep a fair distance.");
            if (!allowRear)
                Assert.That(Vector3.Angle(back, toSpot), Is.GreaterThanOrEqualTo(profile.relocateRearHalfAngle - 0.5f));
        }
    }

    [UnityTest]
    public IEnumerator FlashlightFadeRecoversWhenTheLightLeavesEarly()
    {
        profile.attackAfterSeconds = 0.3f;
        profile.litFadeSeconds = 2f;
        profile.litRecoverSeconds = 0.5f;
        SpawnArena(withRemote: false);
        Place(localPlayer, Vector3.zero);
        var flashlight = localPlayer.GetComponent<PlayerFlashlight>();
        if (!flashlight.IsOn)
            flashlight.Toggle();
        TiktikBrain brain = CreateTiktik(new Vector3(-20f, 0f, -20f));
        yield return WaitUntil(() => brain.CurrentState == brain.Attacking && brain.Attacking.Current == TiktikAttackingState.Phase.Rushing,
            4f, "The attack starts.");
        Vector3 look = brain.transform.position - localPlayer.transform.position;
        float facing = Quaternion.LookRotation(new Vector3(look.x, 0f, look.z)).eulerAngles.y;
        Place(localPlayer, Vector3.zero, facing);
        yield return WaitUntil(() => brain.Banish > 0.2f, 2f, "It starts fading.");
        Place(localPlayer, Vector3.zero, facing + 180f);
        yield return WaitUntil(() => brain.Banish <= 0f, 2f, "Out of the light it recovers.");
        Assert.That(brain.Attacking.Banishments, Is.Zero, "It never vanished.");
    }

    [UnityTest]
    public IEnumerator AttackEndsFadesAwayAndPicksADifferentVictim()
    {
        profile.attackAfterSeconds = 0.3f;
        profile.attackDurationSeconds = 0.8f;
        profile.contactDamage = 0;
        profile.firstPerchDelayMin = 30f;
        profile.firstPerchDelayMax = 30f;
        SpawnArena(withRemote: true);
        Place(localPlayer, new Vector3(10f, 0f, 0f));
        Place(remotePlayer, new Vector3(-10f, 0f, 0f));
        TiktikBrain brain = CreateTiktik(new Vector3(0f, 0f, -25f));
        TiktikWaitingState waiting = brain.Waiting;

        yield return WaitUntil(() => brain.Victim != null, 2f, "It picks a victim.");
        NetworkIdentity first = brain.Victim;
        // Skip the long first delay to reach the attack quickly.
        brain.ChangeState(brain.Stalking);
        yield return WaitUntil(() => brain.CurrentState == brain.Attacking, 3f, "The attack starts.");
        yield return WaitUntil(() => brain.CurrentState == waiting, 3f, "The attack ends after its duration.");
        Assert.That(brain.IsVisible, Is.False, "It fades away and despawns.");
        Assert.That(brain.IsAttacking, Is.False);
        Assert.That(brain.GetComponent<EnemyFearSource>().enabled, Is.False);
        yield return WaitUntil(() => brain.Victim != null, 2f, "It rerolls a victim.");
        Assert.That(brain.Victim, Is.Not.EqualTo(first), "Another player is chosen.");
    }

    [UnityTest]
    public IEnumerator BawangOrFireMakeItCeaseTheHuntForAWhile()
    {
        profile.attackAfterSeconds = 0.3f;
        profile.contactDamage = 0;
        SpawnArena(withRemote: false);
        Place(localPlayer, Vector3.zero);
        TiktikBrain brain = CreateTiktik(new Vector3(-20f, 0f, -20f));
        yield return WaitUntil(() => brain.CurrentState == brain.Attacking && brain.IsVisible, 4f, "The attack starts.");

        Assert.That(brain.ServerTakeGarlic(localPlayer.GetComponent<NetworkIdentity>(), 30f), Is.True);
        Assert.That(brain.CurrentState, Is.EqualTo(brain.Waiting), "It ceases its hunt.");
        Assert.That(brain.IsRepelled, Is.True);
        yield return new WaitForSeconds(1.5f);
        Assert.That(brain.Victim, Is.Null, "No victim is picked while repelled.");
        Assert.That(brain.CurrentState, Is.EqualTo(brain.Waiting));
    }

    [UnityTest]
    public IEnumerator AMolotovFireNearTheAttackMakesItGiveUp()
    {
        profile.attackAfterSeconds = 0.3f;
        profile.contactDamage = 0;
        SpawnArena(withRemote: false);
        Place(localPlayer, Vector3.zero);
        TiktikBrain brain = CreateTiktik(new Vector3(-20f, 0f, -20f));
        yield return WaitUntil(() => brain.CurrentState == brain.Attacking && brain.IsVisible, 4f, "The attack starts.");
        TestFires.Spawn(brain.transform.position, created);
        yield return WaitUntil(() => brain.CurrentState == brain.Waiting, 1f, "The fire did not end the attack.");
        Assert.That(brain.IsRepelled, Is.True);
    }

    [UnityTest]
    public IEnumerator DeadVictimsAreDroppedAndAWeaponHitStunsIt()
    {
        profile.attackAfterSeconds = 0.3f;
        SpawnArena(withRemote: false);
        Place(localPlayer, Vector3.zero);
        TiktikBrain brain = CreateTiktik(new Vector3(-20f, 0f, -20f));
        yield return WaitUntil(() => brain.CurrentState == brain.Attacking && brain.IsVisible, 4f, "The attack starts.");

        var weapon = ScriptableObject.CreateInstance<ItemDefinition>();
        try
        {
            Assert.That(brain.ServerTakeWeaponHit(weapon, localPlayer.GetComponent<NetworkIdentity>()), Is.True);
            Assert.That(brain.Attacking.IsStunned, Is.True);
        }
        finally
        {
            Object.DestroyImmediate(weapon);
        }

        localPlayer.GetComponent<PlayerHealth>().SetHealth(0);
        yield return WaitUntil(() => brain.CurrentState == brain.Waiting, 3f, "It gives up on a dead victim.");
        yield return new WaitForSeconds(1.2f);
        Assert.That(brain.Victim, Is.Null, "Dead players are never picked.");
    }

    private GameObject CreateBuilding(Vector3 groundCentre, Vector3 size)
    {
        GameObject building = GameObject.CreatePrimitive(PrimitiveType.Cube);
        building.name = "Tiktik Test Building";
        building.transform.position = groundCentre + Vector3.up * (size.y * 0.5f);
        building.transform.localScale = size;
        created.Add(building);
        Physics.SyncTransforms();
        return building;
    }

    private void SpawnArena(bool withRemote)
    {
        Assert.That(NetworkServer.active || NetworkClient.active, Is.False, "Run these tests without an active NetworkManager.");
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPath);
        transportObject = new GameObject("Tiktik Test Transport");
        Transport.active = transportObject.AddComponent<TelepathyTransport>();
        NetworkServer.listen = false;
        NetworkServer.Listen(4);
        NetworkClient.RegisterPrefab(prefab);
        NetworkClient.RegisterPrefab(AssetDatabase.LoadAssetAtPath<GameObject>(TiktikPath));
        NetworkClient.ConnectHost();
        NetworkServer.localConnection.isAuthenticated = true;
        NetworkClient.connection.isAuthenticated = true;
        HostMode.InvokeOnConnected();
        Assert.That(NetworkClient.Ready(), Is.True);

        ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
        ground.name = "Tiktik Test Ground";
        ground.transform.position = new Vector3(0f, -0.5f, 0f);
        ground.transform.localScale = new Vector3(100f, 1f, 100f);
        Physics.SyncTransforms();

        localPlayer = Object.Instantiate(prefab, new Vector3(40f, 0f, 40f), Quaternion.identity);
        Assert.That(NetworkServer.AddPlayerForConnection(NetworkServer.localConnection, localPlayer), Is.True);
        if (withRemote)
        {
            remotePlayer = Object.Instantiate(prefab, new Vector3(-40f, 0f, 40f), Quaternion.identity);
            NetworkServer.Spawn(remotePlayer);
        }

        var sources = new List<NavMeshBuildSource>();
        NavMeshBuilder.CollectSources(ground.transform, ~0, NavMeshCollectGeometry.PhysicsColliders, 0,
            new List<NavMeshBuildMarkup>(), sources);
        NavMeshData data = NavMeshBuilder.BuildNavMeshData(NavMesh.GetSettingsByID(0), sources,
            new Bounds(Vector3.zero, new Vector3(110f, 20f, 110f)), Vector3.zero, Quaternion.identity);
        navMesh = NavMesh.AddNavMeshData(data);
    }

    private TiktikBrain CreateTiktik(Vector3 position)
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(TiktikPath);
        GameObject enemy = Object.Instantiate(prefab, position, Quaternion.identity);
        created.Add(enemy);
        var brain = enemy.GetComponent<TiktikBrain>();
        brain.ApplyProfile(profile);
        brain.AssignHomeArea(position, new Vector2(4f, 4f));
        NetworkServer.Spawn(enemy);
        Physics.SyncTransforms();
        return brain;
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
