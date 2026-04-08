using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using Rubickanov.ACS.Runtime;
using UnityEditor;
using UnityEngine;

namespace Rubickanov.ACS.Editor
{
    public sealed class RuntimeAspectDrawer
    {
        private enum FieldKind { ReactiveProperty, Subject, Plain }

        private readonly struct CachedField
        {
            public readonly FieldInfo Field;
            public readonly FieldKind Kind;
            public readonly PropertyInfo? ValueProp;
            public readonly string TypeLabel;

            public CachedField(FieldInfo field, FieldKind kind, PropertyInfo? valueProp, string typeLabel)
            {
                Field = field;
                Kind = kind;
                ValueProp = valueProp;
                TypeLabel = typeLabel;
            }
        }

        private static readonly Dictionary<Type, CachedField[]> TypeCache = new();

        private static readonly Color HeaderColor = new(1f, 1f, 1f, 0.4f);
        private static readonly Color ValueColor = new(0.4f, 0.75f, 0.45f);
        private static readonly Color SignalColor = new(0.7f, 0.5f, 0.9f);
        private static readonly Color PlainColor = new(0.8f, 0.8f, 0.8f);
        private static readonly Color FieldNameColor = new(1f, 1f, 1f, 0.6f);
        private static readonly Color FlashColor = new(1f, 1f, 1f, 1f);

        private const double FlashDuration = 0.4;

        private readonly Dictionary<string, bool> _foldouts = new();

        // Value change tracking: key -> (previous formatted value, last change time)
        private readonly Dictionary<int, (string prev, double changeTime)> _valueTracker = new();

        [InitializeOnLoadMethod]
        private static void ClearCache()
        {
            TypeCache.Clear();
            SignalTracker.ClearAll();
        }

        public void Draw(EntityContext context)
        {
            if (!Application.isPlaying) return;

            var aspects = new List<(Type type, object instance)>();
            foreach (object aspect in context.GetAllAspects())
                aspects.Add((aspect.GetType(), aspect));

            if (aspects.Count == 0)
            {
                var dimStyle = new GUIStyle(EditorStyles.miniLabel)
                {
                    normal = { textColor = HeaderColor }
                };
                EditorGUILayout.LabelField("No aspects registered", dimStyle);
                return;
            }

            var headerStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 11,
                normal = { textColor = HeaderColor }
            };
            EditorGUILayout.LabelField("RUNTIME DATA", headerStyle);

            aspects.Sort((a, b) => string.Compare(a.type.Name, b.type.Name, StringComparison.Ordinal));

            foreach (var (type, instance) in aspects)
                DrawAspect(type, instance);
        }

        public void Dispose()
        {
            SignalTracker.ClearAll();
            _valueTracker.Clear();
        }

        private void DrawAspect(Type type, object instance)
        {
            string name = type.Name;
            if (!_foldouts.ContainsKey(name))
                _foldouts[name] = false;

            _foldouts[name] = EditorGUILayout.Foldout(_foldouts[name], name, true, EditorStyles.foldoutHeader);
            if (!_foldouts[name]) return;

            var fields = GetCachedFields(type);

            EditorGUI.indentLevel++;

            foreach (ref readonly var cached in fields.AsSpan())
                DrawField(cached, instance);

            EditorGUI.indentLevel--;
            EditorGUILayout.Space(2);
        }

        private void DrawField(in CachedField cached, object aspectInstance)
        {
            EditorGUILayout.BeginHorizontal();

            var nameStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                fontStyle = FontStyle.Bold,
                normal = { textColor = FieldNameColor }
            };
            EditorGUILayout.LabelField(cached.Field.Name, nameStyle, GUILayout.Width(160));

            switch (cached.Kind)
            {
                case FieldKind.ReactiveProperty:
                    DrawReactiveValue(cached, aspectInstance);
                    break;
                case FieldKind.Subject:
                    DrawSignalLabel(cached, aspectInstance);
                    break;
                case FieldKind.Plain:
                    DrawPlainValue(cached, aspectInstance);
                    break;
            }

