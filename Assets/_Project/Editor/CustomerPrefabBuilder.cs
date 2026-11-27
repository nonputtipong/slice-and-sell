using System.Collections.Generic;
using System.IO;
using System.Linq;
using SliceAndSell;
using UnityEditor;
using UnityEngine;

// Builds the customer prefabs from the Polyfork GLBs (CC BY 4.0): root (Customer) → imported model
// (legacy Animation, Idle/Walk looping) + patience ring. Idempotent: re-running overwrites the prefabs in place.
// Batch mode: -executeMethod CustomerPrefabBuilder.Build
public static class CustomerPrefabBuilder
{
    const string ModelDir = "Assets/_Project/ThirdParty/Polyfork/";
    const string OutDir = "Assets/_Project/Prefabs/Customers";
    const string RingMatPath = "Assets/_Project/Materials/PatienceRing.mat";
    const float RingHeight = 1.95f;   // above the head (~1.70 m); the order bubble floats beside the head, not over it

    static readonly (string file, string name)[] Models =
    {
        ("salaryman-commuter-4170c1.glb", "Salaryman"),
        ("backpacker-f56207.glb", "Backpacker"),
        ("delivery-courier-327e30.glb", "Courier"),
        ("student-in-a-hoodie-7c8223.glb", "Student"),
    };
    static readonly string[] NeededClips = { "Idle", "Walk" };

    [MenuItem("Slice & Sell/Build Customer Prefabs")]
    public static GameObject[] Build()
    {
        Directory.CreateDirectory(OutDir);
        var ringMat = RingMaterial();
        var built = new List<GameObject>();
        foreach (var (file, name) in Models)
        {
            string path = ModelDir + file;
            if (!EnsureLegacyImport(path)) continue;
            var modelAsset = AssetDatabase.LoadAssetAtPath<GameObject>(path);

            var root = new GameObject(name);
            var customer = root.AddComponent<Customer>();
            var model = (GameObject)PrefabUtility.InstantiatePrefab(modelAsset, root.transform);
            customer.model = model.transform;

            // glTFast puts the Animation (all clips, first one as default) on the model root; make sure Idle/Walk are there.
            var anim = model.GetComponent<Animation>();
            if (!anim) anim = model.AddComponent<Animation>();
            var clips = AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().ToArray();
            foreach (var clipName in NeededClips)
            {
                var clip = clips.FirstOrDefault(c => c.name == clipName);
                if (!clip) { Debug.LogError($"[CustomerPrefabBuilder] {file}: clip '{clipName}' MISSING (has: {string.Join(", ", clips.Select(c => c.name))})"); continue; }
                if (!anim.GetClip(clipName)) anim.AddClip(clip, clipName);
                if (clipName == "Idle") anim.clip = clip;
            }
            anim.wrapMode = WrapMode.Loop;
            anim.playAutomatically = true;
            // Save the bones in Idle's first frame, arms relaxed at the sides, not glTF's T-pose bind pose: that is what
            // the editor, previews and the first frame before Idle plays show.
            if (anim.clip) anim.clip.SampleAnimation(model, 0f);
            StreetBuilder.CheapSkinning(model.transform);
            customer.anim = anim;

            customer.head = model.GetComponentsInChildren<Transform>().FirstOrDefault(t => t.name == "Head");
            if (!customer.head) Debug.LogWarning($"[CustomerPrefabBuilder] {file}: no 'Head' bone, nod/head-shake disabled");

            var ringGo = new GameObject("Patience ring", typeof(MeshFilter), typeof(MeshRenderer));
            ringGo.transform.SetParent(root.transform, false);
            ringGo.transform.localPosition = new Vector3(0, RingHeight, 0);
            var mr = ringGo.GetComponent<MeshRenderer>();
            mr.sharedMaterial = ringMat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            customer.ring = ringGo.AddComponent<PatienceRing>();

            // Log before DestroyImmediate: afterwards `customer` is gone and its head would read as missing.
            Debug.Log($"[CustomerPrefabBuilder] {name}.prefab from {file}: clips [{string.Join(", ", clips.Select(c => $"{c.name}{(c.legacy ? "" : " NOT legacy")} {c.length:0.##}s"))}], head {(customer.head ? "ok" : "missing")}");
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, $"{OutDir}/{name}.prefab");
            Object.DestroyImmediate(root);
            built.Add(prefab);
        }
        AssetDatabase.SaveAssets();
        Debug.Log($"[CustomerPrefabBuilder] Built {built.Count}/{Models.Length} prefabs in {OutDir}");
        return built.ToArray();
    }

    // glTFast's design-time default is Mecanim; legacy Animation needs no controller asset and plays by name.
    // Decided by the imported clips, not the serialized setting: a fresh importer can show Legacy in its
    // serialized defaults yet import as Mecanim (GltfImporter swaps a null importSettings for Mecanim).
    static bool EnsureLegacyImport(string path)
    {
        var importer = AssetImporter.GetAtPath(path);
        if (!importer) { Debug.LogError($"[CustomerPrefabBuilder] {path}: no importer (file missing, or not imported yet)"); return false; }

        bool IsLegacy()
        {
            var main = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            return main && main.GetComponent<Animation>() && AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().Any(c => c.legacy);
        }
        if (IsLegacy()) return true;

        var so = new SerializedObject(importer);
        var method = so.FindProperty("importSettings.animationMethod");
        var names = so.FindProperty("importSettings.nodeNameMethod");
        if (method == null || names == null) { Debug.LogError($"[CustomerPrefabBuilder] {path}: importer is {importer.GetType().Name}, expected glTFast's GltfImporter"); return false; }
        method.intValue = (int)GLTFast.AnimationMethod.Legacy;
        names.intValue = (int)GLTFast.NameImportMethod.OriginalUnique;   // glTFast's design-time default; some GLBs repeat node names
        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(importer);   // write the settings to the .meta even if they matched the serialized defaults
        importer.SaveAndReimport();

        bool ok = IsLegacy();
        if (ok) Debug.Log($"[CustomerPrefabBuilder] {path}: reimported with legacy animation");
        else Debug.LogError($"[CustomerPrefabBuilder] {path}: still no legacy clips / Animation after reimport");
        return ok;
    }

    static Material RingMaterial()
    {
        var m = AssetDatabase.LoadAssetAtPath<Material>(RingMatPath);
        if (!m) { m = new Material(Shader.Find("Universal Render Pipeline/Unlit")); AssetDatabase.CreateAsset(m, RingMatPath); }
        m.SetColor("_BaseColor", Color.white);   // PatienceRing sets the live colour per renderer
        EditorUtility.SetDirty(m);
        return m;
    }
}
