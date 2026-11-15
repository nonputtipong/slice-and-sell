using System;
using System.IO;
using System.Linq;
using HCD545;
using SliceAndSell;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Filtering;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using Object = UnityEngine.Object;

// Builds the prefabs (fruits from Models/Fruits/fruits.json, knife, bag, extras, customers), the two levels and
// the Stall scene, from the Lab 5 scene (which already has the XR rig, the XR Interaction Simulator and the
// tracking-lost banner). Re-run it after changing anything here. Batch mode: -executeMethod StallBuilder.Build
public static class StallBuilder
{
    const string Root = "Assets/_Project";
    const string ScenePath = Root + "/Scenes/Stall.unity";
    const string Lab5Scene = "Assets/HCD545/Lab5/Scenes/Lab5Interaction.unity";
    const string FruitsJson = Root + "/Models/Fruits/fruits.json";
    const int LayerDefault = 1 << 0, LayerFruit = 1 << 1, LayerBag = 1 << 2;   // XRI/Settings/InteractionLayerSettings
    const float CounterTop = 0.9f;

    static readonly string[] Lab5Roots = { "Lamp", "Cube 3 cm", "Cube 6 cm", "Cube 12 cm", "Grab table", "Teleport anchors",
                                           "Ray shelf", "Grab trial", "Comfort Menu", "Poke panel" };
    static readonly string[] Lab5RoomParts = { "Sofa (2.73 m)", "White knot" };

    // Knife grip for tracked hands (first guess; tune on the headset, then rebuild).
    static readonly Vector3 KnifeHandGripPosition = new Vector3(0, 0, 0.035f);
    static readonly Vector3 KnifeHandGripEuler = Vector3.zero;

    // Customers walk along the street behind the counter (far edge z 0.85) and stop in front of the tray.
    static readonly Vector3 CustomerEntry = new Vector3(-2.5f, 0, 1.35f), CounterSpot = new Vector3(0, 0, 1.35f), CustomerExit = new Vector3(2.5f, 0, 1.35f);

    // ---------- fruits.json (written by Tools/Blender/make_fruits.py) ----------
    [Serializable] class FruitsFile { public FruitSpec[] fruits; }
    [Serializable] class FruitSpec { public string name, file, juiceColor; public int price; public float[] size; public VolumeSpec cutVolume; public string[] stages; public CutSpec[] cuts; }
    [Serializable] class VolumeSpec { public string type; public float[] center, size; }
    [Serializable] class CutSpec { public float[] normal, point; public float depth; }
    static Vector3 V(float[] a) => new Vector3(a[0], a[1], a[2]);

