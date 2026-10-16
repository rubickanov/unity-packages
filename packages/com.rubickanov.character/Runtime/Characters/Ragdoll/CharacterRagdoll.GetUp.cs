using System.Collections.Generic;
using KinematicCharacterController;
using Rubickanov.Rig;
using Unity.Profiling;
using UnityEngine;

namespace Rubickanov.Characters
{
    // The nearest-frame get-up (CharacterAnimationsConfig.GetUps with baked GetUpPoses): the clip, start and heading
    // nearest the lying body (GetUpSearch), checked cheapest first for a spot to stand, a fixed piece the trunk or head
    // would rise into and another body in its way; the clip starts at once from that frame and, with inertialization,
    // the ragdoll's pose dies away over it. Ported from SPLATDOWN's Sinew Actors/Actor.Rise.cs (ViewOfBody l.169–179,
    // BeginClipRise l.199–225, RisesIntoFixedPiece l.566–611, BodyInTheWay l.623–661).
    public sealed partial class CharacterRagdoll
    {
        // Every character's ragdoll that is on, for the bodies one getting up must not rise into.
        private static readonly List<CharacterRagdoll> Everyone = new();
        private static readonly ProfilerMarker SearchMarker = new("CharacterRagdoll.GetUpSearch");
        private static readonly Collider[] PathHits = new Collider[32];
        private static readonly RaycastHit[] CeilingHits = new RaycastHit[8];

        // Two bodies nearer than this touch, m (Sinew ActorTouch.Margin).
        private const float TouchMargin = 0.02f;

        // A ceiling is looked for from this far over the ground, m (Sinew ActorGround.CeilingGap).
        private const float CeilingGap = 1e-3f;

        // Other characters farther than this from the root are not in the way, m.
        private const float OthersReach = 4f;

        private int _nearestChecked;
        private float _searchAgain;
        private bool _risingByClip;
        private int _riseClip;
        private Vector3 _riseRoot;
        private Quaternion _riseTurn;
        private Vector3 _placePosition;
        private Quaternion _placeRotation;
        private readonly List<GetUpMatch> _matches = new();
        private readonly List<Collider> _otherColliders = new();
        private readonly HashSet<Collider> _others = new();
        private readonly List<CharacterRagdoll> _risingOthers = new();
        private readonly Quaternion[] _lyingTurns = new Quaternion[GetUpSearch.Segments];
        private readonly float[] _lyingHeights = new float[GetUpSearch.Segments];
        private Vector3[] _segmentCentres;
        private Quaternion[] _shown;
        private Vector3 _shownHips;

        /// <summary>The last nearest-frame get-up's match (clip = its Entry, Frame, Heading, Cost).</summary>
        public GetUpMatch LastGetUp { get; private set; }

        /// <summary>How many cheaper candidates the last nearest-frame get-up passed over, and why.</summary>
        public string LastGetUpRejects { get; private set; } = "";

        /// <summary>Getting up by a nearest-frame clip now.</summary>
        public bool RisingByClip => _risingByClip && State == BodyState.GettingUp;

        // Nearest-frame get-ups for a body on land, if the config has them baked.
        private bool UsesNearest() => NearestReady() && !InDeepWater(_ragdoll.Hips.transform.position, out _, out _);

        // The config's list and its bake agree; checked once, a mismatch warns once and keeps the face up / down path.
        private bool NearestReady()
        {
            if (!Animator.HasGetUps)
            {
                return false;
            }

            if (_nearestChecked == 0)
            {
                CharacterAnimationsConfig animations = Animator.Animations;
                string mismatch = animations.GetUpPoses.Mismatch(animations.GetUps);
                _nearestChecked = mismatch == null ? 1 : -1;
                if (mismatch != null)
                {
                    Log.Warn($"Get-up poses {animations.GetUpPoses.name} do not match {animations.name}.GetUps " +
                             $"({mismatch}): rebuild them. Getting up face up / face down meanwhile.", this);
                }
            }

            return _nearestChecked > 0;
        }

