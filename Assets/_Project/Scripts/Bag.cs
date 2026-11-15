using UnityEngine;

namespace SliceAndSell
{
    public class Bag : MonoBehaviour
    {
        public GameObject filledVisual;     // the slices showing in the bag (inactive in the prefab)

        public string Contents { get; private set; }
        public int Cuts { get; private set; }
        public bool IsFilled => Contents != null;

        public void Fill(Fruit fruit)
        {
            Contents = fruit.fruitName;
            Cuts = fruit.CutsDone;
            if (filledVisual) filledVisual.SetActive(true);
        }
    }
}
