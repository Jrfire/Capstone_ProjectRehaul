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
    [Tooltip("Enemy types this destination can roll. One is rolled per trip (more different types from round 3 on); an empty pool rolls no enemies.")]
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

    /// <summary>
    /// Rolls up to <paramref name="maxTypes"/> different enemy types: the first is always rolled (when the pool has
    /// valid entries); each further slot is added with <paramref name="extraChance"/>, stopping at the first failed
    /// chance. Types are weighted and never repeated.
    /// </summary>
    public List<EnemyDefinition> RollMany(int maxTypes, float extraChance, Func<float> random01 = null)
    {
        var result = new List<EnemyDefinition>();
        if (maxTypes <= 0)
            return result;
        Func<float> random = random01 ?? (() => UnityEngine.Random.value);
        var remaining = new List<EnemyRollEntry>();
        foreach (EnemyRollEntry entry in enemyPool)
        {
            if (entry != null && entry.enemy != null && entry.weight > 0f && !remaining.Exists(e => e.enemy == entry.enemy))
                remaining.Add(entry);
        }

        while (remaining.Count > 0 && result.Count < maxTypes)
        {
            if (result.Count > 0 && random() >= extraChance)
                break;
            float total = 0f;
            foreach (EnemyRollEntry entry in remaining)
                total += entry.weight;
            float pick = random() * total;
            int chosen = remaining.Count - 1;
            for (int index = 0; index < remaining.Count; index++)
            {
                if (pick < remaining[index].weight)
                {
                    chosen = index;
                    break;
                }
                pick -= remaining[index].weight;
            }
            result.Add(remaining[chosen].enemy);
            remaining.RemoveAt(chosen);
        }
        return result;
    }

    /// <summary>
    /// Most different enemy types a destination may roll in a round: 1, then one more every third round (round 3 = up
    /// to 2, round 6 = up to 3, round 9 onwards = up to 4).
    /// </summary>
    public static int MaxEnemyTypesForRound(int round) => Mathf.Clamp(1 + Mathf.Max(0, round) / 3, 1, 4);
}
