using System;
using System.Collections.Generic;
using Rubickanov.Log;
#if ENABLE_PROFILER
using Unity.Profiling;
#endif

namespace Rubickanov.App
{
    /// <summary>
    /// What one scope's services live by. The scope makes them by hand and tells this what of theirs runs and when:
    /// <see cref="Initialize"/> at once, <see cref="TimeTick"/> first in every frame, before Unity takes its time,
    /// <see cref="EarlyTick"/> every frame at its start, before any scope's starts, <see cref="Start"/> once at the
    /// start of the next frame, <see cref="Tick"/> every frame before the scripts' Update, <see cref="FixedTick"/> every
    /// physics step before their FixedUpdate, <see cref="PrePhysicsTick"/> every physics step after their FixedUpdate,
    /// right before the simulation, <see cref="PhysicsTick"/> every physics step after the simulation,
    /// <see cref="PreLateTick"/> every frame before their LateUpdate and <see cref="LateTick"/> after it
    /// (<see cref="FrameLoop"/>), each in the order given. <see cref="Own"/> takes a service to dispose with the scope,
    /// the last taken first. A step that throws is logged and the others go on, in disposal too. A tick that throws is
    /// still called every time (a skipped tick would stop its service for good); only its first exception of each kind
    /// is logged.
    /// </summary>
    public sealed class Lifecycle : IDisposable
    {
        private static readonly LogChannel Log = LogChannel.Get("App");

        private readonly Steps _timeTicks = new(FrameLoop.TimeTickSystem);
        private readonly Steps _earlyTicks = new(FrameLoop.StartSystem);
        private readonly List<Action> _starts = new();
        private readonly Steps _ticks = new(FrameLoop.TickSystem);
        private readonly Steps _fixedTicks = new(FrameLoop.FixedTickSystem);
        private readonly Steps _prePhysicsTicks = new(FrameLoop.PrePhysicsTickSystem);
        private readonly Steps _physicsTicks = new(FrameLoop.PhysicsTickSystem);
        private readonly Steps _preLateTicks = new(FrameLoop.PreLateTickSystem);
        private readonly Steps _lateTicks = new(FrameLoop.LateTickSystem);
        private readonly List<IDisposable> _owned = new();
        private readonly HashSet<(Action, Type)> _thrown = new();

        public Lifecycle() => FrameLoop.Join(this);

        public bool Disposed { get; private set; }

        /// <summary>Disposes <paramref name="service"/> with the scope; returns it.</summary>
        public T Own<T>(T service) where T : IDisposable
        {
            _owned.Add(service);
            return service;
        }

        /// <summary>Runs the steps now.</summary>
        public void Initialize(params Action[] steps)
        {
            foreach (Action step in steps)
            {
                Run(step);
            }
        }

        /// <summary>Runs the steps first in every frame, before Unity takes the frame's time: only the game clock.</summary>
        public void TimeTick(params Action[] steps) => _timeTicks.Add(steps);

        /// <summary>Runs the steps every frame at its start, before every scope's starts.</summary>
        public void EarlyTick(params Action[] steps) => _earlyTicks.Add(steps);

        /// <summary>Runs the steps once, at the start of the next frame.</summary>
        public void Start(params Action[] steps) => _starts.AddRange(steps);

        /// <summary>Runs the steps every frame, before the scripts' Update.</summary>
        public void Tick(params Action[] steps) => _ticks.Add(steps);

        /// <summary>Runs the steps every physics step, before the scripts' FixedUpdate.</summary>
        public void FixedTick(params Action[] steps) => _fixedTicks.Add(steps);

        /// <summary>Runs the steps every physics step, after the scripts' FixedUpdate, right before the simulation.</summary>
        public void PrePhysicsTick(params Action[] steps) => _prePhysicsTicks.Add(steps);

        /// <summary>Runs the steps every physics step, right after the physics simulated it.</summary>
        public void PhysicsTick(params Action[] steps) => _physicsTicks.Add(steps);

        /// <summary>Runs the steps every frame, before the scripts' LateUpdate.</summary>
        public void PreLateTick(params Action[] steps) => _preLateTicks.Add(steps);

        /// <summary>Runs the steps every frame, after the scripts' LateUpdate.</summary>
        public void LateTick(params Action[] steps) => _lateTicks.Add(steps);

