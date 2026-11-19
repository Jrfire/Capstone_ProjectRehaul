using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Animated main menu background: plays shots in shuffled rounds (each available shot once per round, never the same
/// shot twice in a row), separated by fades to black:
/// a sky-view pan over a freshly generated street, the Tikbalang chasing the dollying camera down a straight street,
/// the Manananggal flying past the moon and the Santelmo chasing the camera through its inferno pocket corridor.
/// Every few seconds a static burst flashes one darkened enemy face. Everything is local (no networking); creature
/// visuals are plain billboarded sprites.
/// </summary>
[DisallowMultipleComponent]
public sealed class MainMenuBackground : MonoBehaviour
{
    public enum Shot { StreetPan, TikbalangChase, ManananggalMoon, SantelmoPocket }

    [Serializable]
    public sealed class CreatureSprites
    {
        [Tooltip("Frames played while the creature runs at the camera (front view).")]
        public Sprite[] front = Array.Empty<Sprite>();
        [Tooltip("Frames played while it moves to the viewer's right (left uses these flipped when empty).")]
        public Sprite[] right = Array.Empty<Sprite>();
        public Sprite[] left = Array.Empty<Sprite>();
        [Min(0.1f)] public float framesPerSecond = 10f;
        [Min(0.1f), Tooltip("Height of the visible sprite in metres.")]
        public float height = 2.5f;
    }

    /// <summary>A jumpscare sprite framed on the face (same values as the prefab's EnemyJumpscare).</summary>
    [Serializable]
    public sealed class JumpscareFace
    {
        public Sprite sprite;
        [Tooltip("Face position on the sprite (0-1 from bottom-left); it is kept at the screen centre.")]
        public Vector2 focus = new Vector2(0.5f, 0.7f);
        [Min(0.1f), Tooltip("Sprite height in screen heights (higher = closer crop on the face).")]
        public float screenHeight = 4f;
    }

    [Header("Scene")]
    [SerializeField] private Camera menuCamera;
    [SerializeField, Tooltip("Map built offline with a random seed (Build On Start off). Its connector pool also builds the chase corridors.")]
    private MapManager streetMap;
    [SerializeField, Tooltip("Light carried by the camera (searchlight / flashlight).")]
    private Light cameraLight;
    [SerializeField] private Material spriteMaterial;

    [Header("Overlay")]
    [SerializeField] private Image fade;
    [SerializeField] private RawImage staticImage;
    [SerializeField] private Image jumpscareImage;
    [SerializeField] private Image infernoTint;

    [Header("Creatures")]
    [SerializeField] private CreatureSprites tikbalang = new CreatureSprites();
    [SerializeField] private CreatureSprites manananggal = new CreatureSprites { height = 3.5f };
    [SerializeField] private CreatureSprites santelmo = new CreatureSprites { height = 1.2f };
    [SerializeField, Tooltip("Enemy faces flashed during static bursts (one per burst), cropped like their in-game jumpscare.")]
    private JumpscareFace[] faces = Array.Empty<JumpscareFace>();

    [Header("Timing")]
    [SerializeField, Min(0.05f)] private float fadeSeconds = 1.2f;
    [SerializeField, Min(0f)] private float blackHoldSeconds = 0.4f;
    [SerializeField] private Vector2 panSeconds = new Vector2(12f, 16f);
    [SerializeField] private Vector2 chaseSeconds = new Vector2(8f, 11f);
    [SerializeField, Min(1)] private int minMoonPasses = 2;
    [SerializeField, Min(1)] private int maxMoonPasses = 3;
    [SerializeField, Min(0.5f)] private float moonPassSeconds = 3.5f;

    [Header("Street pan")]
    [SerializeField, Min(5f)] private float panHeight = 30f;
    [SerializeField, Range(10f, 90f)] private float panPitch = 55f;

    [Header("Chases")]
    [SerializeField, Min(0.1f)] private float dollySpeed = 4.5f;
    [SerializeField] private float chaseStartGap = 14f;
    [SerializeField] private float chaseEndGap = 5f;
    [SerializeField, Min(3)] private int corridorCopies = 7;
    [SerializeField] private Vector3 corridorOrigin = new Vector3(0f, 0f, 900f);
    [SerializeField] private Color infernoAmbient = new Color(0.07f, 0.035f, 0.025f);
    [SerializeField] private Color infernoLampColor = new Color(1f, 0.55f, 0.32f);
    [SerializeField, Range(0f, 1f)] private float infernoTintAlpha = 0.1f;

    [Header("Moon")]
    [SerializeField] private Vector3 moonCameraPosition = new Vector3(0f, 2f, -900f);
    [SerializeField, Min(5f)] private float moonFlyDistance = 12f;

    [Header("Jumpscare flashes")]
    [SerializeField] private Vector2 burstInterval = new Vector2(7f, 15f);
    [SerializeField, Min(0.02f)] private float flashSeconds = 0.1f;
    [SerializeField, Range(0f, 1f), Tooltip("Brightness of the flashed jumpscares (low = emerging from the dark).")]
    private float jumpscareBrightness = 0.3f;
    [SerializeField, Range(0f, 1f)] private float staticAlpha = 0.45f;
    [SerializeField, Min(1f)] private float staticFramesPerSecond = 24f;

    private readonly List<Shot> available = new List<Shot>();
    private readonly List<Shot> bag = new List<Shot>();
    private readonly List<GameObject> corridorLamps = new List<GameObject>();
    private GameObject corridor;
    private Vector3 corridorAxis = Vector3.right;
    private Vector3 corridorStart;
    private float corridorLength;
    private Texture2D noise;
    private float nextNoiseTime;
    private float nextBurstTime;
    private int lastFace = -1;
    private Color ambient;

    public Shot? CurrentShot { get; private set; }
    public int ShotsPlayed { get; private set; }
    public int BurstCount { get; private set; }
    /// <summary>Faces shown so far (one per burst).</summary>
    public int FlashCount { get; private set; }
    public bool HasCorridor => corridor != null;
    public IReadOnlyList<Shot> AvailableShots => available;
    /// <summary>Forces the next shot (tests); cleared once used.</summary>
    public Shot? NextShotOverride { get; set; }

    private void Start()
    {
        if (menuCamera == null)
            menuCamera = Camera.main;
        MoonSky.BloodMoon = false;
        ambient = RenderSettings.ambientLight;
        SetAlpha(fade, 1f);
        SetAlpha(staticImage, 0f);
        SetAlpha(infernoTint, 0f);
        if (jumpscareImage != null)
            jumpscareImage.enabled = false;
        noise = new Texture2D(192, 108, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Repeat };
        if (staticImage != null)
            staticImage.texture = noise;
        ScheduleBurst();
        BuildSets();
        StartCoroutine(Run());
    }

    private void OnDestroy()
    {
        RenderSettings.ambientLight = ambient;
        if (noise != null)
            Destroy(noise);
    }

    private void BuildSets()
    {
        available.Clear();
        if (streetMap != null && !streetMap.IsDone)
            streetMap.Build(Environment.TickCount);
        if (streetMap != null && streetMap.IsBuilt)
            available.Add(Shot.StreetPan);
        if (TryBuildCorridor())
        {
            if (Has(tikbalang))
                available.Add(Shot.TikbalangChase);
            if (Has(santelmo))
                available.Add(Shot.SantelmoPocket);
        }
        if (Has(manananggal))
            available.Add(Shot.ManananggalMoon);
    }

    private static bool Has(CreatureSprites sprites) => sprites != null && (sprites.front.Length > 0 || sprites.right.Length > 0);

    private IEnumerator Run()
    {
        Shot? last = null;
        while (true)
        {
            if (available.Count == 0)
            {
                yield return null;
                continue;
            }
            Shot shot = PickShot(last);
            last = shot;
            CurrentShot = shot;
            ShotsPlayed++;
            yield return Play(shot);
            SetAlpha(fade, 1f);
            ResetShot();
            if (blackHoldSeconds > 0f)
                yield return new WaitForSeconds(blackHoldSeconds);
        }
    }

    /// <summary>
    /// Shuffle bag: every available shot plays once (random order) before any repeats, and a new bag never starts
    /// with the shot that just played.
    /// </summary>
    private Shot PickShot(Shot? last)
    {
        if (NextShotOverride.HasValue && available.Contains(NextShotOverride.Value))
        {
            Shot forced = NextShotOverride.Value;
            NextShotOverride = null;
            bag.Remove(forced);
            return forced;
        }
        bag.RemoveAll(shot => !available.Contains(shot));
        if (bag.Count == 0)
        {
            bag.AddRange(available);
            for (int i = bag.Count - 1; i > 0; i--)
            {
                int j = UnityEngine.Random.Range(0, i + 1);
                (bag[i], bag[j]) = (bag[j], bag[i]);
            }
            if (bag.Count > 1 && last.HasValue && bag[0] == last.Value)
                (bag[0], bag[bag.Count - 1]) = (bag[bag.Count - 1], bag[0]);
        }
        Shot next = bag[0];
        bag.RemoveAt(0);
        return next;
    }

    private IEnumerator Play(Shot shot)
    {
        switch (shot)
        {
            case Shot.StreetPan: return PlayPan();
            case Shot.TikbalangChase: return PlayChase(tikbalang, false);
            case Shot.SantelmoPocket: return PlayChase(santelmo, true);
            default: return PlayMoon();
        }
    }

    // ------------------------------------------------------------------ shots

    private IEnumerator PlayPan()
    {
        Bounds bounds = MapBounds();
        float yaw = UnityEngine.Random.Range(0, 4) * 90f + UnityEngine.Random.Range(-20f, 20f);
        Vector3 direction = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
        float reach = Mathf.Max(bounds.extents.x, bounds.extents.z) * 0.7f;
        Vector3 from = bounds.center - direction * reach + Vector3.up * panHeight;
        Vector3 to = bounds.center + direction * reach * 0.6f + Vector3.up * panHeight;
        // Pitched down, looking ahead along the pan.
        Quaternion rotation = Quaternion.Euler(panPitch, yaw, 0f);
        SetLight(true, 70f, 90f, 40f);
        float duration = UnityEngine.Random.Range(panSeconds.x, panSeconds.y);
        yield return Animate(duration, t =>
        {
            menuCamera.transform.SetPositionAndRotation(Vector3.Lerp(from, to, t), rotation);
        });
    }

    private IEnumerator PlayChase(CreatureSprites creature, bool inferno)
    {
        if (inferno)
        {
            RenderSettings.ambientLight = infernoAmbient;
            SetAlpha(infernoTint, infernoTintAlpha);
            foreach (GameObject lamp in corridorLamps)
                lamp.SetActive(true);
            SetLight(false, 0f, 0f, 0f);
        }
        else
        {
            SetLight(true, 55f, 30f, 12f);
        }

        SpriteRenderer body = CreateSprite("Menu " + (inferno ? "Santelmo" : "Tikbalang"));
        if (inferno)
        {
            Light glow = new GameObject("Glow").AddComponent<Light>();
            glow.transform.SetParent(body.transform.parent, false);
            glow.type = LightType.Point;
            glow.color = infernoLampColor;
            glow.intensity = 6f;
            glow.range = 9f;
        }
        float duration = UnityEngine.Random.Range(chaseSeconds.x, chaseSeconds.y);
        Vector3 back = -corridorAxis;
        Vector3 start = corridorStart + corridorAxis * (chaseStartGap + 2f);
        float hover = inferno ? 1.1f : 0f;
        yield return Animate(duration, t =>
        {
            float seconds = t * duration;
            Vector3 cameraFeet = start + corridorAxis * (dollySpeed * seconds);
            Vector3 eye = cameraFeet + Vector3.up * 1.7f;
            float gap = Mathf.Lerp(chaseStartGap, chaseEndGap, t);
            Vector3 feet = cameraFeet + back * gap + Vector3.up * hover;
            if (inferno)
                feet.y += Mathf.Sin(seconds * 3f) * 0.15f;
            PlaceSprite(body, creature, creature.front, feet, seconds, false);
            menuCamera.transform.SetPositionAndRotation(eye, Quaternion.LookRotation((feet + Vector3.up * creature.height * 0.6f - eye).normalized, Vector3.up));
        });
    }

    private IEnumerator PlayMoon()
    {
        SetLight(false, 0f, 0f, 0f);
        MoonSky moon = FindAnyObjectByType<MoonSky>();
        Vector3 moonDirection = moon != null ? moon.MoonDirection : Quaternion.Euler(-22f, 35f, 0f) * Vector3.forward;
        Vector3 eye = moonCameraPosition;
        // Moon slightly off centre (upper right).
        Quaternion look = Quaternion.LookRotation(moonDirection, Vector3.up) * Quaternion.Euler(4f, -6f, 0f);
        menuCamera.transform.SetPositionAndRotation(eye, look);
        Vector3 right = Vector3.Cross(Vector3.up, moonDirection).normalized;
        Vector3 up = Vector3.Cross(moonDirection, right).normalized;
        Vector3 center = eye + moonDirection * moonFlyDistance;
        float span = moonFlyDistance * 0.9f;
        SpriteRenderer body = CreateSprite("Menu Manananggal");
        int passes = UnityEngine.Random.Range(minMoonPasses, maxMoonPasses + 1);
        for (int pass = 0; pass < passes; pass++)
        {
            bool toRight = pass % 2 == 0;
            float height = UnityEngine.Random.Range(-1.2f, 1.2f);
            float hold = pass == passes - 1 ? 1.2f : 0.3f;
            yield return Animate(moonPassSeconds + hold, t =>
            {
                float travel = Mathf.Clamp01(t * (moonPassSeconds + hold) / moonPassSeconds);
                float x = Mathf.Lerp(-span, span, toRight ? travel : 1f - travel);
                float seconds = t * (moonPassSeconds + hold);
                Vector3 position = center + right * x + up * (height + Mathf.Sin(seconds * 4f) * 0.4f);
                Sprite[] frames = toRight ? manananggal.right : manananggal.left;
                bool flip = false;
                if (frames.Length == 0)
                {
                    frames = toRight ? manananggal.left : manananggal.right;
                    flip = frames.Length > 0;
                }
                if (frames.Length == 0)
                    frames = manananggal.front;
                PlaceSprite(body, manananggal, frames, position - up * manananggal.height * 0.5f, seconds, flip);
            }, pass == 0, pass == passes - 1);
        }
    }

    // ------------------------------------------------------------------ helpers

    /// <summary>Runs <paramref name="step"/> (0-1) for the duration, fading in from black at the start and out at the end.</summary>
    private IEnumerator Animate(float duration, Action<float> step, bool fadeIn = true, bool fadeOut = true)
    {
        float elapsed = 0f;
        while (true)
        {
            float t = Mathf.Clamp01(elapsed / duration);
            step(t);
            float alpha = 0f;
            if (fadeIn)
                alpha = Mathf.Max(alpha, 1f - elapsed / fadeSeconds);
            if (fadeOut)
                alpha = Mathf.Max(alpha, (elapsed - (duration - fadeSeconds)) / fadeSeconds);
            SetAlpha(fade, Mathf.Clamp01(alpha));
            if (elapsed >= duration)
                yield break;
            yield return null;
            elapsed += Time.deltaTime;
        }
    }

    private void ResetShot()
    {
        foreach (Transform child in transform)
        {
            if (child.name.StartsWith("Menu ", StringComparison.Ordinal))
                Destroy(child.gameObject);
        }
        foreach (GameObject lamp in corridorLamps)
            lamp.SetActive(false);
        RenderSettings.ambientLight = ambient;
        SetAlpha(infernoTint, 0f);
    }

    private SpriteRenderer CreateSprite(string objectName)
    {
        var root = new GameObject(objectName);
        root.transform.SetParent(transform, false);
        var sprite = new GameObject("WorldSprites");
        sprite.transform.SetParent(root.transform, false);
        SpriteRenderer renderer = sprite.AddComponent<SpriteRenderer>();
        if (spriteMaterial != null)
            renderer.sharedMaterial = spriteMaterial;
        sprite.AddComponent<BillboardSprite>();
        return renderer;
    }

    private static void PlaceSprite(SpriteRenderer renderer, CreatureSprites creature, Sprite[] frames, Vector3 feet, float seconds, bool flip)
    {
        Transform root = renderer.transform.parent;
        root.position = feet;
        if (frames == null || frames.Length == 0)
            return;
        Sprite frame = frames[(int)(seconds * creature.framesPerSecond) % frames.Length];
        renderer.sprite = frame;
        renderer.flipX = flip;
        if (frame == null)
            return;
        float scale = creature.height / Mathf.Max(0.01f, frame.bounds.size.y);
        renderer.transform.localScale = Vector3.one * scale;
        renderer.transform.localPosition = Vector3.up * (-frame.bounds.min.y * scale);
    }

    private void SetLight(bool on, float angle, float range, float intensity)
    {
        if (cameraLight == null)
            return;
        cameraLight.enabled = on;
        if (!on)
            return;
        cameraLight.type = LightType.Spot;
        cameraLight.spotAngle = angle;
        cameraLight.innerSpotAngle = angle * 0.6f;
        cameraLight.range = range;
        cameraLight.intensity = intensity;
    }

    private Bounds MapBounds()
    {
        var bounds = new Bounds(streetMap.transform.position, Vector3.one);
        bool any = false;
        for (int i = 0; i < streetMap.BlockCount; i++)
        {
            StreetBlock block = streetMap.GetBlock(i);
            if (block == null)
                continue;
            var blockBounds = new Bounds(block.transform.position, new Vector3(block.Size.x, 1f, block.Size.y));
            if (any)
                bounds.Encapsulate(blockBounds);
            else
                bounds = blockBounds;
            any = true;
        }
        return bounds;
    }

    private bool TryBuildCorridor()
    {
        if (corridor != null)
            return true;
        if (streetMap == null)
            return false;
        var options = new List<(StreetBlock block, bool alongX)>();
        foreach (IReadOnlyList<StreetBlock> pool in new[] { streetMap.ConnectorBlocks, streetMap.TownBlocks })
        {
            if (pool == null)
                continue;
            foreach (StreetBlock block in pool)
            {
                if (block == null)
                    continue;
                if (PlayerTrance.IsStraight(block, BlockSide.West, BlockSide.East, true))
                    options.Add((block, true));
                else if (PlayerTrance.IsStraight(block, BlockSide.South, BlockSide.North, false))
                    options.Add((block, false));
            }
        }
        if (options.Count == 0)
            return false;

        (StreetBlock prefab, bool alongX) = options[UnityEngine.Random.Range(0, options.Count)];
        corridor = new GameObject("Menu Corridor");
        corridorAxis = alongX ? Vector3.right : Vector3.forward;
        float segment = alongX ? prefab.Size.x : prefab.Size.y;
        StreetBlockEntrance entrance = prefab.GetEntrance(alongX ? BlockSide.West : BlockSide.South);
        Vector3 lateral = alongX ? new Vector3(0f, 0f, entrance.LocalCenter.z) : new Vector3(entrance.LocalCenter.x, 0f, 0f);
        for (int i = 0; i < corridorCopies; i++)
        {
            StreetBlock copy = Instantiate(prefab, corridor.transform);
            copy.name = $"{prefab.name} (Menu {i})";
            Vector3 center = corridorOrigin + corridorAxis * (i * segment);
            copy.transform.SetPositionAndRotation(center, Quaternion.identity);
            PlayerTrance.StripCopy(copy, alongX);
            var lamp = new GameObject($"Inferno Lamp {i}").AddComponent<Light>();
            lamp.transform.SetParent(corridor.transform, false);
            lamp.transform.position = center + lateral + Vector3.up * 5f;
            lamp.type = LightType.Point;
            lamp.color = infernoLampColor;
            lamp.intensity = 8f;
            lamp.range = segment * 1.2f;
            lamp.shadows = LightShadows.None;
            lamp.gameObject.SetActive(false);
            corridorLamps.Add(lamp.gameObject);
        }
        corridorLength = segment * corridorCopies;
        corridorStart = corridorOrigin - corridorAxis * (segment * 0.5f) + lateral;
        float needed = chaseStartGap + 2f + dollySpeed * chaseSeconds.y + 4f;
        if (needed > corridorLength)
            dollySpeed = Mathf.Max(0.5f, (corridorLength - chaseStartGap - 6f) / chaseSeconds.y);
        return true;
    }

    // ------------------------------------------------------------------ jumpscare bursts

    private void Update()
    {
        if (staticImage != null && staticImage.color.a > 0.001f && Time.unscaledTime >= nextNoiseTime)
        {
            nextNoiseTime = Time.unscaledTime + 1f / staticFramesPerSecond;
            RefreshNoise();
        }
        if (faces.Length > 0 && Time.unscaledTime >= nextBurstTime)
        {
            ScheduleBurst();
            StartCoroutine(Burst());
        }
    }

    private void ScheduleBurst() => nextBurstTime = Time.unscaledTime + UnityEngine.Random.Range(burstInterval.x, burstInterval.y);

    /// <summary>Starts a static burst with darkened jumpscare flashes now.</summary>
    public void TriggerBurst() => StartCoroutine(Burst());

    private IEnumerator Burst()
    {
        BurstCount++;
        float rise = 0.2f;
        for (float t = 0f; t < rise; t += Time.unscaledDeltaTime)
        {
            SetAlpha(staticImage, staticAlpha * t / rise);
            yield return null;
        }
        SetAlpha(staticImage, staticAlpha);
        if (jumpscareImage != null && faces.Length > 0)
        {
            int pick = UnityEngine.Random.Range(0, faces.Length);
            if (faces.Length > 1 && pick == lastFace)
                pick = (pick + 1 + UnityEngine.Random.Range(0, faces.Length - 1)) % faces.Length;
            lastFace = pick;
            JumpscareFace face = faces[pick];
            if (face != null && face.sprite != null)
            {
                FrameFace(face);
                jumpscareImage.color = new Color(jumpscareBrightness, jumpscareBrightness, jumpscareBrightness, 1f);
                jumpscareImage.enabled = true;
                FlashCount++;
                yield return new WaitForSecondsRealtime(flashSeconds);
                jumpscareImage.enabled = false;
                yield return new WaitForSecondsRealtime(flashSeconds * 1.5f);
            }
        }
        for (float t = 0f; t < 0.35f; t += Time.unscaledDeltaTime)
        {
            SetAlpha(staticImage, staticAlpha * (1f - t / 0.35f));
            yield return null;
        }
        SetAlpha(staticImage, 0f);
    }

    /// <summary>Scales the sprite to the face's screen height with the face pinned to the screen centre.</summary>
    private void FrameFace(JumpscareFace face)
    {
        RectTransform rect = jumpscareImage.rectTransform;
        var parent = rect.parent as RectTransform;
        float screenHeight = parent != null ? parent.rect.height : Screen.height;
        Rect spriteRect = face.sprite.rect;
        float height = screenHeight * face.screenHeight;
        float width = height * spriteRect.width / Mathf.Max(1f, spriteRect.height);
        jumpscareImage.sprite = face.sprite;
        jumpscareImage.preserveAspect = false;
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(Mathf.Clamp01(face.focus.x), Mathf.Clamp01(face.focus.y));
        rect.sizeDelta = new Vector2(width, height);
        rect.anchoredPosition = Vector2.zero;
    }

    private void RefreshNoise()
    {
        var pixels = noise.GetPixels32();
        for (int i = 0; i < pixels.Length; i++)
        {
            byte value = (byte)UnityEngine.Random.Range(0, 256);
            pixels[i] = new Color32(value, value, value, 255);
        }
        noise.SetPixels32(pixels);
        noise.Apply(false);
    }

    private static void SetAlpha(Graphic graphic, float alpha)
    {
        if (graphic == null)
            return;
        Color color = graphic.color;
        color.a = alpha;
        graphic.color = color;
    }
}
