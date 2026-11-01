#if UNITY_EDITOR
using System.Collections;
using System.Reflection;
using Mirror;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.TestTools;

public sealed class NetworkPlayerTests : InputTestFixture
{
    private const string PrefabPath = "Assets/PREFABS/NetworkPlayer.prefab";
    private const string InputPath = "Assets/InputSystem_Actions.inputactions";
    private GameObject transportObject;
    private GameObject ground;
    private GameObject localPlayer;
    private GameObject remotePlayer;
    private Transport previousTransport;
    private bool previousListen;
    private CharacterDefinition testDefinition;
    private Texture2D testSheet;
    private Sprite[] testFrames;
    private GameObject inviteTarget;
    private GameObject blocker;
    private CursorLockMode previousCursorLock;
    private bool previousCursorVisible;

    public override void Setup()
    {
        base.Setup();
        previousTransport = Transport.active;
        previousListen = NetworkServer.listen;
        previousCursorLock = Cursor.lockState;
        previousCursorVisible = Cursor.visible;
    }

    public override void TearDown()
    {
        if (transportObject != null)
        {
            NetworkClient.Disconnect();
            NetworkClient.Shutdown();
            NetworkServer.Shutdown();
            Object.DestroyImmediate(transportObject);
        }

        if (localPlayer != null)
            Object.DestroyImmediate(localPlayer);
        if (remotePlayer != null)
            Object.DestroyImmediate(remotePlayer);
        if (ground != null)
            Object.DestroyImmediate(ground);
        if (inviteTarget != null)
            Object.DestroyImmediate(inviteTarget);
        if (blocker != null)
            Object.DestroyImmediate(blocker);
        if (testDefinition != null)
            Object.DestroyImmediate(testDefinition);
        if (testFrames != null)
        {
            foreach (Sprite sprite in testFrames)
                Object.DestroyImmediate(sprite);
        }
        if (testSheet != null)
            Object.DestroyImmediate(testSheet);

        Transport.active = previousTransport;
        NetworkServer.listen = previousListen;
        Cursor.lockState = previousCursorLock;
        Cursor.visible = previousCursorVisible;
        base.TearDown();
    }

    [Test]
    public void PrefabHasWiredInputTransformAndSeparateViews()
    {
        GameObject prefab = LoadPrefab();
        NetworkIdentity identity = prefab.GetComponent<NetworkIdentity>();
        Assert.That(identity, Is.Not.Null);
        Assert.That(new SerializedObject(identity).FindProperty("_assetId").longValue, Is.Not.Zero);
        Assert.That(identity.sceneId, Is.Zero);
        Assert.That(prefab.GetComponent<CharacterController>(), Is.Not.Null);

        NetworkTransformReliable networkTransform = prefab.GetComponent<NetworkTransformReliable>();
        Assert.That(networkTransform, Is.Not.Null);
        Assert.That(networkTransform.target, Is.EqualTo(prefab.transform));
        Assert.That(networkTransform.syncDirection, Is.EqualTo(SyncDirection.ClientToServer));

        PlayerMovement movement = prefab.GetComponent<PlayerMovement>();
        Assert.That(movement, Is.Not.Null);
        Object input = new SerializedObject(movement).FindProperty("inputActions").objectReferenceValue;
        Assert.That(input, Is.EqualTo(AssetDatabase.LoadAssetAtPath<InputActionAsset>(InputPath)));

        GameObject firstPersonView = ViewRoot(prefab, "firstPersonView");
        GameObject worldView = ViewRoot(prefab, "worldView");
        Assert.That(firstPersonView, Is.Not.EqualTo(worldView));
        Assert.That(firstPersonView.activeSelf, Is.False);
        Assert.That(worldView.activeSelf, Is.False);
        Assert.That(firstPersonView.GetComponentInChildren<Camera>(true), Is.Not.Null);
        Assert.That(firstPersonView.GetComponentInChildren<AudioListener>(true), Is.Not.Null);
        Assert.That(firstPersonView.GetComponentInChildren<Canvas>(true).renderMode,
            Is.EqualTo(RenderMode.ScreenSpaceOverlay));
        Assert.That(worldView.GetComponentInChildren<SpriteRenderer>(true), Is.Not.Null);
        PlayerVision vision = prefab.GetComponent<PlayerVision>();
        Assert.That(vision, Is.Not.Null);
        var serializedVision = new SerializedObject(vision);
        Assert.That(serializedVision.FindProperty("characterDefinition").objectReferenceValue,
            Is.EqualTo(AssetDatabase.LoadAssetAtPath<CharacterDefinition>("Assets/SPRITES/PlayerCharacter.asset")));
        Assert.That(serializedVision.FindProperty("playerCamera").objectReferenceValue,
            Is.EqualTo(firstPersonView.GetComponentInChildren<Camera>(true)));
        Assert.That(serializedVision.FindProperty("worldSprites").objectReferenceValue,
            Is.EqualTo(worldView.GetComponentInChildren<SpriteRenderer>(true)));
        PlayerLook look = prefab.GetComponent<PlayerLook>();
        Assert.That(look, Is.Not.Null);
        var serializedLook = new SerializedObject(look);
        Assert.That(serializedLook.FindProperty("inputActions").objectReferenceValue, Is.EqualTo(input));
        Assert.That(serializedLook.FindProperty("playerCamera").objectReferenceValue,
            Is.EqualTo(firstPersonView.GetComponentInChildren<Camera>(true)));
        Assert.That(serializedLook.FindProperty("mouseSensitivity").floatValue, Is.EqualTo(0.15f));
        Assert.That(serializedLook.FindProperty("pitchLimit").floatValue, Is.EqualTo(85f));
    }

