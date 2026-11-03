using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using NUnit.Framework;
using Rubickanov.Save;

namespace Rubickanov.Settings.Tests
{
    /// <summary>The settings file: kept by id, what nobody declared kept too, broken files aside, older ones migrated.</summary>
    public sealed class SettingsStoreTests
    {
        private static readonly SaveArea Area = SaveArea.AtRoot("Settings", "settings");
        private static readonly SaveKey Key = new(Area, "settings.json");

        private MemorySaveStore _disk = null!;
        private List<string> _errors = null!;
        private List<string> _warnings = null!;

        [SetUp]
        public void SetUp()
        {
            _disk = new MemorySaveStore();
            _errors = new List<string>();
            _warnings = new List<string>();
        }

        [Test]
        public void ReadAsync_NoFile_DefaultsStayAndNothingIsWritten()
        {
            using var store = Store();
            StepSetting master = store.Steps("sound.master", 10);
            Setting<WindowMode> window = store.Enum("display.window", WindowMode.Fullscreen);

            store.ReadAsync().GetAwaiter().GetResult();

            Assert.That(master.Value, Is.EqualTo(10));
            Assert.That(window.Value, Is.EqualTo(WindowMode.Fullscreen));
            Assert.That(store.IsRead, Is.True);
            Assert.That(store.SaveAsync().GetAwaiter().GetResult(), Is.False);
            Assert.That(File(), Is.Null);
        }

        [Test]
        public void SaveAsync_ThenAnotherStoreReads_ValuesComeBack()
        {
            using (var first = Store())
            {
                var display = new DisplaySettings(first);
                StepSetting master = first.Steps("sound.master", 10);
                Setting<float> gamma = first.Range("picture.gamma", 1f, 0.5f, 2f);
                Setting<string> name = first.Text("profile.name", "");
                first.ReadAsync().GetAwaiter().GetResult();
                display.Window.Value = WindowMode.Windowed;
                display.Size.Value = new ScreenSize(1280, 720);
                display.VSync.Value = false;
                display.FrameCap.Value = 144;
                master.Value = 3;
                gamma.Value = 1.25f;
                name.Value = "Pina";

                Assert.That(first.SaveAsync().GetAwaiter().GetResult(), Is.True);
            }

            using var second = Store();
            var read = new DisplaySettings(second);
            StepSetting readMaster = second.Steps("sound.master", 10);
            Setting<float> readGamma = second.Range("picture.gamma", 1f, 0.5f, 2f);
            Setting<string> readName = second.Text("profile.name", "");
            second.ReadAsync().GetAwaiter().GetResult();

            Assert.That(read.Window.Value, Is.EqualTo(WindowMode.Windowed));
            Assert.That(read.Size.Value, Is.EqualTo(new ScreenSize(1280, 720)));
            Assert.That(read.VSync.Value, Is.False);
            Assert.That(read.FrameCap.Value, Is.EqualTo(144));
            Assert.That(readMaster.Value, Is.EqualTo(3));
            Assert.That(readGamma.Value, Is.EqualTo(1.25f));
            Assert.That(readName.Value, Is.EqualTo("Pina"));
            Assert.That(File(), Does.Contain("\"window\": \"windowed\"").And.Contain("\"size\": \"1280x720\""));
            Assert.That(_errors, Is.Empty);
            Assert.That(_warnings, Is.Empty);
        }

        [Test]
        public void SaveAsync_IdsNobodyDeclared_AreWrittenBackAsTheyWere()
        {
            Write("{\"format\":1,\"sound\":{\"master\":3},\"future\":{\"thing\":[1,2],\"deep\":{\"x\":\"y\"},\"empty\":{}},\"other\":true}");
            using (var store = Store())
            {
                StepSetting master = store.Steps("sound.master", 10);
                store.ReadAsync().GetAwaiter().GetResult();
                master.Value = 4;

                store.SaveAsync().GetAwaiter().GetResult();
            }

            Assert.That(File(), Does.Contain("\"thing\": [1,2]").And.Contain("\"empty\": {}").And.Contain("\"other\": true"));
            using var later = Store();
            Setting<string> deep = later.Text("future.deep.x", "");
            StepSetting again = later.Steps("sound.master", 10);
            later.ReadAsync().GetAwaiter().GetResult();
            Assert.That(deep.Value, Is.EqualTo("y"));
            Assert.That(again.Value, Is.EqualTo(4));
        }

        [Test]
        public void Add_AfterTheRead_TakesTheKeptValueAndIsNoChange()
        {
            Write("{\"format\":1,\"look\":{\"mouse\":8}}");
            using var store = Store();
            store.ReadAsync().GetAwaiter().GetResult();

            var look = new LookSettings(store);

            Assert.That(look.Mouse.Value, Is.EqualTo(8));
            Assert.That(look.Pad.Value, Is.EqualTo(LookSettings.MiddleStep));
            Assert.That(store.SaveAsync().GetAwaiter().GetResult(), Is.False);
        }

