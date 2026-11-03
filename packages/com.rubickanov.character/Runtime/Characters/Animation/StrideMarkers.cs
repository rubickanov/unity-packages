using System;
using Rubickanov.Rig;
using UnityEngine;

namespace Rubickanov.Characters
{
    /// <summary>
    /// The strides of a character's locomotion loops, baked on its rig (package editor <c>StrideBaker</c>): per clip
    /// the reach of every frame (left foot minus right along the root's forward, m) and each foot's touch-down frames
    /// (<see cref="Strides.FindMarkers"/>). <see cref="CharacterAnimationsConfig.StrideSync"/> starts the locomotion
    /// mixer on the stance of the foot that bears.
    /// </summary>
    public sealed class StrideMarkers : ScriptableObject
    {
        [Serializable]
        public struct Entry
        {
            public string Name;
            public float Length;
            public int FrameCount;
            public int FirstFrame;
            public int[] Left;
            public int[] Right;
        }

        public Entry[] Entries = { };

        /// <summary>Per clip <see cref="Loop"/> frames from its <see cref="Entry.FirstFrame"/>.</summary>
        public float[] Reach = { };

        /// <summary>The frames of a loop's period: its last frame is its first again.</summary>
        public static int Loop(in Entry entry) => Mathf.Max(1, entry.FrameCount - 1);

        /// <summary>The entry baked for <paramref name="clip"/>: false when there is none; <paramref name="stale"/>
        /// when its name matches but its length or frame count does not.</summary>
        public bool TryGet(AnimationClip clip, out Entry entry, out bool stale)
        {
            stale = false;
            foreach (Entry e in Entries)
            {
                if (e.Name != clip.name)
                {
                    continue;
                }

                entry = e;
                stale = Mathf.Abs(clip.length - e.Length) > 1e-4f || GetUpPoses.FramesOf(clip) != e.FrameCount;
                return !stale;
            }

            entry = default;
            return false;
        }

        /// <summary>The clip's normalized time whose stance matches a shown pose (<see cref="Strides.StanceFrame"/>);
        /// -1 when the clip has no strides of both feet.</summary>
        public float StanceTime(in Entry entry, float shownReach, bool leftBears)
        {
            int loop = Loop(entry);
            var reach = new ArraySegment<float>(Reach, entry.FirstFrame, loop);
            int frame = Strides.StanceFrame(reach, entry.Left, entry.Right, shownReach, leftBears);
            return frame < 0 ? -1f : frame / (float)loop;
        }
    }
}
