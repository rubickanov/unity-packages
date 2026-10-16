using System;
using NUnit.Framework;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace Rubickanov.Characters.Netcode.Tests
{
    /// <summary>
    /// A snapshot comes off the wire as it went on: every core field and flag, the game's parts by id (one this machine
    /// does not know skipped), a part too big for the room left out, and a cut buffer refused instead of read as garbage.
    /// </summary>
    public sealed class CharacterNetSnapshotTests
    {
        [Test]
        public void RoundTrip_CoreFieldsAndEachFlag_ComeBackEqual()
        {
            foreach (CharacterNetFlags flag in Enum.GetValues(typeof(CharacterNetFlags)))
            {
                CharacterNetSnapshot sent = Core(flag);

                byte[] bytes = Write(sent);
                CharacterNetSnapshot back = Read(bytes);

                Assert.That(bytes.Length, Is.EqualTo(CharacterNetSnapshot.CoreSize + 1));
                Assert.That(sent.SerializedSize, Is.EqualTo(bytes.Length));
                Assert.That(back.Time, Is.EqualTo(sent.Time));
                Assert.That(back.Position, Is.EqualTo(sent.Position));
                Assert.That(back.Rotation, Is.EqualTo(sent.Rotation));
                Assert.That(back.Aim, Is.EqualTo(sent.Aim));
                Assert.That((back.Jumps, back.Falls, back.GetUps), Is.EqualTo((sent.Jumps, sent.Falls, sent.GetUps)));
                Assert.That(back.Flags, Is.EqualTo(flag), $"flag {flag}");
                Assert.That(back.Parts.Length, Is.Zero);
            }
        }

        [Test]
        public void FlagProperties_EachSetsItsOwnBit()
        {
            var snapshot = new CharacterNetSnapshot
            {
                Grounded = true, Swimming = true, Seated = true, FacesLook = true, FaceUp = true, Down = true,
            };
            Assert.That((byte)snapshot.Flags, Is.EqualTo(0b11_1111));

            snapshot.Seated = false;

            Assert.That(snapshot.Flags & CharacterNetFlags.Seated, Is.EqualTo(CharacterNetFlags.None));
            Assert.That(snapshot.Grounded && snapshot.Swimming && snapshot.FacesLook && snapshot.FaceUp && snapshot.Down, Is.True);
        }

        [Test]
        public void RoundTrip_TwoParts_EachReadByItsId()
        {
            CharacterNetSnapshot sent = Core(CharacterNetFlags.Grounded);
            var first = new CharacterNetPartWriter();
            first.WriteVarULong(300);
            var second = new CharacterNetPartWriter();
            second.WriteVector3(new Vector3(1.5f, -2f, 3.25f));
            second.WriteBool(true);
            Assert.That(sent.TryAddPart(1, first) && sent.TryAddPart(7, second), Is.True);

            byte[] bytes = Write(sent);
            CharacterNetSnapshot back = Read(bytes);

            Assert.That(bytes.Length, Is.EqualTo(CharacterNetSnapshot.CoreSize + 1 + (2 + 2) + (2 + 13)));
            Assert.That(back.SerializedSize, Is.EqualTo(bytes.Length));
            Assert.That(back.TryGetPart(1, out CharacterNetPartReader a), Is.True);
            Assert.That(a.ReadVarULong(), Is.EqualTo(300UL));
            Assert.That(a.Remaining, Is.Zero);
            Assert.That(back.TryGetPart(7, out CharacterNetPartReader b), Is.True);
            Assert.That(b.ReadVector3(), Is.EqualTo(new Vector3(1.5f, -2f, 3.25f)));
            Assert.That(b.ReadBool(), Is.True);
            b.ReadByte();
            Assert.That(b.Truncated, Is.True, "Reading past a part gives zeros and says so.");
        }

        [Test]
        public void TryGetPart_UnknownPartFirst_SkippedByLength()
        {
            var snapshot = new CharacterNetSnapshot();
            var unknown = new CharacterNetPartWriter();
            unknown.WriteFloat(4f);
            unknown.WriteByte(1);
            var known = new CharacterNetPartWriter();
            known.WriteByte(42);
            snapshot.TryAddPart(9, unknown);
            snapshot.TryAddPart(1, known);

            CharacterNetSnapshot back = Read(Write(snapshot));

            Assert.That(back.TryGetPart(1, out CharacterNetPartReader reader), Is.True);
            Assert.That(reader.ReadByte(), Is.EqualTo(42));
            Assert.That(back.TryGetPart(2, out _), Is.False);
        }

        [Test]
        public void TryAddPart_NoRoomLeft_LeavesThePartOut()
        {
            var snapshot = new CharacterNetSnapshot();
            var big = new CharacterNetPartWriter();
            for (int i = 0; i < 100; i++)
            {
                big.WriteByte((byte)i);
            }

            var small = new CharacterNetPartWriter();
            small.WriteVector3(Vector3.one);
            small.WriteVector3(Vector3.one);
            var huge = new CharacterNetPartWriter();
            for (int i = 0; i < 200; i++)
            {
                huge.WriteByte(1);
            }

            Assert.That(snapshot.TryAddPart(1, big), Is.True);
            Assert.That(snapshot.TryAddPart(2, small), Is.False, "102 + 2 + 24 B is past the 126 B room.");
            Assert.That(snapshot.Parts.Length, Is.EqualTo(102));
            Assert.That(huge.Overflowed, Is.True);
            Assert.That(new CharacterNetSnapshot().TryAddPart(3, huge), Is.False);

            CharacterNetSnapshot back = Read(Write(snapshot));

            Assert.That(back.TryGetPart(1, out CharacterNetPartReader reader) && reader.Remaining == 100, Is.True);
            Assert.That(back.TryGetPart(2, out _), Is.False);
        }

        [TestCase(1)]
        [TestCase(10)]
        [TestCase(30)]
        public void Read_CutBuffer_Throws(int cut)
        {
            CharacterNetSnapshot sent = Core(CharacterNetFlags.Down);
            var part = new CharacterNetPartWriter();
            part.WriteQuaternion(Quaternion.identity);
            sent.TryAddPart(1, part);
            byte[] bytes = Write(sent);

            Assert.Throws<OverflowException>(() => Read(bytes[..^cut]));
        }

        private static CharacterNetSnapshot Core(CharacterNetFlags flags) => new()
        {
            Time = 1234.5678,
            Position = new Vector3(1f, 2f, -3.5f),
            Rotation = Quaternion.Euler(10f, 20f, 30f),
            Aim = new Vector3(0f, 0.5f, 0.86f),
            Jumps = 3,
            Falls = 250,
            GetUps = 7,
            Flags = flags,
        };

        private static byte[] Write(in CharacterNetSnapshot snapshot)
        {
            using var writer = new FastBufferWriter(1300, Allocator.Temp);
            writer.WriteNetworkSerializable(snapshot);
            return writer.ToArray();
        }

        private static CharacterNetSnapshot Read(byte[] bytes)
        {
            using var reader = new FastBufferReader(bytes, Allocator.Temp);
            reader.ReadNetworkSerializable(out CharacterNetSnapshot snapshot);
            return snapshot;
        }
    }
}
