using System;
using UnityEngine;

namespace Rubickanov.Motor
{
    /// <summary>
    /// Lightweight binary reader for module state deserialization.
    /// Used by <see cref="IStatefulModule.RestoreState"/>.
    /// </summary>
    public struct ModuleStateReader
    {
        private readonly byte[] _buffer;
        private int _position;

        public ModuleStateReader(byte[] buffer)
        {
            _buffer = buffer;
            _position = 0;
        }

        public float ReadFloat()
        {
            float value = BitConverter.ToSingle(_buffer, _position);
            _position += 4;
            return value;
        }

        public bool ReadBool()
        {
            return _buffer[_position++] != 0;
        }

        public int ReadInt()
        {
            int value = BitConverter.ToInt32(_buffer, _position);
            _position += 4;
            return value;
        }

        public Vector3 ReadVector3()
        {
            return new Vector3(ReadFloat(), ReadFloat(), ReadFloat());
        }
    }
}
