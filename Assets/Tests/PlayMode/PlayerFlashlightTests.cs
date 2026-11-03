#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Mirror;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.TestTools;

public sealed class PlayerFlashlightTests : InputTestFixture
{
    private const string PlayerPath = "Assets/PREFABS/NetworkPlayer.prefab";

    private readonly List<GameObject> created = new List<GameObject>();
    private GameObject transportObject;
    private PlayerFlashlight local;
    private PlayerFlashlight remote;
    private Transport previousTransport;
    private bool previousListen;

    public override void Setup()
    {
        base.Setup();
        Assert.That(NetworkServer.active || NetworkClient.active, Is.False, "Run these tests without an active NetworkManager.");
        previousTransport = Transport.active;
        previousListen = NetworkServer.listen;
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPath);
        transportObject = new GameObject("Flashlight Test Transport");
        Transport.active = transportObject.AddComponent<TelepathyTransport>();
        NetworkServer.listen = false;
        NetworkServer.Listen(4);
        NetworkClient.RegisterPrefab(prefab);
        NetworkClient.ConnectHost();
        NetworkServer.localConnection.isAuthenticated = true;
        NetworkClient.connection.isAuthenticated = true;
        HostMode.InvokeOnConnected();
        Assert.That(NetworkClient.Ready(), Is.True);

