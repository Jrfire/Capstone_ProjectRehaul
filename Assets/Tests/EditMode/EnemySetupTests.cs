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
            Assert.That(spawner.Enemies.Count, Is.EqualTo(1));
            Assert.That(spawner.Enemies[0].prefab.gameObject, Is.EqualTo(prefab));
            Assert.That(spawner.Enemies[0].count, Is.EqualTo(1));
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
}
