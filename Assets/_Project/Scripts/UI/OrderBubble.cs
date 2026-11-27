using TMPro;
using UnityEngine;

namespace SliceAndSell
{
    // Playtest 5: the order is a speech bubble at the customer instead of a card on the stall. It floats beside the
    // head of the customer at the counter (on the player's side, a little above, so it never covers the face) and turns
    // to face the player; with nobody at the counter it rests over the counter spot (shift messages). Hidden when
    // OrderManager has nothing to say. OrderManager still writes orderCard.text, which is this bubble's text.
    public class OrderBubble : MonoBehaviour
    {
        public OrderManager orders;
        public TMP_Text text;
        public GameObject look;                 // the panel, tail and text (shown only when there is text)
        public float above = 0.12f, beside = 0.32f, follow = 8f;
        public float restHeight = 1.75f;        // over the counter spot when nobody is there

        Transform cam;

        void LateUpdate()
        {
            bool show = text && !string.IsNullOrEmpty(text.text);
            if (look && look.activeSelf != show) look.SetActive(show);
            if (!show) return;
            if (!cam) { var c = Camera.main; if (c) cam = c.transform; }

            var target = Anchor();
            float k = 1f - Mathf.Exp(-follow * Time.unscaledDeltaTime);
            transform.position = Vector3.Distance(transform.position, target) > 1.5f ? target : Vector3.Lerp(transform.position, target, k);
            if (cam)
            {
                var d = transform.position - cam.position; d.y = 0f;
                if (d.sqrMagnitude > 1e-4f) transform.rotation = Quaternion.LookRotation(d);
            }
        }

        // Beside the customer's head, towards the player's right; or over the counter spot.
        public Vector3 Anchor()
        {
            var who = orders ? orders.Present : null;
            if (who && !who.Dead)
            {
                var head = who.head ? who.head.position : who.transform.position + Vector3.up * 1.6f;
                var right = cam ? Vector3.ProjectOnPlane(cam.right, Vector3.up).normalized : Vector3.right;
                return head + Vector3.up * above + right * beside;
            }
            var spot = orders && orders.counterSpot ? orders.counterSpot.position : transform.position;
            return new Vector3(spot.x, restHeight, spot.z);
        }
    }
}
