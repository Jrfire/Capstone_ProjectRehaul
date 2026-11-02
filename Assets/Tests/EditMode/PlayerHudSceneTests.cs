using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public sealed class PlayerHudSceneTests
{
    [Test]
    public void SceneHasASeparateHudAndPlayerHasSixHealthAndFullStamina()
    {
        GameObject player = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/PREFABS/NetworkPlayer.prefab");
        Assert.That(player.GetComponent<PlayerHealth>(), Is.Not.Null);
        Assert.That(player.GetComponent<PlayerHealth>().CurrentHealth, Is.EqualTo(6));
        Assert.That(player.GetComponent<PlayerStamina>().CurrentStamina, Is.EqualTo(PlayerStamina.MaxStamina));
        Assert.That(PlayerStamina.MaxStamina, Is.EqualTo(100f));
        Assert.That(player.GetComponentInChildren<PlayerHud>(true), Is.Null);
        var scene = EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity", OpenSceneMode.Additive);
        try
        {
            PlayerHud hud = scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<PlayerHud>(true)).Single();
            Assert.That(hud.transform.parent, Is.Null);
            Assert.That(PrefabUtility.GetCorrespondingObjectFromSource(hud.gameObject),
                Is.EqualTo(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/PREFABS/PlayerHud.prefab")));
            SerializedObject serialized = new SerializedObject(hud);
            Assert.That(serialized.FindProperty("healthPanel").objectReferenceValue, Is.Not.Null);
            Assert.That(serialized.FindProperty("hearts").arraySize, Is.EqualTo(3));
            Canvas canvas = hud.GetComponent<Canvas>();
            Assert.That(canvas.renderMode, Is.EqualTo(RenderMode.ScreenSpaceOverlay));
            Assert.That(hud.transform.localScale.x * hud.transform.localScale.y, Is.GreaterThan(0f),
                "The overlay canvas drives its root scale; the scene HUD must stay visible.");
            GameObject panel = (GameObject)serialized.FindProperty("healthPanel").objectReferenceValue;
            Assert.That(panel.GetComponent<UnityEngine.UI.Image>(), Is.Not.Null, "The background panel needs an Image.");
            GameObject staminaPanel = (GameObject)serialized.FindProperty("staminaPanel").objectReferenceValue;
            var fill = (UnityEngine.UI.Image)serialized.FindProperty("staminaFill").objectReferenceValue;
            Assert.That(staminaPanel, Is.Not.Null);
            Assert.That(staminaPanel.transform.parent, Is.EqualTo(hud.transform));
            Assert.That(staminaPanel.GetComponent<UnityEngine.UI.Image>().sprite.name, Is.EqualTo("StaminaBackground"));
            Assert.That(fill.transform.parent, Is.EqualTo(staminaPanel.transform));
            Assert.That(fill.sprite.name, Is.EqualTo("StaminaFills"));
            Assert.That(fill.type, Is.EqualTo(UnityEngine.UI.Image.Type.Filled));
            Assert.That(fill.fillMethod, Is.EqualTo(UnityEngine.UI.Image.FillMethod.Horizontal));
            Assert.That(fill.fillOrigin, Is.EqualTo((int)UnityEngine.UI.Image.OriginHorizontal.Left));
            Assert.That(serialized.FindProperty("lowStaminaFraction").floatValue, Is.EqualTo(0.3f));
            Assert.That(serialized.FindProperty("fullHeart").objectReferenceValue, Is.Not.Null,
                "Renaming the HUD must keep the heart sprites assigned in the scene.");
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
