using UnityEngine;

/// <summary>
/// Blood moon hunting: never despawns. Circles the map at hunting altitude around its centre (the radius drifts so
/// every part of the map gets covered) and now and then stops to hover and look around in snapped turns. The first
/// player it sees starts a normal chase; afterwards it returns to circling instead of leaving.
/// </summary>
public sealed class ManananggalCirclingState : EnemyState
{
    private readonly ManananggalBrain brain;
    private float angle;
    private float direction = 1f;
    private float radiusPhase;
    private float hoverTimer;
    private float hoverLeft;
    private float lookTimer;

    public ManananggalCirclingState(ManananggalBrain brain) => this.brain = brain;

    public override string Name => "Circling";
    public bool IsHovering => hoverLeft > 0f;
    public int HoverCount { get; private set; }
    public Vector3 Center { get; private set; }
    public float Radius { get; private set; }

    public override void Enter()
    {
        brain.ApplyVision();
        brain.LookTarget = null;
        brain.ClearChaseTarget();
        UpdateOrbit();
        Vector3 offset = brain.transform.position - Center;
        offset.y = 0f;
        angle = offset.sqrMagnitude > 0.01f ? Mathf.Atan2(offset.z, offset.x) : Random.Range(0f, Mathf.PI * 2f);
        direction = Random.value < 0.5f ? -1f : 1f;
        radiusPhase = Random.Range(0f, Mathf.PI * 2f);
        hoverLeft = 0f;
        hoverTimer = ManananggalProfile.RandomIn(brain.Profile.bloodMoonHoverIntervalSeconds);

        if (brain.IsHidden)
        {
            // Coming out of a hidden wait: appear on the orbit, not in the middle of the map.
            brain.Flight.Warp(OrbitPoint(angle));
            brain.SetHidden(false);
        }
    }

    /// <summary>Starts a hover right away (for tests/debugging).</summary>
    public void HoverNow() => hoverTimer = 0f;

    public override void Tick(float deltaTime)
    {
        if (brain.TrySpotPlayer(out PlayerPersistence player))
        {
            brain.StartChase(player);
            return;
        }

        if (hoverLeft > 0f)
        {
            brain.Flight.Stop();
            hoverLeft -= deltaTime;
            lookTimer -= deltaTime;
            if (lookTimer <= 0f)
            {
                lookTimer = brain.Profile.bloodMoonLookTurnSeconds;
                Vector3 look = Quaternion.Euler(0f, Random.Range(60f, 150f) * (Random.value < 0.5f ? -1f : 1f), 0f) * brain.transform.forward;
                brain.Flight.FaceTowards(brain.transform.position + look);
            }
            if (hoverLeft <= 0f)
                hoverTimer = ManananggalProfile.RandomIn(brain.Profile.bloodMoonHoverIntervalSeconds);
            return;
        }

        hoverTimer -= deltaTime;
        if (hoverTimer <= 0f)
        {
            hoverLeft = Mathf.Max(0.1f, ManananggalProfile.RandomIn(brain.Profile.bloodMoonHoverSeconds));
            lookTimer = 0f;
            HoverCount++;
            return;
        }

        UpdateOrbit();
        radiusPhase += deltaTime * 0.15f;
        angle += direction * brain.Profile.huntSpeed * deltaTime / Mathf.Max(4f, Radius);
        brain.Flight.FlyTowards(OrbitPoint(angle + direction * 0.35f), brain.Profile.huntSpeed, deltaTime);
    }

    private void UpdateOrbit()
    {
        MapManager map = MapManager.Active;
        float altitude;
        float extent;
        if (map != null && map.IsBuilt && ManananggalBrain.TryGetMapBounds(map, out Bounds bounds))
        {
            Center = new Vector3(bounds.center.x, 0f, bounds.center.z);
            altitude = bounds.min.y + brain.Profile.huntAltitude;
            extent = Mathf.Min(bounds.extents.x, bounds.extents.z);
        }
        else
        {
            Center = new Vector3(brain.Home.Center.x, 0f, brain.Home.Center.z);
            altitude = brain.Home.Center.y + brain.Profile.huntAltitude;
            extent = Mathf.Min(brain.Home.HalfSize.x, brain.Home.HalfSize.y);
        }
        Center += Vector3.up * altitude;
        // Drift between the inner and outer part of the map so each loop sweeps different streets.
        Radius = Mathf.Max(6f, extent * Mathf.Lerp(0.25f, 0.9f, 0.5f + 0.5f * Mathf.Sin(radiusPhase)));
    }

    private Vector3 OrbitPoint(float at) => Center + new Vector3(Mathf.Cos(at), 0f, Mathf.Sin(at)) * Radius;
}
