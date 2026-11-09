using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

public sealed class NightVisionRendererFeature : ScriptableRendererFeature
{
    [SerializeField] private Shader shader;
    private Material fogMaterial;
    private Material pixelMaterial;
    private Material maskMaterial;
    private NightPass fogPass;
    private NightPass pixelPass;

    public override void Create()
    {
        CoreUtils.Destroy(fogMaterial);
        CoreUtils.Destroy(pixelMaterial);
        CoreUtils.Destroy(maskMaterial);
        if (shader == null)
        {
            Debug.LogError("NightVisionRendererFeature requires the Capstone Night Vision shader.");
            return;
        }
        fogMaterial = CoreUtils.CreateEngineMaterial(shader);
        pixelMaterial = CoreUtils.CreateEngineMaterial(shader);
        maskMaterial = CoreUtils.CreateEngineMaterial(shader);
        fogPass = new NightPass(fogMaterial, 0, RenderPassEvent.BeforeRenderingPostProcessing, null);
        pixelPass = new NightPass(pixelMaterial, 1, RenderPassEvent.AfterRenderingPostProcessing, maskMaterial);
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        Camera camera = renderingData.cameraData.camera;
        if (camera.cameraType != CameraType.Game ||
            !camera.TryGetComponent(out PlayerCameraEffects effects) || !effects.isActiveAndEnabled)
            return;
        if (fogMaterial == null || pixelMaterial == null)
        {
            Debug.LogError("Night vision materials are missing. Assign the shader on the renderer feature.");
            return;
        }
        NightFog fog = VolumeManager.instance.stack.GetComponent<NightFog>();
        if (fog != null && fog.active && fog.enabled.value && fog.density.value * effects.FogDensityMultiplier > 0f)
            renderer.EnqueuePass(fogPass);
        PlayerFearEffects fear = camera.GetComponent<PlayerFearEffects>();
        if (effects.pixelation || effects.HasScreenEffects || (fear != null && fear.CanAffectCamera && fear.OverlayIntensity > 0f))
            renderer.EnqueuePass(pixelPass);
    }

    protected override void Dispose(bool disposing)
    {
        CoreUtils.Destroy(fogMaterial);
        CoreUtils.Destroy(pixelMaterial);
        CoreUtils.Destroy(maskMaterial);
    }

    private sealed class NightPass : ScriptableRenderPass
    {
        private const int MaxLocalLights = 8;
        private const int MaskShaderPass = 2;
        private readonly Material material;
        private readonly Material maskMaterial;
        private readonly int shaderPass;
        private readonly Vector4[] lightPositions = new Vector4[MaxLocalLights];
        private readonly Vector4[] lightColors = new Vector4[MaxLocalLights];
        private readonly Vector4[] lightDirections = new Vector4[MaxLocalLights];
        private readonly List<Renderer> markedRenderers = new List<Renderer>();

        private sealed class PassData
        {
            public TextureHandle source;
            public TextureHandle mask;
            public Material material;
            public int shaderPass;
        }

        private sealed class MaskPassData
        {
            public TextureHandle depth;
            public Material material;
            public Matrix4x4 view;
            public Matrix4x4 projection;
            public readonly List<Renderer> renderers = new List<Renderer>();
        }

        public NightPass(Material material, int shaderPass, RenderPassEvent passEvent, Material maskMaterial)
        {
            this.material = material;
            this.maskMaterial = maskMaterial;
            this.shaderPass = shaderPass;
            renderPassEvent = passEvent;
            requiresIntermediateTexture = true;
            ConfigureInput(ScriptableRenderPassInput.Depth);
        }

