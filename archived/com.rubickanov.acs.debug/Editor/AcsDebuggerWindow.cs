using System;
using System.Collections.Generic;
using Rubickanov.ACS.Editor;
using Rubickanov.ACS.Runtime;
using UnityEditor;
using UnityEngine;
// Unity 6 introduced UnityEngine.EntityId, which collides with the ACS handle. Alias to the ACS
// type so the unqualified name in this window always means the framework's entity id.
using EntityId = Rubickanov.ACS.Runtime.EntityId;

namespace Rubickanov.ACS.Debug
{
    /// <summary>
    /// Live inspector for every entity in the active <see cref="World"/>. The left pane lists all
    /// world-registered entities (MonoEntity, pure Entity, and the World itself); the right pane
    /// shows the selected entity's aspects and their reactive field values, updating each frame.
    /// <para/>
    /// Reuses <see cref="RuntimeAspectDrawer"/> from <c>Rubickanov.ACS.Editor</c> for the per-aspect
    /// rendering, so reactive / Subject / ObservableCollection fields and the flash-on-change
    /// highlight behave exactly as in the <c>MonoEntity</c> inspector.
    /// <para/>
    /// Editor-only: it shows nothing outside Play Mode and contributes no runtime code to a build.
    /// </summary>
    public sealed class AcsDebuggerWindow : EditorWindow
    {
        [MenuItem("Window/ACS/Debugger")]
        private static void Open()
        {
            var window = GetWindow<AcsDebuggerWindow>("ACS Debugger");
            window.minSize = new Vector2(480, 240);
        }

        private readonly RuntimeAspectDrawer _drawer = new();
        private readonly List<IEntity> _entityBuffer = new();

        private Vector2 _listScroll;
        private Vector2 _detailScroll;
        private string _filter = string.Empty;

        // Selection survives list reordering / repaints because it keys off the stable EntityId,
        // re-resolved each frame via TryFindById. A despawned entity falls out automatically.
        private ulong _selectedId;

        private void OnDisable() => _drawer.Dispose();

        // Editor windows don't poll values; drive a repaint while playing so live values tick.
        private void Update()
        {
            if (Application.isPlaying)
                Repaint();
        }

        private void OnGUI()
        {
            if (!Application.isPlaying)
            {
                EditorGUILayout.HelpBox("Enter Play Mode to inspect live entities.", MessageType.Info);
                return;
            }

            var world = World.Current;
            if (world == null)
            {
                EditorGUILayout.HelpBox(
                    "No active World. Drop a MonoWorld in the scene (or assign one via World.SetCurrent).",
                    MessageType.Warning);
                return;
            }

            DrawToolbar();

            EditorGUILayout.BeginHorizontal();
            DrawEntityList(world);
            DrawDetail(world);
            EditorGUILayout.EndHorizontal();
        }

        private void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            GUILayout.Label("Filter", GUILayout.Width(38));
            _filter = EditorGUILayout.TextField(_filter, EditorStyles.toolbarSearchField);
            EditorGUILayout.EndHorizontal();
        }

        private void DrawEntityList(World world)
        {
            EditorGUILayout.BeginVertical(GUILayout.Width(220));
            _listScroll = EditorGUILayout.BeginScrollView(_listScroll);

            _entityBuffer.Clear();
            foreach (var entity in world.Registry.AllEntities)
            {
                // MonoWorld is a MonoEntity that delegates every aspect call into the embedded
                // World — so it mirrors the "World (global)" entry exactly. Skip the wrapper and
                // show the world once. (User singletons subclassing SingletonMonoEntity are NOT
                // MonoWorld and stay visible.)
                if (entity is MonoWorld) continue;
                _entityBuffer.Add(entity);
            }

            _entityBuffer.Sort(static (a, b) => a.Id.Value.CompareTo(b.Id.Value));

            int shown = 0;
            foreach (var entity in _entityBuffer)
            {
                if (!PassesFilter(entity)) continue;
                shown++;

                bool selected = entity.Id.Value == _selectedId;
                var style = selected ? EditorStyles.boldLabel : EditorStyles.label;
                string label = $"{EntityLabel(entity)}  ·  {AspectCount(entity)}";
                if (GUILayout.Button(label, style))
                    _selectedId = entity.Id.Value;
            }

            if (shown == 0)
                EditorGUILayout.LabelField("No entities match.", EditorStyles.miniLabel);

            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        private void DrawDetail(World world)
        {
            EditorGUILayout.BeginVertical();
            _detailScroll = EditorGUILayout.BeginScrollView(_detailScroll);

            if (_selectedId != 0 && world.TryFindById(new EntityId(_selectedId), out var entity))
            {
                EditorGUILayout.LabelField(EntityLabel(entity), EditorStyles.boldLabel);
                EditorGUILayout.LabelField(entity.Id.ToString(), EditorStyles.miniLabel);
                EditorGUILayout.Space(4);
                _drawer.Draw(entity);
            }
            else
            {
                EditorGUILayout.LabelField("Select an entity from the list.", EditorStyles.miniLabel);
            }

            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        private bool PassesFilter(IEntity entity)
        {
            if (string.IsNullOrEmpty(_filter)) return true;

            if (EntityLabel(entity).IndexOf(_filter, StringComparison.OrdinalIgnoreCase) >= 0)
                return true;

            foreach (var aspectType in entity.AspectTypes)
                if (aspectType.Name.IndexOf(_filter, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;

            return false;
        }

        private static int AspectCount(IEntity entity) => entity.AspectTypes.Count;

        private static string EntityLabel(IEntity entity)
        {
            // MonoEntity is a UnityEngine.Object: a destroyed-but-still-referenced instance reads
            // as Unity-null, so guard before touching gameObject and fall back to the id.
            if (entity is MonoEntity mono && mono != null)
                return mono.gameObject.name;
            if (entity is World)
                return "World (global)";
            return entity.Id.ToString();
        }
    }
}
