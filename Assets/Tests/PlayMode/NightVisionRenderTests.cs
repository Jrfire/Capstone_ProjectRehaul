using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Mirror;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.TestTools;
using UnityEngine.UI;

public sealed class NightVisionRenderTests
{
    private readonly List<Object> objects = new List<Object>();
    private Camera camera;
    private PlayerCameraEffects effects;
    private RenderTexture target;
    private Texture2D texture;
    private NightFog fog;
    private FilmGrain grain;

    [SetUp]
    public void Setup()
    {
        GameObject cameraObject = Track(new GameObject("Night vision test camera"));
        camera = cameraObject.AddComponent<Camera>();
        camera.transform.position = new Vector3(1000f, 0f, 0f);
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = Color.black;
        camera.cullingMask = 1 << 30;
        camera.nearClipPlane = 0.05f;
        camera.farClipPlane = 40f;
        effects = cameraObject.AddComponent<PlayerCameraEffects>();
        effects.pixelHeight = 16;
        UniversalAdditionalCameraData data = camera.GetUniversalAdditionalCameraData();
        data.volumeLayerMask = 1 << 30;
        data.renderPostProcessing = true;
        data.requiresDepthTexture = true;
        target = Track(new RenderTexture(256, 144, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear));
        target.Create();
        camera.targetTexture = target;

        GameObject volumeObject = Track(new GameObject("Night vision test volume"));
        volumeObject.layer = 30;
        Volume volume = volumeObject.AddComponent<Volume>();
        volume.isGlobal = true;
        volume.priority = 1000f;
        VolumeProfile profile = Track(ScriptableObject.CreateInstance<VolumeProfile>());
        volume.sharedProfile = profile;
        fog = Track(profile.Add<NightFog>(true));
        fog.enabled.value = false;
        grain = Track(profile.Add<FilmGrain>(true));
        grain.intensity.value = 0f;
        profile.Add<Tonemapping>(true).mode.value = TonemappingMode.None;
        profile.Add<Bloom>(true).intensity.value = 0f;
        profile.Add<Vignette>(true).intensity.value = 0f;
        profile.Add<ColorAdjustments>(true).postExposure.value = 0f;
        foreach (VolumeComponent component in profile.components)
        {
            if (!objects.Contains(component))
                objects.Add(component);
        }

        GameObject quad = Track(GameObject.CreatePrimitive(PrimitiveType.Quad));
        quad.layer = 30;
        quad.transform.position = camera.transform.position + Vector3.forward * 4f;
        quad.transform.localScale = new Vector3(12f, 12f, 1f);
        Material material = Track(new Material(Shader.Find("Universal Render Pipeline/Unlit")));
        texture = Track(new Texture2D(1024, 1, TextureFormat.RGBA32, false, true));
        texture.wrapMode = TextureWrapMode.Clamp;
        Color[] gradient = Enumerable.Range(0, 1024).Select(index =>
            Color.white * Mathf.Lerp(0.1f, 0.9f, index / 1023f)).ToArray();
        texture.SetPixels(gradient);
        texture.Apply();
        material.SetTexture("_BaseMap", texture);
        quad.GetComponent<Renderer>().sharedMaterial = material;
    }

    [TearDown]
    public void TearDown()
    {
        camera.targetTexture = null;
        target.Release();
        for (int index = objects.Count - 1; index >= 0; index--)
            Object.DestroyImmediate(objects[index]);
        objects.Clear();
    }

    [UnityTest]
    public IEnumerator PixelationQuantizesTheRenderedWorldAndCanBeDisabled()
    {
        effects.pixelation = false;
        yield return null;
        yield return null;
        Color32[] fullResolution = ReadPixels();
        int originalColors = RowColors(fullResolution);
        effects.pixelation = true;
        yield return null;
        yield return null;
        Color32[] pixelated = ReadPixels();
        Assert.That(originalColors, Is.GreaterThan(80));
        Assert.That(RowColors(pixelated), Is.InRange(15, 30));
        Assert.That(pixelated[72 * 256 + 120], Is.EqualTo(pixelated[72 * 256 + 121]));
        effects.enabled = false;
        yield return null;
        yield return null;
        Assert.That(RowColors(ReadPixels()), Is.GreaterThan(80), "Unmarked/disabled cameras must not be pixelated.");
    }

