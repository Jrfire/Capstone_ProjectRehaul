#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using Mirror;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.TestTools;

public sealed class ManananggalTests : InputTestFixture
{
    private const string PlayerPath = "Assets/PREFABS/NetworkPlayer.prefab";
    private const string ManananggalPath = "Assets/PREFABS/Enemies/Manananggal.prefab";

    private readonly List<GameObject> created = new List<GameObject>();
    private GameObject transportObject;
    private GameObject localPlayer;
    private GameObject remotePlayer;
    private GameObject ground;
    private ManananggalProfile profile;
    private Transport previousTransport;
    private bool previousListen;

    public override void Setup()
    {
        base.Setup();
        previousTransport = Transport.active;
        previousListen = NetworkServer.listen;
        NoiseSystem.ClearPulses();
        profile = ScriptableObject.CreateInstance<ManananggalProfile>();
        profile.firstPassDelaySeconds = new Vector2(0.2f, 0.2f);
        profile.revealDelaySeconds = 0.1f;
        profile.huntSpeed = 30f;
        profile.huntAltitude = 10f;
        profile.fallbackPassHalfLength = 15f;
        profile.chaseSpeed = 8f;
        profile.leaveSpeed = 30f;
        profile.hitPauseSeconds = 0.8f;
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
        Object.DestroyImmediate(profile);
        NoiseSystem.ClearPulses();
        PlayerStamina.TravelPaused = false;
        PlayerPersistence.ResetLocalPlayerState();
        Transport.active = previousTransport;
        NetworkServer.listen = previousListen;
        base.TearDown();
    }

    [UnityTest]
    public IEnumerator HuntingPassAppearsAtAltitudeFliesStraightAcrossAndDespawns()
    {
        SpawnPlayers();
        ManananggalBrain brain = CreateManananggal(new Vector3(0f, 0f, -30f));
        var sprite = brain.Presentation.WorldSprites.gameObject;
        var body = brain.GetComponent<Collider>();
        var fear = brain.GetComponent<EnemyFearSource>();

        yield return WaitUntil(() => brain.CurrentState == brain.Hunting, 1f, "It did not start hunting.");
        Assert.That(brain.IsHidden, Is.True, "It starts despawned.");
        Assert.That(sprite.activeSelf || body.enabled || fear.enabled, Is.False, "Despawned means invisible, intangible and harmless.");

        yield return WaitUntil(() => brain.Hunting.Phase != ManananggalHuntingState.HuntPhase.Waiting, 2f, "No pass started.");
        Vector3 start = brain.Hunting.PassStart;
        Vector3 end = brain.Hunting.PassEnd;
        Assert.That(start.y, Is.EqualTo(10f).Within(0.01f), "Passes fly at hunting altitude.");
        Assert.That(end.y, Is.EqualTo(10f).Within(0.01f));
        Assert.That(Vector3.Distance(start, end), Is.EqualTo(30f).Within(0.01f));
        Assert.That(Vector3.Distance(brain.transform.position, start), Is.LessThan(0.01f), "It appears at the start edge.");
        Assert.That(brain.IsHidden, Is.True, "It stays hidden briefly so interpolation cannot streak the sprite.");

        yield return WaitUntil(() => !brain.IsHidden, 1f, "It never appeared.");
        Assert.That(sprite.activeSelf && body.enabled && fear.enabled, Is.True);
        Vector3 axis = (end - start).normalized;
        float progress = 0f;
        float timeout = Time.realtimeSinceStartup + 4f;
        while (brain.Hunting.CompletedPassCount == 0 && Time.realtimeSinceStartup < timeout)
        {
            Vector3 offset = brain.transform.position - start;
            Assert.That(Vector3.Cross(axis, offset).magnitude, Is.LessThan(0.05f), "The pass is a straight line.");
            float along = Vector3.Dot(offset, axis);
            Assert.That(along, Is.GreaterThanOrEqualTo(progress - 0.001f));
            progress = along;
            Assert.That(brain.StateName, Is.EqualTo("Hunting"), "Nobody is in view of the pass.");
            yield return null;
        }
        Assert.That(brain.Hunting.CompletedPassCount, Is.EqualTo(1));
        Assert.That(brain.IsHidden, Is.True, "It despawns at the far side.");
        Assert.That(brain.Hunting.Phase, Is.EqualTo(ManananggalHuntingState.HuntPhase.Waiting));
        Assert.That(brain.Hunting.TimeRemaining, Is.InRange(14.9f, 40f), "It waits 15-40 s before the next pass.");

        brain.Hunting.SkipWait();
        yield return WaitUntil(() => brain.Hunting.PassCount == 2, 1f, "The next pass did not start.");
    }

