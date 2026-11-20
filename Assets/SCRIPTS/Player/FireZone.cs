using System.Collections.Generic;
using Mirror;
using UnityEngine;

/// <summary>
/// A burning molotov fire (networked prefab spawned by <see cref="ThrowableProjectile"/>). The server burns living
/// players standing in the <see cref="CoreRadius"/> (<see cref="DamagePerTick"/> every <see cref="TickSeconds"/>, no
/// killer) and removes the fire after <see cref="Lifetime"/>. Enemies poll <see cref="Active"/> (EnemyBrain) and react
/// inside the <see cref="VicinityRadius"/>. Every peer fades the flames, light and particles out over the last
/// <see cref="FadeSeconds"/>.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(NetworkIdentity))]
public sealed class FireZone : NetworkBehaviour
{
    [SerializeField, Min(0.1f), Tooltip("Enemies within this horizontal distance react to the fire (Tikbalang returns home, Manananggal/Tiktik are repelled like by a bawang, the Santelmo is drawn in).")]
    private float vicinityRadius = 3f;
    [SerializeField, Min(0.1f), Tooltip("The burning area: players inside take damage; the Santelmo inside is empowered.")]
    private float coreRadius = 1.25f;
    [SerializeField, Min(0.1f), Tooltip("Height above/below the fire still counted as inside it.")]
    private float heightTolerance = 2.5f;
    [SerializeField, Min(0.1f), Tooltip("Seconds the fire burns.")]
    private float lifetime = 8f;
    [SerializeField, Min(0), Tooltip("Damage per tick to players in the core (1 = half a heart).")]
    private int damagePerTick = 1;
    [SerializeField, Min(0.1f)] private float tickSeconds = 1f;
    [SerializeField, Min(0f), Tooltip("Seconds repelled enemies (Manananggal, Tiktik) stay away, like garlic.")]
    private float repelSeconds = 30f;
    [SerializeField, Min(0f), Tooltip("Seconds the visuals fade out at the end of the lifetime.")]
    private float fadeSeconds = 1f;

    [Header("Visuals (every peer)")]
    [SerializeField] private SpriteRenderer[] flames = new SpriteRenderer[0];
    [SerializeField] private Light[] lights = new Light[0];
    [SerializeField] private ParticleSystem[] particles = new ParticleSystem[0];

    [Header("Sounds (every peer)")]
    [SerializeField, Tooltip("Played once where the bottle breaks (bottle shatter).")]
    private Capstone.Audio.AudioClipSet shatterSound = new Capstone.Audio.AudioClipSet();
    [SerializeField, Tooltip("Played once as the fire catches (together with the shatter).")]
    private Capstone.Audio.AudioClipSet igniteSound = new Capstone.Audio.AudioClipSet();
    [SerializeField, Min(0.1f)] private float impactSoundMaxDistance = 30f;
    [SerializeField, Tooltip("Looping crackle on this object; it fades out with the flames.")]
    private Capstone.Audio.FireSound loopSound;

    [SyncVar] private double startTime;

    private static readonly List<FireZone> active = new List<FireZone>();
    private readonly Dictionary<PlayerHealth, double> nextDamage = new Dictionary<PlayerHealth, double>();
    private float[] lightIntensities;
    private Color[] flameColors;
    private bool stopped;
    private bool impactPlayed;

    /// <summary>Peers that saw the fire start play the shatter/ignite sounds; late joiners do not.</summary>
    public const float ImpactSoundWindow = 1f;

    public int ImpactSoundsPlayed { get; private set; }
    public Capstone.Audio.FireSound LoopSound => loopSound;

    /// <summary>Fires currently burning (on every peer).</summary>
    public static IReadOnlyList<FireZone> Active => active;

    public float VicinityRadius => vicinityRadius;
    public float CoreRadius => coreRadius;
    public float Lifetime => lifetime;
    public float RepelSeconds => repelSeconds;
    public int DamagePerTick => damagePerTick;
    public float TickSeconds => tickSeconds;
    public float Age => (float)(Now - startTime);
    public bool IsBurning => isActiveAndEnabled && Age < lifetime;

    private static double Now => NetworkServer.active || NetworkClient.active ? NetworkTime.time : Time.timeAsDouble;

    /// <summary>Server: spawns <paramref name="prefab"/> burning at <paramref name="position"/>.</summary>
    public static FireZone ServerSpawn(FireZone prefab, Vector3 position)
    {
        if (!NetworkServer.active || prefab == null)
            return null;
        FireZone fire = Instantiate(prefab, position, Quaternion.identity);
        fire.name = prefab.name;
        fire.startTime = NetworkTime.time;
        NetworkServer.Spawn(fire.gameObject);
        return fire;
    }

    /// <summary>Runtime setup for tests and code-created fires.</summary>
    public void Configure(float vicinity, float core, float seconds, int damage = 1, float tick = 1f, float repel = 30f)
    {
        vicinityRadius = Mathf.Max(0.1f, vicinity);
        coreRadius = Mathf.Max(0.1f, core);
        lifetime = Mathf.Max(0.1f, seconds);
        damagePerTick = Mathf.Max(0, damage);
        tickSeconds = Mathf.Max(0.1f, tick);
        repelSeconds = Mathf.Max(0f, repel);
    }

