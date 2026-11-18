using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

// The soi around the stall (GDD > Art direction; look reference: a Thai fruit pushcart in a narrow street at sunset).
// The player stands at the origin facing +Z, behind the cart. The soi runs away from the player along +Z, about 7 m
// between shopfronts, and opens onto a cross street at z ≈ 34 where the sunset shows and the motorbikes pass.
// Customers walk up the soi to the counter and back down it.
// Sources: Kenney CC0 kits (City Kit Commercial/Roads, Holiday, Furniture, Food), Poly Pizza models (see README), and
// original props from Tools/Blender/make_street_props.py (Assets/_Project/Models/Street). Every imported material
// becomes one shared URP Lit material per source + material name, so the street batches well; anything that never
// moves is static (batching + baked lighting). Still one real-time light (GDD budget); the lanterns and bulbs glow
// through emissive materials, which also light the walls in the bake.
public static class StreetBuilder
{
    const string Third = "Assets/_Project/ThirdParty/";
    const string Props = "Assets/_Project/Models/Street/";
    const string MatDir = "Assets/_Project/Materials/Street/";
    public const float AlleyHalf = 3.5f;        // shopfronts at x = ±3.5
    const float AlleyStart = -8f, AlleyEnd = 33f;
    const float WorkLightIntensity = 1.1f;      // baked work light over the counter (tune by the player-view render)
    static readonly Dictionary<string, Material> materials = new Dictionary<string, Material>();
    static TMP_FontAsset font;

    // Customers' path and the queue spot (StallBuilder reads these): up the middle of the soi to the counter.
    public static readonly Vector3 CustomerEntry = new Vector3(-1.1f, 0, 11f), CustomerExit = new Vector3(1.1f, 0, 11f);
    public static readonly Vector3 QueueSpot = new Vector3(-0.8f, 0, 2.5f);
    // The motorbike passes along the cross street at the end of the soi.
    public static readonly Vector3 BikeFrom = new Vector3(-25f, 0.8f, 36f), BikeTo = new Vector3(25f, 0.8f, 36f);

    public static void Build(Transform room, TMP_FontAsset thaiFont)
    {
        System.IO.Directory.CreateDirectory(MatDir);
        materials.Clear();
        font = thaiFont;
        PrepareImports();
        // The Lab room's grid floor: keep its collider (dropped things land on it), hide the grid.
        var floor = room.Find("Floor (20 x 20 m)");
        if (floor) foreach (var r in floor.GetComponentsInChildren<Renderer>()) r.enabled = false;

        var street = new GameObject("Street").transform;
        Ground(street);
        Buildings(street);
        Shopfronts(street);
        Overhead(street);
        People(street);
        Sky();
    }

    // ---------- ground and buildings ----------

    static void Ground(Transform street)
    {
        var g = new GameObject("Ground").transform; g.SetParent(street);
        // Asphalt down the middle (3 m), concrete pavement each side up to the shopfronts.
        for (float z = AlleyStart; z <= AlleyEnd + 1f; z += 3f) Tile(g, "road-straight", new Vector3(0, 0, z + 1.5f), 3f, 90f);
        for (float z = AlleyStart; z <= AlleyEnd; z += 2f)
            foreach (float x in new[] { -2.5f, 2.5f }) Tile(g, "tile-low", new Vector3(x, 0, z + 1f), 2f);
        // The cross street at the end, and the far pavement.
        for (float x = -27f; x <= 27f; x += 6f) Tile(g, "road-straight", new Vector3(x, 0, AlleyEnd + 4f), 6f);
        for (float x = -26f; x <= 26f; x += 2f) Tile(g, "tile-low", new Vector3(x, 0, AlleyEnd + 8f), 2f);
    }

    static void Tile(Transform parent, string model, Vector3 pos, float size, float yaw = 0f)
    {
        var t = Model($"{Third}Kenney/CityKitRoads/{model}.glb", parent, pos, yaw);
        t.localScale = new Vector3(size, 1f, size);     // flat: 2 cm thick whatever the size
        Lightmap(t, 0.3f);
    }

