using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

public sealed class BlobShadowTests
{
    private readonly List<Object> objects = new List<Object>();

    [TearDown]
    public void TearDown()
    {
        for (int index = objects.Count - 1; index >= 0; index--)
            Object.DestroyImmediate(objects[index]);
        objects.Clear();
    }

    [UnityTest]
    public IEnumerator ShadowSitsOnTheGroundUnderTheFollowedSpriteAndIgnoresItsOwnColliders()
    {
        GameObject ground = Track(GameObject.CreatePrimitive(PrimitiveType.Cube));
        ground.transform.position = new Vector3(500f, 0f, 0f);
        ground.transform.localScale = new Vector3(20f, 1f, 20f);

        GameObject enemy = Track(new GameObject("Shadow test enemy"));
        enemy.transform.position = new Vector3(500f, 0.5f, 0f);
        GameObject body = Track(GameObject.CreatePrimitive(PrimitiveType.Cube));
        body.transform.SetParent(enemy.transform, false);
        body.transform.localPosition = new Vector3(0f, 0.8f, 0f);
        body.transform.localScale = Vector3.one * 0.3f;
        var sprite = new GameObject("WorldSprites").transform;
        sprite.SetParent(enemy.transform, false);
        sprite.localPosition = new Vector3(0f, 1.4f, 0f);

        GameObject shadowObject = Track(GameObject.CreatePrimitive(PrimitiveType.Quad));
        Object.DestroyImmediate(shadowObject.GetComponent<Collider>());
        shadowObject.transform.SetParent(enemy.transform, false);
        BlobShadow shadow = shadowObject.AddComponent<BlobShadow>();
        typeof(BlobShadow).GetField("follow", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(shadow, sprite);
        Physics.SyncTransforms();

        yield return null;
        Assert.That(shadow.IsGrounded, Is.True);
        Assert.That(shadowObject.GetComponent<MeshRenderer>().enabled, Is.True);
        Assert.That(shadowObject.transform.position.y, Is.EqualTo(0.52f).Within(0.005f),
            "Placed on the ground top, not on the enemy's own body collider.");
        Assert.That(shadowObject.transform.up.y, Is.EqualTo(0f).Within(0.01f), "The quad lies flat.");
        Assert.That(Vector3.Dot(shadowObject.transform.forward, Vector3.down), Is.GreaterThan(0.99f));
        Assert.That(shadowObject.transform.lossyScale.x, Is.EqualTo(shadow.Diameter).Within(0.01f));

        sprite.position += new Vector3(2f, 0f, -1f);
        yield return null;
        Assert.That(shadowObject.transform.position.x, Is.EqualTo(sprite.position.x).Within(0.001f));
        Assert.That(shadowObject.transform.position.z, Is.EqualTo(sprite.position.z).Within(0.001f));

        ground.SetActive(false);
        yield return null;
        Assert.That(shadow.IsGrounded, Is.False);
        Assert.That(shadowObject.GetComponent<MeshRenderer>().enabled, Is.False, "No ground below means no floating shadow.");
    }

    private T Track<T>(T instance) where T : Object
    {
        objects.Add(instance);
        return instance;
    }
}
