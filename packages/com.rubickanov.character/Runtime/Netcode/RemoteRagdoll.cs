using System.Collections.Generic;
using Rubickanov.Characters;
using UnityEngine;

namespace Rubickanov.Characters.Netcode
{
    /// <summary>
    /// Plays a downed character's ragdoll back on its replica, on the same clock as its RemoteCharacterMotion,
    /// so the bones lie where the capsule that follows them is shown.
    /// </summary>
    public sealed class RemoteRagdoll
    {
        private readonly CharacterRagdoll _ragdoll;
        // Sorted by time.
        private readonly List<RagdollSnapshot> _buffer = new();
        private readonly Quaternion[] _rotations = new Quaternion[RagdollSnapshot.BoneCount];

        public RemoteRagdoll(CharacterRagdoll ragdoll) => _ragdoll = ragdoll;

        public void Add(RagdollSnapshot snapshot)
        {
            int index = _buffer.Count;
            while (index > 0 && _buffer[index - 1].Time > snapshot.Time)
            {
                index--;
            }

            if (index > 0 && _buffer[index - 1].Time == snapshot.Time)
            {
                return;
            }

            _buffer.Insert(index, snapshot);
        }

        /// <summary>Shows the pose at <paramref name="playback"/>, the owner's time the replica is shown at.</summary>
        public void Update(double playback)
        {
            if (!_ragdoll.IsDown)
            {
                // Poses only come while down; the ones already passed belong to a fall that is over.
                int passed = 0;
                while (passed < _buffer.Count && _buffer[passed].Time <= playback)
                {
                    passed++;
                }

                _buffer.RemoveRange(0, passed);
                return;
            }

            // Drop poses older than the one playback is currently leaving.
            int drop = 0;
            while (drop + 1 < _buffer.Count && _buffer[drop + 1].Time <= playback)
            {
                drop++;
            }

            _buffer.RemoveRange(0, drop);
            if (_buffer.Count == 0)
            {
                return;
            }

            RagdollSnapshot from = _buffer[0];
            RagdollSnapshot to = _buffer.Count > 1 ? _buffer[1] : from;
            float t = to.Time > from.Time ? Mathf.Clamp01((float)((playback - from.Time) / (to.Time - from.Time))) : 0f;
            for (int i = 0; i < _rotations.Length; i++)
            {
                _rotations[i] = Quaternion.Slerp(from.GetRotation(i), to.GetRotation(i), t);
            }

            _ragdoll.ShowPose(Vector3.Lerp(from.Hips, to.Hips, t), _rotations);
        }
    }
}
