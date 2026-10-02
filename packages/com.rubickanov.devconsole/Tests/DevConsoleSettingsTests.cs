using System.Text.RegularExpressions;
using NUnit.Framework;
using Rubickanov.DevConsole.Editor;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.TestTools;

namespace Rubickanov.DevConsole.Tests
{
    [TestFixture]
    public class DevConsoleSettingsTests
    {
        private const string Folder = "Assets/DevConsoleSettingsTests";

        [TearDown]
        public void TearDown()
        {
            AssetDatabase.DeleteAsset(Folder);
            DevConsoleSettings.ResetStatics();
        }

        [Test]
        public void FromJson_SavedValues_OverrideTheDefaults()
        {
            var json = $"{{\"useBuiltInToggle\":false,\"toggleKey\":{(int)Key.F2},\"consoleHeight\":0.7}}";

            var settings = DevConsoleSettings.FromJson(json);

            Assert.IsFalse(settings.UseBuiltInToggle);
            Assert.AreEqual(Key.F2, settings.ToggleKey);
            Assert.AreEqual(0.7f, settings.ConsoleHeight, 1e-5f);
        }

        [Test]
        public void FromJson_NoFile_GivesTheDefaults()
        {
            var settings = DevConsoleSettings.FromJson(null);

            Assert.IsTrue(settings.UseBuiltInToggle);
            Assert.AreEqual(Key.Backquote, settings.ToggleKey);
            Assert.AreEqual(0.4f, settings.ConsoleHeight, 1e-5f);
        }

        [Test]
        public void FromJson_BrokenFile_WarnsAndGivesTheDefaults()
        {
            LogAssert.Expect(LogType.Warning, new Regex("could not be read"));

            var settings = DevConsoleSettings.FromJson("{ toggleKey: ");

            Assert.AreEqual(Key.Backquote, settings.ToggleKey);
        }

        [Test]
        public void DefaultAssetPath_IsInAResourcesFolderOfTheProject()
        {
            StringAssert.StartsWith("Assets/", DevConsoleSettingsStore.DefaultAssetPath);
            StringAssert.Contains("/Resources/", DevConsoleSettingsStore.DefaultAssetPath);
        }

        [Test]
        public void GetOrCreate_FileTheEditorSaved_IsReadFromResources()
        {
            var edited = DevConsoleSettings.FromJson($"{{\"toggleKey\":{(int)Key.F2},\"consoleHeight\":0.7}}");
            DevConsoleSettingsStore.Save(edited, Folder + "/Resources/" + DevConsoleSettings.ResourceName + ".json");
            DevConsoleSettings.ResetStatics();

            var settings = DevConsoleSettings.GetOrCreate();

            Assert.AreEqual(Key.F2, settings.ToggleKey);
            Assert.AreEqual(0.7f, settings.ConsoleHeight, 1e-5f);
        }
    }
}
