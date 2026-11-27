using System;
using System.IO;
using System.Linq;
using HCD545;
using SliceAndSell;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Filtering;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using Object = UnityEngine.Object;

// Builds the prefabs (fruits from Models/Fruits/fruits.json, knife, Eto, bowl, extras, customers), the two levels and
// the Stall scene, from the Lab 5 scene (which already has the XR rig, the XR Interaction Simulator and the
// tracking-lost banner). Re-run it after changing anything here. Batch mode: -executeMethod StallBuilder.Build
public static class StallBuilder
{
    const string Root = "Assets/_Project";
    const string ScenePath = Root + "/Scenes/Stall.unity";
    const string Lab5Scene = "Assets/HCD545/Lab5/Scenes/Lab5Interaction.unity";
    const string FruitsJson = Root + "/Models/Fruits/fruits.json";
    const int LayerDefault = 1 << 0, LayerFruit = 1 << 1, LayerBowl = 1 << 2;   // XRI/Settings/InteractionLayerSettings
    const int LayerMag = 1 << 3;                                                 // pistol magazines: only the grip's socket takes them
    const float CounterTop = 0.9f;

    static readonly string[] Lab5Roots = { "Lamp", "Cube 3 cm", "Cube 6 cm", "Cube 12 cm", "Grab table", "Teleport anchors",
                                           "Ray shelf", "Grab trial", "Comfort Menu", "Poke panel" };
    static readonly string[] Lab5RoomParts = { "Sofa (2.73 m)", "White knot" };

    // Knife grip for tracked hands (first guess; tune on the headset, then rebuild).
    static readonly Vector3 KnifeHandGripPosition = new Vector3(0, 0, 0.035f);
    static readonly Vector3 KnifeHandGripEuler = Vector3.zero;

    // Customers walk along the street behind the counter (far edge z 0.85) and stop in front of the tray.
    // Customers walk up the soi to the counter and back down it; the next one in Level 3 waits a step behind (StreetBuilder).
    static readonly Vector3 CustomerEntry = StreetBuilder.CustomerEntry, CounterSpot = new Vector3(0, 0, 1.35f), CustomerExit = StreetBuilder.CustomerExit;
    static readonly Vector3 QueueSpot = StreetBuilder.QueueSpot;

    // ---------- fruits.json (written by Tools/Blender/make_fruits.py) ----------
    [Serializable] class FruitsFile { public FruitSpec[] fruits; }
    [Serializable] class FruitSpec { public string name, file, juiceColor; public int price; public bool cleaver; public float[] size; public VolumeSpec cutVolume; public string[] stages; public CutSpec[] cuts; }
    [Serializable] class VolumeSpec { public string type; public float[] center, size; }
    [Serializable] class CutSpec { public float[] normal, point; public float depth; }
    static Vector3 V(float[] a) => new Vector3(a[0], a[1], a[2]);

    // Slice & Sell > Build Stall Scene + Bake Lighting: the full build for a release (the bake takes minutes).
    [MenuItem("Slice & Sell/Build Stall Scene + Bake Lighting")]
    public static void BuildAndBake()
    {
        Build();
        var scene = EditorSceneManager.OpenScene(ScenePath);
        StreetBuilder.BakeLighting();
        StreetBuilder.BakeOcclusion();
        EditorSceneManager.SaveScene(scene);
    }

    [MenuItem("Slice & Sell/Build Stall Scene")]
    public static void Build()
    {
        Directory.CreateDirectory(Root + "/Prefabs");
        Directory.CreateDirectory(Root + "/Materials/Greybox");
        Directory.CreateDirectory(Root + "/Materials/Fruits");
        Directory.CreateDirectory(Root + "/Levels");

        UiKit.Font = ThaiFont();
        var specs = JsonUtility.FromJson<FruitsFile>(File.ReadAllText(FruitsJson)).fruits;
        var fruitPrefabs = specs.Select(s => SavePrefab(MakeFruit(s), s.name)).ToArray();
        var knifePrefab = SavePrefab(MakeKnife(), "Knife");
        var cleaverPrefab = SavePrefab(MakeCleaver(), "Cleaver");
        var bowl = SavePrefab(MakeBowl(), "Bowl");
        var skewer = SavePrefab(MakeSkewer(), "Skewer");
        var chili = SavePrefab(MakeChili(), "Chili");
        // With the other prefabs, before the scene opens: saving a prefab mid-build drops unrecorded changes on
        // prefab instances already in the scene (the knives' rack board was lost that way).
        var bulletPrefab = SavePrefab(MakeBullet(), "Bullet");
        magPrefab = SavePrefab(MakeMagazine(), "Magazine");
        pistolPrefab = SavePrefab(MakePistol(bulletPrefab.GetComponent<Bullet>()), "Pistol");
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
        StreetBuilder.Build(room, UiKit.Font);

        // Stationary game (GDD > Comfort): no move, turn or teleport, so teleport/snap turn/vignette aren't needed.
        foreach (var p in Object.FindObjectsByType<UnityEngine.XR.Interaction.Toolkit.Locomotion.LocomotionProvider>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            p.enabled = false;
        foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None).Where(t => t.name.Contains("Teleport Interactor")))
            t.gameObject.SetActive(false);

