using System.IO;
using System.Linq;
using HCD545;
using SliceAndSell;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Interaction.Toolkit.Filtering;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

// Builds the Milestone 1 greybox: prefabs (Guava, Knife, Bag) and the Stall scene, from the Lab 5 scene
// (which already has the XR rig, the XR Interaction Simulator and the tracking-lost banner).
// Re-run it after changing the layout here. Batch mode: -executeMethod StallBuilder.Build
public static class StallBuilder
{
    const string Root = "Assets/_Project";
    const string ScenePath = Root + "/Scenes/Stall.unity";
    const string Lab5Scene = "Assets/HCD545/Lab5/Scenes/Lab5Interaction.unity";
    const int LayerDefault = 1 << 0, LayerFruit = 1 << 1, LayerBag = 1 << 2;   // XRI/Settings/InteractionLayerSettings

    static readonly string[] Lab5Roots = { "Lamp", "Cube 3 cm", "Cube 6 cm", "Cube 12 cm", "Grab table", "Teleport anchors",
                                           "Ray shelf", "Grab trial", "Comfort Menu", "Poke panel" };
    static readonly string[] Lab5RoomParts = { "Sofa (2.73 m)", "White knot" };

    // Knife grip for tracked hands (first guess; tune on the headset, then rebuild).
    static readonly Vector3 KnifeHandGripPosition = new Vector3(0, 0, 0.035f);
    static readonly Vector3 KnifeHandGripEuler = Vector3.zero;

    static AudioClip selectSound, releaseSound;

    [MenuItem("Slice & Sell/Build Stall Scene")]
    public static void Build()
    {
        selectSound = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/HCD545/Lab5/Audio/Select.wav");
        releaseSound = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/HCD545/Lab5/Audio/Release.wav");
        Directory.CreateDirectory(Root + "/Prefabs");
        Directory.CreateDirectory(Root + "/Materials/Greybox");

        var guava = SavePrefab(MakeGuava(), "Guava");
        var knifePrefab = SavePrefab(MakeKnife(), "Knife");
        var bag = SavePrefab(MakeBag(), "Bag");

        // Overwrite the file in place (not delete + copy) so Stall.unity keeps its GUID across rebuilds.
        File.Copy(Lab5Scene, ScenePath, true);
        AssetDatabase.ImportAsset(ScenePath, ImportAssetOptions.ForceUpdate);
        var scene = EditorSceneManager.OpenScene(ScenePath);
        foreach (var go in scene.GetRootGameObjects())
            if (Lab5Roots.Contains(go.name)) Object.DestroyImmediate(go);
        var room = scene.GetRootGameObjects().First(g => g.name == "Room").transform;
        foreach (var n in Lab5RoomParts) { var t = room.Find(n); if (t) Object.DestroyImmediate(t.gameObject); }

        BuildStall(guava, knifePrefab, bag);

        EditorSceneManager.SaveScene(scene);
        // Pass the GUIDs explicitly, and clear first: Unity compares by path, keeps a stale GUID and skips the write.
        EditorBuildSettings.scenes = new EditorBuildSettingsScene[0];
        EditorBuildSettings.scenes = new[]
        {
            new EditorBuildSettingsScene(new GUID(AssetDatabase.AssetPathToGUID(ScenePath)), true),
            new EditorBuildSettingsScene(new GUID(AssetDatabase.AssetPathToGUID(Lab5Scene)), false),
        };
        AssetDatabase.SaveAssets();
        Debug.Log("[StallBuilder] Build scenes: " + string.Join(", ", EditorBuildSettings.scenes.Select(s => $"{s.path} {s.guid} {s.enabled}")));
        Debug.Log("[StallBuilder] Built. Roots: " + string.Join(", ", scene.GetRootGameObjects().Select(g => g.name)));
    }

