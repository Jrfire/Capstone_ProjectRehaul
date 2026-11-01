using System.Collections.Generic;
using Mirror;
using NUnit.Framework;
using UnityEngine;

public sealed class SpawnPointTests
{
    [Test]
    public void SixChildSpawnsRegisterInOrderAndUnregisterOnDisable()
    {
        List<Transform> previousPoints = NetworkManager.startPositions;
        int previousIndex = NetworkManager.startPositionIndex;
        NetworkManager.startPositions = new List<Transform>();
        NetworkManager.startPositionIndex = 0;
        var parent = new GameObject("SpawnPoints");
        var managerObject = new GameObject("TestNetworkManager");
        parent.SetActive(false);
        managerObject.SetActive(false);
        try
        {
            for (int index = 0; index < 6; index++)
            {
                var child = new GameObject(index.ToString());
                child.transform.SetParent(parent.transform);
                child.transform.localPosition = new Vector3(index * 2f, 0f, 0f);
            }

            parent.AddComponent<PlayerSpawnPoints>();
            parent.SetActive(true);
            NetworkManager manager = managerObject.AddComponent<NetworkManager>();
            manager.playerSpawnMethod = PlayerSpawnMethod.RoundRobin;
            Assert.That(NetworkManager.startPositions.Count, Is.EqualTo(6));
            for (int index = 0; index < 6; index++)
                Assert.That(manager.GetStartPosition(), Is.SameAs(parent.transform.GetChild(index)));
            Assert.That(manager.GetStartPosition(), Is.SameAs(parent.transform.GetChild(0)));
            parent.SetActive(false);
            Assert.That(NetworkManager.startPositions, Is.Empty);
            parent.SetActive(true);
            Assert.That(NetworkManager.startPositions.Count, Is.EqualTo(6));
            Assert.That(manager.GetStartPosition(), Is.SameAs(parent.transform.GetChild(0)));
        }
        finally
        {
            Object.DestroyImmediate(parent);
            Object.DestroyImmediate(managerObject);
            NetworkManager.startPositions = previousPoints;
            NetworkManager.startPositionIndex = previousIndex;
        }
    }
}
