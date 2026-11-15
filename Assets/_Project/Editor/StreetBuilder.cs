using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

// The soi around the stall (GDD > Art direction): pavement, a road where the motorbikes pass, shophouses across the
// street and behind the player, electricity poles, the vendor's parasol, string lights and lanterns, and a dusk sky.
// Kenney CC0 kits (City Kit Commercial, City Kit Roads, Holiday Kit). Each kit has one colour-map texture, so each kit
// gets one shared URP Lit material and the street batches well (GDD > Performance: one atlas, static batching).
// The player stands at the origin facing +Z; customers walk along z = 1.35; the road runs along X from z = 2.4 to 8.4.
public static class StreetBuilder
{
    const string Kenney = "Assets/_Project/ThirdParty/Kenney/";
    const string MatDir = "Assets/_Project/Materials/Street/";
    static readonly Dictionary<string, Material> kitMaterials = new Dictionary<string, Material>();

    public static void Build(Transform room)
    {
        System.IO.Directory.CreateDirectory(MatDir);
        kitMaterials.Clear();
        // The Lab room's grid floor: keep its collider (dropped things land on it), hide the grid.
        var floor = room.Find("Floor (20 x 20 m)");
        if (floor) foreach (var r in floor.GetComponentsInChildren<Renderer>()) r.enabled = false;

        var street = new GameObject("Street").transform;
        Ground(street);
        Buildings(street);
        StreetLights(street);
        Sky(room);
    }

    // Stall dressing, under the stall so seated mode lowers it with the counter.
    public static void DressStall(Transform stall)
    {
        // A green awning frame over the counter. The model's legs stand at its far edge (z 0.1..0.25 x3 = 0.3..0.74 m),
        // so from z = 0.1 they land just past the counter's far corners, clear of the customers' line at z = 1.35.
        Place("CityKitCommercial", "detail-awning-wide", stall, new Vector3(0f, 0f, 0.1f), 0f, new Vector3(2.1f, 5.4f, 3f)).name = "Awning";
        // String lights hang from its front edge (roof top at 2.16 m), and a lantern on each leg.
        for (int i = 0; i < 2; i++)
            Place("HolidayKit", i == 0 ? "lights-colored" : "lights-red", stall, new Vector3(-0.3f + i * 0.6f, 1.86f, 0.95f), 0f, 0.6f).name = "String lights";     // just in front of the roof, above the eye-to-customer line
        Place("HolidayKit", "lantern-hanging", stall, new Vector3(-0.84f, 1.85f, 0.8f), 0f, 0.4f).name = "Lantern";
        Place("HolidayKit", "lantern-hanging", stall, new Vector3(0.84f, 1.85f, 0.8f), 0f, 0.4f).name = "Lantern";
    }

    static void Ground(Transform street)
    {
        var g = new GameObject("Ground").transform; g.SetParent(street);
        // Pavement on the stall side (z -7.5..2.4) and across the street (z 8.4..10.4), 2 m tiles.
        for (float x = -17f; x <= 17f; x += 2f)
        {
            for (float z = -6.5f; z <= 1.5f; z += 2f) Tile(g, "tile-low", new Vector3(x, 0, z), 2f);
            Tile(g, "tile-low", new Vector3(x, 0, 9.4f), 2f);
        }
        // Road: 6 m tiles along X.
        for (float x = -18f; x <= 18f; x += 6f) Tile(g, "road-straight", new Vector3(x, 0, 5.4f), 6f);
    }

    static void Tile(Transform parent, string model, Vector3 pos, float size, float yaw = 0f)
    {
        var t = Place("CityKitRoads", model, parent, pos, yaw, 1f);
        t.localScale = new Vector3(size, 1f, size);     // flat: 2 cm thick whatever the size
    }

    static void Buildings(Transform street)
    {
        var b = new GameObject("Buildings").transform; b.SetParent(street);
        string[] models = { "building-a", "building-c", "building-e", "building-h", "building-k", "building-n" };
        // Across the street, fronts facing the stall.
        for (int i = 0; i < 6; i++) Place("CityKitCommercial", models[i], b, new Vector3(-17.5f + i * 7f, 0, 14f), 180f, 7.5f);
        // Behind the player, fronts facing the street.
        for (int i = 0; i < 5; i++) Place("CityKitCommercial", models[(i + 3) % 6], b, new Vector3(-14f + i * 7f, 0, -10f), 0f, 7.5f);
    }

