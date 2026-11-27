using UnityEngine;

namespace SliceAndSell
{
    // A pistol magazine (endless, from the ammo tray). Pushed into the bottom of the pistol's grip it clicks into the
    // pistol's socket; each shot uses a round, and an empty one drops out (Pistol) and is gone a moment later.
    public class Magazine : MonoBehaviour
    {
        public const int Capacity = 6;
        public int rounds = Capacity;
        public bool IsEmpty => rounds <= 0;
    }
}
