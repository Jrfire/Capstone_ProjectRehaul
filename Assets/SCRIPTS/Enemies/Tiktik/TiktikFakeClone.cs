using Mirror;
using UnityEngine;

/// <summary>
/// Blood moon Tiktik trick (local, victim only): a harmless copy of the Tiktik that appears ahead of where the victim is
/// looking and rushes them, ignoring flashlights. When it reaches them it vanishes with the damaged sound and the red
/// damage flash, but deals no damage. Created by <see cref="TiktikBrain"/>'s TargetRpc.
/// </summary>
public sealed class TiktikFakeClone : MonoBehaviour
{
    private const float ReachDistance = 1.1f;
    private const float LifetimeSeconds = 8f;

    private SpriteRenderer spriteRenderer;
    private EnemySpriteDefinition sprites;
    private Transform viewer;
    private float speed;
    private float age;

    /// <summary>Clones currently rushing on this client.</summary>
    public static int ActiveCount { get; private set; }
    /// <summary>Clones that reached the local player (fake hits) on this client.</summary>
    public static int FakeHits { get; private set; }

    public bool Reached { get; private set; }

    /// <summary>Spawns a clone of <paramref name="source"/> up to <paramref name="distance"/> ahead of the local camera.</summary>
    public static TiktikFakeClone Spawn(TiktikBrain source, float distance, float rushSpeed)
    {
        if (source == null || NetworkClient.localPlayer == null)
            return null;
        Transform player = NetworkClient.localPlayer.transform;
        Camera camera = player.GetComponentInChildren<Camera>();
        Vector3 eye = camera != null ? camera.transform.position : player.position + Vector3.up * 1.6f;
        Vector3 forward = camera != null ? camera.transform.forward : player.forward;
        forward.y = 0f;
        if (forward.sqrMagnitude < 0.0001f)
            forward = player.forward;
        forward.Normalize();

        // Stop in front of walls so it rushes out of the visible street, not from inside a building.
        float reach = distance;
        if (Physics.SphereCast(eye, 0.3f, forward, out RaycastHit wall, distance, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            reach = Mathf.Max(3f, wall.distance - 0.5f);
        Vector3 start = player.position + forward * reach;
        if (Physics.Raycast(start + Vector3.up * 3f, Vector3.down, out RaycastHit ground, 8f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            start.y = ground.point.y;

        var root = new GameObject("TiktikFakeClone");
        root.transform.position = start;
        var clone = root.AddComponent<TiktikFakeClone>();
        clone.viewer = player;
        clone.speed = rushSpeed;

        SpriteRenderer original = source.Presentation != null ? source.Presentation.WorldSprites : null;
        var visual = new GameObject("WorldSprites");
        visual.transform.SetParent(root.transform, false);
        clone.spriteRenderer = visual.AddComponent<SpriteRenderer>();
        if (original != null)
        {
            visual.transform.localPosition = original.transform.localPosition;
            visual.transform.localScale = original.transform.lossyScale;
            clone.spriteRenderer.sharedMaterial = original.sharedMaterial;
            clone.spriteRenderer.sprite = original.sprite;
            clone.spriteRenderer.color = new Color(original.color.r, original.color.g, original.color.b, 1f);
        }
        clone.sprites = source.Presentation != null ? source.Presentation.Sprites : null;
        visual.AddComponent<BillboardSprite>();
        ActiveCount++;
        return clone;
    }

    private void Update()
    {
        age += Time.deltaTime;
        if (viewer == null || age >= LifetimeSeconds)
        {
            Destroy(gameObject);
            return;
        }

        Vector3 toViewer = viewer.position - transform.position;
        toViewer.y = 0f;
        float distance = toViewer.magnitude;
        if (distance <= ReachDistance)
        {
            Reach();
            return;
        }
        Vector3 step = toViewer / distance * Mathf.Min(distance, speed * Time.deltaTime);
        Vector3 next = transform.position + step;
        next.y = Mathf.MoveTowards(transform.position.y, viewer.position.y, speed * Time.deltaTime);
        transform.position = next;

        if (sprites != null && spriteRenderer != null)
        {
            Sprite frame = sprites.GetAnimation(EnemyAnimationState.Moving).GetFrame(CharacterDirection.South, age, true);
            if (frame != null)
                spriteRenderer.sprite = frame;
        }
    }

    private void Reach()
    {
        Reached = true;
        FakeHits++;
        if (viewer.TryGetComponent(out PlayerAudio playerAudio))
            playerAudio.PlayFakeDamage();
        PlayerDamageFlash flash = FindAnyObjectByType<PlayerDamageFlash>();
        if (flash != null)
            flash.Flash();
        Destroy(gameObject);
    }

    private void OnDestroy() => ActiveCount--;

    /// <summary>Test hook: clears the counters.</summary>
    public static void ResetCounters()
    {
        FakeHits = 0;
    }
}
