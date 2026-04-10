using NUnit.Framework;
using UnityEngine;

namespace Rubickanov.Motor.Tests
{
    [TestFixture]
    public class ModuleStateSerializationTests
    {
        [Test]
        public void WriteReadFloat_Roundtrip_ReturnsSameValue()
        {
            var writer = new ModuleStateWriter(32);
            writer.Write(3.14159f);
            var reader = new ModuleStateReader(writer.ToArray());

            Assert.AreEqual(3.14159f, reader.ReadFloat());
        }

        [Test]
        public void WriteReadBool_Roundtrip_ReturnsSameValue()
        {
            var writer = new ModuleStateWriter(32);
            writer.Write(true);
            writer.Write(false);
            var reader = new ModuleStateReader(writer.ToArray());

            Assert.IsTrue(reader.ReadBool());
            Assert.IsFalse(reader.ReadBool());
        }

        [Test]
        public void WriteReadInt_Roundtrip_ReturnsSameValue()
        {
            var writer = new ModuleStateWriter(32);
            writer.Write(-12345);
            writer.Write(98765);
            var reader = new ModuleStateReader(writer.ToArray());

            Assert.AreEqual(-12345, reader.ReadInt());
            Assert.AreEqual(98765, reader.ReadInt());
        }

        [Test]
        public void WriteReadVector2_Roundtrip_ReturnsSameValue()
        {
            var writer = new ModuleStateWriter(32);
            writer.Write(new Vector2(1.5f, -2.25f));
            var reader = new ModuleStateReader(writer.ToArray());

            Assert.AreEqual(new Vector2(1.5f, -2.25f), reader.ReadVector2());
        }

        [Test]
        public void WriteReadVector3_Roundtrip_ReturnsSameValue()
        {
            var writer = new ModuleStateWriter(32);
            writer.Write(new Vector3(1f, 2f, 3f));
            var reader = new ModuleStateReader(writer.ToArray());

            Assert.AreEqual(new Vector3(1f, 2f, 3f), reader.ReadVector3());
        }

        [Test]
        public void InterleavedTypes_RoundtripInOrder_PreservesAllValues()
        {
            var writer = new ModuleStateWriter(32);
            writer.Write(1.5f);
            writer.Write(true);
            writer.Write(42);
            writer.Write(new Vector3(10f, 20f, 30f));
            writer.Write(false);
            writer.Write(new Vector2(-1f, 1f));

            var reader = new ModuleStateReader(writer.ToArray());

            Assert.AreEqual(1.5f, reader.ReadFloat());
            Assert.IsTrue(reader.ReadBool());
            Assert.AreEqual(42, reader.ReadInt());
            Assert.AreEqual(new Vector3(10f, 20f, 30f), reader.ReadVector3());
            Assert.IsFalse(reader.ReadBool());
            Assert.AreEqual(new Vector2(-1f, 1f), reader.ReadVector2());
        }

        [Test]
        public void WriteBeyondInitialCapacity_BufferGrowsAndDataIntact()
        {
            var writer = new ModuleStateWriter(4);
            for (int i = 0; i < 20; i++)
                writer.Write((float)i);

            var reader = new ModuleStateReader(writer.ToArray());

            for (int i = 0; i < 20; i++)
                Assert.AreEqual((float)i, reader.ReadFloat());
        }

        [Test]
        public void ToArray_ReturnsOnlyWrittenBytes()
        {
            var writer = new ModuleStateWriter(64);
            writer.Write(1f);
            writer.Write(true);

            var bytes = writer.ToArray();

            Assert.AreEqual(5, bytes.Length);
        }
    }
}
