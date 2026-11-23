using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace SliceAndSell
{
    // A bowl placed on the tray is handed to the customer. Bowls are endless, so the tray consumes it.
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
            var bowl = args.interactableObject.transform.GetComponent<Bowl>();
            if (bowl && orders.Deliver(bowl) != DeliveryResult.NoCustomer) Destroy(bowl.gameObject, 0.5f);
        }
    }
}
