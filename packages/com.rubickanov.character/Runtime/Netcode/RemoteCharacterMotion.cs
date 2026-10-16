using System;
using System.Collections.Generic;
using UnityEngine;

namespace Rubickanov.Characters.Netcode
{
    /// <summary>
    /// Plays a character's snapshots back a little behind real time, interpolating between them.
    /// Events fire when playback reaches the step they happened on, so they stay in sync with the pose.
    /// A pose sent in a frame part's space (e.g. a vehicle's deck) is played back in that space and put on this
    /// machine's copy of it; the game's parts play when playback reaches their snapshot.
    /// </summary>
    public sealed class RemoteCharacterMotion : ICharacterMotionSource
    {
        // Playback runs this much faster or slower at most to drift back to the target delay.
        private const float MaxClockCorrection = 0.1f;
        // Further off than this, playback jumps to the target instead, e.g. after a hitch.
        private const double MaxClockError = 0.5;

        public CharacterMotion Motion { get; private set; }

        public Vector3 Aim { get; private set; }
        public event Action Jumped;
        public event Action Landed;
        public event Action<CharacterMotion> MotionUpdated;

        /// <summary>The owner knocked the character down: its ragdoll is the body from here.</summary>
        public event Action FellDown;

        /// <summary>The owner started getting up: lying face up or down, the nearest-frame clip and its start. The
        /// body is already where the owner stood it.</summary>
        public event Action<CharacterGetUp> GettingUp;

        /// <summary>The character sat in a seat at the step playback has reached.</summary>
        public bool Seated => _seated;

        /// <summary>The owner's time the character is shown at.</summary>
        public double Playback => _playback;

        /// <summary>The space the shown pose is in (a frame part's, e.g. a deck), or null for the world.</summary>
        public ICharacterSpace Space { get; private set; }

        /// <summary>The shown pose in <see cref="Space"/> (the world's without one).</summary>
        public Vector3 LocalPosition { get; private set; }

        public Quaternion LocalRotation { get; private set; } = Quaternion.identity;

        /// <summary>On the ground at the step playback has reached.</summary>
        public bool Grounded => _grounded;

        private readonly Transform _body;
        private readonly float _delay;
        private readonly float _teleportDistance;
        private readonly ICharacterNetFrame _frame;
        private readonly IReadOnlyList<ICharacterNetPart> _parts;
        // Sorted by time. The first entry is at or before playback once it has started.
        private readonly List<CharacterNetSnapshot> _buffer = new();
        private double _playback;
        private bool _started;
        private int _applied = -1;
        private bool _grounded;
        private bool _swimming;
        private bool _seated;
        private byte _jumps;
        private byte _falls;
        private byte _getUps;

        /// <param name="frame">The part whose bytes name the space a pose was sent in, or null: always the world.</param>
        /// <param name="parts">The game's parts to play, the frame among them or not.</param>
        public RemoteCharacterMotion(Transform body, float delay, float teleportDistance, ICharacterNetFrame frame = null,
            IReadOnlyList<ICharacterNetPart> parts = null)
        {
            _body = body;
            _delay = delay;
            _teleportDistance = teleportDistance;
            _frame = frame;
            _parts = parts;
        }

        // This machine's copy of the space a snapshot's pose is in, resolved when shown, or null for the world.
        private ICharacterSpace SpaceOf(in CharacterNetSnapshot snapshot) =>
            _frame != null && snapshot.TryGetPart(_frame.PartId, out CharacterNetPartReader reader) ? _frame.Space(reader) : null;

        private static Vector3 World(ICharacterSpace space, Vector3 position) => space != null ? space.ToWorld(position) : position;

        private static Quaternion World(ICharacterSpace space, Quaternion rotation) =>
            space != null ? space.ToWorld(rotation) : rotation;