    static void BuildStall(GameObject guava, GameObject knifePrefab, GameObject bag)
    {
        var stall = new GameObject("Stall").transform;

        // Counter: top at 0.9 m, front edge 25 cm in front of the player, everything within ~65 cm reach.
        Box("Counter", stall, new Vector3(0, 0.45f, 0.55f), new Vector3(1.4f, 0.9f, 0.6f), Mat("Counter", new Color(0.55f, 0.38f, 0.24f)));

        // Cutting board with the fruit socket and the scoop spot (bag socket) to its right.
        var boardGo = new GameObject("Cutting board"); boardGo.transform.SetParent(stall);
        Box("Board", boardGo.transform, new Vector3(-0.05f, 0.91f, 0.52f), new Vector3(0.32f, 0.02f, 0.22f), Mat("Board", new Color(0.86f, 0.72f, 0.5f)));
        var board = boardGo.AddComponent<CuttingBoard>();
        board.fruitSocket = Socket("Fruit socket", boardGo.transform, new Vector3(-0.05f, 0.965f, 0.52f), new Vector3(0.16f, 0.12f, 0.16f), LayerFruit);
        board.scoopSocket = Socket("Scoop spot", boardGo.transform, new Vector3(0.2f, 0.995f, 0.52f), new Vector3(0.14f, 0.16f, 0.1f), LayerBag);
        Box("Scoop mark", boardGo.transform, new Vector3(0.2f, 0.902f, 0.52f), new Vector3(0.13f, 0.004f, 0.08f), Mat("Mark", new Color(1f, 0.8f, 0.2f)), false);

        // Endless fruit bin (left) and bag stack (right).
        var bin = new GameObject("Fruit bin").transform; bin.SetParent(stall); bin.position = new Vector3(-0.45f, 0.9f, 0.5f);
        var wood = Mat("Crate", new Color(0.45f, 0.3f, 0.18f));
        Box("Wall L", bin, bin.position + new Vector3(-0.11f, 0.04f, 0), new Vector3(0.02f, 0.08f, 0.22f), wood);
        Box("Wall R", bin, bin.position + new Vector3(0.11f, 0.04f, 0), new Vector3(0.02f, 0.08f, 0.22f), wood);
        Box("Wall F", bin, bin.position + new Vector3(0, 0.04f, -0.11f), new Vector3(0.22f, 0.08f, 0.02f), wood);
        Box("Wall B", bin, bin.position + new Vector3(0, 0.04f, 0.11f), new Vector3(0.22f, 0.08f, 0.02f), wood);
        Dispenser(bin, guava, bin.position + new Vector3(0, 0.06f, 0));

        var bags = new GameObject("Bag stack").transform; bags.SetParent(stall); bags.position = new Vector3(0.42f, 0.9f, 0.5f);
        Dispenser(bags, bag, bags.position + new Vector3(0, 0.08f, 0));

        // Knife, lying in front of the board.
        var knife = (GameObject)PrefabUtility.InstantiatePrefab(knifePrefab, stall);
        knife.transform.SetPositionAndRotation(new Vector3(-0.05f, 0.92f, 0.34f), Quaternion.Euler(0, 90, 90));
        knife.GetComponent<Knife>().board = board;

        // Customer, order card and chalkboard.
        var orders = new GameObject("Orders").AddComponent<OrderManager>();
        orders.transform.SetParent(stall);
        for (int i = 0; i < 3; i++) orders.orders.Add(new Order { fruit = "Guava", cuts = 2, price = 20 });
        var customer = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        customer.name = "Customer"; customer.transform.SetParent(stall);
        customer.transform.position = new Vector3(0, 0.85f, 1.35f); customer.transform.localScale = new Vector3(0.45f, 0.85f, 0.45f);
        customer.GetComponent<Renderer>().sharedMaterial = Mat("Customer", new Color(0.3f, 0.5f, 0.85f));
        orders.customer = customer;
        Box("Order card", stall, new Vector3(0, 1.7f, 1.12f), new Vector3(0.36f, 0.2f, 0.01f), Mat("Card", new Color(0.98f, 0.96f, 0.9f)), false);
        orders.orderCard = Text("Order card text", stall, new Vector3(0, 1.7f, 1.112f), new Vector2(0.32f, 0.17f), new Color(0.15f, 0.1f, 0.05f));
        Box("Chalkboard", stall, new Vector3(-0.55f, 1.45f, 0.97f), new Vector3(0.5f, 0.3f, 0.02f), Mat("Chalk", new Color(0.12f, 0.22f, 0.16f)), false);
        orders.chalkboard = Text("Chalkboard text", stall, new Vector3(-0.55f, 1.45f, 0.955f), new Vector2(0.44f, 0.25f), Color.white);

        // Hand-off tray (far edge, in front of the customer).
        var tray = new GameObject("Hand-off tray"); tray.transform.SetParent(stall);
        Box("Tray", tray.transform, new Vector3(0.25f, 0.905f, 0.76f), new Vector3(0.2f, 0.01f, 0.16f), Mat("Tray", new Color(0.9f, 0.45f, 0.15f)));
        Socket("Tray socket", tray.transform, new Vector3(0.25f, 0.985f, 0.76f), new Vector3(0.2f, 0.16f, 0.16f), LayerBag)
            .gameObject.AddComponent<HandoffTray>().orders = orders;

        // Service bell: poke from above, or ray + trigger.
        var bell = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        bell.name = "Service bell"; bell.transform.SetParent(stall);
        bell.transform.position = new Vector3(0.5f, 0.915f, 0.74f); bell.transform.localScale = new Vector3(0.08f, 0.015f, 0.08f);
        bell.GetComponent<Renderer>().sharedMaterial = Mat("Bell", new Color(0.95f, 0.75f, 0.2f));
        var simple = bell.AddComponent<XRSimpleInteractable>();
        var poke = bell.AddComponent<XRPokeFilter>();
        poke.pokeInteractable = simple;
        poke.pokeConfiguration.Value.pokeDirection = PokeAxis.NegativeY;
        Feedback(bell);
        bell.AddComponent<ServiceBell>().orders = orders;
    }

