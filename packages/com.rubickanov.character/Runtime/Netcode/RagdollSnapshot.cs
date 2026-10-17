using System;
using Rubickanov.Rig;
using Unity.Netcode;
using UnityEngine;

namespace Rubickanov.Characters.Netcode
{
    /// <summary>
    /// A character's ragdoll on one simulation step, as Rubickanov.Characters.Ragdoll.Capture gives it: the hips'
    /// position and rotation in a frame's space and every other bone's rotation, each packed into four bytes. Frame 0 is
    /// the root's space on the owner's clock (a downed body); any other is the game's (<see cref="IRagdollNetFrame"/>).
    /// </summary>
    public struct RagdollSnapshot : INetworkSerializeByMemcpy
    {
        /// <summary>The bones RagdollBuilder builds; a ragdoll with another count is not replicated.</summary>
        public const int BoneCount = 12;

        /// <summary>
        /// Frame 0: the owner's Time.fixedTime of the step, the same as the CharacterSnapshot sent with it; any other:
        /// the time on the frame's clock.
        /// </summary>
        public double Time;
        /// <summary>The space the hips are in: 0 for the root's, any other the game's.</summary>
        public uint Frame;
        public Vector3 Hips;
        // Fixed fields rather than an array, so the struct copies as it is and allocates nothing.
        private uint _r0, _r1, _r2, _r3, _r4, _r5, _r6, _r7, _r8, _r9, _r10, _r11;

        public Quaternion GetRotation(int bone) => RagdollMath.UnpackRotation(this[bone]);

        public void SetRotation(int bone, Quaternion rotation) => this[bone] = RagdollMath.PackRotation(rotation);

        private uint this[int bone]
        {
            readonly get => bone switch
            {
                0 => _r0,
                1 => _r1,
                2 => _r2,
                3 => _r3,
                4 => _r4,
                5 => _r5,
                6 => _r6,
                7 => _r7,
                8 => _r8,
                9 => _r9,
                10 => _r10,
                11 => _r11,
                _ => throw new ArgumentOutOfRangeException(nameof(bone)),
            };
            set
            {
                switch (bone)
                {
                    case 0: _r0 = value; break;
                    case 1: _r1 = value; break;
                    case 2: _r2 = value; break;
                    case 3: _r3 = value; break;
                    case 4: _r4 = value; break;
                    case 5: _r5 = value; break;
                    case 6: _r6 = value; break;
                    case 7: _r7 = value; break;
                    case 8: _r8 = value; break;
                    case 9: _r9 = value; break;
                    case 10: _r10 = value; break;
                    case 11: _r11 = value; break;
                    default: throw new ArgumentOutOfRangeException(nameof(bone));
                }
            }
        }
    }
}