    [MenuItem("Slice & Sell/Build Stall Scene")]
    public static void Build()
    {
        Directory.CreateDirectory(Root + "/Prefabs");
        Directory.CreateDirectory(Root + "/Materials/Greybox");
        Directory.CreateDirectory(Root + "/Materials/Fruits");
        Directory.CreateDirectory(Root + "/Levels");

        var specs = JsonUtility.FromJson<FruitsFile>(File.ReadAllText(FruitsJson)).fruits;
        var fruitPrefabs = specs.Select(s => SavePrefab(MakeFruit(s), s.name)).ToArray();
        var knifePrefab = SavePrefab(MakeKnife(), "Knife");
        var bag = SavePrefab(MakeBag(), "Bag");
        var skewer = SavePrefab(MakeSkewer(), "Skewer");
        var chili = SavePrefab(MakeChiliPacket(), "Chili packet");
        var customers = CustomerPrefabBuilder.Build().Select(p => p.GetComponent<Customer>()).ToArray();
        var menu = specs.Select(s => new FruitMenuItem { name = s.name, cuts = s.cuts.Length, price = s.price }).ToArray();
        var levels = MakeLevels();

        // Overwrite the file in place (not delete + copy) so Stall.unity keeps its GUID across rebuilds.
        File.Copy(Lab5Scene, ScenePath, true);
        AssetDatabase.ImportAsset(ScenePath, ImportAssetOptions.ForceUpdate);
        var scene = EditorSceneManager.OpenScene(ScenePath);
        foreach (var go in scene.GetRootGameObjects())
            if (Lab5Roots.Contains(go.name)) Object.DestroyImmediate(go);
        var room = scene.GetRootGameObjects().First(g => g.name == "Room").transform;
        foreach (var n in Lab5RoomParts) { var t = room.Find(n); if (t) Object.DestroyImmediate(t.gameObject); }

        BuildStall(fruitPrefabs, knifePrefab, bag, skewer, chili, customers, menu, levels);

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

    // GDD > Level plan. Numbers are placeholders until the first playtest.
    static LevelDefinition[] MakeLevels()
    {
        var l1 = LevelAsset("Level1 Opening Day");
        l1.title = "Opening Day"; l1.shiftSeconds = 0; l1.customers = 3; l1.fruits = new[] { "Guava" };
        l1.target = 60; l1.patienceSeconds = 0; l1.skewer = false; l1.chiliChance = 0; l1.seed = 1;
        var l2 = LevelAsset("Level2 Lunch Rush");
        l2.title = "Lunch Rush"; l2.shiftSeconds = 180; l2.customers = 0; l2.fruits = new[] { "Guava", "Mango", "Pineapple" };
        l2.target = 400; l2.patienceSeconds = 45; l2.skewer = true; l2.chiliChance = 0.4f; l2.seed = 2;
        foreach (var l in new[] { l1, l2 }) EditorUtility.SetDirty(l);
        return new[] { l1, l2 };
    }

    static LevelDefinition LevelAsset(string name)
    {
        string path = $"{Root}/Levels/{name}.asset";
        var l = AssetDatabase.LoadAssetAtPath<LevelDefinition>(path);
        if (!l) { l = ScriptableObject.CreateInstance<LevelDefinition>(); AssetDatabase.CreateAsset(l, path); }
        return l;
    }

    static void BuildStall(GameObject[] fruits, GameObject knifePrefab, GameObject bag, GameObject skewer, GameObject chili,
                           Customer[] customers, FruitMenuItem[] menu, LevelDefinition[] levels)
    {
        var stall = new GameObject("Stall").transform;

        // Counter: top at 0.9 m, front edge 25 cm in front of the player, everything within ~65 cm reach.
        Box("Counter", stall, new Vector3(0, 0.45f, 0.55f), new Vector3(1.4f, 0.9f, 0.6f), Mat("Counter", new Color(0.55f, 0.38f, 0.24f)));

        // Cutting board: fruit socket (fruit rests on the board top), guide line, scoop spot (bag socket) to its right.
        var boardGo = new GameObject("Cutting board"); boardGo.transform.SetParent(stall);
        Box("Board", boardGo.transform, new Vector3(-0.05f, 0.91f, 0.52f), new Vector3(0.32f, 0.02f, 0.22f), Mat("Board", new Color(0.86f, 0.72f, 0.5f)));
        var board = boardGo.AddComponent<CuttingBoard>();
        board.fruitSocket = Socket("Fruit socket", boardGo.transform, new Vector3(-0.05f, 0.98f, 0.52f), new Vector3(0.18f, 0.14f, 0.18f), LayerFruit);
        board.fruitSocket.attachTransform = Marker(board.fruitSocket.transform, "Attach", new Vector3(0, 0.92f - 0.98f, 0));
        board.scoopSocket = Socket("Scoop spot", boardGo.transform, new Vector3(0.2f, 0.995f, 0.52f), new Vector3(0.14f, 0.16f, 0.1f), LayerBag);
        Box("Scoop mark", boardGo.transform, new Vector3(0.2f, 0.902f, 0.52f), new Vector3(0.13f, 0.004f, 0.08f), Mat("Mark", new Color(1f, 0.8f, 0.2f)), false);
        board.guideLine = GuideLine(boardGo.transform);

        // Endless fruit crates on the left: guava nearest the board, mango behind it, pineapple to the far left.
        var crates = new[] { new Vector3(-0.36f, CounterTop, 0.42f), new Vector3(-0.36f, CounterTop, 0.66f), new Vector3(-0.58f, CounterTop, 0.42f) };
        for (int i = 0; i < fruits.Length && i < crates.Length; i++)
        {
            var crate = Crate(fruits[i].name + " crate", stall, crates[i], 0.2f, 0.08f);
            Dispenser(crate, fruits[i], crates[i] + new Vector3(0, 0.01f, 0));
        }

        // Bag stack and the extras (skewer cup, chili-salt tray) on the right.
        var bags = new GameObject("Bag stack").transform; bags.SetParent(stall); bags.position = new Vector3(0.42f, CounterTop, 0.5f);
        Dispenser(bags, bag, bags.position + new Vector3(0, 0.08f, 0));
        var cup = Crate("Skewer cup", stall, new Vector3(0.6f, CounterTop, 0.36f), 0.07f, 0.06f);
        Dispenser(cup, skewer, cup.position + new Vector3(0, 0.11f, 0));
        var chiliTray = Crate("Chili-salt tray", stall, new Vector3(0.6f, CounterTop, 0.56f), 0.12f, 0.03f);
        Dispenser(chiliTray, chili, chiliTray.position + new Vector3(0, 0.02f, 0));

        // Knife, lying in front of the board; it comes back if it lands on the floor.
        var knife = (GameObject)PrefabUtility.InstantiatePrefab(knifePrefab, stall);
        knife.transform.SetPositionAndRotation(new Vector3(-0.05f, 0.92f, 0.34f), Quaternion.Euler(0, 90, 90));
        knife.GetComponent<Knife>().board = board;

        // Orders: levels, customers walking along the street, order card (chest height, so it never hides a face), chalkboard.
        var orders = new GameObject("Orders").AddComponent<OrderManager>();
        orders.transform.SetParent(stall);
        orders.levels = levels; orders.menu = menu; orders.customerPrefabs = customers;
        orders.customerEntry = Marker(stall, "Customer entry", CustomerEntry);
        orders.counterSpot = Marker(stall, "Counter spot", CounterSpot);
        orders.customerExit = Marker(stall, "Customer exit", CustomerExit);
        Box("Order card", stall, new Vector3(0, 1.3f, 1.12f), new Vector3(0.36f, 0.2f, 0.01f), Mat("Card", new Color(0.98f, 0.96f, 0.9f)), false);
        orders.orderCard = Text("Order card text", stall, new Vector3(0, 1.3f, 1.112f), new Vector2(0.32f, 0.17f), new Color(0.15f, 0.1f, 0.05f));
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
        Feedback(bell, "bell");
        bell.AddComponent<ServiceBell>().orders = orders;

        // Juice, crunch, coins (GameEvents listeners).
        EffectsSetup.Create(stall);
    }

    // ---------- prefabs ----------

    // A fruit from its Blender GLB: stages Stage0..N, guides and cut volume from fruits.json.
    // The glTF materials are swapped for shared URP Lit ones (one per glTF material name): cheaper on Quest
    // (SRP batcher) and they have _BaseColor, which the hover tint uses.
    static GameObject MakeFruit(FruitSpec s)
    {
        var root = new GameObject(s.name);
        var model = (GameObject)Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>($"{Root}/Models/Fruits/{s.file}"), root.transform);
        model.name = "Model";
        foreach (var r in model.GetComponentsInChildren<Renderer>(true))
            r.sharedMaterials = r.sharedMaterials.Select(FruitMaterial).ToArray();

        var rb = root.AddComponent<Rigidbody>(); rb.mass = Mathf.Clamp(s.size[0] * s.size[1] * s.size[2] * 900f, 0.1f, 2f);
        Collider col;
        if (s.cutVolume.type == "sphere")
        {
            var sc = root.AddComponent<SphereCollider>(); sc.center = V(s.cutVolume.center); sc.radius = s.cutVolume.size[0] / 2f; col = sc;
        }
        else
        {
            var bc = root.AddComponent<BoxCollider>(); bc.center = V(s.cutVolume.center); bc.size = V(s.cutVolume.size); col = bc;
        }
        Grab(root, LayerDefault | LayerFruit, dynamicAttach: true);

        var fruit = root.AddComponent<Fruit>();
        fruit.fruitName = s.name;
        fruit.juiceColor = ColorUtility.TryParseHtmlString(s.juiceColor, out var jc) ? jc : Color.yellow;
        fruit.cutVolume = col;
        fruit.stages = s.stages.Select(n => FindDeep(model.transform, n)?.gameObject ?? throw new Exception($"{s.file}: no node {n}")).ToArray();
        fruit.guides = s.cuts.Select(c => new CutGuide { normal = V(c.normal), point = V(c.point), depth = c.depth }).ToArray();
        for (int i = 0; i < fruit.stages.Length; i++) fruit.stages[i].SetActive(i == 0);
        if (fruit.stages.Length != fruit.guides.Length + 1) throw new Exception($"{s.name}: {fruit.stages.Length} stages but {fruit.guides.Length} cuts");

        root.AddComponent<DespawnWhenDropped>();
        Feedback(root, "fruit");
        return root;
    }

