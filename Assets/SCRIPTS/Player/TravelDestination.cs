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
    [Tooltip("Enemy types this destination can roll. One is rolled per trip in round 1; from round 2 on (after the first quota) more different types; an empty pool rolls no enemies.")]
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
    /// chance. The first <paramref name="guaranteedTypes"/> slots skip the chance. Types are weighted and never repeated.
    /// </summary>
    public List<EnemyDefinition> RollMany(int maxTypes, float extraChance, Func<float> random01 = null, int guaranteedTypes = 1)
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
            if (result.Count >= Mathf.Max(1, guaranteedTypes) && random() >= extraChance)
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
    /// Most different enemy types a destination may roll in a round: 1 in round 1, then 2 after the first quota, one
    /// more every 2 quotas after that (round 4 = 3, round 6 onwards = 4).
    /// </summary>
    public static int MaxEnemyTypesForRound(int round) => round <= 1 ? 1 : Mathf.Clamp(2 + (round - 2) / 2, 2, 4);

    /// <summary>Different types always rolled in a round (when the pool has them): 1 in round 1, 2 once a quota is completed.</summary>
    public static int GuaranteedEnemyTypesForRound(int round) => round <= 1 ? 1 : 2;
}
