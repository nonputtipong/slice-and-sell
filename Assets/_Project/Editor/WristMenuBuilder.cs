using SliceAndSell;
using TMPro;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.Inputs;

// Builds the wrist menu (GDD > 3D UI) and the hand-loss pause card (GDD > Failure cases) into the open scene,
// which must already hold the XR rig. Called from StallBuilder.
public static class WristMenuBuilder
{
    static readonly Color DarkWood = new Color(0.28f, 0.17f, 0.09f);

    public static WristMenu Build(Transform parentForMenu, OrderManager orders)
    {
        var origin = Object.FindAnyObjectByType<XROrigin>();
        var modality = Object.FindAnyObjectByType<XRInputModalityManager>();

        var menu = new GameObject("Wrist Menu").AddComponent<WristMenu>();
        menu.transform.SetParent(parentForMenu, false);
        menu.orders = orders;
        menu.origin = origin.transform;
        menu.head = origin.Camera.transform;
        menu.leftController = modality ? modality.leftController : null;

        // Fruit crate, 200 x 264 mm: dark frame, wood board, slats between the rows.
        // Two columns of 80 mm buttons at x = ±48 (16 mm gap); rows every 56 mm (40 mm button + 16 mm gap).
        var panel = UiKit.Panel("Crate panel", menu.transform, Vector3.zero, Quaternion.identity, new Vector2(200, 264), DarkWood);
        Rect(panel, Vector2.zero, new Vector2(188, 252), UiKit.Wood);
        foreach (float y in new[] { 40f, -16f, -72f }) Rect(panel, new Vector2(0, y), new Vector2(188, 3), DarkWood);
        UiKit.Label(panel, "Menu", new Vector2(0, 108), new Vector2(176, 24), UiKit.TitleSize, UiKit.Cream);

        var button = new Vector2(80, 40);
        menu.pauseLabel = Text(UiKit.Button(panel, "Pause", new Vector2(-48, 68), button, menu.TogglePause));
        UiKit.Button(panel, "Restart", new Vector2(48, 68), button, menu.Restart);
        menu.stanceLabel = Text(UiKit.Button(panel, "Standing", new Vector2(-48, 12), button, menu.ToggleSeated));
        UiKit.Button(panel, "Recenter", new Vector2(48, 12), button, menu.Recenter);
        UiKit.Button(panel, "-", new Vector2(-68, -44), new Vector2(40, 40), menu.VolumeDown);
        menu.volumeLabel = UiKit.Label(panel, "Volume\n100%", new Vector2(0, -44), new Vector2(64, 40), UiKit.BodySize, UiKit.Cream);
        UiKit.Button(panel, "+", new Vector2(68, -44), new Vector2(40, 40), menu.VolumeUp);
        UiKit.Button(panel, "Exit to menu", new Vector2(0, -100), new Vector2(176, 40), menu.ExitToMenu);
        menu.panel = panel;
        UiKit.CheckSizes(panel);
        panel.gameObject.SetActive(false);                  // WristMenu shows it

        // Hand-loss pause: the card is placed 1 m in front of the eyes when it appears.
        var loss = new GameObject("Hand Loss Pause").AddComponent<HandLossPause>();
        loss.transform.SetParent(parentForMenu, false);
        loss.head = menu.head;
        var card = UiKit.Panel("Show your hands card", loss.transform, Vector3.zero, Quaternion.identity, new Vector2(360, 130), UiKit.Cream);
        UiKit.Label(card, "Show your hands", new Vector2(0, 28), new Vector2(340, 40), UiKit.TitleSize * 1.5f, UiKit.Ink);
        UiKit.Label(card, "Paused. The game resumes when a hand or controller is back in view.", new Vector2(0, -24), new Vector2(340, 56), UiKit.TitleSize, UiKit.Ink);
        card.gameObject.SetActive(false);
        loss.card = card.gameObject;

        // The Lab 5 rig's own "hands not tracked" banner would show on top of the card.
        foreach (var b in Object.FindObjectsByType<HCD545.TrackingLostBanner>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            b.enabled = false;
            if (b.banner) b.banner.SetActive(false);
        }
        return menu;
    }

    static TMP_Text Text(Button b) => b.GetComponentInChildren<TMP_Text>(true);

    // Plain coloured rectangle on a panel (decoration, not a raycast target).
    static void Rect(RectTransform panel, Vector2 posMm, Vector2 sizeMm, Color colour)
    {
        var go = new GameObject("Wood", typeof(RectTransform), typeof(Image));
        var rt = (RectTransform)go.transform;
        rt.SetParent(panel, false);
        rt.anchoredPosition = posMm; rt.sizeDelta = sizeMm;
        var img = go.GetComponent<Image>();
        img.color = colour; img.raycastTarget = false;
    }
}