        public void Add(in CharacterNetSnapshot snapshot)
        {
            // Unreliable delivery can reorder; anything playback has passed is useless.
            if (_started && snapshot.Time <= _playback && _buffer.Count > 0)
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

        public void Update(float deltaTime)
        {
            if (_buffer.Count == 0)
            {
                return;
            }

            AdvanceClock(deltaTime);

            // Drop snapshots older than the one playback is currently leaving.
            int drop = 0;
            while (drop + 1 < _buffer.Count && _buffer[drop + 1].Time <= _playback)
            {
                drop++;
            }

            bool jumped = false, landed = false, fell = false;
            CharacterNetSnapshot? gotUp = null;
            if (_buffer[drop].Time <= _playback)
            {
                ApplyBodyEvents(drop, out fell, out gotUp);
                ApplyEvents(drop, out jumped, out landed);
            }

            _buffer.RemoveRange(0, drop);
            _applied -= drop;
            // Until playback reaches the first snapshot there is no state to present yet.
            bool hasState = _applied >= 0;

            CharacterNetSnapshot from = _buffer[0];
            CharacterNetSnapshot to = _buffer.Count > 1 ? _buffer[1] : from;
            Vector3 position;
            Quaternion rotation;
            Vector3 aim = from.Aim;
            Vector3 velocity = Vector3.zero;
            ICharacterSpace space = SpaceOf(from);
            ICharacterSpace next = SpaceOf(to);
            Vector3 fromPosition = from.Position, toPosition = to.Position;
            Quaternion fromRotation = from.Rotation, toRotation = to.Rotation;
            if (next != space)
            {
                // Into a space or out of it between the two: both in the world, as this machine draws the spaces now.
                fromPosition = World(space, fromPosition);
                fromRotation = World(space, fromRotation);
                toPosition = World(next, toPosition);
                toRotation = World(next, toRotation);
                space = null;
            }

            // Into a get-up the owner stood the capsule somewhere new: held, then set there as the get-up starts.
            if (to.Time <= from.Time || _playback <= from.Time || to.GetUps != from.GetUps)
            {
                position = fromPosition;
                rotation = fromRotation;
            }
            else if (Vector3.Distance(fromPosition, toPosition) > _teleportDistance)
            {
                position = fromPosition;
                rotation = fromRotation;
            }
            else
            {
                float t = (float)((_playback - from.Time) / (to.Time - from.Time));
                position = Vector3.Lerp(fromPosition, toPosition, t);
                rotation = Quaternion.Slerp(fromRotation, toRotation, t);
                aim = Vector3.Slerp(from.Aim, to.Aim, t);
                velocity = (toPosition - fromPosition) / (float)(to.Time - from.Time);
            }

            Space = space;
            LocalPosition = position;
            LocalRotation = rotation;
            // Velocity in the space, and with the space's own in the world.
            Vector3 relative = World(space, rotation) * (Quaternion.Inverse(rotation) * velocity);
            position = World(space, position);
            rotation = World(space, rotation);
            Vector3 world = space != null ? relative + space.PointVelocity(position) : relative;
            _body.SetPositionAndRotation(position, rotation);
            if (!hasState)
            {
                return;
            }

            Aim = rotation * aim;
            Motion = new CharacterMotion(world, relative, Aim, rotation, _grounded, _grounded, _swimming, to.FacesLook);
            // Before the motion events, which the animator ignores for a body that is down or getting up.
            if (fell)
            {
                FellDown?.Invoke();
            }

            if (gotUp is { } getUp)
            {
                GettingUp?.Invoke(new CharacterGetUp(getUp.FaceUp, getUp.GetUpClip, getUp.GetUpTime / 1000f));
            }

            if (jumped)
            {
                Jumped?.Invoke();
            }

            if (landed)
            {
                Landed?.Invoke();
            }

            MotionUpdated?.Invoke(Motion);
        }

        // Playback follows the newest snapshot at a fixed delay, speeding up or slowing down slightly
        // rather than jumping, so the pose stays smooth while network jitter is absorbed.
        private void AdvanceClock(float deltaTime)
        {
            double target = _buffer[^1].Time - _delay;
            if (!_started || Math.Abs(target - _playback) > MaxClockError)
            {
                _playback = target;
                _started = true;
                return;
            }
            float correction = Mathf.Clamp((float)(target - _playback), -MaxClockCorrection, MaxClockCorrection);
            _playback += deltaTime * (1f + correction);
        }

        // Replays knockdowns and get-ups among the snapshots ApplyEvents is about to reach. A get-up counts only
        // if no knockdown came after it: the body ends down then. Run before ApplyEvents moves _applied.
        private void ApplyBodyEvents(int reached, out bool fell, out CharacterNetSnapshot? gotUp)
        {
            fell = false;
            gotUp = null;
            for (int i = _applied + 1; i <= reached; i++)
            {
                CharacterNetSnapshot snapshot = _buffer[i];
                if (_applied < 0 && i == 0)
                {
                    _falls = snapshot.Falls;
                    _getUps = snapshot.GetUps;
                    fell = snapshot.Down;
                    continue;
                }

                if (snapshot.Falls != _falls)
                {
                    _falls = snapshot.Falls;
                    fell = true;
                    gotUp = null;
                }

                if (snapshot.GetUps != _getUps)
                {
                    _getUps = snapshot.GetUps;
                    gotUp = snapshot;
                }
            }
        }

        // Replays grounded, swimming, seated and jump changes and the parts of every snapshot playback has reached since
        // the last frame.
        private void ApplyEvents(int reached, out bool jumped, out bool landed)
        {
            jumped = landed = false;
            for (int i = _applied + 1; i <= reached; i++)
            {
                CharacterNetSnapshot snapshot = _buffer[i];
                if (_applied < 0 && i == 0)
                {
                    // The first snapshot sets the baseline; a character that is already down still lands,
                    // which takes the animator out of its initial fall.
                    _jumps = snapshot.Jumps;
                    landed = snapshot.Grounded;
                    _grounded = snapshot.Grounded;
                    _swimming = snapshot.Swimming;
                    _seated = snapshot.Seated;
                    PlayParts(snapshot);
                    continue;
                }
                if (snapshot.Jumps != _jumps)
                {
                    _jumps = snapshot.Jumps;
                    jumped = true;
                    landed = false;
                }
                if (snapshot.Grounded && !_grounded)
                {
                    landed = true;
                }

                _grounded = snapshot.Grounded;
                _swimming = snapshot.Swimming;
                _seated = snapshot.Seated;
                PlayParts(snapshot);
            }
            _applied = Math.Max(_applied, reached);
        }

        private void PlayParts(in CharacterNetSnapshot snapshot)
        {
            if (_parts == null)
            {
                return;
            }

            for (int i = 0; i < _parts.Count; i++)
            {
                if (snapshot.TryGetPart(_parts[i].PartId, out CharacterNetPartReader reader))
                {
                    _parts[i].Play(reader);
                }
            }
        }
    }
}
