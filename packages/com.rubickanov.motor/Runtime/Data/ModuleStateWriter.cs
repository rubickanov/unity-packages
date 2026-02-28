using System;
using UnityEngine;

namespace Rubickanov.Motor
{
    /// <summary>
    /// Lightweight binary writer for module state serialization.
    /// Used by <see cref="IStatefulModule.SaveState"/>.
    /// </summary>
    public struct ModuleStateWriter
    {
        private byte[] _buffer;
        private int _position;

        public ModuleStateWriter(int initialCapacity = 64)
        {
            _buffer = new byte[initialCapacity];
            _position = 0;
        }

        public byte[] ToArray()
        {
            var result = new byte[_position];
            Array.Copy(_buffer, result, _position);
            return result;
        }

        public void Write(float value)
        {
            EnsureCapacity(4);
            BitConverter.TryWriteBytes(_buffer.AsSpan(_position), value);
            _position += 4;
        }

        public void Write(bool value)
        {
            EnsureCapacity(1);
            _buffer[_position++] = value ? (byte)1 : (byte)0;
        }

        public void Write(int value)
        {
            EnsureCapacity(4);
            BitConverter.TryWriteBytes(_buffer.AsSpan(_position), value);
            _position += 4;
        }

        public void Write(Vector3 value)
        {
            Write(value.x);
            Write(value.y);
            Write(value.z);
        }

        private void EnsureCapacity(int bytes)
        {
            if (_position + bytes <= _buffer.Length) return;
            int newSize = Math.Max(_buffer.Length * 2, _position + bytes);
            var newBuffer = new byte[newSize];
            Array.Copy(_buffer, newBuffer, _position);
            _buffer = newBuffer;
        }
    }
}
