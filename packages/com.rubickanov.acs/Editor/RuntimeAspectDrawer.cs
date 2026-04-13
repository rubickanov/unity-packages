using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using ObservableCollections;
using R3;
using Rubickanov.ACS.Runtime;
using UnityEditor;
using UnityEngine;

namespace Rubickanov.ACS.Editor
{
    public sealed class RuntimeAspectDrawer
    {
        private enum FieldKind { ReactiveProperty, Subject, ObservableCollection, Plain }

        // Composite (aspect-instance, field-name) key for per-field tracking.
        // Replaces a 32-bit HashCode.Combine int — two aspect-instances with the
        // same field name could theoretically collide, making EnsureSubscribed
        // skip the second subscription.
        private readonly struct SignalKey : IEquatable<SignalKey>
        {
            public readonly object Instance;
            public readonly string Field;

            public SignalKey(object instance, string field)
            {
                Instance = instance;
                Field = field;
            }

            public bool Equals(SignalKey other)
                => ReferenceEquals(Instance, other.Instance) && Field == other.Field;

            public override bool Equals(object? obj) => obj is SignalKey k && Equals(k);

            public override int GetHashCode()
                => HashCode.Combine(RuntimeHelpers.GetHashCode(Instance), Field);
        }

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

        // Lazy-init styles: EditorStyles.* is not guaranteed to be valid at type-init time,
        // so we defer construction to first access and reset on domain reload via ClearCache.
        private static GUIStyle? _dimStyle;
        private static GUIStyle? _headerStyle;
        private static GUIStyle? _nameStyle;
        private static GUIStyle? _valueStyle;

        private static GUIStyle DimStyle => _dimStyle ??= new GUIStyle(EditorStyles.miniLabel)
        {
            normal = { textColor = HeaderColor }
        };

        private static GUIStyle HeaderStyle => _headerStyle ??= new GUIStyle(EditorStyles.boldLabel)
        {
            fontSize = 11,
            normal = { textColor = HeaderColor }
        };

        private static GUIStyle NameStyle => _nameStyle ??= new GUIStyle(EditorStyles.miniLabel)
        {
            fontStyle = FontStyle.Bold,
            normal = { textColor = FieldNameColor }
        };

        // Shared mutable style for reactive/signal/plain values — color is set per-call.
        private static GUIStyle ValueStyle => _valueStyle ??= new GUIStyle(EditorStyles.miniLabel);

        private readonly Dictionary<string, bool> _foldouts = new();

        // Value change tracking: key -> (previous formatted value, last change time)
        private readonly Dictionary<SignalKey, (string prev, double changeTime)> _valueTracker = new();

        // Reused across repaints to avoid per-frame allocations.
        private readonly List<(Type type, object instance)> _aspectsBuffer = new();

        // Per-instance subscription tracker: subscriptions live with this drawer and are
        // disposed on Dispose() (OnDisable of MonoEntityEditor). On domain reload the drawer
        // itself is discarded, so GC releases subscriptions without needing a static hook.
        private readonly SignalTracker _signalTracker = new();

        [InitializeOnLoadMethod]
        private static void ClearCache()
        {
            TypeCache.Clear();
            _dimStyle = null;
            _headerStyle = null;
            _nameStyle = null;
            _valueStyle = null;
        }

        public void Draw(MonoEntity context)
        {
            if (!Application.isPlaying) return;

            _aspectsBuffer.Clear();
            foreach (object aspect in context.GetAllAspects())
                _aspectsBuffer.Add((aspect.GetType(), aspect));

            if (_aspectsBuffer.Count == 0)
            {
                EditorGUILayout.LabelField("No aspects registered", DimStyle);
                return;
            }

            EditorGUILayout.LabelField("RUNTIME DATA", HeaderStyle);

            _aspectsBuffer.Sort((a, b) => string.Compare(a.type.Name, b.type.Name, StringComparison.Ordinal));

            foreach (var (type, instance) in _aspectsBuffer)
                DrawAspect(type, instance);
        }

        public void Dispose()
        {
            _signalTracker.DisposeAll();
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

            EditorGUILayout.LabelField(cached.Field.Name, NameStyle, GUILayout.Width(160));

            switch (cached.Kind)
            {
                case FieldKind.ReactiveProperty:
                    DrawReactiveValue(cached, aspectInstance);
                    break;
                case FieldKind.Subject:
                    DrawSignalLabel(cached, aspectInstance);
                    break;
                case FieldKind.ObservableCollection:
                    DrawCollectionValue(cached, aspectInstance);
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

            var key = new SignalKey(aspectInstance, cached.Field.Name);
            Color color = GetFlashedColor(key, display, ValueColor);

            ValueStyle.normal.textColor = color;
            EditorGUILayout.LabelField(display, ValueStyle);
        }

        private void DrawSignalLabel(in CachedField cached, object aspectInstance)
        {
            // Ensure subscription exists for this Subject field
            var key = new SignalKey(aspectInstance, cached.Field.Name);
            object? subjectInstance = null;
            try { subjectInstance = cached.Field.GetValue(aspectInstance); }
            catch { /* ignore */ }

            if (subjectInstance != null)
                _signalTracker.EnsureSubscribed(key, subjectInstance);

            double flash = _signalTracker.GetFlashAmount(key);
            Color color = Color.Lerp(SignalColor, FlashColor, (float)flash);

            ValueStyle.normal.textColor = color;

            string label = flash > 0.01
                ? $"{cached.TypeLabel}  \u25cf" // bullet indicator when recently fired
                : cached.TypeLabel;

            EditorGUILayout.LabelField(label, ValueStyle);
        }

        private void DrawCollectionValue(in CachedField cached, object aspectInstance)
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
                    object? count = cached.ValueProp.GetValue(fieldObj);
                    display = $"{cached.TypeLabel} [Count={count ?? 0}]";
                }
            }
            catch
            {
                display = "<error>";
            }

