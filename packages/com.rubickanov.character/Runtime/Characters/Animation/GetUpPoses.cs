using System;
using System.Collections.Generic;
using Animancer;
using Rubickanov.Rig;
using UnityEngine;

namespace Rubickanov.Characters
{
    /// <summary>
    /// Every frame of the get-up clips of a <see cref="CharacterAnimationsConfig"/>, baked on the character's ragdoll
    /// bones by the package's editor (GetUpBaker) for the nearest-frame get-up (<see cref="GetUpSearch"/>). Entries
    /// follow the config's <see cref="CharacterAnimationsConfig.GetUps"/> list; each keeps its clip's name, length and
    /// frame count so a list changed without a rebake is seen. Frames are flat arrays: per frame
    /// <see cref="GetUpSearch.Joints"/> joint turns, the pelvis's turn and place in the clip root's frame and
    /// <see cref="GetUpSearch.Segments"/> segment heights; the whole-body path every <see cref="PathStep"/> s, each
    /// segment a capsule (two ends in the frame of the root at the clip's start, radius per segment).
    /// </summary>
    [CreateAssetMenu(menuName = "Characters/Get-up Poses")]
    public sealed class GetUpPoses : ScriptableObject
    {
        [Serializable]
        public struct Entry
        {
            public string Name;
            public float Length;
            public int FrameCount;
            public float FrameRate;

            /// <summary>The first frame the clip stands from; the search starts it no later than
            /// <see cref="GetUpPoses.GetUpLeft"/> s before.</summary>
            public int StandFrame;

            /// <summary>The root's level travel from start to end, in the start root's frame.</summary>
            public Vector3 EndTravel;

            public int FirstFrame;
            public int FirstPath;
            public int PathCount;
        }

        /// <summary>A frame where the clip already stands would leave it nothing to play, s (Sinew Actor.Rise.cs:36).</summary>
        public const float GetUpLeft = 0.5f;

        /// <summary>The whole-body path's step, s (Sinew RoomEvery, 6 plan steps).</summary>
        public const float PathStep = 0.1f;

        public Entry[] Entries = { };

        /// <summary>The ragdoll bone each one hangs from, -1 for the hips.</summary>
        public int[] Parents = { };

        /// <summary>Each segment's capsule radius, m.</summary>
        public float[] Radii = { };

        public Quaternion[] JointTurns = { };
        public Quaternion[] PelvisTurns = { };
        public Vector3[] PelvisPositions = { };
        public float[] Heights = { };

        /// <summary>The path: <see cref="GetUpSearch.Segments"/> capsules per sample, ends A and B.</summary>
        public Vector3[] PathA = { };
        public Vector3[] PathB = { };

        [NonSerialized] private GetUpClipView[] _views;
        [NonSerialized] private Entry[] _viewsOf;

        /// <summary>The entries as the search reads them.</summary>
        public IReadOnlyList<GetUpClipView> Views
        {
            get
            {
                if (_views == null || _viewsOf != Entries)
                {
                    _viewsOf = Entries;
                    _views = new GetUpClipView[Entries.Length];
                    for (int i = 0; i < Entries.Length; i++)
                    {
                        Entry e = Entries[i];
                        _views[i] = new GetUpClipView
                        {
                            FirstFrame = e.FirstFrame,
                            FrameCount = e.FrameCount,
                            LastStart = Mathf.Max(0, e.StandFrame - Mathf.RoundToInt(GetUpLeft * e.FrameRate)),
                        };
                    }
                }

                return _views;
            }
        }

        /// <summary>Null when every entry is the clip at the same place of <paramref name="clips"/> (name, length,
        /// frame count); else what differs.</summary>
        public string Mismatch(IReadOnlyList<ClipTransition> clips)
        {
            if (clips.Count != Entries.Length)
            {
                return $"{Entries.Length} baked get-ups for {clips.Count} in the list";
            }

            for (int i = 0; i < clips.Count; i++)
            {
                AnimationClip clip = clips[i] != null ? clips[i].Clip : null;
                Entry e = Entries[i];
                if (clip == null)
                {
                    return $"get-up {i} has no clip";
                }

                if (clip.name != e.Name || Mathf.Abs(clip.length - e.Length) > 1e-4f || FramesOf(clip) != e.FrameCount)
                {
                    return $"get-up {i} is {clip.name} ({clip.length:F3} s), baked {e.Name} ({e.Length:F3} s)";
                }
            }

            return Parents.Length == GetUpSearch.Segments ? null : "baked for another ragdoll";
        }

        /// <summary>The frames a clip is baked at: every frame at its rate, both ends.</summary>
        public static int FramesOf(AnimationClip clip) => Mathf.Max(1, Mathf.RoundToInt(clip.length * clip.frameRate) + 1);

        /// <summary>The capsule of a ragdoll bone's first collider (on it or a holder under it), in the bone's space;
        /// false without one.</summary>
        public static bool BoneShape(Transform bone, out Vector3 a, out Vector3 b, out float radius)
        {
            Collider collider = bone.GetComponentInChildren<Collider>(true);
            if (collider == null)
            {
                a = b = Vector3.zero;
                radius = 0f;
                return false;
            }

            Shape(collider, out a, out b, out radius);
            Transform holder = collider.transform;
            if (holder != bone)
            {
                a = bone.InverseTransformPoint(holder.TransformPoint(a));
                b = bone.InverseTransformPoint(holder.TransformPoint(b));
            }

            return true;
        }

        /// <summary>
        /// The capsule a ragdoll segment's collider makes, in the collider's space: a capsule as it is, a sphere as a
        /// capsule of no length, a box along its longest side with the smallest half side as radius.
        /// </summary>
        public static void Shape(Collider collider, out Vector3 a, out Vector3 b, out float radius)
        {
            switch (collider)
            {
                case CapsuleCollider capsule:
                {
                    Vector3 axis = capsule.direction == 0 ? Vector3.right
                        : capsule.direction == 1 ? Vector3.up : Vector3.forward;
                    float half = Mathf.Max(0f, capsule.height * 0.5f - capsule.radius);
                    a = capsule.center - axis * half;
                    b = capsule.center + axis * half;
                    radius = capsule.radius;
                    return;
                }
                case SphereCollider sphere:
                    a = b = sphere.center;
                    radius = sphere.radius;
                    return;
                case BoxCollider box:
                {
                    Vector3 h = box.size * 0.5f;
                    int longest = h.x >= h.y && h.x >= h.z ? 0 : h.y >= h.z ? 1 : 2;
                    Vector3 axis = longest == 0 ? Vector3.right : longest == 1 ? Vector3.up : Vector3.forward;
                    float smallest = Mathf.Min(longest == 0 ? h.y : h.x, longest == 2 ? h.y : h.z);
                    float half = Mathf.Max(0f, h[longest] - smallest);
                    a = box.center - axis * half;
                    b = box.center + axis * half;
                    radius = smallest;
                    return;
                }
                default:
                    a = b = Vector3.zero;
                    radius = 0f;
                    return;
            }
        }
    }
}