        public void Dispose()
        {
            if (Disposed)
            {
                return;
            }

            Disposed = true;
            FrameLoop.Leave(this);
            for (int i = _owned.Count - 1; i >= 0; i--)
            {
                Run(_owned[i].Dispose);
            }

            _owned.Clear();
            _timeTicks.Clear();
            _earlyTicks.Clear();
            _starts.Clear();
            _ticks.Clear();
            _fixedTicks.Clear();
            _prePhysicsTicks.Clear();
            _physicsTicks.Clear();
            _preLateTicks.Clear();
            _lateTicks.Clear();
            _thrown.Clear();
        }

        internal void RunStarts()
        {
            if (_starts.Count == 0)
            {
                return;
            }

            Action[] starts = _starts.ToArray();
            _starts.Clear();
            foreach (Action start in starts)
            {
                if (Disposed)
                {
                    return;
                }

                Run(start);
            }
        }

        internal void RunTimeTicks() => RunEach(_timeTicks);

        internal void RunEarlyTicks() => RunEach(_earlyTicks);

        internal void RunTicks() => RunEach(_ticks);

        internal void RunFixedTicks() => RunEach(_fixedTicks);

        internal void RunPrePhysicsTicks() => RunEach(_prePhysicsTicks);

        internal void RunPhysicsTicks() => RunEach(_physicsTicks);

        internal void RunPreLateTicks() => RunEach(_preLateTicks);

        internal void RunLateTicks() => RunEach(_lateTicks);

        /// <summary>Every tick step of this scope with the player loop system it runs under (profiler builds only).</summary>
        internal void Names(List<(string Step, string System)> into)
        {
            _timeTicks.Names(into);
            _earlyTicks.Names(into);
            _ticks.Names(into);
            _fixedTicks.Names(into);
            _prePhysicsTicks.Names(into);
            _physicsTicks.Names(into);
            _preLateTicks.Names(into);
            _lateTicks.Names(into);
        }

        private void RunEach(Steps steps)
        {
            List<Action> run = steps.Run;
            for (int i = 0; i < run.Count && !Disposed; i++)
            {
                Action step = run[i];
                try
                {
#if ENABLE_PROFILER
                    using (steps.Markers[i].Auto())
                    {
                        step();
                    }
#else
                    step();
#endif
                }
                catch (Exception exception)
                {
                    if (_thrown.Add((step, exception.GetType())))
                    {
                        Log.Exception(exception);
                    }
                }
            }
        }

        /// <summary>
        /// <see cref="FrameLoop.MarkerPrefix"/> + the step's type and method: a method group by its declaring type, a
        /// lambda by the type and method that made it with a "~" (<c>&lt;Schedule&gt;b__12_0</c> → <c>Schedule~</c>).
        /// </summary>
        internal static string NameOf(Action step)
        {
            System.Reflection.MethodInfo method = step.Method;
            Type type = method.DeclaringType;
            while (type != null && type.Name.StartsWith("<", StringComparison.Ordinal) && type.DeclaringType != null)
            {
                type = type.DeclaringType;
            }

            string name = method.Name;
            if (name.StartsWith("<", StringComparison.Ordinal))
            {
                int end = name.IndexOf('>');
                name = (end > 1 ? name.Substring(1, end - 1) : "lambda") + "~";
            }

            return FrameLoop.MarkerPrefix + (type != null ? type.Name : "?") + "." + name;
        }

        // One tick list: its steps and, in profiler builds, a marker for each, named after its service and method.
        // Markers are made once here, never per frame; one name twice is one marker, summed.
        private sealed class Steps
        {
            public readonly List<Action> Run = new();
            private readonly string _system;
#if ENABLE_PROFILER
            public readonly List<ProfilerMarker> Markers = new();
            private readonly List<string> _labels = new();
#endif

            public Steps(string system) => _system = system;

            public void Add(Action[] steps)
            {
                Run.AddRange(steps);
#if ENABLE_PROFILER
                foreach (Action step in steps)
                {
                    string name = NameOf(step);
                    _labels.Add(name);
                    Markers.Add(new ProfilerMarker(ProfilerCategory.Scripts, name));
                }
#endif
            }

            public void Clear()
            {
                Run.Clear();
#if ENABLE_PROFILER
                Markers.Clear();
                _labels.Clear();
#endif
            }

            public void Names(List<(string Step, string System)> into)
            {
#if ENABLE_PROFILER
                foreach (string label in _labels)
                {
                    into.Add((label, _system));
                }
#endif
            }
        }

        private static void Run(Action step)
        {
            try
            {
                step();
            }
            catch (Exception exception)
            {
                Log.Exception(exception);
            }
        }
    }
}
