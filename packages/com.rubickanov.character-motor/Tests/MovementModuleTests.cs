using NUnit.Framework;
using Rubickanov.Motor.Modules;
using UnityEngine;

namespace Rubickanov.Motor.Tests
{
    [TestFixture]
    public class MovementModuleTests
    {
        private const float WalkSpeed = 6f;
        private const float Epsilon = 0.0001f;

        private MotorState _state = default!;
        private FakeMotorBody _body = default!;
        private MovementModule _module = default!;
        private GameObject? _orientationGo;

        [SetUp]
        public void SetUp()
        {
            _state = new MotorState();
            _body = new FakeMotorBody();
            _module = new MovementModule();
            _module.Initialize(_state, _body, new NullModuleResolver());
        }

        [TearDown]
        public void TearDown()
        {
            _body.Dispose();
            if (_orientationGo != null)
                Object.DestroyImmediate(_orientationGo);
        }

        [Test]
        public void Simulate_IdleInput_SetsDesiredVelocityToZero()
        {
            _module.Orientation = MovementOrientation.World;
            _state.MoveInput = Vector2.zero;

            _module.Simulate(0.02f);

            Assert.AreEqual(Vector3.zero, _state.DesiredVelocity);
        }

        [Test]
        public void Simulate_WorldOrientation_ForwardInput_MovesPositiveZAtWalkSpeed()
        {
            _module.Orientation = MovementOrientation.World;
            _state.MoveInput = new Vector2(0f, 1f);

            _module.Simulate(0.02f);

            Assert.AreEqual(new Vector3(0f, 0f, WalkSpeed), _state.DesiredVelocity);
        }

        [Test]
        public void Simulate_WorldOrientation_RightInput_MovesPositiveXAtWalkSpeed()
        {
            _module.Orientation = MovementOrientation.World;
            _state.MoveInput = new Vector2(1f, 0f);

            _module.Simulate(0.02f);

            Assert.AreEqual(new Vector3(WalkSpeed, 0f, 0f), _state.DesiredVelocity);
        }

        [Test]
        public void Simulate_WorldOrientation_DiagonalInput_NormalizedToWalkSpeed()
        {
            _module.Orientation = MovementOrientation.World;
            _state.MoveInput = new Vector2(1f, 1f);

            _module.Simulate(0.02f);

            Assert.AreEqual(WalkSpeed, _state.DesiredVelocity.magnitude, Epsilon);
        }

        [Test]
        public void Simulate_TransformOrientation_UsesOrientationSourceProjectedOnHorizontalPlane()
        {
            _orientationGo = new GameObject("Orientation");
            // Yawed 90° — forward now points along +X
            _orientationGo.transform.rotation = Quaternion.Euler(0f, 90f, 0f);
            _module.Orientation = MovementOrientation.Transform;
            _module.SetOrientationSource(_orientationGo.transform);
            _state.MoveInput = new Vector2(0f, 1f);

            _module.Simulate(0.02f);

            Assert.AreEqual(WalkSpeed, _state.DesiredVelocity.x, Epsilon);
            Assert.AreEqual(0f, _state.DesiredVelocity.y, Epsilon);
            Assert.AreEqual(0f, _state.DesiredVelocity.z, Epsilon);
        }

        [Test]
        public void Simulate_TransformOrientation_SourceTiltedDown_ProjectsToHorizontalPlaneByDefault()
        {
            _orientationGo = new GameObject("Orientation");
            // Pitch down 45° — forward has a downward Y component
            _orientationGo.transform.rotation = Quaternion.Euler(45f, 0f, 0f);
            _module.Orientation = MovementOrientation.Transform;
            _module.SetOrientationSource(_orientationGo.transform);
            _state.MoveInput = new Vector2(0f, 1f);

            _module.Simulate(0.02f);

            Assert.AreEqual(0f, _state.DesiredVelocity.y, Epsilon);
            Assert.AreEqual(WalkSpeed, _state.DesiredVelocity.magnitude, Epsilon);
        }

        [Test]
        public void Simulate_TransformOrientation_AllowVerticalMovement_PreservesYComponent()
        {
            _orientationGo = new GameObject("Orientation");
            _orientationGo.transform.rotation = Quaternion.Euler(45f, 0f, 0f);
            _module.Orientation = MovementOrientation.Transform;
            _module.AllowVerticalMovement = true;
            _module.SetOrientationSource(_orientationGo.transform);
            _state.MoveInput = new Vector2(0f, 1f);

            _module.Simulate(0.02f);

            Assert.Greater(Mathf.Abs(_state.DesiredVelocity.y), 0.1f);
        }
    }
}