        // The cheapest get-up that stands, does not rise into a fixed piece and is not in another body's way; its
        // capsule pose goes to _placePosition/_placeRotation. None: false, looked for again in SpotRetry. Down past
        // SpotGiveUp, the cheapest rises anyway.
        private bool ChooseNearest(out int clip, out float time)
        {
            clip = 0;
            time = 0f;
            bool gaveUp = _downTime >= Config.SpotGiveUp;
            if (!gaveUp && _downTime < _searchAgain)
            {
                return false;
            }

            using ProfilerMarker.AutoScope scope = SearchMarker.Auto();
            GetUpPoses poses = Animator.Animations.GetUpPoses;
            Vector3 up = Up;
            // The body as the clips see it (ViewOfBody): gravity up, heading 0, from the ground under the hips.
            Quaternion frame = Quaternion.FromToRotation(up, Vector3.up);
            Vector3 hips = _ragdoll.Hips.transform.position;
            Vector3 ground = GroundUnder(hips);
            Vector3[] centres = SegmentCentres(poses);
            IReadOnlyList<RagdollBone> bones = _ragdoll.Bones;
            for (int i = 0; i < GetUpSearch.Segments; i++)
            {
                Transform bone = bones[i].transform;
                _lyingTurns[i] = frame * bone.rotation;
                _lyingHeights[i] = Vector3.Dot(bone.TransformPoint(centres[i]) - ground, up);
            }

            GetUpSearch.Nearest(poses.Views, poses.JointTurns, poses.PelvisTurns, poses.PelvisPositions, poses.Heights,
                poses.Parents, _lyingTurns, _lyingHeights, _matches);
            Transform body = Character.transform;
            Transform model = Animator.Animancer.Animator.transform;
            Vector3 modelInBody = body.InverseTransformVector(model.position - body.position);
            Quaternion modelRest = Animator.ModelRest;
            Vector3 hipsInFrame = frame * (hips - ground);
            Vector3 head = _ragdoll.Head.transform.position;
            string rejects = "";
            foreach (GetUpMatch match in _matches)
            {
                Vector3 root = ground + Quaternion.Inverse(frame) * match.Root(hipsInFrame, 0f);
                Quaternion rootTurn = Quaternion.Inverse(frame) * match.Turn;
                Quaternion bodyRotation = rootTurn * Quaternion.Inverse(modelRest);
                Vector3 bodyPosition = root - bodyRotation * modelInBody;
                string name = poses.Entries[match.Entry].Name;
                if (Config.SafeSpot)
                {
                    if (FindSpot(bodyPosition, bodyRotation, head, out Vector3 spot))
                    {
                        root += spot - bodyPosition;
                        bodyPosition = spot;
                    }
                    else if (!gaveUp)
                    {
                        rejects += $"{name}: no spot; ";
                        continue;
                    }
                }

                if (!gaveUp && RisesIntoFixedPiece(poses, match.Entry, root, rootTurn))
                {
                    rejects += $"{name}: rises into a piece; ";
                    continue;
                }

                if (BodyInTheWay(poses, match.Entry, root, rootTurn))
                {
                    rejects += $"{name}: a body in the way; ";
                    continue;
                }

                _placePosition = bodyPosition;
                _placeRotation = bodyRotation;
                clip = match.Entry;
                time = match.Frame / poses.Entries[match.Entry].FrameRate;
                LastGetUp = match;
                LastGetUpRejects = rejects;
                Log.Info($"Get-up {name} from {time:F2} s, heading {match.Heading * Mathf.Rad2Deg:F0}°, cost " +
                         $"{match.Cost:F2}{(rejects.Length > 0 ? ", passed over " + rejects : "")}", this);
                return true;
            }

            LastGetUpRejects = rejects;
            _searchAgain = _downTime + Config.SpotRetry;
            return false;
        }

        // The switch frame: the pose shown now is kept, the capsule goes to the found place (the owner's), the clip
        // plays from the found time, and with inertialization the shown pose is laid over it and dies away, the
        // ragdoll gone at once; without, the held clip blends in from the ragdoll as the face up / down get-up does.
        // The pose shown now, which an inertial get-up starts from. A replica captures it as the get-up arrives,
        // before this frame's stream moves its lying body onto the capsule the owner stood up at.
        private bool CaptureShown()
        {
            PoseInertia inertia = Animator.Inertia;
            if (!Animator.InertiaOf(InertiaKind.GetUp, out _) || inertia.BoneCount <= 0)
            {
                return false;
            }

            if (_shown == null || _shown.Length != inertia.BoneCount)
            {
                _shown = new Quaternion[inertia.BoneCount];
            }

            inertia.Capture(_shown, out _shownHips);
            return true;
        }

