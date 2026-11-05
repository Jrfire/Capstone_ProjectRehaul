using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public sealed class HudContextSetupTests
{
    private const string FontPath = "Assets/Fonts/blood_crow/bloodcrow.ttf";

    private static Font BloodCrow => AssetDatabase.LoadAssetAtPath<Font>(FontPath);

    [Test]
    public void NametagHudIsOnThePlayerHudWithTheBloodCrowFont()
    {
        GameObject hud = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/PREFABS/PlayerHud.prefab");
        NametagHud nametag = hud.GetComponentInChildren<NametagHud>(true);
        Assert.That(nametag, Is.Not.Null);
        Assert.That(BloodCrow, Is.Not.Null);
        Assert.That(nametag.Font, Is.SameAs(BloodCrow));
        Assert.That(nametag.NameText, Is.Not.Null);
        Assert.That(nametag.PromptText, Is.Not.Null);
        Assert.That(nametag.NameText.font, Is.SameAs(BloodCrow));
        Assert.That(nametag.PromptText.font, Is.SameAs(BloodCrow));
        var rect = (RectTransform)nametag.transform;
        Assert.That(rect.anchorMin, Is.EqualTo(new Vector2(0.5f, 0f)), "The nametag sits near the bottom centre.");
        Assert.That(rect.anchorMax, Is.EqualTo(new Vector2(0.5f, 0f)));
        Assert.That(nametag.GetComponentsInChildren<Graphic>(true).All(graphic => !graphic.raycastTarget), Is.True);
    }

    [Test]
    public void PlayersHaveASyncedNameAndSampleSceneInteractablesHaveNametags()
    {
        GameObject player = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/PREFABS/NetworkPlayer.prefab");
        Assert.That(player.GetComponent<PlayerName>(), Is.Not.Null);
        Assert.That(player.GetComponent<Mirror.NetworkIdentity>().assetId, Is.EqualTo(1952560444u));

        Scene scene = EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity", OpenSceneMode.Additive);
        try
        {
            Interactable[] interactables = scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<Interactable>(true)).ToArray();
            Assert.That(interactables, Is.Not.Empty);
            foreach (Interactable interactable in interactables)
            {
                Nametag nametag = interactable.GetComponent<Nametag>();
                Assert.That(nametag, Is.Not.Null, interactable.name + " needs a Nametag.");
                Assert.That(nametag.DisplayName, Is.Not.Empty);
            }
        }
        finally
        {
            EditorSceneManager.CloseScene(scene, true);
        }
    }

    [Test]
    public void LoadingScreenShowsRandomTipsFromAnEditableAsset()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/PREFABS/TravelLoadingScreen.prefab");
        TravelLoadingScreen screen = prefab.GetComponent<TravelLoadingScreen>();
        LoadingTips tips = AssetDatabase.LoadAssetAtPath<LoadingTips>("Assets/Settings/LoadingTips.asset");
        Assert.That(tips, Is.Not.Null);
        Assert.That(screen.Tips, Is.SameAs(tips));
        Assert.That(tips.Count, Is.EqualTo(5));
        Assert.That(tips.tips.Count(tip => tip.StartsWith("TIP: ")), Is.EqualTo(3));
        Assert.That(tips.tips.Any(tip => tip.Contains("Manananggal")), Is.True);
        Assert.That(screen.TipText, Is.Not.Null);
        Assert.That(screen.TipText.font, Is.SameAs(BloodCrow));
        Assert.That(screen.TipText.transform.parent, Is.SameAs(screen.LoadingText.transform.parent));

        // Below "Loading..." and above the enemy row (both in the 1920x1080 reference canvas).
        var tip = (RectTransform)screen.TipText.transform;
        var loading = (RectTransform)screen.LoadingText.transform;
        float tipTop = 540f + tip.anchoredPosition.y + tip.sizeDelta.y * 0.5f;
        float tipBottom = 540f + tip.anchoredPosition.y - tip.sizeDelta.y * 0.5f;
        float loadingBottom = 540f + loading.anchoredPosition.y - loading.sizeDelta.y * 0.5f;
        float rowTop = screen.EnemyRow.anchoredPosition.y + screen.EnemyRow.sizeDelta.y * (1f - screen.EnemyRow.pivot.y);
        Assert.That(tipTop, Is.LessThanOrEqualTo(loadingBottom));
        Assert.That(tipBottom, Is.GreaterThanOrEqualTo(rowTop));
    }

    [Test]
    public void TravelMapButtonsRevealTheirBackgroundOnHover()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/PREFABS/TravelMapScreen.prefab");
        Button[] buttons = prefab.GetComponentsInChildren<Button>(true);
        Assert.That(buttons.Length, Is.GreaterThanOrEqualTo(4));
        foreach (Button button in buttons)
        {
            HoverRevealGraphic reveal = button.GetComponent<HoverRevealGraphic>();
            Assert.That(reveal, Is.Not.Null, button.name + " needs HoverRevealGraphic.");
            Assert.That(reveal.Target, Is.SameAs(button.targetGraphic));
            Assert.That(reveal.HoverAlpha, Is.EqualTo(button.targetGraphic.color.a).Within(0.001f));
        }
        Assert.That(prefab.GetComponentsInChildren<Text>(true).All(text => text.font == BloodCrow), Is.True);
    }

    [TestCase("Assets/Scenes/SampleScene.unity")]
    [TestCase("Assets/Scenes/StreetsTest.unity")]
    public void TheMoonLightShinesFromTheSkyMoon(string path)
    {
        Material sky = AssetDatabase.LoadAssetAtPath<Material>("Assets/Settings/NightSky.mat");
        float azimuth = sky.GetFloat("_MoonAzimuth") * Mathf.Deg2Rad;
        float elevation = sky.GetFloat("_MoonElevation") * Mathf.Deg2Rad;
        var materialMoon = new Vector3(Mathf.Sin(azimuth) * Mathf.Cos(elevation), Mathf.Sin(elevation),
            Mathf.Cos(azimuth) * Mathf.Cos(elevation));
        Scene scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
        try
        {
            MoonSky moon = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<MoonSky>(true)).Single();
            Assert.That(new SerializedObject(moon).FindProperty("alignMoonToLight").boolValue, Is.True);
            Assert.That(Vector3.Angle(moon.MoonDirection, materialMoon), Is.LessThan(0.5f),
                "The light keeps the moon where the material placed it before alignment.");
        }
        finally
        {
            EditorSceneManager.CloseScene(scene, true);
        }
    }
}
