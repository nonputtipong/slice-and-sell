using UnityEngine;

namespace SliceAndSell
{
    public class Bag : MonoBehaviour
    {
        public GameObject filledVisual;             // the slices showing in the bag (inactive in the prefab)
        public GameObject skewerVisual, chiliVisual;

        public string Contents { get; private set; }
        public int Cuts { get; private set; }
        public bool IsFilled => Contents != null;
        public bool HasSkewer { get; private set; }
        public bool HasChili { get; private set; }

        public void Fill(Fruit fruit)
        {
            Contents = fruit.fruitName;
            Cuts = fruit.CutsDone;
            if (filledVisual) filledVisual.SetActive(true);
        }

        // Returns false if the bag already has one (the extra is then left alone).
        public bool AddExtra(ExtraKind kind)
        {
            if (kind == ExtraKind.Skewer ? HasSkewer : HasChili) return false;
            if (kind == ExtraKind.Skewer) { HasSkewer = true; if (skewerVisual) skewerVisual.SetActive(true); }
            else { HasChili = true; if (chiliVisual) chiliVisual.SetActive(true); }
            GameEvents.RaiseExtraAdded(transform.position, kind.ToString());
            return true;
        }
    }
}
