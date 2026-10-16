using System;
using System.Collections.Generic;
using System.Text;
using Animancer;
using Rubickanov.Rig;
using UnityEditor;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using Object = UnityEngine.Object;

namespace Rubickanov.Characters.Editor
{
    /// <summary>
    /// Bakes the strides of a character's locomotion loops (the clips of <see cref="CharacterAnimationsConfig"/>'s
    /// Locomotion, Uphill and Downhill mixers) into <see cref="StrideMarkers"/>, sampled on an instance of the
    /// character prefab through a PlayableGraph as <see cref="GetUpBaker"/> does: per frame of the loop's period the
    /// reach, left foot minus right along the root's forward, and each foot's touch-down markers
    /// (<see cref="Strides.FindMarkers"/>, the right foot's on the negated reach). A game calls it from its own
    /// builder after the clips are imported and the prefab built.
    /// </summary>
    public static class StrideBaker
    {
        /// <summary>The loops of <paramref name="config"/>'s locomotion mixers, each clip once.</summary>
        public static List<AnimationClip> LocomotionClips(CharacterAnimationsConfig config)
        {
            var clips = new List<AnimationClip>();
            Add(config.Locomotion.Animations, clips);
            Add(config.Uphill.Animations, clips);
            Add(config.Downhill.Animations, clips);
            return clips;
        }

        private static void Add(Object[] animations, List<AnimationClip> clips)
        {
            if (animations == null)
            {
                return;
            }

            foreach (Object animation in animations)
            {
                // Clips only: a transition asset among the children is skipped.
                var clip = animation as AnimationClip;
                if (clip != null && !clips.Contains(clip))
                {
                    clips.Add(clip);
                }
            }
        }

        /// <summary>Bakes <paramref name="clips"/> on <paramref name="character"/> (a prefab with a
        /// <see cref="CharacterAnimator"/>) into <paramref name="into"/>, which is marked dirty. Returns a report.</summary>
        public static string Bake(GameObject character, IReadOnlyList<AnimationClip> clips, StrideMarkers into)
        {
            var instance = (GameObject)Object.Instantiate(character);
            instance.hideFlags = HideFlags.HideAndDontSave;
            instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            try
            {
                return Bake(instance, clips, into, true);
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }

        private static string Bake(GameObject instance, IReadOnlyList<AnimationClip> clips, StrideMarkers into, bool _)
        {
            var characterAnimator = instance.GetComponentInChildren<CharacterAnimator>(true);
            Animator animator = characterAnimator != null && characterAnimator.Animancer != null
                ? characterAnimator.Animancer.Animator
                : throw new InvalidOperationException($"{instance.name}: a CharacterAnimator with an Animancer");
            Transform model = animator.transform;
            Transform leftFoot = animator.GetBoneTransform(HumanBodyBones.LeftFoot);
            Transform rightFoot = animator.GetBoneTransform(HumanBodyBones.RightFoot);
            Transform hip = animator.GetBoneTransform(HumanBodyBones.LeftUpperLeg);
            Transform knee = animator.GetBoneTransform(HumanBodyBones.LeftLowerLeg);
            if (leftFoot == null || rightFoot == null || hip == null || knee == null)
            {
                throw new InvalidOperationException($"{instance.name}: a humanoid avatar with both legs");
            }

            float leg = Vector3.Distance(hip.position, knee.position) + Vector3.Distance(knee.position, leftFoot.position);
            float stride = Strides.StrideReach * leg;

            var entries = new StrideMarkers.Entry[clips.Count];
            var reach = new List<float>();
            var report = new StringBuilder();
            report.Append($"Stride markers: {clips.Count} loops on {instance.name}, leg {leg:F2} m, stride reach {stride:F2} m\n");
            report.Append("  clip  frames  left markers  right markers  reach min/max m\n");
            bool applied = animator.applyRootMotion;
            AnimatorCullingMode culling = animator.cullingMode;
            animator.applyRootMotion = true;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            model.GetPositionAndRotation(out Vector3 startPosition, out Quaternion startRotation);
            PlayableGraph graph = PlayableGraph.Create("StrideBake");
            try
            {
                graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                var output = AnimationPlayableOutput.Create(graph, "Bake", animator);
                for (int c = 0; c < clips.Count; c++)
                {
                    AnimationClip clip = clips[c] ?? throw new InvalidOperationException($"Locomotion clip {c} is missing");
                    int frames = GetUpPoses.FramesOf(clip);
                    var entry = new StrideMarkers.Entry
                    {
                        Name = clip.name, Length = clip.length, FrameCount = frames, FirstFrame = reach.Count,
                    };
                    int loop = StrideMarkers.Loop(entry);
                    float step = clip.length / loop;
                    var playable = AnimationClipPlayable.Create(graph, clip);
                    playable.SetApplyFootIK(false);
                    output.SetSourcePlayable(playable);
                    var clipReach = new float[loop];
                    for (int f = 0; f < loop; f++)
                    {
                        model.SetPositionAndRotation(startPosition, startRotation);
                        playable.SetTime(f * step);
                        graph.Evaluate();
                        Vector3 apart = Quaternion.Inverse(model.rotation) * (leftFoot.position - rightFoot.position);
                        clipReach[f] = apart.z;
                    }

                    playable.Destroy();
                    int spacing = Mathf.Max(1, Mathf.RoundToInt(Strides.MarkerSpacing * loop / Mathf.Max(clip.length, 1e-6f)));
                    entry.Left = Strides.FindMarkers(clipReach, true, spacing, stride);
                    var negated = new float[loop];
                    for (int f = 0; f < loop; f++)
                    {
                        negated[f] = -clipReach[f];
                    }

                    entry.Right = Strides.FindMarkers(negated, true, spacing, stride);
                    reach.AddRange(clipReach);
                    entries[c] = entry;
                    report.Append($"  {clip.name}  {frames}  [{string.Join(",", entry.Left)}]  " +
                                  $"[{string.Join(",", entry.Right)}]  {Min(clipReach):F2}/{-Min(negated):F2}\n");
                }
            }
            finally
            {
                graph.Destroy();
                model.SetPositionAndRotation(startPosition, startRotation);
                animator.applyRootMotion = applied;
                animator.cullingMode = culling;
            }

            // Whole arrays replaced: no undo for a bake.
            into.Entries = entries;
            into.Reach = reach.ToArray();
            EditorUtility.SetDirty(into);
            report.Append($"  {reach.Count} frames");
            return report.ToString();
        }

        private static float Min(float[] values)
        {
            float min = float.MaxValue;
            foreach (float v in values)
            {
                min = Mathf.Min(min, v);
            }

            return min;
        }
    }
}
