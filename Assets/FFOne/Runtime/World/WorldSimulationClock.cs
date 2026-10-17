using UnityEngine;

namespace FFOne.World
{
    public static class WorldSimulationClock
    {
        public static bool Controlled;
        static double elapsed;
        public static float Seconds => Controlled ? (float)elapsed : Time.time;
        public static double ExactSeconds => Controlled ? elapsed : Time.timeAsDouble;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reset() { Controlled = false; elapsed = 0; }
        public static void BeginControlled() { Controlled = true; elapsed = 0; }
        public static void Advance() { if (Controlled) elapsed += Time.deltaTime; }
    }
}
