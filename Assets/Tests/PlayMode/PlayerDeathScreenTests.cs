#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using Mirror;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

public sealed class PlayerDeathScreenTests
{
    private const string PlayerPath = "Assets/PREFABS/NetworkPlayer.prefab";
    private const string HudPath = "Assets/PREFABS/PlayerHud.prefab";
    private const string TikbalangPath = "Assets/PREFABS/Enemies/Tikbalang.prefab";

    private readonly List<Object> objects = new List<Object>();
    private Transport previousTransport;
    private bool previousListen;
    private PlayerHealth local;
    private PlayerDeathScreen screen;

    [SetUp]
    public void Setup()
    {
        Assert.That(NetworkServer.active || NetworkClient.active, Is.False);
        previousTransport = Transport.active;
        previousListen = NetworkServer.listen;
        GameObject transport = Track(new GameObject("Death screen test transport"));
        Transport.active = transport.AddComponent<TelepathyTransport>();
        NetworkServer.listen = false;
        NetworkServer.Listen(4);
        NetworkClient.RegisterPrefab(Load(PlayerPath));
        NetworkClient.RegisterPrefab(Load(TikbalangPath));
        NetworkClient.ConnectHost();
        NetworkServer.localConnection.isAuthenticated = true;
        NetworkClient.connection.isAuthenticated = true;
        HostMode.InvokeOnConnected();
        Assert.That(NetworkClient.Ready(), Is.True);

        local = SpawnPlayer(Vector3.zero, true);
        screen = Track(Object.Instantiate(Load(HudPath))).GetComponentInChildren<PlayerDeathScreen>(true);
        Assert.That(screen, Is.Not.Null, "The PlayerHud prefab carries the death screen.");
        screen.zoomSeconds = 0.2f;
        screen.holdSeconds = 0.1f;
        screen.noKillerDelay = 0.3f;
        screen.fadeSeconds = 0.1f;
        screen.blackHoldSeconds = 0.05f;
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
        PlayerControlLock.ReleaseAll();
        Transport.active = previousTransport;
        NetworkServer.listen = previousListen;
        yield return null;
    }

    [UnityTest]
    public IEnumerator CreatureKillZoomsItsFrontViewThenStaysBlackWithNobodyAlive()
    {
        GameObject tikbalang = Load(TikbalangPath);
        Sprite frontView = tikbalang.GetComponent<EnemyJumpscare>().FrontView;
        Assert.That(frontView, Is.Not.Null, "The Tikbalang has a front-view jumpscare image.");
        Assert.That(EnemyJumpscare.Find(tikbalang.GetComponent<NetworkIdentity>().assetId), Is.SameAs(tikbalang.GetComponent<EnemyJumpscare>()));

        yield return null;
        Assert.That(screen.Phase, Is.EqualTo(DeathScreenPhase.Alive));
        Assert.That(screen.BlackoutAlpha, Is.Zero);

        local.TakeDamage(2, tikbalang.GetComponent<NetworkIdentity>());
        Assert.That(local.KillerAssetId, Is.Zero, "Only the fatal hit records a killer.");
        local.TakeDamage(PlayerHealth.MaxHealth, tikbalang.GetComponent<NetworkIdentity>());
        Assert.That(local.KillerAssetId, Is.EqualTo(tikbalang.GetComponent<NetworkIdentity>().assetId));
        yield return null;
        Assert.That(screen.Phase, Is.EqualTo(DeathScreenPhase.Jumpscare));
        Assert.That(screen.JumpscareShown, Is.True);
        Assert.That(screen.JumpscareSprite, Is.SameAs(frontView));
        Assert.That(screen.BlackoutAlpha, Is.EqualTo(1f), "The jumpscare plays on a completely black screen.");
        Assert.That(screen.JumpscareImage.enabled, Is.True);
        RectTransform imageRect = screen.JumpscareImage.rectTransform;
        Vector2 focus = tikbalang.GetComponent<EnemyJumpscare>().FocusPoint;
        Assert.That(focus.y, Is.GreaterThan(0.7f), "The Tikbalang's jumpscare focuses on its face, not its stomach.");
        Assert.That(imageRect.pivot, Is.EqualTo(focus));
        Vector3 focusWorld = imageRect.TransformPoint(Rect.NormalizedToPoint(imageRect.rect, focus));
        Vector3 screenCentre = ((RectTransform)screen.transform).TransformPoint(((RectTransform)screen.transform).rect.center);
        Assert.That(Vector2.Distance(focusWorld, screenCentre), Is.LessThan(0.5f), "The focus point sits at the screen centre.");
        float startScale = screen.JumpscareImage.rectTransform.localScale.x;
        Assert.That(startScale, Is.LessThan(1f));
        Assert.That(PlayerControlLock.IsHeldBy(screen), Is.True, "Dead players cannot move or interact.");

        yield return new WaitForSecondsRealtime(0.22f);
        Assert.That(screen.JumpscareImage.rectTransform.localScale.x, Is.EqualTo(1f).Within(0.001f), "The image zooms in to full size.");

        yield return WaitUntil(() => screen.Phase == DeathScreenPhase.AllDead, 2f, "The jumpscare did not fade to black.");
        yield return null;
        Assert.That(screen.BlackoutAlpha, Is.EqualTo(1f), "With nobody left alive the screen stays black.");
        Assert.That(screen.JumpscareImage.enabled, Is.False);

        local.SetHealth(2);
        Assert.That(local.KillerAssetId, Is.Zero);
        yield return null;
        Assert.That(screen.Phase, Is.EqualTo(DeathScreenPhase.Alive));
        Assert.That(screen.BlackoutAlpha, Is.Zero);
        Assert.That(PlayerControlLock.IsHeldBy(screen), Is.False);
    }