    static void Buildings(Transform street)
    {
        var b = new GameObject("Buildings").transform; b.SetParent(street);
        string[] models = { "building-a", "building-c", "building-e", "building-h", "building-k", "building-n" };
        // Both sides of the soi, fronts facing the street, packed along Z by their real widths.
        for (int side = -1; side <= 1; side += 2)
        {
            float z = AlleyStart; int i = side < 0 ? 0 : 3;
            while (z < AlleyEnd)
            {
                var t = Model($"{Third}Kenney/CityKitCommercial/{models[i++ % models.Length]}.glb", b, Vector3.zero, side < 0 ? 90f : -90f, scale: 7f);
                var bounds = Bounds(t);
                // Front face on the shopfront line, first edge at z.
                t.position += new Vector3(side * AlleyHalf - (side < 0 ? bounds.max.x : bounds.min.x), 0, z - bounds.min.z);
                z += bounds.size.z;
                Lightmap(t, 0.15f);
            }
        }
        // Behind the player: the soi ends in a shophouse facing it.
        var end = Model($"{Third}Kenney/CityKitCommercial/building-e.glb", b, Vector3.zero, 0f, scale: 7f);
        var eb = Bounds(end); end.position += new Vector3(-eb.center.x, 0, AlleyStart - eb.max.z);
        Lightmap(end, 0.15f);
        // Skyline across the cross street, low and dark against the sunset.
        for (int i = 0; i < 6; i++)
        {
            var t = Model($"{Third}Kenney/CityKitCommercial/{models[(i + 2) % models.Length]}.glb", b, new Vector3(-17.5f + i * 7f, 0, AlleyEnd + 14f), 180f, scale: 7f);
            Lightmap(t, 0.08f);
        }
    }

    // ---------- shopfront clutter (both sides of the soi) ----------

    static void Shopfronts(Transform street)
    {
        var s = new GameObject("Shopfronts").transform; s.SetParent(street);
        float L = -AlleyHalf, R = AlleyHalf;
        // Roller shutters and signs on the ground floors; AC units and plants on the upper floors.
        foreach (float z in new[] { 4.5f, 11.5f, 19f, 26f })
        {
            Prop("RollerShutter", s, new Vector3(L + 0.02f, 0, z), 90f);
            Prop("RollerShutter", s, new Vector3(R - 0.02f, 0, z + 3.2f), -90f);
            Prop("AirCon", s, new Vector3(L, 3.4f, z + 1.6f), 90f);
            Prop("AirCon", s, new Vector3(R, 4.1f, z - 1.2f), -90f);
            Prop("AirCon", s, new Vector3(L, 6.6f, z - 1.5f), 90f);
        }
        Sign(s, "ร้านค้า", new Vector3(L, 3.0f, 7.5f), 90f, new Color(0.8f, 0.15f, 0.12f), Color.white);
        Sign(s, "ผลไม้สด", new Vector3(R, 3.1f, 6.2f), -90f, new Color(0.98f, 0.82f, 0.25f), new Color(0.65f, 0.12f, 0.08f));
        Sign(s, "ร้านค้า", new Vector3(R, 2.9f, 15.5f), -90f, new Color(0.95f, 0.93f, 0.85f), new Color(0.15f, 0.3f, 0.7f));
        Sign(s, "ข้าวมันไก่", new Vector3(L, 2.9f, 17f), 90f, new Color(0.15f, 0.45f, 0.3f), Color.white);

        // Doorstep life: plants, stools, baskets, a cooler, boxes, a trash can, parked scooters, a cat and a dog.
        string kitF = Third + "Kenney/FurnitureKit/";
        Fit(kitF + "pottedPlant.glb", s, new Vector3(L + 0.35f, 0, 3.0f), 0f, 0.9f);
        Fit(kitF + "plantSmall1.glb", s, new Vector3(L + 0.3f, 0, 9.6f), 0f, 0.5f);
        Fit(kitF + "plantSmall3.glb", s, new Vector3(R - 0.3f, 0, 9.2f), 0f, 0.55f);
        Fit(Third + "PolyPizza/houseplant.glb", s, new Vector3(R - 0.35f, 0, 3.6f), 0f, 0.8f);
        Fit(Third + "PolyPizza/houseplant.glb", s, new Vector3(L + 0.3f, 3.15f, 12.5f), 30f, 0.6f);    // on a balcony ledge
        Fit(kitF + "pottedPlant.glb", s, new Vector3(R - 0.3f, 0, 13.2f), 90f, 0.8f);
        Fit(kitF + "trashcan.glb", s, new Vector3(R - 0.4f, 0, 1.7f), 0f, 0.75f);
        Fit(kitF + "cardboardBoxClosed.glb", s, new Vector3(L + 0.5f, 0, 1.4f), 15f, 0.35f);
        Fit(kitF + "cardboardBoxOpen.glb", s, new Vector3(L + 0.55f, 0.35f, 1.45f), -10f, 0.3f);
        Fit(Third + "PolyPizza/lowpoly-scooter.glb", s, new Vector3(-2.65f, 0, 6.2f), 160f, 1.1f);
        Fit(Third + "PolyPizza/vespa.glb", s, new Vector3(2.7f, 0, 4.6f), -20f, 1.15f);
        Fit(Third + "PolyPizza/lowpoly-scooter.glb", s, new Vector3(2.6f, 0, 12.5f), 200f, 1.1f);
        Fit(Third + "PolyPizza/vespa.glb", s, new Vector3(-2.7f, 0, 18f), 15f, 1.15f);
        Fit(Third + "PolyPizza/cat.glb", s, new Vector3(L + 0.45f, 0, 5.4f), 120f, 0.28f);
        Fit(Third + "PolyPizza/shiba-inu.glb", s, new Vector3(2.0f, 0, 9.0f), 230f, 0.5f);
        Prop("PlasticStool", s, new Vector3(-2.3f, 0, 2.8f), 10f, Stool(new Color(0.85f, 0.2f, 0.18f)));
        Prop("PlasticStool", s, new Vector3(-2.0f, 0, 3.3f), -20f, Stool(new Color(0.2f, 0.4f, 0.8f)));
        Prop("PlasticStool", s, new Vector3(2.4f, 0, 7.8f), 0f, Stool(new Color(0.9f, 0.85f, 0.75f)));
        Prop("BasketStack", s, new Vector3(R - 0.35f, 0, 2.6f), -90f);
        Prop("CoolerBox", s, new Vector3(-2.25f, 0, 1.5f), 80f);
        Prop("IceTray", s, new Vector3(-2.25f, 0.4f, 1.38f), 80f);
        Prop("Scale", s, new Vector3(-2.27f, 0.4f, 1.7f), 100f);
        Prop("SpiritHouse", s, new Vector3(L + 0.4f, 0, 8.3f), 90f);
        Prop("PlasticStool", s, new Vector3(1.9f, 0, 16.5f), 30f, Stool(new Color(0.85f, 0.2f, 0.18f)));
        Prop("PlasticStool", s, new Vector3(-2.2f, 0, 21f), 0f, Stool(new Color(0.2f, 0.4f, 0.8f)));
    }

