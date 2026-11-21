using System.Threading;
using System.Threading.Tasks;

namespace FFOne.Network
{
    internal static class ProtocolLauncher
    {
        internal const uint Request = 0x1300003e;

        internal static byte[] Movement((int x, int y, int z) position, float horizontalX, float horizontalY, float vertical, int heading)
        {
            var payload = new byte[40];
            Protocol0104.Write64(payload, 0, System.Diagnostics.Stopwatch.GetTimestamp() * 1000L / System.Diagnostics.Stopwatch.Frequency);
            Protocol0104.Write32(payload, 8, position.x);
            Protocol0104.Write32(payload, 12, position.y);
            Protocol0104.Write32(payload, 16, position.z);
            Protocol0104.Write32(payload, 20, (int)(horizontalX * 100));
            Protocol0104.Write32(payload, 24, (int)(horizontalY * 100));
            Protocol0104.Write32(payload, 32, heading);
            Protocol0104.Write32(payload, 36, (int)(vertical * 100));
            return payload;
        }
    }

    public sealed partial class FusionClient
    {
        internal Task SendLauncherAsync(byte[] payload, CancellationToken cancellation) =>
            SendGameplayAsync(ProtocolLauncher.Request, payload, cancellation);
    }
}