    [UnityTest]
    public IEnumerator DeathWithoutKillerFadesAfterADelayThenSpectatesAndCyclesLivingPlayers()
    {
        PlayerHealth first = SpawnPlayer(new Vector3(5f, 0f, 0f), false);
        PlayerHealth second = SpawnPlayer(new Vector3(-5f, 0f, 3f), false);
        first.transform.rotation = Quaternion.Euler(0f, 90f, 0f);
        second.transform.rotation = Quaternion.Euler(0f, 200f, 0f);
        Camera camera = local.GetComponent<PlayerVision>().PlayerCamera;
        Vector3 cameraLocalPosition = camera.transform.localPosition;
        Canvas[] firstPersonCanvases = local.GetComponentsInChildren<Canvas>();
        yield return null;

        local.SetHealth(0);
        yield return null;
        Assert.That(screen.Phase, Is.EqualTo(DeathScreenPhase.Jumpscare));
        Assert.That(screen.JumpscareShown, Is.False, "Deaths without a killer have no jumpscare.");
        yield return new WaitForSecondsRealtime(0.15f);
        Assert.That(screen.BlackoutAlpha, Is.Zero, "The screen waits before fading to black.");
        yield return WaitUntil(() => screen.BlackoutAlpha >= 0.99f, 1f, "The screen did not fade to black.");

        yield return WaitUntil(() => screen.Phase == DeathScreenPhase.Spectating, 1f, "Spectating did not start.");
        PlayerPersistence expected = first.netId < second.netId ? first.GetComponent<PlayerPersistence>() : second.GetComponent<PlayerPersistence>();
        Assert.That(screen.Target, Is.SameAs(expected));
        yield return WaitUntil(() => screen.BlackoutAlpha == 0f, 1f, "The view did not fade back in on the spectated player.");
        AssertWatching(camera, screen.Target);
        Assert.That(PlayerDeathScreen.SpectatedPlayer, Is.SameAs(screen.Target.gameObject));
        Assert.That(screen.Target.GetComponent<PlayerVision>().WorldSprites.forceRenderingOff, Is.True,
            "The spectated player's own sprite is hidden so it does not block their view.");
        Assert.That(screen.SpectatingLabel.enabled, Is.True);
        Assert.That(screen.SpectatingLabel.text, Does.StartWith("Spectating: " + screen.Target.GetComponent<PlayerName>().DisplayName),
            "The label names the spectated player.");
        Assert.That(PlayerDeathScreen.HudPlayer, Is.SameAs(screen.Target.netIdentity));
        PlayerHud hud = screen.GetComponentInParent<PlayerHud>();
        yield return null;
        Assert.That(hud.ObservedHealth, Is.SameAs(screen.Target.GetComponent<PlayerHealth>()),
            "The spectator's HUD shows the spectated player's health and stamina.");
        foreach (Canvas canvas in firstPersonCanvases)
            Assert.That(canvas.enabled, Is.False, "First-person sprites are hidden while spectating.");

        PlayerPersistence previous = screen.Target;
        screen.SpectateNext();
        Assert.That(screen.Target, Is.Not.SameAs(previous), "Left click cycles to the next living player.");
        yield return null;
        yield return null;
        AssertWatching(camera, screen.Target);
        Assert.That(previous.GetComponent<PlayerVision>().WorldSprites.forceRenderingOff, Is.False);
        screen.SpectateNext();
        Assert.That(screen.Target, Is.SameAs(previous));

        PlayerHealth watched = screen.Target.GetComponent<PlayerHealth>();
        watched.SetHealth(0);
        yield return null;
        yield return null;
        Assert.That(screen.Target, Is.Not.SameAs(watched.GetComponent<PlayerPersistence>()), "Spectating moves on when the watched player dies.");
        Assert.That(watched.GetComponent<PlayerVision>().WorldSprites.forceRenderingOff, Is.True, "Dead players are hidden.");

        screen.Target.GetComponent<PlayerHealth>().SetHealth(0);
        yield return WaitUntil(() => screen.Phase == DeathScreenPhase.AllDead, 1f, "Losing the last living player did not end spectating.");
        yield return null;
        Assert.That(screen.BlackoutAlpha, Is.EqualTo(1f));
        Assert.That(screen.SpectatingLabel.enabled, Is.False);

        local.SetHealth(2);
        yield return null;
        Assert.That(screen.Phase, Is.EqualTo(DeathScreenPhase.Alive));
        Assert.That(camera.transform.localPosition, Is.EqualTo(cameraLocalPosition), "Respawning restores the player's own view.");
        foreach (Canvas canvas in firstPersonCanvases)
            Assert.That(canvas.enabled, Is.True);
        Assert.That(PlayerDeathScreen.SpectatedPlayer, Is.Null);
        yield return null;
        Assert.That(screen.GetComponentInParent<PlayerHud>().ObservedHealth, Is.SameAs(local), "Reviving restores the player's own HUD.");
    }

