using ContentPaths = FFOne.Content.ContentPaths;
using System;
using System.IO;
using System.IO.Compression;

namespace FFOne.Editor
{
    // Unity's color PNG loader reduces Gray16 to color precision. Decode the accepted
    // canonical height PNG losslessly, keeping the original 32767 normalization.
    public static class TutorialHeightmap
    {
        public static float[,] Read(byte[] png, int expectedResolution)
        {
            if (png.Length < 33 || png[0] != 137 || png[1] != 80 || png[2] != 78 || png[3] != 71)
                throw new InvalidDataException("Invalid terrain height PNG.");
            int width = Big(png, 16), height = Big(png, 20);
            if (width != expectedResolution || height != expectedResolution || png[24] != 16 || png[25] != 0 || png[28] != 0)
                throw new InvalidDataException("Tutorial terrain requires noninterlaced Gray16 at the authored resolution.");
            using var packed = new MemoryStream();
            for (int offset = 8; offset + 12 <= png.Length;)
            {
                int count = Big(png, offset);
                if (count < 0 || offset + 12L + count > png.Length) throw new InvalidDataException("Truncated PNG chunk.");
                if (png[offset + 4] == 73 && png[offset + 5] == 68 && png[offset + 6] == 65 && png[offset + 7] == 84)
                    packed.Write(png, offset + 8, count);
                offset += count + 12;
            }
            byte[] payload = packed.ToArray();
            if (payload.Length < 6) throw new InvalidDataException("Missing PNG data.");
            using var input = new MemoryStream(payload, 2, payload.Length - 6);
            using var inflate = new DeflateStream(input, CompressionMode.Decompress);
            using var output = new MemoryStream(); inflate.CopyTo(output);
            byte[] encoded = output.ToArray();
            int stride = width * 2;
            if (encoded.Length != height * (stride + 1)) throw new InvalidDataException("Unexpected PNG scanline size.");
            var decoded = new byte[height * stride];
            for (int y = 0; y < height; y++)
            {
                int filter = encoded[y * (stride + 1)];
                if (filter > 4) throw new InvalidDataException("Unsupported PNG filter.");
                for (int x = 0; x < stride; x++)
                {
                    int a = x >= 2 ? decoded[y * stride + x - 2] : 0;
                    int b = y > 0 ? decoded[(y - 1) * stride + x] : 0;
                    int c = y > 0 && x >= 2 ? decoded[(y - 1) * stride + x - 2] : 0;
                    int prediction = filter == 1 ? a : filter == 2 ? b : filter == 3 ? (a + b) / 2 : filter == 4 ? Paeth(a, b, c) : 0;
                    decoded[y * stride + x] = unchecked((byte)(encoded[y * (stride + 1) + x + 1] + prediction));
                }
            }
            var result = new float[height, width];
            for (int y = 0; y < height; y++) for (int x = 0; x < width; x++)
            {
                int at = y * stride + 2 * x;
                result[y, x] = (decoded[at] * 256 + decoded[at + 1]) / 32767f;
            }
            return result;
        }
        static int Big(byte[] bytes, int offset) => (bytes[offset] << 24) | (bytes[offset + 1] << 16) | (bytes[offset + 2] << 8) | bytes[offset + 3];
        static int Paeth(int a, int b, int c)
        {
            int p = a + b - c, pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c);
            return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
        }
    }
}
