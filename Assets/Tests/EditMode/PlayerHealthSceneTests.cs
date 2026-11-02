using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public sealed class PlayerHealthSceneTests
{
    [Test]
    public void SceneHasASeparateHudAndPlayerHasSixHealth()
    {
        GameObject player = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/PREFABS/NetworkPlayer.prefab");
        Assert.That(player.GetComponent<PlayerHealth>(), Is.Not.Null);
        Assert.That(player.GetComponent<PlayerHealth>().CurrentHealth, Is.EqualTo(6));
        Assert.That(player.GetComponentInChildren<PlayerHealthHUD>(true), Is.Null);
        var scene = EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity", OpenSceneMode.Additive);
        try
        {
            PlayerHealthHUD hud = scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<PlayerHealthHUD>(true)).Single();
            Assert.That(hud.transform.parent, Is.Null);
            Assert.That(PrefabUtility.GetCorrespondingObjectFromSource(hud.gameObject),
                Is.EqualTo(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/PREFABS/PlayerHealthHUD.prefab")));
            SerializedObject serialized = new SerializedObject(hud);
            Assert.That(serialized.FindProperty("healthPanel").objectReferenceValue, Is.Not.Null);
            Assert.That(serialized.FindProperty("hearts").arraySize, Is.EqualTo(3));
            Canvas canvas = hud.GetComponent<Canvas>();
            Assert.That(canvas.renderMode, Is.EqualTo(RenderMode.ScreenSpaceOverlay));
            Assert.That(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/PREFABS/PlayerHealthHUD.prefab")
                .transform.localScale, Is.EqualTo(Vector3.one), "A zero-scale root hides the whole HUD.");
            Assert.That(hud.transform.localScale.x * hud.transform.localScale.y, Is.GreaterThan(0f));
            GameObject panel = (GameObject)serialized.FindProperty("healthPanel").objectReferenceValue;
            Assert.That(panel.GetComponent<UnityEngine.UI.Image>(), Is.Not.Null, "The background panel needs an Image.");
            foreach (RectTransform rect in hud.GetComponentsInChildren<RectTransform>(true).Where(r => r != hud.transform))
            {
                Assert.That(rect.localScale.x * rect.localScale.y, Is.Not.Zero, $"{rect.name} has zero scale.");
                Assert.That(rect.rect.width * rect.rect.height, Is.GreaterThan(0f), $"{rect.name} has no visible area.");
            }
        }
        finally
        {
            EditorSceneManager.CloseScene(scene, true);
        }
    }
}
