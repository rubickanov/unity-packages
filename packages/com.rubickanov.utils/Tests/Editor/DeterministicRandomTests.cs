using System;
using NUnit.Framework;
using Rubickanov.Utils;

namespace Rubickanov.Utils.Tests
{
    public class DeterministicRandomTests
    {
        [Test]
        public void Hash_SameInputs_ReturnsSameResult()
        {
            uint result1 = DeterministicRandom.Hash(42, 7);
            uint result2 = DeterministicRandom.Hash(42, 7);

            Assert.AreEqual(result1, result2);
        }

        [Test]
        public void Hash_DifferentSecondKey_ReturnsDifferentResult()
        {
            uint hash1 = DeterministicRandom.Hash(1, 2);
            uint hash2 = DeterministicRandom.Hash(1, 3);

            Assert.AreNotEqual(hash1, hash2);
        }

        [Test]
        public void Hash_DifferentFirstKey_ReturnsDifferentResult()
        {
            uint hash1 = DeterministicRandom.Hash(1, 2);
            uint hash2 = DeterministicRandom.Hash(2, 2);

            Assert.AreNotEqual(hash1, hash2);
        }

        [Test]
        public void Hash_ZeroInputs_DoesNotThrow()
        {
            uint result = DeterministicRandom.Hash(0, 0);

            Assert.IsNotNull(result);
        }

        [Test]
        public void Hash_MaxValueInputs_DoesNotThrow()
        {
            uint result = DeterministicRandom.Hash(uint.MaxValue, uint.MaxValue);

            Assert.IsNotNull(result);
        }

        [Test]
        public void Hash3_SameInputs_ReturnsSameResult()
        {
            uint result1 = DeterministicRandom.Hash(1, 2, 3);
            uint result2 = DeterministicRandom.Hash(1, 2, 3);

            Assert.AreEqual(result1, result2);
        }

        [Test]
        public void Hash3_DifferentThirdKey_ReturnsDifferentResult()
        {
            uint hash1 = DeterministicRandom.Hash(1, 2, 3);
            uint hash2 = DeterministicRandom.Hash(1, 2, 4);

            Assert.AreNotEqual(hash1, hash2);
        }

        [Test]
        public void Hash4_DifferentKeys_ReturnsDifferentResults()
        {
            uint hash1 = DeterministicRandom.Hash(1, 2, 3, 4);
            uint hash2 = DeterministicRandom.Hash(1, 2, 3, 5);

            Assert.AreNotEqual(hash1, hash2);
        }

        [Test]
        public void Float01_AlwaysInRange([Range(0u, 100u, 1u)] uint a)
        {
            float value = DeterministicRandom.Float01(a, 42);

            Assert.GreaterOrEqual(value, 0f);
            Assert.Less(value, 1f);
        }

        [Test]
        public void Float01_ThreeKeys_AlwaysInRange([Range(0u, 100u, 1u)] uint a)
        {
            float value = DeterministicRandom.Float01(a, 42, 99);

            Assert.GreaterOrEqual(value, 0f);
            Assert.Less(value, 1f);
        }

        [Test]
        public void Range_AlwaysInBounds([Range(0u, 50u, 1u)] uint a)
        {
            float value = DeterministicRandom.Range(a, 7, 10f, 20f);

            Assert.GreaterOrEqual(value, 10f);
            Assert.Less(value, 20f);
        }

        [Test]
        public void Range_MinEqualsMax_ReturnsMin()
        {
            float value = DeterministicRandom.Range(1, 2, 5f, 5f);

            Assert.AreEqual(5f, value);
        }

        [Test]
        public void Int_MaxEqualsMin_ThrowsArgumentException()
        {
            Assert.Throws<ArgumentException>(() => DeterministicRandom.Int(1u, 2u, 5, 5));
        }

        [Test]
        public void Int_MaxLessThanMin_ThrowsArgumentException()
        {
            Assert.Throws<ArgumentException>(() => DeterministicRandom.Int(1u, 2u, 10, 5));
        }