    [Test]
    public void MovementActionMapsWasdToTheCorrectDirections()
    {
        InputActionAsset input = AssetDatabase.LoadAssetAtPath<InputActionAsset>(InputPath);
        InputAction movement = input.FindAction("Player/Movement", true);
        Assert.That(movement.type, Is.EqualTo(InputActionType.Value));
        Assert.That(movement.expectedControlType, Is.EqualTo("Vector2"));
        Assert.That(input.FindAction("Player/Move"), Is.Null);

        AssertBinding(movement, "<Keyboard>/w", "up");
        AssertBinding(movement, "<Keyboard>/s", "down");
        AssertBinding(movement, "<Keyboard>/a", "left");
        AssertBinding(movement, "<Keyboard>/d", "right");
    }

    [UnityTest]
    public IEnumerator OnlyTheLocalPlayerMovesAndDiagonalSpeedIsNormalized()
    {
        Keyboard keyboard = InputSystem.AddDevice<Keyboard>();
        SpawnPlayers();
        yield return null;
        yield return null;

        Assert.That(localPlayer.GetComponent<NetworkIdentity>().isLocalPlayer, Is.True);
        Assert.That(remotePlayer.GetComponent<NetworkIdentity>().isLocalPlayer, Is.False);
        Assert.That(localPlayer.GetComponent<CharacterController>().enabled, Is.True);
        Assert.That(remotePlayer.GetComponent<CharacterController>().enabled, Is.False);
        Assert.That(RuntimeInput(remotePlayer), Is.Null);
        Assert.That(RuntimeInput(localPlayer),
            Is.Not.SameAs(AssetDatabase.LoadAssetAtPath<InputActionAsset>(InputPath)));
        Assert.That(RuntimeInput(localPlayer).FindAction("Player/Look", true).enabled, Is.False);

        CheckMovement(keyboard.wKey, Vector2.up);
        CheckMovement(keyboard.sKey, Vector2.down);
        CheckMovement(keyboard.aKey, Vector2.left);
        CheckMovement(keyboard.dKey, Vector2.right);

        Press(keyboard.wKey);
        Press(keyboard.dKey);
        InputSystem.Update();
        Vector3 before = localPlayer.transform.position;
        StepMovement(localPlayer);
        Vector3 delta = localPlayer.transform.position - before;
        Assert.That(new Vector2(delta.x, delta.z).magnitude,
            Is.EqualTo(5f * Time.deltaTime).Within(0.001f));
        Release(keyboard.wKey);
        Release(keyboard.dKey);
        InputSystem.Update();

        PlayerVision vision = localPlayer.GetComponent<PlayerVision>();
        Press(keyboard.wKey);
        InputSystem.Update();
        yield return WaitForAnimationState(vision, CharacterAnimationState.Walk);
        Release(keyboard.wKey);
        InputSystem.Update();
        yield return WaitForAnimationState(vision, CharacterAnimationState.Idle);
    }

