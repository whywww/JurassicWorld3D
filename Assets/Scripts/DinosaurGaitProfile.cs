using System;
using UnityEngine;

namespace JurassicWorld
{
    // Baked in scene scale; paths and sole offsets are relative to the animation rig.
    public sealed class DinosaurGaitProfile : ScriptableObject
    {
        [Serializable]
        public sealed class Foot
        {
            public string path, hipPath, kneePath;
            public Vector3 soleOffset;
            public float[] contact;
        }
        public AnimationClip clip;
        public float groundSpeed;
        public float heightCorrection;
        public float turnSpeed = 2f;
        public float loopStart, loopEnd;
        public Foot[] feet;

        public float Contact(int foot, float time)
        {
            var samples = feet[foot].contact;
            float frame = Mathf.Repeat(time, clip.length) / clip.length * (samples.Length - 1);
            int a = Mathf.FloorToInt(frame);
            return Mathf.Lerp(samples[a], samples[Mathf.Min(a + 1, samples.Length - 1)], frame - a);
        }
    }
}
