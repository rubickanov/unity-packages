using System;
using Unity.Collections;
using UnityEngine;

namespace Rubickanov.Characters.Netcode
{
    /// <summary>
    /// Where a part writes its bytes (<see cref="ICharacterNetPart.Write"/>). Writing past the room a snapshot has
    /// leaves the part out of it, with a warning.
    /// </summary>
    public struct CharacterNetPartWriter
    {
        internal FixedList128Bytes<byte> Bytes;

        public readonly int Length => Bytes.Length;

        /// <summary>More was written than a snapshot can carry.</summary>
        public bool Overflowed { readonly get; private set; }

        public void WriteByte(byte value)
        {
            if (Bytes.Length >= Bytes.Capacity)
            {
                Overflowed = true;
                return;
            }

            Bytes.Add(value);
        }

        public void WriteBool(bool value) => WriteByte(value ? (byte)1 : (byte)0);

        /// <summary>Seven bits a byte: 1 byte below 128, 2 below 16384.</summary>
        public void WriteVarULong(ulong value)
        {
            while (value >= 0x80)
            {
                WriteByte((byte)(value | 0x80));
                value >>= 7;
            }

            WriteByte((byte)value);
        }

        public void WriteFloat(float value)
        {
            int bits = BitConverter.SingleToInt32Bits(value);
            WriteByte((byte)bits);
            WriteByte((byte)(bits >> 8));
            WriteByte((byte)(bits >> 16));
            WriteByte((byte)(bits >> 24));
        }

        public void WriteVector3(Vector3 value)
        {
            WriteFloat(value.x);
            WriteFloat(value.y);
            WriteFloat(value.z);
        }

        public void WriteQuaternion(Quaternion value)
        {
            WriteFloat(value.x);
            WriteFloat(value.y);
            WriteFloat(value.z);
            WriteFloat(value.w);
        }
    }

    /// <summary>
    /// A part's bytes in one snapshot (<see cref="ICharacterNetPart.Play"/>), read in the order they were written.
    /// Reading past them gives zeros and sets <see cref="Truncated"/>.
    /// </summary>
    public struct CharacterNetPartReader
    {
        private FixedList128Bytes<byte> _bytes;
        private readonly int _end;
        private int _at;

        internal CharacterNetPartReader(in FixedList128Bytes<byte> bytes, int start, int length)
        {
            _bytes = bytes;
            _at = start;
            _end = start + length;
            Truncated = false;
        }

        public readonly int Remaining => _end - _at;

        public bool Truncated { readonly get; private set; }

        public byte ReadByte()
        {
            if (_at >= _end)
            {
                Truncated = true;
                return 0;
            }

            return _bytes[_at++];
        }

        public bool ReadBool() => ReadByte() != 0;

        public ulong ReadVarULong()
        {
            ulong value = 0;
            for (int shift = 0; shift < 64; shift += 7)
            {
                byte b = ReadByte();
                value |= (ulong)(b & 0x7F) << shift;
                if ((b & 0x80) == 0 || Truncated)
                {
                    break;
                }
            }

            return value;
        }

        public float ReadFloat()
        {
            int bits = ReadByte() | (ReadByte() << 8) | (ReadByte() << 16) | (ReadByte() << 24);
            return BitConverter.Int32BitsToSingle(bits);
        }

        public Vector3 ReadVector3() => new(ReadFloat(), ReadFloat(), ReadFloat());

        public Quaternion ReadQuaternion() => new(ReadFloat(), ReadFloat(), ReadFloat(), ReadFloat());
    }
}
