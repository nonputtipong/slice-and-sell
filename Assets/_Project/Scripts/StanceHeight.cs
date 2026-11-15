using UnityEngine;

namespace SliceAndSell
{
    // On the Stall root: moves the whole stall (counter, bins, board, customers' spot) down when seated.
    // The player never moves; the world comes to their arms.
    public class StanceHeight : MonoBehaviour
    {
        float standingY;

        void Awake() => standingY = transform.position.y;
        void OnEnable() { GameSettings.Changed += Apply; Apply(); }
        void OnDisable() => GameSettings.Changed -= Apply;

        void Apply()
        {
            var p = transform.position;
            transform.position = new Vector3(p.x, standingY - (GameSettings.Seated ? GameSettings.SeatedDrop : 0f), p.z);
        }
    }
}
