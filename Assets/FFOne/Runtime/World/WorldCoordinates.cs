using UnityEngine;

namespace FFOne.World
{
    // Retrobution CoordUtil / AvatarUtil; protocol positions are centimetres, Z-up.
    // MOVE velocity floats are metres/second. JUMP velocity integers are centimetres/second.
    public static class WorldCoordinates
    {
        public static Vector3 FromServer(int x, int y, int z) => new(x * .01f, z * .01f, y * .01f);
        public static Vector3Int ToServer(Vector3 value) => new((int)(value.x * 100), (int)(value.z * 100), (int)(value.y * 100));
        public static int ToServerHeading(float yaw) => (int)Mathf.Repeat(yaw, 360) - 180;
        public static float FromServerHeading(int angle) => Mathf.Repeat(angle + 180, 360);
        public static byte Direction(float side, float forward)
        {
            int[,] keys = { { 6, 5, 4 }, { 7, 0, 3 }, { 8, 1, 2 } };
            return (byte)keys[Sign(forward) + 1, Sign(side) + 1];
        }
        static int Sign(float value) => value > .001f ? 1 : value < -.001f ? -1 : 0;
    }
}