    static Material Stool(Color c)
    {
        var m = SharedMaterial($"Stool {ColorUtility.ToHtmlStringRGB(c)}", c);
        return m;
    }

    // A shop sign: Blender board with Thai text (Noto Sans Thai) on it; plain coloured box if the board isn't made yet.
    static void Sign(Transform parent, string text, Vector3 pos, float yaw, Color panel, Color ink)
    {
        var board = Prop("SignBoard", parent, pos, yaw);
        if (!board)
        {
            var box = GameObject.CreatePrimitive(PrimitiveType.Cube); Object.DestroyImmediate(box.GetComponent<Collider>());
            box.transform.SetParent(parent); box.transform.localPosition = pos + Quaternion.Euler(0, yaw, 0) * new Vector3(0, 0.2f, 0.02f);
            box.transform.localRotation = Quaternion.Euler(0, yaw, 0); box.transform.localScale = new Vector3(1.2f, 0.4f, 0.04f);
            board = box.transform;
        }
        foreach (var r in board.GetComponentsInChildren<Renderer>())
            r.sharedMaterials = r.sharedMaterials.Select(m => m && m.name.Contains("SignFrame") ? m : SharedMaterial("Sign " + ColorUtility.ToHtmlStringRGB(panel), panel)).ToArray();
        var go = new GameObject("Sign text", typeof(RectTransform));
        go.transform.SetParent(board, false);
        var t = go.AddComponent<TextMeshPro>();
        t.text = text; t.font = font; t.fontSize = 2.6f; t.color = ink; t.alignment = TextAlignmentOptions.Center;
        t.textWrappingMode = TextWrappingModes.NoWrap;
        var rt = (RectTransform)go.transform; rt.sizeDelta = new Vector2(1.1f, 0.36f);
        rt.localPosition = new Vector3(0, 0.2f, 0.045f); rt.localRotation = Quaternion.Euler(0, 180f, 0);   // TMP faces -Z; the board's front is +Z
    }

    // ---------- overhead: poles, wires, lanterns, bunting ----------

