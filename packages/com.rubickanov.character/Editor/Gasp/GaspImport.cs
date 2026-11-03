using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using Object = UnityEngine.Object;

namespace Rubickanov.Characters.Editor
{
    /// <summary>
    /// Imports clips of Epic's Game Animation Sample (exported to FBX on the UEFN Mannequin skeleton) as humanoids
    /// that play on a game's character. The avatar is the skeleton's reference pose (<c>RefPose.fbx</c>) with its
    /// legs, arms, hands and fingers turned to the game's reference model's T-pose, so a muscle pose means the same limb
    /// directions on both. Root handling goes by the clip's kind (<see cref="KindOf"/>); a get-up gives its level travel
    /// as root motion and is turned by <c>rotationOffset</c> so it ends facing the root's forward.
    /// </summary>
    public static class GaspImport
    {
        /// <summary>How a clip's root is imported.</summary>
        public enum RootKind
        {
            /// <summary>Height in the pose, level travel and turn as root motion.</summary>
            Motion,

            /// <summary>A looping clip with all root motion in the pose (ragdoll flails).</summary>
            Flail,

            /// <summary>Turn and height in the pose, level travel as root motion, turned to end facing forward.</summary>
            GetUp,

            /// <summary>All root motion in the pose, no loop (reference poses).</summary>
            Pose,
        }

        /// <summary>What the import measured on one clip, on the reference model.</summary>
        public sealed class ClipReport
        {
            public string Name;
            public RootKind Kind;
            public float Length;

            /// <summary>Level root travel over the clip in the reference's metres, root frame (+Z forward).</summary>
            public Vector3 Travel;

            /// <summary>The body's level travel by its curves, reference metres (a check on the root's).</summary>
            public Vector3 BodyTravel;

            /// <summary>The same in the source's metres (the GASP figure's own height).</summary>
            public Vector3 SourceTravel;

            /// <summary>The body's yaw at the last frame with no offset, degrees (+ = to the right).</summary>
            public float EndFacing;

            /// <summary>The same by the thighs and upper arms (a check on the body's), degrees.</summary>
            public float EndFacingBones;

            /// <summary>The body's yaw at the last frame with the offset written, degrees.</summary>
            public float Residual;

            /// <summary>The same by the thighs and upper arms, degrees.</summary>
            public float ResidualBones;

            /// <summary>The importer's rotationOffset written to the clip, degrees (turns it by minus this).</summary>
            public float Offset;

            /// <summary>The hips in the root frame at the first frame, metres.</summary>
            public Vector3 StartHips;
        }

        // Ported from wipeout Sinew/Editor/Motion/GaspClips.cs: the limbs whose bones are turned to the reference.
        private static readonly HumanBodyBones[][] PosedLimbs =
        {
            new[] { HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot, HumanBodyBones.LeftToes },
            new[] { HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot, HumanBodyBones.RightToes },
            new[] { HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand },
            new[] { HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand },
        };

        /// <summary>The kind of a GASP clip by its name: <c>_flail_</c>, <c>_getup_</c>, <c>_poses_</c>, else motion.</summary>
        public static RootKind KindOf(string clipName) =>
            clipName.Contains("_flail_") ? RootKind.Flail
            : clipName.Contains("_getup_") ? RootKind.GetUp
            : clipName.Contains("_poses_") ? RootKind.Pose
            : RootKind.Motion;