    // Street lights along both kerbs, staggered, arms over the road. None right in front of the stall.
    static void StreetLights(Transform street)
    {
        var p = new GameObject("Street lights").transform; p.SetParent(street);
        foreach (float x in new[] { -14f, -6f, 6f, 14f }) Place("CityKitRoads", "light-square", p, new Vector3(x, 0, 2.6f), 0f, 7f);
        foreach (float x in new[] { -10f, -2f, 10f }) Place("CityKitRoads", "light-square", p, new Vector3(x, 0, 8.2f), 180f, 7f);
    }

    // Dusk: a low warm sun, a purple-orange sky, soft ambient light. Still the one real-time light (GDD budget).
    static void Sky(Transform room)
    {
        var sky = Material("Dusk sky", Shader.Find("Skybox/Procedural"));
        sky.SetFloat("_SunSize", 0.03f);
        sky.SetFloat("_AtmosphereThickness", 1.4f);
        sky.SetColor("_SkyTint", new Color(0.55f, 0.42f, 0.62f));
        sky.SetColor("_GroundColor", new Color(0.18f, 0.15f, 0.16f));
        sky.SetFloat("_Exposure", 0.9f);
        RenderSettings.skybox = sky;
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(0.55f, 0.5f, 0.62f);
        RenderSettings.ambientEquatorColor = new Color(0.5f, 0.42f, 0.38f);
        RenderSettings.ambientGroundColor = new Color(0.22f, 0.19f, 0.17f);
        var sun = Object.FindObjectsByType<Light>(FindObjectsSortMode.None).FirstOrDefault(l => l.type == LightType.Directional);
        if (sun)
        {
            sun.color = new Color(1f, 0.78f, 0.58f);
            sun.intensity = 1.1f;
            sun.transform.rotation = Quaternion.Euler(28f, -35f, 0f);     // low, from behind-left: faces and fruit stay lit
            RenderSettings.sun = sun;
        }
    }

    // A Kenney model as the look of a game object whose own primitive keeps the collider (cutting board, bag).
    public static Transform Prop(string kit, string model, Transform parent, Vector3 localPos, float yaw, float scale) =>
        Place(kit, model, parent, localPos, yaw, Vector3.one * scale);

    // One Kenney model, all its renderers on its kit's shared material, marked static unless under the stall
    // (the stall moves for seated mode).
    static Transform Place(string kit, string model, Transform parent, Vector3 localPos, float yaw, float scale) =>
        Place(kit, model, parent, localPos, yaw, Vector3.one * scale);

    static Transform Place(string kit, string model, Transform parent, Vector3 localPos, float yaw, Vector3 scale)
    {
        var asset = AssetDatabase.LoadAssetAtPath<GameObject>($"{Kenney}{kit}/{model}.glb");
        if (!asset) throw new System.Exception($"Missing {kit}/{model}.glb (imported?)");
        var go = (GameObject)Object.Instantiate(asset, parent);
        go.name = model;
        go.transform.localPosition = localPos;
        go.transform.localRotation = Quaternion.Euler(0, yaw, 0);
        go.transform.localScale = scale;
        var mat = KitMaterial(kit);
        foreach (var r in go.GetComponentsInChildren<Renderer>(true))
        {
            r.sharedMaterials = Enumerable.Repeat(mat, r.sharedMaterials.Length).ToArray();
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;      // flat-lit street, no shadow cost
        }
        if (!parent.GetComponentInParent<SliceAndSell.StanceHeight>() && !parent.GetComponentInParent<Rigidbody>())
            foreach (var t in go.GetComponentsInChildren<Transform>(true)) GameObjectUtility.SetStaticEditorFlags(t.gameObject, StaticEditorFlags.BatchingStatic);
        return go.transform;
    }

    static Material KitMaterial(string kit)
    {
        if (kitMaterials.TryGetValue(kit, out var m)) return m;
        m = Material(kit, Shader.Find("Universal Render Pipeline/Lit"));
        m.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>($"{Kenney}{kit}/Textures/colormap.png"));
        m.SetColor("_BaseColor", Color.white);
        m.SetFloat("_Smoothness", 0.2f);
        if (kit == "HolidayKit")
        {
            // The bulbs and lanterns glow (no extra real-time lights).
            m.EnableKeyword("_EMISSION");
            m.SetTexture("_EmissionMap", m.GetTexture("_BaseMap"));
            m.SetColor("_EmissionColor", Color.white * 1.2f);
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
        }
        m.enableInstancing = true;
        EditorUtility.SetDirty(m);
        return kitMaterials[kit] = m;
    }

    static Material Material(string name, Shader shader)
    {
        string path = MatDir + name + ".mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (!m) { m = new Material(shader); AssetDatabase.CreateAsset(m, path); }
        else m.shader = shader;
        return m;
    }
}
