using System;
using UnityEngine;

namespace Rubickanov.Characters
{
    /// <summary>
    /// How a hand closes on what it holds: round a bar or handle, flat on a big round thing, or round a gun's grip
    /// with the index finger out on the trigger.
    /// </summary>
    public enum HandShape
    {
        Fist,
        OpenPalm,
        Trigger,
    }

    /// <summary>
    /// The fingers of a <see cref="HandShape"/>, degrees from the avatar's rest. Each finger bends at its knuckle
    /// (<see cref="Curl"/>), its middle joint (<see cref="Bend"/>) and its last joint (<see cref="Tip"/>); the index
    /// finger only by 1 - <see cref="IndexOpen"/> of that. A hand with one bone for its fingers (a mitten) bends that
    /// bone by <see cref="Curl"/>. The thumb's root folds it (under the palm, a mitten's over it) and spreads it off
    /// the fingers; its other two joints curl it the way it folds.
    /// </summary>
    [Serializable]
    public struct HandShapePose
    {
        [Range(-30f, 120f)] public float Curl;
        [Range(-30f, 120f)] public float Bend;
        [Range(-30f, 120f)] public float Tip;
        [Range(0f, 1f)] public float IndexOpen;
        [Range(-30f, 120f)] public float ThumbFold;
        [Range(-45f, 90f)] public float ThumbSpread;
        [Range(-30f, 120f)] public float ThumbCurl;

        public HandShapePose(float curl, float thumbFold, float thumbSpread)
            : this(curl, 0f, 0f, 0f, thumbFold, thumbSpread, 0f)
        {
        }

        public HandShapePose(float curl, float bend, float tip, float indexOpen, float thumbFold, float thumbSpread,
            float thumbCurl)
        {
            Curl = curl;
            Bend = bend;
            Tip = tip;
            IndexOpen = indexOpen;
            ThumbFold = thumbFold;
            ThumbSpread = thumbSpread;
            ThumbCurl = thumbCurl;
        }
    }

    /// <summary>
    /// A hand's hold on an item, in the item's space, metres: the palm's middle at <see cref="Position"/>, the palm
    /// facing the rotation's forward and the fingers along its up.
    /// </summary>
    [Serializable]
    public struct HandGrip
    {
        public Vector3 Position;
        public Vector3 Euler;
        public HandShape Shape;

        public HandGrip(Vector3 position, Vector3 euler, HandShape shape)
        {
            Position = position;
            Euler = euler;
            Shape = shape;
        }

        /// <summary>False while both are zero: never set, so a builder may fill it.</summary>
        public bool IsSet => Position != Vector3.zero || Euler != Vector3.zero;

        public Quaternion Rotation => Quaternion.Euler(Euler);
    }
}
