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

    // Palette (the cart's own colours): dark-wood frames, a warm wood board, cream paper and buttons, brown ink,
    // and the mango orange of the hand-over tray for the one main action per screen and for headings.
    public static readonly Color Wood = new Color(0.40f, 0.25f, 0.15f), DarkWood = new Color(0.22f, 0.13f, 0.08f),
        Cream = new Color(0.99f, 0.95f, 0.86f), CreamDim = new Color(0.93f, 0.83f, 0.68f),
        Ink = new Color(0.17f, 0.10f, 0.05f), Accent = new Color(0.95f, 0.55f, 0.16f), AccentDark = new Color(0.66f, 0.33f, 0.07f),
        Gold = new Color(1f, 0.78f, 0.24f), Shadow = new Color(0f, 0f, 0f, 0.35f);
    static readonly Color CreamLip = new Color(0.72f, 0.58f, 0.40f);
    const string RoundedPath = "Assets/_Project/Materials/UI/Rounded.png";
    const int RoundedPx = 64, RoundedRadiusPx = 24;      // 9-slice sprite: 24 px corners on a 64 px square
    static Sprite rounded;

    // Set by the scene builder once the Thai font exists; null = TMP default.
    public static TMP_FontAsset Font;

    static AudioClip press, release;

    // A world-space canvas `sizeMm` big, centred on `pos`, facing along `rot`'s forward (the reader stands on the -forward side).
    // Drawn as a card: soft shadow, a rounded frame a shade darker than `background`, and the rounded board inset 6 mm.
    public static RectTransform Panel(string name, Transform parent, Vector3 pos, Quaternion rot, Vector2 sizeMm, Color background)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Canvas), typeof(TrackedDeviceGraphicRaycaster));
        go.transform.SetParent(parent, false);
        go.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
        var rt = (RectTransform)go.transform;
        rt.position = pos; rt.rotation = rot;
        rt.sizeDelta = sizeMm;
        rt.localScale = Vector3.one * 0.001f;
        Rounded(rt, "Shadow", new Vector2(0f, -5f), sizeMm + new Vector2(6f, 6f), Shadow, 16f);
        Rounded(rt, "Frame", Vector2.zero, sizeMm, Color.Lerp(background, Ink, 0.55f), 14f);
        Rounded(rt, "Board", Vector2.zero, sizeMm - new Vector2(12f, 12f), background, 10f);
        return rt;
    }

    // A rounded rectangle (decoration, never a raycast target), `radiusMm` corners, centred on `posMm`.
    public static Image Rounded(RectTransform parent, string name, Vector2 posMm, Vector2 sizeMm, Color colour, float radiusMm)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        rt.anchoredPosition = posMm; rt.sizeDelta = sizeMm;
        var img = go.GetComponent<Image>();
        img.sprite = RoundedSprite(); img.type = Image.Type.Sliced;
        img.pixelsPerUnitMultiplier = RoundedRadiusPx / Mathf.Max(1f, radiusMm);   // canvas: 1 unit = 1 mm, 100 px per unit
        img.color = colour; img.raycastTarget = false;
        return img;
    }

    // Short accent rule under a heading.
    public static Image Rule(RectTransform parent, Vector2 posMm, float widthMm) => Rounded(parent, "Rule", posMm, new Vector2(widthMm, 4f), Accent, 2f);

    // Button at `posMm` (from the panel centre). Poke depresses the face 4 mm with a click (Lab 5 PokeButtonFeedback);
    // hover/select/release colours come from the Button's colour tint. A rounded face over a darker lip (the face
    // sinks onto it when poked); `primary` = the screen's main action, orange with cream text.
    public static Button Button(RectTransform panel, string label, Vector2 posMm, Vector2 sizeMm, UnityAction onClick = null, bool primary = false)
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

        var lip = Rounded(rt, "Lip", new Vector2(0f, -3f), Vector2.zero, primary ? AccentDark : CreamLip, 8f);
        lip.rectTransform.anchorMin = Vector2.zero; lip.rectTransform.anchorMax = Vector2.one; lip.rectTransform.sizeDelta = Vector2.zero;
        lip.rectTransform.anchoredPosition = new Vector2(0f, -3f);

        var img = Rounded(rt, "Face", Vector2.zero, Vector2.zero, primary ? Accent : Cream, 8f);
        var frt = img.rectTransform;
        frt.anchorMin = Vector2.zero; frt.anchorMax = Vector2.one; frt.sizeDelta = Vector2.zero;

        var b = go.GetComponent<Button>();
        b.targetGraphic = img;
        var colors = b.colors;      // tints multiply the face colour, so the same set works for cream and orange faces
        colors.normalColor = Color.white; colors.highlightedColor = new Color(1f, 0.9f, 0.74f);
        colors.pressedColor = new Color(0.86f, 0.76f, 0.64f); colors.selectedColor = Color.white;
        colors.disabledColor = new Color(0.74f, 0.68f, 0.6f, 1f);      // opaque: locked reads as greyed card, not see-through
        colors.fadeDuration = 0.05f;
        b.colors = colors;
        var nav = b.navigation; nav.mode = Navigation.Mode.None; b.navigation = nav;

        var fb = go.AddComponent<PokeButtonFeedback>();
        fb.face = frt; fb.depth = 4f; fb.pressSound = press; fb.releaseSound = release;

        var t = Label(frt, label, Vector2.zero, sizeMm - new Vector2(6, 4), BodySize, primary ? Cream : Ink, style: FontStyles.Bold);
        t.rectTransform.anchorMin = Vector2.zero; t.rectTransform.anchorMax = Vector2.one; t.rectTransform.sizeDelta = new Vector2(-6, -4);
        if (onClick != null) UnityEventTools.AddPersistentListener(b.onClick, onClick);
        return b;
    }

    public static TMP_Text Label(RectTransform parent, string text, Vector2 posMm, Vector2 sizeMm, float fontSize, Color colour,
                                 TextAlignmentOptions align = TextAlignmentOptions.Center, FontStyles style = FontStyles.Normal)
    {
        var go = new GameObject("Label", typeof(RectTransform));
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        rt.anchoredPosition = posMm; rt.sizeDelta = sizeMm;
        var t = go.AddComponent<TextMeshProUGUI>();
        if (Font) t.font = Font;
        t.text = text; t.fontSize = Mathf.Max(fontSize, BodySize * 0.85f); t.color = colour; t.alignment = align;
        t.fontStyle = style;
        t.raycastTarget = false;
        t.textWrappingMode = TextWrappingModes.Normal;
        return t;
    }

    // White rounded square, 9-sliced (generated once, like the star sprite: no art dependency).
    static Sprite RoundedSprite()
    {
        if (rounded) return rounded;
        if (!System.IO.File.Exists(RoundedPath))
        {
            var tex = new Texture2D(RoundedPx, RoundedPx, TextureFormat.RGBA32, false);
            float r = RoundedRadiusPx, c = RoundedPx / 2f;
            for (int y = 0; y < RoundedPx; y++)
                for (int x = 0; x < RoundedPx; x++)
                {
                    // Distance outside the rounded square (negative inside), 1 px anti-aliased edge.
                    float qx = Mathf.Abs(x + 0.5f - c) - (c - r), qy = Mathf.Abs(y + 0.5f - c) - (c - r);
                    float d = new Vector2(Mathf.Max(qx, 0f), Mathf.Max(qy, 0f)).magnitude + Mathf.Min(Mathf.Max(qx, qy), 0f) - r;
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01(0.5f - d)));
                }
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(RoundedPath));
            System.IO.File.WriteAllBytes(RoundedPath, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(RoundedPath);
        }
        var imp = (TextureImporter)AssetImporter.GetAtPath(RoundedPath);
        var border = new Vector4(RoundedRadiusPx, RoundedRadiusPx, RoundedRadiusPx, RoundedRadiusPx);
        if (imp.textureType != TextureImporterType.Sprite || imp.spriteImportMode != SpriteImportMode.Single || imp.spriteBorder != border)
        {
            // Single mode: the 9-slice border lives on the one sprite (Multiple mode would leave it unsliced, stretched).
            imp.textureType = TextureImporterType.Sprite; imp.spriteImportMode = SpriteImportMode.Single;
            imp.alphaIsTransparency = true; imp.mipmapEnabled = false;
            imp.spriteBorder = border;
            imp.wrapMode = TextureWrapMode.Clamp; imp.filterMode = FilterMode.Bilinear;
            imp.SaveAndReimport();
        }
        return rounded = AssetDatabase.LoadAssetAtPath<Sprite>(RoundedPath);
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
