#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Mirror;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem;
using UnityEngine.TestTools;

public sealed class SantelmoTests : InputTestFixture
{
    private const string PlayerPath = "Assets/PREFABS/NetworkPlayer.prefab";
    private const string SantelmoPath = "Assets/PREFABS/Enemies/Santelmo.prefab";

    private readonly List<GameObject> created = new List<GameObject>();
    private GameObject transportObject;
    private GameObject localPlayer;
    private GameObject remotePlayer;
    private GameObject ground;
    private SantelmoProfile profile;
    private NavMeshDataInstance navMesh;
    private Transport previousTransport;
    private bool previousListen;

    public override void Setup()
    {
        base.Setup();
        previousTransport = Transport.active;
        previousListen = NetworkServer.listen;
        NoiseSystem.ClearPulses();
        profile = ScriptableObject.CreateInstance<SantelmoProfile>();
        profile.wanderSpeed = 3f;
        profile.wanderPauseSeconds = 0.2f;
        profile.gazeSeconds = 1f;
        profile.lostSightGraceSeconds = 0.2f;
        profile.escapeCooldownSeconds = 30f;
        profile.tranceCooldownSeconds = 30f;
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
        foreach (GameObject pocket in Object.FindObjectsByType<GameObject>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (pocket != null && pocket.transform.parent == null && pocket.name.StartsWith("Santelmo Pocket"))
                Object.DestroyImmediate(pocket);
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
    public void PrefabGlowsBillboardsAndCarriesTheTranceChaser()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(SantelmoPath);
        Assert.That(prefab.GetComponent<SantelmoBrain>().Profile, Is.Not.Null);
        Assert.That(prefab.GetComponentInChildren<Light>(true), Is.Not.Null, "The orb glows.");
        Assert.That(prefab.GetComponentInChildren<FireLightFlicker>(true), Is.Not.Null);
        Assert.That(prefab.GetComponentInChildren<ParticleSystem>(true), Is.Not.Null, "A fire trail follows it.");
        var trance = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPath).GetComponent<PlayerTrance>();
        Assert.That(trance, Is.Not.Null);
        Assert.That(new SerializedObject(trance).FindProperty("pocketChaserPrefab").objectReferenceValue, Is.Not.Null);
    }

    [UnityTest]
    public IEnumerator GazeBlursTheVictimAndEntrancesThemAfterGazeSeconds()
    {
        SpawnArena();
        ConfigureTrance(localPlayer, chaserSpeed: 0f);
        Place(localPlayer, new Vector3(0f, 0f, 6f), 180f);
        Place(remotePlayer, new Vector3(-40f, 0f, -40f));
        SantelmoBrain brain = CreateSantelmo(Vector3.zero);
        var trance = localPlayer.GetComponent<PlayerTrance>();
        var effects = localPlayer.GetComponentInChildren<PlayerCameraEffects>(true);

        yield return WaitUntil(() => brain.CurrentState == brain.Gazing, 3f, "Spotting a player makes it stop and stare.");
        Assert.That(brain.GazeTarget, Is.EqualTo(localPlayer.GetComponent<NetworkIdentity>()));
        yield return WaitUntil(() => trance.Gaze > 0.3f, 2f, "The gaze builds while the player stays in sight.");
        Assert.That(effects.TranceBlur, Is.GreaterThan(0f), "The victim's view blurs.");
        Assert.That(effects.ScreenFade.a, Is.GreaterThan(0f), "... and darkens.");

        yield return WaitUntil(() => trance.InTrance, 2f, "A full gaze entrances the player.");
        Assert.That(brain.CurrentState, Is.EqualTo(brain.Wandering), "It drifts on after taking a victim.");
        Assert.That(brain.IsIgnoring(localPlayer.GetComponent<PlayerPersistence>()), Is.True);
        Assert.That(trance.PocketActive, Is.True, "The owner is pulled into the pocket street.");
        Assert.That(trance.PocketRoot, Is.Not.Null);
        Assert.That(Vector3.Distance(localPlayer.transform.position, new Vector3(0f, 0f, 6f)), Is.GreaterThan(1000f));
        Assert.That(trance.PocketChaser, Is.Not.Null, "A Santelmo chases them there.");
        Assert.That(localPlayer.GetComponent<PlayerCorpse>().Corpse, Is.Not.Null, "Others see a body where they stood.");
        Assert.That(localPlayer.GetComponent<PlayerHealth>().IsDead, Is.False);
    }

