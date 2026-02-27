using UnityEngine;

namespace Rubickanov.Camera
{
    /// <summary>
    /// ScriptableObject configuration for the camera service (aim weights, dead zone, shake force).
    /// </summary>
    [CreateAssetMenu(menuName = "Config/Camera")]
    public class CameraConfig : ScriptableObject
    {
        [Header("Aim Offset")]
        [Tooltip("How much the focus point shifts toward the aim point when using mouse")]
        [field: SerializeField] public float MouseAimWeight { get; private set; } = 0.3f;

        [Tooltip("How much the focus point shifts toward the aim point when using gamepad")]
        [field: SerializeField] public float GamepadAimWeight { get; private set; } = 0f;

        [Tooltip("Maximum distance the focus point can shift from the player")]
        [field: SerializeField] public float MaxAimOffset { get; private set; } = 5f;

        [Tooltip("Speed of blending between mouse and gamepad aim weights")]
        [field: SerializeField] public float AimBlendSpeed { get; private set; } = 8f;

        [Tooltip("Lerp speed for smooth aim offset transition")]
        [field: SerializeField] public float AimSmoothSpeed { get; private set; } = 10f;

        [Header("Dead Zone")]
        [Tooltip("Aim distance from character below which camera does not shift")]
        [field: SerializeField] public float DeadZoneRadius { get; private set; } = 2f;

        [Header("South Compensation")]
        [Tooltip("Offset multiplier when aiming toward camera (south on screen). 1 = no compensation")]
        [field: SerializeField] public float SouthOffsetMultiplier { get; private set; } = 1.5f;

        [Header("Screen Shake")]
        [Tooltip("Impulse force applied to camera on shot (recoil direction)")]
        [field: SerializeField] public float ShootShakeForce { get; private set; } = 0.3f;
    }
}
