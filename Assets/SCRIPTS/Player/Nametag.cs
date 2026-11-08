using UnityEngine;

/// <summary>
/// Context for the HUD nametag: attach it to an object (pickupable items, the Steam invite object, the jeepney's back,
/// etc.) to show <see cref="DisplayName"/> near the bottom centre of the screen while the local player aims the camera
/// centre at one of its non-trigger colliders (including child colliders) within <see cref="maxDistance"/>.
/// Players use <see cref="PlayerName"/> instead, which is filled automatically.
/// </summary>
[DisallowMultipleComponent]
public class Nametag : MonoBehaviour
{
    [SerializeField, Tooltip("Name shown on the HUD while this object is aimed at.")]
    private string displayName = "Object";
    [SerializeField, Min(0.1f), Tooltip("Maximum aim distance from the camera, in metres.")]
    private float maxDistance = 3.5f;

    /// <summary>Optional runtime override (e.g. "Someone else is talking to the albularyo."); null or empty keeps the Display Name.</summary>
    public System.Func<string> Override { get; set; }

    public virtual string DisplayName
    {
        get
        {
            string replaced = Override?.Invoke();
            return string.IsNullOrEmpty(replaced) ? displayName : replaced;
        }
        set => displayName = value;
    }

    public float MaxDistance
    {
        get => maxDistance;
        set => maxDistance = Mathf.Max(0.1f, value);
    }
}