    [UnityTest]
    public IEnumerator OwnerAndRemoteViewsStaySeparateAcrossEnableDisableAndDespawn()
    {
        SpawnPlayers();
        yield return null;
        yield return null;

        GameObject localView = ViewRoot(localPlayer, "firstPersonView");
        GameObject localWorld = ViewRoot(localPlayer, "worldView");
        GameObject remoteView = ViewRoot(remotePlayer, "firstPersonView");
        GameObject remoteWorld = ViewRoot(remotePlayer, "worldView");
        Assert.That(localView.activeInHierarchy, Is.True);
        Assert.That(localWorld.activeInHierarchy, Is.False);
        Assert.That(remoteView.activeInHierarchy, Is.False);
        Assert.That(remoteWorld.activeInHierarchy, Is.True);
        Assert.That(remoteView.GetComponentInChildren<Camera>(true).isActiveAndEnabled, Is.False);
        Assert.That(remoteView.GetComponentInChildren<AudioListener>(true).isActiveAndEnabled, Is.False);

        PlayerPresentation presentation = localPlayer.GetComponent<PlayerPresentation>();
        presentation.enabled = false;
        Assert.That(localView.activeInHierarchy, Is.False);
        Assert.That(localWorld.activeInHierarchy, Is.False);
        presentation.enabled = true;
        Assert.That(localView.activeInHierarchy, Is.True);
        Assert.That(localWorld.activeInHierarchy, Is.False);

        PlayerMovement movement = localPlayer.GetComponent<PlayerMovement>();
        InputActionAsset runtimeInput = RuntimeInput(localPlayer);
        movement.enabled = false;
        Assert.That(runtimeInput.FindAction("Player/Movement", true).enabled, Is.False);
        Assert.That(localPlayer.GetComponent<CharacterController>().enabled, Is.False);
        movement.enabled = true;
        Assert.That(runtimeInput.FindAction("Player/Movement", true).enabled, Is.True);
        Assert.That(localPlayer.GetComponent<CharacterController>().enabled, Is.True);

        NetworkServer.Destroy(localPlayer);
        yield return null;
        Assert.That(runtimeInput == null, Is.True);
    }

    private void SpawnPlayers()
    {
        Assert.That(NetworkServer.active, Is.False, "Run these tests without an active NetworkManager.");
        Assert.That(NetworkClient.active, Is.False, "Run these tests without an active NetworkManager.");
        GameObject prefab = LoadPrefab();
        transportObject = new GameObject("Player Test Transport");
        Transport.active = transportObject.AddComponent<TelepathyTransport>();

        // Host connections are in-memory; no socket or Steam initialization is needed.
        NetworkServer.listen = false;
        NetworkServer.Listen(4);
        NetworkClient.RegisterPrefab(prefab);
        NetworkClient.ConnectHost();
        NetworkServer.localConnection.isAuthenticated = true;
        NetworkClient.connection.isAuthenticated = true;
        HostMode.InvokeOnConnected();
        Assert.That(NetworkClient.Ready(), Is.True);

        ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
        ground.name = "Player Test Ground";
        ground.transform.position = new Vector3(0f, -0.5f, 0f);
        ground.transform.localScale = new Vector3(100f, 1f, 100f);
        Physics.SyncTransforms();

        localPlayer = Object.Instantiate(prefab);
        remotePlayer = Object.Instantiate(prefab, new Vector3(4f, 0f, 0f), Quaternion.identity);
        Assert.That(NetworkServer.AddPlayerForConnection(NetworkServer.localConnection, localPlayer), Is.True);
        NetworkServer.Spawn(remotePlayer);
    }

