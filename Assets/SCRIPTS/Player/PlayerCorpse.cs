using Mirror;
using UnityEngine;

/// <summary>
/// Leaves a dead body where this player died (NetworkPlayer, local on every peer). When the synced health reaches 0 a
/// runtime "Corpse" object is placed at the player's feet in the active scene: the chosen character's last Dead frame,
/// billboarded toward the local camera, or, while no Dead frames exist, the front Idle frame laid flat on the ground.
/// It is removed on respawn/revival, when the player despawns or with the scene.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(PlayerHealth))]
public sealed class PlayerCorpse : NetworkBehaviour
{
    [SerializeField, Tooltip("Renderer whose material, sorting and scale the corpse copies (WorldSprites).")]
    private SpriteRenderer worldSprites;
    [SerializeField, Tooltip("Ground search distance below the player for placing the body.")]
    private float maxGroundDistance = 4f;

    private PlayerHealth health;
    private PlayerVision vision;
    private PlayerTrance trance;
    private GameObject corpse;

    public GameObject Corpse => corpse;

    private void Awake()
    {
        health = GetComponent<PlayerHealth>();
        vision = GetComponent<PlayerVision>();
        trance = GetComponent<PlayerTrance>();
        if (worldSprites == null && vision != null)
            worldSprites = vision.WorldSprites;
    }

    private void OnEnable()
    {
        if (health != null)
            health.HealthChanged += OnHealthChanged;
        if (trance != null)
            trance.TranceChanged += OnTranceChanged;
    }

    private void OnDisable()
    {
        if (health != null)
            health.HealthChanged -= OnHealthChanged;
        if (trance != null)
            trance.TranceChanged -= OnTranceChanged;
    }

    public override void OnStopClient() => RemoveCorpse();

    private void OnDestroy() => RemoveCorpse();

    private void OnHealthChanged(int current)
    {
        if (current <= 0)
        {
            if (corpse == null)
                SpawnCorpse();
        }
        else if (trance == null || !trance.InTrance)
            RemoveCorpse();
    }

    // A tranced player's body lies where the Santelmo caught them; it stays if they die in the pocket.
    private void OnTranceChanged(bool inTrance)
    {
        if (inTrance)
        {
            if (corpse == null)
                SpawnCorpse();
        }
        else if (health == null || health.CurrentHealth > 0)
            RemoveCorpse();
    }

    /// <summary>Places the body at the player's current position (replacing any previous one).</summary>
    public void SpawnCorpse()
    {
        RemoveCorpse();
        CharacterDefinition definition = vision != null ? vision.ActiveDefinition : null;
        Sprite sprite = null;
        bool flat = false;
        if (definition != null)
        {
            CharacterAnimation dead = definition.GetAnimation(CharacterAnimationState.Dead);
            if (dead.IsReady && dead.Frames.Count > 0)
                sprite = dead.Frames[dead.Frames.Count - 1];
            else
            {
                CharacterAnimation idle = definition.GetAnimation(CharacterAnimationState.Idle);
                if (idle.IsReady)
                {
                    sprite = idle.GetFrame(CharacterDirection.South, 0d, true);
                    flat = true;
                }
            }
        }

        Vector3 feet = transform.position;
        if (Physics.Raycast(feet + Vector3.up * 0.5f, Vector3.down, out RaycastHit hit, maxGroundDistance + 0.5f, ~0,
                QueryTriggerInteraction.Ignore) && hit.collider.transform.root != transform.root)
            feet = hit.point;

        corpse = new GameObject($"Corpse ({name})");
        corpse.transform.position = feet;
        var body = new GameObject("Body");
        body.transform.SetParent(corpse.transform, false);
        SpriteRenderer renderer = body.AddComponent<SpriteRenderer>();
        renderer.sprite = sprite;
        if (worldSprites != null)
        {
            renderer.sharedMaterial = worldSprites.sharedMaterial;
            renderer.sortingLayerID = worldSprites.sortingLayerID;
            renderer.sortingOrder = worldSprites.sortingOrder;
            body.transform.localScale = worldSprites.transform.lossyScale;
        }
        if (flat)
        {
            body.transform.localPosition = Vector3.up * 0.02f;
            body.transform.localRotation = Quaternion.Euler(90f, transform.eulerAngles.y, 0f);
        }
        else
        {
            if (sprite != null)
                body.transform.localPosition = Vector3.up * Mathf.Max(0f, (sprite.pivot.y / sprite.pixelsPerUnit) *
                    body.transform.localScale.y);
            body.AddComponent<BillboardSprite>();
        }
    }

    private void RemoveCorpse()
    {
        if (corpse != null)
            Destroy(corpse);
        corpse = null;
    }
}
