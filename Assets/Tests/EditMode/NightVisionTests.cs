using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public sealed class NightVisionTests
{
    [Test]
    public void PlayerCameraAndBothQualityRenderersAreWiredWithoutPixelatingTheCanvas()
    {
        GameObject player = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/PREFABS/NetworkPlayer.prefab");
        Camera camera = player.GetComponentInChildren<Camera>(true);
        Assert.That(camera.GetComponent<PlayerCameraEffects>().pixelation, Is.True);
        Assert.That(camera.GetComponent<PlayerCameraEffects>().pixelHeight, Is.GreaterThan(0),
            "Pixel Height is user-tuned; it only needs a valid virtual resolution.");
        Assert.That(camera.GetComponent<PlayerFearEffects>(), Is.Not.Null);
        Assert.That(camera.GetComponent<PlayerFearEffects>().verticalLineThickness, Is.InRange(0.05f, 2f));
        Assert.That(camera.farClipPlane, Is.GreaterThan(camera.nearClipPlane), "Far Clip Plane is user-tuned.");
        Assert.That(camera.GetUniversalAdditionalCameraData().renderPostProcessing, Is.True);
        Assert.That(camera.GetUniversalAdditionalCameraData().requiresDepthTexture, Is.True);
        Assert.That(player.GetComponentInChildren<Canvas>(true).renderMode, Is.EqualTo(RenderMode.ScreenSpaceOverlay));
        foreach (string name in new[] { "PC", "Mobile" })
        {
            var renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>($"Assets/Settings/{name}_Renderer.asset");
            NightVisionRendererFeature feature = renderer.rendererFeatures.OfType<NightVisionRendererFeature>().Single();
            Assert.That(feature.isActive, Is.True);
            Shader shader = (Shader)new SerializedObject(feature).FindProperty("shader").objectReferenceValue;
            Assert.That(shader, Is.Not.Null);
            Assert.That(ShaderUtil.ShaderHasError(shader), Is.False);
        }
    }

    [Test]
    public void SampleSceneHasAdjustableGrainVolumetricsAndNightLighting()
    {
        var scene = EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity", OpenSceneMode.Additive);
        try
        {
            Volume volume = scene.GetRootGameObjects().SelectMany(root =>
                root.GetComponentsInChildren<Volume>(true)).Single();
            Assert.That(volume.isGlobal, Is.True);
            Assert.That(volume.sharedProfile.TryGet(out NightFog fog), Is.True);
            Assert.That(fog.enabled.value, Is.True);
            Assert.That(fog.density.overrideState, Is.True);
            Assert.That(fog.density.value, Is.EqualTo(0.06f));
            Assert.That(fog.steps.value, Is.EqualTo(32));
            Assert.That(volume.sharedProfile.TryGet(out FilmGrain grain), Is.True);
            Assert.That(grain.intensity.overrideState, Is.True);
            Assert.That(grain.intensity.value, Is.InRange(0f, 1f), "Grain intensity is user-tuned within Unity's range.");
            Assert.That(volume.sharedProfile.TryGet(out ColorAdjustments color), Is.True);
            Assert.That(color.postExposure.value, Is.LessThan(-0.9f), "The endless night is very dark.");
            Assert.That(Mathf.Abs(color.colorFilter.value.b - color.colorFilter.value.r), Is.LessThan(0.05f),
                "The night palette is neutral rather than tinted blue.");
            Assert.That(System.IO.File.ReadAllText("Assets/Scenes/SampleScene.unity"),
                Does.Contain("m_AmbientSkyColor: {r: 0.005, g: 0.005, b: 0.006, a: 1}"));
            string streets = System.IO.File.ReadAllText("Assets/Scenes/StreetsTest.unity");
            Assert.That(streets, Does.Contain("m_AmbientSkyColor: {r: 0.0015, g: 0.0015, b: 0.0018, a: 1}"),
                "The streets are darker than the base so flashlights matter.");
            foreach (string path in new[] { "Assets/Scenes/SampleScene.unity", "Assets/Scenes/StreetsTest.unity" })
            {
                string text = System.IO.File.ReadAllText(path);
                Assert.That(text, Does.Contain("m_DefaultReflectionMode: 1"), path);
                Assert.That(text, Does.Contain("m_ReflectionIntensity: 0\n"), path + " has no grey default reflections.");
            }
            Light moon = scene.GetRootGameObjects().SelectMany(root =>
                root.GetComponentsInChildren<Light>(true)).Single(light => light.type == LightType.Directional);
            Assert.That(moon.intensity, Is.EqualTo(0.08f).Within(0.0001f));
            Assert.That(moon.color.b - moon.color.r, Is.LessThan(0.1f));
            Assert.That(moon.shadows, Is.Not.EqualTo(LightShadows.None));
        }

        finally
        {
            EditorSceneManager.CloseScene(scene, true);
        }
    }

    [Test]
    public void FearSourcesAreWiredAndTheTikbalangHasDistanceControls()
    {
        // The EnemyFearTest cube was removed from SampleScene once the Tikbalang existed; any remaining sources must be valid.
        var scene = EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity", OpenSceneMode.Additive);
        try
        {
            foreach (EnemyFearSource sceneSource in scene.GetRootGameObjects().SelectMany(root =>
                         root.GetComponentsInChildren<EnemyFearSource>(true)))
                Assert.That(sceneSource.visibilityRenderer, Is.Not.Null, sceneSource.name);
        }
        finally
        {
            EditorSceneManager.CloseScene(scene, true);
        }

        GameObject tikbalang = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/PREFABS/Enemies/Tikbalang.prefab");
        EnemyFearSource source = tikbalang.GetComponent<EnemyFearSource>();
        Assert.That(source, Is.Not.Null);
        Assert.That(source.visibilityRenderer, Is.Not.Null);
        Assert.That(source.fullIntensityDistance, Is.EqualTo(2f));
        Assert.That(source.maxDistance, Is.EqualTo(25f));
        Assert.That(source.StrengthAtDistance(2f), Is.EqualTo(1f));
        Assert.That(source.StrengthAtDistance(13.5f), Is.EqualTo(0.5f));
        Assert.That(source.StrengthAtDistance(25f), Is.Zero);
    }
}
