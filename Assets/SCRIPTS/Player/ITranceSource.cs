using UnityEngine;

/// <summary>
/// Implemented by an enemy that can put players into a trance (the Santelmo), so the player-side pocket dimension can
/// read its settings without referencing the Enemies assembly.
/// </summary>
public interface ITranceSource
{
    /// <summary>Music the tranced player hears inside the pocket dimension (null = silence).</summary>
    AudioClip PocketTheme { get; }
}
