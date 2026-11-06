using System.Linq;
using Mirror;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public sealed class AgimatSetupTests
{
    private const string FontPath = "Assets/Fonts/blood_crow/bloodcrow.ttf";

    [Test]
    public void QuotaStartsAtFiveAndEachIncreaseDoublesThePrevious()
    {
        Assert.That(AgimatBank.QuotaFor(1), Is.EqualTo(5));
        Assert.That(AgimatBank.QuotaFor(2), Is.EqualTo(7));
        Assert.That(AgimatBank.QuotaFor(3), Is.EqualTo(11));
        Assert.That(AgimatBank.QuotaFor(4), Is.EqualTo(19));
        Assert.That(AgimatBank.QuotaFor(5), Is.EqualTo(35));
        Assert.That(AgimatBank.QuotaFor(0), Is.EqualTo(5), "Rounds below 1 use the first quota.");
        Assert.That(AgimatHud.QuotaLabel(2, 7), Is.EqualTo("2/7"));
    }

    [Test]
    public void HudListsGoldSilverBronzeAndTheQuotaBelowStamina()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/PREFABS/PlayerHud.prefab");
        AgimatHud hud = prefab.GetComponentInChildren<AgimatHud>(true);
        Assert.That(hud, Is.Not.Null);
        Font font = AssetDatabase.LoadAssetAtPath<Font>(FontPath);
        Text[] texts = { hud.GoldText, hud.SilverText, hud.BronzeText, hud.QuotaText };
        string[] icons = { "agimatgold", "agimatsilver", "agimatbronze", "agimaticon" };
        float previousY = float.MaxValue;
        for (int i = 0; i < texts.Length; i++)
        {
            Assert.That(texts[i], Is.Not.Null);
            Assert.That(texts[i].font, Is.SameAs(font), "Agimat texts use blood_crow.");
            Image icon = texts[i].transform.parent.GetComponentsInChildren<Image>(true).Single();
            Assert.That(icon.sprite.name, Is.EqualTo(icons[i]));
            Assert.That(icon.raycastTarget, Is.False);
            float y = ((RectTransform)texts[i].transform.parent).anchoredPosition.y;
            Assert.That(y, Is.LessThan(previousY), "Rows run downwards: gold, silver, bronze, then the quota.");
            previousY = y;
        }

        var panel = (RectTransform)hud.transform;
        Assert.That(panel.anchorMin, Is.EqualTo(new Vector2(0f, 1f)), "Hugs the top-left, below health and stamina.");
        RectTransform stamina = (RectTransform)prefab.transform.Find("StaminaPanel");
        Assert.That(panel.anchoredPosition.y, Is.LessThan(stamina.anchoredPosition.y));
        Assert.That(panel.anchoredPosition.x, Is.EqualTo(stamina.anchoredPosition.x));
        Assert.That(prefab.transform.GetChild(prefab.transform.childCount - 1).name, Is.EqualTo("DamageFlash"));
    }

    [Test]
    public void AlbularyoScreenHasDialogueOfferingsTurnedInAndReward()
    {
        AlbularyoScreen screen = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/PREFABS/AlbularyoScreen.prefab").GetComponent<AlbularyoScreen>();
        Assert.That(screen, Is.Not.Null);
        Assert.That(screen.IsOpen, Is.False, "The screen starts closed.");
        Font font = AssetDatabase.LoadAssetAtPath<Font>(FontPath);
        Assert.That(screen.GetComponentsInChildren<Text>(true).All(text => text.font == font), Is.True, "Every text uses blood_crow.");
        Button[] buttons = { screen.GoldButton, screen.SilverButton, screen.BronzeButton };
        foreach (Button button in buttons)
        {
            Assert.That(button, Is.Not.Null);
            Assert.That(button.GetComponentInChildren<Text>(true).text, Is.EqualTo("Sacrifice"));
            Assert.That(button.GetComponent<HoverRevealGraphic>(), Is.Not.Null);
        }
        Assert.That(screen.GoldCountText, Is.Not.Null);
        Assert.That(screen.SilverCountText, Is.Not.Null);
        Assert.That(screen.BronzeCountText, Is.Not.Null);
        Assert.That(screen.DialogueText, Is.Not.Null);
        Assert.That(screen.Dialogue, Is.SameAs(AssetDatabase.LoadAssetAtPath<AlbularyoDialogue>("Assets/Settings/AlbularyoDialogue.asset")));
        Assert.That(screen.Dialogue.requestLines.Count(line => !string.IsNullOrWhiteSpace(line)), Is.GreaterThan(1));
        Assert.That(screen.Dialogue.quotaMetLines.Count(line => !string.IsNullOrWhiteSpace(line)), Is.GreaterThan(0));
        Image portrait = screen.PortraitImage;
        Assert.That(portrait, Is.Not.Null, "The dialogue box has a bust image.");
        Assert.That(portrait.sprite, Is.Not.Null, "The placeholder bust uses the player's front sprite.");
        Assert.That(portrait.raycastTarget, Is.False);
        Assert.That(portrait.transform.parent.name, Is.EqualTo("DialoguePanel"));
        var portraitRect = (RectTransform)portrait.transform;
        Assert.That(portraitRect.anchorMin.x, Is.Zero, "The bust sits on the left of the dialogue box.");
        var dialogueRect = (RectTransform)screen.DialogueText.transform;
        var panelRect = (RectTransform)portrait.transform.parent;
        float textLeft = dialogueRect.anchorMin.x * panelRect.sizeDelta.x + dialogueRect.anchoredPosition.x - dialogueRect.pivot.x * dialogueRect.sizeDelta.x;
        Assert.That(textLeft, Is.GreaterThanOrEqualTo(portraitRect.anchoredPosition.x + portraitRect.sizeDelta.x), "The line starts right of the bust.");
        Assert.That(screen.RewardText.text, Is.EqualTo("Reward: ???"));
        Assert.That(screen.TurnedInText.text, Is.EqualTo("Turned in: 0x"));
        Assert.That(((RectTransform)screen.TurnedInText.transform).anchorMin.x, Is.Zero, "Turned in sits at the bottom left.");
        Assert.That(((RectTransform)screen.RewardText.transform).anchorMin.x, Is.EqualTo(1f), "Reward sits at the bottom right.");
        Assert.That(screen.GetComponentsInChildren<Image>(true).Any(image => image.sprite != null && image.sprite.name == "agimaticon"), Is.True);
    }

    [Test]
    public void DialogueLinesArePickedAtRandomAndFilledIn()
    {
        var lines = new System.Collections.Generic.List<string> { "A", "", "B" };
        for (int i = 0; i < 20; i++)
            Assert.That(AlbularyoDialogue.PickRandom(lines, "A"), Is.EqualTo("B"), "Never repeats the previous line or picks empty ones.");
        Assert.That(AlbularyoDialogue.PickRandom(new System.Collections.Generic.List<string> { "A" }, "A"), Is.EqualTo("A"));
        Assert.That(AlbularyoDialogue.PickRandom(new System.Collections.Generic.List<string>(), null), Is.Null);
        Assert.That(AlbularyoDialogue.Format("{0} more {3} ({2}/{1})", 3, 5, 2), Is.EqualTo("3 more agimats (2/5)"));
        Assert.That(AlbularyoDialogue.Format("{0} more {3}", 1, 5, 4), Is.EqualTo("1 more agimat"));
        Assert.That(AlbularyoDialogue.Format("Broken {line", 1, 5, 4), Is.EqualTo("Broken {line"));
    }

    [Test]
    public void BaseSceneHasTheBankAndTheAlbularyo()
    {
        Scene hub = EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity", OpenSceneMode.Additive);
        try
        {
            NetworkManager manager = InScene<NetworkManager>(hub).Single();
            Assert.That(manager.GetComponent<AgimatBank>(), Is.Not.Null, "The shared inventory persists with the NetworkManager.");
            AlbularyoInteractable albularyo = InScene<AlbularyoInteractable>(hub).Single();
            Assert.That(albularyo.ScreenPrefab, Is.Not.Null);
            Nametag nametag = albularyo.GetComponent<Nametag>();
            Assert.That(nametag, Is.Not.Null);
            Assert.That(nametag.DisplayName, Is.EqualTo("Talk to the Albularyo."));
            Collider body = albularyo.GetComponentInChildren<Collider>();
            Assert.That(body != null && !body.isTrigger, Is.True, "The placeholder needs a solid collider to be aimed at.");
            Assert.That(albularyo.GetComponentsInChildren<MeshRenderer>(true), Is.Empty, "The capsule mesh is replaced by a sprite.");
            SpriteRenderer sprite = albularyo.GetComponentsInChildren<SpriteRenderer>(true).Single();
            Assert.That(sprite.sprite, Is.Not.Null);
            Assert.That(sprite.sprite.texture.name, Is.EqualTo("MaleFront"), "Placeholder art is the player's PNG.");
            Assert.That(sprite.GetComponent<BillboardSprite>(), Is.Not.Null);
            Assert.That(sprite.GetComponent<BillboardSprite>().Pitch, Is.False, "Upright like players.");
            Assert.That(albularyo.GetComponent<DistanceFadeSprite>(), Is.Not.Null);
        }
        finally
        {
            EditorSceneManager.CloseScene(hub, true);
        }
    }

    [Test]
    public void ManananggalSpritesPitchTowardTheViewer()
    {
        Assert.That(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/PREFABS/Enemies/Manananggal.prefab")
            .GetComponent<EnemyPresentation>().BillboardPitch, Is.True);
        Assert.That(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/PREFABS/Enemies/Tikbalang.prefab")
            .GetComponent<EnemyPresentation>().BillboardPitch, Is.False, "Ground enemies stay upright.");

        Vector3 camera = Vector3.zero;
        Vector3 above = new Vector3(3f, 10f, 10f);
        Quaternion rotation = EnemyPresentation.FullBillboardRotation(camera, above, 0f);
        Vector3 forward = rotation * Vector3.forward;
        Assert.That(Vector3.Angle(forward, above - camera), Is.LessThan(0.01f), "The sprite plane faces the camera exactly.");
        Assert.That((rotation * Vector3.up).y, Is.GreaterThan(0f), "It stays upright, not upside down.");
        Assert.That(Mathf.Abs((rotation * Vector3.right).y), Is.LessThan(0.0001f), "No roll.");

        Quaternion level = EnemyPresentation.FullBillboardRotation(camera, new Vector3(0f, 0f, 5f), 0f);
        Assert.That(Quaternion.Angle(level, Quaternion.identity), Is.LessThan(0.01f));
        Quaternion overhead = EnemyPresentation.FullBillboardRotation(camera, new Vector3(0f, 10f, 0f), 90f);
        Assert.That(Vector3.Angle(overhead * Vector3.forward, Vector3.up), Is.LessThan(0.01f), "Directly overhead still faces the camera.");
    }

    private static T[] InScene<T>(Scene scene) where T : Component =>
        scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<T>(true)).ToArray();
}
