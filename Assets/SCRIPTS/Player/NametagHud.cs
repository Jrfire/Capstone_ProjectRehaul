using Mirror;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Bottom-centre nametag on the PlayerHud: shows the name of the object (<see cref="Nametag"/>) or other player
/// (<see cref="PlayerName"/>) the local player aims the camera centre at, plus the interaction prompt of a usable
/// <see cref="Interactable"/>. Reads <see cref="PlayerInteraction"/> on <c>NetworkClient.localPlayer</c>.
/// </summary>
[DisallowMultipleComponent]
public sealed class NametagHud : MonoBehaviour
{
    [SerializeField, Tooltip("Font used by the name and prompt labels.")]
    private Font font;
    [SerializeField, Tooltip("Shown/hidden as a whole; contains the labels.")]
    private GameObject root;
    [SerializeField, Tooltip("Shows the aimed object's Nametag name or the aimed player's Steam name.")]
    private Text nameText;
    [SerializeField, Tooltip("Shows the interaction prompt of a usable object. Optional.")]
    private Text promptText;

    [Tooltip("Show the interaction prompt line for usable objects.")]
    public bool showPrompt = true;
    [Tooltip("Prompt line format; {0} is the object's prompt (e.g. \"Invite Steam friends\").")]
    public string promptFormat = "[E] {0}";

    public Font Font
    {
        get => font;
        set
        {
            font = value;
            ApplyFont();
        }
    }

    public bool Visible => root != null && root.activeSelf;
    public string ShownName => nameText != null && nameText.gameObject.activeSelf ? nameText.text : null;
    public string ShownPrompt => promptText != null && promptText.gameObject.activeSelf ? promptText.text : null;
    public Text NameText => nameText;
    public Text PromptText => promptText;

    private void Awake()
    {
        ApplyFont();
        if (nameText != null)
        {
            nameText.supportRichText = false;
            nameText.raycastTarget = false;
        }
        if (promptText != null)
            promptText.raycastTarget = false;
        Show(null, null);
    }

    private void OnValidate() => ApplyFont();

    private void OnDisable() => Show(null, null);

    private void LateUpdate()
    {
        NetworkIdentity localPlayer = NetworkClient.active ? NetworkClient.localPlayer : null;
        PlayerInteraction interaction = localPlayer != null ? localPlayer.GetComponent<PlayerInteraction>() : null;
        if (interaction == null || !interaction.isActiveAndEnabled)
        {
            Show(null, null);
            return;
        }
        Interactable target = interaction.CurrentTarget;
        string prompt = showPrompt && target != null && !string.IsNullOrWhiteSpace(target.Prompt)
            ? string.Format(string.IsNullOrEmpty(promptFormat) ? "{0}" : promptFormat, target.Prompt)
            : null;
        Show(interaction.AimedName, prompt);
    }

    private void Show(string shownName, string prompt)
    {
        bool hasName = !string.IsNullOrWhiteSpace(shownName);
        bool hasPrompt = !string.IsNullOrWhiteSpace(prompt);
        if (nameText != null)
        {
            if (hasName && nameText.text != shownName)
                nameText.text = shownName;
            nameText.gameObject.SetActive(hasName);
        }
        if (promptText != null)
        {
            if (hasPrompt && promptText.text != prompt)
                promptText.text = prompt;
            promptText.gameObject.SetActive(hasPrompt);
        }
        if (root != null && root != gameObject)
            root.SetActive(hasName || hasPrompt);
    }

    private void ApplyFont()
    {
        if (font == null)
            return;
        if (nameText != null)
            nameText.font = font;
        if (promptText != null)
            promptText.font = font;
    }
}
