#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using Mirror;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

public sealed class AgimatTests
{
    private const string PlayerPath = "Assets/PREFABS/NetworkPlayer.prefab";
    private const string LoadingPath = "Assets/PREFABS/TravelLoadingScreen.prefab";
    private const string HudPath = "Assets/PREFABS/PlayerHud.prefab";
    private const string ScreenPath = "Assets/PREFABS/AlbularyoScreen.prefab";

    private readonly List<Object> created = new List<Object>();
    private GameObject managerObject;
    private Transport previousTransport;
    private bool previousListen;

    [SetUp]
    public void Setup()
    {
        previousTransport = Transport.active;
        previousListen = NetworkServer.listen;
    }

    [TearDown]
    public void TearDown()
    {
        if (AlbularyoScreen.Current != null)
            Object.DestroyImmediate(AlbularyoScreen.Current.gameObject);
        if (managerObject != null)
        {
            managerObject.GetComponent<NetworkManager>().StopHost();
            Object.DestroyImmediate(managerObject);
        }
        NetworkClient.Disconnect();
        NetworkClient.Shutdown();
        NetworkServer.Shutdown();
        NetworkManager.ResetStatics();
        for (int i = created.Count - 1; i >= 0; i--)
        {
            if (created[i] != null)
                Object.DestroyImmediate(created[i]);
        }
        created.Clear();
        PlayerControlLock.ReleaseAll();
        HudVisibility.ShowAll();
        PlayerStamina.TravelPaused = false;
        PlayerPersistence.ResetLocalPlayerState();
        Transport.active = previousTransport;
        NetworkServer.listen = previousListen;
    }

    [UnityTest]
    public IEnumerator SacrificingAtTheAlbularyoMeetsTheQuotaAndStartsTheNextRound()
    {
        Track(GroundAt(Vector3.zero));
        (RoundClock clock, AgimatBank bank) = StartHost();
        yield return WaitUntil(() => NetworkClient.localPlayer != null, 5f, "The host player did not spawn.");
        GameObject player = NetworkClient.localPlayer.gameObject;
        AlbularyoInteractable albularyo = Albularyo(player.transform.position + new Vector3(2f, 0f, 0f));
        GameObject hudObject = Track(Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(HudPath)));
        AgimatHud hud = hudObject.GetComponentInChildren<AgimatHud>(true);
        RoundClockHud clockHud = hudObject.GetComponent<RoundClockHud>();
        GameObject healthPanel = hudObject.transform.Find("HealthPanel").gameObject;

        Assert.That(bank.Quota, Is.EqualTo(5));
        bank.ServerAdd(AgimatType.Gold, 2);
        bank.ServerAdd(AgimatType.Silver, 1);
        bank.ServerAdd(AgimatType.Bronze, 3);
        yield return WaitUntil(() => hud.GoldText.text == "x2", 2f, "The HUD did not show the shared gold count.");
        Assert.That(hud.Panel.activeSelf, Is.True);
        Assert.That(hud.SilverText.text, Is.EqualTo("x1"));
        Assert.That(hud.BronzeText.text, Is.EqualTo("x3"));
        Assert.That(hud.QuotaText.text, Is.EqualTo("0/5"));
        Assert.That(healthPanel.activeSelf && clockHud.ClockPanel.gameObject.activeSelf, Is.True);

        Assert.That(albularyo.CanInteract(null), Is.True);
        Assert.That(albularyo.GetComponent<Nametag>().DisplayName, Is.EqualTo("Talk to the Albularyo."));
        albularyo.Interact(null);
        AlbularyoScreen screen = AlbularyoScreen.Current;
        Assert.That(screen != null && screen.IsOpen, Is.True);
        Assert.That(PlayerControlLock.IsLocked, Is.True);
        yield return null;
        yield return null;
        Assert.That(hud.Panel.activeSelf, Is.False, "The side agimat column moves into the Albularyo screen.");
        Assert.That(healthPanel.activeSelf, Is.False, "Health and stamina are hidden while talking.");
        Assert.That(clockHud.ClockPanel.gameObject.activeSelf, Is.False, "The timer is hidden while talking.");
        Assert.That(screen.GoldCountText.text, Is.EqualTo("x2"));
        Assert.That(screen.BronzeCountText.text, Is.EqualTo("x3"));
        Assert.That(screen.TurnedInText.text, Is.EqualTo("Turned in: 0x"));
        Assert.That(screen.RewardText.text, Is.EqualTo("Reward: ???"));
        Assert.That(screen.Dialogue.requestLines, Does.Contain(screen.CurrentLine), "A random request line is shown.");
        Assert.That(screen.DialogueText.text, Is.EqualTo(AlbularyoDialogue.Format(screen.CurrentLine, 5, 5, 0)));

