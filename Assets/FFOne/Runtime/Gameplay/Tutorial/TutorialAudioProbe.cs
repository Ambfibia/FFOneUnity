#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Threading;
using UnityEngine;

namespace FFOne.Tutorial
{
    // Attached to the actual AudioListener during an audible validation run.
    public sealed class TutorialAudioProbe : MonoBehaviour
    {
        long samples;
        volatile float peak;
        public long Samples => Interlocked.Read(ref samples);
        public float Peak => peak;
        readonly float[] output = new float[1024];
        void Update()
        {
            AudioListener.GetOutputData(output, 0);
            float maximum = peak;
            foreach (float sample in output) maximum = Mathf.Max(maximum, Mathf.Abs(sample));
            peak = maximum;
            Interlocked.Add(ref samples, output.Length);
        }
    }
}
#endif