    [UnityTest]
    public IEnumerator EscapingTheGazeEarlyLetsThePlayerGoAndItIgnoresThemForAWhile()
    {
        profile.gazeSeconds = 5f;
        SpawnArena();
        Place(localPlayer, new Vector3(0f, 0f, 6f), 180f);
        Place(remotePlayer, new Vector3(-40f, 0f, -40f));
        SantelmoBrain brain = CreateSantelmo(Vector3.zero);
        var trance = localPlayer.GetComponent<PlayerTrance>();
        var persistence = localPlayer.GetComponent<PlayerPersistence>();

        yield return WaitUntil(() => brain.CurrentState == brain.Gazing && trance.Gaze > 0.05f, 3f, "The gaze starts.");
        Place(localPlayer, new Vector3(40f, 0f, 40f));
        yield return WaitUntil(() => brain.CurrentState == brain.Wandering, 2f, "Leaving its sight breaks the gaze.");
        Assert.That(trance.InTrance, Is.False);
        Assert.That(trance.Gaze, Is.EqualTo(0f), "The blur clears.");
        Assert.That(brain.IsIgnoring(persistence), Is.True, "It looks for its next victim instead.");

        brain.enabled = false;
        Place(localPlayer, brain.transform.position + brain.transform.forward * 5f);
        Assert.That(brain.Senses.CanSee(persistence), Is.True);
        Assert.That(brain.TrySpotPlayer(out _), Is.False, "Escaped players are ignored during the cooldown.");
    }

    [UnityTest]
    public IEnumerator TrancedPlayersLookDeadAndAreIgnoredByEnemies()
    {
        SpawnArena();
        Place(localPlayer, new Vector3(40f, 0f, 40f));
        Place(remotePlayer, new Vector3(0f, 0f, 6f), 180f);
        SantelmoBrain brain = CreateSantelmo(Vector3.zero, enableBrain: false);
        var persistence = remotePlayer.GetComponent<PlayerPersistence>();
        var trance = remotePlayer.GetComponent<PlayerTrance>();
        Assert.That(brain.Senses.FindVisiblePlayer(), Is.EqualTo(persistence));

        Assert.That(trance.ServerStartTrance(brain.netIdentity), Is.True);
        Assert.That(trance.ServerStartTrance(brain.netIdentity), Is.False, "Already entranced.");
        yield return null;
        yield return null;
        Assert.That(PlayerTrance.IsInTrance(persistence), Is.True);
        Assert.That(trance.PocketActive, Is.False, "Only the owner enters the pocket.");
        Assert.That(brain.Senses.FindVisiblePlayer(), Is.Null, "Enemies ignore entranced players.");
        Assert.That(remotePlayer.GetComponent<PlayerNoise>().IsMakingNoise, Is.False);
        var worldSprites = remotePlayer.GetComponent<PlayerVision>().WorldSprites;
        Assert.That(worldSprites.forceRenderingOff, Is.True, "The standing body is hidden ...");
        Assert.That(remotePlayer.GetComponent<PlayerCorpse>().Corpse, Is.Not.Null, "... and a corpse shows instead.");

        trance.ServerEndTrance(false);
        yield return null;
        yield return null;
        Assert.That(PlayerTrance.IsInTrance(persistence), Is.False);
        Assert.That(remotePlayer.GetComponent<PlayerCorpse>().Corpse, Is.Null, "Surviving removes the corpse.");
        Assert.That(worldSprites.forceRenderingOff, Is.False);
        Assert.That(brain.Senses.FindVisiblePlayer(), Is.EqualTo(persistence));
    }

    [UnityTest]
    public IEnumerator BeingCaughtInThePocketKillsThePlayerWithTheSantelmoAsKiller()
    {
        SpawnArena();
        ConfigureTrance(localPlayer, chaserSpeed: 30f, startDistance: 6f);
        Place(localPlayer, new Vector3(5f, 0f, 5f));
        Place(remotePlayer, new Vector3(-40f, 0f, -40f));
        SantelmoBrain brain = CreateSantelmo(new Vector3(30f, 0f, -30f), enableBrain: false);
        var trance = localPlayer.GetComponent<PlayerTrance>();
        var health = localPlayer.GetComponent<PlayerHealth>();
        Vector3 body = localPlayer.transform.position;

        Assert.That(trance.ServerStartTrance(brain.netIdentity), Is.True);
        yield return WaitUntil(() => trance.PocketActive, 1f, "The owner enters the pocket.");
        yield return WaitUntil(() => health.IsDead, 3f, "The pocket Santelmo catches and kills them.");
        Assert.That(health.KillerAssetId, Is.EqualTo(brain.netIdentity.assetId), "The death screen shows the Santelmo.");
        Assert.That(trance.InTrance, Is.False);
        Assert.That(trance.PocketActive, Is.False);
        Assert.That(trance.PocketRoot == null, Is.True, "The pocket is torn down.");
        Assert.That(Vector3.Distance(localPlayer.transform.position, body), Is.LessThan(0.5f), "The body stays where it fell.");
        Assert.That(localPlayer.GetComponent<PlayerCorpse>().Corpse, Is.Not.Null, "The dead body remains.");
    }