    [UnityTest]
    public IEnumerator PanicOnlyAffectsTheOwnerEvenIfARemoteCameraIsActivated()
    {
        SpawnPlayers();
        yield return null;
        Camera localCamera = localPlayer.GetComponentInChildren<Camera>(true);
        Camera remoteCamera = remotePlayer.GetComponentInChildren<Camera>(true);
        localPlayer.GetComponent<PlayerLook>().enabled = false;
        PlayerFearEffects localFear = localCamera.GetComponent<PlayerFearEffects>();
        PlayerFearEffects remoteFear = remoteCamera.GetComponent<PlayerFearEffects>();
        localFear.buildUpSpeed = 1000f;
        inviteTarget = GameObject.CreatePrimitive(PrimitiveType.Cube);
        inviteTarget.name = "Fear test enemy";
        inviteTarget.transform.position = localCamera.transform.position + Vector3.forward * 2f;
        inviteTarget.AddComponent<EnemyFearSource>();
        remoteCamera.transform.position = localCamera.transform.position;
        remoteCamera.transform.rotation = localCamera.transform.rotation;
        ViewRoot(remotePlayer, "firstPersonView").SetActive(true);
        remoteCamera.GetComponent<AudioListener>().enabled = false;
        Physics.SyncTransforms();
        yield return null;
        yield return null;
        Assert.That(localFear.CurrentIntensity, Is.GreaterThan(0.9f));
        Assert.That(remoteFear.CurrentIntensity, Is.Zero);
        Assert.That(remoteCamera.fieldOfView, Is.EqualTo(65f));
        localPlayer.GetComponent<PlayerPresentation>().enabled = false;
        Assert.That(localFear.CurrentIntensity, Is.Zero);
        Assert.That(localCamera.fieldOfView, Is.EqualTo(65f));
    }

    [UnityTest]
    public IEnumerator CameraFacingAndStatesSyncWhileArtworkAndBillboardsStayLocal()
    {
        SpawnPlayers();
        ConfigureRuntimeSprites();
        yield return null;
        yield return null;
        PlayerVision owner = localPlayer.GetComponent<PlayerVision>();
        PlayerVision observed = remotePlayer.GetComponent<PlayerVision>();
        localPlayer.GetComponent<PlayerLook>().enabled = false;
        Camera camera = ViewRoot(localPlayer, "firstPersonView").GetComponentInChildren<Camera>(true);
        SpriteRenderer renderer = ViewRoot(remotePlayer, "worldView").GetComponentInChildren<SpriteRenderer>(true);
        camera.transform.rotation = Quaternion.Euler(20f, 90f, 0f);
        owner.SetAnimationState(CharacterAnimationState.Run);
        yield return WaitForAnimationState(owner, CharacterAnimationState.Run);
        Assert.That(owner.FacingYaw, Is.EqualTo(90f).Within(0.001f));
        Assert.That(owner.AnimationState, Is.EqualTo(CharacterAnimationState.Run));

        // Exercise the generated SyncVar serializer on a distinct observed player replica.
        var writer = new NetworkWriter();
        owner.OnSerialize(writer, true);
        observed.OnDeserialize(new NetworkReader(writer.ToArray()), true);
        Assert.That(observed.FacingYaw, Is.EqualTo(owner.FacingYaw));
        Assert.That(observed.AnimationState, Is.EqualTo(owner.AnimationState));
        yield return null;
        Assert.That(observed.DisplayedDirection, Is.EqualTo(CharacterDirection.North));
        Assert.That(renderer.sprite == testFrames[6] || renderer.sprite == testFrames[7], Is.True);

        Quaternion rootRotation = remotePlayer.transform.rotation;
        camera.transform.rotation = Quaternion.identity;
        yield return null;
        Assert.That(observed.DisplayedDirection, Is.EqualTo(CharacterDirection.East));
        Assert.That(renderer.sprite == testFrames[4] || renderer.sprite == testFrames[5], Is.True);
        Assert.That(remotePlayer.transform.rotation, Is.EqualTo(rootRotation));
        Assert.That(Quaternion.Angle(renderer.transform.rotation,
            PlayerVision.BillboardRotation(camera.transform.position, renderer.transform.position, 0f)), Is.LessThan(0.001f));
        Assert.That(ViewRoot(localPlayer, "worldView").activeInHierarchy, Is.False);

        owner.SetAnimationState(CharacterAnimationState.Attack);
        yield return WaitForAnimationState(owner, CharacterAnimationState.Attack);
        Assert.That(owner.AnimationState, Is.EqualTo(CharacterAnimationState.Attack));
        owner.ResumeMovementAnimation();
        yield return WaitForAnimationState(owner, CharacterAnimationState.Idle);
        Assert.That(owner.AnimationState, Is.EqualTo(CharacterAnimationState.Idle));
    }