        [Test]
        public void Int3_MaxEqualsMin_ThrowsArgumentException()
        {
            Assert.Throws<ArgumentException>(() => DeterministicRandom.Int(1u, 2u, 3u, 5, 5));
        }

        [Test]
        public void Int3_MaxLessThanMin_ThrowsArgumentException()
        {
            Assert.Throws<ArgumentException>(() => DeterministicRandom.Int(1u, 2u, 3u, 10, 5));
        }

        [Test]
        public void Int_AlwaysInBounds([Range(0u, 100u, 1u)] uint a)
        {
            int value = DeterministicRandom.Int(a, 7, 5, 15);

            Assert.GreaterOrEqual(value, 5);
            Assert.Less(value, 15);
        }

        [Test]
        public void Int_ThreeKeys_AlwaysInBounds([Range(0u, 100u, 1u)] uint a)
        {
            int value = DeterministicRandom.Int(a, 7, 3, 0, 10);

            Assert.GreaterOrEqual(value, 0);
            Assert.Less(value, 10);
        }

        [Test]
        public void Int_FullIntRange_StaysInBounds([Range(0u, 200u, 1u)] uint a)
        {
            int value = DeterministicRandom.Int(a, 13, int.MinValue, int.MaxValue);

            Assert.GreaterOrEqual(value, int.MinValue);
            Assert.Less(value, int.MaxValue);
        }

        [Test]
        public void Int_RangeWiderThanIntMaxValue_StaysInBounds([Range(0u, 200u, 1u)] uint a)
        {
            const int min = -2_000_000_000;
            const int maxExclusive = 2_000_000_000;

            int value = DeterministicRandom.Int(a, 17, min, maxExclusive);

            Assert.GreaterOrEqual(value, min);
            Assert.Less(value, maxExclusive);
        }

        [Test]
        public void Int3_RangeWiderThanIntMaxValue_StaysInBounds([Range(0u, 200u, 1u)] uint a)
        {
            const int min = -2_000_000_000;
            const int maxExclusive = 2_000_000_000;

            int value = DeterministicRandom.Int(a, 17, 29, min, maxExclusive);

            Assert.GreaterOrEqual(value, min);
            Assert.Less(value, maxExclusive);
        }

        [Test]
        public void Bool_Deterministic()
        {
            bool a = DeterministicRandom.Bool(42, 7);
            bool b = DeterministicRandom.Bool(42, 7);

            Assert.AreEqual(a, b);
        }

        [Test]
        public void Bool_NotAlwaysSame()
        {
            bool foundTrue = false;
            bool foundFalse = false;
            for (uint i = 0; i < 100; i++)
            {
                if (DeterministicRandom.Bool(i, 0)) foundTrue = true;
                else foundFalse = true;
            }

            Assert.IsTrue(foundTrue, "Bool never returned true");
            Assert.IsTrue(foundFalse, "Bool never returned false");
        }

        [Test]
        public void Bool3_SameInputs_ReturnsSameResult()
        {
            bool a = DeterministicRandom.Bool(1u, 2u, 3u);
            bool b = DeterministicRandom.Bool(1u, 2u, 3u);

            Assert.AreEqual(a, b);
        }

        [Test]
        public void Bool3_NotAlwaysSame()
        {
            bool foundTrue = false;
            bool foundFalse = false;
            for (uint i = 0; i < 100; i++)
            {
                if (DeterministicRandom.Bool(i, 0u, 7u)) foundTrue = true;
                else foundFalse = true;
            }

            Assert.IsTrue(foundTrue, "Bool3 never returned true");
            Assert.IsTrue(foundFalse, "Bool3 never returned false");
        }

        [Test]
        public void Sign_ReturnsOnlyMinusOneOrPlusOne([Range(0u, 50u, 1u)] uint a)
        {
            float value = DeterministicRandom.Sign(a, 42);

            Assert.That(value == -1f || value == 1f);
        }

        [Test]
        public void Sign3_ReturnsOnlyMinusOneOrPlusOne([Range(0u, 50u, 1u)] uint a)
        {
            float value = DeterministicRandom.Sign(a, 42u, 7u);

            Assert.That(value == -1f || value == 1f);
        }
    }
}
