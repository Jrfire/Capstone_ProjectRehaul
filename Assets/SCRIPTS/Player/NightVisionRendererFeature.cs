using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

public sealed class NightVisionRendererFeature : ScriptableRendererFeature
{
    [SerializeField] private Shader shader;
    private Material fogMaterial;
    private Material pixelMaterial;
    private NightPass fogPass;
    private NightPass pixelPass;

    public override void Create()
    {
        CoreUtils.Destroy(fogMaterial);
        CoreUtils.Destroy(pixelMaterial);
        if (shader == null)
        {
            Debug.LogError("NightVisionRendererFeature requires the Capstone Night Vision shader.");
            return;
        }
        fogMaterial = CoreUtils.CreateEngineMaterial(shader);
        pixelMaterial = CoreUtils.CreateEngineMaterial(shader);
        fogPass = new NightPass(fogMaterial, 0, RenderPassEvent.BeforeRenderingPostProcessing);
        pixelPass = new NightPass(pixelMaterial, 1, RenderPassEvent.AfterRenderingPostProcessing);
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
        if (fog != null && fog.active && fog.enabled.value && fog.density.value > 0f)
            renderer.EnqueuePass(fogPass);
        PlayerFearEffects fear = camera.GetComponent<PlayerFearEffects>();
        if (effects.pixelation || (fear != null && fear.CanAffectCamera && fear.CurrentIntensity > 0f))
            renderer.EnqueuePass(pixelPass);
    }

    protected override void Dispose(bool disposing)
    {
        CoreUtils.Destroy(fogMaterial);
        CoreUtils.Destroy(pixelMaterial);
    }

    private sealed class NightPass : ScriptableRenderPass
    {
        private const int MaxLocalLights = 8;
        private readonly Material material;
        private readonly int shaderPass;
        private readonly Vector4[] lightPositions = new Vector4[MaxLocalLights];
        private readonly Vector4[] lightColors = new Vector4[MaxLocalLights];
        private readonly Vector4[] lightDirections = new Vector4[MaxLocalLights];

        private sealed class PassData
        {
            public TextureHandle source;
            public Material material;
            public int shaderPass;
        }

        public NightPass(Material material, int shaderPass, RenderPassEvent passEvent)
        {
            this.material = material;
            this.shaderPass = shaderPass;
            renderPassEvent = passEvent;
            requiresIntermediateTexture = true;
            if (shaderPass == 0)
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
            if (shaderPass == 0)
            {
                NightFog fog = VolumeManager.instance.stack.GetComponent<NightFog>();
                material.SetVector("_FogDensityHeight", new Vector4(fog.density.value,
                    fog.baseHeight.value, fog.heightFalloff.value,
                    Mathf.Min(fog.maxDistance.value, cameraData.camera.farClipPlane)));
                material.SetColor("_FogAmbient", fog.ambientColor.value);
                material.SetVector("_FogLighting", new Vector4(fog.lightScattering.value,
                    fog.anisotropy.value, fog.steps.value, 0f));
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
                material.SetVector("_Panic", panic ? new Vector4(fear.CurrentIntensity,
                    Mathf.Clamp01(fear.vignetteStrength), Mathf.Clamp01(fear.verticalGrainStrength),
                    Mathf.Max(1f, fear.verticalLineCount)) : Vector4.zero);
                material.SetFloat("_FilmTime", panic ? Time.time * Mathf.Max(0f, fear.filmJitterSpeed) : 0f);
                material.SetFloat("_FilmLineThickness", panic ? Mathf.Clamp(fear.verticalLineThickness, 0.05f, 2f) : 1f);
            }

            TextureHandle source = resources.activeColorTexture;
            TextureDesc descriptor = graph.GetTextureDesc(source);
            descriptor.name = shaderPass == 0 ? "Night Volumetrics" : "Pixelated World and Panic";
            descriptor.clearBuffer = false;
            TextureHandle destination = graph.CreateTexture(descriptor);
            using (var builder = graph.AddRasterRenderPass<PassData>(descriptor.name, out var data))
            {
                data.source = source;
                data.material = material;
                data.shaderPass = shaderPass;
                builder.UseTexture(source);
                if (shaderPass == 0)
                {
                    builder.UseTexture(resources.cameraDepthTexture);
                    if (resources.mainShadowsTexture.IsValid())
                        builder.UseTexture(resources.mainShadowsTexture);
                }
                builder.SetRenderAttachment(destination, 0);
                builder.SetRenderFunc((PassData pass, RasterGraphContext context) =>
                    Blitter.BlitTexture(context.cmd, pass.source, new Vector4(1f, 1f, 0f, 0f),
                        pass.material, pass.shaderPass));
            }
            resources.cameraColor = destination;
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
