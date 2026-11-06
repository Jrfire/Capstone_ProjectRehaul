using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Lines the Albularyo says on the <see cref="AlbularyoScreen"/>, picked at random like the loading tips. Edit the lists
/// on the asset (Create > Capstone > Albularyo Dialogue) to add more; empty lines are ignored. Lines may use
/// {0} = agimats still needed, {1} = quota, {2} = turned in this round, {3} = "agimat" or "agimats" to match {0}.
/// </summary>
[CreateAssetMenu(menuName = "Capstone/Albularyo Dialogue", fileName = "AlbularyoDialogue")]
public sealed class AlbularyoDialogue : ScriptableObject
{
    [TextArea(2, 4), Tooltip("Said while agimats are still needed this round.")]
    public List<string> requestLines = new List<string>();

    [TextArea(2, 4), Tooltip("Said right after the quota is met.")]
    public List<string> quotaMetLines = new List<string>();

    public string PickRequest(string previous = null) => PickRandom(requestLines, previous);

    public string PickQuotaMet(string previous = null) => PickRandom(quotaMetLines, previous);

    /// <summary>A random non-empty line, avoiding <paramref name="previous"/> when another exists; null if none.</summary>
    public static string PickRandom(IList<string> lines, string previous)
    {
        if (lines == null)
            return null;
        var candidates = new List<string>();
        foreach (string line in lines)
        {
            if (!string.IsNullOrWhiteSpace(line) && line != previous)
                candidates.Add(line);
        }
        if (candidates.Count == 0)
            return string.IsNullOrWhiteSpace(previous) || !lines.Contains(previous) ? null : previous;
        return candidates[UnityEngine.Random.Range(0, candidates.Count)];
    }

    /// <summary>Fills the placeholders; a line with malformed braces is shown as written instead of throwing.</summary>
    public static string Format(string line, int stillNeeded, int quota, int turnedIn)
    {
        if (string.IsNullOrEmpty(line))
            return string.Empty;
        try
        {
            return string.Format(line, stillNeeded, quota, turnedIn, stillNeeded == 1 ? "agimat" : "agimats");
        }
        catch (FormatException)
        {
            return line;
        }
    }
}
