using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.LowLevel;
using UnityEngine.PlayerLoop;

namespace Rubickanov.App
{
    /// <summary>
    /// The game's places in Unity's frame, put into the player loop before anything loads. Time tick: first in the
    /// frame, before Unity takes the frame's time, so a time scale set there acts in this frame. Start: at the start of
    /// the frame, before Unity starts the objects made the frame before; every scope's early ticks run there first, then
    /// every scope's starts. Tick: just before the scripts' Update. Fixed tick: every physics step, just before the
    /// scripts' FixedUpdate. Pre-physics tick: every physics step, after the scripts' FixedUpdate, right before the
    /// simulation. Physics tick: every physics step, right after the physics simulated it. Pre-late tick: just before the
    /// scripts' LateUpdate. Late tick: after all of Unity's LateUpdate, before the frame is drawn. Each runs the live
    /// <see cref="Lifecycle"/>s in the order they joined: the app's first, then the scene's.
    /// </summary>
    public static class FrameLoop
    {
        private static Lifecycle[] _lives = Array.Empty<Lifecycle>();

        // The profiler names of the systems: their types' names.
        internal const string TimeTickSystem = nameof(GameTimeTick);
        internal const string StartSystem = nameof(GameStart);
        internal const string TickSystem = nameof(GameTick);
        internal const string FixedTickSystem = nameof(GameFixedTick);
        internal const string PrePhysicsTickSystem = nameof(GamePrePhysicsTick);
        internal const string PhysicsTickSystem = nameof(GamePhysicsTick);
        internal const string PreLateTickSystem = nameof(GamePreLateTick);
        internal const string LateTickSystem = nameof(GameLateTick);

        /// <summary>
        /// What every tick step's profiler marker starts with, before its type and method ("Game.Waves.Step"). Set it
        /// before the scopes are made: markers are named when the steps are given.
        /// </summary>
        public static string MarkerPrefix { get; set; } = "Game.";

        /// <summary>
        /// Every tick step's marker of the scopes alive now with the player loop system it runs under, the app's first
        /// (profiler builds; empty in release, which has no markers).
        /// </summary>
        public static List<(string Step, string System)> Names()
        {
            var names = new List<(string Step, string System)>();
            foreach (Lifecycle life in _lives)
            {
                life.Names(names);
            }

            return names;
        }

        // A tick may make or end a scope: a run goes over the list it started with.
        internal static void Join(Lifecycle life)
        {
            var lives = new Lifecycle[_lives.Length + 1];
            _lives.CopyTo(lives, 0);
            lives[^1] = life;
            _lives = lives;
        }

        internal static void Leave(Lifecycle life)
        {
            int index = Array.IndexOf(_lives, life);
            if (index < 0)
            {
                return;
            }

            var lives = new Lifecycle[_lives.Length - 1];
            Array.Copy(_lives, 0, lives, 0, index);
            Array.Copy(_lives, index + 1, lives, index, lives.Length - index);
            _lives = lives;
        }

        internal static void RunTimeTicks()
        {
            foreach (Lifecycle life in _lives)
            {
                life.RunTimeTicks();
            }
        }

        internal static void RunStarts()
        {
            foreach (Lifecycle life in _lives)
            {
                life.RunEarlyTicks();
            }

            foreach (Lifecycle life in _lives)
            {
                life.RunStarts();
            }
        }

        internal static void RunTicks()
        {
            foreach (Lifecycle life in _lives)
            {
                life.RunTicks();
            }
        }

        internal static void RunFixedTicks()
        {
            foreach (Lifecycle life in _lives)
            {
                life.RunFixedTicks();
            }
        }

        internal static void RunPrePhysicsTicks()
        {
            foreach (Lifecycle life in _lives)
            {
                life.RunPrePhysicsTicks();
            }
        }

        internal static void RunPhysicsTicks()
        {
            foreach (Lifecycle life in _lives)
            {
                life.RunPhysicsTicks();
            }
        }

        internal static void RunPreLateTicks()
        {
            foreach (Lifecycle life in _lives)
            {
                life.RunPreLateTicks();
            }
        }

