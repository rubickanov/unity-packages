using System.Collections.Generic;
using System.Text;
using ObservableCollections;
using R3;
using Rubickanov.ACS.Runtime;
using Rubickanov.ACS.Runtime.Persistence;
using UnityEngine;

namespace Experiments
{
    /// <summary>
    /// Solo (no networking) experiment for acs.persistence. Drop on any empty
    /// GameObject in a scene and enter Play Mode. Buttons in the HUD exercise
    /// the Snapshot/Restore round-trip against a local World.
    /// </summary>
    public class PersistenceExperiment : MonoBehaviour
    {
        public class HeroStatsAspect : IEntityAspect
        {
            [PersistedState] public readonly ReactiveProperty<int> Health = new(100);
            [PersistedState] public readonly ReactiveProperty<string> Name = new("Arthur");
            [PersistedState] public readonly ObservableList<string> Inventory = new();
            [PersistedState] public readonly ObservableDictionary<string, float> Cooldowns = new();

            // Runtime-only: проверяем что поле без атрибута в снапшот не уезжает.
            public readonly ReactiveProperty<bool> IsInCombat = new(false);
        }

        public class WorldClockAspect : IEntityAspect
        {
            [PersistedState] public readonly ReactiveProperty<float> TimeOfDay = new(8f);
        }

        private World _world = default!;
        private IEntity _hero = default!;
        private HeroStatsAspect _heroStats = default!;
        private WorldClockAspect _worldClock = default!;

        private AspectSnapshot _heroSnapshot;
        private AspectSnapshot _worldSnapshot;
        private string _log = "";

        private void Start()
        {
            _world = new World();
            _worldClock = ((IEntity)_world).Require<WorldClockAspect>();

            _hero = new Entity(_world);
            _heroStats = _hero.Require<HeroStatsAspect>();
            _heroStats.Inventory.Add("sword");
            _heroStats.Inventory.Add("potion");
            _heroStats.Cooldowns["fireball"] = 3f;

            AppendLog("World ready. Hero at default state.");
        }

        private void OnDestroy()
        {
            _world?.Dispose();
        }

        private void OnGUI()
        {
            const float width = 420f;
            const float height = 520f;
            const float margin = 10f;
            var panel = new Rect(Screen.width - width - margin, Screen.height - height - margin, width, height);
            GUI.Box(panel, "ACS Persistence Experiment");

            GUILayout.BeginArea(new Rect(panel.x + 10, panel.y + 25, panel.width - 20, panel.height - 35));

            GUILayout.Label("Live state", EditorLikeHeader());
            GUILayout.Label(DumpLiveState());

            GUILayout.Space(4);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Mutate")) Mutate();
            if (GUILayout.Button("Snapshot")) Capture();
            if (GUILayout.Button("Restore")) RestoreInPlace();
            if (GUILayout.Button("New World + Load")) RebuildAndLoad();
            GUILayout.EndHorizontal();

            GUILayout.Space(6);
            GUILayout.Label("Captured snapshot", EditorLikeHeader());
            GUILayout.Label(DumpSnapshot(_heroSnapshot, "Hero") + DumpSnapshot(_worldSnapshot, "World"));

            GUILayout.Space(6);
            GUILayout.Label("Log", EditorLikeHeader());
            GUILayout.Label(_log);

            GUILayout.EndArea();
        }

        private void Mutate()
        {
            _heroStats.Health.Value = Random.Range(1, 100);
            _heroStats.Name.Value = "Hero_" + Random.Range(0, 1000);
            _heroStats.Inventory.Add("loot_" + Random.Range(0, 100));
            _heroStats.Cooldowns["fireball"] = Random.Range(0.5f, 10f);
            _heroStats.IsInCombat.Value = !_heroStats.IsInCombat.Value;
            _worldClock.TimeOfDay.Value = Random.Range(0f, 24f);
            AppendLog("Mutated hero + world clock.");
        }

