using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using UnityEngine.UI;

public sealed class HudContextTests
{
    private readonly List<Object> created = new List<Object>();

    [TearDown]
    public void TearDown()
    {
        foreach (Object item in created)
        {
            if (item != null)
                Object.DestroyImmediate(item);
        }
        created.Clear();
    }

    [UnityTest]
    public IEnumerator HoverRevealShowsTheButtonBackgroundOnlyWhileHovered()
    {
        var buttonObject = new GameObject("Hover Button", typeof(RectTransform), typeof(Image), typeof(Button));
        created.Add(buttonObject);
        Image image = buttonObject.GetComponent<Image>();
        image.color = new Color(0.2f, 0.3f, 0.4f, 1f);
        Button button = buttonObject.GetComponent<Button>();
        button.targetGraphic = image;
        button.transition = Selectable.Transition.None;
        HoverRevealGraphic reveal = buttonObject.AddComponent<HoverRevealGraphic>();
        SetHoverSettings(reveal, 0.9f, 0f);

        Assert.That(reveal.Target, Is.SameAs(image));
        Assert.That(reveal.CurrentAlpha, Is.EqualTo(0f), "Hidden until hovered.");
        Assert.That(image.raycastTarget, Is.True, "Invisible backgrounds must still receive the pointer.");

        var pointer = new PointerEventData(null);
        reveal.OnPointerEnter(pointer);
        yield return null;
        Assert.That(reveal.Hovered, Is.True);
        Assert.That(reveal.CurrentAlpha, Is.EqualTo(0.9f).Within(0.001f));
        Assert.That(image.color.r, Is.EqualTo(0.2f).Within(0.001f), "Only alpha changes.");

        reveal.OnPointerExit(pointer);
        yield return null;
        Assert.That(reveal.CurrentAlpha, Is.EqualTo(0f));

        button.interactable = false;
        reveal.OnPointerEnter(pointer);
        yield return null;
        Assert.That(reveal.CurrentAlpha, Is.EqualTo(0f), "Disabled buttons stay hidden by default.");
        button.interactable = true;
        yield return null;
        Assert.That(reveal.CurrentAlpha, Is.EqualTo(0.9f).Within(0.001f));

        buttonObject.SetActive(false);
        buttonObject.SetActive(true);
        Assert.That(reveal.Hovered, Is.False);
        Assert.That(reveal.CurrentAlpha, Is.EqualTo(0f), "Re-enabling resets the hover state.");

        SetHoverSettings(reveal, 1f, 0.5f);
        reveal.OnPointerEnter(pointer);
        yield return null;
        Assert.That(reveal.CurrentAlpha, Is.GreaterThan(0f).And.LessThan(1f), "A fade time eases the background in.");
    }

    [Test]
    public void LoadingTipsPickNonEmptyLinesAndAvoidRepeats()
    {
        var tips = ScriptableObject.CreateInstance<LoadingTips>();
        created.Add(tips);
        Assert.That(tips.PickRandom(), Is.Null);

        tips.tips = new List<string> { "A", "", "  ", "B" };
        for (int i = 0; i < 40; i++)
        {
            Assert.That(tips.PickRandom(), Is.EqualTo("A").Or.EqualTo("B"));
            Assert.That(tips.PickRandom("A"), Is.EqualTo("B"));
        }

        tips.tips = new List<string> { "Only" };
        Assert.That(tips.PickRandom("Only"), Is.EqualTo("Only"));
    }

    [Test]
    public void TheLoadingScreenShowsARandomTipFromTheProjectAsset()
    {
        GameObject instance = Object.Instantiate(
            AssetDatabase.LoadAssetAtPath<GameObject>("Assets/PREFABS/TravelLoadingScreen.prefab"));
        created.Add(instance);
        TravelLoadingScreen screen = instance.GetComponent<TravelLoadingScreen>();
        Assert.That(screen.Tips, Is.Not.Null);

        var seen = new HashSet<string>();
        for (int i = 0; i < 30; i++)
        {
            screen.ShowLoading();
            Assert.That(screen.Tips.tips, Does.Contain(screen.CurrentTip));
            Assert.That(screen.TipText.gameObject.activeSelf, Is.True);
            Assert.That(screen.TipText.text, Is.EqualTo(screen.CurrentTip));
            seen.Add(screen.CurrentTip);
            screen.HideLoading();
        }
        Assert.That(seen.Count, Is.GreaterThan(1), "Tips are picked at random.");
    }

    private static void SetHoverSettings(HoverRevealGraphic reveal, float hoverAlpha, float fadeSeconds)
    {
        var serialized = new SerializedObject(reveal);
        serialized.FindProperty("hoverAlpha").floatValue = hoverAlpha;
        serialized.FindProperty("fadeSeconds").floatValue = fadeSeconds;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }
}