        private void BeginNearest(int clip, float time, bool owner, bool captured = false)
        {
            _ragdoll.Map(1f);
            PoseInertia inertia = Animator.Inertia;
            bool inertial = Animator.InertiaOf(InertiaKind.GetUp, out float length) && inertia.BoneCount > 0;
            if (inertial && !captured)
            {
                CaptureShown();
            }

            if (owner)
            {
                Character.Teleport(_placePosition, _placeRotation);
            }

            State = BodyState.GettingUp;
            GetUpPoses.Entry entry = Animator.Animations.GetUpPoses.Entries[clip];
            float standTime = entry.StandFrame / entry.FrameRate;
            _getUpClip = Animator.PlayGetUpAt(clip, time, standTime, owner, inertial ? inertia : null);
            Transform model = Animator.Animancer.Animator.transform;
            _risingByClip = true;
            _riseClip = clip;
            _riseRoot = model.position;
            _riseTurn = model.rotation;
            if (inertial)
            {
                inertia.BeginFrom(_shown, _shownHips, length);
                _ragdoll.TurnOff();
                _blend = 0f;
            }
            else
            {
                _getUpClip.Speed = 0f;
                _blend = 1f;
                _blendTime = Config.GetUpBlendTime;
                _ragdoll.Map(1f);
            }

            if (Rig != null)
            {
                Rig.enabled = true;
            }

            if (owner)
            {
                GettingUp?.Invoke(new CharacterGetUp(LiesFaceUp(), (byte)clip, time));
            }
        }

