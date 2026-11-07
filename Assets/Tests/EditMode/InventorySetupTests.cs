using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

public sealed class InventorySetupTests
{
    private const string FontPath = "Assets/Fonts/blood_crow/bloodcrow.ttf";

    [Test]
    public void InputActionsScrollAndSelectSlotsOneToFour()
    {
        var actions = AssetDatabase.LoadAssetAtPath<InputActionAsset>("Assets/InputSystem_Actions.inputactions");
        InputAction scroll = actions.FindAction("Player/InventoryScroll");
        Assert.That(scroll, Is.Not.Null);
        Assert.That(scroll.type, Is.EqualTo(InputActionType.Value));
        Assert.That(scroll.bindings.Select(binding => binding.path), Has.Member("<Mouse>/scroll/y"));
        for (int index = 1; index <= PlayerInventory.MaxSlots; index++)
        {
            InputAction slot = actions.FindAction($"Player/Slot{index}");
            Assert.That(slot, Is.Not.Null);
            Assert.That(slot.type, Is.EqualTo(InputActionType.Button));
            Assert.That(slot.bindings.Select(binding => binding.path), Has.Member($"<Keyboard>/{index}"));
        }
        foreach (string unused in new[] { "Player/Previous", "Player/Next" })
        {
            InputAction action = actions.FindAction(unused);
            if (action != null)
                Assert.That(action.bindings.Any(binding => binding.path.StartsWith("<Keyboard>/")), Is.False, $"{unused} no longer shares the number keys.");
        }
    }

    [Test]
    public void NetworkPlayerHasAnInventoryWithTheCatalogAndActions()
    {
        PlayerInventory inventory = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/PREFABS/NetworkPlayer.prefab").GetComponent<PlayerInventory>();
        Assert.That(inventory, Is.Not.Null);
        Assert.That(inventory.Catalog, Is.SameAs(AssetDatabase.LoadAssetAtPath<ItemCatalog>("Assets/Settings/ItemCatalog.asset")));
        Assert.That(new SerializedObject(inventory).FindProperty("inputActions").objectReferenceValue,
            Is.SameAs(AssetDatabase.LoadAssetAtPath<InputActionAsset>("Assets/InputSystem_Actions.inputactions")));
        Assert.That(PlayerInventory.BaseSlots, Is.EqualTo(2));
        Assert.That(PlayerInventory.MaxSlots, Is.EqualTo(4));
    }

    [Test]
    public void HudBarSitsAtTheBottomCentreWithThePassiveSlotFirst()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/PREFABS/PlayerHud.prefab");
        InventoryHud hud = prefab.GetComponentInChildren<InventoryHud>(true);
        Assert.That(hud, Is.Not.Null);
        var root = (RectTransform)hud.transform;
        Assert.That(root.anchorMin, Is.EqualTo(new Vector2(0.5f, 0f)));
        Assert.That(root.anchorMax, Is.EqualTo(new Vector2(0.5f, 0f)));
        Assert.That(prefab.transform.GetChild(prefab.transform.childCount - 1).name, Is.EqualTo("DamageFlash"));

        Assert.That(hud.Bar.GetComponent<HorizontalLayoutGroup>(), Is.Not.Null);
        Assert.That(hud.Bar.GetChild(0), Is.SameAs(hud.PassiveSlot.transform), "The passive slot is on the left.");
        Assert.That(hud.PassiveSlot.GetComponent<Image>().sprite.name, Is.EqualTo("PassiveUI.template"));
        Assert.That(hud.Slots.Length, Is.EqualTo(PlayerInventory.MaxSlots));
        for (int index = 0; index < hud.Slots.Length; index++)
        {
            Assert.That(hud.Slots[index].transform.parent, Is.SameAs(hud.Bar));
            Assert.That(hud.Slots[index].transform.GetSiblingIndex(), Is.EqualTo(index + 1));
            Image background = hud.Slots[index].GetComponent<Image>();
            Assert.That(background.sprite.name, Is.EqualTo("SlotUI"));
            Assert.That(background.raycastTarget, Is.False);
            Assert.That(hud.SlotIcons[index].transform.parent, Is.SameAs(hud.Slots[index].transform), "Item icons sit on top of the slot.");
            Assert.That(hud.SlotIcons[index].raycastTarget, Is.False);
        }
        Assert.That(hud.PassiveIcon.transform.parent, Is.SameAs(hud.PassiveSlot.transform));
        Assert.That(hud.SelectedSlotSize, Is.GreaterThan(hud.SlotSize), "The selected slot is slightly larger.");
        Assert.That(hud.PulseScale, Is.GreaterThan(1f));
    }

    [Test]
    public void MoneySitsRightOfTheHeartsInBloodCrow()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/PREFABS/PlayerHud.prefab");
        AgimatHud hud = prefab.GetComponentInChildren<AgimatHud>(true);
        Text money = hud.MoneyText;
        Assert.That(money, Is.Not.Null);
        Assert.That(money.transform.parent.name, Is.EqualTo("HealthPanel"));
        var rect = (RectTransform)money.transform;
        Assert.That(rect.anchorMin.x, Is.EqualTo(1f), "Anchored to the right edge of the hearts.");
        Assert.That(rect.pivot.x, Is.EqualTo(0f));
        Assert.That(money.font, Is.SameAs(AssetDatabase.LoadAssetAtPath<Font>(FontPath)));
        Assert.That(money.raycastTarget, Is.False);
    }

    [Test]
    public void SacrificeRewardsAndPesoFormat()
    {
        var host = new GameObject("Bank");
        try
        {
            AgimatBank bank = host.AddComponent<AgimatBank>();
            Assert.That(bank.RewardFor(AgimatType.Gold), Is.EqualTo(20));
            Assert.That(bank.RewardFor(AgimatType.Silver), Is.EqualTo(10));
            Assert.That(bank.RewardFor(AgimatType.Bronze), Is.EqualTo(5));
        }
        finally
        {
            Object.DestroyImmediate(host);
        }
        Assert.That(AgimatBank.PesoSign, Is.EqualTo("\u20B1"));
        Assert.That(AgimatBank.FormatPeso(100), Is.EqualTo("\u20B1100"));
    }

    [Test]
    public void ItemCatalogFindsRegisteredItems()
    {
        var catalog = ScriptableObject.CreateInstance<ItemCatalog>();
        var backpack = ScriptableObject.CreateInstance<ItemDefinition>();
        try
        {
            backpack.Configure("backpack", "Backpack", null, ItemSlotKind.Passive, 2);
            catalog.Register(backpack);
            Assert.That(catalog.Find("backpack"), Is.SameAs(backpack));
            Assert.That(catalog.Find("missing"), Is.Null);
            Assert.That(catalog.Find(""), Is.Null);
            Assert.That(backpack.ExtraSlots, Is.EqualTo(2));
        }
        finally
        {
            Object.DestroyImmediate(backpack);
            Object.DestroyImmediate(catalog);
        }
    }
}