        /// <summary>
        /// Sets up <paramref name="refPose"/> as the humanoid avatar and every clip in <paramref name="clips"/> on it
        /// (asset paths), measures each clip on <paramref name="reference"/> (a model with a humanoid Animator, posed
        /// by its avatar's skeleton while this runs) and turns get-ups to end facing forward. Returns a report.
        /// </summary>
        public static string Import(string refPose, IReadOnlyList<string> clips, Animator reference,
            out List<ClipReport> measured)
        {
            var report = new StringBuilder();
            reference.Rebind();
            Avatar avatar = ImportAvatar(refPose, reference, report);
            float sourceScale = HumanScale(refPose);
            float scale = reference.humanScale;
            report.AppendLine($"Reference hips in its T-pose at " +
                              $"{reference.transform.InverseTransformPoint(reference.GetBoneTransform(HumanBodyBones.Hips).position):F3}");

            AssetDatabase.StartAssetEditing();
            try
            {
                foreach (string clip in clips)
                {
                    SetClip(clip, avatar, 0f);
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
            }

            measured = new List<ClipReport>();
            var turned = new List<ClipReport>();
            foreach (string path in clips)
            {
                AnimationClip clip = ClipAt(path);
                var r = new ClipReport
                {
                    Name = System.IO.Path.GetFileNameWithoutExtension(path),
                    Kind = KindOf(System.IO.Path.GetFileNameWithoutExtension(path)),
                    Length = clip.length,
                };
                r.Travel = Travel(reference, clip);
                r.SourceTravel = r.Travel * (sourceScale / scale);
                r.BodyTravel = BodyTravel(clip, scale);
                r.StartHips = Sample(reference, clip, 0f).Hips;
                (Vector3 body, Vector3 bones, _) = Sample(reference, clip, clip.length);
                r.EndFacing = Yaw(body);
                r.EndFacingBones = Yaw(bones);
                r.Residual = r.EndFacing;
                if (r.Kind == RootKind.GetUp)
                {
                    // The importer's rotationOffset turns the clip against the yaw's sign (measured: an offset of -17°
                    // took the end yaw from 17° to 34°), so the end yaw is written as is to turn it back to 0.
                    r.Offset = Mathf.Round(r.EndFacing * 100f) / 100f;
                    turned.Add(r);
                }

                measured.Add(r);
            }

            if (turned.Count > 0)
            {
                AssetDatabase.StartAssetEditing();
                try
                {
                    foreach (ClipReport r in turned)
                    {
                        SetClip(clips[measured.IndexOf(r)], avatar, r.Offset);
                    }
                }
                finally
                {
                    AssetDatabase.StopAssetEditing();
                }

                foreach (ClipReport r in turned)
                {
                    AnimationClip clip = ClipAt(clips[measured.IndexOf(r)]);
                    (Vector3 body, Vector3 bones, _) = Sample(reference, clip, clip.length);
                    r.Residual = Yaw(body);
                    r.ResidualBones = Yaw(bones);
                    r.Travel = Travel(reference, clip);
                    r.SourceTravel = r.Travel * (sourceScale / scale);
                    if (Mathf.Abs(r.Residual) > 2f)
                    {
                        report.AppendLine(string.Format(CultureInfo.InvariantCulture,
                            "WARNING {0}: ends {1:F1}° off forward after the offset", r.Name, r.Residual));
                    }
                }
            }

            report.AppendLine(
                $"{clips.Count} clips on {refPose}; figure scale {scale:F3}, source scale {sourceScale:F3}");
            report.AppendLine("clip | kind | length s | root travel m (x, z) ours | body travel (x, z) | |root travel| source m | end yaw body (bones) | offset | residual body (bones) | hips at 0 m (x, y, z)");
            foreach (ClipReport r in measured)
            {
                report.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "{0} | {1} | {2:F3} | ({3:F2}, {4:F2}) | ({11:F2}, {12:F2}) | {5:F2} | {6:F1} ({7:F1}) | {8:F1} | {9:F1} ({13:F1}) | {14:F3}", r.Name,
                    r.Kind, r.Length, r.Travel.x, r.Travel.z, new Vector2(r.SourceTravel.x, r.SourceTravel.z).magnitude,
                    r.EndFacing, r.EndFacingBones, r.Offset, r.Residual, r.StartHips.y, r.BodyTravel.x, r.BodyTravel.z,
                    r.ResidualBones, r.StartHips));
            }

            return report.ToString();
        }

