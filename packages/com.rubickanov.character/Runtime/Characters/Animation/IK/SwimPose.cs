using UnityEngine;

namespace Rubickanov.Characters
{
    /// <summary>
    /// Holds a swimmer's head out of the water over the swimming clips: the body rises a little treading water, and
    /// swimming forward it rises more and tilts nose up around the hips, as a head-up crawl, the legs sinking.
    /// Driven by the motion alone, so remote characters get the same pose.
    /// </summary>
    public sealed class SwimPose
    {
        private readonly Transform _model;
        private readonly Transform _hips;
        // How much of the treading and of the crawling pose is on, 0 to 1.
        private float _tread;
        private float _crawl;

        public bool IsValid => _hips != null;

        public SwimPose(Animator animator, Transform model)
        {
            _model = model;
            _hips = animator.GetBoneTransform(HumanBodyBones.Hips);
        }

        public void Reset() => _tread = _crawl = 0f;

        /// <param name="swimming">Swimming at the surface with the swimming clips on.</param>
        /// <param name="fullSpeed">Swimming speed that takes the whole crawling pose, m/s.</param>
        public void Apply(CharacterRigConfig config, bool swimming, float planarSpeed, float fullSpeed, float weight,
            float deltaTime)
        {
            float crawl = swimming && fullSpeed > 0f ? Mathf.Clamp01(planarSpeed / fullSpeed) : 0f;
            float follow = config.SwimPoseTime > 0f ? 1f - Mathf.Exp(-deltaTime / config.SwimPoseTime) : 1f;
            _tread = Mathf.Lerp(_tread, swimming ? 1f : 0f, follow);
            _crawl = Mathf.Lerp(_crawl, crawl, follow);
            float lift = (config.TreadLift * _tread + config.CrawlLift * _crawl) * weight;
            float pitch = config.CrawlPitch * _crawl * weight;
            if (lift <= 0.0001f && pitch <= 0.01f)
            {
                return;
            }

            _hips.rotation = Quaternion.AngleAxis(-pitch, _model.right) * _hips.rotation;
            _hips.position += _model.up * lift;
        }
    }
}
