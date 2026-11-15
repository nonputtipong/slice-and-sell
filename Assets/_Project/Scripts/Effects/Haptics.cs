using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace SliceAndSell
{
    public static class Haptics
    {
        // Buzz every controller holding this interactable. Hand interactors have no haptic device, so the call does nothing for them.
        public static void Pulse(IXRInteractable interactable, float amplitude, float seconds)
        {
            if (!(interactable is IXRSelectInteractable selectable)) return;
            var holders = selectable.interactorsSelecting;
            for (int i = 0; i < holders.Count; i++)
                if (holders[i] is XRBaseInputInteractor input) input.SendHapticImpulse(amplitude, seconds);
        }
    }
}