        /// <summary>Makes <paramref name="refPose"/> a humanoid with the UEFN Mannequin map, posed to the reference.</summary>
        public static Avatar ImportAvatar(string refPose, Animator reference, StringBuilder report)
        {
            var importer = (ModelImporter)AssetImporter.GetAtPath(refPose)
                           ?? throw new InvalidOperationException($"No model at {refPose}");
            SetHuman(importer);
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            importer.importAnimation = false;
            importer.SaveAndReimport();
            PoseAvatar(importer, refPose, reference, report);
            Avatar avatar = AssetDatabase.LoadAllAssetsAtPath(refPose).OfType<Avatar>().FirstOrDefault();
            if (avatar == null || !avatar.isValid || !avatar.isHuman)
            {
                throw new InvalidOperationException($"{refPose} made no valid humanoid avatar");
            }

            return avatar;
        }

        /// <summary>Sets a clip's FBX up as a humanoid on <paramref name="avatar"/> with its kind's root.</summary>
        public static void SetClip(string path, Avatar avatar, float rotationOffset)
        {
            var importer = (ModelImporter)AssetImporter.GetAtPath(path)
                           ?? throw new InvalidOperationException($"No model at {path}");
            importer.animationType = ModelImporterAnimationType.Human;
            importer.avatarSetup = ModelImporterAvatarSetup.CopyFromOther;
            importer.sourceAvatar = avatar;
            importer.materialImportMode = ModelImporterMaterialImportMode.None;
            importer.importAnimation = true;
            string name = System.IO.Path.GetFileNameWithoutExtension(path);
            SetRoot(importer, name, KindOf(name), rotationOffset);
            importer.SaveAndReimport();
        }

        // Ported from GaspClips.SetRoot (l.194–220): height from the file, no motion node; what goes in the pose and
        // what is root motion by the kind. Get-ups keep their level travel as root motion (the capsule carries it).
        private static void SetRoot(ModelImporter importer, string name, RootKind kind, float rotationOffset)
        {
            ModelImporterClipAnimation[] clips = importer.clipAnimations;
            if (clips == null || clips.Length == 0)
            {
                clips = importer.defaultClipAnimations;
            }

            bool turnInPose = kind != RootKind.Motion;
            bool travelInPose = kind is RootKind.Flail or RootKind.Pose;
            foreach (ModelImporterClipAnimation c in clips)
            {
                c.name = clips.Length == 1 ? name : c.name;
                c.loopTime = kind == RootKind.Flail;
                c.loopPose = false;
                c.lockRootHeightY = true;
                c.keepOriginalPositionY = true;
                c.heightFromFeet = false;
                c.lockRootRotation = turnInPose;
                c.keepOriginalOrientation = true;
                c.lockRootPositionXZ = travelInPose;
                // A get-up's level travel starts from the body's centre of mass, not the file's root: the rolls' root
                // stands 2 m (source) ahead of the body all along, which would leave the capsule ahead of the body.
                c.keepOriginalPositionXZ = kind != RootKind.GetUp;
                c.cycleOffset = 0f;
                c.rotationOffset = kind == RootKind.GetUp ? rotationOffset : 0f;
                c.heightOffset = 0f;
            }

            importer.clipAnimations = clips;
            importer.motionNodeName = "";
        }

        private static AnimationClip ClipAt(string path) =>
            AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>()
                .FirstOrDefault(c => !c.name.StartsWith("__preview__", StringComparison.Ordinal))
            ?? throw new InvalidOperationException($"{path} has no clip");

        // The root motion the clip gives the figure over its length, summed frame by frame at 60 Hz, root frame.
        private static Vector3 Travel(Animator figure, AnimationClip clip)
        {
            Transform root = figure.transform;
            (Vector3 position, Quaternion rotation) = (root.position, root.rotation);
            bool applied = figure.applyRootMotion;
            figure.applyRootMotion = true;
            PlayableGraph graph = PlayableGraph.Create("GaspTravel");
            Vector3 travel = Vector3.zero;
            try
            {
                graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                var output = AnimationPlayableOutput.Create(graph, "Travel", figure);
                var playable = AnimationClipPlayable.Create(graph, clip);
                playable.SetApplyFootIK(false);
                playable.SetDuration(clip.length);
                output.SetSourcePlayable(playable);
                graph.Evaluate();
                int steps = Mathf.Max(1, Mathf.RoundToInt(clip.length * 60f));
                float step = clip.length / steps;
                for (var i = 0; i < steps; i++)
                {
                    graph.Evaluate(step);
                    travel += figure.deltaPosition;
                }
            }
            finally
            {
                graph.Destroy();
                root.SetPositionAndRotation(position, rotation);
                figure.applyRootMotion = applied;
            }

            travel = Quaternion.Inverse(rotation) * travel;
            travel.y = 0f;
            return travel;
        }

