using System;

namespace FFOne.Creation
{
    public static class CharacterCreationRules
    {
        // Row zero is the table sentinel; row one can be the selectable <BLANK>.
        public static int CycleName(int index, int delta, int count)
        {
            if (count < 2) throw new InvalidOperationException("The character name table is empty.");
            int selectable = count - 1;
            return 1 + ((index - 1 + delta) % selectable + selectable) % selectable;
        }

        public static string ComposeLastName(string middle, string last)
        {
            middle = middle == " " ? "" : middle ?? "";
            last = last == " " ? "" : last ?? "";
            if (last.Length > 0)
            {
                char initial = middle.Length == 0 || middle.Contains(" ")
                    ? char.ToUpperInvariant(last[0]) : char.ToLowerInvariant(last[0]);
                last = initial + last.Substring(1);
            }
            return middle + last;
        }

        public static void SplitCustomName(string text, out string first, out string last)
        {
            var parts = (text ?? "").Trim().Split(new[] { ' ' }, 2, StringSplitOptions.RemoveEmptyEntries);
            first = parts.Length > 0 ? parts[0] : "";
            last = parts.Length > 1 ? parts[1].Trim() : "";
        }

        public static bool ValidName(string first, string last) =>
            !string.IsNullOrWhiteSpace(first) && !string.IsNullOrWhiteSpace(last) && first.Length <= 8 && last.Length <= 16;
    }
}
