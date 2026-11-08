using System.Linq;
using Capstone.Audio;
using Mirror;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public sealed class NightAudioSetupTests
{
    private const string MixerPath = "Assets/Audio/GameAudioMixer.mixer";

    [Test]
    public void ClockTimeRunsFromTwelveToTwelve()
    {
        const float hour = 225f;
        Assert.That(RoundClockHud.TimeString(0f, hour, 12), Is.EqualTo("12:00"));
        Assert.That(RoundClockHud.TimeString(hour * 0.5f, hour, 12), Is.EqualTo("12:30"));
        Assert.That(RoundClockHud.TimeString(hour, hour, 12), Is.EqualTo("1:00"));
        Assert.That(RoundClockHud.TimeString(hour * 9f, hour, 12), Is.EqualTo("9:00"));
        Assert.That(RoundClockHud.TimeString(hour * 11.99f, hour, 12), Is.EqualTo("11:59"));
        Assert.That(RoundClockHud.TimeString(hour * 12f, hour, 12), Is.EqualTo("12:00"));
        Assert.That(RoundClockHud.TimeString(hour * 20f, hour, 12), Is.EqualTo("12:00"), "Clamped at midnight.");
    }

    [Test]
    public void EnemyCountsGrowEveryTwoQuotasUpToFourAndFavourLargeMaps()
    {
        var host = new GameObject("Spawner");
        try
        {
            EnemySpawner spawner = host.AddComponent<EnemySpawner>();
            Assert.That(spawner.MaxCountForRound(1), Is.EqualTo(1));
            Assert.That(spawner.MaxCountForRound(2), Is.EqualTo(1));
            Assert.That(spawner.MaxCountForRound(3), Is.EqualTo(2));
            Assert.That(spawner.MaxCountForRound(5), Is.EqualTo(3));
            Assert.That(spawner.MaxCountForRound(7), Is.EqualTo(4));
            Assert.That(spawner.MaxCountForRound(50), Is.EqualTo(4));
            Assert.That(spawner.ExtraChanceForBlocks(15), Is.GreaterThan(spawner.ExtraChanceForBlocks(5)));

            Assert.That(spawner.RollCount(1, 15, () => 0f), Is.EqualTo(1), "Round 1 never adds extras.");
            Assert.That(spawner.RollCount(7, 15, () => 0f), Is.EqualTo(4));
            Assert.That(spawner.RollCount(7, 15, () => 0.99f), Is.EqualTo(1), "Every extra is rolled.");
            float mid = (spawner.ExtraChanceForBlocks(5) + spawner.ExtraChanceForBlocks(15)) * 0.5f;
            Assert.That(spawner.RollCount(7, 15, () => mid), Is.EqualTo(4), "Large maps pass rolls small maps fail.");
            Assert.That(spawner.RollCount(7, 5, () => mid), Is.EqualTo(1));
        }
        finally
        {
            Object.DestroyImmediate(host);
        }
    }

    [Test]
    public void DropActionsUseGAndQ()
    {
        var actions = AssetDatabase.LoadAssetAtPath<InputActionAsset>("Assets/InputSystem_Actions.inputactions");
        foreach ((string name, string key) in new[] { ("Player/DropItem", "<Keyboard>/g"), ("Player/DropPassive", "<Keyboard>/q") })
        {
            InputAction action = actions.FindAction(name);
            Assert.That(action, Is.Not.Null, name);
            Assert.That(action.type, Is.EqualTo(InputActionType.Button));
            Assert.That(action.bindings.Select(binding => binding.path), Has.Member(key));
        }
    }

    [Test]
    public void MixerHasTheFiveChannelsWithExposedVolumes()
    {
        var mixer = AssetDatabase.LoadAssetAtPath<AudioMixer>(MixerPath);
        Assert.That(mixer, Is.Not.Null);
        foreach (string group in new[] { "Master", "SFX", "UI", "Music", "Jumpscare" })
            Assert.That(mixer.FindMatchingGroups(group).Any(match => match.name == group), Is.True, group);
        foreach (AudioChannel channel in System.Enum.GetValues(typeof(AudioChannel)))
            Assert.That(mixer.GetFloat(AudioManager.VolumeParameter(channel), out _), Is.True, $"{channel} volume is exposed.");
        Assert.That(AudioManager.LinearToDecibels(1f), Is.EqualTo(0f).Within(0.001f));
        Assert.That(AudioManager.LinearToDecibels(0f), Is.EqualTo(AudioManager.MinDecibels));
    }

    [Test]
    public void HudHasNightTransitionReadyListAndClockTime()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/PREFABS/PlayerHud.prefab");
        Assert.That(prefab.transform.GetChild(prefab.transform.childCount - 1).name, Is.EqualTo("DamageFlash"));

        NightTransitionScreen night = prefab.GetComponentInChildren<NightTransitionScreen>(true);
        Assert.That(night, Is.Not.Null);
        Assert.That(night.Blackout, Is.Not.Null);
        Assert.That(night.Label, Is.Not.Null);
        Assert.That(night.Label.font, Is.SameAs(AssetDatabase.LoadAssetAtPath<Font>("Assets/Fonts/blood_crow/bloodcrow.ttf")));
        Assert.That(night.Blackout.raycastTarget, Is.False);

        ReadyHud ready = prefab.GetComponentInChildren<ReadyHud>(true);
        Assert.That(ready, Is.Not.Null);
        var panel = (RectTransform)ready.Panel.transform;
        Assert.That(panel.anchorMin, Is.EqualTo(new Vector2(1f, 0.5f)), "Middle right of the screen.");
        Assert.That(ready.Panel.GetComponentInChildren<Image>(true).color.a, Is.LessThan(1f), "The panel is transparent.");
        Assert.That(ready.ListText, Is.Not.Null);

        RoundClockHud clock = prefab.GetComponentInChildren<RoundClockHud>(true);
        var time = (Text)new SerializedObject(clock).FindProperty("timeText").objectReferenceValue;
        Assert.That(time, Is.Not.Null);
        Assert.That(time.font.name, Does.Contain("DS-DIGI").IgnoreCase);
        Assert.That(time.transform.parent.name, Is.EqualTo("ClockPanel"), "Blinks with the clock sprite.");
    }

    [Test]
    public void PlayerPrefabHasSelectionCorpseAudioAndDropPrefab()
    {
        GameObject player = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/PREFABS/NetworkPlayer.prefab");
        PlayerCharacterSelection selection = player.GetComponent<PlayerCharacterSelection>();
        Assert.That(selection, Is.Not.Null);
        Assert.That(selection.Options.Length, Is.EqualTo(2));
        Assert.That(selection.Options.All(option => option.definition != null), Is.True);
        Assert.That(selection.Options.Select(option => option.displayName), Is.EquivalentTo(new[] { "Male", "Female" }));
        Assert.That(player.GetComponent<PlayerCorpse>(), Is.Not.Null);
        Assert.That(player.GetComponent<PlayerAudio>(), Is.Not.Null);
        GameObject drop = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/PREFABS/ItemPickup.prefab");
        Assert.That(drop.GetComponent<ItemPickup>(), Is.Not.Null);
        Assert.That(drop.GetComponent<ItemPickupInteractable>(), Is.Not.Null);
        Assert.That(new SerializedObject(player.GetComponent<PlayerInventory>()).FindProperty("itemPickupPrefab").objectReferenceValue,
            Is.Not.Null);
        Assert.That(PlayerHealth.LethalThreshold, Is.EqualTo(1), "Half a heart kills.");
    }

    [Test]
    public void EnemiesFloorsAndScenesHaveAudioHooks()
    {
        foreach (string enemy in new[] { "Tikbalang", "Manananggal" })
            Assert.That(AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/PREFABS/Enemies/{enemy}.prefab").GetComponent<EnemyAudio>(), Is.Not.Null, enemy);
        GroundSurface road = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefab/Streets Object/Path_Road.prefab").GetComponent<GroundSurface>();
        Assert.That(road, Is.Not.Null);
        Assert.That(road.Type, Is.EqualTo(GroundSurfaceType.Solid));

        Scene hub = EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity", OpenSceneMode.Additive);
        try
        {
            NetworkManager manager = InScene<NetworkManager>(hub).Single();
            AudioManager audio = manager.GetComponent<AudioManager>();
            Assert.That(audio, Is.Not.Null);
            Assert.That(audio.Mixer, Is.SameAs(AssetDatabase.LoadAssetAtPath<AudioMixer>(MixerPath)));
            foreach (AudioChannel channel in System.Enum.GetValues(typeof(AudioChannel)))
                Assert.That(audio.GroupFor(channel), Is.Not.Null, channel.ToString());
            Assert.That(manager.spawnPrefabs, Does.Contain(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/PREFABS/ItemPickup.prefab")));
            CharacterSelectScreen select = InScene<CharacterSelectScreen>(hub).Single();
            Assert.That(select.TitleText, Is.Not.Null);
            Assert.That(new SerializedObject(select).FindProperty("title").stringValue, Is.EqualTo("Select your character"));
            Assert.That(select.Buttons.Length, Is.EqualTo(2));
            Assert.That(select.Portraits.Length, Is.EqualTo(2), "Filled at runtime with each option's front sprite.");
            Assert.That(select.Portraits.All(portrait => portrait != null), Is.True);
        }
        finally
        {
            EditorSceneManager.CloseScene(hub, true);
        }
        Scene streets = EditorSceneManager.OpenScene("Assets/Scenes/StreetsTest.unity", OpenSceneMode.Additive);
        try
        {
            Assert.That(InScene<SceneMusic>(streets), Has.Length.EqualTo(1));
        }
        finally
        {
            EditorSceneManager.CloseScene(streets, true);
        }
    }

    private static T[] InScene<T>(Scene scene) where T : Component =>
        scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<T>(true)).ToArray();
}