        public override void RecordRenderGraph(RenderGraph graph, ContextContainer frameData)
        {
            UniversalResourceData resources = frameData.Get<UniversalResourceData>();
            UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
            if (resources.isActiveTargetBackBuffer)
            {
                Debug.LogError("Night vision requires an intermediate camera color texture.");
                return;
            }
            TextureHandle mask = TextureHandle.nullHandle;
            if (shaderPass == 0)
            {
                NightFog fog = VolumeManager.instance.stack.GetComponent<NightFog>();
                PlayerCameraEffects fogEffects = cameraData.camera.GetComponent<PlayerCameraEffects>();
                Color ambient = fog.ambientColor.value;
                float density = fog.density.value;
                if (fogEffects != null)
                {
                    // Per-camera override (e.g. the Santelmo trance's inferno), never written to the shared Volume.
                    ambient = Color.Lerp(ambient, fogEffects.FogAmbientOverride, fogEffects.FogAmbientOverride.a);
                    density *= fogEffects.FogDensityMultiplier;
                }
                material.SetVector("_FogDensityHeight", new Vector4(density,
                    fog.baseHeight.value, fog.heightFalloff.value,
                    Mathf.Min(fog.maxDistance.value, cameraData.camera.farClipPlane)));
                material.SetColor("_FogAmbient", ambient);
                material.SetVector("_FogLighting", new Vector4(fog.lightScattering.value,
                    fog.anisotropy.value, fog.steps.value, fog.moonFog.value));
                material.SetVector("_FogLocal", new Vector4(fog.localLightScattering.value,
                    Mathf.Max(0.001f, fog.localLightNearFade.value), 0f, 0f));
                SetMoon(RenderSettings.skybox);
                SetLights(frameData.Get<UniversalLightData>());
            }
            else
            {
                PlayerCameraEffects effects = cameraData.camera.GetComponent<PlayerCameraEffects>();
                int height = Mathf.Clamp(effects.pixelHeight, 1,
                    cameraData.cameraTargetDescriptor.height);
                int width = Mathf.Max(1, Mathf.RoundToInt(height *
                    (float)cameraData.cameraTargetDescriptor.width / cameraData.cameraTargetDescriptor.height));
                material.SetVector("_PixelGrid", new Vector4(width, height, effects.pixelation ? 1f : 0f, 0f));
                PlayerFearEffects fear = cameraData.camera.GetComponent<PlayerFearEffects>();
                bool panic = fear != null && fear.CanAffectCamera;
                material.SetVector("_Panic", panic ? new Vector4(fear.OverlayIntensity,
                    Mathf.Clamp01(fear.vignetteStrength), Mathf.Clamp01(fear.verticalGrainStrength),
                    Mathf.Max(1f, fear.verticalLineCount)) : Vector4.zero);
                material.SetFloat("_FilmTime", panic ? Time.time * Mathf.Max(0f, fear.filmJitterSpeed) : 0f);
                material.SetFloat("_FilmLineThickness", panic ? Mathf.Clamp(fear.verticalLineThickness, 0.05f, 2f) : 1f);
                material.SetVector("_PanicStatic", panic ? new Vector4(fear.OverlayIntensity * Mathf.Clamp01(fear.staticStrength),
                    Mathf.Floor(Time.time * Mathf.Max(1f, fear.staticFramesPerSecond)) % 997f, 0f, 0f) : Vector4.zero);
                material.SetVector("_Trance", new Vector4(Mathf.Clamp01(effects.TranceBlur) * 0.025f, 0f, 0f, 0f));
                material.SetColor("_TranceTint", effects.TranceTint);
                material.SetColor("_TranceFade", effects.ScreenFade);
                float detail = Mathf.Clamp(effects.enemyPixelDetail, 1f, 4f);
                if (effects.pixelation && detail > 1.001f)
                    mask = RecordMask(graph, resources, cameraData);
                material.SetVector("_EnemyDetail", new Vector4(detail, mask.IsValid() ? 1f : 0f, 0f, 0f));
                if (!mask.IsValid())
                    material.SetTexture("_EnemyMask", Texture2D.blackTexture);
                if (effects.pixelation && resources.cameraDepthTexture.IsValid())
                    SetMoon(RenderSettings.skybox);
                else
                    material.SetVector("_FogMoon", Vector4.zero);
            }

            TextureHandle source = resources.activeColorTexture;
            TextureDesc descriptor = graph.GetTextureDesc(source);
            descriptor.name = shaderPass == 0 ? "Night Volumetrics" : "Pixelated World and Panic";
            descriptor.clearBuffer = false;
            TextureHandle destination = graph.CreateTexture(descriptor);
            using (var builder = graph.AddRasterRenderPass<PassData>(descriptor.name, out var data))
            {
                data.source = source;
                data.mask = mask;
                data.material = material;
                data.shaderPass = shaderPass;
                builder.UseTexture(source);
                if (mask.IsValid())
                    builder.UseTexture(mask);
                if (shaderPass == 0)
                {
                    builder.UseTexture(resources.cameraDepthTexture);
                    if (resources.mainShadowsTexture.IsValid())
                        builder.UseTexture(resources.mainShadowsTexture);
                }
                else if (resources.cameraDepthTexture.IsValid())
                {
                    builder.UseTexture(resources.cameraDepthTexture);
                }
                builder.SetRenderAttachment(destination, 0);
                builder.SetRenderFunc((PassData pass, RasterGraphContext context) =>
                {
                    if (pass.mask.IsValid())
                        pass.material.SetTexture("_EnemyMask", (Texture)pass.mask);
                    Blitter.BlitTexture(context.cmd, pass.source, new Vector4(1f, 1f, 0f, 0f),
                        pass.material, pass.shaderPass);
                });
            }
            resources.cameraColor = destination;
        }

