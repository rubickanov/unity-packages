namespace Rubickanov.Characters
{
    /// <summary>How a character starts getting up: lying face up or down, and for a nearest-frame get-up the clip
    /// (its place in <see cref="CharacterAnimationsConfig.GetUps"/>) and the time it starts from.</summary>
    public readonly struct CharacterGetUp
    {
        /// <summary>The clip of the face up / face down get-up, not one of the nearest-frame list.</summary>
        public const byte Classic = 255;

        public readonly bool FaceUp;
        public readonly byte Clip;

        /// <summary>Where in the clip it starts, s.</summary>
        public readonly float Time;

        public CharacterGetUp(bool faceUp, byte clip = Classic, float time = 0f)
        {
            FaceUp = faceUp;
            Clip = clip;
            Time = time;
        }
    }
}
