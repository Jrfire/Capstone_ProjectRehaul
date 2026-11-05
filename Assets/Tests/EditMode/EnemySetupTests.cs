using System.Collections.Generic;
using System.Linq;
using Mirror;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

public sealed class EnemySetupTests
{
    private const string TikbalangPath = "Assets/PREFABS/Enemies/Tikbalang.prefab";
    private const string ProfilePath = "Assets/PREFABS/Enemies/Profiles/Tikbalang_Default.asset";
    private const string SpritesPath = "Assets/SPRITES/Enemies/Tikbalang_Sprites.asset";

    [Test]
    public void TikbalangPrefabIsAServerDrivenNetworkedEnemy()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(TikbalangPath);
        Assert.That(prefab, Is.Not.Null);
        var identity = prefab.GetComponent<NetworkIdentity>();
        Assert.That(identity, Is.Not.Null);
        Assert.That(identity.assetId, Is.Not.EqualTo(0u), "Mirror needs a stable asset id to spawn it on clients.");
        Assert.That(prefab.GetComponent<NetworkTransformReliable>(), Is.Not.Null);

        var agent = prefab.GetComponent<NavMeshAgent>();
        Assert.That(agent, Is.Not.Null);
        Assert.That(agent.enabled, Is.False, "Only the server enables the agent once it stands on the NavMesh.");
        Assert.That(prefab.GetComponent<CapsuleCollider>(), Is.Not.Null);

        var brain = prefab.GetComponent<TikbalangBrain>();
        Assert.That(brain, Is.Not.Null);
        Assert.That(brain.Profile, Is.EqualTo(AssetDatabase.LoadAssetAtPath<TikbalangProfile>(ProfilePath)));
        Assert.That(prefab.GetComponent<EnemyMotor>(), Is.Not.Null);
        Assert.That(prefab.GetComponent<EnemySenses>(), Is.Not.Null);

