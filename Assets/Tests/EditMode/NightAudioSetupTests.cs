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
    public void EnemyCountsStartAtTwoPerTypeAndGainTwoEveryTwoQuotas()
    {
        var host = new GameObject("Spawner");
        try
        {
            EnemySpawner spawner = host.AddComponent<EnemySpawner>();
            Assert.That(spawner.RolledEnemyCount, Is.EqualTo(2));
            Assert.That(spawner.ExtrasForRound(1), Is.EqualTo(0));
            Assert.That(spawner.ExtrasForRound(2), Is.EqualTo(0));
            Assert.That(spawner.ExtrasForRound(3), Is.EqualTo(2), "Two quotas completed add two enemies.");
            Assert.That(spawner.ExtrasForRound(5), Is.EqualTo(4));

            Assert.That(spawner.DistributeCounts(1, 1, () => 0f), Is.EqualTo(new[] { 2 }), "Two of a type regardless of map size.");
            Assert.That(spawner.DistributeCounts(1, 3, () => 0f), Is.EqualTo(new[] { 4 }));
            Assert.That(spawner.DistributeCounts(2, 3, () => 0f), Is.EqualTo(new[] { 4, 2 }), "Extras go to random types.");
            Assert.That(spawner.DistributeCounts(2, 3, () => 0.99f), Is.EqualTo(new[] { 2, 4 }));
            Assert.That(spawner.DistributeCounts(1, 9, () => 0f), Is.EqualTo(new[] { 10 }), "+2 every 2 quotas keeps growing (no cap).");
            Assert.That(spawner.DistributeCounts(4, 21, () => 0f).Sum(), Is.EqualTo(14), "Capped at 14 enemies in total.");
            Assert.That(spawner.DistributeCounts(2, 9, () => 0f).Sum(), Is.EqualTo(12), "Below the cap extras still grow.");
            Assert.That(spawner.DistributeCounts(0, 5), Is.Empty);
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
        GroundSurface road = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefab/_Prefab Street Building/Path_Road.prefab").GetComponent<GroundSurface>();
        Assert.That(road, Is.Not.Null);
        Assert.That(road.Type, Is.EqualTo(GroundSurfaceType.Solid));

        Scene hub = EditorSceneManager.OpenScene("Assets/Scenes/Base.unity", OpenSceneMode.Additive);
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
        Scene streets = EditorSceneManager.OpenScene("Assets/Scenes/Streets.unity", OpenSceneMode.Additive);
        try
        {
            Assert.That(InScene<SceneMusic>(streets), Has.Length.EqualTo(1));
            AmbientSoundscape ambience = InScene<AmbientSoundscape>(streets).Single();
            Assert.That(ambience.Sets, Has.Length.EqualTo(4), "Crickets, wind, owls and wolves.");
            Assert.That(ambience.Sets.All(set => set.HasClips), Is.True);
            Assert.That(ambience.Distance.x, Is.GreaterThanOrEqualTo(20f), "Ambience plays far from the players.");
        }
        finally
        {
            EditorSceneManager.CloseScene(streets, true);
        }
        Scene menu = EditorSceneManager.OpenScene("Assets/Scenes/MainMenu.unity", OpenSceneMode.Additive);
        try
        {
            MenuMusic music = InScene<MenuMusic>(menu).Single();
            Assert.That(music.Theme, Is.SameAs(AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/OST/MainMenuTheme.wav")));
            Assert.That(music.MusicGroup, Is.Not.Null);
            Assert.That(music.MusicGroup.name, Is.EqualTo("Music"));
        }
        finally
        {
            EditorSceneManager.CloseScene(menu, true);
        }
    }

    [Test]
    public void SoundHooksHaveTheirClips()
    {
        GameObject player = Prefab("Assets/PREFABS/NetworkPlayer.prefab");
        AssertClips(player.GetComponent<PlayerAudio>(), "solidSteps", "dirtSteps", "damagedSound");
        AssertClips(player.GetComponent<PlayerFlashlight>(), "toggleSound");

        FireZone fire = Prefab("Assets/PREFABS/Items/MolotovFire.prefab").GetComponent<FireZone>();
        AssertClips(fire, "shatterSound", "igniteSound");
        Assert.That(fire.LoopSound, Is.Not.Null);
        Assert.That(fire.LoopSound.Clip, Is.Not.Null);

        foreach (string enemy in new[] { "Tikbalang", "Manananggal", "Santelmo", "Tiktik" })
        {
            GameObject prefab = Prefab($"Assets/PREFABS/Enemies/{enemy}.prefab");
            AssertClips(prefab.GetComponent<EnemyJumpscare>(), "jumpscareSound");
            Assert.That(prefab.GetComponent<EnemyAudio>().ChaseTheme, Is.Not.Null, enemy);
        }
        GameObject tikbalang = Prefab("Assets/PREFABS/Enemies/Tikbalang.prefab");
        AssertClips(tikbalang.GetComponent<EnemyAudio>(), "movementSounds", "chaseLoopForAll");

        var flaps = new SerializedObject(Prefab("Assets/PREFABS/Enemies/Manananggal.prefab").GetComponent<EnemyAudio>());
        AssertClips(flaps.targetObject, "movementSounds");
        Assert.That(flaps.FindProperty("movementEcho").floatValue, Is.GreaterThan(flaps.FindProperty("chaseMovementEcho").floatValue));
        Assert.That(flaps.FindProperty("chaseMovementVolume").floatValue, Is.LessThan(1f), "Flaps quieten during a chase.");

        GameObject tiktik = Prefab("Assets/PREFABS/Enemies/Tiktik.prefab");
        AssertClips(tiktik.GetComponent<TiktikAudio>(), "taps");
        Assert.That(new SerializedObject(tiktik.GetComponent<EnemyAudio>()).FindProperty("movementSounds.clips").arraySize, Is.Zero,
            "TiktikAudio plays the taps instead.");

        foreach (string path in new[] { "Assets/PREFABS/Enemies/Santelmo.prefab", "Assets/PREFABS/Enemies/SantelmoPocketChaser.prefab" })
        {
            GameObject prefab = Prefab(path);
            Assert.That(prefab.GetComponentInChildren<FireSound>(true).Clip, Is.Not.Null, path);
            Assert.That(prefab.GetComponentInChildren<IntervalSound>(true).Sounds.HasClips, Is.True, path);
        }
        Assert.That(Prefab("Assets/PREFABS/Enemies/SantelmoPocketChaser.prefab").GetComponentInChildren<IntervalSound>(true).Interval.y,
            Is.LessThan(Prefab("Assets/PREFABS/Enemies/Santelmo.prefab").GetComponentInChildren<IntervalSound>(true).Interval.x),
            "The voice repeats more often in the pocket.");

        WhiteLady lady = Prefab("Assets/PREFABS/Enemies/WhiteLady.prefab").GetComponent<WhiteLady>();
        AssertClips(lady, "laughSound", "vanishLaughSound", "jumpscareSound");
        Assert.That(lady.LaughSound.volume, Is.LessThan(lady.JumpscareSound.volume), "Subtle laughs, loud jumpscare.");

        foreach (string gate in new[] { "Gate 1 (Breakable)", "Gate 1 (Closed)", "Gate 2 (Breakable)", "Gate 2 (Closed)" })
        {
            EntranceObstacle obstacle = Prefab($"Assets/Prefab/_Prefab Walls & Gate/Gates/{gate}.prefab").GetComponent<EntranceObstacle>();
            Assert.That(obstacle.ClearSound.HasClips, Is.True, gate);
        }
    }

    [Test]
    public void SoundLevelsFollowDistanceAndPocketTime()
    {
        TiktikAudio taps = Prefab("Assets/PREFABS/Enemies/Tiktik.prefab").GetComponent<TiktikAudio>();
        Assert.That(taps.IntervalAt(1f), Is.LessThan(taps.IntervalAt(10f)));
        Assert.That(taps.IntervalAt(10f), Is.LessThan(taps.IntervalAt(30f)), "Taps speed up as it closes in.");

        WhiteLady lady = Prefab("Assets/PREFABS/Enemies/WhiteLady.prefab").GetComponent<WhiteLady>();
        Assert.That(lady.LaughIntervalScale(3f), Is.LessThan(1f), "She laughs more often up close.");
        Assert.That(lady.LaughIntervalScale(30f), Is.EqualTo(1f));

        PlayerTrance trance = Prefab("Assets/PREFABS/NetworkPlayer.prefab").GetComponent<PlayerTrance>();
        trance.PocketFireLevels(0f, out float startVolume, out float startEcho);
        trance.PocketFireLevels(50f, out float peakVolume, out float peakEcho);
        trance.PocketFireLevels(60f, out float endVolume, out float endEcho);
        Assert.That(peakVolume, Is.GreaterThan(startVolume), "The fire swells in the pocket.");
        Assert.That(peakEcho, Is.GreaterThan(startEcho));
        Assert.That(endVolume, Is.LessThan(startVolume), "It falls away as the screen whitens.");
        Assert.That(endEcho, Is.LessThan(peakEcho));
    }

    private static GameObject Prefab(string path) =>
        AssetDatabase.LoadAssetAtPath<GameObject>(path) ?? throw new AssertionException("Missing " + path);

    private static void AssertClips(Object component, params string[] fields)
    {
        Assert.That(component, Is.Not.Null);
        var so = new SerializedObject(component);
        foreach (string field in fields)
        {
            SerializedProperty clips = so.FindProperty(field + ".clips");
            Assert.That(clips, Is.Not.Null, $"{component.GetType().Name}.{field}");
            Assert.That(clips.arraySize, Is.GreaterThan(0), $"{component.name} {field} has no clips.");
            for (int index = 0; index < clips.arraySize; index++)
                Assert.That(clips.GetArrayElementAtIndex(index).objectReferenceValue, Is.Not.Null, $"{component.name} {field}[{index}]");
        }
    }

    private static T[] InScene<T>(Scene scene) where T : Component =>
        scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<T>(true)).ToArray();
}
