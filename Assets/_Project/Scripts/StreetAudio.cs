using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace SliceAndSell
{
    // The night market around the stall (GDD > Spatial audio): a 2D crowd bed, a radio on the shelf behind the
    // player (poke or ray it to switch it off and on), motorbikes passing along the street, and customer voices.
    // Board chops and cash-box coins come from StallEffects at their own positions.
    public class StreetAudio : MonoBehaviour
    {
        public AudioSource ambience;            // 2D loop
        public AudioSource radio;               // 3D loop on the shelf
        public XRSimpleInteractable radioSwitch;
        public AudioSource motorbike;           // 3D, moved along the street for each pass
        public AudioClip[] voices;              // short wordless blips
        public Transform customerSpot;          // the customer's feet at the counter
        public Vector3 streetFrom = new Vector3(-12f, 0.8f, 4f), streetTo = new Vector3(12f, 0.8f, 4f);
        public Vector2 passEverySeconds = new Vector2(20f, 40f);
        public float passSpeed = 8f;            // m/s

        float nextPass, passStart;
        bool passing;
        AudioSource voice;

        void Awake()
        {
            // Its own object: it moves to the customer's head, the rest of the street audio stays put.
            voice = new GameObject("Customer voice").AddComponent<AudioSource>();
            voice.transform.SetParent(transform, false);
            voice.spatialBlend = 1f; voice.minDistance = 0.5f; voice.maxDistance = 10f; voice.playOnAwake = false;
        }

        void OnEnable()
        {
            nextPass = Time.time + Random.Range(5f, 10f);
            if (radioSwitch) radioSwitch.selectEntered.AddListener(ToggleRadio);
            GameEvents.Paid += OnPaid;
            GameEvents.WalkedOut += OnUnhappy;
            GameEvents.Refused += OnUnhappy;
        }

        void OnDisable()
        {
            if (radioSwitch) radioSwitch.selectEntered.RemoveListener(ToggleRadio);
            GameEvents.Paid -= OnPaid;
            GameEvents.WalkedOut -= OnUnhappy;
            GameEvents.Refused -= OnUnhappy;
        }

        void ToggleRadio(SelectEnterEventArgs _) { if (radio.isPlaying) radio.Pause(); else radio.UnPause(); }

        void Update()
        {
            if (!motorbike) return;
            if (!passing && Time.time >= nextPass)
            {
                passing = true; passStart = Time.time;
                if (Random.value < 0.5f) (streetFrom, streetTo) = (streetTo, streetFrom);    // either direction
                motorbike.Play();
            }
            if (!passing) return;
            float t = (Time.time - passStart) * passSpeed / Vector3.Distance(streetFrom, streetTo);
            motorbike.transform.position = Vector3.Lerp(streetFrom, streetTo, t);
            if (t >= 1f) { passing = false; motorbike.Stop(); nextPass = Time.time + Random.Range(passEverySeconds.x, passEverySeconds.y); }
        }

        // A customer speaks from where they stand (about head height above the counter spot).
        public void Speak(Vector3 head, float pitch = 1f)
        {
            if (voices == null || voices.Length == 0) return;
            voice.transform.position = head;
            voice.pitch = pitch;
            voice.PlayOneShot(voices[Random.Range(0, voices.Length)]);
        }

        Vector3 Head => customerSpot ? customerSpot.position + Vector3.up * 1.6f : transform.position;
        void OnPaid(Vector3 at, int amount, int tip, bool correct) => Speak(Head, correct ? 1.15f : 0.85f);
        void OnUnhappy(Vector3 at) => Speak(Head, 0.8f);
    }
}