    [UnityTest]
    public IEnumerator FogAttenuatesSurfacesAndLocalLightsScatterInsideTheVolume()
    {
        effects.pixelation = false;
        yield return null;
        yield return null;
        float clear = CenterBrightness(ReadPixels());
        fog.enabled.value = true;
        fog.density.value = 0.2f;
        fog.heightFalloff.value = 0f;
        fog.ambientColor.value = Color.black;
        fog.lightScattering.value = 0f;
        yield return null;
        yield return null;
        float darkFog = CenterBrightness(ReadPixels());
        Assert.That(darkFog, Is.LessThan(clear * 0.8f));

        GameObject lightObject = Track(new GameObject("Volumetric point light"));
        lightObject.transform.position = camera.transform.position + new Vector3(0.3f, 0f, 2f);
        Light light = lightObject.AddComponent<Light>();
        light.type = LightType.Point;
        light.intensity = 10f;
        light.range = 8f;
        fog.lightScattering.value = 1f;
        yield return null;
        yield return null;
        Assert.That(CenterBrightness(ReadPixels()), Is.GreaterThan(darkFog + 0.05f),
            "A light within the fog must add in-scattered radiance, not just distance tint.");
    }

    [UnityTest]
    public IEnumerator GrainStrengthChangesTheActualRenderedNoise()
    {
        effects.pixelation = false;
        texture.SetPixels(Enumerable.Repeat(new Color(0.5f, 0.5f, 0.5f, 1f), 1024).ToArray());
        texture.Apply();
        yield return null;
        yield return null;
        float smoothVariance = Variance(ReadPixels());
        grain.intensity.value = 1f;
        grain.response.value = 0f;
        yield return null;
        yield return null;
        Assert.That(Variance(ReadPixels()), Is.GreaterThan(smoothVariance + 0.00001f));
    }

    [Test]
    public void LobbyMessagesRoundTripUsingMirrorGeneratedSerializers()
    {
        const ulong lobbyId = 109212290963735762ul;
        using (NetworkWriterPooled writer = NetworkWriterPool.Get())
        {
            writer.Write(new SteamLobbyMessage { lobbyId = lobbyId });
            using (NetworkReaderPooled reader = NetworkReaderPool.Get(writer.ToArraySegment()))
                Assert.That(reader.Read<SteamLobbyMessage>().lobbyId, Is.EqualTo(lobbyId));
        }
    }

    [UnityTest]
    public IEnumerator PanicDarkensEdgesAndAddsVerticalScratchesWithoutChangingTheVolume()
    {
        effects.pixelation = false;
        texture.SetPixels(Enumerable.Repeat(new Color(0.5f, 0.5f, 0.5f, 1f), 1024).ToArray());
        texture.Apply();
        yield return null;
        yield return null;
        Color32[] clear = ReadPixels();
        PlayerFearEffects fear = camera.gameObject.AddComponent<PlayerFearEffects>();
        fear.fovShudder = 0f;
        fear.buildUpSpeed = 1000f;
        fear.recoverySpeed = 1000f;
        fear.verticalGrainStrength = 0f;
        GameObject quad = objects.OfType<GameObject>().Single(obj => obj.GetComponent<MeshFilter>() != null);
        quad.AddComponent<EnemyFearSource>();
        Physics.SyncTransforms();
        yield return new WaitForSeconds(0.02f);
        yield return null;
        yield return null;
        Assert.That(fear.CurrentIntensity, Is.GreaterThan(0.8f));
        Color32[] vignette = ReadPixels();
        int edge = 72 * 256 + 12;
        Assert.That(vignette[edge].r, Is.LessThan(clear[edge].r * 0.8f));
        Assert.That(CenterBrightness(vignette), Is.EqualTo(CenterBrightness(clear)).Within(0.02f));

        fear.vignetteStrength = 0f;
        fear.verticalGrainStrength = 1f;
        fear.verticalLineCount = 16f;
        fear.filmJitterSpeed = 0f;
        yield return null;
        yield return null;
        Color32[] scratches = ReadPixels();
        float horizontalDifference = 0f;
        float verticalDifference = 0f;
        for (int y = 10; y < 133; y++)
        {
            for (int x = 10; x < 245; x++)
            {
                int pixel = y * 256 + x;
                horizontalDifference += Mathf.Abs(scratches[pixel].r - scratches[pixel + 1].r);
                verticalDifference += Mathf.Abs(scratches[pixel].r - scratches[pixel + 256].r);
            }
        }
        Assert.That(Variance(scratches), Is.GreaterThan(Variance(clear) + 0.0001f));
        Assert.That(horizontalDifference, Is.GreaterThan(verticalDifference * 2f),
            "Scratches must be elongated vertically rather than horizontal scanlines.");
        Assert.That(grain.intensity.value, Is.Zero, "Panic must not mutate the shared Volume.");

        GameObject otherViewer = Track(new GameObject("Independent panic render viewer"));
        Camera otherCamera = otherViewer.AddComponent<Camera>();
        otherCamera.transform.SetPositionAndRotation(camera.transform.position, camera.transform.rotation);
        otherCamera.cullingMask = camera.cullingMask;
        otherCamera.clearFlags = CameraClearFlags.SolidColor;
        otherCamera.backgroundColor = Color.black;
        otherViewer.AddComponent<PlayerCameraEffects>().pixelHeight = 144;
        UniversalAdditionalCameraData otherData = otherCamera.GetUniversalAdditionalCameraData();
        otherData.volumeLayerMask = 1 << 30;
        otherData.renderPostProcessing = true;
        RenderTexture otherTarget = Track(new RenderTexture(256, 144, 24,
            RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear));
        otherTarget.Create();
        otherCamera.targetTexture = otherTarget;
        yield return null;
        yield return null;
        Assert.That(Variance(ReadPixels(otherTarget)), Is.EqualTo(Variance(clear)).Within(0.00001f),
            "The renderer material must not leak panic to another camera.");
        Assert.That(Variance(ReadPixels()), Is.GreaterThan(Variance(clear) + 0.0001f));
        otherCamera.targetTexture = null;
        otherTarget.Release();
        otherCamera.enabled = false;
        fear.enabled = false;
        yield return null;
        yield return null;
        Assert.That(Variance(ReadPixels()), Is.EqualTo(Variance(clear)).Within(0.00001f));
    }

