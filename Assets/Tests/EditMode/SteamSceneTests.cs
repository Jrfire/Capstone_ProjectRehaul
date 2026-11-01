using System.Linq;
using Mirror;
using Mirror.FizzySteam;
using NUnit.Framework;
using Steamworks;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

public sealed class SteamSceneTests
{
    [Test]
    public void SampleSceneHasSteamTransportSixSpawnsAndAnInviteTerminal()
    {
        Scene scene = EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity", OpenSceneMode.Additive);
        try
        {
            GameObject[] roots = scene.GetRootGameObjects();
            NetworkManager[] managers = roots.SelectMany(root => root.GetComponentsInChildren<NetworkManager>(true)).ToArray();
            Assert.That(managers.Length, Is.EqualTo(1));
            NetworkManager manager = managers[0];
            Assert.That(manager.name, Is.EqualTo("NetworkManager"));
            Assert.That(manager, Is.TypeOf<SteamNetworkManager>());
            Assert.That(manager.GetComponent<NetworkManagerHUD>().enabled, Is.True);
            Assert.That(manager.GetComponent<SteamSession>().enabled, Is.True);
            Assert.That(manager.transport, Is.SameAs(manager.GetComponent<FizzySteamworks>()));
            Assert.That(manager.GetComponents<Transport>().Length, Is.EqualTo(1));
            Assert.That(manager.GetComponent<FizzySteamworks>().Channels,
                Is.EqualTo(new[] { EP2PSend.k_EP2PSendReliable, EP2PSend.k_EP2PSendUnreliableNoDelay }));
            Assert.That(manager.GetComponent<FizzySteamworks>().UseNextGenSteamNetworking, Is.True);
            Assert.That(manager.maxConnections, Is.EqualTo(6));
            Assert.That(manager.autoCreatePlayer, Is.True);
            Assert.That(manager.playerSpawnMethod, Is.EqualTo(PlayerSpawnMethod.RoundRobin));
            Assert.That(manager.playerPrefab,
                Is.EqualTo(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/PREFABS/NetworkPlayer.prefab")));
            Assert.That(manager.onlineScene, Is.Null.Or.Empty);
            Assert.That(manager.offlineScene, Is.Null.Or.Empty);

            GameObject points = roots.Single(root => root.name == "SpawnPoints");
            Assert.That(points.GetComponent<PlayerSpawnPoints>(), Is.Not.Null);
            Assert.That(points.transform.childCount, Is.EqualTo(6));
            for (int index = 0; index < points.transform.childCount; index++)
            {
                Transform point = points.transform.GetChild(index);
                Assert.That(point.gameObject.activeSelf, Is.True);
                for (int other = index + 1; other < points.transform.childCount; other++)
                    Assert.That(Vector3.Distance(point.position, points.transform.GetChild(other).position),
                        Is.GreaterThan(0.7f), "Player capsules must not overlap at their initial spawns.");
            }

            SteamInviteInteractable terminal = roots.SelectMany(root =>
                root.GetComponentsInChildren<SteamInviteInteractable>(true)).Single();
            Assert.That(terminal.GetComponent<Collider>().isTrigger, Is.False);
            Camera[] cameras = roots.SelectMany(root => root.GetComponentsInChildren<Camera>(true)).ToArray();
            Assert.That(cameras.Where(camera => camera.GetComponentInParent<NetworkIdentity>() == null), Is.Empty);
            Assert.That(cameras.Where(camera => camera.gameObject.activeInHierarchy), Is.Empty);
            Assert.That(roots.SelectMany(root => root.GetComponentsInChildren<AudioListener>(true))
                .Where(listener => listener.gameObject.activeInHierarchy), Is.Empty);
        }
        finally
        {
            EditorSceneManager.CloseScene(scene, true);
        }
    }

    [Test]
    public void UseIsAnEKeyButtonAndThePlayerPrefabReferencesIt()
    {
        InputActionAsset input = AssetDatabase.LoadAssetAtPath<InputActionAsset>("Assets/InputSystem_Actions.inputactions");
        InputAction action = input.FindAction("Player/Use", true);
        Assert.That(action.type, Is.EqualTo(InputActionType.Button));
        Assert.That(action.bindings.Count, Is.EqualTo(1));
        Assert.That(action.bindings[0].path, Is.EqualTo("<Keyboard>/e"));
        Assert.That(action.interactions, Is.Empty);

        GameObject player = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/PREFABS/NetworkPlayer.prefab");
        var interaction = new SerializedObject(player.GetComponent<PlayerInteraction>());
        Assert.That(interaction.FindProperty("inputActions").objectReferenceValue, Is.EqualTo(input));
        Camera camera = (Camera)interaction.FindProperty("playerCamera").objectReferenceValue;
        Assert.That(camera.transform.IsChildOf(player.transform), Is.True);
        Assert.That(interaction.FindProperty("useDistance").floatValue, Is.EqualTo(3.5f));
    }

    [TestCase("+connect 76561198000000001", true)]
    [TestCase("  +connect   76561198000000001  ", true)]
    [TestCase(null, false)]
    [TestCase("", false)]
    [TestCase("+connect localhost", false)]
    [TestCase("+connect 0", false)]
    [TestCase("+connect -1", false)]
    [TestCase("+connect 18446744073709551616", false)]
    [TestCase("76561198000000001", false)]
    [TestCase("+connect 76561198000000001 extra", false)]
    [TestCase("+connect", false)]
    public void InviteConnectStringsAcceptOnlyValidSteamUserIds(string value, bool valid)
    {
        Assert.That(SteamSession.TryParseConnectString(value, out string hostId), Is.EqualTo(valid));
        Assert.That(hostId, Is.EqualTo(valid ? "76561198000000001" : null));
    }

    [TestCase("rehaul_v1", "76561198000000001", 76561198000000001ul, true)]
    [TestCase("another_spacewar_game", "76561198000000001", 76561198000000001ul, false)]
    [TestCase("", "76561198000000001", 76561198000000001ul, false)]
    [TestCase("rehaul_v1", "localhost", 76561198000000001ul, false)]
    [TestCase("rehaul_v1", "76561198000000001", 76561198000000002ul, false)]
    public void LobbyMetadataRequiresThisProjectAndTheOriginalHost(string project, string host, ulong owner, bool valid)
    {
        Assert.That(SteamSession.TryParseLobbyHost(project, host, owner, out string hostId), Is.EqualTo(valid));
        Assert.That(hostId, Is.EqualTo(valid ? host : null));
    }

    [Test]
    public void LobbyIdsUseSteamLobbyIdsNotPlayerIds()
    {
        var lobby = new CSteamID(new AccountID_t(1234), (uint)EChatSteamIDInstanceFlags.k_EChatInstanceFlagLobby,
            EUniverse.k_EUniversePublic, EAccountType.k_EAccountTypeChat);
        Assert.That(SteamSession.IsValidLobbyId(lobby.m_SteamID), Is.True);
        Assert.That(SteamSession.IsValidLobbyId(76561198000000001ul), Is.False);
        Assert.That(SteamSession.IsValidLobbyId(0), Is.False);
    }
}