        // A player request through the button is validated by the host (the player is next to the Albularyo).
        clock.ServerSetElapsed(clock.HourSeconds * 4f);
        screen.GoldButton.onClick.Invoke();
        yield return WaitUntil(() => bank.Count(AgimatType.Gold) == 1, 2f, "The Sacrifice button did not reach the host.");
        Assert.That(bank.TurnedIn, Is.EqualTo(1));
        yield return null;
        Assert.That(screen.TurnedInText.text, Is.EqualTo("Turned in: 1x"));
        Assert.That(screen.GoldCountText.text, Is.EqualTo("x1"));

        Assert.That(bank.ServerSacrifice(AgimatType.Gold), Is.True);
        Assert.That(bank.ServerSacrifice(AgimatType.Silver), Is.True);
        Assert.That(bank.ServerSacrifice(AgimatType.Silver), Is.False, "Nothing left of that type.");
        Assert.That(bank.ServerSacrifice(AgimatType.Bronze), Is.True);
        Assert.That(clock.Round, Is.EqualTo(1));
        Assert.That(bank.ServerSacrifice(AgimatType.Bronze), Is.True, "The fifth agimat meets the first quota.");

        Assert.That(clock.Round, Is.EqualTo(2));
        Assert.That(clock.Elapsed, Is.Zero, "Meeting the quota resets the timer.");
        Assert.That(bank.TurnedIn, Is.Zero, "Turned-in agimats reset each round.");
        Assert.That(bank.Quota, Is.EqualTo(7));
        Assert.That(bank.Count(AgimatType.Bronze), Is.EqualTo(1), "Leftover agimats carry over.");
        yield return null;
        Assert.That(screen.TurnedInText.text, Is.EqualTo("Turned in: 0x"));
        Assert.That(screen.Dialogue.quotaMetLines, Does.Contain(screen.CurrentLine), "Meeting the quota shows a quota-met line.");
        Assert.That(screen.DialogueText.text, Is.EqualTo(screen.CurrentLine));
        Assert.That(screen.GoldButton.interactable, Is.False, "No gold left to sacrifice.");
        Assert.That(screen.BronzeButton.interactable, Is.True);

