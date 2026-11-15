using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace SliceAndSell
{
    // A bag placed on the tray is handed to the customer. Bags are endless, so the tray consumes it.
    [RequireComponent(typeof(XRSocketInteractor))]
    public class HandoffTray : MonoBehaviour
    {
        public OrderManager orders;
        XRSocketInteractor socket;

        void Awake() => socket = GetComponent<XRSocketInteractor>();
        void OnEnable() => socket.selectEntered.AddListener(OnPlaced);
        void OnDisable() => socket.selectEntered.RemoveListener(OnPlaced);

        void OnPlaced(SelectEnterEventArgs args)
        {
            var bag = args.interactableObject.transform.GetComponent<Bag>();
            if (bag && orders.Deliver(bag) != DeliveryResult.NoCustomer) Destroy(bag.gameObject, 0.5f);
        }
    }
}