    static void Overhead(Transform street)
    {
        var o = new GameObject("Overhead").transform; o.SetParent(street);
        float poleX = AlleyHalf - 0.45f;
        float[] poles = { 2.5f, 13f, 23.5f };
        foreach (var z in poles)
        {
            Prop("ElectricPole", o, new Vector3(-poleX, 0, z), 90f);
            Prop("ElectricPole", o, new Vector3(poleX, 0, z + 5f), -90f);
        }
        // Cable bundles along each side between poles, and across the soi.
        for (int i = 0; i + 1 < poles.Length; i++)
        {
            Wire(o, new Vector3(-poleX, 6.6f, poles[i]), new Vector3(-poleX, 6.6f, poles[i + 1]));
            Wire(o, new Vector3(poleX, 6.4f, poles[i] + 5f), new Vector3(poleX, 6.4f, poles[i + 1] + 5f));
        }
        foreach (var z in poles) Wire(o, new Vector3(-poleX, 6.2f, z), new Vector3(poleX, 6.0f, z + 5f));
        Wire(o, new Vector3(-AlleyHalf, 4.6f, 6f), new Vector3(AlleyHalf, 4.8f, 9f));
        Wire(o, new Vector3(-AlleyHalf, 5.2f, 15f), new Vector3(AlleyHalf, 5.0f, 17f));

        // Paper lanterns strung across the soi, and bunting further down.
        string[] colours = { "Red", "Orange", "Green", "Teal", "Purple" };
        int k = 0;
        foreach (var (z, y) in new[] { (5.0f, 3.6f), (10.5f, 3.9f), (16f, 3.7f) })
        {
            for (float x = -2.6f; x <= 2.61f; x += 1.3f)
            {
                float sag = 0.35f * (1f - (x / 2.6f) * (x / 2.6f));
                Prop("Lantern" + colours[k++ % colours.Length], o, new Vector3(x, y - sag, z), 0f);
            }
        }
        Prop("Bunting", o, new Vector3(-AlleyHalf, 4.2f, 20.5f), 0f, scaleX: 2 * AlleyHalf / 3f);
        Prop("Bunting", o, new Vector3(-AlleyHalf, 4.4f, 26f), 0f, scaleX: 2 * AlleyHalf / 3f);
    }

    // A WireBundle (10 m along +X) stretched from a to b.
    static void Wire(Transform parent, Vector3 a, Vector3 b)
    {
        var d = b - a; var flat = new Vector3(d.x, 0, d.z);
        var t = Prop("WireBundle", parent, a, Mathf.Atan2(-flat.z, flat.x) * Mathf.Rad2Deg, scaleX: flat.magnitude / 10f);
        if (!t) return;
        // Tilt so the far end meets b's height.
        t.localRotation *= Quaternion.Euler(0, 0, Mathf.Atan2(d.y, flat.magnitude) * Mathf.Rad2Deg);
    }

    // Shoppers further down the soi (the customers' models, idling; no gameplay).
    static void People(Transform street)
    {
        var p = new GameObject("People").transform; p.SetParent(street);
        var spots = new[] { (new Vector3(-2.2f, 0, 14f), 70f, "student-in-a-hoodie-7c8223"), (new Vector3(1.6f, 0, 18.5f), 200f, "backpacker-f56207"),
                            (new Vector3(-0.8f, 0, 23f), 160f, "delivery-courier-327e30"), (new Vector3(2.3f, 0, 26f), 250f, "salaryman-commuter-4170c1") };
        foreach (var (pos, yaw, file) in spots)
        {
            var t = Model($"{Third}Polyfork/{file}.glb", p, pos, yaw, isStatic: false);
            var anim = t.GetComponentInChildren<Animation>();
            if (anim && anim.GetClip("Idle")) { anim.clip = anim.GetClip("Idle"); anim.playAutomatically = true; anim.clip.SampleAnimation(anim.gameObject, 0f); }   // saved in Idle, not the T-pose
            CheapSkinning(t);
        }
    }

    // Quest: animate and skin only while on screen, against the imported (T-pose) bounds, which hold every idle/walk
    // pose, instead of recomputing bounds from the bones every frame.
    public static void CheapSkinning(Transform character)
    {
        foreach (var a in character.GetComponentsInChildren<Animation>(true)) a.cullingType = AnimationCullingType.BasedOnRenderers;
        foreach (var smr in character.GetComponentsInChildren<SkinnedMeshRenderer>(true)) smr.updateWhenOffscreen = false;
    }

    // ---------- the stall's dressing (under the stall: seated mode lowers it with the counter) ----------

