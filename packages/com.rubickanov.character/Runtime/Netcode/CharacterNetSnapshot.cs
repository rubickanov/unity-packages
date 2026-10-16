using System;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace Rubickanov.Characters.Netcode
{
    /// <summary>The yes/no state of a snapshot, one byte on the wire.</summary>
    [Flags]
    public enum CharacterNetFlags : byte
    {
        None = 0,
        Grounded = 1 << 0,
        Swimming = 1 << 1,
        /// <summary>
        /// Sat in a seat on this step. The host's word that it stood up comes ahead of the snapshots of it standing up,
        /// so a replica keeps the body in the seat until playback reaches one without.
        /// </summary>
        Seated = 1 << 2,
        /// <summary>The body faces where it looks (first person with MovementConfig.FirstPersonTurn AtOnce).</summary>
        FacesLook = 1 << 3,
        /// <summary>The latest get-up was from lying face up.</summary>
        FaceUp = 1 << 4,
        /// <summary>The ragdoll was the body on this step, for a replica that starts watching mid-fall.</summary>
        Down = 1 << 5,
    }

    /// <summary>
    /// One simulation step of a character as its owner saw it: what the package simulates, and the game's parts
    /// (<see cref="ICharacterNetPart"/>) as bytes. Written field by field: <see cref="CoreSize"/> bytes, then the parts'
    /// length and the parts.
    /// </summary>
    public struct CharacterNetSnapshot : INetworkSerializable
    {
        /// <summary>Time 8, Position 12, Rotation 16, Aim 12, Jumps, Falls, GetUps, Flags 1 each, GetUpClip 1,
        /// GetUpTime 2.</summary>
        public const int CoreSize = 55;

        /// <summary>The owner's Time.fixedTime of the step. Only meaningful relative to other snapshots.</summary>
        public double Time;

        /// <summary>In the space a frame part gives (<see cref="ICharacterNetFrame"/>), the world's without one.</summary>
        public Vector3 Position;

        public Quaternion Rotation;

        /// <summary>Look direction in the character's space, as in CharacterMotion.Aim.</summary>
        public Vector3 Aim;

        /// <summary>Jumps so far, wrapping. A change means a jump happened, even if its snapshot was lost.</summary>
        public byte Jumps;

        /// <summary>Knockdowns so far, wrapping, as with Jumps.</summary>
        public byte Falls;

        /// <summary>Get-ups so far, wrapping, as with Jumps.</summary>
        public byte GetUps;

        public CharacterNetFlags Flags;

        /// <summary>The last get-up's clip in the config's GetUps, <see cref="CharacterGetUp.Classic"/> for the face up /
        /// down one.</summary>
        public byte GetUpClip;

        /// <summary>Where the last get-up's clip started, ms.</summary>
        public ushort GetUpTime;

        /// <summary>The game's parts, back to back: id, length, bytes each. Empty without any.</summary>
        public FixedList128Bytes<byte> Parts;

        public bool Grounded { readonly get => Has(CharacterNetFlags.Grounded); set => Set(CharacterNetFlags.Grounded, value); }

        public bool Swimming { readonly get => Has(CharacterNetFlags.Swimming); set => Set(CharacterNetFlags.Swimming, value); }

        public bool Seated { readonly get => Has(CharacterNetFlags.Seated); set => Set(CharacterNetFlags.Seated, value); }

        public bool FacesLook { readonly get => Has(CharacterNetFlags.FacesLook); set => Set(CharacterNetFlags.FacesLook, value); }

        public bool FaceUp { readonly get => Has(CharacterNetFlags.FaceUp); set => Set(CharacterNetFlags.FaceUp, value); }

        public bool Down { readonly get => Has(CharacterNetFlags.Down); set => Set(CharacterNetFlags.Down, value); }

        /// <summary>Bytes on the wire, before the RPC's own framing.</summary>
        public readonly int SerializedSize => CoreSize + 1 + Parts.Length;

        private readonly bool Has(CharacterNetFlags flag) => (Flags & flag) != 0;

        private void Set(CharacterNetFlags flag, bool on) => Flags = on ? Flags | flag : Flags & ~flag;

        /// <summary>Adds a part's bytes; false, leaving the snapshot as it was, if they do not fit.</summary>
        public bool TryAddPart(byte id, in CharacterNetPartWriter writer)
        {
            int length = writer.Length;
            if (writer.Overflowed || Parts.Length + 2 + length > Parts.Capacity)
            {
                return false;
            }

            Parts.Add(id);
            Parts.Add((byte)length);
            for (int i = 0; i < length; i++)
            {
                Parts.Add(writer.Bytes[i]);
            }

            return true;
        }

        /// <summary>The bytes of the part with this id, skipping any other (a part this machine does not have).</summary>
        public readonly bool TryGetPart(byte id, out CharacterNetPartReader reader)
        {
            FixedList128Bytes<byte> parts = Parts;
            int at = 0;
            while (at + 2 <= parts.Length)
            {
                byte partId = parts[at];
                int length = parts[at + 1];
                int start = at + 2;
                if (start + length > parts.Length)
                {
                    break;
                }

                if (partId == id)
                {
                    reader = new CharacterNetPartReader(parts, start, length);
                    return true;
                }

                at = start + length;
            }

            reader = default;
            return false;
        }

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref Time);
            serializer.SerializeValue(ref Position);
            serializer.SerializeValue(ref Rotation);
            serializer.SerializeValue(ref Aim);
            serializer.SerializeValue(ref Jumps);
            serializer.SerializeValue(ref Falls);
            serializer.SerializeValue(ref GetUps);
            byte flags = (byte)Flags;
            serializer.SerializeValue(ref flags);
            Flags = (CharacterNetFlags)flags;
            serializer.SerializeValue(ref GetUpClip);
            serializer.SerializeValue(ref GetUpTime);

            // No serializer overload for a FixedList: the used length, then the bytes. A short buffer fails at once.
            byte count = (byte)Parts.Length;
            serializer.SerializeValue(ref count);
            if (serializer.IsReader && count > Parts.Capacity)
            {
                throw new OverflowException($"Character snapshot parts of {count} B, more than {Parts.Capacity}");
            }

            if (!serializer.PreCheck(count))
            {
                throw new OverflowException($"Character snapshot parts of {count} B past the end of the buffer");
            }

            if (serializer.IsReader)
            {
                Parts.Length = count;
            }

            for (int i = 0; i < count; i++)
            {
                byte b = serializer.IsWriter ? Parts[i] : (byte)0;
                serializer.SerializeValuePreChecked(ref b);
                if (serializer.IsReader)
                {
                    Parts[i] = b;
                }
            }
        }
    }
}