    static Material FruitMaterial(Material gltf)
    {
        string path = $"{Root}/Materials/Fruits/{gltf.name}.mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (!m) { m = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(m, path); }
        var c = gltf.HasProperty("baseColorFactor") ? gltf.GetColor("baseColorFactor") : gltf.HasProperty("_BaseColor") ? gltf.GetColor("_BaseColor") : Color.white;
        m.SetColor("_BaseColor", c);
        m.SetFloat("_Smoothness", 0.35f);
        m.enableInstancing = true;
        EditorUtility.SetDirty(m);
        return m;
    }

    static Transform FindDeep(Transform t, string name)
    {
        if (t.name == name) return t;
        foreach (Transform c in t) { var f = FindDeep(c, name); if (f) return f; }
        return null;
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
        root.AddComponent<RespawnOnDrop>();     // Lab 5: back where it started if it lands on the floor
        Feedback(root, "knife");
        return root;
    }

    static GameObject MakeBag()
    {
        var root = new GameObject("Bag");
        var rb = root.AddComponent<Rigidbody>(); rb.mass = 0.05f;
        var col = root.AddComponent<BoxCollider>(); col.size = new Vector3(0.12f, 0.15f, 0.07f);
        Grab(root, LayerDefault | LayerBag, dynamicAttach: true);
        Box("Paper", root.transform, Vector3.zero, new Vector3(0.12f, 0.15f, 0.07f), Mat("Bag", new Color(0.93f, 0.9f, 0.8f)), false);
        var b = root.AddComponent<Bag>();
        b.filledVisual = Box("Slices", root.transform, new Vector3(0, 0.08f, 0), new Vector3(0.1f, 0.03f, 0.05f), Mat("Slices", new Color(0.98f, 0.8f, 0.55f)), false);
        b.skewerVisual = Box("Skewer", root.transform, new Vector3(0.035f, 0.11f, 0), new Vector3(0.004f, 0.12f, 0.004f), Mat("Skewer", new Color(0.85f, 0.7f, 0.45f)), false);
        b.chiliVisual = Box("Chili", root.transform, new Vector3(-0.035f, 0.09f, 0.02f), new Vector3(0.04f, 0.05f, 0.005f), Mat("Chili", new Color(0.85f, 0.15f, 0.1f)), false);
        foreach (var v in new[] { b.filledVisual, b.skewerVisual, b.chiliVisual }) v.SetActive(false);
        // Mouth: a trigger on top; an extra dropped into it is added to the bag.
        var mouth = new GameObject("Mouth"); mouth.transform.SetParent(root.transform, false); mouth.transform.localPosition = new Vector3(0, 0.08f, 0);
        var mc = mouth.AddComponent<BoxCollider>(); mc.isTrigger = true; mc.size = new Vector3(0.14f, 0.06f, 0.09f);
        mouth.AddComponent<BagMouth>().bag = b;
        root.AddComponent<DespawnWhenDropped>();
        Feedback(root, "bag");
        return root;
    }

