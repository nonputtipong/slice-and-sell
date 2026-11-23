using UnityEngine;
using UnityEngine.UI;

namespace HCD545
{
    // Lab 5: what the poke panel buttons do. Press them with your index finger (hands) or touch them with the
    // controller tip; the ray works too. Buttons are at least 4 x 4 cm, so a fingertip can hit them.
    public class PokePanelActions : MonoBehaviour
    {
        public Button lightButton, colourButton, resetButton;
        public Slider brightness;           // Lamp brightness, 0 to 1
        public Light lamp;                  // found by name ("Lamp") if empty
        public Renderer[] recolour;         // objects that change color with the Colour button

        static readonly Color[] colours = { Color.white, new Color(1f, 0.34f, 0.17f), new Color(0.16f, 0.71f, 0.96f), new Color(0.96f, 0.77f, 0.27f) };
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        int colourIndex;
        float maxIntensity = 3f;

        void Start()
        {
            if (!lamp) { var g = GameObject.Find("Lamp"); if (g) lamp = g.GetComponentInChildren<Light>(); }
            if (lamp) maxIntensity = Mathf.Max(lamp.intensity, 0.01f);
            if (lightButton) lightButton.onClick.AddListener(() => { if (lamp) lamp.enabled = !lamp.enabled; });
            if (colourButton) colourButton.onClick.AddListener(NextColour);
            if (resetButton) resetButton.onClick.AddListener(RespawnOnDrop.ResetAll);
            if (brightness)
            {
                brightness.value = 1f;
                brightness.onValueChanged.AddListener(v => { if (lamp) lamp.intensity = v * maxIntensity; });
            }
        }

        void NextColour()
        {
            colourIndex = (colourIndex + 1) % colours.Length;
            var block = new MaterialPropertyBlock();
            foreach (var r in recolour)
            {
                if (!r) continue;
                r.GetPropertyBlock(block);
                block.SetColor(BaseColorId, colours[colourIndex]);
                r.SetPropertyBlock(block);
            }
        }
    }
}