        // Quest performance (tasks/handoff-quest3-performance.md, Tier 1).
        var rig = Object.FindAnyObjectByType<Unity.XR.CoreUtils.XROrigin>();
        rig.Camera.farClipPlane = 80f;                       // the fog ends at 70 m: nothing past it is visible
        rig.gameObject.AddComponent<FoveationLevel>();       // fixed foveated rendering (OpenXR feature on for Android)
        // The editor-only XR Interaction Simulator (EditorSimulatorSwitch turns it on in the Editor without a headset)
        // stays out of player builds, and with it ~14 MB of XRI sample assets it references.
        foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None).Where(t => t.name == "XR Interaction Simulator"))
            t.gameObject.tag = "EditorOnly";

        BuildStall(fruitPrefabs, knifePrefab, cleaverPrefab, bowl, skewer, chili, customers, menu, levels);

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
        var l3 = LevelAsset("Level3 Night Market");
        l3.title = "Night Market"; l3.shiftSeconds = 240; l3.customers = 0; l3.fruits = new[] { "Guava", "Mango", "Pineapple", "Watermelon" };
        l3.target = 700; l3.patienceSeconds = 40; l3.skewer = true; l3.chiliChance = 0.5f; l3.queue = true; l3.seed = 3;
        foreach (var l in new[] { l1, l2, l3 }) EditorUtility.SetDirty(l);
        return new[] { l1, l2, l3 };
    }

    static LevelDefinition LevelAsset(string name)
    {
        string path = $"{Root}/Levels/{name}.asset";
        var l = AssetDatabase.LoadAssetAtPath<LevelDefinition>(path);
        if (!l) { l = ScriptableObject.CreateInstance<LevelDefinition>(); AssetDatabase.CreateAsset(l, path); }
        return l;
    }

    static void BuildStall(GameObject[] fruits, GameObject knifePrefab, GameObject cleaverPrefab, GameObject bowl, GameObject skewer, GameObject chili,
                           Customer[] customers, FruitMenuItem[] menu, LevelDefinition[] levels)
    {
        var stall = new GameObject("Stall").transform;
        stall.gameObject.AddComponent<StanceHeight>();     // seated: the whole stall drops to arm height

        // Counter: top at 0.9 m, front edge 25 cm in front of the player, everything within ~65 cm reach.
        var counter = Box("Counter", stall, new Vector3(0, 0.45f, 0.55f), new Vector3(1.4f, 0.9f, 0.6f), Mat("Counter", new Color(0.55f, 0.38f, 0.24f)));

        // Cutting board: fruit socket (fruit rests on the board top), guide line, scoop spot (bowl socket) to its right.
        var boardGo = new GameObject("Cutting board"); boardGo.transform.SetParent(stall);
        var boardBox = Box("Board", boardGo.transform, new Vector3(-0.05f, 0.91f, 0.52f), new Vector3(0.32f, 0.02f, 0.22f), Mat("Board", new Color(0.86f, 0.72f, 0.5f)));
        // Kenney Food Kit board as the look (0.82 x 0.58, turned 90 degrees and scaled to 0.32 x 0.23); the box keeps the collider.
        boardBox.GetComponent<Renderer>().enabled = false;
        var boardLook = StreetBuilder.Prop("FoodKit", "cutting-board", boardGo.transform, new Vector3(-0.05f, 0.9f, 0.52f), 90f, 0.39f);
        var board = boardGo.AddComponent<CuttingBoard>();
        board.fruitSocket = Socket("Fruit socket", boardGo.transform, new Vector3(-0.05f, 0.98f, 0.52f), new Vector3(0.18f, 0.14f, 0.18f), LayerFruit);
        board.fruitSocket.attachTransform = Marker(board.fruitSocket.transform, "Attach", new Vector3(0, 0.92f - 0.98f, 0));
        // The bowl's root is its middle (BowlHalfHeight above its foot), so a socket at that height stands it on the deck.
        board.scoopSocket = Socket("Scoop spot", boardGo.transform, new Vector3(0.2f, CounterTop + BowlHalfHeight, 0.52f), new Vector3(0.16f, 0.12f, 0.16f), LayerBowl);
        var scoopMark = Box("Scoop mark", boardGo.transform, new Vector3(0.2f, 0.902f, 0.52f), new Vector3(0.15f, 0.004f, 0.15f), Mat("Mark", new Color(1f, 0.8f, 0.2f)), false);
        DeckLabel("BOWL HERE", boardGo.transform, new Vector3(0.2f, 0.905f, 0.52f), 0.13f);
        board.guideLine = GuideLine(boardGo.transform);
        SocketFeedback(board.fruitSocket, boardLook.GetComponentInChildren<Renderer>().gameObject);
        SocketFeedback(board.scoopSocket, scoopMark);

        // Endless fruit crates on the left: guava nearest the board, mango behind it, pineapple to the far left,
        // watermelon (Level 3 only) in the far-left back corner, in a wider crate. All inside the cart's lip (deck top
        // x -0.67..0.67, z 0.28..0.82): guava and mango crates a little narrower so the column fits between the melon
        // crate (sharing its wall) and the board's left edge (x -0.21).
        var crates = new[] { new Vector3(-0.315f, CounterTop, 0.42f), new Vector3(-0.315f, CounterTop, 0.66f), new Vector3(-0.55f, CounterTop, 0.42f),
                             new Vector3(-0.535f, CounterTop, 0.67f) };
        var widths = new[] { 0.17f, 0.17f, 0.2f, 0.23f };
        for (int i = 0; i < fruits.Length && i < crates.Length; i++)
        {
            bool big = fruits[i].GetComponent<Fruit>().needsCleaver;
            var crate = Crate(fruits[i].name + " crate", stall, crates[i], widths[i], big ? 0.05f : 0.08f);
            Dispenser(crate, fruits[i], crates[i] + new Vector3(0, 0.01f, 0));
        }

        // A bowl always waits on the scoop spot, next to the board: the cut fruit goes in by hand. Lifting it (to the
        // hand-off tray) makes no new one; the next appears once it is handed over, or dropped and despawned.
        Dispenser(board.scoopSocket.transform, bowl, board.scoopSocket.transform.position + new Vector3(0, 0.002f, 0));
        var bowls = board.scoopSocket.GetComponent<SliceAndSell.Dispenser>();
        bowls.onlyWhenGone = true;
        // The extras (skewer cup, chili tray) on the right.
        var cup = Crate("Skewer cup", stall, new Vector3(0.615f, CounterTop, 0.42f), 0.07f, 0.06f);     // behind the cleaver's blade
        Dispenser(cup, skewer, cup.position + new Vector3(0, 0.11f, 0));
        var chiliTray = Crate("Chili tray", stall, new Vector3(0.59f, CounterTop, 0.56f), 0.12f, 0.03f);
        Dispenser(chiliTray, chili, chiliTray.position + new Vector3(0, 0.012f, 0));
        // On the deck just behind the tray (the skewer cup hides its front): the order card says "chili", this is it.
        DeckLabel("CHILI", stall, new Vector3(0.59f, CounterTop + 0.012f, 0.695f), 0.09f);

        // Knife, lying in front of the board; it comes back if it lands on the floor.
        var knife = (GameObject)PrefabUtility.InstantiatePrefab(knifePrefab, stall);
        knife.transform.SetPositionAndRotation(new Vector3(-0.05f, 0.92f, 0.34f), Quaternion.Euler(0, 90, 90));
        knife.GetComponent<Knife>().board = board;

        // Eto lying at the front right, handle toward the board, blade edge away from the player, resting on the
        // handle and the blade's far edge (tilted 8.7 deg about its length; its weight is in the blade).
        var cleaver = (GameObject)PrefabUtility.InstantiatePrefab(cleaverPrefab, stall);
        cleaver.transform.SetPositionAndRotation(new Vector3(0.22f, CounterTop + 0.0171f, 0.3f),
                                                 Quaternion.AngleAxis(8.7f, Vector3.right) * Quaternion.LookRotation(Vector3.right, Vector3.back));
        cleaver.GetComponent<Knife>().board = board;
        var whoosh = AssetDatabase.LoadAssetAtPath<AudioClip>(Root + "/ThirdParty/Kenney/InterfaceSounds/maximize_001.ogg");
        // The pistol (for the fruit thief) lying on its side where the bowl stack was, grip toward the player, barrel
        // to the right; the ammo tray of endless magazines just behind it. It comes back to here like the knives.
        var pistol = (GameObject)PrefabUtility.InstantiatePrefab(pistolPrefab, stall);
        pistol.transform.SetPositionAndRotation(new Vector3(0.37f, CounterTop + 0.0155f, 0.46f), Quaternion.LookRotation(Vector3.right, Vector3.forward));
        var ammo = Crate("Ammo tray", stall, new Vector3(0.42f, CounterTop, 0.6f), 0.07f, 0.025f);
        Dispenser(ammo, magPrefab, ammo.position + new Vector3(0, 0.043f, 0));
        foreach (var tool in new[] { knife, cleaver, pistol })
        {
            var rack = tool.GetComponent<ReturnToRack>();
            rack.board = boardGo.transform.Find("Board");     // the board's top, not the "Cutting board" root at the stall origin
            rack.whoosh = whoosh;
        }

        // Orders: levels, customers walking along the street, order card (chest height, so it never hides a face), chalkboard.
        var orders = new GameObject("Orders").AddComponent<OrderManager>();
        orders.transform.SetParent(stall);
        orders.levels = levels; orders.menu = menu; orders.customerPrefabs = customers;
        orders.customerEntry = Marker(stall, "Customer entry", CustomerEntry);
        orders.counterSpot = Marker(stall, "Counter spot", CounterSpot);
        orders.customerExit = Marker(stall, "Customer exit", CustomerExit);
        orders.queueSpot = Marker(stall, "Queue spot", QueueSpot);
        // Order card: a paper ticket on a dark-wood backing with an orange header strip, at the customer's chest
        // (1.24 m): seated, the eyes drop twice as far as the stall, and a higher card covered their chin.
        var card = Box("Order card", stall, new Vector3(0, 1.24f, 1.12f), new Vector3(0.36f, 0.2f, 0.01f), Mat("Card", new Color(0.99f, 0.95f, 0.86f)), false);
        Box("Order card backing", card.transform.parent, new Vector3(0, 1.24f, 1.126f), new Vector3(0.38f, 0.22f, 0.006f), Mat("Wood trim", new Color(0.22f, 0.13f, 0.08f)), false);
        Box("Order card strip", card.transform.parent, new Vector3(0, 1.329f, 1.1145f), new Vector3(0.36f, 0.022f, 0.001f), Mat("Accent", new Color(0.95f, 0.55f, 0.16f)), false);
        orders.orderCard = Text("Order card text", stall, new Vector3(0, 1.228f, 1.112f), new Vector2(0.32f, 0.15f), new Color(0.17f, 0.1f, 0.05f));
        orders.orderCard.fontStyle = FontStyles.Bold;
        // Chalkboard in a wooden frame with a chalk ledge; warm chalk-white text (OrderManager sizes the lines).
        Box("Chalkboard", stall, new Vector3(-0.55f, 1.45f, 0.97f), new Vector3(0.5f, 0.3f, 0.02f), Mat("Chalk", new Color(0.12f, 0.22f, 0.16f)), false);
        Box("Chalkboard frame", stall, new Vector3(-0.55f, 1.45f, 0.978f), new Vector3(0.54f, 0.34f, 0.02f), Mat("Wood trim", new Color(0.22f, 0.13f, 0.08f)), false);
        Box("Chalk ledge", stall, new Vector3(-0.55f, 1.285f, 0.955f), new Vector3(0.5f, 0.012f, 0.03f), Mat("Wood trim", new Color(0.22f, 0.13f, 0.08f)), false);
        orders.chalkboard = Text("Chalkboard text", stall, new Vector3(-0.55f, 1.45f, 0.955f), new Vector2(0.44f, 0.25f), new Color(0.97f, 0.95f, 0.88f));
        // Money bowl (the cash box's look; coins clink there) on the free deck behind the board, inside the cart's lip.
        var cash = Box("Cash box", stall, new Vector3(-0.03f, 0.94f, 0.728f), new Vector3(0.14f, 0.08f, 0.1f), Mat("Cash box", new Color(0.2f, 0.35f, 0.6f)));
        var moneyBowl = StreetBuilder.Prop("MoneyBowl", stall, new Vector3(-0.03f, CounterTop, 0.728f), 0f);
        if (moneyBowl) { moneyBowl.localScale = Vector3.one * 0.75f; cash.GetComponent<Renderer>().enabled = false; }
        orders.cashBox = cash.transform;

        // Shop-sign main menu starts shifts; results board after each one.
        orders.autoStart = false;
        var (mainMenu, _) = MenuBuilder.Build(stall, orders);

        // In-game menu at the scene root (it opens in front of the eyes); its MENU sign sits on the cart.
        var wristMenu = WristMenuBuilder.Build(null, stall, orders);
        UnityEditor.Events.UnityEventTools.AddPersistentListener(wristMenu.onExitToMenu, mainMenu.Show);

        // Hand-off tray (far edge, in front of the customer, its back rim just inside the cart's lip at z 0.82).
        var tray = new GameObject("Hand-off tray"); tray.transform.SetParent(stall);
        var trayMesh = Box("Tray", tray.transform, new Vector3(0.25f, 0.905f, 0.73f), new Vector3(0.2f, 0.01f, 0.16f), Mat("Tray", new Color(0.9f, 0.45f, 0.15f)));
        var traySocket = Socket("Tray socket", tray.transform, new Vector3(0.25f, CounterTop + 0.006f + BowlHalfHeight, 0.73f), new Vector3(0.2f, 0.14f, 0.16f), LayerBowl);
        traySocket.gameObject.AddComponent<HandoffTray>().orders = orders;
        // A real tray with a rim (Blender) as the look, so it reads as "put it down here"; the box keeps the collider.
        var trayLook = StreetBuilder.Prop("HandoverTray", tray.transform, new Vector3(0.25f, CounterTop, 0.73f), 0f);
        if (trayLook) trayMesh.GetComponent<Renderer>().enabled = false;
        SocketFeedback(traySocket, trayLook ? trayLook.GetComponentInChildren<Renderer>().gameObject : trayMesh);
        DeckLabel("HAND OVER", tray.transform, new Vector3(0.25f, CounterTop + 0.009f, 0.73f), 0.2f);

        // Service bell: poke from above, or ray + trigger.
        var bell = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        bell.name = "Service bell"; bell.transform.SetParent(stall);
        // Sized to the bell model (Ø 9 cm, 6 cm tall) so the finger meets the dome where it sees it.
        bell.transform.position = new Vector3(0.5f, 0.93f, 0.74f); bell.transform.localScale = new Vector3(0.09f, 0.03f, 0.09f);
        bell.GetComponent<Renderer>().sharedMaterial = Mat("Bell", new Color(0.95f, 0.75f, 0.2f));
        // A real counter bell as the look, under the bell so hover/press feedback tints it (scale undone: no skew, no rotation).
        var bellLook = StreetBuilder.Prop("ServiceBell", bell.transform, new Vector3(0, -1f, 0), 0f);
        if (bellLook) { bellLook.localScale = new Vector3(1f / 0.09f, 1f / 0.03f, 1f / 0.09f); bell.GetComponent<Renderer>().enabled = false; }
        var simple = bell.AddComponent<XRSimpleInteractable>();
        var poke = bell.AddComponent<XRPokeFilter>();
        poke.pokeInteractable = simple;
        poke.pokeConfiguration.Value.pokeDirection = PokeAxis.NegativeY;
        Feedback(bell, "bell");
        var ring = bell.AddComponent<ServiceBell>();
        ring.orders = orders;
        Dwell(bell, ring.Ring);

        // Level 1 tutorial: intro card, then glow + arrow + text for customer 1, text for customer 2.
        TutorialBuilder.Build(stall, orders, board, knife.GetComponent<Knife>(), stall.Find("Guava crate").GetComponent<SliceAndSell.Dispenser>(),
                              bowls.GetComponent<SliceAndSell.Dispenser>(), tray.GetComponentInChildren<XRSocketInteractor>());

        // Juice, crunch, coins (GameEvents listeners), and the market around the stall.
        EffectsSetup.Create(stall);
        StreetSounds(stall, orders.counterSpot);
        // The pushcart look replaces the counter box's own (the box keeps the collider); umbrella, bulbs, lanterns, scale...
        if (StreetBuilder.DressStall(stall, counter.transform.position)) counter.GetComponent<Renderer>().enabled = false;
        // Performance report, build C ("before" optimisation 2): -perfBeforeOpt2 on the command line skips the shadow
        // trimming and the decor batching. Never set for the real game.
        if (!Environment.GetCommandLineArgs().Contains("-perfBeforeOpt2"))
        {
            TrimShadows(stall);
            PickDecorBatches(stall);
        }
        else Debug.LogWarning("[StallBuilder] -perfBeforeOpt2: TrimShadows and BatchDecor skipped (performance report build C)");
        PerfHud(stall);
    }

    // Performance measuring (Lab 6 FrameHud + perf-log.csv): on the stall's upper right, over the bell and clear of the
    // counter and the customer's face, facing the player, Start route by ray. It exists only in Development builds on
    // the device (DevelopmentOnly): a release build and the Editor remove it at start. Added after the decor batching,
    // and a canvas, so it is never combined or shadow-trimmed.
    const string FrameHudPath = "Assets/HCD545/Lab6/Prefabs/FrameHud.prefab";
    static void PerfHud(Transform stall)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(FrameHudPath);
        if (!prefab) { Debug.LogWarning("[StallBuilder] no FrameHud prefab: performance HUD left out"); return; }
        var hud = (GameObject)PrefabUtility.InstantiatePrefab(prefab, stall);
        PrefabUtility.UnpackPrefabInstance(hud, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);   // its own copy: components can go
        var pos = new Vector3(0.6f, 1.38f, 0.85f);
        hud.transform.SetPositionAndRotation(stall.TransformPoint(pos), Quaternion.LookRotation(stall.TransformPoint(pos) - new Vector3(0f, 1.6f, 0f)));
        foreach (var follow in hud.GetComponentsInChildren<UnityEngine.XR.Interaction.Toolkit.UI.LazyFollow>(true)) Object.DestroyImmediate(follow);   // stays on the stall
        hud.AddComponent<SliceAndSell.Perf.DevelopmentOnly>();
        hud.AddComponent<SliceAndSell.Perf.PerfStatsLogger>().fps = hud.GetComponentInChildren<HCD545.FpsLogger>(true);
    }

    // Quest draw calls (handoff #6): the still decor under the stall is combined into a few batches at start
    // (BatchDecor), relative to the stall root so seated mode still lowers it. Left out: everything you pick up, UI
    // canvases and text, the tutorial's glow/arrow, customers and order logic, street audio (its motorbike moves),
    // effects, and anything hidden (the collider boxes behind looks).
    static void PickDecorBatches(Transform stall)
    {
        var picks = stall.GetComponentsInChildren<MeshRenderer>(false)
            .Where(r => r.enabled && r.TryGetComponent(out MeshFilter f) && f.sharedMesh
                        && !r.GetComponent<TMP_Text>()
                        && !r.GetComponentInParent<XRGrabInteractable>(true) && !r.GetComponentInParent<Canvas>(true)
                        && !r.GetComponentInParent<Tutorial>(true) && !r.GetComponentInParent<OrderManager>(true)
                        && !r.GetComponentInParent<StreetAudio>(true) && !r.GetComponentInParent<ParticleSystem>(true))
            .Select(r => r.gameObject).ToArray();
        stall.gameObject.AddComponent<BatchDecor>().decor = picks;
        Debug.Log($"[StallBuilder] {picks.Length} stall decor renderers combined at start (BatchDecor)");
    }

    // Quest shadow pass (tasks/handoff-quest3-performance.md #7): the stall is dynamic (seated mode moves it), so all of it
    // goes through the sun's real-time shadow map. Small decor (crate walls, lanterns, bulb strings, bowl, bell, tray,
    // cards, labels) stops casting; the cart, the umbrella, the chalkboard and everything you pick up keep their shadows,
    // which ground the counter and help judge depth.
    static void TrimShadows(Transform stall)
    {
        int off = 0;
        foreach (var r in stall.GetComponentsInChildren<Renderer>(true))
        {
            if (r.shadowCastingMode == UnityEngine.Rendering.ShadowCastingMode.Off || r.GetComponentInParent<XRGrabInteractable>(true)) continue;
            var s = r.bounds.size;
            bool small = Mathf.Max(s.x, Mathf.Max(s.y, s.z)) < 0.4f;
            bool bulbs = r.GetComponentsInParent<Transform>(true).Any(t => t.name.StartsWith("BulbString"));
            bool radio = r.name == "Radio look" || r.name == "Radio shelf";   // the low sun throws their long shadow onto the street ahead
            if ((!small && !bulbs) || radio) continue;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            off++;
        }
        Debug.Log($"[StallBuilder] {off} small stall renderers no longer cast real-time shadows");
    }

    // ---------- audio and font ----------

    const string Ambience = Root + "/ThirdParty/Ambience/";

    static void StreetSounds(Transform stall, Transform counterSpot)
    {
        var street = new GameObject("Street audio").AddComponent<StreetAudio>();
        street.transform.SetParent(stall, false);
        street.customerSpot = counterSpot;
        street.voices = Directory.GetFiles(Root + "/ThirdParty/Voices", "*.ogg").Select(AssetDatabase.LoadAssetAtPath<AudioClip>).Where(c => c).ToArray();

        street.ambience = Loop(street.gameObject, Clip("market-ambience.mp3", stream: true), 0.35f, spatial: false);

        // Radio on a little shelf behind the player: turn round and poke it (or ray it) to switch it off.
        var shelf = Box("Radio shelf", stall, new Vector3(0.45f, 0.95f, -0.45f), new Vector3(0.3f, 0.02f, 0.18f), Mat("Crate", new Color(0.45f, 0.3f, 0.18f)));
        var radio = Box("Radio", stall, new Vector3(0.45f, 1.02f, -0.45f), new Vector3(0.2f, 0.12f, 0.08f), Mat("Radio", new Color(0.75f, 0.2f, 0.18f)));
        street.radio = Loop(radio, Clip("radio-loop.ogg", stream: true), 0.45f, spatial: true);
        street.radioSwitch = radio.AddComponent<XRSimpleInteractable>();
        radio.GetComponent<Renderer>().enabled = false;     // Kenney radio as the look; the box keeps the poke collider
        StreetBuilder.Fit(Root + "/ThirdParty/Kenney/FurnitureKit/radio.glb", radio.transform.parent, new Vector3(0.45f, 0.96f, -0.45f), 180f, 0.14f, isStatic: false).name = "Radio look";
        var poke = radio.AddComponent<XRPokeFilter>();
        poke.pokeInteractable = street.radioSwitch;
        poke.pokeConfiguration.Value.pokeDirection = PokeAxis.NegativeZ;     // pushed from the player's side
        Feedback(radio, "bell");
        Dwell(radio, street.ToggleRadio);

        var bike = new GameObject("Motorbike"); bike.transform.SetParent(street.transform, false);
        street.motorbike = bike.AddComponent<AudioSource>();
        street.motorbike.clip = Clip("motorbike-pass.mp3", stream: false);
        street.motorbike.spatialBlend = 1f; street.motorbike.minDistance = 2f; street.motorbike.maxDistance = 30f;
        street.motorbike.playOnAwake = false; street.motorbike.loop = true;
        street.streetFrom = StreetBuilder.BikeFrom; street.streetTo = StreetBuilder.BikeTo;     // the cross street at the end of the soi
    }

    static AudioSource Loop(GameObject on, AudioClip clip, float volume, bool spatial)
    {
        var a = on.AddComponent<AudioSource>();
        a.clip = clip; a.loop = true; a.playOnAwake = true; a.volume = volume;
        a.spatialBlend = spatial ? 1f : 0f; a.minDistance = 0.4f; a.maxDistance = 8f;
        return a;
    }

    // Long loops are kept compressed (Vorbis in memory) on Quest instead of decompressed (~30 MB for the 1:25 bed).
    static AudioClip Clip(string file, bool stream)
    {
        string path = Ambience + file;
        var imp = (AudioImporter)AssetImporter.GetAtPath(path);
        var s = imp.defaultSampleSettings;
        var want = stream ? AudioClipLoadType.CompressedInMemory : AudioClipLoadType.DecompressOnLoad;
        if (s.loadType != want || s.compressionFormat != AudioCompressionFormat.Vorbis)
        {
            s.loadType = want; s.compressionFormat = AudioCompressionFormat.Vorbis; s.quality = 0.6f;
            imp.defaultSampleSettings = s;
            imp.SaveAndReimport();
        }
        return AssetDatabase.LoadAssetAtPath<AudioClip>(path);
    }

    // Noto Sans Thai as a TMP font asset (dynamic atlas): Thai text and the baht sign.
    static TMP_FontAsset ThaiFont()
    {
        const string path = Root + "/ThirdParty/Fonts/NotoSansThai SDF.asset";
        var fa = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
        if (fa) return fa;
        var font = AssetDatabase.LoadAssetAtPath<Font>(Root + "/ThirdParty/Fonts/NotoSansThai-Regular.ttf");
        fa = TMP_FontAsset.CreateFontAsset(font, 90, 9, UnityEngine.TextCore.LowLevel.GlyphRenderMode.SDFAA, 1024, 1024, AtlasPopulationMode.Dynamic);
        fa.name = "NotoSansThai SDF";
        AssetDatabase.CreateAsset(fa, path);
        fa.atlasTextures[0].name = "NotoSansThai Atlas";
        AssetDatabase.AddObjectToAsset(fa.atlasTextures[0], fa);
        fa.material.name = "NotoSansThai Material";
        AssetDatabase.AddObjectToAsset(fa.material, fa);
        AssetDatabase.SaveAssets();
        return fa;
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
        fruit.needsCleaver = s.cleaver;
        fruit.stages = s.stages.Select(n => FindDeep(model.transform, n)?.gameObject ?? throw new Exception($"{s.file}: no node {n}")).ToArray();
        fruit.guides = s.cuts.Select(c => new CutGuide { normal = V(c.normal), point = V(c.point), depth = c.depth }).ToArray();
        for (int i = 0; i < fruit.stages.Length; i++) fruit.stages[i].SetActive(i == 0);
        if (fruit.stages.Length != fruit.guides.Length + 1) throw new Exception($"{s.name}: {fruit.stages.Length} stages but {fruit.guides.Length} cuts");

        root.AddComponent<DespawnWhenDropped>();
        root.AddComponent<HoldOnTrackingLoss>();
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
        var handle = Box("Handle", root.transform, new Vector3(0, 0, 0), new Vector3(0.025f, 0.025f, 0.1f), Mat("Handle", new Color(0.35f, 0.2f, 0.1f)));
        var blade = Box("Blade", root.transform, new Vector3(0, 0, 0.11f), new Vector3(0.003f, 0.03f, 0.12f), Mat("Steel", new Color(0.8f, 0.82f, 0.85f)));
        // The Blender knife (black handle, brass rivets, bolster, pointed blade with a bright edge) over the two boxes,
        // which keep the colliders.
        if (StreetBuilder.Prop("Knife", root.transform, Vector3.zero, 0f))
            foreach (var b in new[] { handle, blade }) b.GetComponent<Renderer>().enabled = false;
        var attach = new GameObject("Attach").transform; attach.SetParent(root.transform, false);
        var grab = Grab<KnifeGrab>(root, LayerDefault, dynamicAttach: false);
        grab.attachTransform = attach;
        // Hands pinch the front of the handle (by the blade), like a chef's pinch grip. Tune on the headset.
        grab.handAttachTransform = Marker(root.transform, "Hand attach", KnifeHandGripPosition);
        grab.handAttachTransform.localRotation = Quaternion.Euler(KnifeHandGripEuler);
        var knife = root.AddComponent<Knife>();
        knife.edgeStart = Marker(root.transform, "Edge start", new Vector3(0, -0.015f, 0.05f));
        knife.edgeEnd = Marker(root.transform, "Edge end", new Vector3(0, -0.015f, 0.17f));
        root.AddComponent<ReturnToRack>();      // back to its rack 2 s after landing out of reach (board and whoosh set in BuildStall)
        root.AddComponent<HoldOnTrackingLoss>();
        Feedback(root, "knife");
        return root;
    }

    // The Eto (Thai chopping knife, Blender model): heavier than the paring knife, cuts every fruit and is the only tool
    // for the watermelon. One hand is enough; a second hand on the handle steadies it (XRI aims it between the two).
    // Built along +Z like the knife: a short wooden handle (z -0.06..0.055), a tall blade (z 0.065..0.2455) whose edge
    // faces down at y -0.085.
    static GameObject MakeCleaver()
    {
        var root = new GameObject("Cleaver");
        var rb = root.AddComponent<Rigidbody>(); rb.mass = 0.45f;
        // Weight in the steel blade (not spread evenly over the colliders): set down, it rests on the blade's edge and
        // the handle like a real cleaver, instead of balancing on the handle, blade raised.
        rb.automaticCenterOfMass = false; rb.centerOfMass = new Vector3(0f, -0.03f, 0.15f);
        var handle = Box("Handle", root.transform, new Vector3(0, 0, -0.0025f), new Vector3(0.031f, 0.031f, 0.115f), Mat("Handle", new Color(0.35f, 0.2f, 0.1f)));
        var blade = Box("Blade", root.transform, new Vector3(0, -0.0354f, 0.155f), new Vector3(0.004f, 0.1f, 0.18f), Mat("Cleaver steel", new Color(0.5f, 0.53f, 0.57f)));
        // The Blender Eto over the two boxes, which keep the colliders.
        if (StreetBuilder.Prop("Cleaver", root.transform, Vector3.zero, 0f))
            foreach (var b in new[] { handle, blade }) b.GetComponent<Renderer>().enabled = false;
        // Snaps into the hand (no dynamic attach): the handle in the palm, blade pointing forward, edge down, however it
        // was picked up, like the paring knife. A second hand may steady it, but only the first hand aims it.
        var grab = Grab<KnifeGrab>(root, LayerDefault, dynamicAttach: false);
        grab.attachTransform = Marker(root.transform, "Attach", new Vector3(0, 0, -0.0025f));
        grab.handAttachTransform = Marker(root.transform, "Hand attach", new Vector3(0, 0, -0.0025f));
        grab.handAttachTransform.localRotation = Quaternion.Euler(KnifeHandGripEuler);
        grab.selectMode = InteractableSelectMode.Multiple;
        var twoHands = root.AddComponent<UnityEngine.XR.Interaction.Toolkit.Transformers.XRGeneralGrabTransformer>();
        twoHands.allowTwoHandedRotation = UnityEngine.XR.Interaction.Toolkit.Transformers.XRGeneralGrabTransformer.TwoHandedRotationMode.FirstHandOnly;
        var tool = root.AddComponent<Knife>();
        tool.cleaver = true;
        tool.edgeStart = Marker(root.transform, "Edge start", new Vector3(0, -0.083f, 0.09f));
        tool.edgeEnd = Marker(root.transform, "Edge end", new Vector3(0, -0.083f, 0.23f));
        root.AddComponent<ReturnToRack>();
        root.AddComponent<HoldOnTrackingLoss>();
        Feedback(root, "knife");
        return root;
    }

    // ---------- pistol (the thief) ----------

    static GameObject pistolPrefab, magPrefab;     // saved in Build() with the other prefabs
    static readonly Color GunMetal = new Color(0.13f, 0.13f, 0.15f), GunGrip = new Color(0.27f, 0.2f, 0.15f);

    // Built along +Z like the knife: barrel forward, slide on top, grip down. The hand holds the grip ("Grip attach",
    // barrel along the hand's pointing direction). A socket under the grip takes a magazine (LayerMag only).
    static GameObject MakePistol(Bullet bullet)
    {
        var root = new GameObject("Pistol");
        var rb = root.AddComponent<Rigidbody>(); rb.mass = 0.8f;
        var metal = Mat("Gun metal", GunMetal);
        var slide = Box("Slide", root.transform, new Vector3(0, 0.03f, 0.045f), new Vector3(0.03f, 0.034f, 0.18f), metal);
        var grip = Box("Grip", root.transform, new Vector3(0, -0.025f, -0.02f), new Vector3(0.028f, 0.09f, 0.04f), Mat("Gun grip", GunGrip));
        Box("Trigger guard", root.transform, new Vector3(0, -0.008f, 0.032f), new Vector3(0.006f, 0.006f, 0.045f), metal, false);
        Box("Trigger", root.transform, new Vector3(0, 0.002f, 0.02f), new Vector3(0.005f, 0.016f, 0.006f), metal, false);
        Box("Front sight", root.transform, new Vector3(0, 0.05f, 0.125f), new Vector3(0.004f, 0.006f, 0.006f), metal, false);
        Box("Muzzle ring", root.transform, new Vector3(0, 0.03f, 0.136f), new Vector3(0.018f, 0.018f, 0.004f), Mat("Muzzle", new Color(0.05f, 0.05f, 0.05f)), false);

        var grab = Grab(root, LayerDefault, dynamicAttach: false);
        grab.attachTransform = Marker(root.transform, "Grip attach", new Vector3(0, -0.01f, -0.02f));
        // Grabbed by its body only: the magazine socket's trigger is not a handle.
        grab.colliders.Clear();
        grab.colliders.Add(slide.GetComponent<Collider>()); grab.colliders.Add(grip.GetComponent<Collider>());

        var socket = Socket("Magazine socket", root.transform, new Vector3(0, -0.085f, -0.02f), new Vector3(0.05f, 0.05f, 0.06f), LayerMag);
        socket.attachTransform = Marker(socket.transform, "Seat", new Vector3(0, 0.05f, 0));     // the magazine sits in the grip

        var pistol = root.AddComponent<Pistol>();
        pistol.muzzle = Marker(root.transform, "Muzzle", new Vector3(0, 0.03f, 0.14f));
        pistol.magSocket = socket;
        pistol.bulletPrefab = bullet;
        pistol.shotSound = AssetDatabase.LoadAssetAtPath<AudioClip>(Root + "/ThirdParty/Kenney/ImpactSounds/impactPunch_heavy_000.ogg");
        pistol.drySound = AssetDatabase.LoadAssetAtPath<AudioClip>(Root + "/ThirdParty/Kenney/InterfaceSounds/click_004.ogg");
        pistol.magInSound = AssetDatabase.LoadAssetAtPath<AudioClip>(Root + "/ThirdParty/Kenney/InterfaceSounds/switch_002.ogg");
        root.AddComponent<ReturnToRack>();
        root.AddComponent<HoldOnTrackingLoss>();
        Feedback(root, "knife");
        return root;
    }

    // 2.2 x 8 x 3.2 cm, a brass round showing at the top.
    static GameObject MakeMagazine()
    {
        var root = new GameObject("Magazine");
        var rb = root.AddComponent<Rigidbody>(); rb.mass = 0.12f;
        var col = root.AddComponent<BoxCollider>(); col.size = new Vector3(0.022f, 0.08f, 0.032f);
        Box("Body", root.transform, Vector3.zero, new Vector3(0.022f, 0.08f, 0.032f), Mat("Gun metal", GunMetal), false);
        Box("Base plate", root.transform, new Vector3(0, -0.042f, 0), new Vector3(0.026f, 0.006f, 0.036f), Mat("Gun grip", GunGrip), false);
        Box("Round", root.transform, new Vector3(0, 0.042f, 0.004f), new Vector3(0.008f, 0.005f, 0.018f), Mat("Brass", new Color(0.85f, 0.65f, 0.25f)), false);
        Grab(root, LayerDefault | LayerMag, dynamicAttach: true);
        root.AddComponent<Magazine>();
        root.AddComponent<DespawnWhenDropped>();
        root.AddComponent<HoldOnTrackingLoss>();
        Feedback(root, "extra");
        return root;
    }

    // A small bright round with a short tracer trail.
    static GameObject MakeBullet()
    {
        var root = new GameObject("Bullet");
        var tracer = Mat("Tracer", new Color(1f, 0.85f, 0.35f));
        var ball = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        Object.DestroyImmediate(ball.GetComponent<Collider>());
        ball.name = "Round"; ball.transform.SetParent(root.transform, false); ball.transform.localScale = Vector3.one * 0.012f;
        ball.GetComponent<Renderer>().sharedMaterial = tracer;
        ball.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        var trail = root.AddComponent<TrailRenderer>();
        trail.time = 0.07f; trail.startWidth = 0.008f; trail.endWidth = 0f; trail.minVertexDistance = 0.02f;
        trail.sharedMaterial = tracer; trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        root.AddComponent<Bullet>();
        return root;
    }

    const float BowlHalfHeight = 0.0325f;      // the Blender bowl is 6.5 cm tall; the prefab root is its middle

    // The serving bowl (Blender: Ø 15 cm, 6.5 cm tall, foot at y 0 of the model). One solid box is the body (the
    // grab and the sockets); the mouth trigger over it takes a cut fruit let go over it, and extras dropped in.
    static GameObject MakeBowl()
    {
        var root = new GameObject("Bowl");
        var rb = root.AddComponent<Rigidbody>(); rb.mass = 0.2f;
        var col = root.AddComponent<BoxCollider>(); col.size = new Vector3(0.13f, 2f * BowlHalfHeight, 0.13f);
        Grab(root, LayerDefault | LayerBowl, dynamicAttach: true);
        StreetBuilder.Prop("Bowl", root.transform, new Vector3(0f, -BowlHalfHeight, 0f), 0f).name = "Bowl look";
        var b = root.AddComponent<Bowl>();
        // A heap of slices just under the rim, a skewer standing in it, a chilli on top.
        b.filledVisual = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        b.filledVisual.name = "Slices";
        Object.DestroyImmediate(b.filledVisual.GetComponent<Collider>());
        b.filledVisual.transform.SetParent(root.transform, false);
        b.filledVisual.transform.localPosition = new Vector3(0f, 0.012f, 0f);
        b.filledVisual.transform.localScale = new Vector3(0.12f, 0.036f, 0.12f);
        b.filledVisual.GetComponent<Renderer>().sharedMaterial = Mat("Slices", new Color(0.98f, 0.8f, 0.55f));
        b.skewerVisual = Box("Skewer", root.transform, new Vector3(0.03f, 0.06f, 0f), new Vector3(0.004f, 0.12f, 0.004f), Mat("Skewer", new Color(0.85f, 0.7f, 0.45f)), false);
        b.chiliVisual = StreetBuilder.Prop("Chili", root.transform, new Vector3(-0.015f, 0.032f, 0.01f), 30f).gameObject;
        foreach (var v in new[] { b.filledVisual, b.skewerVisual, b.chiliVisual }) v.SetActive(false);
        // Mouth: a trigger from inside the bowl to a hand's width over its rim.
        var mouth = new GameObject("Mouth"); mouth.transform.SetParent(root.transform, false); mouth.transform.localPosition = new Vector3(0, 0.05f, 0);
        var mc = mouth.AddComponent<BoxCollider>(); mc.isTrigger = true; mc.size = new Vector3(0.16f, 0.09f, 0.16f);
        mouth.AddComponent<BowlMouth>().bowl = b;
        root.AddComponent<DespawnWhenDropped>();
        root.AddComponent<HoldOnTrackingLoss>();
        Feedback(root, "bowl");
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
        root.AddComponent<HoldOnTrackingLoss>();
        root.AddComponent<DespawnWhenDropped>();
        Feedback(root, "extra");
        return root;
    }

    // A fresh red Thai chilli (Blender), lying along +Z; a 9 mm grab radius along its length so a pinch finds it.
    static GameObject MakeChili()
    {
        var root = new GameObject("Chili");
        root.AddComponent<Rigidbody>().mass = 0.01f;
        var col = root.AddComponent<CapsuleCollider>(); col.radius = 0.009f; col.height = 0.12f; col.direction = 2;
        StreetBuilder.Prop("Chili", root.transform, Vector3.zero, 0f).name = "Chili look";
        Grab(root, LayerDefault, dynamicAttach: true);
        root.AddComponent<Extra>().kind = ExtraKind.Chili;
        root.AddComponent<HoldOnTrackingLoss>();
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
        // Physics steps at 50 Hz, the headset draws at 72-90: without it a dropped or thrown item judders as it falls.
        go.GetComponent<Rigidbody>().interpolation = RigidbodyInterpolation.Interpolate;
        return g;
    }

    // A short word lying flat on the counter, readable from the player's side. Auto-sized to fill the width (≥ 1.5 cm tall glyphs; the counter is ~0.6 m from the eyes).
    static void DeckLabel(string text, Transform parent, Vector3 pos, float width)
    {
        var go = new GameObject("Label " + text, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var t = go.AddComponent<TextMeshPro>();
        t.text = text; t.font = UiKit.Font; t.fontStyle = FontStyles.Bold;
        t.enableAutoSizing = true; t.fontSizeMin = 0.1f; t.fontSizeMax = 0.4f;
        t.color = new Color(0.2f, 0.12f, 0.06f);
        t.alignment = TextAlignmentOptions.Center;
        t.textWrappingMode = TextWrappingModes.NoWrap;
        var rt = (RectTransform)go.transform; rt.sizeDelta = new Vector2(width, 0.035f);
        rt.position = pos; rt.rotation = Quaternion.Euler(90f, 0f, 0f);    // flat, top of the text away from the player
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
        var wood = Mat("Supply crate", new Color(0.2f, 0.52f, 0.82f));     // every "take from here" container: bright blue plastic
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

    // Dwell to press (GameSettings.Dwell) on a 3D button: hovering it for DwellPress.Seconds does what a poke does.
    static void Dwell(GameObject go, UnityEngine.Events.UnityAction action)
    {
        var d = go.AddComponent<DwellPress>();
        UnityEditor.Events.UnityEventTools.AddPersistentListener(d.onDwell, action);
    }

    static void Feedback(GameObject go, string item)
    {
        var f = go.AddComponent<ItemFeedback>();     // every renderer and material, not just the first (Lab 5's InteractionFeedback)
        f.selectSound = EffectsSetup.GrabSounds(item).FirstOrDefault();
        f.releaseSound = EffectsSetup.ReleaseSounds(item).FirstOrDefault();
    }

    // Hover / snap / release visuals on what the socket sits on (assignment §7), with a click on snap.
    static void SocketFeedback(XRSocketInteractor socket, GameObject surface)
    {
        var f = socket.gameObject.AddComponent<SliceAndSell.SocketFeedback>();
        f.targets = new[] { surface.GetComponent<Renderer>() };
        f.snapSound = EffectsSetup.ReleaseSounds("bell").FirstOrDefault();
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
        if (UiKit.Font) t.font = UiKit.Font;
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
        orders.orderCard.text = "Mango\n3 cuts\n+ skewer, chili"; orders.chalkboard.text = "฿120 / ฿400\nCombo x4\n2:15";
        foreach (var c in Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None)) c.gameObject.SetActive(false);   // menus and tutorial card
        var stallRoot = Object.FindAnyObjectByType<StanceHeight>().transform;     // the preview items go under it, so the seated shot moves them too
        foreach (var d in Object.FindObjectsByType<SliceAndSell.Dispenser>(FindObjectsSortMode.None))
            Object.Instantiate(d.prefab, d.spawnPoint.position, d.spawnPoint.rotation, stallRoot);
        // (The patience ring builds its mesh in Awake, which doesn't run in edit mode, so it isn't shown here.)
        if (orders.customerPrefabs.Length > 0)
            Object.Instantiate(orders.customerPrefabs[0], orders.counterSpot.position, Quaternion.Euler(0, 180, 0), stallRoot);
        var board = Object.FindAnyObjectByType<CuttingBoard>();
        var mango = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>($"{Root}/Prefabs/Mango.prefab"),
                                       board.fruitSocket.attachTransform.position, Quaternion.identity, stallRoot).GetComponent<Fruit>();
        mango.stages[0].SetActive(false); mango.stages[1].SetActive(true);
        var cam = new GameObject("Preview cam").AddComponent<Camera>();
        cam.transform.position = new Vector3(0, 1.6f, -0.05f);
        cam.transform.LookAt(new Vector3(0, 1.15f, 0.75f));
        cam.fieldOfView = 80;
        ShaderUtil.allowAsyncCompilation = false;   // otherwise the first frame shows placeholder shaders
        // Batch-mode renders don't run the skinning update: skin the saved (Idle) pose at render time instead.
        foreach (var smr in Object.FindObjectsByType<SkinnedMeshRenderer>(FindObjectsSortMode.None)) smr.forceMatrixRecalculationPerRender = true;
        Shot(cam, "Logs/stall-preview.png");
        // The reference framing (look image 1): eye height behind the cart, looking down the soi.
        cam.transform.position = new Vector3(0, 1.65f, -0.25f); cam.transform.LookAt(new Vector3(0, 1.05f, 4f)); cam.fieldOfView = 75;
        Shot(cam, "Logs/stall-reference.png");
        // Seated: the stall drops by SeatedDrop (as StanceHeight does), eyes at about 1.2 m, looking down at the cart.
        stallRoot.position += Vector3.down * GameSettings.SeatedDrop;
        cam.transform.position = new Vector3(0, 1.2f, -0.05f); cam.transform.LookAt(new Vector3(0, 0.8f, 0.6f)); cam.fieldOfView = 80;
        Shot(cam, "Logs/stall-seated.png");
        stallRoot.position += Vector3.up * GameSettings.SeatedDrop;
        cam.transform.position = new Vector3(0, 1.6f, -0.05f); cam.transform.LookAt(new Vector3(0, 1.15f, 0.75f)); cam.fieldOfView = 80;

        // Second shot: the shop sign as the player first sees it (no customer, no fruit on the board).
        foreach (var c in Object.FindObjectsByType<Customer>(FindObjectsSortMode.None)) Object.DestroyImmediate(c.gameObject);
        Object.DestroyImmediate(mango.gameObject);
        var sign = Object.FindAnyObjectByType<MainMenu>(FindObjectsInactive.Include);
        foreach (var c in sign.GetComponentsInChildren<Canvas>(true)) c.gameObject.SetActive(true);
        sign.Show();
        Shot(cam, "Logs/stall-menu.png");

        // The street around the stall: from behind and above the player, and looking back from the counter.
        foreach (var c in sign.GetComponentsInChildren<Canvas>(true)) c.gameObject.SetActive(false);
        cam.fieldOfView = 70;
        cam.transform.position = new Vector3(-3.5f, 2.6f, -2.5f); cam.transform.LookAt(new Vector3(0.5f, 1.2f, 4f));
        Shot(cam, "Logs/stall-street.png");
        cam.transform.position = new Vector3(0, 1.6f, 0.6f); cam.transform.LookAt(new Vector3(0, 1.4f, -3f));
        Shot(cam, "Logs/stall-behind.png");
        cam.transform.position = new Vector3(0.4f, 1.6f, 2.8f); cam.transform.LookAt(new Vector3(0, 1.4f, 0.5f));
        Shot(cam, "Logs/stall-front.png");     // what a customer sees: awning, lights, lanterns
        Object.DestroyImmediate(cam.gameObject);
        Debug.Log("[StallBuilder] Previews written to Logs/stall-preview.png and Logs/stall-menu.png");
    }

    static void Shot(Camera cam, string file)
    {
        var rt = new RenderTexture(1600, 1000, 24);
        cam.targetTexture = rt;
        Canvas.ForceUpdateCanvases();
        cam.Render(); cam.Render();
        RenderTexture.active = rt;
        var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0); tex.Apply();
        File.WriteAllBytes(file, tex.EncodeToPNG());
        RenderTexture.active = null;
        cam.targetTexture = null;
    }
}
