using System;
using Mirror;
using UnityEngine;

/// <summary>
/// A thrown item (networked prefab referenced by a Throwable <see cref="ItemDefinition"/>). It flies a ballistic arc from
/// its synced launch position/velocity/time, so every peer moves it identically without physics or a NetworkTransform.
/// The server sweeps the path each frame (ignoring the thrower and players) and handles the first hit:
/// <list type="bullet">
/// <item>Molotov: spawns <see cref="firePrefab"/> on the ground below the impact.</item>
/// <item>Garlic (bawang): an <see cref="IGarlicTarget"/> collider (Manananggal, Tiktik) is repelled for <see cref="garlicSeconds"/>; anything else just stops it.</item>
/// </list>
/// The visible sprite is a child (BillboardSprite).
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(NetworkIdentity))]
public sealed class ThrowableProjectile : NetworkBehaviour
{
    [SerializeField] private ThrowableKind kind = ThrowableKind.Molotov;
    [SerializeField, Tooltip("Molotov: the fire spawned where it lands (registered spawn prefab).")]
    private FireZone firePrefab;
    [SerializeField, Min(0f), Tooltip("Garlic: seconds a hit Manananggal/Tiktik is repelled.")]
    private float garlicSeconds = 30f;
    [SerializeField, Min(0.01f), Tooltip("Radius of the swept hit test.")]
    private float radius = 0.12f;
    [SerializeField, Min(0f), Tooltip("Extra upward launch speed, so throws arc.")]
    private float upwardSpeed = 2.5f;
    [SerializeField, Min(0f)] private float gravityScale = 1f;
    [SerializeField, Min(0.5f), Tooltip("Removed after this many seconds without a hit.")]
    private float maxLifetime = 6f;
    [SerializeField, Tooltip("Spins the sprite child while flying (degrees per second, 0 = none).")]
    private float spinSpeed = 540f;
    [SerializeField] private Transform spinner;

    [SyncVar] private Vector3 launchPosition;
    [SyncVar] private Vector3 launchVelocity;
    [SyncVar] private double launchTime;

    private static readonly RaycastHit[] hits = new RaycastHit[16];
    private NetworkIdentity thrower;
    private Vector3 lastPosition;
    private bool finished;

    /// <summary>Server: the projectile hit something (projectile, hit collider or null, impact point).</summary>
    public static event Action<ThrowableProjectile, Collider, Vector3> ServerImpact;

    public ThrowableKind Kind => kind;
    public FireZone FirePrefab => firePrefab;
    public float GarlicSeconds => garlicSeconds;
    public float UpwardSpeed => upwardSpeed;
    public NetworkIdentity Thrower => thrower;
    /// <summary>Server: what the projectile hit (null until it hits).</summary>
    public Collider HitCollider { get; private set; }

    private Vector3 Gravity => Physics.gravity * gravityScale;
    private float Age => (float)(NetworkTime.time - launchTime);

    /// <summary>Runtime setup for tests and code-created prefabs.</summary>
    public void Configure(ThrowableKind throwableKind, FireZone fire, float garlic = 30f)
    {
        kind = throwableKind;
        firePrefab = fire;
        garlicSeconds = Mathf.Max(0f, garlic);
    }

    /// <summary>Server: throws a copy of <paramref name="prefab"/> from <paramref name="origin"/> with <paramref name="velocity"/>.</summary>
    public static ThrowableProjectile ServerLaunch(ThrowableProjectile prefab, Vector3 origin, Vector3 velocity, NetworkIdentity thrower)
    {
        if (!NetworkServer.active || prefab == null)
            return null;
        ThrowableProjectile projectile = Instantiate(prefab, origin, Quaternion.identity);
        projectile.name = prefab.name;
        projectile.thrower = thrower;
        projectile.launchPosition = origin;
        projectile.launchVelocity = velocity;
        projectile.launchTime = NetworkTime.time;
        projectile.lastPosition = origin;
        NetworkServer.Spawn(projectile.gameObject);
        return projectile;
    }

    /// <summary>Position along the arc <paramref name="seconds"/> after the launch.</summary>
    public Vector3 PositionAt(float seconds) =>
        launchPosition + launchVelocity * seconds + 0.5f * seconds * seconds * Gravity;

    private void Update()
    {
        if (launchTime <= 0d || finished)
            return;
        float age = Age;
        Vector3 position = PositionAt(Mathf.Max(0f, age));
        if (spinner != null && spinSpeed != 0f)
            spinner.localRotation = Quaternion.Euler(0f, 0f, age * spinSpeed);

        if (!isServer)
        {
            transform.position = position;
            return;
        }
        if (Sweep(lastPosition, position, out RaycastHit hit))
        {
            transform.position = hit.point;
            Impact(hit);
            return;
        }
        transform.position = position;
        lastPosition = position;
        if (age > maxLifetime || position.y < -100f)
            Finish();
    }

    private bool Sweep(Vector3 from, Vector3 to, out RaycastHit best)
    {
        best = default;
        Vector3 delta = to - from;
        float distance = delta.magnitude;
        if (distance < 0.0001f)
            return false;
        int count = Physics.SphereCastNonAlloc(from, radius, delta / distance, hits, distance,
            Physics.DefaultRaycastLayers, QueryTriggerInteraction.Collide);
        bool found = false;
        for (int index = 0; index < count; index++)
        {
            Collider collider = hits[index].collider;
            if (collider == null || (found && hits[index].distance >= best.distance) || IsIgnored(collider))
                continue;
            best = hits[index];
            if (best.distance <= 0f && best.point == Vector3.zero)
                best.point = from;
            found = true;
        }
        return found;
    }

    private bool IsIgnored(Collider collider)
    {
        if (collider.transform.IsChildOf(transform))
            return true;
        if (thrower != null && collider.transform.IsChildOf(thrower.transform))
            return true;
        if (collider.GetComponentInParent<PlayerPersistence>() != null)
            return true; // thrown items pass players, like weapon swings
        // Triggers only count when they belong to something garlic affects (some enemy bodies are triggers).
        return collider.isTrigger && (kind != ThrowableKind.Garlic || collider.GetComponentInParent<IGarlicTarget>() == null);
    }

    [Server]
    private void Impact(RaycastHit hit)
    {
        HitCollider = hit.collider;
        switch (kind)
        {
            case ThrowableKind.Molotov:
                if (firePrefab != null)
                    FireZone.ServerSpawn(firePrefab, GroundBelow(hit));
                break;
            case ThrowableKind.Garlic:
                IGarlicTarget target = hit.collider.GetComponentInParent<IGarlicTarget>();
                target?.ServerTakeGarlic(thrower, garlicSeconds);
                break;
        }
        ServerImpact?.Invoke(this, hit.collider, hit.point);
        Finish();
    }

    private static Vector3 GroundBelow(RaycastHit hit)
    {
        Vector3 start = hit.point + hit.normal * 0.2f + Vector3.up * 0.5f;
        RaycastHit[] down = Physics.RaycastAll(start, Vector3.down, 30f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        float bestDistance = float.PositiveInfinity;
        Vector3 ground = hit.point;
        foreach (RaycastHit candidate in down)
        {
            if (candidate.collider.GetComponentInParent<PlayerPersistence>() != null ||
                candidate.collider.GetComponentInParent<IWeaponTarget>() != null || candidate.distance >= bestDistance)
                continue;
            bestDistance = candidate.distance;
            ground = candidate.point;
        }
        return ground;
    }

    private void Finish()
    {
        if (finished)
            return;
        finished = true;
        if (isServer)
            NetworkServer.Destroy(gameObject);
        else
            Destroy(gameObject);
    }
}
