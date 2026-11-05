using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Tips and lore lines shown at random on the travel loading screen. Edit the list on the asset
/// (Create > Capstone > Loading Tips) to add more; empty lines are ignored.
/// </summary>
[CreateAssetMenu(menuName = "Capstone/Loading Tips", fileName = "LoadingTips")]
public sealed class LoadingTips : ScriptableObject
{
    [TextArea(2, 4), Tooltip("One tip or lore line per entry. Rich text is not used.")]
    public List<string> tips = new List<string>();

    public int Count => tips != null ? tips.Count : 0;

    /// <summary>A random non-empty line, avoiding <paramref name="previous"/> when another exists; null if none.</summary>
    public string PickRandom(string previous = null)
    {
        if (tips == null)
            return null;
        var candidates = new List<string>();
        foreach (string tip in tips)
        {
            if (!string.IsNullOrWhiteSpace(tip) && tip != previous)
                candidates.Add(tip);
        }
        if (candidates.Count == 0)
            return string.IsNullOrWhiteSpace(previous) || !tips.Contains(previous) ? null : previous;
        return candidates[Random.Range(0, candidates.Count)];
    }
}
