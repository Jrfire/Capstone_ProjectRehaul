using System.Collections.Generic;
using System.Linq;
using Mirror;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public sealed class ShopSetupTests
{
    [Test]
    public void ShopScreenPrefabIsWiredForTwoItems()
    {
        ShopScreen screen = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/PREFABS/ShopScreen.prefab").GetComponent<ShopScreen>();
        Assert.That(screen, Is.Not.Null);
        Assert.That(screen.GetComponent<AlbularyoScreen>(), Is.Null);
        Assert.That(screen.DialogueText, Is.Not.Null);
        Image albularyoPortrait = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/PREFABS/AlbularyoScreen.prefab").GetComponent<AlbularyoScreen>().PortraitImage;
        Assert.That(screen.PortraitImage, Is.Not.Null);
        Assert.That(screen.PortraitImage.sprite, Is.EqualTo(albularyoPortrait != null ? albularyoPortrait.sprite : null), "Shares the Albularyo's portrait for now.");
        Assert.That(screen.Columns.Length, Is.EqualTo(2));
        for (int index = 0; index < 2; index++)
        {
            Assert.That(screen.Columns[index], Is.Not.Null);
            Assert.That(screen.ItemIcons[index] != null && screen.ItemIcons[index].preserveAspect, Is.True);
            Assert.That(screen.ItemNames[index], Is.Not.Null);
            Assert.That(screen.ItemPrices[index], Is.Not.Null);
            Assert.That(screen.BuyButtons[index], Is.Not.Null);
            Assert.That(screen.BuyButtons[index].GetComponent<HoverRevealGraphic>(), Is.Not.Null);
        }
        Assert.That(screen.MoneyText, Is.Not.Null);
        Assert.That(screen.StatusText, Is.Not.Null);
        Assert.That(screen.GreetingLines, Is.Not.Empty);
    }

    [Test]
    public void BaseSceneHasTheShopkeeperAndStock()
    {
        Scene hub = EditorSceneManager.OpenScene("Assets/Scenes/Base.unity", OpenSceneMode.Additive);
        try
        {
            NetworkManager manager = InScene<NetworkManager>(hub).Single();
            ShopStock stock = manager.GetComponent<ShopStock>();
            Assert.That(stock, Is.Not.Null, "The shop stock persists with the NetworkManager.");
            Assert.That(stock.Catalog, Is.EqualTo(AssetDatabase.LoadAssetAtPath<ItemCatalog>("Assets/Settings/ItemCatalog.asset")));
            Assert.That(stock.ItemsPerQuota, Is.EqualTo(2));
            ShopkeeperInteractable shopkeeper = InScene<ShopkeeperInteractable>(hub).Single();
            Assert.That(shopkeeper.ScreenPrefab, Is.Not.Null);
            Assert.That(PrefabUtility.GetCorrespondingObjectFromSource(shopkeeper.gameObject), Is.Not.Null, "Placed from Shopkeeper.prefab.");
            Assert.That(shopkeeper.GetComponent<AlbularyoInteractable>(), Is.Null);
            Assert.That(shopkeeper.GetComponentInChildren<Nametag>(true).DisplayName, Is.EqualTo("Browse the shop."));
            Collider body = shopkeeper.GetComponentInChildren<Collider>();
            Assert.That(body != null && !body.isTrigger, Is.True);
            SpriteRenderer sprite = shopkeeper.GetComponentsInChildren<SpriteRenderer>(true).Single();
            AlbularyoInteractable albularyo = InScene<AlbularyoInteractable>(hub).Single();
            Assert.That(sprite.sprite, Is.EqualTo(albularyo.GetComponentsInChildren<SpriteRenderer>(true).Single().sprite),
                "Shares the Albularyo's sprite for now.");
        }
        finally
        {
            EditorSceneManager.CloseScene(hub, true);
        }
    }

    [Test]
    public void CatalogItemsArePricedAndKeysAreNotSold()
    {
        ItemCatalog catalog = AssetDatabase.LoadAssetAtPath<ItemCatalog>("Assets/Settings/ItemCatalog.asset");
        Assert.That(catalog.Items.Count(item => item.ShopPrice > 0), Is.GreaterThanOrEqualTo(2));
        if (catalog.KeyTemplate != null)
            Assert.That(catalog.KeyTemplate.ShopPrice, Is.Zero);
        List<ItemDefinition> stock = ShopStock.RollStock(catalog, 2, new System.Random(3));
        Assert.That(stock.Count, Is.EqualTo(2));
        Assert.That(stock.Distinct().Count(), Is.EqualTo(2));
    }

    [Test]
    public void RollStockHonoursPricesAndWeights()
    {
        ItemCatalog catalog = ScriptableObject.CreateInstance<ItemCatalog>();
        var items = new List<ItemDefinition>();
        try
        {
            for (int index = 0; index < 3; index++)
            {
                ItemDefinition item = ScriptableObject.CreateInstance<ItemDefinition>();
                item.name = $"Item{index}";
                items.Add(item);
                catalog.Register(item);
            }
            items[0].ConfigureShop(10, 1f);
            items[1].ConfigureShop(0);
            items[2].ConfigureShop(10, 0f);
            List<ItemDefinition> stock = ShopStock.RollStock(catalog, 2, new System.Random(1));
            Assert.That(stock, Is.EqualTo(new[] { items[0] }), "Unpriced and zero-weight items are never stocked.");
        }
        finally
        {
            foreach (ItemDefinition item in items)
                Object.DestroyImmediate(item);
            Object.DestroyImmediate(catalog);
        }
    }

    [Test]
    public void HudShowsTheSelectedItemNameBottomLeft()
    {
        GameObject hud = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/PREFABS/PlayerHud.prefab");
        InventoryHud inventory = hud.GetComponentInChildren<InventoryHud>(true);
        Text label = inventory.SelectedNameText;
        Assert.That(label, Is.Not.Null);
        var rect = (RectTransform)label.transform;
        Assert.That(rect.anchorMin, Is.EqualTo(Vector2.zero));
        Assert.That(rect.anchorMax, Is.EqualTo(Vector2.zero));
        Assert.That(label.font, Is.Not.Null);
        Assert.That(label.raycastTarget, Is.False);
        Assert.That(rect.GetSiblingIndex(), Is.LessThan(hud.transform.Find("DamageFlash").GetSiblingIndex()));
    }

    [Test]
    public void ItemPickupsAreAgimatSized()
    {
        ItemPickup pickup = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/PREFABS/ItemPickup.prefab").GetComponent<ItemPickup>();
        SpriteRenderer agimat = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/PREFABS/AgimatPickup.prefab")
            .GetComponentsInChildren<SpriteRenderer>(true).First(renderer => renderer.sprite != null && renderer.name == "WorldSprites");
        float agimatSize = Mathf.Max(agimat.sprite.bounds.size.x, agimat.sprite.bounds.size.y) * agimat.transform.localScale.x;
        Assert.That(pickup.WorldSize, Is.EqualTo(agimatSize).Within(0.05f));
        ItemCatalog catalog = AssetDatabase.LoadAssetAtPath<ItemCatalog>("Assets/Settings/ItemCatalog.asset");
        foreach (ItemDefinition item in catalog.Items)
        {
            if (item.Icon == null)
                continue;
            float scale = ItemPickup.ScaleFor(item.Icon, pickup.WorldSize);
            float size = Mathf.Max(item.Icon.bounds.size.x, item.Icon.bounds.size.y) * scale;
            Assert.That(size, Is.EqualTo(pickup.WorldSize).Within(0.001f), item.name);
        }
    }

    private static IEnumerable<T> InScene<T>(Scene scene) where T : Component =>
        scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<T>(true));
}