        // The trunk or head led by the clip from this root deeper than PieceSlop into a fixed piece over the ground
        // it gets up from (the edge of a slab over the head); a piece standing on that ground is the spot's business.
        private bool RisesIntoFixedPiece(GetUpPoses poses, int entry, Vector3 root, Quaternion turn)
        {
            KinematicCharacterMotor motor = Character.Motor;
            GetUpPoses.Entry e = poses.Entries[entry];
            Vector3 up = Up;
            float ground = Vector3.Dot(root, up);
            for (int p = 0; p < e.PathCount; p++)
            {
                // Hips, spine, chest, head.
                for (int s = 0; s <= Ragdoll.HeadIndex; s++)
                {
                    int k = (e.FirstPath + p) * GetUpSearch.Segments + s;
                    float radius = poses.Radii[s] - Config.PieceSlop;
                    if (radius <= 0f)
                    {
                        continue;
                    }

                    Vector3 a = root + turn * poses.PathA[k];
                    Vector3 b = root + turn * poses.PathB[k];
                    int count = Physics.OverlapCapsuleNonAlloc(a, b, radius, PathHits, motor.CollidableLayers,
                        QueryTriggerInteraction.Ignore);
                    bool inPiece = false;
                    for (int i = 0; i < count && !inPiece; i++)
                    {
                        inPiece = FixedPiece(PathHits[i]);
                    }

                    if (!inPiece)
                    {
                        continue;
                    }

                    Vector3 centre = (a + b) * 0.5f;
                    float height = Vector3.Dot(centre, up) - ground;
                    if (CeilingOver(centre - up * (height - CeilingGap), up, height + poses.Radii[s] - CeilingGap))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        // A fixed piece over the point within reach, not one the point is inside.
        private bool CeilingOver(Vector3 from, Vector3 up, float reach)
        {
            if (reach <= 0f)
            {
                return false;
            }

            int count = Physics.RaycastNonAlloc(from, up, CeilingHits, reach, Character.Motor.CollidableLayers,
                QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                if (CeilingHits[i].distance > 0f && FixedPiece(CeilingHits[i].collider))
                {
                    return true;
                }
            }

            return false;
        }

        // Static or kinematic, not a character's capsule or ragdoll, and solid to this character.
        private bool FixedPiece(Collider collider)
        {
            Rigidbody body = collider.attachedRigidbody;
            if (body != null && (!body.isKinematic || body.GetComponent<RagdollBone>() != null))
            {
                return false;
            }

            if (collider.GetComponent<KinematicCharacterMotor>() != null)
            {
                return false;
            }

            ICharacterController controller = Character.Motor.CharacterController;
            return controller == null || controller.IsColliderValidForCollisions(collider);
        }

        // Another character's body in the clip's way from this root (Sinew BodyInTheWay): a posed get-up cannot be
        // pushed, so a limb swung through the other would knock both down already in each other. Only bodies whose pose
        // leads count (Sinew PoseLeads, Actor.Body.cs:155): one rising is waited for till it stands, one rising by a
        // clip also by its end pose; one standing only till this body has lain MaxDownTime. A body down is a ragdoll
        // that gets pushed, never waited for (two piled bodies would wait for each other). Down past SpotGiveUp, it
        // gets up anyway.
        private bool BodyInTheWay(GetUpPoses poses, int entry, Vector3 root, Quaternion turn)
        {
            if (_downTime > Config.SpotGiveUp)
            {
                return false;
            }

            _others.Clear();
            _risingOthers.Clear();
            float reach = OthersReach * OthersReach;
            foreach (CharacterRagdoll other in Everyone)
            {
                if (other == this || other._ragdoll == null || other.State == BodyState.Down ||
                    (other.State == BodyState.Upright && _downTime > Config.MaxDownTime) ||
                    (other.Character.transform.position - root).sqrMagnitude > reach)
                {
                    continue;
                }

                if (other._ragdoll.Mode != RagdollMode.Off)
                {
                    _otherColliders.Clear();
                    other._ragdoll.GetColliders(_otherColliders);
                    _others.UnionWith(_otherColliders);
                }

                if (other.Character.Motor.Capsule.enabled)
                {
                    _others.Add(other.Character.Motor.Capsule);
                }

                if (other.RisingByClip && other.Animator.Animations.GetUpPoses == poses)
                {
                    _risingOthers.Add(other);
                }
            }

            if (_others.Count == 0 && _risingOthers.Count == 0)
            {
                return false;
            }

            GetUpPoses.Entry e = poses.Entries[entry];
            for (int p = 0; p < e.PathCount; p++)
            {
                for (int s = 0; s < GetUpSearch.Segments; s++)
                {
                    int k = (e.FirstPath + p) * GetUpSearch.Segments + s;
                    Vector3 a = root + turn * poses.PathA[k];
                    Vector3 b = root + turn * poses.PathB[k];
                    float radius = poses.Radii[s] + TouchMargin;
                    if (_others.Count > 0)
                    {
                        int count = Physics.OverlapCapsuleNonAlloc(a, b, radius, PathHits, ~0,
                            QueryTriggerInteraction.Ignore);
                        for (int i = 0; i < count; i++)
                        {
                            if (_others.Contains(PathHits[i]))
                            {
                                return true;
                            }
                        }
                    }

                    foreach (CharacterRagdoll other in _risingOthers)
                    {
                        if (other.EndPoseNear(poses, a, b, radius))
                        {
                            return true;
                        }
                    }
                }
            }

            return false;
        }

        // This character's get-up's end pose within radius of segment a–b.
        private bool EndPoseNear(GetUpPoses poses, Vector3 a, Vector3 b, float radius)
        {
            GetUpPoses.Entry e = poses.Entries[_riseClip];
            int last = (e.FirstPath + e.PathCount - 1) * GetUpSearch.Segments;
            for (int s = 0; s < GetUpSearch.Segments; s++)
            {
                Vector3 oa = _riseRoot + _riseTurn * poses.PathA[last + s];
                Vector3 ob = _riseRoot + _riseTurn * poses.PathB[last + s];
                if (GetUpSearch.SegmentDistance(a, b, oa, ob) < radius + poses.Radii[s])
                {
                    return true;
                }
            }

            return false;
        }

        // Each bone's segment centre in its own space, as the bake measured it.
        private Vector3[] SegmentCentres(GetUpPoses poses)
        {
            if (_segmentCentres != null)
            {
                return _segmentCentres;
            }

            IReadOnlyList<RagdollBone> bones = _ragdoll.Bones;
            _segmentCentres = new Vector3[bones.Count];
            for (int i = 0; i < bones.Count; i++)
            {
                if (GetUpPoses.BoneShape(bones[i].transform, out Vector3 a, out Vector3 b, out _))
                {
                    _segmentCentres[i] = (a + b) * 0.5f;
                }
            }

            return _segmentCentres;
        }
    }
}
