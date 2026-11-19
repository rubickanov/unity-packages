using System;
using System.Linq;
using NUnit.Framework;
using UnityEngine.LowLevel;
using UnityEngine.PlayerLoop;

namespace Rubickanov.App.Tests
{
    public sealed class FrameLoopTests
    {
        [Test]
        public void Install_DefaultLoop_EachPlaceOnceWhereTheServicesExpectIt()
        {
            PlayerLoopSystem loop = PlayerLoop.GetDefaultPlayerLoop();

            FrameLoop.Install(ref loop);

            Assert.That(Before(Phase(loop, typeof(TimeUpdate)), "GameTimeTick"),
                Is.EqualTo(nameof(TimeUpdate.WaitForLastPresentationAndUpdateTime)));
            Assert.That(Before(Phase(loop, typeof(EarlyUpdate)), "GameStart"),
                Is.EqualTo(nameof(EarlyUpdate.ScriptRunDelayedStartupFrame)));
            Assert.That(Before(Phase(loop, typeof(Update)), "GameTick"),
                Is.EqualTo(nameof(Update.ScriptRunBehaviourUpdate)));
            Assert.That(Before(Phase(loop, typeof(FixedUpdate)), "GameFixedTick"),
                Is.EqualTo(nameof(FixedUpdate.ScriptRunBehaviourFixedUpdate)));
            Assert.That(Before(Phase(loop, typeof(FixedUpdate)), "GamePrePhysicsTick"),
                Is.EqualTo(nameof(FixedUpdate.PhysicsFixedUpdate)));
            Assert.That(Before(Phase(loop, typeof(FixedUpdate)), "GamePhysicsTick"),
                Is.EqualTo(nameof(FixedUpdate.ScriptRunDelayedFixedFrameRate)));
            Assert.That(Before(Phase(loop, typeof(PreLateUpdate)), "GamePreLateTick"),
                Is.EqualTo(nameof(PreLateUpdate.ScriptRunBehaviourLateUpdate)));
            PlayerLoopSystem[] late = Phase(loop, typeof(PreLateUpdate));
            Assert.That(Only(late, "GameLateTick"), Is.EqualTo(late.Length - 1));
        }

        [Test]
        public void Install_Twice_KeepsOneOfEach()
        {
            PlayerLoopSystem loop = PlayerLoop.GetDefaultPlayerLoop();
            int count = Phase(loop, typeof(PreLateUpdate)).Length;

            FrameLoop.Install(ref loop);
            FrameLoop.Install(ref loop);

            PlayerLoopSystem[] late = Phase(loop, typeof(PreLateUpdate));
            Assert.That(late, Has.Length.EqualTo(count + 2));
            Only(late, "GamePreLateTick");
            Only(late, "GameLateTick");
        }

        private static PlayerLoopSystem[] Phase(PlayerLoopSystem loop, Type phase) =>
            loop.subSystemList.Single(system => system.type == phase).subSystemList;

        // The name of the system right after the only one called name.
        private static string Before(PlayerLoopSystem[] systems, string name) => systems[Only(systems, name) + 1].type.Name;

        private static int Only(PlayerLoopSystem[] systems, string name)
        {
            int[] found = Enumerable.Range(0, systems.Length).Where(i => systems[i].type?.Name == name).ToArray();
            Assert.That(found, Has.Length.EqualTo(1), name);
            return found[0];
        }
    }
}