        var presentation = prefab.GetComponent<EnemyPresentation>();
        Assert.That(presentation.Sprites, Is.EqualTo(AssetDatabase.LoadAssetAtPath<EnemySpriteDefinition>(SpritesPath)));
        Assert.That(presentation.WorldSprites, Is.Not.Null);
        Assert.That(presentation.WorldSprites.transform.parent, Is.EqualTo(prefab.transform));
        Assert.That(prefab.GetComponent<EnemyFearSource>().visibilityRenderer, Is.EqualTo(presentation.WorldSprites));
    }

    [Test]
    public void TikbalangSpriteIsSolidAndHasAGroundShadow()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(TikbalangPath);
        var presentation = prefab.GetComponent<EnemyPresentation>();
        Material spriteMaterial = presentation.WorldSprites.sharedMaterial;
        Assert.That(spriteMaterial, Is.Not.Null);
        Assert.That(spriteMaterial.shader.name, Is.EqualTo("Capstone/Solid Sprite"),
            "Enemy sprites must write depth so Night Fog does not paint the scene behind them over the sprite.");
        Assert.That(spriteMaterial.renderQueue, Is.LessThanOrEqualTo(2500), "Drawn with opaques so the depth copy includes it.");
        Assert.That(spriteMaterial.FindPass("DepthOnly"), Is.GreaterThanOrEqualTo(0));

        var shadow = prefab.GetComponentInChildren<BlobShadow>(true);
        Assert.That(shadow, Is.Not.Null);
        Assert.That(shadow.transform.parent, Is.EqualTo(prefab.transform));
        Assert.That(shadow.Follow, Is.EqualTo(presentation.WorldSprites.transform), "The shadow follows the snapped sprite.");
        Assert.That(shadow.GetComponent<Collider>(), Is.Null, "The shadow must not block vision, flashlight or movement.");
        Assert.That(shadow.GetComponent<MeshFilter>().sharedMesh, Is.Not.Null);
        var renderer = shadow.GetComponent<MeshRenderer>();
        Assert.That(renderer.sharedMaterial.shader.name, Is.EqualTo("Capstone/Blob Shadow"));
        Assert.That(renderer.shadowCastingMode, Is.EqualTo(UnityEngine.Rendering.ShadowCastingMode.Off));
        Assert.That(prefab.GetComponentsInChildren<Collider>(true).Length, Is.EqualTo(1), "Only the body capsule.");
    }

    [Test]
    public void TikbalangSpriteFadesWithDistanceSoItCannotBeSeenThroughTheFog()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(TikbalangPath);
        var fade = prefab.GetComponent<DistanceFadeSprite>();
        Assert.That(fade, Is.Not.Null);
        var so = new SerializedObject(fade);
        SerializedProperty renderers = so.FindProperty("renderers");
        Assert.That(renderers.arraySize, Is.EqualTo(1));
        Assert.That(renderers.GetArrayElementAtIndex(0).objectReferenceValue,
            Is.EqualTo(prefab.GetComponent<EnemyPresentation>().WorldSprites));
        Assert.That(fade.SolidDistance, Is.InRange(10f, 15f));
        Assert.That(fade.FadedDistance, Is.GreaterThan(fade.SolidDistance));
        Assert.That(fade.MinimumOpacity, Is.InRange(0f, 0.25f), "Far sprites are nearly invisible.");
        Assert.That(fade.DarkestDistance, Is.GreaterThan(fade.DarkenStartDistance));
        Assert.That(fade.MinimumBrightness, Is.LessThan(0.5f), "Far sprites are darkened.");
    }

    [Test]
    public void DistanceFadeOpacityIsSolidNearbyAndNearlyInvisibleFarAway()
    {
        Assert.That(DistanceFadeSprite.EvaluateOpacity(0f, 12f, 30f, 0.01f), Is.EqualTo(1f));
        Assert.That(DistanceFadeSprite.EvaluateOpacity(12f, 12f, 30f, 0.01f), Is.EqualTo(1f));
        float middle = DistanceFadeSprite.EvaluateOpacity(21f, 12f, 30f, 0.01f);
        Assert.That(middle, Is.EqualTo(0.505f).Within(0.001f));
        Assert.That(DistanceFadeSprite.EvaluateOpacity(20f, 12f, 30f, 0.01f), Is.GreaterThan(middle));
        Assert.That(DistanceFadeSprite.EvaluateOpacity(30f, 12f, 30f, 0.01f), Is.EqualTo(0.01f).Within(0.0001f));
        Assert.That(DistanceFadeSprite.EvaluateOpacity(100f, 12f, 30f, 0.01f), Is.EqualTo(0.01f).Within(0.0001f));
    }

    [Test]
    public void DefaultProfileMatchesTheDesign()
    {
        var profile = AssetDatabase.LoadAssetAtPath<TikbalangProfile>(ProfilePath);
        Assert.That(profile, Is.Not.Null);
        Assert.That(profile.chaseSpeed, Is.GreaterThan(profile.walkSpeed));
        Assert.That(profile.visionAngle, Is.LessThan(60f), "Narrower than the player's view.");
        Assert.That(profile.searchingVisionAngle, Is.GreaterThan(profile.visionAngle));
        Assert.That(profile.hearingRadius, Is.GreaterThan(0f));
        Assert.That(profile.idleWaitSeconds, Is.EqualTo(10f));
        Assert.That(profile.chaseDurationSeconds, Is.EqualTo(60f));
        Assert.That(profile.contactDamage, Is.EqualTo(2));
        Assert.That(profile.hitPauseSeconds, Is.EqualTo(1.5f));
        Assert.That(profile.teleportDistance, Is.EqualTo(35f));
    }

    [Test]
    public void PlayersEmitARunningNoiseDome()
    {
        GameObject player = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/PREFABS/NetworkPlayer.prefab");
        var noise = player.GetComponent<PlayerNoise>();
        Assert.That(noise, Is.Not.Null);
        Assert.That(noise.NoiseRadius, Is.EqualTo(6f));
        Assert.That(player.GetComponent<NetworkIdentity>().assetId, Is.Not.EqualTo(0u));
    }

    [Test]
    public void TikbalangIsRegisteredAndSpawnedInTheStreetScene()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(TikbalangPath);
        Scene sample = EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity", OpenSceneMode.Additive);
        Scene streets = EditorSceneManager.OpenScene("Assets/Scenes/StreetsTest.unity", OpenSceneMode.Additive);
        try
        {
            NetworkManager manager = sample.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<NetworkManager>(true)).Single();
            Assert.That(manager.spawnPrefabs, Does.Contain(prefab));

            EnemySpawner spawner = streets.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<EnemySpawner>(true)).Single();
            Assert.That(spawner.Enemies.Count, Is.GreaterThanOrEqualTo(1));
            Assert.That(spawner.Enemies.Any(entry => entry.prefab != null && entry.prefab.gameObject == prefab && entry.count == 1), Is.True);
            var map = new SerializedObject(spawner).FindProperty("map").objectReferenceValue as MapManager;
            Assert.That(map, Is.Not.Null);
            Assert.That(map.gameObject.scene, Is.EqualTo(streets));
        }
        finally
        {
            EditorSceneManager.CloseScene(streets, true);
            EditorSceneManager.CloseScene(sample, true);
        }
    }

    private const string ManananggalPath = "Assets/PREFABS/Enemies/Manananggal.prefab";

    [Test]
    public void ManananggalReusesTheEnemyComponentsButFliesWithItsOwnBrain()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ManananggalPath);
        Assert.That(prefab, Is.Not.Null);
        var identity = prefab.GetComponent<NetworkIdentity>();
        Assert.That(identity.assetId, Is.Not.EqualTo(0u));
        Assert.That(identity.assetId, Is.Not.EqualTo(AssetDatabase.LoadAssetAtPath<GameObject>(TikbalangPath).GetComponent<NetworkIdentity>().assetId));
        Assert.That(prefab.GetComponent<NetworkTransformReliable>(), Is.Not.Null);
        Assert.That(prefab.GetComponent<NavMeshAgent>(), Is.Null, "It flies; it is not limited to the NavMesh.");
        Assert.That(prefab.GetComponent<EnemyMotor>(), Is.Null);
        Assert.That(prefab.GetComponent<EnemyFlightMotor>(), Is.Not.Null);
        Assert.That(prefab.GetComponent<TikbalangBrain>(), Is.Null);

        var brain = prefab.GetComponent<ManananggalBrain>();
        Assert.That(brain, Is.Not.Null);
        Assert.That(brain.RoamsWholeMap, Is.True);
        Assert.That(brain.Profile, Is.EqualTo(AssetDatabase.LoadAssetAtPath<ManananggalProfile>("Assets/PREFABS/Enemies/Profiles/Manananggal_Default.asset")));
        Assert.That(prefab.GetComponent<EnemySenses>(), Is.Not.Null);

        var presentation = prefab.GetComponent<EnemyPresentation>();
        Assert.That(presentation.Sprites, Is.EqualTo(AssetDatabase.LoadAssetAtPath<EnemySpriteDefinition>("Assets/SPRITES/Enemies/Manananggal_Sprites.asset")));
        Assert.That(presentation.Sprites.GetAnimation(EnemyAnimationState.Idle).IsReady, Is.True);
        Assert.That(presentation.Sprites.GetAnimation(EnemyAnimationState.Moving).IsReady, Is.True);
        Assert.That(presentation.WorldSprites.transform.parent, Is.EqualTo(prefab.transform));
        Assert.That(presentation.WorldSprites.sharedMaterial.shader.name, Is.EqualTo("Capstone/Solid Sprite"));
        Assert.That(presentation.WorldSprites.GetComponent<ReducedPixelation>(), Is.Not.Null);
        Assert.That(prefab.GetComponent<EnemyFearSource>().visibilityRenderer, Is.EqualTo(presentation.WorldSprites));
        Assert.That(prefab.GetComponent<DistanceFadeSprite>(), Is.Not.Null);
        Assert.That(prefab.GetComponentInChildren<BlobShadow>(true).Follow, Is.EqualTo(presentation.WorldSprites.transform));
        Assert.That(prefab.GetComponent<EnemyJumpscare>().FrontView, Is.Not.Null);
        Assert.That(prefab.GetComponent<EnemyLoadingMiniature>().IsReady, Is.True);
        Assert.That(prefab.GetComponentsInChildren<Collider>(true).Length, Is.EqualTo(1), "Only the body capsule.");
    }

    [Test]
    public void ManananggalPassesComeFromAnyDirectionOnParallelLanesAcrossTheMap()
    {
        var bounds = new Bounds(new Vector3(10f, 0f, -5f), new Vector3(60f, 0f, 30f));
        foreach (float yaw in new[] { 0f, 37f, 90f, 135f, 200f, 270f, 333f })
        {
            Vector3 heading = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
            var lanes = new List<Vector3>();
            foreach (float lane in new[] { 0f, 0.25f, 0.5f, 1f })
            {
                ManananggalBrain.PlanLane(bounds, yaw, lane, 14f, 12f, out Vector3 start, out Vector3 end);
                Assert.That(start.y, Is.EqualTo(14f));
                Assert.That(end.y, Is.EqualTo(14f));
                Assert.That(Vector3.Angle(end - start, heading), Is.LessThan(0.01f), $"Yaw {yaw} flies straight along its heading.");
                Assert.That(DistanceOutside(bounds, start) >= 11.9f && DistanceOutside(bounds, end) >= 11.9f, Is.True,
                    $"Yaw {yaw} lane {lane}: it appears and despawns beyond the map's edge margin.");
                lanes.Add(start);
            }
            Vector3 side = Vector3.Cross(Vector3.up, heading);
            float wide = Mathf.Abs(side.x) * bounds.extents.x + Mathf.Abs(side.z) * bounds.extents.z;
            Assert.That(Vector3.Dot(lanes[3] - lanes[0], side), Is.EqualTo(2f * wide).Within(0.01f),
                "Lanes 0 and 1 run parallel along opposite edges of the map.");
            Assert.That(Vector3.Dot(lanes[3] - lanes[0], heading), Is.EqualTo(0f).Within(0.01f));
        }
    }

    private static float DistanceOutside(Bounds bounds, Vector3 point)
    {
        float dx = Mathf.Max(0f, Mathf.Max(bounds.min.x - point.x, point.x - bounds.max.x));
        float dz = Mathf.Max(0f, Mathf.Max(bounds.min.z - point.z, point.z - bounds.max.z));
        return Mathf.Sqrt(dx * dx + dz * dz);
    }

    [Test]
    public void ManananggalDefaultProfileAndRegistration()
    {
        var profile = AssetDatabase.LoadAssetAtPath<ManananggalProfile>("Assets/PREFABS/Enemies/Profiles/Manananggal_Default.asset");
        Assert.That(profile.respawnDelaySeconds.x, Is.GreaterThan(0f));
        Assert.That(profile.respawnDelaySeconds.y, Is.GreaterThanOrEqualTo(profile.respawnDelaySeconds.x));
        Assert.That(profile.chaseDurationSeconds, Is.EqualTo(20f));
        Assert.That(profile.hitPauseSeconds, Is.EqualTo(2.5f));
        Assert.That(profile.contactDamage, Is.EqualTo(2));
        Assert.That(profile.visionPitch, Is.InRange(20f, 80f), "Its cone looks down and a little forward.");
        Assert.That(profile.huntAltitude, Is.GreaterThan(8f));
        Assert.That(profile.passDirectionStep, Is.EqualTo(0f), "Passes may come from any direction.");

        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ManananggalPath);
        var definition = AssetDatabase.LoadAssetAtPath<EnemyDefinition>("Assets/PREFABS/Enemies/Definitions/Manananggal.asset");
        Assert.That(definition.DisplayName, Is.EqualTo("Manananggal"));
        Assert.That(definition.prefab.gameObject, Is.EqualTo(prefab));

        Scene sample = EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity", OpenSceneMode.Additive);
        try
        {
            NetworkManager manager = sample.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<NetworkManager>(true)).Single();
            Assert.That(manager.spawnPrefabs, Does.Contain(prefab));
        }
        finally
        {
            EditorSceneManager.CloseScene(sample, true);
        }
    }
}
