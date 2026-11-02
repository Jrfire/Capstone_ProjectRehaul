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
            Assert.That(color.postExposure.value, Is.LessThan(0f));
            Light moon = scene.GetRootGameObjects().SelectMany(root =>
                root.GetComponentsInChildren<Light>(true)).Single(light => light.type == LightType.Directional);
            Assert.That(moon.intensity, Is.EqualTo(0.25f));
            Assert.That(moon.shadows, Is.Not.EqualTo(LightShadows.None));
        }

        finally
        {
            EditorSceneManager.CloseScene(scene, true);
        }
    }

    [Test]
    public void SampleSceneHasAVisibleFearTestObjectWithDistanceControls()
    {
        var scene = EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity", OpenSceneMode.Additive);
        try
        {
            EnemyFearSource source = scene.GetRootGameObjects().SelectMany(root =>
                root.GetComponentsInChildren<EnemyFearSource>(true)).Single();
            Assert.That(source.name, Is.EqualTo("EnemyFearTest"));
            Assert.That(source.visibilityRenderer, Is.EqualTo(source.GetComponent<Renderer>()));
            Assert.That(source.visibilityRenderer.enabled, Is.True);
            Assert.That(source.GetComponent<Collider>(), Is.Not.Null);
            Assert.That(source.GetComponent<MeshFilter>().sharedMesh, Is.Not.Null);
            Assert.That(source.fullIntensityDistance, Is.EqualTo(2f));
            Assert.That(source.maxDistance, Is.EqualTo(25f));
            Assert.That(source.StrengthAtDistance(2f), Is.EqualTo(1f));
            Assert.That(source.StrengthAtDistance(13.5f), Is.EqualTo(0.5f));
            Assert.That(source.StrengthAtDistance(25f), Is.Zero);
        }
        finally
        {
            EditorSceneManager.CloseScene(scene, true);
        }
    }
}