    [UnityTest]
    public IEnumerator UseRequiresTheLocalCameraAVisibleTargetAndANewKeyPress()
    {
        Keyboard keyboard = InputSystem.AddDevice<Keyboard>();
        SpawnPlayers();
        yield return null;
        yield return null;

        PlayerInteraction localInteraction = localPlayer.GetComponent<PlayerInteraction>();
        PlayerInteraction remoteInteraction = remotePlayer.GetComponent<PlayerInteraction>();
        Camera camera = ViewRoot(localPlayer, "firstPersonView").GetComponentInChildren<Camera>(true);
        inviteTarget = GameObject.CreatePrimitive(PrimitiveType.Cube);
        inviteTarget.transform.position = camera.transform.position + camera.transform.forward * 2f;
        SteamInviteInteractable target = inviteTarget.AddComponent<SteamInviteInteractable>();
        Physics.SyncTransforms();
        StepInteraction(localInteraction);
        StepInteraction(remoteInteraction);
        Assert.That(localInteraction.CurrentTarget, Is.SameAs(target));
        Assert.That(remoteInteraction.CurrentTarget, Is.Null);

        Press(keyboard.eKey);
        InputSystem.Update();
        LogAssert.Expect(LogType.Error, "Steam invites require a SteamSession on NetworkManager.");
        StepInteraction(localInteraction);
        StepInteraction(remoteInteraction);
        InputSystem.Update();
        StepInteraction(localInteraction);
        Release(keyboard.eKey);
        InputSystem.Update();

        blocker = GameObject.CreatePrimitive(PrimitiveType.Cube);
        blocker.transform.position = camera.transform.position + camera.transform.forward;
        blocker.transform.localScale = Vector3.one * 0.25f;
        Physics.SyncTransforms();
        StepInteraction(localInteraction);
        Assert.That(localInteraction.CurrentTarget, Is.Null, "A solid obstruction must block interaction.");
        blocker.SetActive(false);

        target.enabled = false;
        StepInteraction(localInteraction);
        Assert.That(localInteraction.CurrentTarget, Is.Null);
        target.enabled = true;
        inviteTarget.transform.position = camera.transform.position + camera.transform.forward * 4.1f;
        Physics.SyncTransforms();
        StepInteraction(localInteraction);
        Assert.That(localInteraction.CurrentTarget, Is.Null, "Targets beyond 3.5 units must not be usable.");
        inviteTarget.transform.position = camera.transform.position + camera.transform.forward * 3.9f;
        Physics.SyncTransforms();
        StepInteraction(localInteraction);
        Assert.That(localInteraction.CurrentTarget, Is.SameAs(target), "The near face of the cube is within 3.5 units.");

        localInteraction.enabled = false;
        Assert.That(localInteraction.CurrentTarget, Is.Null);
        localInteraction.enabled = true;
        StepInteraction(localInteraction);
        Assert.That(localInteraction.CurrentTarget, Is.SameAs(target));
    }