            var key = new SignalKey(aspectInstance, cached.Field.Name);
            Color color = GetFlashedColor(key, display, ValueColor);

            ValueStyle.normal.textColor = color;
            EditorGUILayout.LabelField(display, ValueStyle);
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

            var key = new SignalKey(aspectInstance, cached.Field.Name);
            Color color = GetFlashedColor(key, display, PlainColor);

            ValueStyle.normal.textColor = color;
            EditorGUILayout.LabelField(display, ValueStyle);
        }

        private Color GetFlashedColor(SignalKey key, string currentDisplay, Color baseColor)
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

                    case FieldKind.ObservableCollection:
                        // All ObservableCollections types expose Count via IReadOnlyCollection<T> —
                        // look it up on the concrete field type so reflection hits the public slot
                        // directly (avoids explicit-interface lookup through the interface map).
                        valueProp = fieldType.GetProperty("Count", BindingFlags.Instance | BindingFlags.Public);
                        typeLabel = FormatTypeName(fieldType);
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
            var def = fieldType.GetGenericTypeDefinition();
            if (def == typeof(ReactiveProperty<>)) return FieldKind.ReactiveProperty;
            if (def == typeof(Subject<>)) return FieldKind.Subject;
            if (ImplementsObservableCollection(fieldType)) return FieldKind.ObservableCollection;
            return FieldKind.Plain;
        }

        private static bool ImplementsObservableCollection(Type fieldType)
        {
            var interfaces = fieldType.GetInterfaces();
            for (int i = 0; i < interfaces.Length; i++)
            {
                var iface = interfaces[i];
                if (iface.IsGenericType && iface.GetGenericTypeDefinition() == typeof(IObservableCollection<>))
                    return true;
            }
            return false;
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
            if (type.IsGenericType)
            {
                var name = type.Name;
                int tick = name.IndexOf('`');
                if (tick > 0) name = name.Substring(0, tick);
                var args = type.GetGenericArguments();
                var argNames = new string[args.Length];
                for (int i = 0; i < args.Length; i++) argNames[i] = FormatTypeName(args[i]);
                return $"{name}<{string.Join(", ", argNames)}>";
            }
            return type.Name;
        }

        /// <summary>
        /// Subscribes to Subject fields via reflection and tracks when they fire.
        /// One instance per <see cref="RuntimeAspectDrawer"/>: subscriptions are owned by the
        /// drawer and released in <see cref="DisposeAll"/>. Previously static — that leaked
        /// subscriptions across inspectors and made per-inspector cleanup clobber others.
        /// </summary>
        private sealed class SignalTracker
        {
            private readonly Dictionary<SignalKey, double> _fireTimes = new();
            private readonly Dictionary<SignalKey, IDisposable> _subscriptions = new();

            // Reflection discovery is process-level — Type/MethodInfo don't hold Subject instances,
            // so caching statically doesn't leak. Reset on domain reload happens implicitly via
            // AppDomain teardown.
            private static MethodInfo? _cachedSubscribeGeneric;

            public void DisposeAll()
            {
                foreach (var sub in _subscriptions.Values)
                {
                    try { sub.Dispose(); }
                    catch { /* ignore */ }
                }
                _subscriptions.Clear();
                _fireTimes.Clear();
            }

            public void EnsureSubscribed(SignalKey key, object subjectInstance)
            {
                if (_subscriptions.ContainsKey(key)) return;

                try
                {
                    var subjectType = subjectInstance.GetType();
                    Type? elementType = null;

                    // Walk up to find Subject<T> and extract T
                    var current = subjectType;
                    while (current != null)
                    {
                        if (current.IsGenericType && current.GetGenericTypeDefinition() == typeof(Subject<>))
                        {
                            elementType = current.GetGenericArguments()[0];
                            break;
                        }
                        current = current.BaseType;
                    }

                    if (elementType == null) return;

                    var subscribeMethod = FindSubscribeMethod(subjectType, elementType);
                    if (subscribeMethod == null) return;

                    // Create Action<T> that records fire time on THIS tracker instance.
                    var actionType = typeof(Action<>).MakeGenericType(elementType);
                    var param = Expression.Parameter(elementType, "x");
                    // One-time boxing of SignalKey at Expression build — no per-fire cost.
                    var keyConst = Expression.Constant(key, typeof(SignalKey));
                    var trackerConst = Expression.Constant(this);
                    var recordMethod = typeof(SignalTracker).GetMethod(
                        nameof(RecordFire), BindingFlags.Instance | BindingFlags.NonPublic)!;
                    var call = Expression.Call(trackerConst, recordMethod, keyConst);
                    var lambda = Expression.Lambda(actionType, call, param);
                    var action = lambda.Compile();

                    // Call Subscribe(subject, action)
                    var result = subscribeMethod.Invoke(null, new[] { subjectInstance, action });
                    if (result is IDisposable disposable)
                        _subscriptions[key] = disposable;
                }
                catch
                {
                    // Subscription failed — silently ignore, signal just won't flash
                }
            }

            public double GetFlashAmount(SignalKey key)
            {
                if (!_fireTimes.TryGetValue(key, out double fireTime)) return 0;
                double elapsed = EditorApplication.timeSinceStartup - fireTime;
                if (elapsed >= FlashDuration) return 0;
                return 1.0 - elapsed / FlashDuration;
            }

            // ReSharper disable once MemberCanBePrivate.Local — called via Expression
            private void RecordFire(SignalKey key)
            {
                _fireTimes[key] = EditorApplication.timeSinceStartup;
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
