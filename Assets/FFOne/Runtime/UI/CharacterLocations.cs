using System.Linq;
using System.Text.RegularExpressions;
using FFOne.Content;
using FFOne.Network;
using UnityEngine;
using CharacterInfo = FFOne.Network.CharacterInfo;

namespace FFOne.UI
{
    // Preserves the exact ordered worldname PathID 8 rectangles already recovered by FFOneClient.
    public static class CharacterLocations
    {
        public sealed class Location
        {
            public Rect Bounds;
            public string District, Zone;
            public string Background => Zone switch {
                "The Suburbs" => "suburbs/CSSuburbsBG_", "Downtown" => "downtown/CSDowntownBG_",
                "The Wilds" => "wilds/CSWildsBG_", "The Darklands" => "darklands/CSDarklandsBG_",
                _ => "future/CSFutureBG_"
            };
        }
        static Location Tile(float x, float z, string district, string zone) => Area(x, z, 512, 512, district, zone);
        static Location Area(float x, float z, float width, float height, string district, string zone) =>
            new Location { Bounds = new Rect(x, z, width, height), District = district, Zone = zone };
        static readonly Location[] Areas = {
            Tile(4096, 2560, "Candy Cove", "The Suburbs"),
            Tile(1024, 1536, "Townsville Park", "Downtown"),
            Tile(2560, 512, "Bravo Beach", "Downtown"),
            Tile(1536, 1024, "Orchid Bay", "Downtown"),
            Tile(1536, 512, "Orchid Bay", "Downtown"),
            Tile(2048, 512, "Orchid Bay", "Downtown"),
            Tile(1536, 1536, "Marquee Row", "Downtown"),
            Tile(512, 6144, "Green Maw", "The Darklands"),
            Tile(512, 5632, "Green Maw", "The Darklands"),
            Tile(1024, 5632, "Green Maw", "The Darklands"),
            Tile(3072, 4608, "Forsaken Valley", "The Darklands"),
            Tile(3584, 4608, "Forsaken Valley", "The Darklands"),
            Tile(3584, 5120, "Forsaken Valley", "The Darklands"),
            Tile(2560, 4608, "Dinosaur Pass", "The Darklands"),
            Tile(6144, 4096, "Nowhere", "The Wilds"),
            Tile(6656, 4096, "Nowhere", "The Wilds"),
            Tile(6656, 5120, "Area 51.5", "The Wilds"),
            Tile(6656, 4608, "Nowhere", "The Wilds"),
            Tile(4096, 7168, "Monkey Mountain", "The Wilds"),
            Tile(4608, 7168, "Monkey Foothills", "The Wilds"),
            Tile(5120, 7168, "Monkey Foothills", "The Wilds"),
            Tile(5120, 6656, "Monkey Foothills", "The Wilds"),
            Tile(2048, 2560, "City Point", "Downtown"),
            Tile(2560, 2560, "Galaxy Gardens", "Downtown"),
            Tile(2048, 1536, "Marquee Row", "Downtown"),
            Tile(2560, 1024, "Bravo Beach", "Downtown"),
            Tile(2048, 3072, "Endsville", "The Suburbs"),
            Area(4608, 4608, 382, 85, "Prickly Pines", "The Wilds"),
            Area(4608, 4690, 338, 201, "Prickly Pines", "The Wilds"),
            Area(4608, 4890, 310, 741, "Prickly Pines", "The Wilds"),
            Area(4991, 4608, 129, 85, "Camp Kidney", "The Wilds"),
            Area(4947, 4690, 173, 201, "Camp Kidney", "The Wilds"),
            Area(4919, 4890, 201, 741, "Camp Kidney", "The Wilds"),
            Tile(7680, 5632, "Lower Catacombs", "The Wilds"),
            Tile(7680, 5120, "Upper Catacombs", "The Wilds"),
            Tile(2560, 5120, "Dinosaur Pass", "The Darklands"),
            Tile(1536, 3072, "Habitat Homes", "The Suburbs"),
            Tile(1536, 2560, "City Hall", "Downtown"),
            Tile(2048, 2048, "City Station", "Downtown"),
            Tile(2560, 1536, "Morbucks Towers", "Downtown"),
            Tile(4096, 3584, "Peach Creek Commons", "The Suburbs"),
            Tile(4096, 3072, "Candy Cove", "The Suburbs"),
            Tile(3072, 2048, "Steam Alley", "Downtown"),
            Tile(2048, 6656, "Dark Glade", "The Darklands"),
            Tile(5632, 5120, "Devil's Bluff", "The Wilds"),
            Tile(5632, 4608, "Devil's Canyon", "The Wilds"),
            Tile(3072, 3072, "Genius Grove", "The Suburbs"),
            Tile(3072, 5120, "Forsaken Valley", "The Darklands"),
            Tile(2048, 6144, "Huntor's Crest", "The Darklands"),
            Tile(1024, 2048, "Townsville Park", "Downtown"),
            Tile(4608, 4096, "Wilson Way", "The Suburbs"),
            Tile(1024, 6144, "Green Maw", "The Darklands"),
            Tile(1536, 6144, "Hero's Hollow", "The Darklands"),
            Tile(1024, 6656, "Fuse's Lair", "The Darklands"),
            Tile(1536, 6656, "The Precipice", "The Darklands"),
            Tile(4608, 6144, "Forgotten Falls", "The Wilds"),
            Tile(2560, 3072, "Eternal Vistas", "The Suburbs"),
            Tile(2560, 3584, "Eternal Meadows", "The Suburbs"),
            Tile(5632, 4096, "Pimpleback Mountains", "The Wilds"),
            Tile(5632, 3584, "Haunted Ridge", "The Wilds"),
            Tile(4608, 3584, "Peach Creek Estates", "The Suburbs"),
            Tile(3584, 4096, "Sector V", "The Suburbs"),
            Tile(5120, 4608, "Leakey Lake", "The Wilds"),
            Tile(5120, 4096, "Acorn Flats", "The Wilds"),
            Tile(2560, 2048, "Mojo's Volcano", "Downtown"),
            Tile(4096, 6656, "Monkey Mountain", "The Wilds"),
            Tile(4608, 6656, "Monkey Foothills", "The Wilds"),
            Tile(6144, 4608, "Nowhere", "The Wilds"),
            Tile(6144, 5120, "Area 51.5", "The Wilds"),
            Tile(2048, 3584, "Nuclear Plant", "The Suburbs"),
            Tile(5632, 5632, "The Ruins", "The Wilds"),
            Tile(4608, 3072, "Goat's Junk Yard", "The Suburbs"),
            Tile(3072, 1536, "Offworld Plaza", "Downtown"),
            Tile(1536, 2048, "Townsville Center", "Downtown"),
            Tile(2560, 6144, "Firepits", "The Darklands"),
            Tile(2560, 5632, "Fireswamps", "The Darklands"),
            Tile(3072, 2560, "Tech Square", "Downtown"),
            Tile(3584, 3072, "Pokey Oaks South", "The Suburbs"),
            Tile(3584, 3584, "Pokey Oaks North", "The Suburbs"),
            Tile(5120, 5120, "Mount Blackhead", "The Wilds"),
            Tile(4608, 5632, "Really Twisted Forest", "The Wilds"),
            Tile(5120, 5632, "Twisted Forest", "The Wilds"),
            Tile(2048, 1024, "Orchid Bay", "Downtown"),
            Tile(7168, 3584, "Crystalline Caverns", "The Wilds"),
            Tile(6656, 1024, "Peach Creek Commons", "The Future"),
            Tile(6656, 512, "Candy Cove", "The Future"),
            Tile(5632, 512, "Genius Grove", "The Future"),
            Tile(7168, 1024, "Peach Creek Estates", "The Future"),
            Tile(6144, 1536, "Sector V", "The Future"),
            Tile(6144, 1024, "Pokey Oaks North", "The Future"),
            Tile(6144, 512, "Pokey Oaks South", "The Future"),
            Tile(7168, 512, "Goat's Junk Yard", "The Future"),
            Tile(512, 512, "Tech Square", "The Future"),
        };
        public static Location Resolve(int serverX, int serverY) => Areas.FirstOrDefault(a => a.Bounds.Contains(new Vector2(serverX * .01f, serverY * .01f)));
        static string Localize(string text, string language)
        {
            string key = "content.location.world." + Regex.Replace(text.ToLowerInvariant(), "[^a-z0-9]+", "_").Trim('_');
            string value = LocalizationCatalog.Lookup(key, language);
            return (value == key ? text : value).ToUpperInvariant();
        }
        public static string Describe(CharacterInfo character, string language, bool selected)
        {
            if (!character.Style.AppearanceComplete) return LocalizationCatalog.Lookup("ui.character_select.location.character_creation", language);
            var location = Resolve(character.X, character.Y);
            if (location == null) return LocalizationCatalog.Lookup("ui.character_select.location.unknown", language);
            string key = selected ? "ui.character_select.location.spaced" : "ui.character_select.location.hyphen";
            return LocalizationCatalog.Lookup(key, language).Replace("{district}", Localize(location.District, language)).Replace("{zone}", Localize(location.Zone, language));
        }
    }
}