    private void Awake()
    {
        if (!NetworkServer.active && !NetworkClient.active)
            startTime = Now;
        lightIntensities = new float[lights.Length];
        for (int index = 0; index < lights.Length; index++)
            lightIntensities[index] = lights[index] != null ? lights[index].intensity : 0f;
        flameColors = new Color[flames.Length];
        for (int index = 0; index < flames.Length; index++)
            flameColors[index] = flames[index] != null ? flames[index].color : Color.white;
    }

    private void OnEnable() => active.Add(this);

    private void OnDisable() => active.Remove(this);

    /// <summary>Horizontal distance from the fire when <paramref name="position"/> is within its height tolerance, else infinity.</summary>
    public float DistanceTo(Vector3 position)
    {
        Vector3 offset = position - transform.position;
        if (Mathf.Abs(offset.y) > heightTolerance)
            return float.PositiveInfinity;
        offset.y = 0f;
        return offset.magnitude;
    }

    public bool InVicinity(Vector3 position) => IsBurning && DistanceTo(position) <= vicinityRadius;
    public bool InCore(Vector3 position) => IsBurning && DistanceTo(position) <= coreRadius;

    /// <summary>The nearest burning fire whose vicinity contains <paramref name="position"/> (null if none).</summary>
    public static FireZone FindAt(Vector3 position, out bool inCore)
    {
        FireZone best = null;
        float bestDistance = float.PositiveInfinity;
        foreach (FireZone fire in active)
        {
            if (fire == null || !fire.IsBurning)
                continue;
            float distance = fire.DistanceTo(position);
            if (distance <= fire.vicinityRadius && distance < bestDistance)
            {
                best = fire;
                bestDistance = distance;
            }
        }
        inCore = best != null && bestDistance <= best.coreRadius;
        return best;
    }

    /// <summary>The nearest burning fire anywhere (null if none).</summary>
    public static FireZone FindNearest(Vector3 position)
    {
        FireZone best = null;
        float bestDistance = float.PositiveInfinity;
        foreach (FireZone fire in active)
        {
            if (fire == null || !fire.IsBurning)
                continue;
            float distance = (fire.transform.position - position).sqrMagnitude;
            if (distance < bestDistance)
            {
                best = fire;
                bestDistance = distance;
            }
        }
        return best;
    }

    private void Update()
    {
        float age = Age;
        UpdateVisuals(age);
        if (!NetworkServer.active)
            return;
        if (age >= lifetime)
        {
            if (isServer)
                NetworkServer.Destroy(gameObject);
            else
                Destroy(gameObject);
            return;
        }
        BurnPlayers();
    }

    private void BurnPlayers()
    {
        if (damagePerTick <= 0)
            return;
        double now = Now;
        foreach (PlayerPersistence player in PlayerPersistence.ActivePlayers)
        {
            if (player == null || player.IsAboard || player.IsInTrance || !player.TryGetComponent(out PlayerHealth health) ||
                health.IsDead || DistanceTo(player.transform.position) > coreRadius)
                continue;
            // The first tick burns at once; standing in the fire keeps burning every tick.
            if (nextDamage.TryGetValue(health, out double next) && now < next)
                continue;
            nextDamage[health] = now + tickSeconds;
            health.TakeDamage(damagePerTick);
        }
    }

    private void UpdateVisuals(float age)
    {
        if (startTime <= 0d)
            return; // a client before the spawn payload arrived
        float remaining = lifetime - age;
        float strength = fadeSeconds > 0f ? Mathf.Clamp01(remaining / fadeSeconds) : (remaining > 0f ? 1f : 0f);
        if (!impactPlayed)
        {
            impactPlayed = true;
            if (age <= ImpactSoundWindow)
            {
                ImpactSoundsPlayed++;
                Capstone.Audio.AudioManager.PlayAt(shatterSound, transform.position, Capstone.Audio.AudioChannel.Sfx, 2f, impactSoundMaxDistance);
                Capstone.Audio.AudioManager.PlayAt(igniteSound, transform.position, Capstone.Audio.AudioChannel.Sfx, 2f, impactSoundMaxDistance);
            }
        }
        if (loopSound != null)
            loopSound.VolumeMultiplier = strength;
        for (int index = 0; index < lights.Length; index++)
        {
            if (lights[index] == null)
                continue;
            FireLightFlicker flicker = lights[index].GetComponent<FireLightFlicker>();
            if (flicker != null)
                flicker.Multiplier = strength;
            else
                lights[index].intensity = lightIntensities[index] * strength;
        }
        for (int index = 0; index < flames.Length; index++)
        {
            if (flames[index] == null)
                continue;
            Color color = flameColors[index];
            color.a *= strength;
            flames[index].color = color;
        }
        if (!stopped && strength < 1f)
        {
            stopped = true;
            foreach (ParticleSystem system in particles)
            {
                if (system != null)
                    system.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            }
        }
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.55f, 0.1f, 0.5f);
        Gizmos.DrawWireSphere(transform.position, vicinityRadius);
        Gizmos.color = new Color(1f, 0.1f, 0.05f, 0.8f);
        Gizmos.DrawWireSphere(transform.position, coreRadius);
    }
}