    // ---------- prefabs ----------

    static GameObject MakeGuava()
    {
        var root = new GameObject("Guava");
        var rb = root.AddComponent<Rigidbody>(); rb.mass = 0.2f;
        var col = root.AddComponent<SphereCollider>(); col.radius = 0.045f;
        var grab = Grab(root, LayerDefault | LayerFruit, dynamicAttach: true);
        var skin = Mat("Guava skin", new Color(0.55f, 0.8f, 0.3f));
        var flesh = Mat("Guava flesh", new Color(0.98f, 0.75f, 0.7f));

        var whole = Stage(root, "Whole");
        Piece(whole, new Vector3(0, 0, 0), new Vector3(0.09f, 0.09f, 0.09f), skin);
        var halves = Stage(root, "Halves");
        foreach (var x in new[] { -0.024f, 0.024f }) Piece(halves, new Vector3(x, 0, 0), new Vector3(0.044f, 0.09f, 0.09f), flesh);
        var quarters = Stage(root, "Quarters");
        foreach (var x in new[] { -0.024f, 0.024f }) foreach (var z in new[] { -0.024f, 0.024f })
            Piece(quarters, new Vector3(x, 0, z), new Vector3(0.044f, 0.09f, 0.044f), flesh);
        halves.SetActive(false); quarters.SetActive(false);

        var fruit = root.AddComponent<Fruit>();
        fruit.fruitName = "Guava"; fruit.thickness = 0.09f; fruit.cutVolume = col;
        fruit.stages = new[] { whole, halves, quarters };
        Feedback(root);
        return root;
    }

