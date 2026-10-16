using Rubickanov.Rig;
using UnityEngine;

namespace Rubickanov.Characters
{
    /// <summary>
    /// One body of a character's ragdoll: its rigidbody and joint, the visible bone it follows and shows, and
    /// what was recorded when it was built. Written by RagdollBuilder through <see cref="Configure"/>;
    /// <see cref="Ragdoll"/> does the rest.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    [DisallowMultipleComponent]
    public sealed class RagdollBone : MonoBehaviour
    {
        [SerializeField] private Rigidbody _body;
        [Tooltip("The joint to the connected bone; none on the hips.")]
        [SerializeField] private ConfigurableJoint _joint;
        [Tooltip("The connected bone; none on the hips.")]
        [SerializeField] private RagdollBone _parent;
        [Tooltip("The model's bone this one follows and shows.")]
        [SerializeField] private Transform _visible;

        [SerializeField, HideInInspector] private Quaternion _jointSpace = Quaternion.identity;
        [SerializeField, HideInInspector] private Quaternion _restLocal = Quaternion.identity;
        [SerializeField, HideInInspector] private Quaternion _offset = Quaternion.identity;
        [SerializeField, HideInInspector] private Vector3 _restPosition;
        [SerializeField, HideInInspector] private Quaternion _restRotation = Quaternion.identity;

        public Rigidbody Body => _body;
        public ConfigurableJoint Joint => _joint;
        public RagdollBone Parent => _parent;
        public Transform Visible => _visible;

        /// <summary>The joint's own axes in this body's axes.</summary>
        public Quaternion JointSpace => _jointSpace;

        /// <summary>This bone's rotation relative to its connected bone when the joint was made: the joint's zero.</summary>
        public Quaternion RestLocal => _restLocal;

        /// <summary>The turn from the visible bone's axes to this bone's: its world rotation is the visible one's times this.</summary>
        public Quaternion Offset => _offset;

        /// <summary>The bone's rotation in the character root's space in the pose it was built in.</summary>
        public Quaternion RestRotation => _restRotation;

        /// <summary>The bone's position in the character root's space in the pose it was built in.</summary>
        public Vector3 RestPosition => _restPosition;

        /// <summary>
        /// Records the joint's axes and the rest pose as they are now, which must be the pose the ragdoll is
        /// built in, with the joint's axes already set. <paramref name="root"/> is the character's root, the
        /// capsule the ragdoll stands in for.
        /// </summary>
        public void Configure(RagdollBone parent, Transform visible, Transform root)
        {
            _body = GetComponent<Rigidbody>();
            _joint = GetComponent<ConfigurableJoint>();
            _parent = parent;
            _visible = visible;
            _jointSpace = _joint != null ? RagdollMath.JointSpace(_joint.axis, _joint.secondaryAxis) : Quaternion.identity;
            _restLocal = parent != null
                ? Quaternion.Inverse(parent.transform.rotation) * transform.rotation
                : Quaternion.identity;
            _offset = Quaternion.Inverse(visible.rotation) * transform.rotation;
            _restPosition = root.InverseTransformPoint(transform.position);
            _restRotation = Quaternion.Inverse(root.rotation) * transform.rotation;
        }

        /// <summary>
        /// Back in the rest pose on the character's <paramref name="root"/>. Done before the bones are switched
        /// on, since a joint takes its zero from the pose its two bodies are in when it is made.
        /// </summary>
        public void ToRest(Transform root) =>
            transform.SetPositionAndRotation(root.TransformPoint(_restPosition), root.rotation * _restRotation);
    }
}
