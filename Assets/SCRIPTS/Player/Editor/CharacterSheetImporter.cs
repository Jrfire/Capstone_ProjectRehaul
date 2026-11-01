using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.U2D.Sprites;
using UnityEngine;

public static class CharacterSheetImporter
{
    public static Rect[] BuildGrid(int width, int height, Vector2Int cellSize, bool directional)
    {
        if (cellSize.x <= 0 || cellSize.y <= 0 || width <= 0 || height <= 0 ||
            width % cellSize.x != 0 || height % cellSize.y != 0)
            throw new ArgumentException("The PNG dimensions must be exact multiples of the positive cell width and height.");

        int columns = width / cellSize.x;
        int rows = height / cellSize.y;
        int count = checked(columns * rows);
        if (directional && count % 4 != 0)
            throw new ArgumentException($"The grid contains {count} frames. Idle, Walk, Run, and Attack need a multiple of four to split equally into West, South, East, North.");

        var rects = new Rect[count];
        for (int row = 0; row < rows; row++)
        {
            for (int column = 0; column < columns; column++)
            {
                rects[row * columns + column] = new Rect(column * cellSize.x,
                    height - (row + 1) * cellSize.y, cellSize.x, cellSize.y);
            }
        }
        return rects;
    }

    public static void Generate(CharacterDefinition definition, CharacterAnimationState state)
    {
        if (definition == null)
            throw new ArgumentNullException(nameof(definition));

        CharacterAnimation animation = definition.GetAnimation(state);
        if (animation.AssignmentMode != CharacterFrameAssignment.Automatic)
            throw new InvalidOperationException("Switch this state to Automatic before generating frames. Manual sprites were not changed.");
        string path = AssetDatabase.GetAssetPath(animation.SpriteSheet);
        if (string.IsNullOrEmpty(path) || !path.StartsWith("Assets/", StringComparison.Ordinal) ||
            !string.Equals(Path.GetExtension(path), ".png", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Assign a PNG texture from the project's Assets folder.");
        if (!(AssetImporter.GetAtPath(path) is TextureImporter importer))
            throw new InvalidOperationException("The assigned PNG has no texture importer.");
        if (animation.PixelsPerUnit <= 0f || float.IsNaN(animation.PixelsPerUnit) ||
            float.IsInfinity(animation.PixelsPerUnit) || animation.FramesPerSecond <= 0f ||
            float.IsNaN(animation.FramesPerSecond) || float.IsInfinity(animation.FramesPerSecond))
            throw new ArgumentException("Pixels Per Unit and Frames Per Second must be finite values greater than zero.");

        importer.GetSourceTextureWidthAndHeight(out int width, out int height);
        if (Math.Max(width, height) > 16384)
            throw new ArgumentException("Sprite sheets must be no larger than 16384 pixels on either axis.");
        Rect[] grid = BuildGrid(width, height, animation.CellSize, animation.Directional);

        Undo.RecordObject(importer, "Slice character sprite sheet");
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Multiple;
        importer.spritePixelsPerUnit = animation.PixelsPerUnit;
        importer.npotScale = TextureImporterNPOTScale.None;
        importer.maxTextureSize = Mathf.Max(importer.maxTextureSize, Mathf.NextPowerOfTwo(Math.Max(width, height)));
        importer.filterMode = FilterMode.Point;
        importer.mipmapEnabled = false;
        importer.alphaIsTransparency = true;
        var textureSettings = new TextureImporterSettings();
        importer.ReadTextureSettings(textureSettings);
        textureSettings.spriteMeshType = SpriteMeshType.FullRect;
        importer.SetTextureSettings(textureSettings);

        var factories = new SpriteDataProviderFactories();
        factories.Init();
        ISpriteEditorDataProvider provider = factories.GetSpriteEditorDataProviderFromObject(importer);
        if (provider == null)
            throw new InvalidOperationException("The Unity 2D Sprite package could not provide a sprite-sheet editor.");
        provider.InitSpriteEditorDataProvider();
        SpriteRect[] previous = provider.GetSpriteRects();
        var rects = new SpriteRect[grid.Length];
        string prefix = Path.GetFileNameWithoutExtension(path);
        for (int i = 0; i < grid.Length; i++)
        {
            SpriteRect existing = previous.FirstOrDefault(rect => rect.rect == grid[i]);
            rects[i] = new SpriteRect
            {
                name = $"{prefix}_{i:D4}",
                rect = grid[i],
                alignment = SpriteAlignment.Center,
                pivot = new Vector2(0.5f, 0.5f),
                spriteID = existing != null ? existing.spriteID : GUID.Generate()
            };
        }
        ISpriteNameFileIdDataProvider names = provider.GetDataProvider<ISpriteNameFileIdDataProvider>();
        if (names == null)
            throw new InvalidOperationException("The texture importer does not support stable sprite IDs.");

        provider.SetSpriteRects(rects);
        names.SetNameFileIdPairs(rects.Select(rect => new SpriteNameFileIdPair(rect.name, rect.spriteID)));
        provider.Apply();
        importer.SaveAndReimport();

        Dictionary<string, Sprite> imported = AssetDatabase.LoadAllAssetsAtPath(path)
            .OfType<Sprite>().ToDictionary(sprite => sprite.name);
        var frames = new Sprite[rects.Length];
        for (int i = 0; i < rects.Length; i++)
        {
            if (!imported.TryGetValue(rects[i].name, out frames[i]))
                throw new InvalidOperationException($"Unity did not import frame '{rects[i].name}'. The definition was not updated.");
        }

        Undo.RecordObject(definition, "Assign character animation frames");
        animation.AssignFrames(frames);
        EditorUtility.SetDirty(definition);
        AssetDatabase.SaveAssetIfDirty(definition);
    }
}