    // Built along +Z: the hand holds the handle, the blade points forward, the edge faces down.
    static GameObject MakeKnife()
    {
        var root = new GameObject("Knife");
        var rb = root.AddComponent<Rigidbody>(); rb.mass = 0.15f;
        Box("Handle", root.transform, new Vector3(0, 0, 0), new Vector3(0.025f, 0.025f, 0.1f), Mat("Handle", new Color(0.35f, 0.2f, 0.1f)));
        Box("Blade", root.transform, new Vector3(0, 0, 0.11f), new Vector3(0.003f, 0.03f, 0.12f), Mat("Steel", new Color(0.8f, 0.82f, 0.85f)));
        var attach = new GameObject("Attach").transform; attach.SetParent(root.transform, false);
        var grab = Grab<KnifeGrab>(root, LayerDefault, dynamicAttach: false);
        grab.attachTransform = attach;
        // Hands pinch the front of the handle (by the blade), like a chef's pinch grip. Tune on the headset.
        grab.handAttachTransform = Marker(root.transform, "Hand attach", KnifeHandGripPosition);
        grab.handAttachTransform.localRotation = Quaternion.Euler(KnifeHandGripEuler);
        var knife = root.AddComponent<Knife>();
        knife.edgeStart = Marker(root.transform, "Edge start", new Vector3(0, -0.015f, 0.05f));
        knife.edgeEnd = Marker(root.transform, "Edge end", new Vector3(0, -0.015f, 0.17f));
        Feedback(root);
        return root;
    }

    static GameObject MakeBag()
    {
        var root = new GameObject("Bag");
        var rb = root.AddComponent<Rigidbody>(); rb.mass = 0.05f;
        var col = root.AddComponent<BoxCollider>(); col.size = new Vector3(0.12f, 0.15f, 0.07f);
        Grab(root, LayerDefault | LayerBag, dynamicAttach: true);
        Box("Paper", root.transform, Vector3.zero, new Vector3(0.12f, 0.15f, 0.07f), Mat("Bag", new Color(0.93f, 0.9f, 0.8f)), false);
        var filled = Box("Slices", root.transform, new Vector3(0, 0.08f, 0), new Vector3(0.1f, 0.03f, 0.05f), Mat("Guava flesh", new Color(0.98f, 0.75f, 0.7f)), false);
        filled.SetActive(false);
        root.AddComponent<Bag>().filledVisual = filled;
        Feedback(root);
        return root;
    }

    // ---------- helpers ----------

    static GameObject SavePrefab(GameObject go, string name)
    {
        var prefab = PrefabUtility.SaveAsPrefabAsset(go, $"{Root}/Prefabs/{name}.prefab");
        Object.DestroyImmediate(go);
        return prefab;
    }

    static XRGrabInteractable Grab(GameObject go, int layers, bool dynamicAttach) => Grab<XRGrabInteractable>(go, layers, dynamicAttach);

    static T Grab<T>(GameObject go, int layers, bool dynamicAttach) where T : XRGrabInteractable
    {
        var g = go.AddComponent<T>();
        g.interactionLayers = layers;
        g.movementType = XRBaseInteractable.MovementType.Instantaneous;
        g.useDynamicAttach = dynamicAttach;
        return g;
    }

