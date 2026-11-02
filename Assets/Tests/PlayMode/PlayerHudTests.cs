#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Mirror;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

public sealed class PlayerHudTests
{
    private readonly List<Object> objects = new List<Object>();
    private Transport previousTransport;
    private bool previousListen;
    private CursorLockMode previousCursorLock;
    private bool previousCursorVisible;
    private PlayerHealth local;
    private PlayerHealth remote;
    private PlayerHud hud;
    private Sprite[] sprites;
    private Scene temporaryScene;

    [SetUp]
    public void Setup()
    {
        Assert.That(NetworkServer.active || NetworkClient.active, Is.False);
        previousTransport = Transport.active;
        previousListen = NetworkServer.listen;
        previousCursorLock = Cursor.lockState;
        previousCursorVisible = Cursor.visible;
        GameObject transport = Track(new GameObject("Health test transport"));
        Transport.active = transport.AddComponent<TelepathyTransport>();
        NetworkServer.listen = false;
        StartNetwork();
        local = Track(Object.Instantiate(PlayerPrefab())).GetComponent<PlayerHealth>();
        remote = Track(Object.Instantiate(PlayerPrefab())).GetComponent<PlayerHealth>();
        Assert.That(NetworkServer.AddPlayerForConnection(NetworkServer.localConnection, local.gameObject), Is.True);
        NetworkServer.Spawn(remote.gameObject);

        hud = Track(Object.Instantiate(HudPrefab())).GetComponent<PlayerHud>();
        Texture2D texture = Track(new Texture2D(3, 1));
        sprites = new Sprite[3];
        for (int index = 0; index < sprites.Length; index++)
            sprites[index] = Track(Sprite.Create(texture, new Rect(index, 0, 1, 1), Vector2.one * 0.5f));
        SerializedObject serialized = new SerializedObject(hud);
        serialized.FindProperty("fullHeart").objectReferenceValue = sprites[0];
        serialized.FindProperty("halfHeart").objectReferenceValue = sprites[1];
        serialized.FindProperty("emptyHeart").objectReferenceValue = sprites[2];
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    [UnityTearDown]
    public IEnumerator TearDown()
    {
        NetworkClient.Disconnect();
        NetworkClient.Shutdown();
        NetworkServer.Shutdown();
        for (int index = objects.Count - 1; index >= 0; index--)
        {
            if (objects[index] != null)
                Object.DestroyImmediate(objects[index]);
        }
        objects.Clear();
        if (temporaryScene.IsValid() && temporaryScene.isLoaded)
            yield return SceneManager.UnloadSceneAsync(temporaryScene);
        Transport.active = previousTransport;
        NetworkServer.listen = previousListen;
        Cursor.lockState = previousCursorLock;
        Cursor.visible = previousCursorVisible;
    }

    [UnityTest]
    public IEnumerator AllSevenHealthValuesMapToThreeOwnerOnlyHearts()
    {
        yield return null;
        yield return null;
        Assert.That(local.CurrentHealth, Is.EqualTo(6));
        for (int health = 6; health >= 0; health--)
        {
            if (health < 6)
                local.TakeDamage(1);
            yield return null;
            yield return null;
            AssertHearts(health);
        }
        local.Heal(5);
        yield return null;
        yield return null;
        AssertHearts(5);
        remote.TakeDamage(6);
        yield return null;
        yield return null;
        Assert.That(remote.CurrentHealth, Is.Zero);
        AssertHearts(5);
        Assert.That(local.GetComponentInChildren<PlayerHud>(true), Is.Null);
        Assert.That(hud.GetComponent<Canvas>().renderMode, Is.EqualTo(RenderMode.ScreenSpaceOverlay));
        Assert.That(hud.GetComponentsInChildren<Image>(true).All(image => !image.raycastTarget), Is.True);
    }

    [UnityTest]
    public IEnumerator StaminaFillTracksTheLocalPlayerAndBlinksBelowThirtyPercent()
    {
        yield return null;
        yield return null;
        PlayerStamina stamina = local.GetComponent<PlayerStamina>();
        PlayerStamina remoteStamina = remote.GetComponent<PlayerStamina>();
        SerializedObject serialized = new SerializedObject(hud);
        GameObject panel = (GameObject)serialized.FindProperty("staminaPanel").objectReferenceValue;
        Image fill = (Image)serialized.FindProperty("staminaFill").objectReferenceValue;
        float baseAlpha = fill.color.a;
        Assert.That(panel.activeSelf, Is.True);
        Assert.That(fill.fillAmount, Is.EqualTo(1f));
        Assert.That(hud.StaminaBlinking, Is.False);

        Assert.That(remoteStamina.TryUseStamina(10f), Is.False, "Only the owner may spend its stamina.");
        Assert.That(stamina.TryUseStamina(50f), Is.True);
        yield return null;
        Assert.That(fill.fillAmount, Is.EqualTo(0.5f).Within(0.001f));
        Assert.That(hud.StaminaBlinking, Is.False);
        Assert.That(fill.color.a, Is.EqualTo(baseAlpha));

        Assert.That(stamina.TryUseStamina(20f), Is.True);
        Image background = panel.GetComponent<Image>();
        float backgroundAlpha = background.color.a;
        yield return null;
        Assert.That(fill.fillAmount, Is.EqualTo(0.3f).Within(0.001f));
        Assert.That(hud.StaminaBlinking, Is.False, "Exactly 30% is not below 30% and must not blink.");
        Assert.That(fill.color.a, Is.EqualTo(baseAlpha));

        Assert.That(stamina.TryUseStamina(1f), Is.True);
        float minimumAlpha = float.MaxValue;
        float maximumAlpha = float.MinValue;
        float until = Time.realtimeSinceStartup + 0.4f;
        while (Time.realtimeSinceStartup < until)
        {
            yield return null;
            Assert.That(hud.StaminaBlinking, Is.True, "Below 30% the fill must blink.");
            Assert.That(hud.StaminaPanelBlinking, Is.False, "Low but not depleted stamina blinks only the fill.");
            Assert.That(background.color.a, Is.EqualTo(backgroundAlpha));
            minimumAlpha = Mathf.Min(minimumAlpha, fill.color.a);
            maximumAlpha = Mathf.Max(maximumAlpha, fill.color.a);
        }
        Assert.That(fill.fillAmount, Is.EqualTo(0.29f).Within(0.001f));
        Assert.That(maximumAlpha - minimumAlpha, Is.GreaterThan(baseAlpha * 0.4f));

        Assert.That(stamina.TryUseStamina(1000f), Is.True);
        float backgroundMinimum = float.MaxValue;
        float backgroundMaximum = float.MinValue;
        until = Time.realtimeSinceStartup + 0.4f;
        while (Time.realtimeSinceStartup < until)
        {
            yield return null;
            Assert.That(hud.StaminaPanelBlinking, Is.True, "Full depletion blinks the whole bar.");
            backgroundMinimum = Mathf.Min(backgroundMinimum, background.color.a);
            backgroundMaximum = Mathf.Max(backgroundMaximum, background.color.a);
        }
        Assert.That(backgroundMaximum - backgroundMinimum, Is.GreaterThan(backgroundAlpha * 0.4f));
        until = Time.realtimeSinceStartup + 5f;
        while (stamina.IsExhausted && Time.realtimeSinceStartup < until)
        {
            Assert.That(hud.StaminaPanelBlinking, Is.True, "The whole bar blinks until 30% refills.");
            yield return null;
        }
        yield return null;
        Assert.That(stamina.IsExhausted, Is.False);
        Assert.That(hud.StaminaPanelBlinking, Is.False);
        Assert.That(background.color.a, Is.EqualTo(backgroundAlpha), "The background stops blinking once unlocked.");

        until = Time.realtimeSinceStartup + 3f;
        while (stamina.NormalizedStamina < 0.3f && Time.realtimeSinceStartup < until)
            yield return null;
        yield return null;
        Assert.That(hud.StaminaBlinking, Is.False);
        Assert.That(fill.color.a, Is.EqualTo(baseAlpha), "The fill must stop blinking at 30%.");

        NetworkServer.RemovePlayerForConnection(NetworkServer.localConnection, RemovePlayerOptions.Destroy);
        yield return null;
        yield return null;
        Assert.That(panel.activeSelf, Is.False);
    }

    [Test]
    public void DamageAndHealingClampAndSyncThroughGeneratedSerializers()
    {
        local.TakeDamage(int.MaxValue);
        Assert.That(local.CurrentHealth, Is.Zero);
        local.Heal(int.MaxValue);
        Assert.That(local.CurrentHealth, Is.EqualTo(6));
        Assert.Throws<ArgumentOutOfRangeException>(() => local.TakeDamage(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => local.Heal(-1));
        PlayerHealth replica = Track(new GameObject("Health client replica")).AddComponent<PlayerHealth>();
        int notifications = 0;
        replica.HealthChanged += _ => notifications++;
        local.TakeDamage(1);
        NetworkWriter writer = new NetworkWriter();
        local.OnSerialize(writer, true);
        replica.OnDeserialize(new NetworkReader(writer.ToArray()), true);
        Assert.That(replica.CurrentHealth, Is.EqualTo(5));
        Assert.That(notifications, Is.EqualTo(1));
        local.TakeDamage(2);
        writer = new NetworkWriter();
        local.OnSerialize(writer, false);
        replica.OnDeserialize(new NetworkReader(writer.ToArray()), false);
        Assert.That(replica.CurrentHealth, Is.EqualTo(3));
        Assert.That(notifications, Is.EqualTo(2));
    }

    [UnityTest]
    public IEnumerator HudSurvivesSceneUnloadRejectsDuplicatesAndRebindsAfterRespawn()
    {
        yield return null;
        yield return null;
        AssertHearts(6);
        temporaryScene = SceneManager.CreateScene("Health HUD persistence test");
        Scene previousScene = SceneManager.GetActiveScene();
        SceneManager.SetActiveScene(temporaryScene);
        GameObject duplicate = Track(Object.Instantiate(HudPrefab()));
        SceneManager.SetActiveScene(previousScene);
        yield return null;
        Assert.That(duplicate == null, Is.True);
        yield return SceneManager.UnloadSceneAsync(temporaryScene);
        Assert.That(hud != null && hud.gameObject.activeInHierarchy, Is.True);
        Assert.That(hud.gameObject.scene.name, Is.EqualTo("DontDestroyOnLoad"));
        Assert.That(Object.FindObjectsByType<PlayerHud>(FindObjectsSortMode.None).Length, Is.EqualTo(1));

        NetworkServer.RemovePlayerForConnection(NetworkServer.localConnection, RemovePlayerOptions.Destroy);
        yield return null;
        yield return null;
        Assert.That(Panel().activeSelf, Is.False);
        local = Track(Object.Instantiate(PlayerPrefab())).GetComponent<PlayerHealth>();
        Assert.That(NetworkServer.AddPlayerForConnection(NetworkServer.localConnection, local.gameObject), Is.True);
        local.TakeDamage(3);
        yield return null;
        yield return null;
        Assert.That(Panel().activeSelf, Is.True);
        AssertHearts(3);
        hud.enabled = false;
        Assert.That(Panel().activeSelf, Is.False);
        local.Heal(1);
        hud.enabled = true;
        yield return null;
        yield return null;
        AssertHearts(4);
        NetworkClient.Disconnect();
        yield return null;
        yield return null;
        Assert.That(Panel().activeSelf, Is.False);
        NetworkClient.Shutdown();
        NetworkServer.Shutdown();
        StartNetwork();
        local = Track(Object.Instantiate(PlayerPrefab())).GetComponent<PlayerHealth>();
        Assert.That(NetworkServer.AddPlayerForConnection(NetworkServer.localConnection, local.gameObject), Is.True);
        yield return null;
        yield return null;
        AssertHearts(6);
    }

    private static void StartNetwork()
    {
        NetworkServer.Listen(4);
        NetworkClient.RegisterPrefab(PlayerPrefab());
        NetworkClient.ConnectHost();
        NetworkServer.localConnection.isAuthenticated = true;
        NetworkClient.connection.isAuthenticated = true;
        HostMode.InvokeOnConnected();
        Assert.That(NetworkClient.Ready(), Is.True);
    }

    private void AssertHearts(int health)
    {
        Assert.That(Panel().activeSelf, Is.True);
        SerializedProperty hearts = new SerializedObject(hud).FindProperty("hearts");
        Assert.That(hearts.arraySize, Is.EqualTo(3));
        for (int index = 0; index < 3; index++)
        {
            int remaining = health - index * 2;
            Sprite expected = sprites[remaining >= 2 ? 0 : remaining == 1 ? 1 : 2];
            Image image = (Image)hearts.GetArrayElementAtIndex(index).objectReferenceValue;
            Assert.That(image.sprite, Is.SameAs(expected), $"Health {health}, heart {index + 1}");
            Assert.That(image.enabled, Is.True);
        }
    }

    private GameObject Panel()
    {
        return (GameObject)new SerializedObject(hud).FindProperty("healthPanel").objectReferenceValue;
    }

    private static GameObject PlayerPrefab() =>
        AssetDatabase.LoadAssetAtPath<GameObject>("Assets/PREFABS/NetworkPlayer.prefab");

    private static GameObject HudPrefab() =>
        AssetDatabase.LoadAssetAtPath<GameObject>("Assets/PREFABS/PlayerHud.prefab");

    private T Track<T>(T obj) where T : Object
    {
        objects.Add(obj);
        return obj;
    }
}
#endif