    [UnityTest]
    public IEnumerator MouseDeltaTurnsOnlyTheOwnerAndUpdatesMovementInteractionAndFacing()
    {
        Mouse mouse = InputSystem.AddDevice<Mouse>();
        Keyboard keyboard = InputSystem.AddDevice<Keyboard>();
        SpawnPlayers();
        yield return null;
        yield return null;

        PlayerLook look = localPlayer.GetComponent<PlayerLook>();
        PlayerLook remoteLook = remotePlayer.GetComponent<PlayerLook>();
        Camera camera = ViewRoot(localPlayer, "firstPersonView").GetComponentInChildren<Camera>(true);
        Camera remoteCamera = ViewRoot(remotePlayer, "firstPersonView").GetComponentInChildren<Camera>(true);
        SetLookFocus(look, false);
        SetLookFocus(look, true);
        ToggleLookCapture(keyboard, look);
        Quaternion remoteRotation = remotePlayer.transform.rotation;
        Quaternion remoteCameraRotation = remoteCamera.transform.localRotation;
        Assert.That(typeof(PlayerLook).GetField("lookAction", BindingFlags.Instance | BindingFlags.NonPublic)
            .GetValue(remoteLook), Is.Null);

        SetMouseDelta(mouse, new Vector2(100f, 50f));
        StepLook(look);
        StepLook(remoteLook);
        Assert.That(Mathf.DeltaAngle(0f, localPlayer.transform.eulerAngles.y), Is.EqualTo(15f).Within(0.001f));
        Assert.That(Mathf.DeltaAngle(0f, camera.transform.localEulerAngles.x), Is.EqualTo(-7.5f).Within(0.001f));
        Assert.That(localPlayer.transform.eulerAngles.x, Is.EqualTo(0f).Within(0.001f));
        Assert.That(remotePlayer.transform.rotation, Is.EqualTo(remoteRotation));
        Assert.That(remoteCamera.transform.localRotation, Is.EqualTo(remoteCameraRotation));

        SetMouseDelta(mouse, new Vector2(0f, 10000f));
        StepLook(look);
        Assert.That(Mathf.DeltaAngle(0f, camera.transform.localEulerAngles.x), Is.EqualTo(-85f).Within(0.001f));
        SetMouseDelta(mouse, new Vector2(0f, -10000f));
        StepLook(look);
        Assert.That(Mathf.DeltaAngle(0f, camera.transform.localEulerAngles.x), Is.EqualTo(85f).Within(0.001f));
        SetMouseDelta(mouse, new Vector2(0f, 85f / 0.15f));
        StepLook(look);
        InputSystem.Update();

        Press(keyboard.wKey);
        InputSystem.Update();
        Vector3 before = localPlayer.transform.position;
        StepMovement(localPlayer);
        Vector3 displacement = localPlayer.transform.position - before;
        Vector3 expected = localPlayer.transform.forward * (5f * Time.deltaTime);
        Assert.That(displacement.x, Is.EqualTo(expected.x).Within(0.001f));
        Assert.That(displacement.z, Is.EqualTo(expected.z).Within(0.001f));
        Release(keyboard.wKey);
        InputSystem.Update();

        inviteTarget = GameObject.CreatePrimitive(PrimitiveType.Cube);
        inviteTarget.transform.position = camera.transform.position + camera.transform.forward * 2f;
        SteamInviteInteractable target = inviteTarget.AddComponent<SteamInviteInteractable>();
        Physics.SyncTransforms();
        PlayerInteraction interaction = localPlayer.GetComponent<PlayerInteraction>();
        StepInteraction(interaction);
        Assert.That(interaction.CurrentTarget, Is.SameAs(target));
        yield return new WaitForSeconds(0.2f);
        Assert.That(localPlayer.GetComponent<PlayerVision>().FacingYaw, Is.EqualTo(15f).Within(0.001f));
    }