    // Thin to look at, but with a 1 cm grab radius so a pinch finds it.
    static GameObject MakeSkewer()
    {
        var root = new GameObject("Skewer");
        root.AddComponent<Rigidbody>().mass = 0.01f;
        var col = root.AddComponent<CapsuleCollider>(); col.radius = 0.01f; col.height = 0.2f; col.direction = 1;
        Box("Stick", root.transform, Vector3.zero, new Vector3(0.004f, 0.2f, 0.004f), Mat("Skewer", new Color(0.85f, 0.7f, 0.45f)), false);
        Grab(root, LayerDefault, dynamicAttach: true);
        root.AddComponent<Extra>().kind = ExtraKind.Skewer;
        root.AddComponent<DespawnWhenDropped>();
        Feedback(root, "extra");
        return root;
    }

    static GameObject MakeChiliPacket()
    {
        var root = new GameObject("Chili packet");
        root.AddComponent<Rigidbody>().mass = 0.02f;
        Box("Packet", root.transform, Vector3.zero, new Vector3(0.06f, 0.012f, 0.08f), Mat("Chili", new Color(0.85f, 0.15f, 0.1f)));
        Grab(root, LayerDefault, dynamicAttach: true);
        root.AddComponent<Extra>().kind = ExtraKind.Chili;
        root.AddComponent<DespawnWhenDropped>();
        Feedback(root, "extra");
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

    // An open box of the given inner width and wall height, standing on the counter at `pos`.
    static Transform Crate(string name, Transform parent, Vector3 pos, float width, float height)
    {
        var t = new GameObject(name).transform; t.SetParent(parent); t.position = pos;
        var wood = Mat("Crate", new Color(0.45f, 0.3f, 0.18f));
        float h = width / 2f + 0.01f, y = height / 2f;
        Box("Wall L", t, pos + new Vector3(-h, y, 0), new Vector3(0.02f, height, width + 0.02f), wood);
        Box("Wall R", t, pos + new Vector3(h, y, 0), new Vector3(0.02f, height, width + 0.02f), wood);
        Box("Wall F", t, pos + new Vector3(0, y, -h), new Vector3(width + 0.02f, height, 0.02f), wood);
        Box("Wall B", t, pos + new Vector3(0, y, h), new Vector3(width + 0.02f, height, 0.02f), wood);
        return t;
    }

    static void Dispenser(Transform at, GameObject prefab, Vector3 spawnPos)
    {
        var d = at.gameObject.AddComponent<SliceAndSell.Dispenser>();
        d.prefab = prefab;
        d.spawnPoint = Marker(at, "Spawn point", at.InverseTransformPoint(spawnPos));
    }

    static void Feedback(GameObject go, string item)
    {
        var f = go.AddComponent<InteractionFeedback>();
        f.selectSound = EffectsSetup.GrabSounds(item).FirstOrDefault();
        f.releaseSound = EffectsSetup.ReleaseSounds(item).FirstOrDefault();
    }

    static LineRenderer GuideLine(Transform parent)
    {
        var go = new GameObject("Guide line"); go.transform.SetParent(parent, false);
        var lr = go.AddComponent<LineRenderer>();
        lr.positionCount = 2; lr.useWorldSpace = true;
        lr.startWidth = lr.endWidth = 0.003f;
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; lr.receiveShadows = false;
        string path = $"{Root}/Materials/Greybox/Guide.mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (!m) { m = new Material(Shader.Find("Universal Render Pipeline/Unlit")); AssetDatabase.CreateAsset(m, path); }
        m.SetColor("_BaseColor", new Color(1f, 1f, 0.85f));
        lr.sharedMaterial = m;
        lr.enabled = false;
        return lr;
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
        // Show what Play mode shows: sample text, one item at each dispenser, a customer at the counter,
        // and a fruit on the board half cut with its guide. The scene is not saved.
        var orders = Object.FindAnyObjectByType<OrderManager>();
        orders.orderCard.text = "Mango\n3 cuts\n+ skewer, chili"; orders.chalkboard.text = "THB 120 / 400\nCombo x4\n2:15";
        foreach (var d in Object.FindObjectsByType<SliceAndSell.Dispenser>(FindObjectsSortMode.None))
            Object.Instantiate(d.prefab, d.spawnPoint.position, d.spawnPoint.rotation);
        // (The patience ring builds its mesh in Awake, which doesn't run in edit mode, so it isn't shown here.)
        if (orders.customerPrefabs.Length > 0)
            Object.Instantiate(orders.customerPrefabs[0], orders.counterSpot.position, Quaternion.Euler(0, 180, 0));
        var board = Object.FindAnyObjectByType<CuttingBoard>();
        var mango = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>($"{Root}/Prefabs/Mango.prefab"),
                                       board.fruitSocket.attachTransform.position, Quaternion.identity).GetComponent<Fruit>();
        mango.stages[0].SetActive(false); mango.stages[1].SetActive(true);
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
