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
        effects.pixelHeight = 32;
        yield return null;
        yield return null;
        Assert.That(RowColors(ReadPixels()), Is.GreaterThan(RowColors(pixelated) * 1.4f),
            "Increasing the exposed Pixel Height must produce finer world pixels at runtime.");
        effects.enabled = false;
        yield return null;
        yield return null;
        Assert.That(RowColors(ReadPixels()), Is.GreaterThan(80), "Unmarked/disabled cameras must not be pixelated.");
    }

    [UnityTest]
    public IEnumerator ReducedPixelationRenderersKeepFinerPixelsThanTheWorld()
    {
        effects.pixelHeight = 16;
        effects.enemyPixelDetail = 2;
        yield return null;
        yield return null;
        int world = RowColors(ReadPixels());
        Assert.That(world, Is.InRange(15, 30));

        GameObject quad = objects.OfType<GameObject>().Single(obj => obj.GetComponent<MeshFilter>() != null);
        ReducedPixelation marker = quad.AddComponent<ReducedPixelation>();
        yield return null;
        yield return null;
        int enemy = RowColors(ReadPixels());
        Assert.That(enemy, Is.GreaterThan(world * 1.5f), "Marked enemy renderers use a finer pixel grid.");
        Assert.That(enemy, Is.LessThan(80), "They are still pixelated, only less so.");

        effects.enemyPixelDetail = 1;
        yield return null;
        yield return null;
        Assert.That(RowColors(ReadPixels()), Is.InRange(15, 30), "Enemy Pixel Detail 1 matches the world.");
        effects.enemyPixelDetail = 2;

        GameObject wall = Track(GameObject.CreatePrimitive(PrimitiveType.Quad));
        wall.layer = 30;
        wall.transform.position = camera.transform.position + Vector3.forward * 2f;
        wall.transform.localScale = new Vector3(12f, 12f, 1f);
        Material wallMaterial = Track(new Material(Shader.Find("Universal Render Pipeline/Unlit")));
        wallMaterial.SetTexture("_BaseMap", texture);
        wall.GetComponent<Renderer>().sharedMaterial = wallMaterial;
        yield return null;
        yield return null;
        Assert.That(RowColors(ReadPixels()), Is.InRange(15, 30), "Hidden enemies do not sharpen the wall in front of them.");
        objects.Remove(wall);
        Object.DestroyImmediate(wall);

        Object.DestroyImmediate(marker);
        quad.SetActive(false);
        Texture2D spriteTexture = Track(new Texture2D(256, 4, TextureFormat.RGBA32, false, true));
        spriteTexture.wrapMode = TextureWrapMode.Clamp;
        spriteTexture.SetPixels(Enumerable.Range(0, 1024).Select(index =>
            Color.white * Mathf.Lerp(0.1f, 0.9f, (index % 256) / 255f) + Color.black).ToArray());
        spriteTexture.Apply();
        GameObject spriteObject = Track(new GameObject("Reduced pixelation sprite"));
        spriteObject.layer = 30;
        spriteObject.transform.position = camera.transform.position + Vector3.forward * 4f;
        spriteObject.transform.localScale = new Vector3(12f / 2.56f, 300f, 1f);
        SpriteRenderer sprite = spriteObject.AddComponent<SpriteRenderer>();
        sprite.sprite = Track(Sprite.Create(spriteTexture, new Rect(0, 0, 256, 4), Vector2.one * 0.5f, 100f));
        sprite.sharedMaterial = Track(new Material(Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default")));
        yield return null;
        yield return null;
        int coarseSprite = RowColors(ReadPixels());
        spriteObject.AddComponent<ReducedPixelation>();
        yield return null;
        yield return null;
        Assert.That(RowColors(ReadPixels()), Is.GreaterThan(coarseSprite * 1.5f), "Sprites (like the Tikbalang) are masked by their shape.");

        sprite.sharedMaterial = Track(new Material(Shader.Find("Capstone/Solid Sprite")));
        yield return null;
        yield return null;
        Assert.That(RowColors(ReadPixels()), Is.GreaterThan(coarseSprite * 1.5f),
            "Depth-writing Solid Sprite renderers still pass their own depth test in the reduced pixelation mask.");
    }

    [UnityTest]
    public IEnumerator SolidSpritesAreFoggedOnlyByTheAirInFrontOfThem()
    {
        effects.pixelation = false;
        fog.enabled.value = true;
        fog.density.value = 0.2f;
        fog.heightFalloff.value = 0f;
        fog.ambientColor.value = Color.black;
        fog.lightScattering.value = 0f;

        Texture2D white = Track(new Texture2D(4, 4, TextureFormat.RGBA32, false, true));
        white.SetPixels(Enumerable.Repeat(Color.white, 16).ToArray());
        white.Apply();
        GameObject spriteObject = Track(new GameObject("Solid sprite"));
        spriteObject.layer = 30;
        spriteObject.transform.position = camera.transform.position + Vector3.forward;
        SpriteRenderer sprite = spriteObject.AddComponent<SpriteRenderer>();
        sprite.sprite = Track(Sprite.Create(white, new Rect(0, 0, 4, 4), Vector2.one * 0.5f, 4f));

        sprite.sharedMaterial = Track(new Material(Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default")));
        yield return null;
        yield return null;
        float transparent = CenterBrightness(ReadPixels());

        sprite.sharedMaterial = Track(new Material(Shader.Find("Capstone/Solid Sprite")));
        yield return null;
        yield return null;
        float solid = CenterBrightness(ReadPixels());

        Assert.That(transparent, Is.LessThan(0.6f), "Depth-less sprites are fogged as if they were the wall 4 m behind them.");
        Assert.That(solid, Is.GreaterThan(0.7f), "Solid sprites only receive the fog of the 1 m in front of them.");
        Assert.That(solid, Is.LessThan(0.95f), "Fog still applies in front of the sprite.");
    }

    [UnityTest]
    public IEnumerator DistanceFadeBlendsSmoothlyUnderPixelationAndDarkensFarSprites()
    {
        effects.pixelation = true;
        objects.OfType<GameObject>().First(obj => obj.name == "Quad").SetActive(false);
        Texture2D white = Track(new Texture2D(4, 4, TextureFormat.RGBA32, false, true));
        white.SetPixels(Enumerable.Repeat(Color.white, 16).ToArray());
        white.Apply();
        GameObject spriteObject = Track(new GameObject("Faded solid sprite"));
        spriteObject.layer = 30;
        spriteObject.transform.position = camera.transform.position + Vector3.forward;
        spriteObject.transform.localScale = Vector3.one * 4f;
        SpriteRenderer sprite = spriteObject.AddComponent<SpriteRenderer>();
        sprite.sprite = Track(Sprite.Create(white, new Rect(0, 0, 4, 4), Vector2.one * 0.5f, 4f));
        Material solid = Track(new Material(Shader.Find("Capstone/Solid Sprite")));
        sprite.sharedMaterial = solid;

        yield return null;
        yield return null;
        Assert.That(CenterBrightness(ReadPixels()), Is.GreaterThan(0.99f));
        sprite.color = new Color(1f, 1f, 1f, 0.5f);
        yield return null;
        yield return null;
        Color32[] half = ReadPixels();
        Assert.That(CenterBrightness(half), Is.EqualTo(0.5f).Within(0.03f), "Renderer alpha blends.");
        Assert.That(Variance(half), Is.LessThan(0.0005f), "Pixelated fades are even, not a checker/dither pattern.");

        camera.gameObject.tag = "MainCamera";
        sprite.color = Color.white;
        spriteObject.transform.position = camera.transform.position + Vector3.forward * 20f;
        spriteObject.transform.localScale = Vector3.one * 60f;
        var fade = spriteObject.AddComponent<DistanceFadeSprite>();
        yield return null;
        yield return null;
        Assert.That(fade.Opacity, Is.LessThan(0.7f).And.GreaterThan(0.1f));
        Assert.That(fade.Brightness, Is.LessThan(0.5f));
        Assert.That(sprite.color.a, Is.EqualTo(fade.Opacity).Within(0.0001f));
        Assert.That(sprite.color.r, Is.EqualTo(fade.Brightness).Within(0.0001f), "Distant sprites are darkened.");
        Assert.That(sprite.sharedMaterial, Is.Not.SameAs(solid), "Faded sprites use a local transparent material copy.");
        Assert.That(sprite.sharedMaterial.GetFloat("_ZWrite"), Is.EqualTo(0f));
        Assert.That(sprite.sharedMaterial.GetShaderPassEnabled("DepthOnly"), Is.False);
        Assert.That(sprite.sharedMaterial.renderQueue, Is.EqualTo((int)RenderQueue.Transparent));
        Assert.That(solid.GetFloat("_ZWrite"), Is.EqualTo(1f), "The shared solid material is never modified.");
        Color32[] far = ReadPixels();
        float expected = fade.Opacity * (QualitySettings.activeColorSpace == ColorSpace.Linear
            ? Mathf.GammaToLinearSpace(fade.Brightness) : fade.Brightness);
        Assert.That(CenterBrightness(far), Is.EqualTo(expected).Within(0.02f));
        Assert.That(Variance(far), Is.LessThan(0.0005f));

        spriteObject.transform.position = camera.transform.position + Vector3.forward * 2f;
        yield return null;
        yield return null;
        Assert.That(fade.Opacity, Is.EqualTo(1f));
        Assert.That(fade.Brightness, Is.EqualTo(1f));
        Assert.That(sprite.sharedMaterial, Is.SameAs(solid), "Opaque sprites go back to the depth-writing material.");
        Assert.That(CenterBrightness(ReadPixels()), Is.GreaterThan(0.99f));

        spriteObject.transform.position = camera.transform.position + Vector3.forward * 28f;
        yield return null;
        fade.enabled = false;
        Assert.That(sprite.color, Is.EqualTo(Color.white), "Disabling the fade restores the sprite.");
        Assert.That(sprite.sharedMaterial, Is.SameAs(solid));
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
        fog.lightScattering.value = 0f;
        fog.localLightScattering.value = 1f;
        fog.localLightNearFade.value = 0f;
        yield return null;
        yield return null;
        Assert.That(CenterBrightness(ReadPixels()), Is.GreaterThan(darkFog + 0.05f),
            "A light within the fog must add in-scattered radiance, not just distance tint.");

        // Local lights use their own scattering, not the directional Light Scattering.
        fog.localLightScattering.value = 0f;
        fog.lightScattering.value = 1f;
        yield return null;
        yield return null;
        Assert.That(CenterBrightness(ReadPixels()), Is.LessThan(darkFog + 0.01f));

        // A flashlight at the lens: Near Fade removes the glowing blob in the air right in front of the camera.
        fog.lightScattering.value = 0f;
        fog.localLightScattering.value = 1f;
        lightObject.transform.SetPositionAndRotation(camera.transform.position, camera.transform.rotation);
        light.type = LightType.Spot;
        light.spotAngle = 60f;
        yield return null;
        yield return null;
        float unfaded = CenterBrightness(ReadPixels());
        fog.localLightNearFade.value = 3f;
        yield return null;
        yield return null;
        float faded = CenterBrightness(ReadPixels());
        Assert.That(faded, Is.LessThan(unfaded - 0.03f), "The air at the lamp must not glow brightest.");
        Assert.That(faded, Is.GreaterThan(darkFog + 0.003f), "The beam further out stays visible in the fog.");
    }

    [UnityTest]
    public IEnumerator FogCoversTheSkyButTheMoonShowsThroughByMoonFog()
    {
        effects.pixelation = false;
        Material previousSky = RenderSettings.skybox;
        try
        {
            Shader shader = Shader.Find("Capstone/Night Sky");
            Assert.That(shader, Is.Not.Null);
            Material sky = Track(new Material(shader));
            Texture2D white = Track(new Texture2D(4, 4, TextureFormat.RGBA32, false, true));
            white.SetPixels(Enumerable.Repeat(Color.white, 16).ToArray());
            white.Apply();
            sky.SetTexture("_MoonTex", white);
            sky.SetFloat("_MoonAzimuth", 180f);
            sky.SetFloat("_MoonElevation", 0f);
            sky.SetFloat("_MoonSize", 30f);
            sky.SetColor("_ZenithColor", Color.white);
            sky.SetColor("_HorizonColor", Color.white);
            sky.SetColor("_GroundColor", Color.white);
            RenderSettings.skybox = sky;
            camera.clearFlags = CameraClearFlags.Skybox;

            // Facing the quad: geometry hides the sky.
            yield return null;
            yield return null;
            Assert.That(CenterBrightness(ReadPixels()), Is.InRange(0.3f, 0.7f), "The world quad, not the sky, is drawn where geometry is.");

            // Facing away (-Z): the moon fills the centre, plain sky the corners.
            camera.transform.rotation = Quaternion.Euler(0f, 180f, 0f);
            yield return null;
            yield return null;
            Assert.That(ReadPixels()[5 * 256 + 5].r / 255f, Is.GreaterThan(0.9f), "Without fog the sky gradient is visible.");

            fog.enabled.value = true;
            fog.density.value = 0.2f;
            fog.heightFalloff.value = 0f;
            fog.ambientColor.value = Color.black;
            fog.lightScattering.value = 0f;
            fog.moonFog.value = 0f;
            yield return null;
            yield return null;
            Color32[] pixels = ReadPixels();
            Assert.That(CenterBrightness(pixels), Is.GreaterThan(0.9f), "Moon Fog 0 leaves the moon undimmed by dense fog.");
            Assert.That(pixels[5 * 256 + 5].r / 255f, Is.LessThan(0.1f), "The rest of the sky is fogged like distant geometry.");
            fog.moonFog.value = 1f;
            yield return null;
            yield return null;
            Assert.That(CenterBrightness(ReadPixels()), Is.LessThan(0.1f), "Moon Fog 1 hides the moon like the sky.");
            fog.moonFog.value = 0.5f;
            yield return null;
            yield return null;
            Assert.That(CenterBrightness(ReadPixels()), Is.InRange(0.3f, 0.75f));
        }
        finally
        {
            RenderSettings.skybox = previousSky;
        }
    }

    [UnityTest]
    public IEnumerator BloodMoonSwapsTheSkyAndTurnsTheMoonLightRed()
    {
        effects.pixelation = false;
        Material previousSky = RenderSettings.skybox;
        try
        {
            MoonSky.BloodMoon = false;
            Material normal = Track(new Material(Shader.Find("Capstone/Night Sky")));
            Material blood = Track(new Material(Shader.Find("Capstone/Night Sky")));
            Texture2D white = Track(new Texture2D(4, 4, TextureFormat.RGBA32, false, true));
            white.SetPixels(Enumerable.Repeat(Color.white, 16).ToArray());
            white.Apply();
            foreach (Material sky in new[] { normal, blood })
            {
                sky.SetTexture("_MoonTex", white);
                sky.SetFloat("_MoonAzimuth", 180f);
                sky.SetFloat("_MoonElevation", 0f);
                sky.SetFloat("_MoonSize", 30f);
            }
            blood.SetColor("_MoonColor", new Color(1f, 0.1f, 0.1f));

            GameObject lightObject = Track(new GameObject("Moon light"));
            lightObject.SetActive(false);
            Light light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.color = new Color(0.7f, 0.72f, 0.78f);
            light.intensity = 0.08f;
            MoonSky moon = lightObject.AddComponent<MoonSky>();
            var fields = typeof(MoonSky);
            fields.GetField("normalSky", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).SetValue(moon, normal);
            fields.GetField("bloodMoonSky", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).SetValue(moon, blood);
            lightObject.SetActive(true);
            Assert.That(RenderSettings.skybox, Is.SameAs(normal));
            Assert.That(light.color, Is.EqualTo(new Color(0.7f, 0.72f, 0.78f)));

            camera.clearFlags = CameraClearFlags.Skybox;
            camera.transform.rotation = Quaternion.Euler(0f, 180f, 0f);
            MoonSky.BloodMoon = true;
            Assert.That(RenderSettings.skybox, Is.SameAs(blood));
            Assert.That(light.color.r, Is.GreaterThan(light.color.g * 5f), "The blood moon's light is red.");
            Assert.That(light.intensity, Is.EqualTo(0.12f).Within(0.0001f));
            yield return null;
            yield return null;
            Color32 centre = ReadPixels()[72 * 256 + 128];
            Assert.That(centre.r, Is.GreaterThan(centre.g * 3), "The blood moon renders red.");

            MoonSky.BloodMoon = false;
            Assert.That(RenderSettings.skybox, Is.SameAs(normal));
            Assert.That(light.color, Is.EqualTo(new Color(0.7f, 0.72f, 0.78f)));
            Assert.That(light.intensity, Is.EqualTo(0.08f).Within(0.0001f));
        }
        finally
        {
            MoonSky.BloodMoon = false;
            RenderSettings.skybox = previousSky;
        }
    }
    [UnityTest]
    public IEnumerator TheMoonFollowsTheMoonLightAndKeepsFullResolutionUnderPixelation()
    {
        effects.pixelation = false;
        Material previousSky = RenderSettings.skybox;
        try
        {
            Material sky = Track(new Material(Shader.Find("Capstone/Night Sky")));
            Texture2D checker = Track(new Texture2D(16, 16, TextureFormat.RGBA32, false, true));
            checker.filterMode = FilterMode.Point;
            checker.wrapMode = TextureWrapMode.Clamp;
            checker.SetPixels(Enumerable.Range(0, 256).Select(index =>
                ((index % 16) + (index / 16)) % 2 == 0 ? Color.white : new Color(0.25f, 0.25f, 0.25f, 1f)).ToArray());
            checker.Apply();
            sky.SetTexture("_MoonTex", checker);
            sky.SetFloat("_MoonAzimuth", 90f);
            sky.SetFloat("_MoonElevation", 0f);
            sky.SetFloat("_MoonSize", 30f);
            sky.SetColor("_ZenithColor", Color.black);
            sky.SetColor("_HorizonColor", Color.black);
            sky.SetColor("_GroundColor", Color.black);
            RenderSettings.skybox = sky;
            camera.clearFlags = CameraClearFlags.Skybox;
            camera.transform.rotation = Quaternion.Euler(0f, 180f, 0f);

            // Material placement (+X) is off screen while facing -Z.
            yield return null;
            yield return null;
            Assert.That(CenterBrightness(ReadPixels()), Is.LessThan(0.05f));

            // A MoonSky light shining towards +Z puts the moon at -Z, in front of the camera.
            GameObject lightObject = Track(new GameObject("Aligned moon light"));
            Light light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 0f;
            MoonSky moon = lightObject.AddComponent<MoonSky>();
            Assert.That(Vector3.Angle(moon.MoonDirection, Vector3.back), Is.LessThan(0.01f));
            yield return null;
            yield return null;
            Color32[] full = ReadPixels();
            Assert.That(CenterBrightness(full), Is.GreaterThan(0.2f), "The moon follows its light, not the material angles.");
            int fullTransitions = Transitions(full, 72);
            Assert.That(fullTransitions, Is.GreaterThan(12), "The checkered moon has many cells across its centre row.");

            effects.pixelation = true;
            effects.pixelHeight = 16;
            yield return null;
            yield return null;
            Assert.That(Transitions(ReadPixels(), 72), Is.GreaterThanOrEqualTo(fullTransitions - 2),
                "World pixelation must not make the moon blocky.");

            // Rotating the light moves the moon away again; disabling MoonSky restores the material angles.
            lightObject.transform.rotation = Quaternion.Euler(0f, 90f, 0f);
            yield return null;
            yield return null;
            Assert.That(CenterBrightness(ReadPixels()), Is.LessThan(0.05f));
            moon.enabled = false;
            camera.transform.rotation = Quaternion.Euler(0f, 90f, 0f);
            yield return null;
            yield return null;
            Assert.That(CenterBrightness(ReadPixels()), Is.GreaterThan(0.2f), "Without MoonSky the material angles apply.");
        }
        finally
        {
            RenderSettings.skybox = previousSky;
        }
    }

    private static int Transitions(Color32[] pixels, int row)
    {
        int count = 0;
        for (int x = 1; x < 256; x++)
        {
            Color32 left = pixels[row * 256 + x - 1];
            Color32 right = pixels[row * 256 + x];
            if (Mathf.Abs(left.r - right.r) > 40)
                count++;
        }
        return count;
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
        fear.staticStrength = 0f;
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
    public IEnumerator PanicStaticIsFaintNoiseAndTranceOverridesBlurTintAndFadeOnlyThisCamera()
    {
        effects.pixelation = false;
        texture.SetPixels(Enumerable.Repeat(new Color(0.2f, 0.2f, 0.2f, 1f), 1024).ToArray());
        texture.Apply();
        yield return null;
        yield return null;
        Color32[] clear = ReadPixels();
        PlayerFearEffects fear = camera.gameObject.AddComponent<PlayerFearEffects>();
        fear.fovShudder = 0f;
        fear.buildUpSpeed = 1000f;
        fear.vignetteStrength = 0f;
        fear.verticalGrainStrength = 0f;
        fear.staticStrength = 0.35f;
        objects.OfType<GameObject>().Single(obj => obj.GetComponent<MeshFilter>() != null)
            .AddComponent<EnemyFearSource>();
        Physics.SyncTransforms();
        yield return new WaitForSeconds(0.02f);
        yield return null;
        yield return null;
        Assert.That(fear.OverlayIntensity, Is.GreaterThan(0.8f));
        Color32[] noisy = ReadPixels();
        Assert.That(Variance(noisy), Is.GreaterThan(Variance(clear) + 0.00002f), "Panic must add static.");
        float worst = 0f;
        for (int index = 0; index < noisy.Length; index++)
            worst = Mathf.Max(worst, Mathf.Abs(noisy[index].r - clear[index].r) / 255f);
        Assert.That(worst, Is.LessThan(0.12f), "Static must stay faint enough to see the street.");
        Assert.That(grain.intensity.value, Is.Zero, "Static must not mutate the shared Volume.");
        fear.enabled = false;

        effects.ScreenFade = Color.white;
        yield return null;
        yield return null;
        Assert.That(ReadPixels().Min(pixel => pixel.r), Is.GreaterThan(250));
        effects.ScreenFade = Color.clear;
        effects.TranceTint = new Color(1f, 0.35f, 0.05f, 1f);
        yield return null;
        yield return null;
        Color32 tinted = ReadPixels()[72 * 256 + 128];
        Assert.That(tinted.r, Is.GreaterThan(tinted.g + 20));
        Assert.That(tinted.g, Is.GreaterThan(tinted.b));
        effects.ClearScreenOverrides();
        yield return null;
        yield return null;
        Assert.That(Variance(ReadPixels()), Is.EqualTo(Variance(clear)).Within(0.00001f));
        Assert.That(ReadPixels()[72 * 256 + 128].r, Is.EqualTo(clear[72 * 256 + 128].r).Within(2));
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
        fear.staticStrength = 0f;
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
            Color32[] thinImage = ReadPixels();
            bool[] thinColumns = ScratchColumns(thinImage, clear);
            Assert.That(thinColumns.Count(changed => changed), Is.GreaterThan(0));
            for (int x = 0; x < 256; x++)
            {
                if (!thinColumns[x])
                    continue;
                for (int y = 0; y < 144; y++)
                {
                    int pixel = y * 256 + x;
                    Assert.That(Mathf.Abs(thinImage[pixel].r - clear[pixel].r), Is.GreaterThan(3),
                        $"Scratch column {x} must remain connected at row {y}, including both screen edges.");
                }
            }
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
