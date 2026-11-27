using UnityEngine;

namespace SliceAndSell
{
    // One of the two trigger hitboxes of a customer or thief (made by Customer at start): head or body.
    public class Hitbox : MonoBehaviour
    {
        Customer owner; bool isHead;
        public void Init(Customer c, bool head) { owner = c; isHead = head; }
        public bool IsHead => isHead;
        void OnTriggerEnter(Collider other) { if (owner) owner.OnHitbox(other, isHead); }
    }
}
