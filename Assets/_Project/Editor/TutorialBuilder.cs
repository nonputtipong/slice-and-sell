using SliceAndSell;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

// Level 1 tutorial: the text card (UiKit, with the intro's OK button), the glow halo and the bobbing arrow.
// Called from StallBuilder.BuildStall.
public static class TutorialBuilder
{
    const string MatDir = "Assets/_Project/Materials/Greybox";

    // Low over the middle of the counter, tilted to the eyes, inside poke reach for the OK button. Parented to the stall,
    // so it drops with it when seated, while the eyes drop twice as far (1.6 -> 1.2 m): it must clear the customer's face
    // and the order bubble beside it in both stances. Standing it sits ~42 degrees down, seated ~21 (face and bubble
    // above the eye line), and it stays above the sight line to the board.
    static readonly Vector3 CardPos = new Vector3(0f, 1.20f, 0.42f);
    static readonly Quaternion CardRot = Quaternion.Euler(35f, 0f, 0f);
    static readonly Vector2 CardSize = new Vector2(280f, 120f);     // mm

    public static Tutorial Build(Transform stall, OrderManager orders, CuttingBoard board, Knife knife, Dispenser guavaBin,
                                 Dispenser bowlStack, XRSocketInteractor tray)
    {
        var t = new GameObject("Tutorial").AddComponent<Tutorial>();
        t.transform.SetParent(stall, false);
        t.orders = orders; t.board = board; t.knife = knife.GetComponent<XRGrabInteractable>();
        t.guavaBin = guavaBin; t.bowlStack = bowlStack; t.tray = tray;

        // One label over the whole card (top-aligned for the intro, so the OK button fits below it).
        var card = UiKit.Panel("Tutorial card", t.transform, CardPos, CardRot, CardSize, UiKit.Wood);
        t.text = UiKit.Label(card, "", Vector2.zero, CardSize - new Vector2(20f, 12f), UiKit.BodySize, UiKit.Cream);
        t.okButton = UiKit.Button(card, "OK", new Vector2(0f, -32f), new Vector2(90f, 44f), t.Ok, primary: true).gameObject;
        UiKit.CheckSizes(card);
        t.card = card.gameObject;

        // Glow: an additive unlit halo around the target, pulsed per renderer (no post-processing on Quest).
        var glow = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        glow.name = "Tutorial glow";
        glow.transform.SetParent(t.transform, false);
        Object.DestroyImmediate(glow.GetComponent<Collider>());     // never blocks a grab or a ray
        t.glow = Unshadowed(glow, Unlit("Tutorial glow", new Color(1f, 0.9f, 0.5f, 0.35f), additive: true));

        // Arrow: a shaft and a diamond head (cube on its corner), tip down; Tutorial bobs it above the target.
        var arrow = new GameObject("Tutorial arrow").transform;
        arrow.SetParent(t.transform, false);
        var blue = Unlit("Tutorial arrow", new Color(0.3f, 0.75f, 1f), additive: false);   // the hover tint's colour: "take this"
        Part(PrimitiveType.Cylinder, "Shaft", arrow, new Vector3(0f, 0.045f, 0f), Quaternion.identity, new Vector3(0.012f, 0.025f, 0.012f), blue);
        Part(PrimitiveType.Cube, "Head", arrow, Vector3.zero, Quaternion.Euler(0f, 0f, 45f), new Vector3(0.035f, 0.035f, 0.012f), blue);
        t.arrow = arrow;

        card.gameObject.SetActive(false); glow.SetActive(false); arrow.gameObject.SetActive(false);
        return t;
    }

    static void Part(PrimitiveType type, string name, Transform parent, Vector3 pos, Quaternion rot, Vector3 scale, Material m)
    {
        var go = GameObject.CreatePrimitive(type);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.SetLocalPositionAndRotation(pos, rot);
        go.transform.localScale = scale;
        Object.DestroyImmediate(go.GetComponent<Collider>());
        Unshadowed(go, m);
    }

    static Renderer Unshadowed(GameObject go, Material m)
    {
        var r = go.GetComponent<Renderer>();
        r.sharedMaterial = m;
        r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false;
        return r;
    }

    static Material Unlit(string name, Color c, bool additive)
    {
        string path = $"{MatDir}/{name}.mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (!m) { m = new Material(Shader.Find("Universal Render Pipeline/Unlit")); AssetDatabase.CreateAsset(m, path); }
        m.SetColor("_BaseColor", c);
        if (additive)
        {
            // Transparent, additive (URP _Blend 2), no depth write: brightens whatever is inside the halo.
            m.SetFloat("_Surface", 1f); m.SetFloat("_Blend", 2f);
            m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha); m.SetFloat("_DstBlend", (float)BlendMode.One);
            m.SetFloat("_SrcBlendAlpha", (float)BlendMode.One); m.SetFloat("_DstBlendAlpha", (float)BlendMode.One);
            m.SetFloat("_ZWrite", 0f);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.SetOverrideTag("RenderType", "Transparent");
            m.renderQueue = (int)RenderQueue.Transparent;
        }
        EditorUtility.SetDirty(m);
        return m;
    }
}
