using UnityEngine;

namespace Rubickanov.Audio
{
    /// <summary>
    /// How much this object's colliders (its own and its children's) block sound on a kind's occlusion mask: 0 lets it
    /// through (a table, a screen's cloth), 1 as solid rock. A collider without one blocks fully.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SoundOccluder : MonoBehaviour
    {
        [Range(0f, 1f), Tooltip("0 sound passes, 1 blocked as by rock.")]
        public float Strength = 0.2f;

        /// <summary>What a hit collider blocks: its nearest <see cref="SoundOccluder"/>'s strength, else 1.</summary>
        public static float Of(Collider collider)
        {
            var occluder = collider.GetComponentInParent<SoundOccluder>();
            return occluder != null && occluder.isActiveAndEnabled ? occluder.Strength : 1f;
        }
    }
}