    [UnityTest]
    public IEnumerator OverlayUiKeepsItsFullResolutionWhileWorldPixelationIsEnabled()
    {
        if (Application.isBatchMode)
            Assert.Ignore("Overlay screenshot validation requires Unity's non-batch end-of-frame stage.");
        camera.targetTexture = null;
        GameObject canvasObject = Track(new GameObject("Crisp overlay test", typeof(Canvas)));
        canvasObject.layer = 30;
        canvasObject.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
        GameObject imageObject = new GameObject("Gradient UI", typeof(RectTransform), typeof(RawImage));
        imageObject.layer = 30;
        imageObject.transform.SetParent(canvasObject.transform, false);
        RectTransform rect = imageObject.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        imageObject.GetComponent<RawImage>().texture = texture;
        PlayerFearEffects fear = camera.gameObject.AddComponent<PlayerFearEffects>();
        fear.fovShudder = 0f;
        fear.buildUpSpeed = 1000f;
        objects.OfType<GameObject>().Single(obj => obj.GetComponent<MeshFilter>() != null)
            .AddComponent<EnemyFearSource>();
        Physics.SyncTransforms();
        yield return new WaitForSeconds(0.02f);
        yield return null;
        yield return null;
        Assert.That(fear.CurrentIntensity, Is.GreaterThan(0.8f));
        Canvas.ForceUpdateCanvases();
        yield return new WaitForEndOfFrame();
        Texture2D screenshot = ScreenCapture.CaptureScreenshotAsTexture();
        try
        {
            Assert.That(screenshot, Is.Not.Null);
            int colors = screenshot.GetPixels32().Skip(screenshot.height / 2 * screenshot.width)
                .Take(screenshot.width).Select(pixel => pixel.r).Distinct().Count();
            Assert.That(colors, Is.GreaterThan(80),
                $"Overlay UI must remain crisp ({screenshot.width}x{screenshot.height}, canvas {rect.rect}).");
        }

        finally
        {
            Object.DestroyImmediate(screenshot);
        }
    }

