#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using Mirror;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

public sealed class ShopTests
{
    private const string PlayerPath = "Assets/PREFABS/NetworkPlayer.prefab";
    private const string LoadingPath = "Assets/PREFABS/TravelLoadingScreen.prefab";
    private const string HudPath = "Assets/PREFABS/PlayerHud.prefab";
    private const string ScreenPath = "Assets/PREFABS/ShopScreen.prefab";
    private const string PickupPath = "Assets/PREFABS/ItemPickup.prefab";
    private const string CatalogPath = "Assets/Settings/ItemCatalog.asset";

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
        if (ShopScreen.Current != null)
            Object.DestroyImmediate(ShopScreen.Current.gameObject);
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
    public IEnumerator ShopkeeperSellsTwoItemsPerQuotaForTheTeamsMoney()
    {
        Track(GroundAt(Vector3.zero));
        (RoundClock clock, AgimatBank bank, ShopStock shop) = StartHost();
        yield return WaitUntil(() => NetworkClient.localPlayer != null, 5f, "The host player did not spawn.");
        GameObject player = NetworkClient.localPlayer.gameObject;
        PlayerInventory inventory = player.GetComponent<PlayerInventory>();
        ShopkeeperInteractable shopkeeper = Shopkeeper(player.transform.position + new Vector3(2f, 0f, 0f));
        yield return WaitUntil(() => shop.Count == 2, 2f, "The shop did not stock two items.");
        Assert.That(shop.StockRound, Is.EqualTo(1));
        ItemDefinition first = shop.GetItem(0);
        ItemDefinition second = shop.GetItem(1);
        Assert.That(first, Is.Not.EqualTo(second), "Two different items per quota.");
        Assert.That(shop.GetPrice(0), Is.EqualTo(first.ShopPrice).And.GreaterThan(0));

        Assert.That(shopkeeper.CanInteract(null), Is.True);
        shopkeeper.Interact(null);
        ShopScreen screen = ShopScreen.Current;
        Assert.That(screen != null && screen.IsOpen, Is.True);
        Assert.That(PlayerControlLock.IsLocked && HudVisibility.Hidden, Is.True, "Controls lock and the HUD hides like the Albularyo.");
        yield return null;
        Assert.That(screen.ItemNames[0].text, Is.EqualTo(first.DisplayName));
        Assert.That(screen.ItemNames[1].text, Is.EqualTo(second.DisplayName));
        Assert.That(screen.ItemPrices[0].text, Is.EqualTo(AgimatBank.FormatPeso(first.ShopPrice)));
        Assert.That(screen.ItemIcons[0].sprite, Is.EqualTo(first.Icon));
        Assert.That(screen.MoneyText.text, Is.EqualTo("Money: " + AgimatBank.FormatPeso(0)));
        Assert.That(screen.GreetingLines, Does.Contain(screen.DialogueText.text));

        screen.BuyButtons[0].onClick.Invoke();
        yield return null;
        Assert.That(screen.StatusText.text, Is.EqualTo("Not enough money."));
        Assert.That(inventory.GetItem(0), Is.Null);

        bank.ServerAddMoney(100);
        screen.BuyButtons[0].onClick.Invoke();
        yield return WaitUntil(() => inventory.GetItem(0) == first, 2f, "The Buy button did not reach the host.");
        Assert.That(bank.Money, Is.EqualTo(100 - first.ShopPrice), "The team pays from its shared money.");
        Assert.That(shop.IsSold(0), Is.True);
        yield return WaitUntil(() => screen.StatusText.text == $"Bought {first.DisplayName}.", 2f, "The purchase was not confirmed.");
        Assert.That(screen.BuyButtons[0].interactable, Is.False);
        Assert.That(screen.BuyButtons[0].GetComponentInChildren<Text>().text, Is.EqualTo("Sold"));
        Assert.That(shop.ServerBuy(inventory, 0), Is.EqualTo(ShopBuyResult.SoldOut), "Each item sells once per quota.");

        Assert.That(shop.ServerBuy(inventory, 1), Is.EqualTo(ShopBuyResult.Bought));
        int money = bank.Money;
        yield return null;
        Assert.That(screen.DialogueText.text, Is.EqualTo(screen.EmptyLine), "Everything is sold.");

        // Out of range requests are refused by the host.
        screen.Close();
        Assert.That(PlayerControlLock.IsLocked, Is.False);
        clock.ServerCompleteRound();
        yield return WaitUntil(() => shop.StockRound == 2, 2f, "The shop did not restock for the next quota.");
        Assert.That(shop.Count, Is.EqualTo(2));
        Assert.That(shop.IsSold(0) || shop.IsSold(1), Is.False);
        Assert.That(shop.ServerBuy(inventory, 0), Is.EqualTo(ShopBuyResult.InventoryFull), "Both base slots are taken.");
        Assert.That(bank.Money, Is.EqualTo(money), "Nothing is charged for a refused purchase.");

        inventory.ServerClear();
        shopkeeper.transform.position = player.transform.position + new Vector3(50f, 0f, 0f);
        ShopBuyResult? answer = null;
        System.Action<int, ShopBuyResult> handler = (index, result) => answer = result;
        ShopStock.BuyAnswered += handler;
        try
        {
            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("not near a shopkeeper"));
            shop.RequestBuy(0);
            yield return WaitUntil(() => answer.HasValue, 2f, "The host did not answer.");
            Assert.That(answer, Is.EqualTo(ShopBuyResult.Refused));
            Assert.That(inventory.GetItem(0), Is.Null);
        }
        finally
        {
            ShopStock.BuyAnswered -= handler;
        }
    }

    [UnityTest]
    public IEnumerator HudNamesTheSelectedItemAndPickupsAreAgimatSized()
    {
        Track(GroundAt(Vector3.zero));
        GameObject pickupPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PickupPath);
        (_, _, _) = StartHost(pickupPrefab);
        yield return WaitUntil(() => NetworkClient.localPlayer != null, 5f, "The host player did not spawn.");
        PlayerInventory inventory = NetworkClient.localPlayer.GetComponent<PlayerInventory>();
        GameObject hudObject = Track(Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(HudPath)));
        Text label = hudObject.GetComponentInChildren<InventoryHud>(true).SelectedNameText;
        yield return null;
        Assert.That(label.gameObject.activeSelf, Is.False, "Nothing is shown for an empty slot.");

        ItemCatalog catalog = AssetDatabase.LoadAssetAtPath<ItemCatalog>(CatalogPath);
        ItemDefinition item = catalog.Items[0];
        Assert.That(inventory.ServerTryAdd(item), Is.True);
        yield return WaitUntil(() => label.gameObject.activeSelf, 2f, "The selected item's name did not appear.");
        Assert.That(label.text, Is.EqualTo(item.DisplayName));

        ItemDefinition key = catalog.GetKey("G1");
        Assert.That(inventory.ServerTryAdd(key), Is.True);
        inventory.SelectSlot(1);
        yield return WaitUntil(() => label.text == key.DisplayName, 2f, "Keys are named too.");
        Assert.That(label.text, Does.Contain("G1"));

        HudVisibility.Hide(this);
        yield return null;
        Assert.That(label.gameObject.activeSelf, Is.False, "Hidden with the rest of the HUD.");
        HudVisibility.Show(this);
        inventory.ServerClear();
        yield return WaitUntil(() => !label.gameObject.activeSelf, 2f, "The label stayed after the item was gone.");

        ItemPickup pickup = ItemPickup.ServerSpawn(pickupPrefab.GetComponent<ItemPickup>(), item, new Vector3(3f, 0f, 0f));
        Track(pickup.gameObject);
        yield return null;
        Sprite sprite = pickup.WorldSprite.sprite;
        Assert.That(sprite, Is.EqualTo(item.Icon));
        float size = Mathf.Max(sprite.bounds.size.x, sprite.bounds.size.y) * pickup.WorldSprite.transform.localScale.x;
        Assert.That(size, Is.EqualTo(pickup.WorldSize).Within(0.001f), "World pickups are as big as an agimat.");
    }

    private (RoundClock, AgimatBank, ShopStock) StartHost(GameObject spawnPrefab = null)
    {
        managerObject = new GameObject("Shop Test NetworkManager");
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
        clockSettings.ApplyModifiedPropertiesWithoutUndo();
        var bank = managerObject.AddComponent<AgimatBank>();
        var shop = managerObject.AddComponent<ShopStock>();
        var shopSettings = new SerializedObject(shop);
        shopSettings.FindProperty("catalog").objectReferenceValue = AssetDatabase.LoadAssetAtPath<ItemCatalog>(CatalogPath);
        shopSettings.ApplyModifiedPropertiesWithoutUndo();
        managerObject.SetActive(true);

        NetworkServer.listen = false;
        manager.StartHost();
        return (clock, bank, shop);
    }

    private ShopkeeperInteractable Shopkeeper(Vector3 position)
    {
        GameObject npc = Track(GameObject.CreatePrimitive(PrimitiveType.Capsule));
        npc.name = "Test Shopkeeper";
        npc.SetActive(false);
        npc.transform.position = position + Vector3.up;
        var shopkeeper = npc.AddComponent<ShopkeeperInteractable>();
        var settings = new SerializedObject(shopkeeper);
        settings.FindProperty("screenPrefab").objectReferenceValue =
            AssetDatabase.LoadAssetAtPath<GameObject>(ScreenPath).GetComponent<ShopScreen>();
        settings.ApplyModifiedPropertiesWithoutUndo();
        npc.SetActive(true);
        return shopkeeper;
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
