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
    /// Bakes every frame of a character's nearest-frame get-up clips (<see cref="CharacterAnimationsConfig.GetUps"/>)
    /// into <see cref="GetUpPoses"/>, sampled on an instance of the character prefab through a PlayableGraph with root
    /// motion on, as the game plays them: per frame the ragdoll bones' joint turns and the pelvis in the clip root's
    /// frame (each ragdoll bone placed on its visible bone as the ragdoll was built), segment heights over the root's
    /// ground, and every <see cref="GetUpPoses.PathStep"/> s the whole body as capsules in the frame of the root at the
    /// clip's start (the root's travel included). The stand frame follows Sinew Control/GettingUp.cs:163–176. A game
    /// calls it from its own builder after the clips are imported and the prefab built.
    /// </summary>
    public static class GetUpBaker
    {
        /// <summary>Bakes <paramref name="clips"/> on <paramref name="character"/> (a prefab with a
        /// <see cref="CharacterRagdoll"/>) into <paramref name="into"/>, which is marked dirty. Returns a report.</summary>
        public static string Bake(GameObject character, IReadOnlyList<ClipTransition> clips, GetUpPoses into)
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

        private static string Bake(GameObject instance, IReadOnlyList<ClipTransition> clips, GetUpPoses into, bool _)
        {
            var ragdoll = instance.GetComponentInChildren<CharacterRagdoll>(true);
            if (ragdoll == null || ragdoll.Bones.Length != GetUpSearch.Segments || ragdoll.Animator == null)
            {
                throw new InvalidOperationException(
                    $"{instance.name}: a CharacterRagdoll with {GetUpSearch.Segments} built bones and an animator");
            }

            Animator animator = ragdoll.Animator.Animancer.Animator;
            Transform model = animator.transform;
            RagdollBone[] bones = ragdoll.Bones;
            int n = bones.Length;
            var parents = new int[n];
            var posOffset = new Vector3[n];
            var rotOffset = new Quaternion[n];
            var shapeA = new Vector3[n];
            var shapeB = new Vector3[n];
            var radii = new float[n];
            for (int i = 0; i < n; i++)
            {
                RagdollBone bone = bones[i];
                Transform visible = bone.Visible;
                parents[i] = bone.Parent != null ? Array.IndexOf(bones, bone.Parent) : -1;
                if (i > 0 && (parents[i] < 0 || parents[i] >= i))
                {
                    throw new InvalidOperationException($"{instance.name}: bone {bone.name} before its parent");
                }

                // The bone on its visible bone as built: the prefab holds both in the build pose.
                Quaternion toVisible = Quaternion.Inverse(visible.rotation);
                posOffset[i] = toVisible * (bone.transform.position - visible.position);
                rotOffset[i] = toVisible * bone.transform.rotation;
                if (!GetUpPoses.BoneShape(bone.transform, out shapeA[i], out shapeB[i], out radii[i]))
                {
                    throw new InvalidOperationException($"{instance.name}: bone {bone.name} has no collider");
                }
            }

            var entries = new GetUpPoses.Entry[clips.Count];
            var joints = new List<Quaternion>();
            var pelvisTurns = new List<Quaternion>();
            var pelvisPositions = new List<Vector3>();
            var heights = new List<float>();
            var pathA = new List<Vector3>();
            var pathB = new List<Vector3>();
            var pelvisHeights = new List<float>();
            var report = new StringBuilder();
            report.Append($"Get-up poses: {clips.Count} clips on {instance.name}\n");
            report.Append("  clip  frames  stand s  last start s  end travel (x, z)  pelvis start/end m  path\n");

            bool applied = animator.applyRootMotion;
            AnimatorCullingMode culling = animator.cullingMode;
            animator.applyRootMotion = true;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            model.GetPositionAndRotation(out Vector3 startPosition, out Quaternion startRotation);
            var bodyRot = new Quaternion[n];
            var bodyPos = new Vector3[n];
            PlayableGraph graph = PlayableGraph.Create("GetUpBake");
            try
            {
                graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                var output = AnimationPlayableOutput.Create(graph, "Bake", animator);
                for (int c = 0; c < clips.Count; c++)
                {
                    AnimationClip clip = clips[c] != null ? clips[c].Clip : null;
                    if (clip == null)
                    {
                        throw new InvalidOperationException($"Get-up {c} has no clip");
                    }

                    int frames = GetUpPoses.FramesOf(clip);
                    float step = frames > 1 ? clip.length / (frames - 1) : 0f;
                    int pathEvery = Mathf.Max(1, Mathf.RoundToInt(GetUpPoses.PathStep * clip.frameRate));
                    var entry = new GetUpPoses.Entry
                    {
                        Name = clip.name, Length = clip.length, FrameCount = frames, FrameRate = (frames - 1) / Mathf.Max(clip.length, 1e-6f),
                        FirstFrame = pelvisTurns.Count, FirstPath = pathA.Count / GetUpSearch.Segments,
                    };
                    if (frames == 1)
                    {
                        entry.FrameRate = clip.frameRate;
                    }

                    var playable = AnimationClipPlayable.Create(graph, clip);
                    playable.SetApplyFootIK(false);
                    playable.SetDuration(clip.length);
                    output.SetSourcePlayable(playable);
                    model.SetPositionAndRotation(startPosition, startRotation);
                    playable.SetTime(0);
                    graph.Evaluate();
                    Vector3 travel = Vector3.zero;
                    Quaternion turned = Quaternion.identity;
                    for (int f = 0; f < frames; f++)
                    {
                        if (f > 0)
                        {
                            model.SetPositionAndRotation(startPosition, startRotation);
                            graph.Evaluate(step);
                            // The root's motion this step, in the start root's frame.
                            travel += turned * (Quaternion.Inverse(startRotation) * animator.deltaPosition);
                            turned = Quaternion.Inverse(startRotation) * animator.deltaRotation * startRotation * turned;
                        }

                        travel.y = 0f;
                        model.GetPositionAndRotation(out Vector3 rootPosition, out Quaternion rootRotation);
                        Quaternion toRoot = Quaternion.Inverse(rootRotation);
                        for (int i = 0; i < n; i++)
                        {
                            Transform visible = bones[i].Visible;
                            bodyRot[i] = toRoot * (visible.rotation * rotOffset[i]);
                            bodyPos[i] = toRoot * (visible.position + visible.rotation * posOffset[i] - rootPosition);
                        }

                        for (int i = 1; i < n; i++)
                        {
                            joints.Add(Quaternion.Inverse(bodyRot[parents[i]]) * bodyRot[i]);
                        }

                        pelvisTurns.Add(bodyRot[0]);
                        pelvisPositions.Add(bodyPos[0]);
                        pelvisHeights.Add(bodyPos[0].y);
                        for (int i = 0; i < n; i++)
                        {
                            heights.Add((bodyPos[i] + bodyRot[i] * ((shapeA[i] + shapeB[i]) * 0.5f)).y);
                        }

                        if (f % pathEvery == 0 || f == frames - 1)
                        {
                            for (int i = 0; i < n; i++)
                            {
                                pathA.Add(travel + turned * (bodyPos[i] + bodyRot[i] * shapeA[i]));
                                pathB.Add(travel + turned * (bodyPos[i] + bodyRot[i] * shapeB[i]));
                            }
                        }
                    }

                    playable.Destroy();
                    entry.EndTravel = travel;
                    entry.PathCount = pathA.Count / GetUpSearch.Segments - entry.FirstPath;
                    entry.StandFrame = GetUpSearch.StandFrame(pelvisHeights.ToArray(), entry.FirstFrame, frames);
                    entries[c] = entry;
                    int lastStart = Mathf.Max(0, entry.StandFrame - Mathf.RoundToInt(GetUpPoses.GetUpLeft * entry.FrameRate));
                    report.Append($"  {clip.name}  {frames}  {entry.StandFrame / entry.FrameRate:F2}  " +
                                  $"{lastStart / entry.FrameRate:F2}  ({travel.x:F2}, {travel.z:F2})  " +
                                  $"{pelvisHeights[entry.FirstFrame]:F2}/{pelvisHeights[entry.FirstFrame + frames - 1]:F2}  " +
                                  $"{entry.PathCount}\n");
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
            into.Parents = parents;
            into.Radii = radii;
            into.JointTurns = joints.ToArray();
            into.PelvisTurns = pelvisTurns.ToArray();
            into.PelvisPositions = pelvisPositions.ToArray();
            into.Heights = heights.ToArray();
            into.PathA = pathA.ToArray();
            into.PathB = pathB.ToArray();
            EditorUtility.SetDirty(into);
            report.Append($"  {pelvisTurns.Count} frames, {pathA.Count / GetUpSearch.Segments} path samples");
            return report.ToString();
        }
    }
}
