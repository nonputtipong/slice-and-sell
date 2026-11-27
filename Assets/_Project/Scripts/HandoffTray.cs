using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace SliceAndSell
{
    // A bowl placed on the tray is handed to the customer, and the tray uses it up (the bowl spot then makes a fresh
    // one). A bowl put down while nobody is waiting, or while paused, stays and is handed over as soon as someone can
    // take it: with one bowl in play it must never get stuck here.
    [RequireComponent(typeof(XRSocketInteractor))]
    public class HandoffTray : MonoBehaviour
    {
        public OrderManager orders;
        XRSocketInteractor socket;
        Bowl handed;                        // already served, gone in 0.5 s: never charged twice

        void Awake() => socket = GetComponent<XRSocketInteractor>();

        void Update()
        {
            if (!socket.hasSelection) return;
            var bowl = socket.firstInteractableSelected.transform.GetComponent<Bowl>();
            if (!bowl || bowl == handed) return;
            if (orders.Deliver(bowl) == DeliveryResult.NoCustomer) return;     // try again next frame
            handed = bowl;
            Destroy(bowl.gameObject, 0.5f);
        }
    }
}
