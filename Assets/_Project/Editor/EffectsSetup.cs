using System.IO;
using System.Linq;
using SliceAndSell;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

// Editor helpers for StallBuilder: the StallEffects object (juice particles + clips) and per-item grab/drop sounds.
// Sounds are Kenney CC0 packs in ThirdParty, picked by file name (not auditioned yet; swap names here after listening).
public static class EffectsSetup
{
    const string Impact = "Assets/_Project/ThirdParty/Kenney/ImpactSounds/";
    const string Ui = "Assets/_Project/ThirdParty/Kenney/InterfaceSounds/";
    const string JuiceMatPath = "Assets/_Project/Materials/Effects/Juice.mat";

    public static StallEffects Create(Transform parent)
    {
        var go = new GameObject("Stall effects");
        go.transform.SetParent(parent, false);

        // Juice: tiny drops thrown up a few cm and falling back on the board (GDD: below eye level, no post-processing).
        var ps = go.AddComponent<ParticleSystem>();
        var main = ps.main;
        main.playOnAwake = false;
        main.loop = false;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.3f, 0.5f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.5f, 1.1f);   // v^2/2g: peaks ~6 cm above the cut
        main.startSize = new ParticleSystem.MinMaxCurve(0.006f, 0.012f);
        main.gravityModifier = 1f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 200;
        var emission = ps.emission; emission.enabled = false;           // bursts come from StallEffects.Emit only
        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 35f; shape.radius = 0.01f;
        shape.rotation = new Vector3(-90f, 0f, 0f);                      // cone points along +Z; turn it to world up
        var size = ps.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0.3f));
        var r = go.GetComponent<ParticleSystemRenderer>();
        r.sharedMaterial = JuiceMaterial();
        r.renderMode = ParticleSystemRenderMode.Billboard;
        r.shadowCastingMode = ShadowCastingMode.Off;
        r.receiveShadows = false;

        var fx = go.AddComponent<StallEffects>();
        fx.juice = ps;
        fx.crunch = Clips(Impact, "impactPunch_medium", 5);
        fx.powerLayer = Clips(Impact, "impactPunch_heavy", 5);
        fx.cleanLayer = Clips(Impact, "impactGlass_light", 5);
        fx.missed = Clips(Impact, "impactSoft_medium", 5);
        fx.coin = Clips(Impact, "impactMetal_light", 5);
        fx.error = Load(Ui, "error_001", "error_002", "error_003", "error_004");
        fx.extra = Load(Ui, "pluck_001", "pluck_002");
        fx.shiftWon = Load(Ui, "confirmation_001", "confirmation_002", "confirmation_003", "confirmation_004");
        fx.shiftLost = Load(Ui, "bong_001");
        return fx;
    }

    // item: "fruit", "knife", "bag", "bell", "extra" (GDD > Feedback states: grab sound per material).
    public static AudioClip[] GrabSounds(string item) => item switch
    {
        "fruit" => Clips(Impact, "impactSoft_medium", 5),
        "knife" => Clips(Impact, "impactMetal_light", 5),
        "bag" => Clips(Impact, "impactGeneric_light", 5),
        "bell" => Clips(Impact, "impactBell_heavy", 5),        // select on the bell = it rings
        "extra" => Clips(Impact, "impactWood_light", 5),
        _ => throw new System.ArgumentException("Unknown item: " + item),
    };

    public static AudioClip[] ReleaseSounds(string item) => item switch
    {
        "fruit" => Clips(Impact, "impactSoft_heavy", 5),
        "knife" => Clips(Impact, "impactMetal_medium", 5),
        "bag" => Clips(Impact, "footstep_carpet", 5),          // soft papery scuff
        "bell" => Load(Ui, "click_001", "click_002", "click_003"),   // button coming back up
        "extra" => Clips(Impact, "impactWood_medium", 5),
        _ => throw new System.ArgumentException("Unknown item: " + item),
    };

    // Kenney impact sets are numbered _000 to _004.
    static AudioClip[] Clips(string folder, string prefix, int count) =>
        Load(folder, Enumerable.Range(0, count).Select(i => $"{prefix}_{i:000}").ToArray());

    static AudioClip[] Load(string folder, params string[] names)
    {
        var clips = names.Select(n => AssetDatabase.LoadAssetAtPath<AudioClip>(folder + n + ".ogg")).Where(c => c).ToArray();
        if (clips.Length < names.Length) Debug.LogWarning($"[EffectsSetup] {names.Length - clips.Length} missing clip(s) in {folder} ({names[0]}...). Imported yet?");
        return clips;
    }

    static Material JuiceMaterial()
    {
        var m = AssetDatabase.LoadAssetAtPath<Material>(JuiceMatPath);
        if (!m)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(JuiceMatPath));
            m = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit"));
            AssetDatabase.CreateAsset(m, JuiceMatPath);
        }
        // Alpha-blended round drops; _ColorMode 0 multiplies by vertex colour, so EmitParams.startColor shows.
        m.SetTexture("_BaseMap", AssetDatabase.GetBuiltinExtraResource<Texture2D>("Default-Particle.psd"));
        m.SetColor("_BaseColor", Color.white);
        m.SetFloat("_ColorMode", 0f);
        m.SetFloat("_Surface", 1f); m.SetFloat("_Blend", 0f);
        m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha); m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
        m.SetFloat("_SrcBlendAlpha", (float)BlendMode.One); m.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
        m.SetFloat("_ZWrite", 0f);
        m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        m.SetOverrideTag("RenderType", "Transparent");
        m.renderQueue = (int)RenderQueue.Transparent;
        m.enableInstancing = true;
        EditorUtility.SetDirty(m);
        return m;
    }
}