    [UnityTest]
    public IEnumerator DownwardConeSeesPlayersBelowAndAheadButNotBehindOrUnderCover()
    {
        SpawnPlayers();
        ManananggalBrain brain = CreateManananggal(new Vector3(0f, 10f, 0f), enableBrain: false);
        var player = localPlayer.GetComponent<PlayerPersistence>();
        EnemySenses senses = brain.Senses;
        Assert.That(Vector3.Angle(senses.VisionAxis, Vector3.forward), Is.EqualTo(50f).Within(0.1f));
        Assert.That(senses.VisionAxis.y, Is.LessThan(0f), "The cone points down.");

        Place(localPlayer, new Vector3(0f, 0f, 8f));
        Assert.That(senses.CanSee(player), Is.True, "Below and a little ahead.");
        Place(localPlayer, new Vector3(0f, 0f, -8f));
        Assert.That(senses.CanSee(player), Is.False, "Behind.");
        Place(localPlayer, new Vector3(0f, 0f, 40f));
        Assert.That(senses.CanSee(player), Is.False, "Out of range.");

        Place(localPlayer, new Vector3(0f, 0f, 8f));
        Block(new Vector3(0f, 4f, 8f), new Vector3(6f, 0.5f, 12f));
        Assert.That(senses.CanSee(player), Is.False, "A roof hides the player.");
        yield return null;
    }

    [UnityTest]
    public IEnumerator SpottingDuringAPassStartsAChaseThatFliesOverWallsAndPausesAfterEachHit()
    {
        profile.firstPassDelaySeconds = new Vector2(30f, 30f);
        SpawnPlayers();
        Place(localPlayer, new Vector3(0f, 0f, 12f));
        ManananggalBrain brain = CreateManananggal(new Vector3(0f, 0f, -12f));
        GameObject wall = Block(new Vector3(0f, 3f, 0f), new Vector3(30f, 6f, 1f));
        Bounds wallBounds = wall.GetComponent<Collider>().bounds;
        var health = localPlayer.GetComponent<PlayerHealth>();
        int startHealth = health.CurrentHealth;
        yield return WaitUntil(() => brain.CurrentState == brain.Hunting, 1f, "It did not start hunting.");

        brain.SetHidden(false);
        brain.Flight.Warp(new Vector3(0f, 1f, -12f));
        brain.StartChase(localPlayer.GetComponent<PlayerPersistence>());
        Assert.That(brain.StateName, Is.EqualTo("Chase"));
        Assert.That(brain.ChaseTarget, Is.EqualTo(localPlayer.GetComponent<NetworkIdentity>()));

        float highest = float.MinValue;
        float timeout = Time.realtimeSinceStartup + 8f;
        while (brain.Chase.HitCount == 0 && Time.realtimeSinceStartup < timeout)
        {
            Vector3 center = brain.transform.position + Vector3.up * brain.Flight.BodyCenterHeight;
            Assert.That(wallBounds.Contains(center), Is.False, "It flies over the wall, never through it.");
            highest = Mathf.Max(highest, brain.transform.position.y);
            yield return null;
        }
        Assert.That(brain.Chase.HitCount, Is.EqualTo(1), "It never reached the player.");
        Assert.That(highest + brain.Flight.BodyCenterHeight, Is.GreaterThan(wallBounds.max.y), "It climbed over the wall.");
        Assert.That(health.CurrentHealth, Is.EqualTo(startHealth - 2), "One heart per hit.");

        Assert.That(brain.Chase.IsPausedAfterHit, Is.True);
        float hitAt = Time.time;
        Vector3 pausedAt = brain.transform.position;
        yield return new WaitForSeconds(0.5f);
        Assert.That(brain.transform.position, Is.EqualTo(pausedAt), "It stays still after a hit.");
        Assert.That(brain.Chase.HitCount, Is.EqualTo(1));

        yield return WaitUntil(() => brain.Chase.HitCount == 2, 3f, "It did not attack again after the pause.");
        Assert.That(Time.time - hitAt, Is.GreaterThanOrEqualTo(profile.hitPauseSeconds - 0.05f));
        Assert.That(health.CurrentHealth, Is.EqualTo(startHealth - 4));
    }

