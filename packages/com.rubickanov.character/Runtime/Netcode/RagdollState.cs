using Rubickanov.Rig;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace Rubickanov.Characters.Netcode
{
    /// <summary>
    /// A ragdoll handed from the machine that simulated it to the one that simulates it next
    /// (<see cref="NetworkCharacter.Lend"/>): every bone's position and rotation in a frame's space
    /// (<see cref="IRagdollNetFrame"/>; frame 0 is the world here), its velocity and spin in the world, and up to
    /// <see cref="MaxGameBytes"/> of the game's own (<see cref="IRagdollNetState"/>). Sent reliably, once per hand-over.
    /// </summary>
    public struct RagdollState : INetworkSerializable
    {
        public const int MaxGameBytes = 64;

        /// <summary>The hand-over it belongs to (<see cref="NetworkCharacter.Lends"/>).</summary>
        public ushort Lend;
        public uint Frame;
        /// <summary>The time on the frame's clock when it was taken.</summary>
        public double Time;
        /// <summary>The ragdoll was on (down, or live on something); off, only the game's bytes count.</summary>
        public bool HasBody;
        public bool Down;
        /// <summary>The body came to rest where it was simulated: the next machine gets it up at once.</summary>
        public bool GetUp;
        public Vector3[] Positions;
        public Quaternion[] Rotations;
        public Vector3[] Velocities;
        public Vector3[] Spins;
        public FixedList128Bytes<byte> Game;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref Lend);
            serializer.SerializeValue(ref Frame);
            serializer.SerializeValue(ref Time);
            byte flags = (byte)((HasBody ? 1 : 0) | (Down ? 2 : 0) | (GetUp ? 4 : 0));
            serializer.SerializeValue(ref flags);
            HasBody = (flags & 1) != 0;
            Down = (flags & 2) != 0;
            GetUp = (flags & 4) != 0;

            byte count = (byte)(HasBody && Positions != null ? Positions.Length : 0);
            serializer.SerializeValue(ref count);
            if (serializer.IsReader)
            {
                Positions = new Vector3[count];
                Rotations = new Quaternion[count];
                Velocities = new Vector3[count];
                Spins = new Vector3[count];
            }

            for (int i = 0; i < count; i++)
            {
                serializer.SerializeValue(ref Positions[i]);
                uint packed = serializer.IsWriter ? RagdollMath.PackRotation(Rotations[i]) : 0u;
                serializer.SerializeValue(ref packed);
                if (serializer.IsReader)
                {
                    Rotations[i] = RagdollMath.UnpackRotation(packed);
                }

                serializer.SerializeValue(ref Velocities[i]);
                serializer.SerializeValue(ref Spins[i]);
            }

            byte length = (byte)Mathf.Min(Game.Length, MaxGameBytes);
            serializer.SerializeValue(ref length);
            if (serializer.IsReader)
            {
                Game.Length = Mathf.Min(length, MaxGameBytes);
            }

            for (int i = 0; i < length; i++)
            {
                byte b = serializer.IsWriter ? Game[i] : (byte)0;
                serializer.SerializeValue(ref b);
                if (serializer.IsReader && i < Game.Length)
                {
                    Game[i] = b;
                }
            }
        }
    }

    /// <summary>
    /// The game's own part of a ragdoll handed to another machine (<see cref="RagdollState"/>), e.g. how far a hand has
    /// slipped on a handle. A component of the game on the character (any child), at most one; optional.
    /// </summary>
    public interface IRagdollNetState
    {
        /// <summary>The machine handing the body on, as it does; at most <see cref="RagdollState.MaxGameBytes"/>.</summary>
        void WriteState(ref CharacterNetPartWriter writer);

        /// <summary>The machine taking it over, just before it does.</summary>
        void ReadState(CharacterNetPartReader reader);
    }
}
