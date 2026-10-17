using System.Collections.Generic;
using Rubickanov.Characters;
using UnityEngine;

namespace Rubickanov.Characters.Netcode
{
    /// <summary>
    /// Plays a character's ragdoll back on its replica. In frame 0 (a downed body) on the same clock as its
    /// RemoteCharacterMotion, so the bones lie where the capsule that follows them is shown; in a game's frame
    /// (<see cref="IRagdollNetFrame"/>) on that frame's clock and in this machine's copy of it, upright as well as down.
    /// </summary>
    public sealed class RemoteRagdoll
    {
        /// <summary>An upright replica goes back to its animation once playback is this far past the newest pose, s.</summary>
        public const double StreamTimeout = 0.25;

        private readonly CharacterRagdoll _ragdoll;
        private readonly IRagdollNetFrame _frames;
        // Sorted by time, all on one clock: frame 0's or the game frames'.
        private readonly List<RagdollSnapshot> _buffer = new();
        private readonly Quaternion[] _rotations = new Quaternion[RagdollSnapshot.BoneCount];
        private bool _wasDown;
        // Game-frame poses up to this time belong to a fall that is over.
        private double _over = double.NegativeInfinity;

        public RemoteRagdoll(CharacterRagdoll ragdoll, IRagdollNetFrame frames = null)
        {
            _ragdoll = ragdoll;
            _frames = frames;
        }

        public void Add(RagdollSnapshot snapshot)
        {
            // A pose on the other clock starts the buffer anew: the two cannot be ordered.
            if (_buffer.Count > 0 && (_buffer[^1].Frame == 0) != (snapshot.Frame == 0))
            {
                _buffer.Clear();
            }

            if (snapshot.Frame != 0 && snapshot.Time <= _over)
            {
                return;
            }

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

        /// <summary>
        /// Shows the pose at <paramref name="ownerPlayback"/>, the owner's time the replica is shown at, or for poses in a
        /// game's frame at the time that frame is shown at.
        /// </summary>
        public void Update(double ownerPlayback)
        {
            bool down = _ragdoll.IsDown;
            if (_wasDown && !down)
            {
                // Got up: game-frame poses still buffered from the fall are not a stream to show upright.
                if (_buffer.Count > 0 && _buffer[^1].Frame != 0)
                {
                    _over = _buffer[^1].Time;
                }

                _buffer.Clear();
            }

            _wasDown = down;
            if (_buffer.Count == 0)
            {
                EndStream();
                return;
            }

            RagdollSnapshot newest = _buffer[^1];
            double playback = ownerPlayback;
            bool framed = newest.Frame != 0;
            if (framed && (_frames == null || !_frames.Resolve(newest.Frame, out _, out playback)))
            {
                // Its frame is not on this machine: nothing to show it in.
                EndStream();
                return;
            }

            if (!down && !framed)
            {
                // Poses in frame 0 only come while down; the ones already passed belong to a fall that is over.
                int passed = 0;
                while (passed < _buffer.Count && _buffer[passed].Time <= playback)
                {
                    passed++;
                }

                _buffer.RemoveRange(0, passed);
                return;
            }

            if (!down && playback - newest.Time > StreamTimeout)
            {
                _buffer.Clear();
                EndStream();
                return;
            }

            // Drop poses older than the one playback is currently leaving.
            int drop = 0;
            while (drop + 1 < _buffer.Count && _buffer[drop + 1].Time <= playback)
            {
                drop++;
            }

            _buffer.RemoveRange(0, drop);
            RagdollSnapshot from = _buffer[0];
            RagdollSnapshot to = _buffer.Count > 1 ? _buffer[1] : from;
            float t = to.Time > from.Time ? Mathf.Clamp01((float)((playback - from.Time) / (to.Time - from.Time))) : 0f;
            if (from.Frame != to.Frame)
            {
                // Two frames (the ride, then the world): no blend between their spaces, the nearer pose is shown.
                from = to = t < 0.5f ? from : to;
                t = 0f;
            }

            for (int i = 0; i < _rotations.Length; i++)
            {
                _rotations[i] = Quaternion.Slerp(from.GetRotation(i), to.GetRotation(i), t);
            }

            Vector3 hips = Vector3.Lerp(from.Hips, to.Hips, t);
            if (from.Frame == 0)
            {
                _ragdoll.ShowPose(hips, _rotations);
                return;
            }

            if (!_frames.Resolve(from.Frame, out ICharacterSpace space, out _))
            {
                return;
            }

            Vector3 position = space != null ? space.ToWorld(Vector3.zero) : Vector3.zero;
            Quaternion rotation = space != null ? space.ToWorld(Quaternion.identity) : Quaternion.identity;
            _ragdoll.ShowStreamed(position, rotation, hips, _rotations);
        }

        private void EndStream()
        {
            if (_ragdoll.ShowsStreamed)
            {
                _ragdoll.EndStreamed();
            }
        }
    }
}
