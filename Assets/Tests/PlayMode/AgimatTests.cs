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
    private const string PickupPath = "Assets/PREFABS/AgimatPickup.prefab";
    private const string LadyPath = "Assets/PREFABS/Enemies/WhiteLady.prefab";

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
        Assert.That(screen.RewardText.text, Is.EqualTo("Reward: \u20B10"));
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
        Assert.That(bank.Money, Is.EqualTo(20), "A gold agimat pays 20 pesos.");
        Assert.That(screen.RewardText.text, Is.EqualTo("Reward: \u20B120"));
        Assert.That(hud.MoneyText.text, Is.EqualTo("\u20B120"), "The money shows next to the hearts.");

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
        Assert.That(bank.Money, Is.EqualTo(20 + 20 + 10 + 5 + 5), "Gold 20, silver 10 and bronze 5 pesos each.");
        Assert.That(bank.RoundReward, Is.Zero, "The round's reward tally restarts with the next quota.");
        yield return null;
        QuotaReachedBanner banner = hudObject.GetComponentInChildren<QuotaReachedBanner>(true);
        Assert.That(banner.IsPlaying && banner.Banner.gameObject.activeSelf, Is.True, "\"Quota Reached\" slams onto the screen.");
        Assert.That(banner.Label.text, Is.EqualTo("Quota Reached"));
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

    [UnityTest]
    public IEnumerator QuotaBannerSlamsInThenFadesAway()
    {
        GameObject hudObject = Track(Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(HudPath)));
        QuotaReachedBanner banner = hudObject.GetComponentInChildren<QuotaReachedBanner>(true);
        Assert.That(banner.Banner.gameObject.activeSelf, Is.False, "Hidden until a quota is met.");
        banner.Play();
        yield return null;
        Assert.That(banner.Banner.localScale.x, Is.GreaterThan(1f), "It starts large and slams down.");
        yield return new WaitForSecondsRealtime(0.5f);
        Assert.That(banner.Banner.localScale.x, Is.EqualTo(1f).Within(0.001f));
        Assert.That(banner.Banner.GetComponent<CanvasGroup>().alpha, Is.EqualTo(1f).Within(0.001f));
        yield return new WaitForSecondsRealtime(banner.TotalSeconds);
        Assert.That(banner.IsPlaying || banner.Banner.gameObject.activeSelf, Is.False, "It fades away.");
    }

    [UnityTest]
    public IEnumerator PickingUpAnAgimatAddsItToTheSharedInventory()
    {
        Track(GroundAt(Vector3.zero));
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PickupPath);
        (_, AgimatBank bank) = StartHost(prefab);
        yield return WaitUntil(() => NetworkClient.localPlayer != null, 5f, "The host player did not spawn.");
        Vector3 player = NetworkClient.localPlayer.transform.position;

        AgimatPickup near = Object.Instantiate(prefab, player + new Vector3(1.5f, 0.35f, 0f), Quaternion.identity).GetComponent<AgimatPickup>();
        near.ServerSetType(AgimatType.Gold);
        NetworkServer.Spawn(near.gameObject);
        Assert.That(near.GetComponent<Nametag>().DisplayName, Is.EqualTo("Gold Agimat"));
        Assert.That(near.WorldSprite.sprite, Is.SameAs(near.SpriteFor(AgimatType.Gold)));
        AgimatPickupInteractable use = near.GetComponent<AgimatPickupInteractable>();
        Assert.That(use.Prompt, Is.EqualTo("Pick up"));
        Assert.That(use.CanInteract(null), Is.True);
        use.Interact(null);
        yield return WaitUntil(() => bank.Count(AgimatType.Gold) == 1, 2f, "Picking it up did not reach the host.");
        yield return null;
        Assert.That(near == null, Is.True, "The picked-up agimat is destroyed for everyone.");

        AgimatPickup far = Track(Object.Instantiate(prefab, player + new Vector3(20f, 0.35f, 0f), Quaternion.identity)).GetComponent<AgimatPickup>();
        far.ServerSetType(AgimatType.Silver);
        NetworkServer.Spawn(far.gameObject);
        LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("too far away"));
        far.RequestPickup();
        yield return new WaitForSecondsRealtime(0.5f);
        Assert.That(far != null && bank.Count(AgimatType.Silver) == 0, Is.True, "Far pickups are rejected by the host.");
    }

    [UnityTest]
    public IEnumerator WhiteLadyVanishesUpCloseThenReturnsAndLungesWhenLookedAt()
    {
        Track(GroundAt(Vector3.zero));
        (_, _) = StartHost();
        yield return WaitUntil(() => NetworkClient.localPlayer != null, 5f, "The host player did not spawn.");
        GameObject player = NetworkClient.localPlayer.gameObject;
        Camera camera = player.GetComponentInChildren<Camera>(true);
        player.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(LadyPath);
        Assert.That(prefab.GetComponentsInChildren<Collider>(true), Is.Empty, "She blocks nothing and cannot be aimed at as an object.");
        Assert.That(prefab.GetComponent<EnemyFearSource>(), Is.Null, "She is not an enemy: no fear effects.");
        WhiteLady lady = Track(Object.Instantiate(prefab)).GetComponent<WhiteLady>();
        lady.BeginAt(new Vector3(0f, 0f, 8f), 1234);
        yield return null;
        Assert.That(lady.Current, Is.EqualTo(WhiteLady.Phase.Lingering));
        Assert.That(lady.Visible, Is.True);

        CharacterController controller = player.GetComponent<CharacterController>();
        controller.enabled = false;
        player.transform.position = new Vector3(0f, 0f, 6f);
        controller.enabled = true;
        yield return null;
        yield return null;
        Assert.That(lady.Current, Is.EqualTo(WhiteLady.Phase.Hidden), "She vanishes when the player gets close.");
        Assert.That(lady.Visible, Is.False);
        Assert.That(lady.ReappearAt - Time.time, Is.InRange(29f, 120f), "She returns 30 seconds to 2 minutes later.");

        // Look away (up at the sky), then let her come back near the player.
        camera.transform.rotation = Quaternion.LookRotation(Vector3.up, Vector3.back);
        lady.ReappearNow();
        yield return null;
        yield return null;
        Assert.That(lady.Current, Is.EqualTo(WhiteLady.Phase.Stalking));
        Assert.That(lady.Visible, Is.True);
        float distance = Vector3.Distance(new Vector3(lady.transform.position.x, 0f, lady.transform.position.z), new Vector3(0f, 0f, 6f));
        Assert.That(distance, Is.InRange(4f, 11f), "She reappears close to the player.");

        camera.transform.rotation = Quaternion.LookRotation(lady.WorldSprite.bounds.center - camera.transform.position);
        Assert.That(lady.IsLookedAt(camera), Is.True);
        yield return null;
        yield return null;
        Assert.That(lady.Current, Is.EqualTo(WhiteLady.Phase.Lunging), "Looking at her makes her lunge.");
        float before = Vector3.Distance(lady.WorldSprite.bounds.center, camera.transform.position);
        yield return new WaitForSeconds(0.2f);
        if (lady != null)
            Assert.That(Vector3.Distance(lady.WorldSprite.bounds.center, camera.transform.position), Is.LessThan(before), "She zooms at the camera.");
        yield return new WaitForSeconds(0.5f);
        Assert.That(lady == null, Is.True, "She disappears after the lunge.");
    }

    [UnityTest]
    public IEnumerator WhiteLadyDirectorSummonsHerOnlyForTheChosenPlayer()
    {
        Track(GroundAt(Vector3.zero));
        (_, _) = StartHost();
        yield return WaitUntil(() => NetworkClient.localPlayer != null, 5f, "The host player did not spawn.");
        WhiteLadyDirector director = managerObject.GetComponent<WhiteLadyDirector>();
        Assert.That(director, Is.Not.Null);
        Assert.That(director.ServerSummon("SomeOtherScene", 99), Is.SameAs(NetworkServer.localConnection), "The only living player is chosen.");
        yield return new WaitForSecondsRealtime(0.5f);
        Assert.That(director.Current, Is.Null, "A summon for another scene is ignored.");

        // This test scene has no street map, so the received apparition finds no place to stand and stays away.
        LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("White Lady found no place"));
        Assert.That(director.ServerSummon(SceneManager.GetActiveScene().name, 99), Is.SameAs(NetworkServer.localConnection));
        yield return new WaitForSecondsRealtime(1f);
        Assert.That(Object.FindObjectsByType<WhiteLady>(FindObjectsSortMode.None), Is.Empty);
    }

    [UnityTest]
    public IEnumerator WhiteLadyFlickersAroundHerFaceAndLeavesWhenTheSpotterWalksAway()
    {
        Track(GroundAt(Vector3.zero));
        (_, _) = StartHost();
        yield return WaitUntil(() => NetworkClient.localPlayer != null, 5f, "The host player did not spawn.");
        GameObject player = NetworkClient.localPlayer.gameObject;
        Camera camera = player.GetComponentInChildren<Camera>(true);
        CharacterController controller = player.GetComponent<CharacterController>();
        void MovePlayer(Vector3 position)
        {
            controller.enabled = false;
            player.transform.SetPositionAndRotation(position, Quaternion.identity);
            controller.enabled = true;
        }
        MovePlayer(Vector3.zero);
        camera.transform.rotation = Quaternion.LookRotation(Vector3.up, Vector3.back);

        WhiteLady lady = Track(Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(LadyPath))).GetComponent<WhiteLady>();
        var settings = new SerializedObject(lady);
        settings.FindProperty("peekChance").floatValue = 0f;
        settings.ApplyModifiedPropertiesWithoutUndo();
        lady.BeginAt(new Vector3(0f, 0f, 8f), 77);
        yield return null;
        Assert.That(lady.Current, Is.EqualTo(WhiteLady.Phase.Lingering));

        // Face pivot: the sprite's pivot is her face, and her feet still stand on the ground.
        Assert.That(lady.WorldSprite.sprite.pivot.y / lady.WorldSprite.sprite.rect.height, Is.EqualTo(0.88f).Within(0.01f));
        Assert.That(lady.FacePosition.y - lady.transform.position.y, Is.InRange(1.4f, 2f));
        Assert.That(lady.WorldSprite.bounds.min.y, Is.EqualTo(lady.transform.position.y).Within(0.15f), "Her feet touch the ground.");

        float min = 1f, max = 0f;
        float until = Time.time + 1.5f;
        while (Time.time < until)
        {
            Assert.That(lady.FlickerAlpha, Is.InRange(0f, 1f));
            min = Mathf.Min(min, lady.FlickerAlpha);
            max = Mathf.Max(max, lady.FlickerAlpha);
            yield return null;
        }
        Assert.That(max - min, Is.GreaterThan(0.1f), "Her opacity drifts up and down.");
        Assert.That(lady.Current, Is.EqualTo(WhiteLady.Phase.Lingering));

        camera.transform.rotation = Quaternion.LookRotation(lady.FacePosition - camera.transform.position);
        yield return null;
        yield return null;
        Assert.That(lady.Spotted, Is.True, "Looking at her face spots her.");
        Assert.That(lady.Current, Is.EqualTo(WhiteLady.Phase.Lingering), "Spotting her from afar does not make her vanish yet.");

        MovePlayer(new Vector3(0f, 0f, -18.5f));
        camera.transform.rotation = Quaternion.LookRotation(Vector3.up, Vector3.back);
        yield return null;
        yield return null;
        Assert.That(lady.Current, Is.EqualTo(WhiteLady.Phase.Away), "Walking far away from her despawns her.");
        Assert.That(lady.Visible, Is.False);
        Assert.That(lady.ReappearAt - Time.time, Is.EqualTo(lady.FarRespawnSeconds).Within(0.5f));

        lady.ReappearNow();
        yield return null;
        yield return null;
        Assert.That(lady.Current, Is.EqualTo(WhiteLady.Phase.Lingering), "She comes back to linger near that player.");
        Assert.That(Vector3.Distance(new Vector3(lady.transform.position.x, 0f, lady.transform.position.z), player.transform.position),
            Is.InRange(7f, 19f));
    }

    [UnityTest]
    public IEnumerator WhiteLadyPeeksAroundACornerHidesWhenSeenThenLunges()
    {
        Track(GroundAt(Vector3.zero));
        GameObject wall = Track(GameObject.CreatePrimitive(PrimitiveType.Cube));
        wall.name = "Test Building";
        wall.transform.position = new Vector3(4f, 1.5f, 11f);
        wall.transform.localScale = new Vector3(4f, 3f, 4f);
        (_, _) = StartHost();
        yield return WaitUntil(() => NetworkClient.localPlayer != null, 5f, "The host player did not spawn.");
        GameObject player = NetworkClient.localPlayer.gameObject;
        Camera camera = player.GetComponentInChildren<Camera>(true);
        CharacterController controller = player.GetComponent<CharacterController>();
        controller.enabled = false;
        player.transform.SetPositionAndRotation(new Vector3(8f, 0f, 0f), Quaternion.identity);
        controller.enabled = true;
        camera.transform.rotation = Quaternion.LookRotation(Vector3.up, Vector3.back);
        Physics.SyncTransforms();

        WhiteLady lady = Track(Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(LadyPath))).GetComponent<WhiteLady>();
        yield return null;
        Vector3 eye = camera.transform.position;
        Assert.That(lady.TryFindPeekSpot(player.transform.position, eye, out WhiteLady.PeekSpot spot), Is.True, "The building has a corner to peek from.");
        float faceHeight = lady.FacePosition.y - lady.transform.position.y;
        bool Clear(Vector3 to) => !Physics.Raycast(eye, (to - eye).normalized, Vector3.Distance(eye, to), Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        Assert.That(Clear(spot.Peek + Vector3.up * faceHeight), Is.True, "Her face shows at the corner.");
        Assert.That(Clear(spot.Hide + Vector3.up * faceHeight), Is.False, "Behind the corner she is out of sight.");
        Assert.That(new Vector2(spot.Peek.x - spot.Hide.x, spot.Peek.z - spot.Hide.z).magnitude, Is.LessThan(2f), "She hides just around the corner.");

        lady.BeginPeeking(spot, 5);
        yield return null;
        Assert.That(lady.Current, Is.EqualTo(WhiteLady.Phase.Peeking));
        Assert.That(lady.Visible, Is.True);

        camera.transform.rotation = Quaternion.LookRotation(lady.FacePosition - camera.transform.position);
        yield return null;
        yield return null;
        Assert.That(lady.Current, Is.EqualTo(WhiteLady.Phase.Retreating), "Being seen makes her duck behind the corner.");
        yield return WaitUntil(() => lady.Current == WhiteLady.Phase.Hidden, 2f, "She did not finish hiding.");
        Assert.That(lady.Visible, Is.False);
        Assert.That(Vector3.Distance(lady.transform.position, spot.Hide), Is.LessThan(0.05f));

        camera.transform.rotation = Quaternion.LookRotation(Vector3.up, Vector3.back);
        Object.DestroyImmediate(wall);
        Physics.SyncTransforms();
        lady.ReappearNow();
        yield return null;
        yield return null;
        Assert.That(lady.Current, Is.EqualTo(WhiteLady.Phase.Stalking), "After hiding she stalks the player for the jumpscare.");
        camera.transform.rotation = Quaternion.LookRotation(lady.WorldSprite.bounds.center - camera.transform.position);
        yield return WaitUntil(() => lady == null || lady.Current == WhiteLady.Phase.Lunging, 1f, "She did not lunge.");
        if (lady != null)
        {
            yield return WaitUntil(() => lady == null || Vector3.Distance(lady.FacePosition, camera.transform.position) < 1.5f, 1f,
                "Her face did not zoom into the camera.");
        }
        yield return new WaitForSeconds(0.8f);
        Assert.That(lady == null, Is.True, "She disappears after the lunge.");
    }

    private (RoundClock, AgimatBank) StartHost(GameObject spawnPrefab = null)
    {
        managerObject = new GameObject("Agimat Test NetworkManager");
        managerObject.SetActive(false);
        var transport = managerObject.AddComponent<TelepathyTransport>();
        var manager = managerObject.AddComponent<SteamNetworkManager>();
        manager.transport = transport;
        manager.playerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPath);
        if (spawnPrefab != null)
            manager.spawnPrefabs.Add(spawnPrefab);
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
        var lady = managerObject.AddComponent<WhiteLadyDirector>();
        var ladySettings = new SerializedObject(lady);
        ladySettings.FindProperty("whiteLadyPrefab").objectReferenceValue = AssetDatabase.LoadAssetAtPath<GameObject>(LadyPath).GetComponent<WhiteLady>();
        ladySettings.ApplyModifiedPropertiesWithoutUndo();
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