    [UnityTest]
    public IEnumerator MouseLookReleasesCaptureForHudOverlayFocusLossAndDespawn()
    {
        Mouse mouse = InputSystem.AddDevice<Mouse>();
        Keyboard keyboard = InputSystem.AddDevice<Keyboard>();
        SpawnPlayers();
        yield return null;
        yield return null;
        PlayerLook look = localPlayer.GetComponent<PlayerLook>();
        SetLookFocus(look, false);
        SetLookFocus(look, true);
        ToggleLookCapture(keyboard, look);
        AssertCursorCaptured(look, true);
        remotePlayer.GetComponent<PlayerLook>().enabled = false;
        AssertCursorCaptured(look, true);

        ToggleLookCapture(keyboard, look);
        AssertCursorCaptured(look, false);
        Quaternion rotation = localPlayer.transform.rotation;
        SetMouseDelta(mouse, new Vector2(100f, 0f));
        StepLook(look);
        Assert.That(localPlayer.transform.rotation, Is.EqualTo(rotation));
        ToggleLookCapture(keyboard, look);
        AssertCursorCaptured(look, true);

        Assert.That(SteamSession.Instance, Is.Null);
        var overlayHost = new GameObject("Inactive Steam Overlay Test");
        overlayHost.SetActive(false);
        try
        {
            // Keep the session inactive: only simulate callback state, never initialize native Steam.
            SteamSession session = overlayHost.AddComponent<SteamSession>();
            PropertyInfo instance = typeof(SteamSession).GetProperty("Instance");
            PropertyInfo overlay = typeof(SteamSession).GetProperty("OverlayActive");
            instance.SetValue(null, session);
            overlay.SetValue(session, true);
            SetMouseDelta(mouse, new Vector2(100f, 0f));
            StepLook(look);
            AssertCursorCaptured(look, false);
            Assert.That(localPlayer.transform.rotation, Is.EqualTo(rotation));
            overlay.SetValue(session, false);
            StepLook(look);
            AssertCursorCaptured(look, true);
            Assert.That(localPlayer.transform.rotation, Is.EqualTo(rotation), "Recapturing must discard accumulated mouse motion.");
        }
        finally
        {
            typeof(SteamSession).GetProperty("Instance").SetValue(null, null);
            Object.DestroyImmediate(overlayHost);
        }

        look.enabled = false;
        AssertCursorCaptured(look, false);
        look.enabled = true;
        StepLook(look);
        AssertCursorCaptured(look, true);
        SetLookFocus(look, false);
        AssertCursorCaptured(look, false);
        SetLookFocus(look, true);
        StepLook(look);
        AssertCursorCaptured(look, false);
        ToggleLookCapture(keyboard, look);
        AssertCursorCaptured(look, true);
        NetworkServer.Destroy(localPlayer);
        yield return null;
        AssertCursorCaptured(look, false);
    }

    private static void AssertCursorCaptured(PlayerLook look, bool expected)
    {
        bool captured = (bool)typeof(PlayerLook).GetField("cursorCaptured", BindingFlags.Instance | BindingFlags.NonPublic)
            .GetValue(look);
        Assert.That(captured, Is.EqualTo(expected));
        // Batchmode has no interactive OS cursor; check the physical lock in the Editor's Test Runner.
        if (!Application.isBatchMode)
        {
            Assert.That(Cursor.lockState, Is.EqualTo(expected ? CursorLockMode.Locked : CursorLockMode.None));
            Assert.That(Cursor.visible, Is.EqualTo(!expected));
        }
    }

    private static void StepLook(PlayerLook look)
    {
        typeof(PlayerLook).GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(look, null);
    }

    private static void SetLookFocus(PlayerLook look, bool focused)
    {
        typeof(PlayerLook).GetMethod("OnApplicationFocus", BindingFlags.Instance | BindingFlags.NonPublic)
            .Invoke(look, new object[] { focused });
    }

    private void ToggleLookCapture(Keyboard keyboard, PlayerLook look)
    {
        Press(keyboard.escapeKey);
        InputSystem.Update();
        StepLook(look);
        Release(keyboard.escapeKey);
        InputSystem.Update();
    }

    private void SetMouseDelta(Mouse mouse, Vector2 delta)
    {
        Set(mouse.delta, delta);
        InputSystem.Update();
    }

