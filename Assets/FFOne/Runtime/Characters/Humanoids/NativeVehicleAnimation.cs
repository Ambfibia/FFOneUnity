namespace FFOne.Content
{
    /// <summary>Both genders' accepted vehicle clips retain the original `end` and `sound` events.</summary>
    public static class NativeVehicleAnimation
    {
        public static float EndSeconds(string name)
        {
            if (name == null || !(name.StartsWith("board_", System.StringComparison.Ordinal) || name.StartsWith("scooter_", System.StringComparison.Ordinal))) return 0;
            string motion = name.Substring(name.IndexOf('_') + 1);
            return motion switch { "jumpstart" => .683333337f, "jumpend" or "jumplandrun" => .350000083f,
                "run" or "runback" => .816666722f, "stand1" or "jump" => 1.350000024f, _ => 0 };
        }
        public static string Sound(string name) => name is "board_jumpstart" or "scooter_jumpstart" ? "Vehicle_JumpStart.wav" :
            name is "board_jumpend" or "scooter_jumpend" ? "Vehicle_JumpLnd.wav" : null;
        public static string NextClip(string name)
        {
            if (EndSeconds(name) <= 0) return null;
            string prefix = name.Substring(0, name.IndexOf('_') + 1);
            return name.Substring(prefix.Length) switch { "jumpstart" => prefix + "jump", "jumpend" => prefix + "stand1",
                "jumplandrun" => prefix + "run", _ => null };
        }
        public const float SoundSeconds = .25f;
        public const float EntryBlendSeconds = .15f;
        public const float ReturnBlendSeconds = .3f;
    }
}