        // The body's level travel by the humanoid clip's body curves (normalised units, times the figure's scale).
        private static Vector3 BodyTravel(AnimationClip clip, float humanScale)
        {
            float Delta(string axis)
            {
                AnimationCurve curve = AnimationUtility.GetEditorCurve(clip,
                    EditorCurveBinding.FloatCurve("", typeof(Animator), "RootT." + axis));
                return curve == null || curve.length == 0 ? 0f : curve.Evaluate(clip.length) - curve.Evaluate(0f);
            }

            return new Vector3(Delta("x"), 0f, Delta("z")) * humanScale;
        }

        private static float HumanScale(string model)
        {
            var figure = (GameObject)Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(model));
            try
            {
                var animator = figure.GetComponent<Animator>();
                animator.Rebind();
                return animator.humanScale;
            }
            finally
            {
                Object.DestroyImmediate(figure);
            }
        }

        // A level forward's yaw in degrees, + to the right.
        private static float Yaw(Vector3 forward) => Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg;

        /// <summary>
        /// Poses <paramref name="figure"/> by <paramref name="clip"/> at <paramref name="time"/>, its root where it is;
        /// returns the forward of <c>Animator.bodyRotation</c>, the figure's forward by its thighs and upper arms, and
        /// the hips, all in the root frame.
        /// </summary>
        public static (Vector3 Body, Vector3 Bones, Vector3 Hips) Sample(Animator figure, AnimationClip clip, float time)
        {
            PlayableGraph graph = PlayableGraph.Create("GaspSample");
            try
            {
                graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                var output = AnimationPlayableOutput.Create(graph, "Pose", figure);
                var playable = AnimationClipPlayable.Create(graph, clip);
                playable.SetApplyFootIK(false);
                playable.SetTime(time);
                playable.SetTime(time);
                output.SetSourcePlayable(playable);
                graph.Evaluate();
                Transform root = figure.transform;
                return (root.InverseTransformDirection(figure.bodyRotation * Vector3.forward), Forward(figure),
                    root.InverseTransformPoint(figure.GetBoneTransform(HumanBodyBones.Hips).position));
            }
            finally
            {
                graph.Destroy();
            }
        }

        private static void SetHuman(ModelImporter importer)
        {
            importer.animationType = ModelImporterAnimationType.Human;
            importer.materialImportMode = ModelImporterMaterialImportMode.None;
            HumanDescription description = importer.humanDescription;
            description.human = HumanMap().Select(kv => new HumanBone
            {
                humanName = kv.Key,
                boneName = kv.Value,
                limit = new HumanLimit { useDefaultValues = true },
            }).ToArray();
            description.skeleton = Array.Empty<SkeletonBone>();
            importer.humanDescription = description;
        }

        /// <summary>Humanoid bone → UEFN Mannequin bone. Ported from GaspClips.HumanMap (l.267–300).</summary>
        public static Dictionary<string, string> HumanMap()
        {
            var map = new Dictionary<string, string>
            {
                ["Hips"] = "pelvis",
                ["Spine"] = "spine_01",
                ["Chest"] = "spine_03",
                ["UpperChest"] = "spine_05",
                ["Neck"] = "neck_01",
                ["Head"] = "head",
            };
            foreach ((string side, string s) in new[] { ("Left", "l"), ("Right", "r") })
            {
                map[$"{side}Shoulder"] = $"clavicle_{s}";
                map[$"{side}UpperArm"] = $"upperarm_{s}";
                map[$"{side}LowerArm"] = $"lowerarm_{s}";
                map[$"{side}Hand"] = $"hand_{s}";
                map[$"{side}UpperLeg"] = $"thigh_{s}";
                map[$"{side}LowerLeg"] = $"calf_{s}";
                map[$"{side}Foot"] = $"foot_{s}";
                map[$"{side}Toes"] = $"ball_{s}";
                foreach ((string finger, string f) in new[] { ("Thumb", "thumb"), ("Index", "index"),
                             ("Middle", "middle"), ("Ring", "ring"), ("Little", "pinky") })
                {
                    map[$"{side} {finger} Proximal"] = $"{f}_01_{s}";
                    map[$"{side} {finger} Intermediate"] = $"{f}_02_{s}";
                    map[$"{side} {finger} Distal"] = $"{f}_03_{s}";
                }
            }

            return map;
        }

        // Ported from GaspClips.PoseAvatar (l.302–352): the avatar's skeleton with the reference pose's limbs, hands
        // and fingers turned to the reference's T-pose, twist shares 1. A bone pair the reference lacks is left as is.
        private static void PoseAvatar(ModelImporter importer, string refPose, Animator reference, StringBuilder report)
        {
            var model = (GameObject)Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(refPose));
            try
            {
                Dictionary<string, Transform> bones = model.GetComponentsInChildren<Transform>(true)
                    .Where(t => t != model.transform).GroupBy(t => t.name).ToDictionary(g => g.Key, g => g.First());
                HumanDescription description = AssetDatabase.LoadAllAssetsAtPath(refPose).OfType<Avatar>().First()
                    .humanDescription;
                Pose(reference.gameObject, reference.avatar.humanDescription.skeleton);
                var animator = model.GetComponent<Animator>();
                report.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "Facing in the root frame (deg): source {0:F1}, reference {1:F1}", Yaw(Forward(animator)),
                    Yaw(Forward(reference))));
                int aligned = 0, skipped = 0;
                foreach (HumanBodyBones[] limb in PosedLimbs.Concat(Fingers()))
                {
                    for (var i = 0; i < limb.Length - 1; i++)
                    {
                        if (Align(animator, reference, limb[i], limb[i + 1]))
                        {
                            aligned++;
                        }
                        else
                        {
                            skipped++;
                        }
                    }

                    if (limb[^1] is HumanBodyBones.LeftHand or HumanBodyBones.RightHand)
                    {
                        AlignHand(animator, reference, limb[^1] == HumanBodyBones.LeftHand, report);
                    }
                }

                report.AppendLine($"Avatar pose: {aligned} bones turned to the reference, {skipped} pairs the reference lacks");
                SkeletonBone[] skeleton = description.skeleton.ToArray();
                for (var i = 0; i < skeleton.Length; i++)
                {
                    if (bones.TryGetValue(skeleton[i].name, out Transform bone))
                    {
                        skeleton[i].position = bone.localPosition;
                        skeleton[i].rotation = bone.localRotation;
                    }
                }

                description.skeleton = skeleton;
                description.upperArmTwist = 1f;
                description.lowerArmTwist = 1f;
                description.upperLegTwist = 1f;
                description.lowerLegTwist = 1f;
                importer.humanDescription = description;
                importer.SaveAndReimport();
            }
            finally
            {
                Object.DestroyImmediate(model);
            }
        }

        private static IEnumerable<HumanBodyBones[]> Fingers()
        {
            for (var bone = HumanBodyBones.LeftThumbProximal; bone <= HumanBodyBones.RightLittleDistal; bone += 3)
            {
                yield return new[] { bone, bone + 1, bone + 2 };
            }
        }

        private static void Pose(GameObject root, SkeletonBone[] skeleton)
        {
            Dictionary<string, Transform> bones = root.GetComponentsInChildren<Transform>(true)
                .Where(t => t != root.transform).GroupBy(t => t.name).ToDictionary(g => g.Key, g => g.First());
            foreach (SkeletonBone bone in skeleton)
            {
                if (bones.TryGetValue(bone.name, out Transform t))
                {
                    t.localPosition = bone.position;
                    t.localRotation = bone.rotation;
                }
            }
        }

        // The level forward of a figure in its root frame, from its thighs and upper arms.
        private static Vector3 Forward(Animator animator)
        {
            Vector3 right = Direction(animator, HumanBodyBones.LeftUpperLeg, HumanBodyBones.RightUpperLeg) +
                            Direction(animator, HumanBodyBones.LeftUpperArm, HumanBodyBones.RightUpperArm);
            return Vector3.Cross(right, Vector3.up);
        }

        // GaspClips.Align (l.375): turns the bone so its direction to the next is the reference's.
        private static bool Align(Animator animator, Animator reference, HumanBodyBones bone, HumanBodyBones next)
        {
            if (!Has(animator, bone, next) || !Has(reference, bone, next))
            {
                return false;
            }

            Transform from = animator.GetBoneTransform(bone);
            Vector3 direction = Direction(animator, bone, next);
            Vector3 goal = Direction(reference, bone, next);
            from.rotation = animator.transform.rotation * Quaternion.FromToRotation(direction, goal) *
                            Quaternion.Inverse(animator.transform.rotation) * from.rotation;
            return true;
        }

        // GaspClips.AlignHand (l.384): turns the hand so its palm lies as the reference's. A reference without a
        // middle and a little finger (a mitten) gives no palm plane; the hand then keeps the reference pose's turn to
        // the forearm, which the arm's alignment carries along (laying it by the index and the thumb bent the wrist
        // ~70° in every GASP frame on beach's mitten hands; checked on the hand close-ups).
        private static void AlignHand(Animator animator, Animator reference, bool left, StringBuilder report)
        {
            HumanBodyBones hand = left ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand;
            if (!HasPalm(reference, left))
            {
                report.AppendLine($"{hand}: the reference has no middle and little finger; kept with the forearm");
                return;
            }

            (Vector3 Along, Vector3 Normal) ours = Palm(animator, left);
            (Vector3 Along, Vector3 Normal) theirs = Palm(reference, left);
            Quaternion turn = Quaternion.LookRotation(theirs.Along, theirs.Normal) *
                              Quaternion.Inverse(Quaternion.LookRotation(ours.Along, ours.Normal));
            Transform bone = animator.GetBoneTransform(hand);
            bone.rotation = animator.transform.rotation * turn * Quaternion.Inverse(animator.transform.rotation) *
                            bone.rotation;
            report.AppendLine(string.Format(CultureInfo.InvariantCulture, "{0}: palm turned {1:F1} deg", hand,
                Quaternion.Angle(Quaternion.identity, turn)));
        }

        private static bool HasPalm(Animator animator, bool left) =>
            Has(animator, left ? HumanBodyBones.LeftMiddleProximal : HumanBodyBones.RightMiddleProximal,
                left ? HumanBodyBones.LeftLittleProximal : HumanBodyBones.RightLittleProximal,
                left ? HumanBodyBones.LeftIndexProximal : HumanBodyBones.RightIndexProximal);

        // GaspClips.Palm (l.396): along the hand to the middle finger, across from the little to the index finger.
        private static (Vector3 Along, Vector3 Normal) Palm(Animator animator, bool left)
        {
            Vector3 along = Direction(animator, left ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand,
                left ? HumanBodyBones.LeftMiddleProximal : HumanBodyBones.RightMiddleProximal);
            Vector3 across = Direction(animator,
                left ? HumanBodyBones.LeftLittleProximal : HumanBodyBones.RightLittleProximal,
                left ? HumanBodyBones.LeftIndexProximal : HumanBodyBones.RightIndexProximal);
            return (along, Vector3.Cross(along, across));
        }

        private static bool Has(Animator animator, params HumanBodyBones[] bones) =>
            bones.All(b => animator.GetBoneTransform(b) != null);

        private static Vector3 Direction(Animator animator, HumanBodyBones from, HumanBodyBones to) =>
            animator.transform.InverseTransformDirection(animator.GetBoneTransform(to).position -
                                                         animator.GetBoneTransform(from).position);
    }
}