    [Test]
    public void SpectateNextIsBoundToLeftClick()
    {
        var actions = AssetDatabase.LoadAssetAtPath<UnityEngine.InputSystem.InputActionAsset>("Assets/InputSystem_Actions.inputactions");
        var action = actions.FindAction("Player/SpectateNext");
        Assert.That(action, Is.Not.Null);
        Assert.That(action.type, Is.EqualTo(UnityEngine.InputSystem.InputActionType.Button));
        Assert.That(action.bindings[0].path, Is.EqualTo("<Mouse>/leftButton"));
    }

    private static void AssertWatching(Camera camera, PlayerPersistence target)
    {
        Transform targetCamera = target.GetComponent<PlayerVision>().PlayerCamera.transform;
        Assert.That(Vector3.Distance(camera.transform.position, targetCamera.position), Is.LessThan(0.001f));
        Assert.That(Mathf.DeltaAngle(camera.transform.eulerAngles.y, target.transform.eulerAngles.y), Is.EqualTo(0f).Within(0.01f));
    }

    private PlayerHealth SpawnPlayer(Vector3 position, bool owned)
    {
        GameObject player = Track(Object.Instantiate(Load(PlayerPath), position, Quaternion.identity));
        if (owned)
            Assert.That(NetworkServer.AddPlayerForConnection(NetworkServer.localConnection, player), Is.True);
        else
            NetworkServer.Spawn(player);
        return player.GetComponent<PlayerHealth>();
    }

    private static IEnumerator WaitUntil(System.Func<bool> condition, float seconds, string message)
    {
        float timeout = Time.realtimeSinceStartup + seconds;
        while (!condition() && Time.realtimeSinceStartup < timeout)
            yield return null;
        Assert.That(condition(), Is.True, message);
    }

    private static GameObject Load(string path) => AssetDatabase.LoadAssetAtPath<GameObject>(path);

    private T Track<T>(T obj) where T : Object
    {
        objects.Add(obj);
        return obj;
    }
}
#endif
