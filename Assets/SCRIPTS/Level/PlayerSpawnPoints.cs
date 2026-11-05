using System.Collections.Generic;
using Mirror;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class PlayerSpawnPoints : MonoBehaviour
{
    private readonly List<Transform> registeredPoints = new List<Transform>();

    private void OnEnable()
    {
        foreach (Transform child in transform)
        {
            if (!child.gameObject.activeInHierarchy || NetworkManager.startPositions.Contains(child))
                continue;
            NetworkManager.RegisterStartPosition(child);
            registeredPoints.Add(child);
        }

        if (registeredPoints.Count == 0)
            Debug.LogError("SpawnPoints needs active child transforms not already registered as Mirror start positions.", this);
    }

    private void OnDisable()
    {
        foreach (Transform point in registeredPoints)
            NetworkManager.UnRegisterStartPosition(point);
        registeredPoints.Clear();
        NetworkManager.startPositionIndex = 0;
    }
}