        [TestCase("not json at all")]
        [TestCase("[1, 2]")]
        [TestCase("{\"format\": \"one\"}")]
        [TestCase("{\"format\": 1.5, \"sound\": {\"master\": 2}}")]
        [TestCase("{\"format\": 1, \"sound\": {\"master\": 2}")]
        public void ReadAsync_BrokenFile_IsKeptAsideAndTheDefaultsStart(string text)
        {
            Write(text);
            using var store = Store();
            StepSetting master = store.Steps("sound.master", 10);

            store.ReadAsync().GetAwaiter().GetResult();

            Assert.That(master.Value, Is.EqualTo(10));
            Assert.That(_disk.ReadTextAsync(new SaveKey(Area, "settings.broken.json")).GetAwaiter().GetResult(), Is.EqualTo(text));
            Assert.That(_errors, Is.Not.Empty);
            Assert.That(_errors[_errors.Count - 1], Does.Contain(store.BrokenWhere));
        }

        [Test]
        public void ReadAsync_FileWithoutFormat_MigrateRenamesAndRemovesBeforeTheSettingsTakeIt()
        {
            Write("{\"sound\":{\"volume\":3},\"picture\":{\"grain\":1}}");
            int from = -1;
            using var store = Store(new SettingsFileOptions
            {
                Format = 1,
                Migrate = (format, values) =>
                {
                    from = format;
                    values.Rename("sound.volume", "sound.master");
                    values.Remove("picture.grain");
                    return true;
                },
            });
            StepSetting master = store.Steps("sound.master", 10);

            store.ReadAsync().GetAwaiter().GetResult();
            master.Value = 2;
            store.SaveAsync().GetAwaiter().GetResult();

            Assert.That(from, Is.EqualTo(0));
            Assert.That(File(), Does.Contain("\"format\": 1").And.Not.Contain("grain").And.Not.Contain("volume"));
        }

        [Test]
        public void ReadAsync_MigrateRefuses_FileIsKeptAsideAndTheDefaultsStart()
        {
            Write("{\"format\":0,\"sound\":{\"master\":3}}");
            using var store = Store(new SettingsFileOptions { Format = 2, Migrate = (_, _) => false });
            StepSetting master = store.Steps("sound.master", 10);

            store.ReadAsync().GetAwaiter().GetResult();

            Assert.That(master.Value, Is.EqualTo(10));
            Assert.That(_disk.FindAsync(new SaveKey(Area, "settings.broken.json")).GetAwaiter().GetResult(), Is.Not.Null);
        }

        [Test]
        public void ReadAsync_NewerFormat_IsReadByIdAndWrittenWithThisBuildsFormat()
        {
            Write("{\"format\":5,\"sound\":{\"master\":2}}");
            using var store = Store();
            StepSetting master = store.Steps("sound.master", 10);

            store.ReadAsync().GetAwaiter().GetResult();
            master.Value = 1;
            store.SaveAsync().GetAwaiter().GetResult();

            Assert.That(_warnings, Has.Count.EqualTo(1));
            Assert.That(File(), Does.StartWith("{\n  \"format\": 1,"));
        }

        [Test]
        public void ReadAsync_ValuesOutsideTheirSetting_AreBroughtInside()
        {
            Write("{\"format\":1,\"sound\":{\"master\":30,\"music\":-4},\"display\":{\"frameCap\":75,\"window\":\"WINDOWED\"}}");
            using var store = Store();
            StepSetting master = store.Steps("sound.master", 10);
            StepSetting music = store.Steps("sound.music", 10);
            var display = new DisplaySettings(store);

            store.ReadAsync().GetAwaiter().GetResult();

            Assert.That(master.Value, Is.EqualTo(10));
            Assert.That(music.Value, Is.EqualTo(0));
            Assert.That(display.FrameCap.Value, Is.EqualTo(60));
            Assert.That(display.Window.Value, Is.EqualTo(WindowMode.Windowed));
        }

        [Test]
        public void ReadAsync_ValueOfTheWrongKind_OnlyThatSettingStaysDefault()
        {
            Write("{\"format\":1,\"sound\":{\"master\":\"loud\",\"music\":4}}");
            using var store = Store();
            StepSetting master = store.Steps("sound.master", 10);
            StepSetting music = store.Steps("sound.music", 10);

            store.ReadAsync().GetAwaiter().GetResult();

            Assert.That(master.Value, Is.EqualTo(10));
            Assert.That(music.Value, Is.EqualTo(4));
            Assert.That(_warnings, Has.Count.EqualTo(1).And.Some.Contain("sound.master"));
            Assert.That(_errors, Is.Empty);
        }

