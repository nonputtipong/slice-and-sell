using UnityEngine;

namespace SliceAndSell
{
    public enum ExtraKind { Skewer, Chili }

    // An order extra (skewer, chili-salt packet). Dropping it into a bowl adds it and uses it up,
    // so hands only need a grab and a release, no precise socket.
    public class Extra : MonoBehaviour
    {
        public ExtraKind kind;
    }
}
