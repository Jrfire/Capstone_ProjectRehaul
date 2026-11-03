using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public sealed class EnemyRollEntry
{
    public EnemyDefinition enemy;
    [Min(0f), Tooltip("Relative chance of this enemy being rolled for the destination.")]
    public float weight = 1f;
}

[Serializable]
public sealed class TravelDestination
{
    [Tooltip("Scene name; it must be in the Build Profile's scene list.")]
    public string sceneName;
    [Tooltip("Enemy types this destination can roll. One is rolled per trip; an empty pool rolls no enemies.")]
    public List<EnemyRollEntry> enemyPool = new List<EnemyRollEntry>();

    public string Name => sceneName != null ? sceneName.Trim() : string.Empty;

    /// <summary>Weighted roll from the pool. Returns null when the pool has no valid entries.</summary>
    public EnemyDefinition Roll(Func<float> random01 = null)
    {
        float total = 0f;
        foreach (EnemyRollEntry entry in enemyPool)
        {
            if (entry != null && entry.enemy != null && entry.weight > 0f)
                total += entry.weight;
        }
        if (total <= 0f)
            return null;

        float pick = (random01 != null ? random01() : UnityEngine.Random.value) * total;
        EnemyDefinition last = null;
        foreach (EnemyRollEntry entry in enemyPool)
        {
            if (entry == null || entry.enemy == null || entry.weight <= 0f)
                continue;
            last = entry.enemy;
            if (pick < entry.weight)
                return entry.enemy;
            pick -= entry.weight;
        }
        return last;
    }
}