    static XRSocketInteractor Socket(string name, Transform parent, Vector3 pos, Vector3 size, int layers)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent); go.transform.position = pos;
        var c = go.AddComponent<BoxCollider>(); c.isTrigger = true; c.size = size;
        var s = go.AddComponent<XRSocketInteractor>();
        s.interactionLayers = layers;
        return s;
    }

    static void Dispenser(Transform at, GameObject prefab, Vector3 spawnPos)
    {
        var d = at.gameObject.AddComponent<Dispenser>();
        d.prefab = prefab;
        d.spawnPoint = Marker(at, "Spawn point", at.InverseTransformPoint(spawnPos));
    }

    static void Feedback(GameObject go)
    {
        var f = go.AddComponent<InteractionFeedback>();
        f.selectSound = selectSound; f.releaseSound = releaseSound;
    }

    static GameObject Stage(GameObject root, string name)
    {
        var s = new GameObject(name); s.transform.SetParent(root.transform, false); return s;
    }

    static void Piece(GameObject stage, Vector3 localPos, Vector3 scale, Material m)
    {
        var p = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        Object.DestroyImmediate(p.GetComponent<Collider>());
        p.transform.SetParent(stage.transform, false);
        p.transform.localPosition = localPos; p.transform.localScale = scale;
        p.GetComponent<Renderer>().sharedMaterial = m;
    }

    // Box at a world position (prefab roots sit at the origin, so there it is also the local position).
    // Parents must be unscaled. Keeps its collider unless told not to.
    static GameObject Box(string name, Transform parent, Vector3 pos, Vector3 scale, Material m, bool collider = true)
    {
        var b = GameObject.CreatePrimitive(PrimitiveType.Cube);
        b.name = name;
        b.transform.SetParent(parent, false);
        b.transform.position = pos;
        b.transform.localScale = scale;
        if (!collider) Object.DestroyImmediate(b.GetComponent<Collider>());
        b.GetComponent<Renderer>().sharedMaterial = m;
        return b;
    }

    static Transform Marker(Transform parent, string name, Vector3 localPos)
    {
        var t = new GameObject(name).transform; t.SetParent(parent, false); t.localPosition = localPos; return t;
    }

    // Text auto-sizes to fit `size` (metres), so it never spills past its panel.
    static TMP_Text Text(string name, Transform parent, Vector3 pos, Vector2 size, Color colour)
    {
        var go = new GameObject(name); go.transform.SetParent(parent); go.transform.position = pos;
        var t = go.AddComponent<TextMeshPro>();
        t.alignment = TextAlignmentOptions.Center;
        t.color = colour;
        t.rectTransform.sizeDelta = size;
        t.enableAutoSizing = true; t.fontSizeMin = 0.1f; t.fontSizeMax = 1.2f;
        t.text = "";
        return t;
    }

    static Material Mat(string name, Color c)
    {
        string path = $"{Root}/Materials/Greybox/{name}.mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (!m) { m = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(m, path); }
        m.SetColor("_BaseColor", c);
        m.enableInstancing = true;
        EditorUtility.SetDirty(m);
        return m;
    }

    // Renders the stall from the player's eye (1.6 m) to Logs/stall-preview.png, for checking the layout
    // without a headset. Batch mode without -nographics: -executeMethod StallBuilder.RenderPreview
    [MenuItem("Slice & Sell/Render Stall Preview")]
    public static void RenderPreview()
    {
        EditorSceneManager.OpenScene(ScenePath);
        // Show what Play mode shows: sample text, and one item at each dispenser. The scene is not saved.
        var orders = Object.FindAnyObjectByType<OrderManager>();
        orders.orderCard.text = "Guava\n2 cuts"; orders.chalkboard.text = "THB 40\nServed 2/3";
        foreach (var d in Object.FindObjectsByType<SliceAndSell.Dispenser>(FindObjectsSortMode.None))
            Object.Instantiate(d.prefab, d.spawnPoint.position, d.spawnPoint.rotation);
        var cam = new GameObject("Preview cam").AddComponent<Camera>();
        cam.transform.position = new Vector3(0, 1.6f, -0.05f);
        cam.transform.LookAt(new Vector3(0, 1.15f, 0.75f));
        cam.fieldOfView = 80;
        var rt = new RenderTexture(1600, 1000, 24);
        cam.targetTexture = rt;
        ShaderUtil.allowAsyncCompilation = false;   // otherwise the first frame shows placeholder shaders
        cam.Render(); cam.Render();
        RenderTexture.active = rt;
        var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0); tex.Apply();
        File.WriteAllBytes("Logs/stall-preview.png", tex.EncodeToPNG());
        RenderTexture.active = null;
        Object.DestroyImmediate(cam.gameObject);
        Debug.Log("[StallBuilder] Preview written to Logs/stall-preview.png");
    }
}
