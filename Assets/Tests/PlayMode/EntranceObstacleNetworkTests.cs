#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Mirror;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

public sealed class EntranceObstacleNetworkTests
{
    private readonly List<GameObject> created = new List<GameObject>();
    private readonly List<EntranceObstacleStateMessage> received = new List<EntranceObstacleStateMessage>();
    private Transport previousTransport;
    private bool previousListen;
    private bool started;

    [SetUp]
    public void SetUp()
    {
        Assert.That(NetworkServer.active || NetworkClient.active, Is.False);
        previousTransport = Transport.active;
        previousListen = NetworkServer.listen;
        started = true;
        Transport.active = NewObject("Obstacle Test Transport").AddComponent<TelepathyTransport>();
        NetworkServer.listen = false;
        NetworkServer.Listen(4);
        EntranceObstacleNetwork.RegisterServerHandlers();
        NetworkClient.ConnectHost();
        NetworkServer.localConnection.isAuthenticated = true;
        NetworkClient.connection.isAuthenticated = true;
        EntranceObstacleNetwork.RegisterClientHandlers();
        NetworkClient.ReplaceHandler<EntranceObstacleStateMessage>(received.Add);
        HostMode.InvokeOnConnected();
        Assert.That(NetworkClient.Ready(), Is.True);
    }

    [TearDown]
    public void TearDown()
    {
        if (!started)
            return;
        EntranceObstacleNetwork.UnregisterClientHandlers();
        NetworkClient.Disconnect();
        NetworkClient.Shutdown();
        NetworkServer.Shutdown();
        for (int i = created.Count - 1; i >= 0; i--)
        {
            if (created[i] != null)
                Object.DestroyImmediate(created[i]);
        }
        created.Clear();
        received.Clear();
        Transport.active = previousTransport;
        NetworkServer.listen = previousListen;
        started = false;
    }

    [UnityTest]
    public IEnumerator PartialAndClearedStatesBroadcastAndSnapshotThenApplyOnAReplica()
    {
        EntranceObstacle obstacle = NewObstacle("Networked Gate", ObstacleRequirement.KeyAndDestruction);
        Assert.That(obstacle.TryUseKey(), Is.True);
        yield return WaitForMessages();
        Assert.That(received.Count, Is.EqualTo(1));
        Assert.That(received[0].keyUsed, Is.True);
        Assert.That(received[0].destroyed, Is.False);
        EntranceObstacleStateMessage partial = received[0];

        received.Clear();
        EntranceObstacleNetwork.RequestSnapshot();
        yield return WaitForMessages();
        Assert.That(received.Count, Is.EqualTo(1), "Late joiners also receive partially fulfilled requirements.");
        Assert.That(received[0].obstaclePath, Is.EqualTo(partial.obstaclePath));

        received.Clear();
        Assert.That(obstacle.TryDestroy(), Is.True);
        yield return WaitForMessages();
        Assert.That(received.Count, Is.EqualTo(1));
        EntranceObstacleStateMessage cleared = received[0];
        Assert.That(cleared.destroyed, Is.True);
        Assert.That(obstacle.gameObject.activeSelf, Is.False);

        received.Clear();
        EntranceObstacleNetwork.RequestSnapshot();
        yield return WaitForMessages();
        Assert.That(received.Count, Is.EqualTo(1), "Deactivated obstacles must still appear in late-join snapshots.");
        Assert.That(received[0].destroyed, Is.True);

        NetworkClient.Disconnect();
        NetworkClient.Shutdown();
        NetworkServer.Shutdown();
        Object.DestroyImmediate(obstacle.gameObject);
        NewObject("Local-only HUD");
        EntranceObstacle replica = NewObstacle("Networked Gate", ObstacleRequirement.KeyAndDestruction);
        GameObject opened = NewObject("Opened Gate");
        var settings = new SerializedObject(replica);
        settings.FindProperty("clearedPrefab").objectReferenceValue = opened;
        settings.ApplyModifiedPropertiesWithoutUndo();
        BoxCollider solid = replica.gameObject.AddComponent<BoxCollider>();
        int events = 0;
        replica.cleared.AddListener(() => events++);

        Apply(partial);
        Assert.That(replica.KeyUsed, Is.True);
        Assert.That(replica.IsCleared, Is.False);
        Assert.That(replica.ClearedInstance, Is.Null);
        EntranceObstacleStateMessage stale = cleared;
        stale.seed++;
        Apply(stale);
        stale = cleared;
        stale.scenePath += ".other";
        Apply(stale);
        Assert.That(replica.IsCleared, Is.False, "States from another map seed or scene must not open this gate.");
        Apply(cleared);
        GameObject instance = replica.ClearedInstance;
        Assert.That(instance, Is.Not.Null);
        Assert.That(solid.enabled, Is.False);
        Apply(cleared);
        Apply(partial);
        Assert.That(replica.ClearedInstance, Is.SameAs(instance));
        Assert.That(replica.IsCleared, Is.True);
        Assert.That(events, Is.EqualTo(1), "Duplicate snapshots must not reopen the gate or replay events.");
    }

    [UnityTest]
    public IEnumerator PureClientCannotChangeObstacleState()
    {
        NetworkServer.Shutdown();
        // Keep the host client connection active to exercise the client-only authority guard.
        Assert.That(NetworkClient.active, Is.True);
        EntranceObstacle obstacle = NewObstacle("Client Gate", ObstacleRequirement.KeyAndDestruction);
        LogAssert.Expect(LogType.Warning, "Entrance obstacles can only be opened by server gameplay in a networked session.");
        Assert.That(obstacle.TryUseKey(), Is.False);
        LogAssert.Expect(LogType.Warning, "Entrance obstacles can only be opened by server gameplay in a networked session.");
        Assert.That(obstacle.TryDestroy(), Is.False);
        Assert.That(obstacle.KeyUsed || obstacle.Destroyed, Is.False);
        yield return null;
    }

    private EntranceObstacle NewObstacle(string name, ObstacleRequirement requirement)
    {
        EntranceObstacle obstacle = NewObject(name).AddComponent<EntranceObstacle>();
        var settings = new SerializedObject(obstacle);
        settings.FindProperty("requirement").enumValueIndex = (int)requirement;
        settings.ApplyModifiedPropertiesWithoutUndo();
        return obstacle;
    }

    private GameObject NewObject(string name)
    {
        var item = new GameObject(name);
        created.Add(item);
        return item;
    }

    // Mirror flushes server batches only every sendInterval, and uncapped frames can be much shorter,
    // so wait in real time for host-mode round trips (client request, server reply).
    private IEnumerator WaitForMessages()
    {
        float deadline = Time.realtimeSinceStartup + 2f;
        while (received.Count == 0 && Time.realtimeSinceStartup < deadline)
            yield return null;
        // Allow any unexpected duplicates to arrive before counts are asserted.
        float settle = Time.realtimeSinceStartup + NetworkServer.sendInterval * 3f + 0.05f;
        while (Time.realtimeSinceStartup < settle)
            yield return null;
    }

    private static void Apply(EntranceObstacleStateMessage message) =>
        typeof(EntranceObstacleNetwork).GetMethod("ApplyState", BindingFlags.Static | BindingFlags.NonPublic)
            .Invoke(null, new object[] { message });
}
#endif