    // Returns the cart look, so StallBuilder can hide the counter box it replaces.
    public static Transform DressStall(Transform stall, Vector3 counterCentre)
    {
        var cart = Prop("PushCart", stall, new Vector3(counterCentre.x, 0, counterCentre.z), 0f);
        // The rainbow umbrella over the cart, its pole just past the cart's far right corner (clear of the player's hands
        // and of the customers' line at z = 1.35).
        Prop("RainbowUmbrella", stall, new Vector3(0.85f, 0, 0.95f), 0f);
        // Bulb strings round the cart's frame rails (top at 2.05 m), warm along the front, coloured on the sides.
        for (int i = 0; i < 2; i++)
        {
            Prop("BulbStringWarm", stall, new Vector3(-0.7f + i * 0.7f, 2.03f, 0.84f), 0f, scaleX: 0.7f);
            Prop("BulbStringColour", stall, new Vector3(-0.6875f, 2.03f, 0.26f + i * 0.29f), -90f, scaleX: 0.29f);   // on the side rails
            Prop("BulbStringColour", stall, new Vector3(0.6875f, 2.03f, 0.26f + i * 0.29f), -90f, scaleX: 0.29f);
        }
        // A warm string along the front skirt of the cart too (the customers' side).
        for (int i = 0; i < 2; i++) Prop("BulbStringColour", stall, new Vector3(-0.7f + i * 0.7f, 0.86f, 0.86f), 0f, scaleX: 0.7f);   // against the skirt's top batten
        // Two lanterns hung from the frame's front top rail (underside y 2.025, z 0.8375), near the corners, bottom at ~1.7 m. Nothing decorative goes on
        // the counter or within reach: everything a new player can touch there does something. (The scale, ice tray,
        // plastic bags, foam boxes and price sign used to be here; they looked usable but weren't.)
        Prop("LanternRed", stall, new Vector3(-0.57f, 2.025f, 0.8375f), 0f);
        Prop("LanternOrange", stall, new Vector3(0.57f, 2.025f, 0.8375f), 0f);
        // Stock on the lower shelf, under the counter: closed boxes, clearly storage.
        Fit(Third + "Kenney/FurnitureKit/cardboardBoxClosed.glb", stall, new Vector3(-0.35f, 0.3f, 0.5f), 0f, 0.26f, isStatic: false);
        // Work light: two warm points under the bulb rail, Baked only (no runtime cost on Quest). The cart is dynamic,
        // so they reach the board, trays and crates through the light probes at counter height (BakeLighting), and
        // leave a warm pool on the pavement round the cart.
        for (int i = -1; i <= 1; i += 2)
        {
            var l = new GameObject("Work light").AddComponent<Light>();
            l.transform.SetParent(stall, false); l.transform.localPosition = new Vector3(0.35f * i, 1.9f, 0.5f);
            l.type = LightType.Point; l.lightmapBakeType = LightmapBakeType.Baked;
            l.color = new Color(1f, 0.82f, 0.6f); l.intensity = WorkLightIntensity; l.range = 2.5f; l.shadows = LightShadows.None;
        }
        return cart;
    }

    // ---------- sky, fog, sun, ambient ----------

    // Sunset at the end of the soi: a gradient sky (deep blue overhead, pink, orange at the horizon), warm haze down
    // the street, a low orange sun from the far end, warm ambient so the cart and faces stay readable.
    static void Sky()
    {
        var tex = GradientTexture();
        var sky = SkyMaterial(tex);
        RenderSettings.skybox = sky;
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(0.62f, 0.55f, 0.68f);
        RenderSettings.ambientEquatorColor = new Color(0.78f, 0.56f, 0.44f);
        RenderSettings.ambientGroundColor = new Color(0.32f, 0.24f, 0.2f);
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Linear;
        RenderSettings.fogStartDistance = 14f; RenderSettings.fogEndDistance = 70f;
        RenderSettings.fogColor = new Color(0.86f, 0.55f, 0.45f);
        var sun = Object.FindObjectsByType<Light>(FindObjectsSortMode.None).FirstOrDefault(l => l.type == LightType.Directional);
        if (sun)
        {
            sun.color = new Color(1f, 0.7f, 0.45f);
            sun.intensity = 1.25f;
            // Low from behind the player's left, raking down the soi: faces and fruit lit, long warm shadows ahead.
            // (The sunset itself is in the sky gradient at the end of the soi.)
            sun.transform.rotation = Quaternion.Euler(24f, 32f, 0f);
            sun.shadows = LightShadows.Hard;
            sun.lightmapBakeType = LightmapBakeType.Mixed;
            RenderSettings.sun = sun;
        }
    }

