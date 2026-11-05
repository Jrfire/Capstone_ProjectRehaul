using System.Collections.Generic;
using Mirror;
using Steamworks;
using UnityEngine;

/// <summary>
/// Synced display name of a player, shown by the HUD nametag when another player aims at them. The owner sends its Steam
/// persona name on spawn (and again after every respawn/travel); without Steam it falls back to "Player {netId}".
/// </summary>
[DisallowMultipleComponent]
public sealed class PlayerName : NetworkBehaviour
{
    public const int MaxLength = 32;

    [SyncVar] private string playerName = string.Empty;

    private static readonly List<PlayerName> active = new List<PlayerName>();

    /// <summary>Enabled players, for aim tests (remote CharacterControllers are disabled, so physics rays miss them).</summary>
    public static IReadOnlyList<PlayerName> Active => active;

    public string DisplayName => string.IsNullOrWhiteSpace(playerName) ? $"Player {netId}" : playerName;

    private void OnEnable()
    {
        if (!active.Contains(this))
            active.Add(this);
    }

    private void OnDisable() => active.Remove(this);

    /// <summary>
    /// Ray distance to this player's CharacterController-shaped capsule within <paramref name="maxDistance"/>, or false.
    /// </summary>
    public bool TryAimDistance(Ray ray, float maxDistance, out float distance)
    {
        distance = 0f;
        if (!TryGetComponent(out CharacterController body) || maxDistance <= 0f)
            return false;
        Vector3 scale = transform.lossyScale;
        float radius = body.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z));
        float half = Mathf.Max(0f, body.height * 0.5f * Mathf.Abs(scale.y) - radius);
        Vector3 center = transform.TransformPoint(body.center);
        Vector3 a = center - transform.up * half;
        Vector3 b = center + transform.up * half;

        // Closest points between the ray segment and the capsule axis (Ericson, Real-Time Collision Detection 5.1.9).
        Vector3 d1 = ray.direction.normalized * maxDistance;
        Vector3 d2 = b - a;
        Vector3 r = ray.origin - a;
        float lengthSq = Vector3.Dot(d1, d1);
        float axisSq = Vector3.Dot(d2, d2);
        float f = Vector3.Dot(d2, r);
        float c = Vector3.Dot(d1, r);
        float dot = Vector3.Dot(d1, d2);
        float s, t;
        if (axisSq <= 1e-6f)
        {
            s = Mathf.Clamp01(-c / lengthSq);
            t = 0f;
        }
        else
        {
            float denominator = lengthSq * axisSq - dot * dot;
            s = denominator > 1e-6f ? Mathf.Clamp01((dot * f - c * axisSq) / denominator) : 0f;
            t = (dot * s + f) / axisSq;
            if (t < 0f)
            {
                t = 0f;
                s = Mathf.Clamp01(-c / lengthSq);
            }
            else if (t > 1f)
            {
                t = 1f;
                s = Mathf.Clamp01((dot - c) / lengthSq);
            }
        }

        Vector3 onRay = ray.origin + d1 * s;
        Vector3 onAxis = a + d2 * t;
        float gapSq = (onRay - onAxis).sqrMagnitude;
        if (gapSq > radius * radius)
            return false;
        // Step back from the closest approach to the capsule surface.
        distance = Mathf.Max(0f, s * maxDistance - Mathf.Sqrt(radius * radius - gapSq));
        return true;
    }

    public override void OnStartLocalPlayer()
    {
        string steamName = LocalSteamName();
        if (!string.IsNullOrEmpty(steamName))
            CmdSetName(steamName);
    }

    /// <summary>Server only: sets the synced name (trimmed, rich text removed, at most <see cref="MaxLength"/> characters).</summary>
    [Server]
    public void ServerSetName(string value) => playerName = Sanitize(value);

    [Command]
    private void CmdSetName(string value) => ServerSetName(value);

    public static string Sanitize(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;
        string clean = value.Replace("<", string.Empty).Replace(">", string.Empty).Replace('\n', ' ').Replace('\r', ' ').Trim();
        return clean.Length > MaxLength ? clean.Substring(0, MaxLength) : clean;
    }

    private static string LocalSteamName()
    {
        if (SteamSession.Instance == null || !SteamSession.Instance.Initialized)
            return null;
        try
        {
            return SteamFriends.GetPersonaName();
        }
        catch (System.Exception exception)
        {
            Debug.LogWarning($"Could not read the Steam persona name: {exception.Message}");
            return null;
        }
    }
}