            EditorGUILayout.EndHorizontal();
        }

        private void DrawReactiveValue(in CachedField cached, object aspectInstance)
        {
            string display;
            try
            {
                object? fieldObj = cached.Field.GetValue(aspectInstance);
                if (fieldObj == null || cached.ValueProp == null)
                {
                    display = "null";
                }
                else
                {
                    object? value = cached.ValueProp.GetValue(fieldObj);
                    display = FormatValue(value);
                }
            }
            catch
            {
                display = "<error>";
            }

            int key = MakeKey(aspectInstance, cached.Field.Name);
            Color color = GetFlashedColor(key, display, ValueColor);

            var style = new GUIStyle(EditorStyles.miniLabel) { normal = { textColor = color } };
            EditorGUILayout.LabelField(display, style);
        }

        private void DrawSignalLabel(in CachedField cached, object aspectInstance)
        {
            // Ensure subscription exists for this Subject field
            int key = MakeKey(aspectInstance, cached.Field.Name);
            object? subjectInstance = null;
            try { subjectInstance = cached.Field.GetValue(aspectInstance); }
            catch { /* ignore */ }

            if (subjectInstance != null)
                SignalTracker.EnsureSubscribed(key, subjectInstance);

            double flash = SignalTracker.GetFlashAmount(key);
            Color color = Color.Lerp(SignalColor, FlashColor, (float)flash);

            var style = new GUIStyle(EditorStyles.miniLabel) { normal = { textColor = color } };

            string label = flash > 0.01
                ? $"{cached.TypeLabel}  \u25cf" // bullet indicator when recently fired
                : cached.TypeLabel;

            EditorGUILayout.LabelField(label, style);
        }

        private void DrawPlainValue(in CachedField cached, object aspectInstance)
        {
            string display;
            try
            {
                object? value = cached.Field.GetValue(aspectInstance);
                display = FormatValue(value);
            }
            catch
            {
                display = "<error>";
            }

            int key = MakeKey(aspectInstance, cached.Field.Name);
            Color color = GetFlashedColor(key, display, PlainColor);

            var style = new GUIStyle(EditorStyles.miniLabel) { normal = { textColor = color } };
            EditorGUILayout.LabelField(display, style);
        }

        private Color GetFlashedColor(int key, string currentDisplay, Color baseColor)
        {
            double now = EditorApplication.timeSinceStartup;

            if (_valueTracker.TryGetValue(key, out var tracked))
            {
                if (tracked.prev != currentDisplay)
                    _valueTracker[key] = (currentDisplay, now);
            }
            else
            {
                _valueTracker[key] = (currentDisplay, 0);
                return baseColor;
            }

            double elapsed = now - _valueTracker[key].changeTime;
            if (elapsed >= FlashDuration) return baseColor;

            float t = (float)(elapsed / FlashDuration);
            return Color.Lerp(FlashColor, baseColor, t);
        }

        private static int MakeKey(object instance, string fieldName)
        {
            return HashCode.Combine(RuntimeHelpers.GetHashCode(instance), fieldName);
        }

        private static string FormatValue(object? value)
        {
            if (value == null) return "null";

            if (value is UnityEngine.Object uObj)
                return uObj != null ? uObj.name : "null (destroyed)";

            return value switch
            {
                float f => f.ToString("F2"),
                double d => d.ToString("F2"),
                Vector2 v => v.ToString("F2"),
                Vector3 v => v.ToString("F2"),
                Vector4 v => v.ToString("F2"),
                Quaternion q => q.eulerAngles.ToString("F1"),
                Color c => $"({c.r:F2}, {c.g:F2}, {c.b:F2}, {c.a:F2})",
                bool b => b ? "True" : "False",
                _ => value.ToString() ?? "null"
            };
        }

        private static CachedField[] GetCachedFields(Type aspectType)
        {
            if (TypeCache.TryGetValue(aspectType, out var cached))
                return cached;

            var fields = aspectType.GetFields(BindingFlags.Instance | BindingFlags.Public);
            var result = new List<CachedField>(fields.Length);

            foreach (var field in fields)
            {
                var fieldType = field.FieldType;
                var kind = ClassifyField(fieldType);
                PropertyInfo? valueProp = null;
                string typeLabel;

                switch (kind)
                {
                    case FieldKind.ReactiveProperty:
                        valueProp = fieldType.GetProperty("Value", BindingFlags.Instance | BindingFlags.Public);
                        var innerType = fieldType.IsGenericType ? fieldType.GetGenericArguments()[0] : null;
                        typeLabel = innerType != null ? $"Reactive<{FormatTypeName(innerType)}>" : "Reactive";
                        break;

                    case FieldKind.Subject:
                        var signalType = fieldType.IsGenericType ? fieldType.GetGenericArguments()[0] : null;
                        typeLabel = signalType != null ? $"Signal<{FormatTypeName(signalType)}>" : "Signal";
                        break;

                    default:
                        typeLabel = FormatTypeName(fieldType);
                        break;
                }

                result.Add(new CachedField(field, kind, valueProp, typeLabel));
            }

            var array = result.ToArray();
            TypeCache[aspectType] = array;
            return array;
        }

        private static FieldKind ClassifyField(Type fieldType)
        {
            if (!fieldType.IsGenericType) return FieldKind.Plain;
            string name = fieldType.GetGenericTypeDefinition().Name;
            if (name.StartsWith("ReactiveProperty")) return FieldKind.ReactiveProperty;
            if (name.StartsWith("Subject")) return FieldKind.Subject;
            return FieldKind.Plain;
        }

        private static string FormatTypeName(Type type)
        {
            if (type == typeof(int)) return "int";
            if (type == typeof(float)) return "float";
            if (type == typeof(double)) return "double";
            if (type == typeof(bool)) return "bool";
            if (type == typeof(string)) return "string";
            if (type == typeof(Vector2)) return "Vector2";
            if (type == typeof(Vector3)) return "Vector3";
            if (type == typeof(Vector4)) return "Vector4";
            if (type == typeof(Quaternion)) return "Quaternion";
            if (type == typeof(Color)) return "Color";
            return type.Name;
        }

        /// <summary>
        /// Subscribes to Subject fields via reflection and tracks when they fire.
        /// </summary>
        private static class SignalTracker
        {
            private static readonly Dictionary<int, double> FireTimes = new();
            private static readonly HashSet<int> Subscribed = new();
            private static readonly List<IDisposable> Subscriptions = new();
            private static MethodInfo? _cachedSubscribeGeneric;

            public static void ClearAll()
            {
                foreach (var sub in Subscriptions)
                {
                    try { sub.Dispose(); }
                    catch { /* ignore */ }
                }
                Subscriptions.Clear();
                Subscribed.Clear();
                FireTimes.Clear();
                _cachedSubscribeGeneric = null;
            }

            public static void EnsureSubscribed(int key, object subjectInstance)
            {
                if (Subscribed.Contains(key)) return;
                Subscribed.Add(key);

                try
                {
                    var subjectType = subjectInstance.GetType();
                    Type? elementType = null;

                    // Walk up to find Observable<T> and extract T
                    var current = subjectType;
                    while (current != null)
                    {
                        if (current.IsGenericType && current.GetGenericTypeDefinition().Name.StartsWith("Subject"))
                        {
                            elementType = current.GetGenericArguments()[0];
                            break;
                        }
                        current = current.BaseType;
                    }

                    if (elementType == null) return;

                    var subscribeMethod = FindSubscribeMethod(subjectType, elementType);
                    if (subscribeMethod == null) return;

                    // Create Action<T> that records fire time
                    var actionType = typeof(Action<>).MakeGenericType(elementType);
                    var param = Expression.Parameter(elementType, "x");
                    var keyConst = Expression.Constant(key);
                    var recordMethod = typeof(SignalTracker).GetMethod(
                        nameof(RecordFire), BindingFlags.Static | BindingFlags.NonPublic)!;
                    var call = Expression.Call(null, recordMethod, keyConst);
                    var lambda = Expression.Lambda(actionType, call, param);
                    var action = lambda.Compile();

                    // Call Subscribe(subject, action)
                    var result = subscribeMethod.Invoke(null, new[] { subjectInstance, action });
                    if (result is IDisposable disposable)
                        Subscriptions.Add(disposable);
                }
                catch
                {
                    // Subscription failed — silently ignore, signal just won't flash
                }
            }

            public static double GetFlashAmount(int key)
            {
                if (!FireTimes.TryGetValue(key, out double fireTime)) return 0;
                double elapsed = EditorApplication.timeSinceStartup - fireTime;
                if (elapsed >= FlashDuration) return 0;
                return 1.0 - elapsed / FlashDuration;
            }

            // ReSharper disable once MemberCanBePrivate.Local — called via Expression
            internal static void RecordFire(int key)
            {
                FireTimes[key] = EditorApplication.timeSinceStartup;
            }

            private static MethodInfo? FindSubscribeMethod(Type subjectType, Type elementType)
            {
                // Cache the generic Subscribe<T>(Observable<T>, Action<T>) method
                if (_cachedSubscribeGeneric != null)
                    return _cachedSubscribeGeneric.MakeGenericMethod(elementType);

                var assembly = subjectType.Assembly;
                foreach (var type in assembly.GetExportedTypes())
                {
                    if (!type.IsAbstract || !type.IsSealed) continue; // static classes only
                    foreach (var method in type.GetMethods(BindingFlags.Static | BindingFlags.Public))
                    {
                        if (method.Name != "Subscribe" || !method.IsGenericMethodDefinition) continue;

                        var genericArgs = method.GetGenericArguments();
                        if (genericArgs.Length != 1) continue;

                        var parameters = method.GetParameters();
                        if (parameters.Length != 2) continue;

                        var p1 = parameters[1].ParameterType;
                        if (!p1.IsGenericType) continue;
                        if (p1.GetGenericTypeDefinition() != typeof(Action<>)) continue;

                        _cachedSubscribeGeneric = method;
                        return method.MakeGenericMethod(elementType);
                    }
                }

                return null;
            }
        }
    }
}
