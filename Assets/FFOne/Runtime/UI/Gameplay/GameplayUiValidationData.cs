#if UNITY_EDITOR || DEVELOPMENT_BUILD
using FFOne.Network;

namespace FFOne.UI
{
    /// <summary>Explicit offline fixture for rendering tests; never applied to an authenticated session.</summary>
    public static class GameplayUiValidationData
    {
        public static WorldEntry Entry()
        {
            var load = new byte[2688];
            load[81] = 1;
            Protocol0104.Write16(load, 86, 5); // Actual Computress guide identity for offline UI validation.
            Protocol0104.Write64(load, 4, 9001);
            Protocol0104.WriteUtf16(load, 14, 9, "UI");
            Protocol0104.WriteUtf16(load, 32, 17, "Validation");
            Protocol0104.Write32(load, 104, 12345);
            Protocol0104.Write32(load, 96, 25); Protocol0104.Write32(load, 100, 50);
            for (int nano = 1; nano <= 3; nano++)
            {
                Protocol0104.Write16(load, 1444 + nano * 6, nano);
                Protocol0104.Write16(load, 1446 + nano * 6, 1);
                Protocol0104.Write16(load, 1448 + nano * 6, nano * 30);
                Protocol0104.Write16(load, 1666 + (nano - 1) * 2, nano);
            }
            Protocol0104.Write16(load, 1672, -1);
            // Starter clothing IDs exist in the accepted item catalog. Protocol slot/type ownership is retained.
            Item(load, 136 + 1 * 12, 1, 1, 1);
            Item(load, 244, 1, 1, 1);
            Item(load, 244 + 49 * 12, 2, 1, 1);
            Protocol0104.Write32(load, 2032, 2248);
            Protocol0104.Write32(load, 2500, 2248);
            return new WorldEntry { PlayerId = 90, CharacterUid = 9001, Level = 1, Hp = 700,
                FusionMatter = 2500, MapNumber = 0, RawLoadData = load };
        }

        static void Item(byte[] bytes, int offset, short type, short id, int option)
        {
            bytes[offset] = (byte)type; bytes[offset + 1] = (byte)(type >> 8);
            bytes[offset + 2] = (byte)id; bytes[offset + 3] = (byte)(id >> 8);
            Protocol0104.Write32(bytes, offset + 4, option);
        }

        public static GameplayUiSession Session()
        {
            var result = new GameplayUiSession(_ => new GameplayLevelLimits(1000, 10000));
            result.SetWorld(Entry());
            return result;
        }
    }
}
#endif
