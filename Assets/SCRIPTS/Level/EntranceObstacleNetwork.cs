using System.Collections.Generic;
using Mirror;
using UnityEngine;

public struct EntranceObstacleStateMessage : NetworkMessage
{
    public string scenePath;
    public int seed;
    public string obstaclePath;
    public bool keyUsed;
    public bool destroyed;
}

public struct EntranceObstacleSnapshotRequest : NetworkMessage
{
}

/// <summary>Replicates state for deterministic, locally built obstacles without spawning network identities.</summary>
public static class EntranceObstacleNetwork
{
    private static bool clientHandlersRegistered;

    public static void RegisterServerHandlers() =>
        NetworkServer.ReplaceHandler<EntranceObstacleSnapshotRequest>((connection, _) =>
        {
            foreach (EntranceObstacle obstacle in FindObstacles())
            {
                if (obstacle.KeyUsed || obstacle.Destroyed)
                    connection.Send(StateOf(obstacle));
            }
        });

    public static void RegisterClientHandlers()
    {
        NetworkClient.RegisterHandler<EntranceObstacleStateMessage>(ApplyState);
        clientHandlersRegistered = true;
    }

    public static void UnregisterClientHandlers()
    {
        NetworkClient.UnregisterHandler<EntranceObstacleStateMessage>();
        clientHandlersRegistered = false;
    }

    public static void RequestSnapshot()
    {
        if (clientHandlersRegistered && NetworkClient.isConnected && NetworkClient.connection.isAuthenticated)
            NetworkClient.Send(new EntranceObstacleSnapshotRequest());
    }

    internal static void Broadcast(EntranceObstacle obstacle)
    {
        if (NetworkServer.active)
            NetworkServer.SendToAll(StateOf(obstacle));
    }

    private static EntranceObstacle[] FindObstacles() =>
        Object.FindObjectsByType<EntranceObstacle>(FindObjectsInactive.Include, FindObjectsSortMode.None);

    private static EntranceObstacleStateMessage StateOf(EntranceObstacle obstacle)
    {
        MapManager map = obstacle.GetComponentInParent<MapManager>(true);
        return new EntranceObstacleStateMessage
        {
            scenePath = obstacle.gameObject.scene.path,
            seed = map != null ? map.Seed : 0,
            obstaclePath = PathOf(obstacle.transform, map != null ? map.GeneratedRoot : null),
            keyUsed = obstacle.KeyUsed,
            destroyed = obstacle.Destroyed
        };
    }

    private static string PathOf(Transform current, Transform mapRoot)
    {
        // Generated children have deterministic sibling indices. Scene root indices can differ
        // between peers because of persistent local UI, so identify those by name and ordinal.
        var path = new List<string>();
        while (current != mapRoot && current.parent != null)
        {
            path.Add(current.GetSiblingIndex().ToString());
            current = current.parent;
        }
        if (current == mapRoot)
        {
            path.Add("Map");
            path.Reverse();
            return string.Join("/", path);
        }
        int ordinal = 0;
        foreach (GameObject root in current.gameObject.scene.GetRootGameObjects())
        {
            if (root.transform == current)
                break;
            if (root.name == current.name)
                ordinal++;
        }
        path.Add(current.name + ":" + ordinal);
        path.Reverse();
        return string.Join("/", path);
    }

    private static void ApplyState(EntranceObstacleStateMessage message)
    {
        if (NetworkServer.active)
            return;
        foreach (EntranceObstacle obstacle in FindObstacles())
        {
            EntranceObstacleStateMessage local = StateOf(obstacle);
            if (local.scenePath == message.scenePath && local.seed == message.seed &&
                local.obstaclePath == message.obstaclePath)
            {
                obstacle.ApplyNetworkState(message.keyUsed, message.destroyed);
                return;
            }
        }
        // A map may still be building when a delta arrives. Its completion requests a fresh snapshot.
    }
}
