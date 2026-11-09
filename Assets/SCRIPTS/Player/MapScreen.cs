using Mirror;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// Local map panel on the PlayerHud, toggled with Player/Map (Tab). It draws the active scene's <see cref="MapManager"/>
/// street through <see cref="MapGraphic"/> with the local player as a dot, updating live while open. The player can keep
/// moving while it is open. Without a built map (e.g. at base) it shows <see cref="noMapMessage"/>. It closes when no
/// local player exists, controls are locked (menus, travel, death), the HUD is hidden or the Steam overlay opens.
/// </summary>
[DisallowMultipleComponent]
public sealed class MapScreen : MonoBehaviour
{
    [SerializeField] private InputActionAsset inputActions;
    [SerializeField, Tooltip("Shown while the map is open.")]
    private GameObject panel;
    [SerializeField] private MapGraphic mapGraphic;
    [SerializeField] private Text titleText;
    [SerializeField, Tooltip("Shown instead of the street when this scene has no generated map.")]
    private Text emptyText;
    [SerializeField, Tooltip("Font applied to the title and message texts.")]
    private Font font;
    [SerializeField] private string title = "Map";
    [SerializeField] private string noMapMessage = "There is no map of this place.";

    [Header("Jeepney label")]
    [SerializeField, Tooltip("Label drawn above the spawn on the map (where the jeepney home is parked). Created under the map graphic at runtime if unassigned.")]
    private Text jeepneyText;
    [SerializeField] private string jeepneyLabel = "Jeepney";
    [SerializeField, Tooltip("Offset in map pixels from the jeepney (or the spawn block centre without one).")]
    private Vector2 jeepneyLabelOffset = new Vector2(0f, 26f);
    [SerializeField, Min(1)] private int jeepneyLabelFontSize = 26;
    [SerializeField] private Color jeepneyLabelColor = new Color(1f, 0.85f, 0.35f, 1f);

    private InputAction toggleAction;

    public GameObject Panel => panel;
    public MapGraphic Graphic => mapGraphic;
    public Text TitleText => titleText;
    public Text EmptyText => emptyText;
    public Font Font => font;
    public bool IsOpen => panel != null && panel.activeSelf;
    public Text JeepneyText => jeepneyText;
    public string JeepneyLabel => jeepneyLabel;

    private static bool CanShow =>
        NetworkClient.active && NetworkClient.localPlayer != null && !PlayerTrance.IsInTrance(NetworkClient.localPlayer) && !PlayerControlLock.IsLocked && !HudVisibility.Hidden &&
        (SteamSession.Instance == null || !SteamSession.Instance.OverlayActive);

    private static MapManager CurrentMap => MapManager.Active != null && MapManager.Active.IsBuilt && MapManager.Active.BlockCount > 0
        ? MapManager.Active : null;

    private void Awake()
    {
        if (panel != null)
            panel.SetActive(false);
        if (titleText != null)
        {
            titleText.text = title;
            if (font != null)
                titleText.font = font;
        }
        if (emptyText != null)
        {
            emptyText.text = noMapMessage;
            if (font != null)
                emptyText.font = font;
        }
        if (jeepneyText == null && mapGraphic != null)
        {
            var label = new GameObject("JeepneyLabel", typeof(RectTransform));
            label.transform.SetParent(mapGraphic.transform, false);
            jeepneyText = label.AddComponent<Text>();
            jeepneyText.alignment = TextAnchor.LowerCenter;
            jeepneyText.horizontalOverflow = HorizontalWrapMode.Overflow;
            jeepneyText.verticalOverflow = VerticalWrapMode.Overflow;
            jeepneyText.raycastTarget = false;
            jeepneyText.fontSize = jeepneyLabelFontSize;
            jeepneyText.color = jeepneyLabelColor;
            var rect = (RectTransform)label.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0f);
            rect.sizeDelta = new Vector2(200f, 40f);
        }
        if (jeepneyText != null)
        {
            jeepneyText.text = jeepneyLabel;
            if (font != null)
                jeepneyText.font = font;
            else if (jeepneyText.font == null)
                jeepneyText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            jeepneyText.gameObject.SetActive(false);
        }

        InputAction configured = inputActions != null ? inputActions.FindAction("Player/Map") : null;
        if (configured == null || configured.type != InputActionType.Button)
            Debug.LogError("MapScreen requires a Button Player/Map action.", this);
        else
            toggleAction = GameSettings.TrackBindings(configured.Clone());
    }

    private void OnEnable() => toggleAction?.Enable();

    private void OnDisable()
    {
        toggleAction?.Disable();
        Close();
    }

    private void OnDestroy()
    {
        toggleAction?.Dispose();
        toggleAction = null;
    }

    public void Toggle()
    {
        if (IsOpen)
            Close();
        else
            Open();
    }

    /// <summary>Opens the map if the local player can see it; returns whether it is open.</summary>
    public bool Open()
    {
        if (panel == null || !CanShow)
            return false;
        panel.SetActive(true);
        Refresh();
        return true;
    }

    public void Close()
    {
        if (panel != null && panel.activeSelf)
            panel.SetActive(false);
    }

    private void Update()
    {
        if (toggleAction != null && toggleAction.WasPressedThisFrame())
            Toggle();
    }

    private void LateUpdate()
    {
        if (!IsOpen)
            return;
        if (!CanShow)
        {
            Close();
            return;
        }
        Refresh();
    }

    private void Refresh()
    {
        MapManager map = CurrentMap;
        if (emptyText != null && emptyText.gameObject.activeSelf != (map == null))
            emptyText.gameObject.SetActive(map == null);
        if (mapGraphic == null)
            return;
        GameObject player = NetworkClient.localPlayer != null ? NetworkClient.localPlayer.gameObject : null;
        mapGraphic.Show(map, map != null && player != null, player != null ? player.transform.position : Vector3.zero);
        UpdateJeepneyLabel(map);
    }

    private void UpdateJeepneyLabel(MapManager map)
    {
        if (jeepneyText == null)
            return;
        bool show = TryGetJeepneyPosition(map, out Vector3 world);
        if (jeepneyText.gameObject.activeSelf != show)
            jeepneyText.gameObject.SetActive(show);
        if (!show)
            return;
        // The label sits in the graphic's local space; anchors are centred, so subtract the rect centre.
        Rect rect = mapGraphic.rectTransform.rect;
        ((RectTransform)jeepneyText.transform).anchoredPosition = mapGraphic.WorldToMap(world) - rect.center + jeepneyLabelOffset;
    }

    /// <summary>The parked jeepney, else the spawn block's centre.</summary>
    public static bool TryGetJeepneyPosition(MapManager map, out Vector3 position)
    {
        position = default;
        if (map == null)
            return false;
        if (map.PlacedJeepney != null)
        {
            position = map.PlacedJeepney.transform.position;
            return true;
        }
        for (int i = 0; i < map.BlockCount; i++)
        {
            if (map.GetRole(i) == MapBlockRole.Spawn && map.GetBlock(i) != null)
            {
                position = map.GetBlock(i).transform.position;
                return true;
            }
        }
        return false;
    }
}