    [UnityTest]
    public IEnumerator SurvivingThePocketReturnsThePlayerToTheirBodyThroughWhite()
    {
        SpawnArena();
        ConfigureTrance(localPlayer, chaserSpeed: 0f, survive: 1.5f, finalPhase: 0.75f);
        Place(localPlayer, new Vector3(5f, 0f, 5f));
        Place(remotePlayer, new Vector3(-40f, 0f, -40f));
        SantelmoBrain brain = CreateSantelmo(new Vector3(30f, 0f, -30f), enableBrain: false);
        var trance = localPlayer.GetComponent<PlayerTrance>();
        var effects = localPlayer.GetComponentInChildren<PlayerCameraEffects>(true);
        Vector3 body = localPlayer.transform.position;

        Assert.That(trance.ServerStartTrance(brain.netIdentity), Is.True);
        yield return WaitUntil(() => trance.PocketActive, 1f, "The owner enters the pocket.");
        yield return null;
        Assert.That(effects.TranceTint.a, Is.GreaterThan(0f), "The pocket is tinted like an inferno.");
        Assert.That(effects.FogAmbientOverride.a, Is.GreaterThan(0f));
        yield return WaitUntil(() => effects.ScreenFade.r > 0.9f && effects.ScreenFade.a > 0.3f, 2f, "The screen whitens near the end.");
        yield return WaitUntil(() => !trance.InTrance, 3f, "Surviving ends the trance.");
        yield return null;
        Assert.That(localPlayer.GetComponent<PlayerHealth>().IsDead, Is.False);
        Assert.That(Vector3.Distance(localPlayer.transform.position, body), Is.LessThan(0.5f), "Back in their body.");
        Assert.That(trance.PocketRoot == null, Is.True);
        Assert.That(localPlayer.GetComponent<PlayerCorpse>().Corpse, Is.Null);
        yield return WaitUntil(() => effects.ScreenFade.a <= 0f && effects.TranceTint.a <= 0f, 3f, "The white fades back to the world.");
    }

    [UnityTest]
    public IEnumerator TheServerEndsATranceThatOutlastsItsTimeout()
    {
        SpawnArena();
        ConfigureTrance(localPlayer, chaserSpeed: 0f, timeout: 0.5f);
        Place(localPlayer, new Vector3(5f, 0f, 5f));
        Place(remotePlayer, new Vector3(-40f, 0f, -40f));
        SantelmoBrain brain = CreateSantelmo(new Vector3(30f, 0f, -30f), enableBrain: false);
        var trance = localPlayer.GetComponent<PlayerTrance>();
        Assert.That(trance.ServerStartTrance(brain.netIdentity), Is.True);
        yield return WaitUntil(() => !trance.InTrance, 2f, "Lost results never strand a player.");
        Assert.That(localPlayer.GetComponent<PlayerHealth>().IsDead, Is.False);
    }

    [UnityTest]
    public IEnumerator PocketIsAStraightLineOfRepeatedMapBlocksWithoutObstaclesOrNetworkObjects()
    {
        MapManager map = BuildMap();
        yield return null;
        SpawnPlayers();
        ConfigureTrance(localPlayer, chaserSpeed: 0f);
        var trance = localPlayer.GetComponent<PlayerTrance>();
        Assert.That(trance.ServerStartTrance(null), Is.True);
        yield return WaitUntil(() => trance.PocketActive, 1f, "The owner enters the pocket.");

        StreetBlock[] copies = trance.PocketRoot.GetComponentsInChildren<StreetBlock>(true);
        Assert.That(copies.Length, Is.EqualTo(5), "Five copies of one map block form the line.");
        Assert.That(copies.Select(copy => copy.name.Substring(0, copy.name.IndexOf(" (Pocket"))).Distinct().Count(), Is.EqualTo(1), "Every segment is the same block.");
        Assert.That(copies[0].name, Does.StartWith("StreetBlock_3Entrances_1"), "Only straight-through blocks form the line.");
        Assert.That(trance.PocketRoot.GetComponentsInChildren<EntranceObstacle>(true), Is.Empty);
        Assert.That(trance.PocketRoot.GetComponentsInChildren<NetworkIdentity>(true), Is.Empty);
        Vector3[] centres = copies.Select(copy => copy.transform.position).OrderBy(p => p.x + p.z).ToArray();
        Vector3 step = centres[1] - centres[0];
        for (int i = 2; i < centres.Length; i++)
            Assert.That(Vector3.Distance(centres[i] - centres[i - 1], step), Is.LessThan(0.01f), "Blocks repeat in a straight line.");
        Assert.That(Physics.Raycast(localPlayer.transform.position + Vector3.up, Vector3.down, out _, 3f), Is.True, "The player stands on the pocket street.");
        Assert.That(map.IsBuilt, Is.True);

        // Walking one segment ahead wraps them back by a segment (it seems endless).
        Vector3 before = localPlayer.transform.position;
        Place(localPlayer, before + step * 0.75f);
        yield return null;
        yield return null;
        Assert.That(Vector3.Distance(localPlayer.transform.position, before), Is.LessThan(step.magnitude * 0.5f));
        trance.ServerEndTrance(false);
        yield return null;
    }

