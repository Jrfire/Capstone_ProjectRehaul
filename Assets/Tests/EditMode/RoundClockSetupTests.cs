using System.Linq;
using Mirror;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public sealed class RoundClockSetupTests
{
    [Test]
    public void ClockFramesFollowTheSheetFromNoonToMidnight()
    {
        const float hour = 225f; // 45 minutes / 12
        Assert.That(45f * 60f / 12f, Is.EqualTo(hour));
        Assert.That(RoundClock.FrameFor(0f, hour, 12), Is.Zero, "Noon shows the 12 o'clock frame.");
        Assert.That(RoundClock.FrameFor(hour - 0.01f, hour, 12), Is.Zero);
        Assert.That(RoundClock.FrameFor(hour, hour, 12), Is.EqualTo(1));
        Assert.That(RoundClock.FrameFor(hour * 9f, hour, 12), Is.EqualTo(9));
        Assert.That(RoundClock.FrameFor(hour * 12f - 0.01f, hour, 12), Is.EqualTo(11));
        Assert.That(RoundClock.FrameFor(hour * 12f, hour, 12), Is.Zero, "Midnight shows 12 again.");
        Assert.That(RoundClock.FrameFor(hour * 20f, hour, 12), Is.Zero);

        Assert.That(RoundClock.IsLowTimeFor(8, 12, 3), Is.False);
        Assert.That(RoundClock.IsLowTimeFor(9, 12, 3), Is.True, "Blinking starts at 9 o'clock (3 hours left).");
        Assert.That(RoundClock.IsLowTimeFor(12, 12, 3), Is.True);
    }

    [Test]
    public void TravelTimeTextDescribesClockHours()
    {
        Assert.That(TravelTimeManager.FormatHours(1f), Is.EqualTo("Travel Time: 1 hour"));
        Assert.That(TravelTimeManager.FormatHours(2f), Is.EqualTo("Travel Time: 2 hours"));
        Assert.That(TravelTimeManager.FormatHours(1.5f), Is.EqualTo("Travel Time: 1.5 hours"));
        Assert.That(TravelTimeManager.FormatHours(0f), Is.EqualTo("Travel Time: None"));
        Assert.That(RoundClockHud.CountdownText(true, 29.2f, false), Is.EqualTo("Midnight! Get back to the jeepney: 30\nPress E on the back of the jeepney"));
        Assert.That(RoundClockHud.CountdownText(false, 3f, true), Does.EndWith("Waiting for the others..."));
    }

    [Test]
    public void BaseSceneHasTheClockAndTravelCosts()
    {
        Scene hub = EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity", OpenSceneMode.Additive);
        try
        {
            NetworkManager manager = InScene<NetworkManager>(hub).Single();
            RoundClock clock = manager.GetComponent<RoundClock>();
            Assert.That(clock, Is.Not.Null, "The round clock lives on the persistent NetworkManager.");
            Assert.That(clock.BaseSceneName, Is.EqualTo("SampleScene"));
            Assert.That(clock.RoundSeconds, Is.EqualTo(45f * 60f));
            Assert.That(clock.ClockHours, Is.EqualTo(12));
            Assert.That(clock.HourSeconds, Is.EqualTo(225f));
            Assert.That(clock.LowTimeHours, Is.EqualTo(3));
            Assert.That(clock.ReturnCountdownSeconds, Is.EqualTo(30f));

            TravelTimeManager costs = InScene<TravelTimeManager>(hub).Single();
            Assert.That(costs.TryGetCost("StreetsTest", out float hours), Is.True);
            Assert.That(hours, Is.GreaterThan(0f), "The StreetsTest cost is user-tunable.");
            Assert.That(costs.TryGetCost("Elsewhere", out _), Is.False);
        }
        finally
        {
            EditorSceneManager.CloseScene(hub, true);
        }
    }

    [Test]
    public void HudShowsTheClockTopRightWithTwelveOrderedFrames()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/PREFABS/PlayerHud.prefab");
        RoundClockHud hud = prefab.GetComponent<RoundClockHud>();
        Assert.That(hud, Is.Not.Null);
        var serialized = new SerializedObject(hud);
        SerializedProperty sprites = serialized.FindProperty("hourSprites");
        Assert.That(sprites.arraySize, Is.EqualTo(12));
        for (int i = 0; i < 12; i++)
            Assert.That(sprites.GetArrayElementAtIndex(i).objectReferenceValue.name, Is.EqualTo("ClockUI-Sheet_" + i));

        Assert.That(hud.ClockPanel.anchorMin, Is.EqualTo(new Vector2(1f, 1f)), "Top right, opposite the health and stamina.");
        Assert.That(hud.ClockPanel.anchorMax, Is.EqualTo(new Vector2(1f, 1f)));
        Assert.That(hud.ClockImage.raycastTarget, Is.False);
        Assert.That(hud.CountdownLabel, Is.Not.Null);
        Assert.That(hud.GameOverPanel.GetComponent<Canvas>().sortingOrder, Is.GreaterThan(500));
        Assert.That(prefab.transform.GetChild(prefab.transform.childCount - 1).name, Is.EqualTo("DamageFlash"));
        PlayerDeathScreen death = prefab.GetComponentInChildren<PlayerDeathScreen>(true);
        Assert.That(death, Is.Not.Null);
        Assert.That(death.Blackout, Is.Not.Null);
        Assert.That(death.JumpscareImage, Is.Not.Null);
        Assert.That(death.SpectatingLabel, Is.Not.Null);
        int deathOrder = death.GetComponent<Canvas>().sortingOrder;
        Assert.That(death.GetComponent<Canvas>().overrideSorting, Is.True);
        Assert.That(deathOrder, Is.GreaterThan(500).And.LessThan(hud.GameOverPanel.GetComponent<Canvas>().sortingOrder),
            "The death screen covers the damage flash but not the game over screen.");

        TravelMapScreen screen = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/PREFABS/TravelMapScreen.prefab").GetComponent<TravelMapScreen>();
        Text label = (Text)new SerializedObject(screen).FindProperty("travelTimeLabel").objectReferenceValue;
        Assert.That(label, Is.Not.Null);
        Assert.That(label, Is.Not.SameAs(screen.EnemiesLabel));
    }

    private static T[] InScene<T>(Scene scene) where T : Component =>
        scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<T>(true)).ToArray();
}
