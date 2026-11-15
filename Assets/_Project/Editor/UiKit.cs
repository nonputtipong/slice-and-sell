using System.Linq;
using HCD545;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.UI;

// Shared builder helpers for every world-space menu (wrist menu, shop sign, results board, tutorial cards).
// Same pattern as the Lab 5 poke panel: a world-space canvas at 1 unit = 1 mm, raycast by XRI's
// TrackedDeviceGraphicRaycaster, so every button takes poke (finger or controller tip) and ray (+ trigger or pinch).
// Size rules (assignment + GDD): poke targets >= 40 mm here (rule: 22 mm), gaps >= 16 mm (rule: 12 mm),
// text cap height >= 8 mm (legible at 0.5 m): body text is 14 canvas units.
public static class UiKit
{
    public const float MinButton = 40f, MinGap = 16f, BodySize = 14f, TitleSize = 20f;

    public static readonly Color Wood = new Color(0.45f, 0.28f, 0.16f), Cream = new Color(0.98f, 0.94f, 0.84f),
        Ink = new Color(0.16f, 0.1f, 0.05f), Accent = new Color(0.95f, 0.55f, 0.15f), Green = new Color(0.3f, 0.65f, 0.3f);

    // Set by the scene builder once the Thai font exists; null = TMP default.
    public static TMP_FontAsset Font;

    static AudioClip press, release;

    // A world-space canvas `sizeMm` big, centred on `pos`, facing along `rot`'s forward (the reader stands on the -forward side).
    public static RectTransform Panel(string name, Transform parent, Vector3 pos, Quaternion rot, Vector2 sizeMm, Color background)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Canvas), typeof(TrackedDeviceGraphicRaycaster));
        go.transform.SetParent(parent, false);
        go.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
        var rt = (RectTransform)go.transform;
        rt.position = pos; rt.rotation = rot;
        rt.sizeDelta = sizeMm;
        rt.localScale = Vector3.one * 0.001f;
        var bg = go.AddComponent<Image>();
        bg.color = background;
        bg.raycastTarget = false;
        return rt;
    }

    // Button at `posMm` (from the panel centre). Poke depresses the face 4 mm with a click (Lab 5 PokeButtonFeedback);
    // hover/select/release colours come from the Button's colour tint.
    public static Button Button(RectTransform panel, string label, Vector2 posMm, Vector2 sizeMm, UnityAction onClick = null)
    {
        sizeMm = Vector2.Max(sizeMm, new Vector2(MinButton, MinButton));
        if (!press) press = Clip("Assets/_Project/ThirdParty/Kenney/InterfaceSounds/click_001.ogg");
        if (!release) release = Clip("Assets/_Project/ThirdParty/Kenney/InterfaceSounds/click_002.ogg");

        var go = new GameObject(label + " button", typeof(RectTransform), typeof(Image), typeof(Button));
        var rt = (RectTransform)go.transform;
        rt.SetParent(panel, false);
        rt.anchoredPosition = posMm; rt.sizeDelta = sizeMm;
        var hit = go.GetComponent<Image>();
        hit.color = new Color(1, 1, 1, 0);       // invisible hit area that never moves

        var face = new GameObject("Face", typeof(RectTransform), typeof(Image));
        var frt = (RectTransform)face.transform;
        frt.SetParent(rt, false);
        frt.anchorMin = Vector2.zero; frt.anchorMax = Vector2.one; frt.sizeDelta = Vector2.zero;
        var img = face.GetComponent<Image>();
        img.color = Color.white; img.raycastTarget = false;

        var b = go.GetComponent<Button>();
        b.targetGraphic = img;
        var colors = b.colors;
        colors.normalColor = Cream; colors.highlightedColor = new Color(1f, 0.85f, 0.55f);
        colors.pressedColor = Accent; colors.selectedColor = Cream; colors.disabledColor = new Color(0.6f, 0.6f, 0.6f, 0.5f);
        colors.fadeDuration = 0.05f;
        b.colors = colors;
        var nav = b.navigation; nav.mode = Navigation.Mode.None; b.navigation = nav;

        var fb = go.AddComponent<PokeButtonFeedback>();
        fb.face = frt; fb.depth = 4f; fb.pressSound = press; fb.releaseSound = release;

        var t = Label(frt, label, Vector2.zero, sizeMm - new Vector2(6, 4), BodySize, Ink);
        t.rectTransform.anchorMin = Vector2.zero; t.rectTransform.anchorMax = Vector2.one; t.rectTransform.sizeDelta = new Vector2(-6, -4);
        if (onClick != null) UnityEventTools.AddPersistentListener(b.onClick, onClick);
        return b;
    }

    public static TMP_Text Label(RectTransform parent, string text, Vector2 posMm, Vector2 sizeMm, float fontSize, Color colour,
                                 TextAlignmentOptions align = TextAlignmentOptions.Center)
    {
        var go = new GameObject("Label", typeof(RectTransform));
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        rt.anchoredPosition = posMm; rt.sizeDelta = sizeMm;
        var t = go.AddComponent<TextMeshProUGUI>();
        if (Font) t.font = Font;
        t.text = text; t.fontSize = Mathf.Max(fontSize, BodySize * 0.85f); t.color = colour; t.alignment = align;
        t.raycastTarget = false;
        t.textWrappingMode = TextWrappingModes.Normal;
        return t;
    }

    // Checks the size rules on every button under `root`: logs and returns false if any is broken.
    public static bool CheckSizes(Transform root)
    {
        bool ok = true;
        var buttons = root.GetComponentsInChildren<Button>(true).Select(b => (RectTransform)b.transform).ToArray();
        foreach (var b in buttons)
        {
            var size = Vector2.Scale(b.rect.size, (Vector2)b.lossyScale) * 1000f;
            if (size.x < 22f || size.y < 22f) { Debug.LogError($"[UiKit] {b.name}: {size.x:F0} x {size.y:F0} mm, under 22 mm"); ok = false; }
        }
        for (int i = 0; i < buttons.Length; i++)
            for (int j = i + 1; j < buttons.Length; j++)
            {
                if (buttons[i].parent != buttons[j].parent) continue;
                float gap = Gap(buttons[i], buttons[j]);
                if (gap < 12f) { Debug.LogError($"[UiKit] {buttons[i].name} / {buttons[j].name}: gap {gap:F0} mm, under 12 mm"); ok = false; }
            }
        return ok;
    }

    static float Gap(RectTransform a, RectTransform b)
    {
        Rect ra = World(a), rb = World(b);
        float dx = Mathf.Max(0, Mathf.Max(ra.xMin - rb.xMax, rb.xMin - ra.xMax));
        float dy = Mathf.Max(0, Mathf.Max(ra.yMin - rb.yMax, rb.yMin - ra.yMax));
        return Mathf.Max(dx, dy) * 1000f;
    }

    // Rect in the parent canvas plane, in metres.
    static Rect World(RectTransform r)
    {
        var s = (Vector2)r.lossyScale;
        var size = Vector2.Scale(r.rect.size, s);
        var c = r.anchoredPosition * s.x;
        return new Rect(c - size / 2f, size);
    }

    static AudioClip Clip(string path) => AssetDatabase.LoadAssetAtPath<AudioClip>(path);
}