        [Test]
        public void SaveAsync_NothingChanged_LeavesTheFileAsItWas()
        {
            const string text = "{ \"format\": 1, \"sound\": { \"master\": 7 } }";
            Write(text);
            using var store = Store();
            StepSetting master = store.Steps("sound.master", 10);
            store.ReadAsync().GetAwaiter().GetResult();

            master.Value = 7;

            Assert.That(store.SaveAsync().GetAwaiter().GetResult(), Is.False);
            Assert.That(File(), Is.EqualTo(text));
        }

        [Test]
        public void Add_GameFormatForItsOwnType_IsKeptAndRead()
        {
            using (var store = Store())
            {
                Setting<Tint> tint = store.Add(new Setting<Tint>("look.tint", new Tint(255, 200, 0), new TintFormat()));
                store.ReadAsync().GetAwaiter().GetResult();
                tint.Value = new Tint(1, 2, 3);
                store.SaveAsync().GetAwaiter().GetResult();
            }

            using var later = Store();
            Setting<Tint> read = later.Add(new Setting<Tint>("look.tint", new Tint(255, 200, 0), new TintFormat()));
            later.ReadAsync().GetAwaiter().GetResult();

            Assert.That(File(), Does.Contain("\"tint\": \"#010203\""));
            Assert.That(read.Value, Is.EqualTo(new Tint(1, 2, 3)));
            Assert.That(read.TrySet("#0a0b0c"), Is.True);
            Assert.That(read.Text, Is.EqualTo("#0A0B0C"));
        }

        [Test]
        public void SaveAsync_TextWithQuotesBackslashesAndNewLines_ReadsBackTheSame()
        {
            const string odd = "a \"b\"\n\\c\t\u0001 é";
            using (var store = Store())
            {
                Setting<string> name = store.Text("profile.name", "");
                store.ReadAsync().GetAwaiter().GetResult();
                name.Value = odd;
                store.SaveAsync().GetAwaiter().GetResult();
            }

            using var later = Store();
            Setting<string> read = later.Text("profile.name", "");
            later.ReadAsync().GetAwaiter().GetResult();

            Assert.That(read.Value, Is.EqualTo(odd));
        }

        [Test]
        public void SaveAsync_UnderACommaCulture_NumbersHaveDots()
        {
            CultureInfo was = Thread.CurrentThread.CurrentCulture;
            Thread.CurrentThread.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
            try
            {
                using var store = Store();
                Setting<float> gamma = store.Range("picture.gamma", 1f, 0f, 2f);
                store.ReadAsync().GetAwaiter().GetResult();
                gamma.Value = 0.5f;
                store.SaveAsync().GetAwaiter().GetResult();

                Assert.That(File(), Does.Contain("\"gamma\": 0.5"));
                Assert.That(gamma.TrySet("1.5"), Is.True);
                Assert.That(gamma.Text, Is.EqualTo("1.5"));
            }
            finally
            {
                Thread.CurrentThread.CurrentCulture = was;
            }
        }

        private SettingsStore Store(SettingsFileOptions? options = null)
        {
            options ??= new SettingsFileOptions();
            options.SaveOnQuit = false;
            options.LogError = _errors.Add;
            options.LogWarning = _warnings.Add;
            return new SettingsStore(_disk, Key, options);
        }

        private void Write(string text) => _disk.WriteTextAsync(Key, text).GetAwaiter().GetResult();

        private string? File() => _disk.ReadTextAsync(Key).GetAwaiter().GetResult();

        private readonly struct Tint : System.IEquatable<Tint>
        {
            public readonly byte R;
            public readonly byte G;
            public readonly byte B;

            public Tint(byte r, byte g, byte b)
            {
                R = r;
                G = g;
                B = b;
            }

            public bool Equals(Tint other) => R == other.R && G == other.G && B == other.B;

            public override string ToString() => $"#{R:X2}{G:X2}{B:X2}";
        }

        private sealed class TintFormat : SettingFormat<Tint>
        {
            public override SettingsJsonValue Write(Tint value) => SettingsJsonValue.Of(Show(value).ToLowerInvariant());

            public override bool TryRead(SettingsJsonValue value, out Tint result)
            {
                result = default;
                return value.Kind == SettingsJsonKind.String && TryParse(value.String!, out result);
            }

            public override string Show(Tint value) => value.ToString();

            public override bool TryParse(string text, out Tint result)
            {
                result = default;
                if (text.Length != 7 || text[0] != '#' ||
                    !int.TryParse(text.Substring(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int rgb))
                {
                    return false;
                }

                result = new Tint((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
                return true;
            }
        }
    }
}
