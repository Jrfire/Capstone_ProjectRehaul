using System;
using Mirror;
using UnityEngine;

/// <summary>One choice on the character select screen.</summary>
[Serializable]
public sealed class CharacterOption
{
    [Tooltip("Button label, e.g. Male / Female.")]
    public string displayName = "Character";
    public CharacterDefinition definition;
    [Tooltip("Optional button picture; empty = the definition's first Idle South frame.")]
    public Sprite portrait;

    /// <summary>The picture shown on the selection button.</summary>
    public Sprite Portrait
    {
        get
        {
            if (portrait != null || definition == null)
                return portrait;
            CharacterAnimation idle = definition.GetAnimation(CharacterAnimationState.Idle);
            return idle.IsReady ? idle.GetFrame(CharacterDirection.South, 0d, true) : null;
        }
    }
}

/// <summary>
/// The player's chosen character (NetworkPlayer). New players start with none (<see cref="SelectedIndex"/> -1), so
/// <see cref="PlayerVision"/> shows no world sprite and <see cref="CharacterSelectScreen"/> locks their controls until
/// the owner picks one. The choice is a server-held SyncVar, so it persists across travel like health.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(NetworkIdentity))]
public sealed class PlayerCharacterSelection : NetworkBehaviour
{
    [SerializeField, Tooltip("Characters offered on the selection screen, in button order.")]
    private CharacterOption[] options = Array.Empty<CharacterOption>();

    [SyncVar(hook = nameof(OnSelectionChanged))]
    private int selectedIndex = -1;

    public static event Action<PlayerCharacterSelection> SelectionChanged;

    public CharacterOption[] Options => options;
    public int SelectedIndex => selectedIndex;
    public bool HasSelected => Definition != null;
    /// <summary>The chosen character's definition; null until one is selected.</summary>
    public CharacterDefinition Definition =>
        selectedIndex >= 0 && selectedIndex < options.Length ? options[selectedIndex].definition : null;

    /// <summary>Owner: chooses option <paramref name="index"/>.</summary>
    public void Select(int index)
    {
        if (!isOwned || index < 0 || index >= options.Length)
            return;
        if (NetworkServer.active)
            ServerSelect(index);
        else
            CmdSelect(index);
    }

    [Command]
    private void CmdSelect(int index) => ServerSelect(index);

    /// <summary>Server: sets the character (ignored when out of range).</summary>
    public void ServerSelect(int index)
    {
        if (!NetworkServer.active || index < 0 || index >= options.Length || options[index].definition == null)
            return;
        selectedIndex = index;
        SelectionChanged?.Invoke(this);
    }

    private void OnSelectionChanged(int _, int __) => SelectionChanged?.Invoke(this);
}
