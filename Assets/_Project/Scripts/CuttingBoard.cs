using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace SliceAndSell
{
    // The board socket holds one fruit. When that fruit is fully cut and an empty bag sits on the
    // scoop spot, the slices go into the bag (whichever happens last triggers it).
    public class CuttingBoard : MonoBehaviour
    {
        public XRSocketInteractor fruitSocket, scoopSocket;

        public Fruit Fruit => fruitSocket.hasSelection ? fruitSocket.firstInteractableSelected.transform.GetComponent<Fruit>() : null;
        Bag ScoopBag => scoopSocket.hasSelection ? scoopSocket.firstInteractableSelected.transform.GetComponent<Bag>() : null;

        void Update()
        {
            var fruit = Fruit; var bag = ScoopBag;
            if (fruit && fruit.IsFullyCut && bag && !bag.IsFilled)
            {
                bag.Fill(fruit);
                Destroy(fruit.gameObject);
            }
        }
    }
}