    private static void StepInteraction(PlayerInteraction interaction)
    {
        typeof(PlayerInteraction).GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic)
            .Invoke(interaction, null);
    }

    private static IEnumerator WaitForAnimationState(PlayerVision vision, CharacterAnimationState state)
    {
        float timeout = Time.realtimeSinceStartup + 2f;
        while (vision.AnimationState != state && Time.realtimeSinceStartup < timeout)
            yield return null;
        Assert.That(vision.AnimationState, Is.EqualTo(state), "The owner's state command did not reach the server.");
    }

    [UnityTest]
    public IEnumerator RemoteWorldSpritesPlayManuallyAssignedFramesWithoutASheet()
    {
        SpawnPlayers();
        ConfigureRuntimeSprites(true);
        yield return null;
        yield return null;
        CharacterAnimation idle = testDefinition.GetAnimation(CharacterAnimationState.Idle);
        Assert.That(idle.AssignmentMode, Is.EqualTo(CharacterFrameAssignment.Manual));
        Assert.That(idle.SpriteSheet, Is.Null);
        Assert.That(idle.IsReady, Is.True);
        SpriteRenderer renderer = ViewRoot(remotePlayer, "worldView").GetComponentInChildren<SpriteRenderer>(true);
        Assert.That(renderer.sprite == testFrames[6] || renderer.sprite == testFrames[7], Is.True);
        Assert.That(remotePlayer.GetComponent<PlayerVision>().DisplayedDirection, Is.EqualTo(CharacterDirection.North));
        Assert.That(ViewRoot(localPlayer, "worldView").activeInHierarchy, Is.False);
    }

    private void ConfigureRuntimeSprites(bool manual = false)
    {
        testDefinition = ScriptableObject.CreateInstance<CharacterDefinition>();
        testSheet = new Texture2D(8, 1);
        testFrames = new Sprite[8];
        for (int index = 0; index < 8; index++)
            testFrames[index] = Sprite.Create(testSheet, new Rect(index, 0f, 1f, 1f), Vector2.one * 0.5f);
        var serializedDefinition = new SerializedObject(testDefinition);
        foreach (CharacterAnimationState state in System.Enum.GetValues(typeof(CharacterAnimationState)))
        {
            serializedDefinition.FindProperty(state.ToString().ToLowerInvariant())
                .FindPropertyRelative("spriteSheet").objectReferenceValue = manual ? null : testSheet;
        }
        serializedDefinition.ApplyModifiedPropertiesWithoutUndo();
        foreach (CharacterAnimationState state in System.Enum.GetValues(typeof(CharacterAnimationState)))
        {
            CharacterAnimation animation = testDefinition.GetAnimation(state);
            if (manual)
                animation.SetManualFrameCount(state == CharacterAnimationState.Dead ? 8 : 2);
            animation.AssignFrames(testFrames);
        }
        var vision = new SerializedObject(remotePlayer.GetComponent<PlayerVision>());
        vision.FindProperty("characterDefinition").objectReferenceValue = testDefinition;
        vision.ApplyModifiedPropertiesWithoutUndo();
    }

    private void CheckMovement(KeyControl key, Vector2 expectedDirection)
    {
        Press(key);
        InputSystem.Update();
        Vector3 before = localPlayer.transform.position;
        Vector3 remoteBefore = remotePlayer.transform.position;
        StepMovement(localPlayer);
        StepMovement(remotePlayer);
        Vector3 delta = localPlayer.transform.position - before;
        Vector2 expected = expectedDirection * (5f * Time.deltaTime);
        Assert.That(delta.x, Is.EqualTo(expected.x).Within(0.001f));
        Assert.That(delta.z, Is.EqualTo(expected.y).Within(0.001f));
        Assert.That(remotePlayer.transform.position, Is.EqualTo(remoteBefore));
        Assert.That(localPlayer.GetComponent<PlayerMovement>().IsMoving, Is.True);
        Assert.That(remotePlayer.GetComponent<PlayerMovement>().IsMoving, Is.False);
        Release(key);
        InputSystem.Update();
    }

    private static void StepMovement(GameObject player)
    {
        MethodInfo update = typeof(PlayerMovement).GetMethod("Update",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(update, Is.Not.Null);
        update.Invoke(player.GetComponent<PlayerMovement>(), null);
    }

    private static InputActionAsset RuntimeInput(GameObject player)
    {
        FieldInfo field = typeof(PlayerMovement).GetField("runtimeInputActions",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(field, Is.Not.Null);
        return (InputActionAsset)field.GetValue(player.GetComponent<PlayerMovement>());
    }

    private static GameObject LoadPrefab()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        Assert.That(prefab, Is.Not.Null);
        return prefab;
    }

    private static GameObject ViewRoot(GameObject player, string property)
    {
        PlayerPresentation presentation = player.GetComponent<PlayerPresentation>();
        Assert.That(presentation, Is.Not.Null);
        Object root = new SerializedObject(presentation).FindProperty(property).objectReferenceValue;
        Assert.That(root, Is.Not.Null);
        return (GameObject)root;
    }

    private static void AssertBinding(InputAction action, string path, string direction)
    {
        foreach (InputBinding binding in action.bindings)
        {
            if (binding.path == path && binding.name == direction && binding.isPartOfComposite)
                return;
        }

        Assert.Fail($"Missing {direction} WASD binding: {path}");
    }
}
#endif