        private void Capture()
        {
            _heroSnapshot = _hero.Snapshot();
            _worldSnapshot = ((IEntity)_world).Snapshot();
            AppendLog($"Captured: hero aspects={_heroSnapshot.Aspects.Count}, world aspects={_worldSnapshot.Aspects.Count}.");
        }

        private void RestoreInPlace()
        {
            if (_heroSnapshot == null || _worldSnapshot == null)
            {
                AppendLog("Nothing captured yet.");
                return;
            }

            _hero.Restore(_heroSnapshot);
            ((IEntity)_world).Restore(_worldSnapshot);
            AppendLog("Restored into existing entities.");
        }

        private void RebuildAndLoad()
        {
            if (_heroSnapshot == null || _worldSnapshot == null)
            {
                AppendLog("Snapshot first, then rebuild.");
                return;
            }

            _world.Dispose();
            _world = new World();
            ((IEntity)_world).Restore(_worldSnapshot);
            _worldClock = ((IEntity)_world).Require<WorldClockAspect>();

            _hero = new Entity(_world);
            _hero.Restore(_heroSnapshot);
            _heroStats = _hero.Require<HeroStatsAspect>();

            AppendLog("Fresh World+Entity built from snapshot (simulated load).");
        }

        private string DumpLiveState()
        {
            var sb = new StringBuilder();
            sb.Append("Hero.Health     = ").Append(_heroStats.Health.Value).AppendLine();
            sb.Append("Hero.Name       = ").Append(_heroStats.Name.Value).AppendLine();
            sb.Append("Hero.Inventory  = [").Append(string.Join(", ", _heroStats.Inventory)).Append(']').AppendLine();
            sb.Append("Hero.Cooldowns  = {");
            bool first = true;
            foreach (var kv in _heroStats.Cooldowns)
            {
                if (!first) sb.Append(", ");
                sb.Append(kv.Key).Append('=').Append(kv.Value.ToString("F1"));
                first = false;
            }

            sb.Append('}').AppendLine();
            sb.Append("Hero.IsInCombat = ").Append(_heroStats.IsInCombat.Value).Append(" (runtime-only)").AppendLine();
            sb.Append("World.TimeOfDay = ").Append(_worldClock.TimeOfDay.Value.ToString("F2"));
            return sb.ToString();
        }

        private static string DumpSnapshot(AspectSnapshot snap, string label)
        {
            if (snap == null) return $"[{label}] —\n";
            var sb = new StringBuilder();
            sb.Append('[').Append(label).Append(']');
            if (snap.IsEmpty)
            {
                sb.AppendLine(" (empty)");
                return sb.ToString();
            }

            sb.AppendLine();
            foreach (var kv in snap.Aspects)
            {
                var shortName = ShortName(kv.Key);
                sb.Append("  ").Append(shortName).AppendLine();
                foreach (var field in kv.Value.Fields)
                    sb.Append("    ").Append(field.Key).Append(" = ").Append(Format(field.Value)).AppendLine();
            }

            return sb.ToString();
        }

        private static string Format(object value)
        {
            if (value == null) return "null";
            if (value is string s) return '"' + s + '"';
            if (value is System.Collections.IEnumerable seq && !(value is string))
            {
                var parts = new List<string>();
                foreach (var item in seq) parts.Add(Format(item));
                return "[" + string.Join(", ", parts) + "]";
            }

            return value.ToString();
        }

        private static string ShortName(string fullName)
        {
            int dot = fullName.LastIndexOf('.');
            return dot >= 0 ? fullName.Substring(dot + 1) : fullName;
        }

        private void AppendLog(string line)
        {
            _log = line + "\n" + _log;
            if (_log.Length > 400) _log = _log.Substring(0, 400);
        }

        private static GUIStyle EditorLikeHeader()
        {
            var s = new GUIStyle(GUI.skin.label)
            {
                fontStyle = FontStyle.Bold
            };
            return s;
        }
    }
}