        internal static void RunLateTicks()
        {
            foreach (Lifecycle life in _lives)
            {
                life.RunLateTicks();
            }
        }

        // Without a domain reload the last session's systems and lives are still there: both are put in afresh.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Install()
        {
            _lives = Array.Empty<Lifecycle>();
            PlayerLoopSystem loop = PlayerLoop.GetCurrentPlayerLoop();
            Install(ref loop);
            PlayerLoop.SetPlayerLoop(loop);
        }

        internal static void Install(ref PlayerLoopSystem loop)
        {
            Insert(ref loop, typeof(TimeUpdate), typeof(TimeUpdate.WaitForLastPresentationAndUpdateTime),
                new PlayerLoopSystem { type = typeof(GameTimeTick), updateDelegate = RunTimeTicks });
            Insert(ref loop, typeof(EarlyUpdate), typeof(EarlyUpdate.ScriptRunDelayedStartupFrame),
                new PlayerLoopSystem { type = typeof(GameStart), updateDelegate = RunStarts });
            Insert(ref loop, typeof(Update), typeof(Update.ScriptRunBehaviourUpdate),
                new PlayerLoopSystem { type = typeof(GameTick), updateDelegate = RunTicks });
            Insert(ref loop, typeof(FixedUpdate), typeof(FixedUpdate.ScriptRunBehaviourFixedUpdate),
                new PlayerLoopSystem { type = typeof(GameFixedTick), updateDelegate = RunFixedTicks });
            Insert(ref loop, typeof(FixedUpdate), typeof(FixedUpdate.PhysicsFixedUpdate),
                new PlayerLoopSystem { type = typeof(GamePrePhysicsTick), updateDelegate = RunPrePhysicsTicks });
            Insert(ref loop, typeof(FixedUpdate), typeof(FixedUpdate.ScriptRunDelayedFixedFrameRate),
                new PlayerLoopSystem { type = typeof(GamePhysicsTick), updateDelegate = RunPhysicsTicks });
            Insert(ref loop, typeof(PreLateUpdate), typeof(PreLateUpdate.ScriptRunBehaviourLateUpdate),
                new PlayerLoopSystem { type = typeof(GamePreLateTick), updateDelegate = RunPreLateTicks });
            Insert(ref loop, typeof(PreLateUpdate), null,
                new PlayerLoopSystem { type = typeof(GameLateTick), updateDelegate = RunLateTicks });
        }

        // Puts the system into the phase before the named one, or last in it; one of its type already there goes first.
        private static void Insert(ref PlayerLoopSystem loop, Type phase, Type before, PlayerLoopSystem system)
        {
            for (int i = 0; i < loop.subSystemList.Length; i++)
            {
                ref PlayerLoopSystem candidate = ref loop.subSystemList[i];
                if (candidate.type != phase)
                {
                    continue;
                }

                PlayerLoopSystem[] old = candidate.subSystemList;
                var systems = new PlayerLoopSystem[old.Length + 1];
                int count = 0;
                bool placed = false;
                foreach (PlayerLoopSystem existing in old)
                {
                    if (existing.type == before)
                    {
                        systems[count++] = system;
                        placed = true;
                    }

                    if (existing.type?.FullName != system.type.FullName)
                    {
                        systems[count++] = existing;
                    }
                }

                if (before == null)
                {
                    systems[count++] = system;
                    placed = true;
                }

                if (!placed)
                {
                    throw new InvalidOperationException($"No {before.Name} in the player loop's {phase.Name}.");
                }

                Array.Resize(ref systems, count);
                candidate.subSystemList = systems;
                return;
            }

            throw new InvalidOperationException($"No {phase.Name} in the player loop.");
        }

        // Markers: the player loop and the profiler name the game's systems by them.
        private struct GameTimeTick
        {
        }

        private struct GameStart
        {
        }

        private struct GameTick
        {
        }

        private struct GameFixedTick
        {
        }

        private struct GamePrePhysicsTick
        {
        }

        private struct GamePhysicsTick
        {
        }

        private struct GamePreLateTick
        {
        }

        private struct GameLateTick
        {
        }
    }
}