    private MapManager BuildMap()
    {
        StreetBlock Block(string name) =>
            AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/PREFABS/StreetBlocks/{name}.prefab").GetComponent<StreetBlock>();
        var root = new GameObject("Santelmo Test Map");
        created.Add(root);
        root.SetActive(false);
        var manager = root.AddComponent<MapManager>();
        var so = new SerializedObject(manager);
        void Fill(string property, params StreetBlock[] blocks)
        {
            SerializedProperty list = so.FindProperty(property);
            list.arraySize = blocks.Length;
            for (int i = 0; i < blocks.Length; i++)
                list.GetArrayElementAtIndex(i).objectReferenceValue = blocks[i];
        }
        // The L-bend is never used for the pocket; the T-junction runs straight West-East.
        Fill("connectorBlocks", Block("StreetBlock_2Entrances_1"), Block("StreetBlock_3Entrances_1"));
        Fill("endCapBlocks", Block("EndBlockStreetBlock_1"));
        Fill("spawnBlocks", Block("SpawnBlockStreetBlock_1"));
        so.FindProperty("connectorBlockCount").intValue = 2;
        so.FindProperty("buildOnStart").boolValue = false;
        so.ApplyModifiedPropertiesWithoutUndo();
        root.SetActive(true);
        Assert.That(manager.Build(3), Is.True);
        return manager;
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
        transportObject = new GameObject("Santelmo Test Transport");
        Transport.active = transportObject.AddComponent<TelepathyTransport>();
        NetworkServer.listen = false;
        NetworkServer.Listen(4);
        NetworkClient.RegisterPrefab(prefab);
        NetworkClient.RegisterPrefab(AssetDatabase.LoadAssetAtPath<GameObject>(SantelmoPath));
        NetworkClient.ConnectHost();
        NetworkServer.localConnection.isAuthenticated = true;
        NetworkClient.connection.isAuthenticated = true;
        HostMode.InvokeOnConnected();
        Assert.That(NetworkClient.Ready(), Is.True);

        ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
        ground.name = "Santelmo Test Ground";
        ground.transform.position = new Vector3(0f, -0.5f, 0f);
        ground.transform.localScale = new Vector3(100f, 1f, 100f);
        Physics.SyncTransforms();

        localPlayer = Object.Instantiate(prefab, new Vector3(40f, 0f, 40f), Quaternion.identity);
        remotePlayer = Object.Instantiate(prefab, new Vector3(-40f, 0f, 40f), Quaternion.identity);
        Assert.That(NetworkServer.AddPlayerForConnection(NetworkServer.localConnection, localPlayer), Is.True);
        NetworkServer.Spawn(remotePlayer);
    }

    private static void ConfigureTrance(GameObject player, float chaserSpeed, float survive = 60f, float finalPhase = 10f,
        float timeout = 80f, float startDistance = 16f)
    {
        var settings = new SerializedObject(player.GetComponent<PlayerTrance>());
        settings.FindProperty("chaserSpeed").floatValue = chaserSpeed;
        settings.FindProperty("chaserSlowSpeed").floatValue = chaserSpeed;
        settings.FindProperty("chaserStartDistance").floatValue = startDistance;
        settings.FindProperty("surviveSeconds").floatValue = survive;
        settings.FindProperty("finalPhaseSeconds").floatValue = finalPhase;
        settings.FindProperty("serverMinimumSurviveSeconds").floatValue = survive * 0.9f;
        settings.FindProperty("serverTimeoutSeconds").floatValue = timeout;
        settings.FindProperty("fadeInSeconds").floatValue = 0.2f;
        settings.FindProperty("returnFadeSeconds").floatValue = 0.5f;
        settings.ApplyModifiedPropertiesWithoutUndo();
    }

    private SantelmoBrain CreateSantelmo(Vector3 position, bool enableBrain = true)
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(SantelmoPath);
        GameObject enemy = Object.Instantiate(prefab, position, Quaternion.identity);
        created.Add(enemy);
        var brain = enemy.GetComponent<SantelmoBrain>();
        var settings = new SerializedObject(brain);
        settings.FindProperty("profile").objectReferenceValue = profile;
        settings.ApplyModifiedPropertiesWithoutUndo();
        brain.ApplyVision();
        brain.AssignHomeArea(position, new Vector2(4f, 4f));
        brain.enabled = enableBrain;
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
