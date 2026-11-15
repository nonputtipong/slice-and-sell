using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace SliceAndSell
{
    // Poke (hands, controller tip) or ray + trigger: calls the next customer.
    [RequireComponent(typeof(XRSimpleInteractable))]
    public class ServiceBell : MonoBehaviour
    {
        public OrderManager orders;
        XRSimpleInteractable interactable;

        void Awake() => interactable = GetComponent<XRSimpleInteractable>();
        void OnEnable() => interactable.selectEntered.AddListener(Ring);
        void OnDisable() => interactable.selectEntered.RemoveListener(Ring);

        void Ring(SelectEnterEventArgs args) => orders.NextCustomer();
    }
}
