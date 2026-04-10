using System.Reflection;
using NUnit.Framework;
using Rubickanov.Motor.Modules;
using UnityEngine;

namespace Rubickanov.Motor.Tests
{
    [TestFixture]
    public class MouseLookModuleTests
    {
        private const float Dt = 0.02f;
        private const float Sensitivity = 2f;
        private const float VerticalClamp = 89f;

        private MotorState _state = default!;
        private FakeMotorBody _body = default!;
        private MouseLookModule _module = default!;
        private GameObject? _cameraGo;

        [SetUp]
        public void SetUp()
        {
            _state = new MotorState();
            _body = new FakeMotorBody();
            _module = new MouseLookModule();
            _module.Initialize(_state, _body, new NullModuleResolver());
        }

        [TearDown]
        public void TearDown()
        {
            _body.Dispose();
            if (_cameraGo != null)
                Object.DestroyImmediate(_cameraGo);
        }

        [Test]
        public void Simulate_DeterministicYawEnabled_RotatesBodyFromLookInputData()
        {
            SetPrivate("_deterministicYaw", true);
            _state.InputExtensions = new InputExtensions();
            _state.InputExtensions.Set(new LookInputData { Look = new Vector2(1f, 0f) });

            _module.Simulate(Dt);

            Assert.AreEqual(1, _body.RotateCalls.Count);
            Assert.AreEqual(Vector3.up, _body.RotateCalls[0].axis);
            Assert.AreEqual(1f * Sensitivity, _body.RotateCalls[0].angle, 0.0001f);
            Assert.AreEqual(Space.World, _body.RotateCalls[0].space);
        }

        [Test]
        public void Simulate_DeterministicYawDisabled_DoesNotRotateBody()
        {
            _state.InputExtensions = new InputExtensions();
            _state.InputExtensions.Set(new LookInputData { Look = new Vector2(1f, 0f) });

            _module.Simulate(Dt);

            Assert.AreEqual(0, _body.RotateCalls.Count);
        }

        [Test]
        public void Simulate_DeterministicYawEnabledButNoInputExtensions_DoesNotRotateBody()
        {
            SetPrivate("_deterministicYaw", true);
            _state.InputExtensions = null;

            _module.Simulate(Dt);

            Assert.AreEqual(0, _body.RotateCalls.Count);
        }

        [Test]
        public void VisualUpdate_NonDeterministic_AppliesYawRotationToBody()
        {
            _module.SetLookInputProvider(() => new Vector2(1f, 0f));

            _module.VisualUpdate(Dt);

            Assert.AreEqual(1, _body.RotateCalls.Count);
            Assert.AreEqual(Vector3.up, _body.RotateCalls[0].axis);
            Assert.AreEqual(1f * Sensitivity, _body.RotateCalls[0].angle, 0.0001f);
        }

        [Test]
        public void VisualUpdate_PositiveLookY_DecreasesPitchWithSensitivity()
        {
            _module.SetLookInputProvider(() => new Vector2(0f, 1f));

            _module.VisualUpdate(Dt);

            Assert.AreEqual(-1f * Sensitivity, _module.Pitch, 0.0001f);
        }

        [Test]
        public void VisualUpdate_PitchAboveClamp_ClampsToVerticalClamp()
        {
            // look.y = -50 → pitch -= -100 → pitch = 100 → clamped to 89
            _module.SetLookInputProvider(() => new Vector2(0f, -50f));

            _module.VisualUpdate(Dt);

            Assert.AreEqual(VerticalClamp, _module.Pitch, 0.0001f);
        }

        [Test]
        public void VisualUpdate_PitchBelowNegativeClamp_ClampsToNegativeVerticalClamp()
        {
            // look.y = 50 → pitch -= 100 → pitch = -100 → clamped to -89
            _module.SetLookInputProvider(() => new Vector2(0f, 50f));

            _module.VisualUpdate(Dt);

            Assert.AreEqual(-VerticalClamp, _module.Pitch, 0.0001f);
        }

        [Test]
        public void VisualUpdate_SensitivityScalesYawAndPitchProportionally()
        {
            SetPrivate("_sensitivity", 4f);
            _module.SetLookInputProvider(() => new Vector2(1f, 1f));

            _module.VisualUpdate(Dt);

            Assert.AreEqual(4f, _body.RotateCalls[0].angle, 0.0001f);
            Assert.AreEqual(-4f, _module.Pitch, 0.0001f);
        }

        [Test]
        public void VisualUpdate_CameraTransformSet_UpdatesCameraLocalRotation()
        {
            _cameraGo = new GameObject("Camera");
            _module.SetCameraTransform(_cameraGo.transform);
            _module.SetLookInputProvider(() => new Vector2(0f, 1f));

            _module.VisualUpdate(Dt);

            Quaternion expected = Quaternion.Euler(-1f * Sensitivity, 0f, 0f);
            Assert.AreEqual(expected, _cameraGo.transform.localRotation);
        }

        [Test]
        public void VisualUpdate_NoProvider_DoesNothing()
        {
            _module.VisualUpdate(Dt);

            Assert.AreEqual(0, _body.RotateCalls.Count);
            Assert.AreEqual(0f, _module.Pitch);
        }

        [Test]
        public void VisualUpdate_ZeroLook_DoesNothing()
        {
            _module.SetLookInputProvider(() => Vector2.zero);

            _module.VisualUpdate(Dt);

            Assert.AreEqual(0, _body.RotateCalls.Count);
            Assert.AreEqual(0f, _module.Pitch);
        }

        [Test]
        public void SaveRestore_Roundtrip_PreservesPitch()
        {
            _module.SetLookInputProvider(() => new Vector2(0f, 5f));
            _module.VisualUpdate(Dt);
            float pitchBefore = _module.Pitch;

            var writer = new ModuleStateWriter(16);
            _module.SaveState(ref writer);
            var bytes = writer.ToArray();

            _module.SetLookInputProvider(() => new Vector2(0f, -5f));
            _module.VisualUpdate(Dt);
            Assert.AreNotEqual(pitchBefore, _module.Pitch, "Precondition: pitch changed before restore");

            var reader = new ModuleStateReader(bytes);
            _module.RestoreState(ref reader);

            Assert.AreEqual(pitchBefore, _module.Pitch, 0.0001f);
        }

        private void SetPrivate(string fieldName, object value)
        {
            var field = typeof(MouseLookModule).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(field, $"Private field {fieldName} not found on MouseLookModule");
            field!.SetValue(_module, value);
        }
    }
}