    [UnityTest]
    public IEnumerator ScratchThicknessChangesRenderedWidthWithoutChangingLineCountOrPanic()
    {
        texture.SetPixels(Enumerable.Repeat(new Color(0.5f, 0.5f, 0.5f, 1f), 1024).ToArray());
        texture.Apply();
        PlayerFearEffects fear = camera.gameObject.AddComponent<PlayerFearEffects>();
        fear.fovShudder = 0f;
        fear.buildUpSpeed = 1000f;
        fear.vignetteStrength = 0f;
        fear.verticalGrainStrength = 1f;
        fear.verticalLineCount = 8f;
        fear.filmJitterSpeed = 0f;
        objects.OfType<GameObject>().Single(obj => obj.GetComponent<MeshFilter>() != null)
            .AddComponent<EnemyFearSource>();
        Physics.SyncTransforms();
        yield return new WaitForSeconds(0.02f);
        yield return null;
        float intensity = fear.CurrentIntensity;
        Assert.That(intensity, Is.GreaterThan(0.8f));
        foreach (bool pixelation in new[] { false, true })
        {
            effects.pixelation = pixelation;
            fear.verticalGrainStrength = 0f;
            yield return null;
            yield return null;
            Color32[] clear = ReadPixels();
            fear.verticalGrainStrength = 1f;
            fear.verticalLineThickness = 1f;
            yield return null;
            yield return null;
            bool[] originalColumns = ScratchColumns(ReadPixels(), clear);
            fear.verticalLineThickness = 0.5f;
            yield return null;
            yield return null;
            bool[] thinColumns = ScratchColumns(ReadPixels(), clear);
            Assert.That(thinColumns.Count(changed => changed), Is.GreaterThan(0));
            Assert.That(thinColumns.Count(changed => changed),
                Is.LessThan(originalColumns.Count(changed => changed) * 0.75f),
                "Half thickness must visibly reduce the width, even with world pixelation enabled.");
            Assert.That(ScratchGroups(thinColumns), Is.EqualTo(ScratchGroups(originalColumns)),
                "Thickness must not change the number of scratch bands.");
            Assert.That(fear.CurrentIntensity, Is.EqualTo(intensity));
            Assert.That(fear.verticalLineCount, Is.EqualTo(8f));
        }
    }

    private static bool[] ScratchColumns(Color32[] image, Color32[] clear)
    {
        bool[] columns = new bool[256];
        for (int x = 0; x < 256; x++)
        {
            for (int y = 0; y < 144; y++)
            {
                int pixel = y * 256 + x;
                if (Mathf.Abs(image[pixel].r - clear[pixel].r) > 3)
                {
                    columns[x] = true;
                    break;
                }
            }
        }
        return columns;
    }

    private static int ScratchGroups(bool[] columns)
    {
        int groups = 0;
        for (int x = 0; x < columns.Length; x++)
        {
            if (columns[x] && (x == 0 || !columns[x - 1]))
                groups++;
        }
        return groups;
    }

    [UnityTest]
    public IEnumerator DirectionalShadowsOccludeInScatteredLight()
    {
        effects.pixelation = false;
        fog.enabled.value = true;
        fog.density.value = 0.3f;
        fog.heightFalloff.value = 0f;
        fog.ambientColor.value = Color.black;
        fog.lightScattering.value = 1f;
        fog.anisotropy.value = 0f;
        GameObject moonObject = Track(new GameObject("Volumetric shadow test light"));
        moonObject.transform.rotation = Quaternion.Euler(0f, -45f, 0f);
        Light moon = moonObject.AddComponent<Light>();
        moon.type = LightType.Directional;
        moon.intensity = 0.5f;
        moon.shadows = LightShadows.Hard;
        yield return null;
        yield return null;
        float unoccluded = CenterBrightness(ReadPixels());
        GameObject cube = Track(GameObject.CreatePrimitive(PrimitiveType.Cube));
        cube.layer = 30;
        cube.transform.position = camera.transform.position + new Vector3(1f, 0f, 2f);
        cube.transform.localScale = new Vector3(1f, 3f, 1f);
        Material material = Track(new Material(Shader.Find("Universal Render Pipeline/Lit")));
        material.SetColor("_BaseColor", Color.black);
        cube.GetComponent<Renderer>().sharedMaterial = material;
        yield return null;
        yield return null;
        Assert.That(CenterBrightness(ReadPixels()), Is.LessThan(unoccluded - 0.01f),
            "Shadow-map occlusion must reduce volumetric radiance even on an unlit background surface.");
    }

    private T Track<T>(T instance) where T : Object
    {
        objects.Add(instance);
        return instance;
    }

    private Color32[] ReadPixels(RenderTexture surface = null)
    {
        RenderTexture previous = RenderTexture.active;
        Texture2D image = new Texture2D(256, 144, TextureFormat.RGBA32, false, true);
        try
        {
            RenderTexture.active = surface != null ? surface : target;
            image.ReadPixels(new Rect(0, 0, 256, 144), 0, 0);
            image.Apply();
            return image.GetPixels32();
        }
        finally
        {
            RenderTexture.active = previous;
            Object.DestroyImmediate(image);
        }
    }

    private static int RowColors(Color32[] pixels)
    {
        return pixels.Skip(72 * 256).Take(256).Select(pixel => pixel.r).Distinct().Count();
    }

    private static float CenterBrightness(Color32[] pixels)
    {
        return pixels[72 * 256 + 128].r / 255f;
    }

    private static float Variance(Color32[] pixels)
    {
        float average = pixels.Average(pixel => pixel.r / 255f);
        return pixels.Average(pixel => Mathf.Pow(pixel.r / 255f - average, 2f));
    }
}
