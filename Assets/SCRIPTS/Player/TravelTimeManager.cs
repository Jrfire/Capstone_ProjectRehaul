using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Clock hours a round trip to one destination costs.</summary>
[Serializable]
public sealed class TravelTimeCost
{
    [Tooltip("Destination scene name, exactly as listed on the map terminal.")]
    public string sceneName;
    [Min(0f), Tooltip("Clock hours deducted when leaving base for this destination. Covers the trip back as well.")]
    public float hours = 1f;
}

/// <summary>
/// Base-scene list of travel time costs. The cost is "back and forth": it is deducted from the round clock when the
/// jeepney leaves base for that destination; returning to base costs nothing.
/// </summary>
[DisallowMultipleComponent]
public sealed class TravelTimeManager : MonoBehaviour
{
    [SerializeField] private List<TravelTimeCost> costs = new List<TravelTimeCost>
    {
        new TravelTimeCost { sceneName = "StreetsTest", hours = 1f },
    };

    private static readonly List<TravelTimeManager> managers = new List<TravelTimeManager>();

    public IReadOnlyList<TravelTimeCost> Costs => costs;

    /// <summary>The manager in the active scene, or null.</summary>
    public static TravelTimeManager Active
    {
        get
        {
            Scene scene = SceneManager.GetActiveScene();
            foreach (TravelTimeManager manager in managers)
            {
                if (manager != null && manager.gameObject.scene == scene)
                    return manager;
            }
            return null;
        }
    }

    private void OnEnable() => managers.Add(this);

    private void OnDisable() => managers.Remove(this);

    public bool TryGetCost(string sceneName, out float hours)
    {
        foreach (TravelTimeCost cost in costs)
        {
            if (cost != null && !string.IsNullOrWhiteSpace(cost.sceneName) &&
                string.Equals(cost.sceneName.Trim(), sceneName?.Trim(), StringComparison.Ordinal))
            {
                hours = Mathf.Max(0f, cost.hours);
                return true;
            }
        }
        hours = 0f;
        return false;
    }

    /// <summary>Round-trip clock hours for a destination from the active scene's manager (0 if unlisted).</summary>
    public static float CostHours(string sceneName) =>
        Active != null && Active.TryGetCost(sceneName, out float hours) ? hours : 0f;

    /// <summary>"Travel Time: 1 hour" style text for the travel map.</summary>
    public static string FormatHours(float hours)
    {
        if (hours <= 0f)
            return "Travel Time: None";
        string amount = Mathf.Approximately(hours, Mathf.Round(hours)) ? Mathf.RoundToInt(hours).ToString() : hours.ToString("0.##");
        return $"Travel Time: {amount} {(Mathf.Approximately(hours, 1f) ? "hour" : "hours")}";
    }
}
