using System;
using UnityEngine;

namespace Rubickanov.Characters
{
    /// <summary>How a hand closes on what it holds: round a bar or handle, or flat on a big round thing.</summary>
    public enum HandShape
    {
        Fist,
        OpenPalm,
    }

    /// <summary>
    /// The fingers of a <see cref="HandShape"/>, degrees from the avatar's rest: the finger block curled toward the
    /// palm, the thumb folded over it and spread off the fingers.
    /// </summary>
    [Serializable]
    public struct HandShapePose
    {
        [Range(-30f, 120f)] public float Curl;
        [Range(-30f, 120f)] public float ThumbFold;
        [Range(-45f, 90f)] public float ThumbSpread;

        public HandShapePose(float curl, float thumbFold, float thumbSpread)
        {
            Curl = curl;
            ThumbFold = thumbFold;
            ThumbSpread = thumbSpread;
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
