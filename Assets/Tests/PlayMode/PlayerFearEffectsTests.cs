using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

public sealed class PlayerFearEffectsTests
{
    private readonly List<GameObject> objects = new List<GameObject>();
    private Camera camera;
    private PlayerFearEffects fear;
    private EnemyFearSource source;

    [SetUp]
    public void Setup()
    {
        GameObject viewer = Track(new GameObject("Fear viewer"));
        camera = viewer.AddComponent<Camera>();
        camera.transform.position = new Vector3(2000f, 10f, 0f);
        camera.fieldOfView = 65f;
        camera.aspect = 1f;
        camera.farClipPlane = 40f;
        fear = viewer.AddComponent<PlayerFearEffects>();
        fear.buildUpSpeed = 1000f;
        fear.recoverySpeed = 1000f;
        source = CreateSource(camera.transform.position + Vector3.forward * 2f);
        source.fullIntensityDistance = 2f;
        source.maxDistance = 12f;
    }

    [TearDown]
    public void TearDown()
    {
        for (int index = objects.Count - 1; index >= 0; index--)
            Object.DestroyImmediate(objects[index]);
        objects.Clear();
    }

    [UnityTest]
    public IEnumerator ProximityScalesVisiblePanicAndLookingAwayRecovers()
    {
        yield return Frames();
        Assert.That(fear.CurrentIntensity, Is.EqualTo(1f).Within(0.001f));
        source.transform.position = camera.transform.position + Vector3.forward * 7f;
        yield return Frames();
        Assert.That(fear.VisibleIntensity, Is.EqualTo(0.5f).Within(0.001f));
        Assert.That(fear.CurrentIntensity, Is.EqualTo(0.5f).Within(0.001f));
        camera.transform.rotation = Quaternion.Euler(0f, 180f, 0f);
        yield return Frames();
        Assert.That(fear.CurrentIntensity, Is.Zero);
        Assert.That(camera.fieldOfView, Is.EqualTo(65f));
        camera.transform.rotation = Quaternion.identity;
        source.transform.position = camera.transform.position + Vector3.forward * 13f;
        yield return Frames();
        Assert.That(fear.CurrentIntensity, Is.Zero);
    }

    [UnityTest]
    public IEnumerator WallsBlockButTriggersAndPlayerCollidersDoNot()
    {
        GameObject wall = Track(GameObject.CreatePrimitive(PrimitiveType.Cube));
        wall.transform.position = camera.transform.position + Vector3.forward;
        wall.transform.localScale = new Vector3(4f, 4f, 0.2f);
        yield return Frames();
        Assert.That(fear.CurrentIntensity, Is.Zero);
        wall.GetComponent<Collider>().isTrigger = true;
        yield return Frames();
        Assert.That(fear.CurrentIntensity, Is.EqualTo(1f));
        wall.transform.SetParent(camera.transform);
        wall.GetComponent<Collider>().isTrigger = false;
        yield return Frames();
        Assert.That(fear.CurrentIntensity, Is.EqualTo(1f));
    }

    [UnityTest]
    public IEnumerator HiddenCulledAndDisabledSourcesDoNotTriggerAndPartialVisibilityCounts()
    {
        source.visibilityRenderer.enabled = false;
        yield return Frames();
        Assert.That(fear.CurrentIntensity, Is.Zero);
        source.visibilityRenderer.enabled = true;
        camera.cullingMask = 0;
        yield return Frames();
        Assert.That(fear.CurrentIntensity, Is.Zero);
        camera.cullingMask = -1;
        source.enabled = false;
        yield return Frames();
        Assert.That(fear.CurrentIntensity, Is.Zero);
        source.enabled = true;
        camera.farClipPlane = 1f;
        yield return Frames();
        Assert.That(fear.CurrentIntensity, Is.Zero);
        camera.farClipPlane = 40f;

        GameObject wall = Track(GameObject.CreatePrimitive(PrimitiveType.Cube));
        wall.transform.position = camera.transform.position + Vector3.forward;
        wall.transform.localScale = new Vector3(4f, 0.15f, 0.1f);
        yield return Frames();
        Assert.That(fear.CurrentIntensity, Is.EqualTo(1f),
            "The center is blocked, but the top and bottom are still visible.");
        wall.transform.localScale = new Vector3(4f, 4f, 0.1f);
        yield return Frames();
        Assert.That(fear.CurrentIntensity, Is.Zero);
    }

    [UnityTest]
    public IEnumerator StrongestSourceWinsAndEachCameraHasItsOwnIntensity()
    {
        source.intensity = 0.25f;
        EnemyFearSource stronger = CreateSource(camera.transform.position + new Vector3(0.8f, 0f, 2f));
        stronger.intensity = 0.75f;
        GameObject otherViewer = Track(new GameObject("Unaffected viewer"));
        Camera otherCamera = otherViewer.AddComponent<Camera>();
        otherCamera.transform.position = camera.transform.position;
        otherCamera.transform.rotation = Quaternion.Euler(0f, 180f, 0f);
        PlayerFearEffects otherFear = otherViewer.AddComponent<PlayerFearEffects>();
        yield return Frames();
        Assert.That(fear.CurrentIntensity, Is.EqualTo(stronger.StrengthAtDistance(
            Vector3.Distance(camera.transform.position, stronger.visibilityRenderer.bounds.center))).Within(0.001f));
        Assert.That(otherFear.CurrentIntensity, Is.Zero);
        stronger.gameObject.SetActive(false);
        yield return Frames();
        Assert.That(fear.CurrentIntensity, Is.EqualTo(0.25f));
    }

    [UnityTest]
    public IEnumerator FovShuddersWithinItsLimitAndRestoresOnDisableAndExternalFovChanges()
    {
        float largestOffset = 0f;
        for (int frame = 0; frame < 16; frame++)
        {
            yield return null;
            largestOffset = Mathf.Max(largestOffset, Mathf.Abs(camera.fieldOfView - 65f));
            Assert.That(Mathf.Abs(camera.fieldOfView - 65f), Is.LessThanOrEqualTo(fear.fovShudder + 0.001f));
        }
        Assert.That(largestOffset, Is.GreaterThan(0.05f));
        camera.fieldOfView = 75f;
        yield return Frames();
        fear.enabled = false;
        Assert.That(fear.CurrentIntensity, Is.Zero);
        Assert.That(camera.fieldOfView, Is.EqualTo(75f));
        fear.enabled = true;
        yield return Frames();
        source.gameObject.SetActive(false);
        yield return Frames();
        Assert.That(camera.fieldOfView, Is.EqualTo(75f));
        Assert.That(fear.CurrentIntensity, Is.Zero);
    }

    private static IEnumerator Frames()
    {
        Physics.SyncTransforms();
        float elapsed = 0f;
        int frames = 0;
        while (elapsed < 0.01f || frames < 2)
        {
            yield return null;
            elapsed += Time.deltaTime;
            frames++;
        }
    }

    private EnemyFearSource CreateSource(Vector3 position)
    {
        GameObject target = Track(GameObject.CreatePrimitive(PrimitiveType.Cube));
        target.transform.position = position;
        return target.AddComponent<EnemyFearSource>();
    }

    private GameObject Track(GameObject instance)
    {
        objects.Add(instance);
        return instance;
    }
}