    static Texture2D GradientTexture()
    {
        const string path = MatDir + "Sunset gradient.png";
        // v = 0 straight down, 0.5 the horizon, 1 straight up (Panoramic latitude-longitude).
        var keys = new[] { (0f, new Color(0.16f, 0.12f, 0.12f)), (0.47f, new Color(0.45f, 0.28f, 0.25f)), (0.5f, new Color(1f, 0.62f, 0.32f)),
                           (0.56f, new Color(0.98f, 0.55f, 0.42f)), (0.66f, new Color(0.72f, 0.42f, 0.56f)), (0.8f, new Color(0.33f, 0.3f, 0.55f)), (1f, new Color(0.13f, 0.15f, 0.36f)) };
        var t = new Texture2D(4, 256, TextureFormat.RGB24, false);
        for (int y = 0; y < 256; y++)
        {
            float v = y / 255f; int i = 0;
            while (i < keys.Length - 2 && v > keys[i + 1].Item1) i++;
            var c = Color.Lerp(keys[i].Item2, keys[i + 1].Item2, Mathf.InverseLerp(keys[i].Item1, keys[i + 1].Item1, v));
            for (int x = 0; x < 4; x++) t.SetPixel(x, y, c);
        }
        System.IO.File.WriteAllBytes(path, t.EncodeToPNG());
        AssetDatabase.ImportAsset(path);
        var imp = (TextureImporter)AssetImporter.GetAtPath(path);
        imp.wrapMode = TextureWrapMode.Clamp; imp.mipmapEnabled = false; imp.textureCompression = TextureImporterCompression.Uncompressed;
        imp.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    static Material SkyMaterial(Texture2D tex)
    {
        string path = MatDir + "Sunset sky.mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        var shader = Shader.Find("Skybox/Panoramic");
        if (!m) { m = new Material(shader); AssetDatabase.CreateAsset(m, path); }
        m.shader = shader;
        m.SetTexture("_MainTex", tex);
        m.SetFloat("_Mapping", 1f);       // latitude-longitude
        m.SetFloat("_ImageType", 0f);     // 360
        m.SetFloat("_Exposure", 1f);
        EditorUtility.SetDirty(m);
        return m;
    }

    // ---------- baked lighting ----------

    // Bakes the street's light (sun bounce, lantern and bulb glow, contact shadows) into lightmaps, and lights the
    // moving things (the cart, which seated mode lowers, customers, fruit, hands) from light probes. Subtractive mixed
    // lighting: static scenery is fully baked; the one real-time light still lights and shadows the dynamic objects.
    public static void BakeLighting()
    {
        const string settingsPath = "Assets/_Project/Scenes/Stall Lighting.lighting";
        var ls = AssetDatabase.LoadAssetAtPath<LightingSettings>(settingsPath);
        if (!ls) { ls = new LightingSettings(); AssetDatabase.CreateAsset(ls, settingsPath); }
        ls.bakedGI = true; ls.realtimeGI = false;
        ls.mixedBakeMode = MixedLightingMode.Subtractive;
        ls.lightmapper = LightingSettings.Lightmapper.ProgressiveCPU;
        ls.lightmapResolution = 6f; ls.lightmapPadding = 2; ls.lightmapMaxSize = 1024;
        ls.directionalityMode = LightmapsMode.NonDirectional;
        ls.lightmapCompression = LightmapCompression.NormalQuality;
        ls.ao = true; ls.aoMaxDistance = 0.8f;
        ls.indirectScale = 1.4f; ls.albedoBoost = 1.15f;       // a little more warm bounce in the narrow street
        ls.directSampleCount = 16; ls.indirectSampleCount = 128; ls.environmentSampleCount = 64; ls.maxBounces = 2;
        EditorUtility.SetDirty(ls);
        Lightmapping.lightingSettings = ls;

        // Probes: a grid round the cart and the player's hands, and two lines up the soi where customers walk.
        var probes = new List<Vector3>();
        for (float x = -1.2f; x <= 1.21f; x += 0.6f)
            for (float z = -0.6f; z <= 1.81f; z += 0.6f)
                foreach (float y in new[] { 0.4f, 1.0f, 1.6f, 2.3f }) probes.Add(new Vector3(x, y, z));
        for (float x = -0.6f; x <= 0.61f; x += 0.3f)          // finer layer just over the work area (board, trays, crates)
            for (float z = 0.3f; z <= 0.91f; z += 0.3f) probes.Add(new Vector3(x, 0.95f, z));
        for (float z = 2.5f; z <= 28f; z += 2.5f)
            foreach (float x in new[] { -1.6f, 0f, 1.6f })
                foreach (float y in new[] { 0.5f, 1.8f }) probes.Add(new Vector3(x, y, z));
        var group = new GameObject("Light probes").AddComponent<LightProbeGroup>();
        group.probePositions = probes.ToArray();

        var t0 = System.DateTime.Now;
        bool ok = Lightmapping.Bake();
        Debug.Log($"[StreetBuilder] Lighting bake {(ok ? "done" : "FAILED")} in {(System.DateTime.Now - t0).TotalSeconds:0} s, {LightmapSettings.lightmaps.Length} lightmap(s)");
    }

    // Occlusion culling: the shophouses (static, flagged Occluder/Occludee by Model) hide the soi behind them. The stall
    // and everything that moves are not static, so they are never occluders; Unity still culls them behind the street.
    public static void BakeOcclusion()
    {
        StaticOcclusionCulling.smallestOccluder = 2f;     // a shophouse front counts; plants, bins and scooters don't
        StaticOcclusionCulling.smallestHole = 0.25f;      // gaps between houses and under awnings stay see-through
        StaticOcclusionCulling.backfaceThreshold = 100f;
        var t0 = System.DateTime.Now;
        bool ok = StaticOcclusionCulling.Compute();
        Debug.Log($"[StreetBuilder] Occlusion bake {(ok ? "done" : "FAILED")} in {(System.DateTime.Now - t0).TotalSeconds:0} s");
    }

    // ---------- placing models ----------

    // A Kenney model as the look of a game object whose own primitive keeps the collider (cutting board).
    public static Transform Prop(string kit, string model, Transform parent, Vector3 localPos, float yaw, float scale)
    {
        var t = Model($"{Third}Kenney/{kit}/{model}.glb", parent, localPos, yaw, scale: scale, isStatic: false);
        return t;
    }

    // A Blender prop (Models/Street) by name; null (with a warning) if it hasn't been made.
    public static Transform Prop(string name, Transform parent, Vector3 localPos, float yaw, Material recolour = null, float scaleX = 1f)
    {
        string path = $"{Props}{name}.glb";
        if (!AssetDatabase.LoadAssetAtPath<GameObject>(path)) { Debug.LogWarning($"[StreetBuilder] {path} missing, skipped"); return null; }
        var t = Model(path, parent, localPos, yaw, isStatic: !parent.GetComponentInParent<SliceAndSell.StanceHeight>());
        t.localScale = new Vector3(scaleX, 1f, 1f);
        if (recolour) foreach (var r in t.GetComponentsInChildren<Renderer>()) r.sharedMaterials = Enumerable.Repeat(recolour, r.sharedMaterials.Length).ToArray();
        return t;
    }

    // A model scaled so it is `height` tall, standing on localPos (models from other sources come in any scale).
    public static Transform Fit(string path, Transform parent, Vector3 localPos, float yaw, float height, bool isStatic = true)
    {
        var t = Model(path, parent, localPos, yaw, isStatic: isStatic);
        var b = Bounds(t);
        if (b.size.y <= 0f) return t;
        float s = height / b.size.y;
        t.localScale *= s;
        b = Bounds(t);
        t.position += new Vector3(0, parent.TransformPoint(localPos).y - b.min.y, 0);
        return t;
    }

    static Transform Model(string path, Transform parent, Vector3 localPos, float yaw, float scale = 1f, bool isStatic = true)
    {
        var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (!asset) throw new System.Exception($"Missing {path} (imported?)");
        var go = (GameObject)Object.Instantiate(asset, parent);
        go.name = System.IO.Path.GetFileNameWithoutExtension(path);
        go.transform.localPosition = localPos;
        go.transform.localRotation = Quaternion.Euler(0, yaw, 0);
        go.transform.localScale *= scale;
        string group = path.StartsWith(Props) ? "Street" : System.IO.Path.GetFileName(System.IO.Path.GetDirectoryName(path));
        foreach (var r in go.GetComponentsInChildren<Renderer>(true))
        {
            if (group != "Polyfork") r.sharedMaterials = r.sharedMaterials.Select(m => Convert(m, group)).ToArray();   // the people keep glTFast's textured materials
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
        }
        if (isStatic && !parent.GetComponentInParent<Rigidbody>())
            foreach (var t in go.GetComponentsInChildren<Transform>(true))
                GameObjectUtility.SetStaticEditorFlags(t.gameObject, StaticEditorFlags.BatchingStatic | StaticEditorFlags.ContributeGI | StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic);
        return go.transform;
    }

    static void Lightmap(Transform t, float scale)
    {
        foreach (var r in t.GetComponentsInChildren<MeshRenderer>(true))
        {
            var so = new SerializedObject(r);
            var p = so.FindProperty("m_ScaleInLightmap");
            if (p != null) { p.floatValue = scale; so.ApplyModifiedPropertiesWithoutUndo(); }
        }
    }

    static Bounds Bounds(Transform t)
    {
        var rs = t.GetComponentsInChildren<Renderer>(true);
        if (rs.Length == 0) return new Bounds(t.position, Vector3.zero);
        var b = rs[0].bounds;
        foreach (var r in rs.Skip(1)) b.Encapsulate(r.bounds);
        return b;
    }

    // One shared URP Lit material per source folder + glTF material name: colour, texture and glow carried over.
    // Kenney Holiday Kit bulbs/lanterns and every "...Glow" material emit light (and light the walls in the bake).
    static Material Convert(Material gltf, string group)
    {
        if (!gltf) return gltf;
        string key = group + " " + gltf.name;
        if (materials.TryGetValue(key, out var m)) return m;
        m = Material(Safe(key), Shader.Find("Universal Render Pipeline/Lit"));
        var c = gltf.HasProperty("baseColorFactor") ? gltf.GetColor("baseColorFactor") : gltf.HasProperty("_BaseColor") ? gltf.GetColor("_BaseColor") : Color.white;
        var tex = gltf.HasProperty("baseColorTexture") ? gltf.GetTexture("baseColorTexture") : gltf.HasProperty("_BaseMap") ? gltf.GetTexture("_BaseMap") : null;
        m.SetColor("_BaseColor", c);
        m.SetTexture("_BaseMap", tex);
        m.SetFloat("_Smoothness", 0.15f);
        var e = gltf.HasProperty("emissiveFactor") ? gltf.GetColor("emissiveFactor") : Color.black;
        bool glow = group == "HolidayKit" || gltf.name.Contains("Glow") || e.maxColorComponent > 0.01f;
        if (glow)
        {
            m.EnableKeyword("_EMISSION");
            if (group == "HolidayKit") { m.SetTexture("_EmissionMap", tex); m.SetColor("_EmissionColor", Color.white * 1.2f); }
            else m.SetColor("_EmissionColor", (e.maxColorComponent > 0.01f ? e : c) * 1.4f);
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.BakedEmissive;
        }
        else { m.DisableKeyword("_EMISSION"); m.SetColor("_EmissionColor", Color.black); m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.EmissiveIsBlack; }
        m.enableInstancing = true;
        EditorUtility.SetDirty(m);
        return materials[key] = m;
    }

    static Material SharedMaterial(string name, Color c)
    {
        if (materials.TryGetValue(name, out var m)) return m;
        m = Material(Safe(name), Shader.Find("Universal Render Pipeline/Lit"));
        m.SetColor("_BaseColor", c); m.SetFloat("_Smoothness", 0.25f); m.enableInstancing = true;
        EditorUtility.SetDirty(m);
        return materials[name] = m;
    }

    static string Safe(string s) => string.Concat(s.Select(ch => System.IO.Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch));

    static Material Material(string name, Shader shader)
    {
        string path = MatDir + name + ".mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (!m) { m = new Material(shader); AssetDatabase.CreateAsset(m, path); }
        else m.shader = shader;
        return m;
    }

    // Static street models get lightmap UVs from glTFast's importer ("Generate Lightmap UVs"); Blender props bring their own.
    static void PrepareImports()
    {
        var folders = new[] { Third + "Kenney/CityKitCommercial", Third + "Kenney/CityKitRoads", Third + "Kenney/HolidayKit", Third + "Kenney/FurnitureKit", Third + "PolyPizza" };
        foreach (var guid in AssetDatabase.FindAssets("t:GameObject", folders))
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            if (!path.EndsWith(".glb")) continue;
            var importer = AssetImporter.GetAtPath(path);
            var so = new SerializedObject(importer);
            var p = so.FindProperty("editorImportSettings.generateSecondaryUVSet");
            if (p == null || p.boolValue) continue;
            p.boolValue = true;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(importer);
            importer.SaveAndReimport();
        }
    }
}
