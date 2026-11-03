using System;
using System.Collections.Generic;
using NUnit.Framework;
using Rubickanov.Save.Data;
using UnityEngine;

namespace Rubickanov.Save.Tests.Data
{
    /// <summary>The text of a tree: canonical when written, exact for every number, lenient with what JsonUtility writes.</summary>
    public sealed class SaveJsonTests
    {
        [Test]
        public void WriteRead_EdgeNumbers_ComeBackBitForBit()
        {
            long[] longs = { long.MinValue, long.MaxValue, 76561198000000000L, 0, -1 };
            double[] doubles = { BitConverter.Int64BitsToDouble(long.MinValue), double.Epsilon, 1e300, -1e-300, double.MaxValue, 0.1, 1.0 / 3, 2.5, 1e15 + 0.5 };
            float[] floats = { float.MaxValue, float.Epsilon, 0.1f, BitConverter.Int32BitsToSingle(int.MinValue), 1f / 3, 16777216f };
            SaveNode tree = SaveNode.NewObject();
            tree["longs"] = Array(longs, SaveNode.Of);
            tree["doubles"] = Array(doubles, SaveNode.Of);
            tree["floats"] = Array(floats, SaveNode.Of);

            string text = SaveJson.Write(tree);
            Assert.That(SaveJson.TryRead(text, out SaveNode read, out string error), Is.True, error);

            for (int i = 0; i < longs.Length; i++)
            {
                Assert.That(read["longs"][i].IsInteger, Is.True, $"{longs[i]}");
                Assert.That(read["longs"][i].Long, Is.EqualTo(longs[i]));
            }

            for (int i = 0; i < doubles.Length; i++)
            {
                Assert.That(BitConverter.DoubleToInt64Bits(read["doubles"][i].Double),
                    Is.EqualTo(BitConverter.DoubleToInt64Bits(doubles[i])), $"{doubles[i]:R} read as {read["doubles"][i].Double:R} from {text}");
            }

            for (int i = 0; i < floats.Length; i++)
            {
                Assert.That(BitConverter.SingleToInt32Bits((float)read["floats"][i].Double),
                    Is.EqualTo(BitConverter.SingleToInt32Bits(floats[i])), $"{floats[i]:R} in {text}");
            }

            Assert.That(SaveJson.Write(read), Is.EqualTo(text), "read and written again, the text is the same");
            Assert.That(text, Does.Contain("\"floats\": [3.4028235E+38, 1.4013E-45, 0.1, -0.0, 0.33333334, 16777216]"),
                "a float has its fewest digits");
        }

        [Test]
        public void Write_Tree_IsCanonicalAndStable()
        {
            SaveWriter w = SaveWriter.Over(null);
            w.String("name", "Ann \"Sunny\"\n");
            w.Int("day", 3);
            w.Float("rain", 0.1f);
            w.Double("zero", BitConverter.Int64BitsToDouble(long.MinValue));
            w.Double("whole", 3.0);
            w.Vector3("at", new Vector3(12.5f, 0.18f, -40.25f));
            w.Time("saved", new DateTime(2026, 10, 20, 15, 0, 0, DateTimeKind.Utc));
            SaveWriter look = w.Object("look");
            look.Float("skin", float.NaN);
            look.Bool("hat", true);
            SaveListWriter links = w.List("links");
            links.Add().String("to", "jetski");
            links.Add();
            w.Map("kept");
            var errors = new List<string>();

            string text = SaveJson.Write(w.Tree, errors.Add);

            Assert.That(text, Is.EqualTo(
                "{\n" +
                "  \"at\": [12.5, 0.18, -40.25],\n" +
                "  \"day\": 3,\n" +
                "  \"kept\": {},\n" +
                "  \"links\": [\n" +
                "    {\n" +
                "      \"to\": \"jetski\"\n" +
                "    },\n" +
                "    {}\n" +
                "  ],\n" +
                "  \"look\": {\n" +
                "    \"hat\": true,\n" +
                "    \"skin\": null\n" +
                "  },\n" +
                "  \"name\": \"Ann \\\"Sunny\\\"\\n\",\n" +
                "  \"rain\": 0.1,\n" +
                "  \"saved\": \"2026-10-20T15:00:00Z\",\n" +
                "  \"whole\": 3,\n" +
                "  \"zero\": -0.0\n" +
                "}\n"));
            Assert.That(errors, Is.EqualTo(new[] { "look/skin: NaN has no JSON, written as null" }));
            Assert.That(SaveJson.TryRead(text, out SaveNode read, out _), Is.True);
            Assert.That(SaveJson.Write(read), Is.EqualTo(text));
            SaveReader r = SaveReader.Of(read);
            Assert.That(r.Vector3("at"), Is.EqualTo(new Vector3(12.5f, 0.18f, -40.25f)));
            Assert.That(r.Time("saved"), Is.EqualTo(new DateTime(2026, 10, 20, 15, 0, 0, DateTimeKind.Utc)));
            Assert.That(r.Float("rain"), Is.EqualTo(0.1f));
        }

        [Test]
        public void TryRead_JsonUtilityText_ReadsNaNAsNullWithANote()
        {
            string json = JsonUtility.ToJson(new Skin());
            var notes = new List<string>();

            bool ok = SaveJson.TryRead(json, out SaveNode node, out string error, notes);

            Assert.That(ok, Is.True, $"{error} in {json}");
            Assert.That(node["r"].IsNull, Is.True);
            Assert.That(node["g"].Double, Is.EqualTo(0.5));
            Assert.That(node["name"].String, Is.EqualTo("Zoë \"Z\""));
            Assert.That(notes, Is.EqualTo(new[] { "r: NaN read as null" }));
            Assert.That(SaveReader.Of(node).Float("r", 1f), Is.EqualTo(1f));
        }

        [Test]
        public void TryRead_EscapedText_ReadsTheCharacters()
        {
            Assert.That(SaveJson.TryRead("{\"name\": \"Zo\\u00eb \\ud83c\\udf34\"}", out SaveNode node, out _), Is.True);

            Assert.That(node["name"].String, Is.EqualTo("Zoë 🌴"));
        }

        private static SaveNode Array<T>(T[] values, Func<T, SaveNode> of)
        {
            SaveNode array = SaveNode.NewArray();
            foreach (T value in values)
            {
                array.Add(of(value));
            }

            return array;
        }

        [Serializable]
        private sealed class Skin
        {
            public float r = float.NaN;
            public float g = 0.5f;
            public string name = "Zoë \"Z\"";
        }
    }
}