    [UnityTest]
    public IEnumerator ChaseEndsAfterItsDurationThenItLeavesAndHuntsAgain()
    {
        profile.firstPassDelaySeconds = new Vector2(30f, 30f);
        profile.chaseDurationSeconds = 0.6f;
        profile.contactDamage = 0;
        SpawnPlayers();
        Place(localPlayer, new Vector3(0f, 0f, 20f));
        ManananggalBrain brain = CreateManananggal(new Vector3(0f, 0f, -10f));
        yield return WaitUntil(() => brain.CurrentState == brain.Hunting, 1f, "It did not start hunting.");

        brain.SetHidden(false);
        brain.Flight.Warp(new Vector3(0f, 2f, 0f));
        brain.StartChase(localPlayer.GetComponent<PlayerPersistence>());
        yield return WaitUntil(() => brain.StateName == "Leaving", 2f, "The chase did not time out.");
        Assert.That(brain.ChaseTarget, Is.Null, "It forgets its victim.");
        Assert.That(brain.IsHidden, Is.False, "It visibly flies away.");
        Assert.That(brain.Leaving.ExitPoint.y, Is.EqualTo(10f).Within(0.01f), "It climbs back to hunting altitude.");

        yield return WaitUntil(() => brain.StateName == "Hunting", 5f, "It never left.");
        Assert.That(brain.IsHidden, Is.True);
        Assert.That(brain.Hunting.TimeRemaining, Is.InRange(14.9f, 40f), "It waits the respawn delay before hunting again.");
    }

    [UnityTest]
    public IEnumerator PlayersSeenDuringAPassAreChased()
    {
        SpawnPlayers();
        Place(localPlayer, new Vector3(0f, 0f, -30f));
        ManananggalBrain brain = CreateManananggal(new Vector3(0f, 0f, -30f));
        yield return WaitUntil(() => brain.StateName == "Chase", 5f, "A player under the pass was not spotted.");
        Assert.That(brain.ChaseTarget, Is.EqualTo(localPlayer.GetComponent<NetworkIdentity>()));
    }

    private void SpawnPlayers()
    {
        Assert.That(NetworkServer.active || NetworkClient.active, Is.False, "Run these tests without an active NetworkManager.");
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPath);
        transportObject = new GameObject("Manananggal Test Transport");
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
        ground.name = "Manananggal Test Ground";
        ground.transform.position = new Vector3(0f, -0.5f, 0f);
        ground.transform.localScale = new Vector3(100f, 1f, 100f);
        Physics.SyncTransforms();

        localPlayer = Object.Instantiate(prefab, new Vector3(40f, 0f, 40f), Quaternion.identity);
        remotePlayer = Object.Instantiate(prefab, new Vector3(-40f, 0f, 40f), Quaternion.identity);
        Assert.That(NetworkServer.AddPlayerForConnection(NetworkServer.localConnection, localPlayer), Is.True);
        NetworkServer.Spawn(remotePlayer);
    }

    private ManananggalBrain CreateManananggal(Vector3 position, bool enableBrain = true)
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ManananggalPath);
        GameObject enemy = Object.Instantiate(prefab, position, Quaternion.identity);
        created.Add(enemy);
        var brain = enemy.GetComponent<ManananggalBrain>();
        var settings = new SerializedObject(brain);
        settings.FindProperty("profile").objectReferenceValue = profile;
        settings.ApplyModifiedPropertiesWithoutUndo();
        brain.ApplyVision();
        brain.AssignHomeArea(new Vector3(position.x, 0f, position.z), new Vector2(6f, 6f));
        brain.enabled = enableBrain;
        NetworkServer.Spawn(enemy);
        Physics.SyncTransforms();
        return brain;
    }

    private GameObject Block(Vector3 position, Vector3 size)
    {
        GameObject block = GameObject.CreatePrimitive(PrimitiveType.Cube);
        block.name = "Test Wall";
        block.transform.position = position;
        block.transform.localScale = size;
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