        GameObject ground = Track(GameObject.CreatePrimitive(PrimitiveType.Cube));
        ground.transform.position = new Vector3(3000f, -0.5f, 0f);
        ground.transform.localScale = new Vector3(60f, 1f, 60f);
        local = Track(Object.Instantiate(prefab, new Vector3(3000f, 0f, 0f), Quaternion.identity)).GetComponent<PlayerFlashlight>();
        remote = Track(Object.Instantiate(prefab, new Vector3(3010f, 0f, 0f), Quaternion.identity)).GetComponent<PlayerFlashlight>();
        Physics.SyncTransforms();
        Assert.That(NetworkServer.AddPlayerForConnection(NetworkServer.localConnection, local.gameObject), Is.True);
        NetworkServer.Spawn(remote.gameObject);
    }

    public override void TearDown()
    {
        NetworkClient.Disconnect();
        NetworkClient.Shutdown();
        NetworkServer.Shutdown();
        for (int index = created.Count - 1; index >= 0; index--)
        {
            if (created[index] != null)
                Object.DestroyImmediate(created[index]);
        }
        created.Clear();
        if (transportObject != null)
            Object.DestroyImmediate(transportObject);
        PlayerControlLock.ReleaseAll();
        PlayerPersistence.ResetLocalPlayerState();
        Transport.active = previousTransport;
        NetworkServer.listen = previousListen;
        base.TearDown();
    }

    [Test]
    public void PrefabHasAStrongFirstPersonBeamAndAWeakerWorldBeam()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPath);
        var flashlight = prefab.GetComponent<PlayerFlashlight>();
        Assert.That(flashlight, Is.Not.Null);
        Light owner = flashlight.OwnerLight, world = flashlight.RemoteLight;
        Assert.That(owner, Is.Not.Null);
        Assert.That(world, Is.Not.Null);
        Assert.That(owner.type, Is.EqualTo(LightType.Spot));
        Assert.That(world.type, Is.EqualTo(LightType.Spot));
        Assert.That(owner.GetComponentInParent<Camera>(true), Is.Not.Null, "The first-person beam follows the owner's camera.");
        Assert.That(owner.transform.IsChildOf(prefab.transform.Find("FirstPersonView")), Is.True);
        Assert.That(world.transform.IsChildOf(prefab.transform.Find("WorldView")), Is.True, "Others see the beam on the world body.");
        Assert.That(world.GetComponentInParent<SpriteRenderer>(true), Is.Null, "The world beam is not billboarded with the sprite.");
        Assert.That(world.intensity, Is.GreaterThan(0f), "Others can see the beam (intensities are user-tunable).");
        Assert.That(world.range, Is.LessThan(owner.range));
        Assert.That(owner.innerSpotAngle, Is.LessThan(owner.spotAngle));
        Assert.That(owner.shadows, Is.Not.EqualTo(LightShadows.None));
        InputAction action = AssetDatabase.LoadAssetAtPath<InputActionAsset>("Assets/InputSystem_Actions.inputactions").FindAction("Player/Flashlight");
        Assert.That(action, Is.Not.Null);
        Assert.That(action.type, Is.EqualTo(InputActionType.Button));
        Assert.That(action.bindings[0].path, Is.EqualTo("<Keyboard>/f"));
    }

    [UnityTest]
    public IEnumerator BeamCookieHasABrightCentreAndRimAroundADimmerMiddle()
    {
        yield return null;
        Assert.That(local.Cookie, Is.Not.Null);
        Assert.That(local.OwnerLight.cookie, Is.SameAs(local.Cookie));
        Assert.That(local.RemoteLight.cookie, Is.SameAs(local.Cookie));
        float centre = local.BeamProfile(0f), middle = local.BeamProfile(0.55f), rim = local.BeamProfile(0.82f);
        Assert.That(centre, Is.GreaterThan(middle + 0.3f));
        Assert.That(rim, Is.GreaterThan(middle + 0.2f), "The reflector rim is brighter than the middle of the beam.");
        Assert.That(local.BeamProfile(1f), Is.Zero.Within(0.01f), "The beam has a hard edge.");
        Color centrePixel = local.Cookie.GetPixel(local.Cookie.width / 2, local.Cookie.height / 2);
        Color cornerPixel = local.Cookie.GetPixel(0, 0);
        Assert.That(centrePixel.a, Is.GreaterThan(0.9f));
        Assert.That(cornerPixel.a, Is.Zero.Within(0.01f));
    }

    [UnityTest]
    public IEnumerator FTogglesOnlyTheOwnersFlashlightAndSyncsItsAim()
    {
        Keyboard keyboard = InputSystem.AddDevice<Keyboard>();
        yield return null;
        Assert.That(local.IsOn, Is.True, "The flashlight starts on.");
        Assert.That(local.OwnerLight.enabled, Is.True);
        Assert.That(local.OwnerLight.isActiveAndEnabled, Is.True, "The owner sees the first-person beam.");
        Assert.That(local.RemoteLight.isActiveAndEnabled, Is.False, "The owner does not see its own world beam.");
        Assert.That(remote.RemoteLight.isActiveAndEnabled, Is.True, "Other players' beams are visible.");
        Assert.That(remote.OwnerLight.isActiveAndEnabled, Is.False);

        PressF(keyboard);
        Assert.That(local.IsOn, Is.False);
        Assert.That(local.OwnerLight.enabled, Is.False);
        Assert.That(remote.IsOn, Is.True, "Only the local player's flashlight toggles.");
        Assert.That(SyncVarValue(local, "isOn"), Is.False);

        PlayerControlLock.Acquire(this);
        PressF(keyboard);
        Assert.That(local.IsOn, Is.False, "F is ignored while controls are locked (menus).");
        PlayerControlLock.Release(this);
        PressF(keyboard);
        Assert.That(local.IsOn, Is.True);

        remote.Toggle();
        Assert.That(remote.IsOn, Is.True, "Toggle is owner-only.");

        Camera camera = local.GetComponentInChildren<Camera>(true);
        camera.transform.localRotation = Quaternion.Euler(30f, 0f, 0f);
        Step(local);
        Assert.That(local.SyncedPitch, Is.EqualTo(30f).Within(0.01f));
        Assert.That(Mathf.DeltaAngle(0f, local.RemoteLight.transform.localEulerAngles.x), Is.EqualTo(30f).Within(0.01f),
            "The world beam follows the owner's camera pitch.");
        camera.transform.localRotation = Quaternion.identity;
        Step(local);
    }

    [UnityTest]
    public IEnumerator BeamFlickersOnlyWhileAnEnemyIsInsideIt()
    {
        yield return null;
        Light beam = local.OwnerLight;
        float baseIntensity = beam.intensity;
        Assert.That(local.OwnerFlickering, Is.False);

        GameObject enemy = Track(GameObject.CreatePrimitive(PrimitiveType.Cube));
        enemy.transform.position = beam.transform.position + beam.transform.forward * 6f;
        enemy.AddComponent<EnemyFearSource>();
        Physics.SyncTransforms();
        bool dropped = false;
        float deadline = Time.time + 4f;
        while (Time.time < deadline && !dropped)
        {
            yield return null;
            Assert.That(local.OwnerFlickering, Is.True, "An enemy in the beam makes it flicker.");
            dropped = beam.intensity < baseIntensity * 0.5f;
        }
        Assert.That(dropped, Is.True, "Flickering includes hard drop-outs.");
        Assert.That(local.FlickerFactor(0f), Is.InRange(0f, 1f));

        GameObject wall = Track(GameObject.CreatePrimitive(PrimitiveType.Cube));
        wall.transform.position = beam.transform.position + beam.transform.forward * 3f;
        wall.transform.rotation = beam.transform.rotation;
        wall.transform.localScale = new Vector3(4f, 4f, 0.2f);
        Physics.SyncTransforms();
        yield return null;
        yield return null;
        Assert.That(local.OwnerFlickering, Is.False, "Walls stop the beam reaching the enemy.");
        Assert.That(beam.intensity, Is.EqualTo(baseIntensity));
        Object.DestroyImmediate(wall);

        enemy.transform.position = beam.transform.position - beam.transform.forward * 6f;
        Physics.SyncTransforms();
        yield return null;
        yield return null;
        Assert.That(local.OwnerFlickering, Is.False, "Enemies outside the cone do not cause flicker.");
        Assert.That(beam.intensity, Is.EqualTo(baseIntensity));
    }

    private void PressF(Keyboard keyboard)
    {
        Press(keyboard.fKey);
        InputSystem.Update();
        Step(local);
        Step(remote);
        Release(keyboard.fKey);
        InputSystem.Update();
        Step(local);
    }

    private static bool SyncVarValue(PlayerFlashlight flashlight, string field) =>
        (bool)typeof(PlayerFlashlight).GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(flashlight);

    private static void Step(Behaviour behaviour) =>
        behaviour.GetType().GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(behaviour, null);

    private GameObject Track(GameObject instance)
    {
        created.Add(instance);
        return instance;
    }
}
#endif
