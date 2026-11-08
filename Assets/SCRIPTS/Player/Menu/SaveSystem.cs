using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

/// <summary>One player's saved profile (keyed by SteamID64; the host uses its own SteamID64).</summary>
[Serializable]
public sealed class PlayerSaveData
{
    public string key;
    public string name;
    public int characterIndex = -1;
    public int health = PlayerHealth.MaxHealth;
    public string passive = string.Empty;
    public List<string> slots = new List<string>();
}

/// <summary>An item lying on the base floor.</summary>
[Serializable]
public sealed class FloorItemSaveData
{
    public string itemId;
    public Vector3 position;
}

/// <summary>Everything a save slot stores: the shared progress, every player's profile and the base floor items.</summary>
[Serializable]
public sealed class GameSaveData
{
    public int version = 1;
    public string savedAt;
    public int round = 1;
    public float elapsed;
    public int gold;
    public int silver;
    public int bronze;
    public int turnedIn;
    public int money;
    public int roundReward;
    public List<PlayerSaveData> players = new List<PlayerSaveData>();
    public List<FloorItemSaveData> baseItems = new List<FloorItemSaveData>();

    public PlayerSaveData FindPlayer(string key)
    {
        if (string.IsNullOrEmpty(key))
            return null;
        foreach (PlayerSaveData player in players)
        {
            if (player != null && player.key == key)
                return player;
        }
        return null;
    }
}

/// <summary>
/// Reads and writes the <see cref="SlotCount"/> save files as JSON under <c>persistentDataPath/Saves</c>
/// (<see cref="DirectoryOverride"/> redirects them, e.g. for tests).
/// </summary>
public static class SaveSystem
{
    public const int SlotCount = 3;

    /// <summary>When set, save files go here instead of <c>persistentDataPath/Saves</c>.</summary>
    public static string DirectoryOverride { get; set; }

    public static string Directory =>
        string.IsNullOrEmpty(DirectoryOverride) ? Path.Combine(Application.persistentDataPath, "Saves") : DirectoryOverride;

    public static bool IsValidSlot(int slot) => slot >= 0 && slot < SlotCount;

    public static string PathFor(int slot) => Path.Combine(Directory, $"save{slot + 1}.json");

    public static bool Exists(int slot) => IsValidSlot(slot) && File.Exists(PathFor(slot));

    public static GameSaveData Load(int slot)
    {
        if (!Exists(slot))
            return null;
        try
        {
            return JsonUtility.FromJson<GameSaveData>(File.ReadAllText(PathFor(slot)));
        }
        catch (Exception exception)
        {
            Debug.LogError($"Could not read save slot {slot + 1}: {exception.Message}");
            return null;
        }
    }

    public static bool Write(int slot, GameSaveData data)
    {
        if (!IsValidSlot(slot) || data == null)
            return false;
        try
        {
            System.IO.Directory.CreateDirectory(Directory);
            data.savedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
            string path = PathFor(slot);
            string temporary = path + ".tmp";
            File.WriteAllText(temporary, JsonUtility.ToJson(data, true));
            if (File.Exists(path))
                File.Delete(path);
            File.Move(temporary, path);
            return true;
        }
        catch (Exception exception)
        {
            Debug.LogError($"Could not write save slot {slot + 1}: {exception.Message}");
            return false;
        }
    }

    public static void Delete(int slot)
    {
        if (Exists(slot))
            File.Delete(PathFor(slot));
    }

    /// <summary>One-line summary for the menu's slot buttons.</summary>
    public static string Describe(int slot)
    {
        GameSaveData data = Load(slot);
        if (data == null)
            return Exists(slot) ? $"Save {slot + 1} - Unreadable" : $"Save {slot + 1} - New Game";
        int agimats = data.gold + data.silver + data.bronze;
        return $"Save {slot + 1} - Night {data.round}  {AgimatBank.FormatPeso(data.money)}  {agimats} agimats  ({data.savedAt})";
    }
}
