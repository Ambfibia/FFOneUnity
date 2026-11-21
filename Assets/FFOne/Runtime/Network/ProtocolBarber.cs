using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace FFOne.Network
{
    internal static class ProtocolBarber
    {
        internal const uint Open = 0x130000a5, Confirm = 0x130000a6, Quote = 0x31000138, Result = 0x31000139, Broadcast = 0x3100013a;

        internal static byte[] Appearance(CharacterInfo character)
        {
            var data = new byte[76];
            Protocol0104.Write64(data, 0, character.Uid);
            data[8] = unchecked((byte)checked((sbyte)character.Style.NameCheck));
            Protocol0104.WriteUtf16(data, 10, 9, character.FirstName);
            Protocol0104.WriteUtf16(data, 28, 17, character.LastName);
            var style = character.Style;
            int[] values = { style.Gender, style.FaceStyle, style.HairStyle, style.HairColor, style.SkinColor, style.EyeColor, style.Height, style.Body };
            for (int i = 0; i < values.Length; i++) data[62 + i] = checked((byte)values[i]);
            Protocol0104.Write32(data, 72, style.Class);
            return data;
        }

        internal static int[] Prices(byte[] data)
        {
            if (data.Length != 288) throw new ProtocolException("Invalid barber quote length.");
            var prices = Enumerable.Range(0, 8).Select(i => Protocol0104.Read32(data, i * 4)).ToArray();
            if (prices[0] is < 0 or > 1 || prices.Any(price => price < 0))
                throw new ProtocolException("Invalid barber quote.");
            return prices;
        }
    }

    public sealed partial class FusionClient
    {
        internal Task OpenBarberAsync(int npc, CancellationToken token) =>
            SendGameplayAsync(ProtocolBarber.Open, ProtocolTravel.Words(npc), token);
        internal Task ConfirmBarberAsync(CharacterInfo draft, CancellationToken token) =>
            SendGameplayAsync(ProtocolBarber.Confirm, ProtocolBarber.Appearance(draft), token);
    }
}
