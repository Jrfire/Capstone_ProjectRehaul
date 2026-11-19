using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

public sealed class MainMenuBackgroundTests
{
    private const string MenuScenePath = "Assets/Scenes/MainMenu.unity";

    [TearDown]
    public void TearDown()
    {
        Time.timeScale = 1f;
        SteamBootstrap.SkipSteamInitialization = false;
    }

    [UnityTest]
    public IEnumerator MenuBackgroundPlaysEveryShotWithFadesAndJumpscareBursts()
    {
        SteamBootstrap.SkipSteamInitialization = true;
        AsyncOperation load = EditorSceneManager.LoadSceneAsyncInPlayMode(MenuScenePath, new LoadSceneParameters(LoadSceneMode.Additive));
        while (!load.isDone)
            yield return null;
        Scene menu = SceneManager.GetSceneByPath(MenuScenePath);
        try
        {
            yield return null;
            yield return null;
            MainMenuBackground background = menu.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<MainMenuBackground>(true)).Single();
            Assert.That(background.AvailableShots, Is.EquivalentTo(new[]
            {
                MainMenuBackground.Shot.StreetPan, MainMenuBackground.Shot.TikbalangChase,
                MainMenuBackground.Shot.ManananggalMoon, MainMenuBackground.Shot.SantelmoPocket,
            }), "The street builds and the corridor finds a straight block.");
            Assert.That(background.HasCorridor, Is.True);
            Assert.That(menu.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<MainMenuScreen>(true)).Count(), Is.EqualTo(1), "The menu UI stays.");

            Time.timeScale = 4f;
            var seen = new System.Collections.Generic.HashSet<MainMenuBackground.Shot>();
            foreach (MainMenuBackground.Shot shot in background.AvailableShots.ToArray())
            {
                background.NextShotOverride = shot;
                float limit = Time.realtimeSinceStartup + 30f;
                while (background.CurrentShot != shot && Time.realtimeSinceStartup < limit)
                    yield return null;
                Assert.That(background.CurrentShot, Is.EqualTo(shot));
                seen.Add(shot);
                yield return new WaitForSecondsRealtime(0.8f);
                if (shot != MainMenuBackground.Shot.StreetPan)
                {
                    SpriteRenderer creature = background.GetComponentsInChildren<SpriteRenderer>().FirstOrDefault();
                    Assert.That(creature, Is.Not.Null, $"{shot} shows its creature.");
                    Assert.That(creature.sprite, Is.Not.Null);
                    Assert.That(Camera.main.WorldToViewportPoint(creature.bounds.center).z, Is.GreaterThan(0f), $"{shot}: creature in front of the camera.");
                }
            }
            Assert.That(seen.Count, Is.EqualTo(4));
            Time.timeScale = 1f;

            Image jumpscare = menu.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<Image>(true)).Single(image => image.name == "Jumpscare");
            RawImage noise = menu.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<RawImage>(true)).Single(image => image.name == "Static");
            int bursts = background.BurstCount;
            int flashes = background.FlashCount;
            background.TriggerBurst();
            bool flashed = false;
            bool faceFramed = false;
            float maxStatic = 0f;
            float end = Time.realtimeSinceStartup + 2f;
            while (Time.realtimeSinceStartup < end)
            {
                flashed |= jumpscare.enabled && jumpscare.sprite != null;
                if (jumpscare.enabled)
                {
                    Assert.That(jumpscare.color.r, Is.LessThan(0.5f), "Jumpscares are darkened.");
                    RectTransform rect = jumpscare.rectTransform;
                    faceFramed |= rect.pivot != new Vector2(0.5f, 0.5f) && rect.anchoredPosition == Vector2.zero
                        && rect.rect.height >= ((RectTransform)rect.parent).rect.height * 1.2f;
                }
                maxStatic = Mathf.Max(maxStatic, noise.color.a);
                yield return null;
            }
            Assert.That(background.BurstCount, Is.GreaterThan(bursts));
            Assert.That(flashed, Is.True);
            Assert.That(background.FlashCount - flashes, Is.EqualTo(1), "Exactly one enemy flashes per burst.");
            Assert.That(faceFramed, Is.True, "The sprite is scaled up around its face focus, so only the face shows.");
            Assert.That(maxStatic, Is.GreaterThan(0.3f));
            Assert.That(noise.color.a, Is.EqualTo(0f).Within(0.01f), "Static fades out after the burst.");
        }
        finally
        {
            Time.timeScale = 1f;
            SceneManager.UnloadSceneAsync(menu);
        }
        yield return null;
    }

    [UnityTest]
    public IEnumerator MenuShotsPlayInShuffledRoundsWithoutRepeats()
    {
        SteamBootstrap.SkipSteamInitialization = true;
        AsyncOperation load = EditorSceneManager.LoadSceneAsyncInPlayMode(MenuScenePath, new LoadSceneParameters(LoadSceneMode.Additive));
        while (!load.isDone)
            yield return null;
        Scene menu = SceneManager.GetSceneByPath(MenuScenePath);
        try
        {
            yield return null;
            MainMenuBackground background = menu.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<MainMenuBackground>(true)).Single();
            int count = background.AvailableShots.Count;
            Assert.That(count, Is.EqualTo(4));
            var played = new System.Collections.Generic.List<MainMenuBackground.Shot>();
            Time.timeScale = 25f;
            int seen = -1;
            float limit = Time.realtimeSinceStartup + 90f;
            while (played.Count < count * 3 && Time.realtimeSinceStartup < limit)
            {
                if (background.ShotsPlayed != seen && background.CurrentShot.HasValue)
                {
                    seen = background.ShotsPlayed;
                    played.Add(background.CurrentShot.Value);
                }
                yield return null;
            }
            Time.timeScale = 1f;
            Assert.That(played.Count, Is.EqualTo(count * 3), string.Join(",", played));
            for (int round = 0; round < 3; round++)
                Assert.That(played.Skip(round * count).Take(count).Distinct().Count(), Is.EqualTo(count),
                    $"Every shot plays once per round: {string.Join(",", played)}");
            for (int i = 1; i < played.Count; i++)
                Assert.That(played[i], Is.Not.EqualTo(played[i - 1]), "Never the same shot twice in a row.");
        }
        finally
        {
            Time.timeScale = 1f;
            SceneManager.UnloadSceneAsync(menu);
        }
        yield return null;
    }

    [UnityTest]
    public IEnumerator LoadFadeGoesBlackBeforeLoadingThenFadesIn()
    {
        float alphaAtLoad = -1f;
        LoadFade fade = LoadFade.Run(() => alphaAtLoad = LoadFade.Active.Alpha, 0.2f, 0.2f);
        Assert.That(LoadFade.Run(() => Assert.Fail("A second fade must not start.")), Is.SameAs(fade));
        float limit = Time.realtimeSinceStartup + 5f;
        while (fade != null && Time.realtimeSinceStartup < limit)
            yield return null;
        Assert.That(alphaAtLoad, Is.EqualTo(1f).Within(0.001f), "The screen is fully black when the load runs.");
        Assert.That(fade == null, Is.True, "The overlay fades back in and removes itself.");
        Assert.That(LoadFade.Active, Is.Null);
    }
}