        private TextureHandle RecordMask(RenderGraph graph, UniversalResourceData resources, UniversalCameraData cameraData)
        {
            Camera camera = cameraData.camera;
            markedRenderers.Clear();
            foreach (Renderer marked in ReducedPixelation.ActiveRenderers)
            {
                if (marked != null && marked.enabled && marked.gameObject.activeInHierarchy && !marked.forceRenderingOff &&
                    (camera.cullingMask & (1 << marked.gameObject.layer)) != 0 &&
                    (!(marked is SpriteRenderer sprite) || sprite.sprite != null))
                    markedRenderers.Add(marked);
            }
            if (markedRenderers.Count == 0 || maskMaterial == null || !resources.cameraDepthTexture.IsValid())
                return TextureHandle.nullHandle;

            TextureDesc descriptor = graph.GetTextureDesc(resources.activeColorTexture);
            descriptor.name = "Reduced Pixelation Mask";
            descriptor.format = GraphicsFormat.R8_UNorm;
            descriptor.msaaSamples = MSAASamples.None;
            descriptor.clearBuffer = true;
            descriptor.clearColor = Color.clear;
            TextureHandle mask = graph.CreateTexture(descriptor);
            maskMaterial.SetVector("_EnemyMaskSize", new Vector4(descriptor.width, descriptor.height,
                1f / Mathf.Max(1, descriptor.width), 1f / Mathf.Max(1, descriptor.height)));
            using (var builder = graph.AddRasterRenderPass<MaskPassData>(descriptor.name, out var data))
            {
                data.depth = resources.cameraDepthTexture;
                data.material = maskMaterial;
                data.view = cameraData.GetViewMatrix();
                data.projection = GL.GetGPUProjectionMatrix(cameraData.GetProjectionMatrix(), true);
                data.renderers.Clear();
                data.renderers.AddRange(markedRenderers);
                builder.UseTexture(data.depth);
                builder.SetRenderAttachment(mask, 0);
                builder.AllowPassCulling(false);
                builder.SetRenderFunc((MaskPassData pass, RasterGraphContext context) =>
                {
                    pass.material.SetTexture("_EnemySceneDepth", (Texture)pass.depth);
                    pass.material.SetMatrix("_EnemyMatrixV", pass.view);
                    pass.material.SetMatrix("_EnemyMatrixVP", pass.projection * pass.view);
                    foreach (Renderer marked in pass.renderers)
                    {
                        if (marked != null)
                            context.cmd.DrawRenderer(marked, pass.material, 0, MaskShaderPass);
                    }
                });
            }
            return mask;
        }

        private void SetMoon(Material sky)
        {
            // Mirrors the Night Sky skybox's moon so the fog pass can let only the moon show through.
            bool moon = sky != null && sky.HasProperty("_MoonTex") && sky.HasProperty("_MoonSize") &&
                sky.GetTexture("_MoonTex") != null;
            material.SetTexture("_FogMoonTex", moon ? sky.GetTexture("_MoonTex") : Texture2D.blackTexture);
            if (!moon)
            {
                material.SetVector("_FogMoon", Vector4.zero);
                material.SetVector("_FogMoonGlow", Vector4.zero);
                return;
            }
            Color glowColor = sky.GetColor("_MoonGlowColor");
            material.SetVector("_FogMoon", new Vector4(sky.GetFloat("_MoonAzimuth"), sky.GetFloat("_MoonElevation"),
                sky.GetFloat("_MoonSize"), 1f));
            material.SetVector("_FogMoonGlow", new Vector4(sky.GetFloat("_MoonGlowSize"),
                glowColor.maxColorComponent > 0.0001f ? 1f : 0f, sky.GetColor("_MoonColor").a, 0f));
        }

        private void SetLights(UniversalLightData data)
        {
            Vector4 mainDirection = Vector4.zero;
            Color mainColor = Color.black;
            if (data.mainLightIndex >= 0)
            {
                VisibleLight main = data.visibleLights[data.mainLightIndex];
                mainDirection = -main.localToWorldMatrix.GetColumn(2);
                mainColor = main.finalColor;
            }
            material.SetVector("_FogMainDirection", mainDirection);
            material.SetColor("_FogMainColor", mainColor);
            int count = 0;
            for (int index = 0; index < data.visibleLights.Length && count < MaxLocalLights; index++)
            {
                VisibleLight light = data.visibleLights[index];
                if (light.lightType != LightType.Point && light.lightType != LightType.Spot)
                    continue;
                Vector4 position = light.localToWorldMatrix.GetColumn(3);
                position.w = light.range;
                Vector4 direction = light.localToWorldMatrix.GetColumn(2);
                direction.w = light.lightType == LightType.Spot ?
                    Mathf.Cos(light.spotAngle * Mathf.Deg2Rad * 0.5f) : -1f;
                lightPositions[count] = position;
                lightColors[count] = light.finalColor;
                lightDirections[count] = direction;
                count++;
            }
            material.SetInt("_FogLocalLightCount", count);
            material.SetVectorArray("_FogLightPositions", lightPositions);
            material.SetVectorArray("_FogLightColors", lightColors);
            material.SetVectorArray("_FogLightDirections", lightDirections);
        }
    }
}