        screen.Close();
        Assert.That(PlayerControlLock.IsLocked, Is.False);
        Assert.That(HudVisibility.Hidden, Is.False);
        yield return null;
        yield return null;
        Assert.That(hud.Panel.activeSelf && healthPanel.activeSelf && clockHud.ClockPanel.gameObject.activeSelf, Is.True);
        Assert.That(hud.QuotaText.text, Is.EqualTo("0/7"));
        Assert.That(hud.BronzeText.text, Is.EqualTo("x1"));
    }

    [UnityTest]
    public IEnumerator SacrificeRequestsFarFromTheAlbularyoAreRejected()
    {
        Track(GroundAt(Vector3.zero));
        (_, AgimatBank bank) = StartHost();
        yield return WaitUntil(() => NetworkClient.localPlayer != null, 5f, "The host player did not spawn.");
        Albularyo(NetworkClient.localPlayer.transform.position + new Vector3(20f, 0f, 0f));
        bank.ServerAdd(AgimatType.Gold, 1);
        LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("not near an Albularyo"));
        bank.RequestSacrifice(AgimatType.Gold);
        yield return new WaitForSecondsRealtime(0.5f);
        Assert.That(bank.Count(AgimatType.Gold), Is.EqualTo(1));
        Assert.That(bank.TurnedIn, Is.Zero);
    }

    [UnityTest]
    public IEnumerator StuckAtBaseWithoutEnoughAgimatsEndsTheGameAndResetsTheInventory()
    {
        Track(GroundAt(Vector3.zero));
        (RoundClock clock, AgimatBank bank) = StartHost();
        yield return WaitUntil(() => NetworkClient.localPlayer != null, 5f, "The host player did not spawn.");
        bank.ServerAdd(AgimatType.Gold, 2);
        bank.ServerAdd(AgimatType.Silver, 1);
        Assert.That(bank.ServerSacrifice(AgimatType.Gold), Is.True);

        clock.ServerSetElapsed(clock.HourSeconds * 6f);
        yield return new WaitForSecondsRealtime(0.6f);
        Assert.That(clock.Phase, Is.EqualTo(RoundPhase.Running), "With time left to travel the game goes on.");

        // Enough held agimats can still meet the quota: no game over even at midnight.
        bank.ServerAdd(AgimatType.Bronze, 2);
        clock.ServerSetElapsed(clock.RoundSeconds);
        yield return new WaitForSecondsRealtime(0.6f);
        Assert.That(clock.Phase, Is.EqualTo(RoundPhase.Running), "The quota is still reachable by sacrificing.");

        bank.ServerAdd(AgimatType.Bronze, -2);
        yield return WaitUntil(() => clock.Phase == RoundPhase.GameOver, 2f, "No time to travel and too few agimats: game over.");
        Assert.That(bank.ServerSacrifice(AgimatType.Gold), Is.False, "No sacrifices during game over.");
        yield return WaitUntil(() => clock.Phase == RoundPhase.Running, 3f, "The game did not reset.");
        Assert.That(clock.Round, Is.EqualTo(1));
        Assert.That(clock.Elapsed, Is.LessThan(0.5f));
        Assert.That(bank.Total, Is.Zero, "The game reset clears the shared inventory.");
        Assert.That(bank.TurnedIn, Is.Zero);
        Assert.That(bank.Quota, Is.EqualTo(5));
        yield return new WaitForSecondsRealtime(0.5f);
        Assert.That(clock.Phase, Is.EqualTo(RoundPhase.Running), "A fresh round with time left is not game over.");
    }

    private (RoundClock, AgimatBank) StartHost()
    {
        managerObject = new GameObject("Agimat Test NetworkManager");
        managerObject.SetActive(false);
        var transport = managerObject.AddComponent<TelepathyTransport>();
        var manager = managerObject.AddComponent<SteamNetworkManager>();
        manager.transport = transport;
        manager.playerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPath);
        manager.autoCreatePlayer = true;
        manager.dontDestroyOnLoad = true;
        var director = managerObject.AddComponent<TravelDirector>();
        var settings = new SerializedObject(director);
        settings.FindProperty("loadingScreenPrefab").objectReferenceValue =
            AssetDatabase.LoadAssetAtPath<GameObject>(LoadingPath).GetComponent<TravelLoadingScreen>();
        settings.FindProperty("fadeDuration").floatValue = 0.1f;
        settings.FindProperty("minimumLoadingSeconds").floatValue = 0f;
        settings.ApplyModifiedPropertiesWithoutUndo();
        var clock = managerObject.AddComponent<RoundClock>();
        var clockSettings = new SerializedObject(clock);
        clockSettings.FindProperty("roundMinutes").floatValue = 1f;
        clockSettings.FindProperty("baseSceneName").stringValue = SceneManager.GetActiveScene().name;
        clockSettings.FindProperty("gameOverSeconds").floatValue = 1f;
        clockSettings.ApplyModifiedPropertiesWithoutUndo();
        var bank = managerObject.AddComponent<AgimatBank>();
        managerObject.SetActive(true);

        NetworkServer.listen = false;
        manager.StartHost();
        return (clock, bank);
    }

    [UnityTest]
    public IEnumerator BillboardSpritesFaceTheViewingCamera()
    {
        GameObject viewer = Track(new GameObject("Viewer", typeof(Camera)));
        viewer.tag = "MainCamera";
        viewer.transform.position = new Vector3(0f, 5f, -10f);
        GameObject sprite = Track(new GameObject("Sprite", typeof(SpriteRenderer)));
        sprite.transform.position = new Vector3(4f, 1f, 0f);
        var billboard = sprite.AddComponent<BillboardSprite>();
        yield return null;
        Vector3 flat = sprite.transform.position - viewer.transform.position;
        flat.y = 0f;
        Assert.That(Vector3.Angle(sprite.transform.forward, flat), Is.LessThan(0.5f), "Faces along the camera ray.");
        Assert.That(Vector3.Angle(sprite.transform.up, Vector3.up), Is.LessThan(0.5f), "Stays upright by default.");

        viewer.transform.position = new Vector3(-6f, 1f, 8f);
        billboard.Pitch = true;
        yield return null;
        Assert.That(Vector3.Angle(sprite.transform.forward, sprite.transform.position - viewer.transform.position), Is.LessThan(0.5f),
            "Pitch tilts it toward the camera too.");
    }

    private AlbularyoInteractable Albularyo(Vector3 position)
    {
        GameObject npc = Track(GameObject.CreatePrimitive(PrimitiveType.Capsule));
        npc.name = "Test Albularyo";
        npc.SetActive(false);
        npc.transform.position = position + Vector3.up;
        var albularyo = npc.AddComponent<AlbularyoInteractable>();
        var settings = new SerializedObject(albularyo);
        settings.FindProperty("screenPrefab").objectReferenceValue =
            AssetDatabase.LoadAssetAtPath<GameObject>(ScreenPath).GetComponent<AlbularyoScreen>();
        settings.ApplyModifiedPropertiesWithoutUndo();
        var nametag = npc.AddComponent<Nametag>();
        var tagSettings = new SerializedObject(nametag);
        tagSettings.FindProperty("displayName").stringValue = "Talk to the Albularyo.";
        tagSettings.ApplyModifiedPropertiesWithoutUndo();
        npc.SetActive(true);
        return albularyo;
    }

    private static GameObject GroundAt(Vector3 position)
    {
        GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
        ground.transform.position = position + new Vector3(0f, -0.5f, 0f);
        ground.transform.localScale = new Vector3(50f, 1f, 50f);
        return ground;
    }

    private static IEnumerator WaitUntil(System.Func<bool> condition, float seconds, string message)
    {
        float timeout = Time.realtimeSinceStartup + seconds;
        while (!condition() && Time.realtimeSinceStartup < timeout)
            yield return null;
        Assert.That(condition(), Is.True, message);
    }

    private T Track<T>(T obj) where T : Object
    {
        created.Add(obj);
        return obj;
    }
}
#endif
