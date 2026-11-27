using SliceAndSell;
using TMPro;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.UI;

// Builds the in-game menu (GDD > 3D UI), the MENU sign that opens it and the hand-loss pause card
// (GDD > Failure cases) into the open scene, which must already hold the XR rig. Called from StallBuilder.
public static class WristMenuBuilder
{
    static readonly Color DarkWood = new Color(0.28f, 0.17f, 0.09f);

    // The MENU sign: on the deck's free front-right corner (in front of the skewer cup, right of the Eto's blade),
    // tilted back 45 degrees toward the eyes, within a short reach for a poke, and in the stall so seated mode lowers it.
    static readonly Vector3 SignPos = new Vector3(0.585f, 0.93f, 0.31f);
    static readonly Quaternion SignTilt = Quaternion.Euler(45f, 0f, 0f);

    public static WristMenu Build(Transform parentForMenu, Transform stall, OrderManager orders)
    {
        var origin = Object.FindAnyObjectByType<XROrigin>();

        var menu = new GameObject("Wrist Menu").AddComponent<WristMenu>();
        menu.transform.SetParent(parentForMenu, false);
        menu.orders = orders;
        menu.origin = origin.transform;
        menu.head = origin.Camera.transform;

        // Fruit crate, 200 x 376 mm: dark frame, wood board, slats between the rows.
        // Two columns of 80 mm buttons at x = ±48 (16 mm gap); rows every 56 mm (40 mm button + 16 mm gap).
        var panel = UiKit.Panel("Crate panel", menu.transform, Vector3.zero, Quaternion.identity, new Vector2(200, 376), UiKit.Wood);
        foreach (float y in new[] { 96f, 40f, -16f, -72f, -128f }) Rect(panel, new Vector2(0, y), new Vector2(176, 3), DarkWood);
        menu.titleLabel = UiKit.Label(panel, "Menu", new Vector2(0, 164), new Vector2(176, 24), UiKit.TitleSize, UiKit.Cream, style: FontStyles.Bold);

        var button = new Vector2(80, 40);
        menu.pauseLabel = Text(UiKit.Button(panel, "Pause", new Vector2(-48, 124), button, menu.TogglePause, primary: true));
        menu.closeButton = UiKit.Button(panel, "Close", new Vector2(48, 124), button, menu.Close);
        UiKit.Button(panel, "Restart", new Vector2(-48, 68), button, menu.Restart);
        UiKit.Button(panel, "Recenter", new Vector2(48, 68), button, menu.Recenter);
        menu.stanceLabel = Text(UiKit.Button(panel, "Standing", new Vector2(0, 12), new Vector2(176, 40), menu.ToggleSeated));
        menu.dwellLabel = Text(UiKit.Button(panel, "Dwell to press: Off", new Vector2(0, -44), new Vector2(176, 40), menu.ToggleDwell));
        UiKit.Button(panel, "-", new Vector2(-68, -100), new Vector2(40, 40), menu.VolumeDown);
        menu.volumeLabel = UiKit.Label(panel, "Volume\n100%", new Vector2(0, -100), new Vector2(64, 40), UiKit.BodySize, UiKit.Cream);
        UiKit.Button(panel, "+", new Vector2(68, -100), new Vector2(40, 40), menu.VolumeUp);
        UiKit.Button(panel, "Exit to menu", new Vector2(0, -156), new Vector2(176, 40), menu.ExitToMenu);
        menu.panel = panel;
        UiKit.CheckSizes(panel);
        panel.gameObject.SetActive(false);                  // WristMenu shows it

        // The MENU sign: one big orange button on a small wooden card.
        var sign = UiKit.Panel("Menu sign", stall, stall.TransformPoint(SignPos), stall.rotation * SignTilt, new Vector2(110, 60), UiKit.Wood);
        UiKit.Button(sign, "MENU", Vector2.zero, new Vector2(94, 44), menu.Toggle, primary: true);
        UiKit.CheckSizes(sign);

        // Hand-loss pause: the card is placed 1 m in front of the eyes when it appears.
        var loss = new GameObject("Hand Loss Pause").AddComponent<HandLossPause>();
        loss.transform.SetParent(parentForMenu, false);
        loss.head = menu.head;
        var card = UiKit.Panel("Show your hands card", loss.transform, Vector3.zero, Quaternion.identity, new Vector2(400, 150), UiKit.Cream);
        UiKit.Label(card, "Show your hands", new Vector2(0, 34), new Vector2(370, 44), UiKit.TitleSize * 1.5f, UiKit.Ink, style: FontStyles.Bold);
        UiKit.Rule(card, new Vector2(0, 8), 70);
        UiKit.Label(card, "Paused. The game resumes when a hand or controller is back in view.", new Vector2(0, -30), new Vector2(370, 56), UiKit.TitleSize, UiKit.Ink);
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
