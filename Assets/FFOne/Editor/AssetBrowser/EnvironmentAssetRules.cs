using ContentPaths = FFOne.Content.ContentPaths;
using System;
using System.Linq;
using System.Text.RegularExpressions;

namespace FFOne.Editor
{
    // Only map objects enter this library. Character IDs and their placement belong to the NPC/XDT tools.
    public static class EnvironmentAssetRules
    {
        public const string Root = "Assets/Resources/Environment";
        public static readonly string[] Categories = {
            "Vegetation/Trees", "Vegetation/Shrubs", "Vegetation/Grass", "Vegetation/Flowers", "Vegetation/Vines", "Vegetation/Cacti", "Vegetation/Deadwood",
            "Geology/Rocks", "Geology/Cliffs", "Geology/Islands", "Geology/Ground",
            "Buildings/Residential", "Buildings/Shops", "Buildings/Civic", "Buildings/Industrial", "Buildings/Landmarks",
            "Architecture/Walls", "Architecture/Roofs", "Architecture/Doors", "Architecture/Windows", "Architecture/Floors", "Architecture/Stairs", "Architecture/Columns",
            "Infrastructure/Roads", "Infrastructure/Bridges", "Infrastructure/Fences", "Infrastructure/Railways", "Infrastructure/Pipes", "Infrastructure/Power", "Infrastructure/Platforms",
            "StreetFurniture/Benches", "StreetFurniture/Seating", "StreetFurniture/Tables", "StreetFurniture/Lights", "StreetFurniture/BusStops", "StreetFurniture/Bins", "StreetFurniture/Hydrants", "StreetFurniture/Signs", "StreetFurniture/Planters",
            "Props/Containers", "Props/Industrial", "Props/Household", "Props/Decoration", "Props/Statues", "Props/Graves", "Props/Sports",
            "Vehicles/Cars", "Vehicles/Trucks", "Vehicles/Boats", "Vehicles/Aircraft", "Vehicles/Trains", "Vehicles/Construction",
            "Attractions/Rides", "Attractions/Stages", "Water/Surfaces", "Water/Fountains", "Water/Waterfalls",
            "Special/Interactive", "Special/Animated", "Technical/Collision", "Technical/Effects", "Review/Unclassified"
        };
        sealed class Rule
        {
            public string Category;
            public Regex Pattern;
            public Rule(string category, string expression) { Category = category; Pattern = new Regex(expression, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant); }
        }
        // Explicit compounds precede broad nouns: power plants, bus stops, tree houses and streetlights are not vegetation/vehicles.
        static readonly Rule[] Rules = {
            new("Technical/Collision", @"collision|collider"),
            new("Special/Interactive", @"jumppad|launchers|warp.?gate|timegate|checkpoint|switch|forcefieldgenerator|fusion.?breeder"),
            new("Water/Waterfalls", @"waterfall"),
            new("Water/Fountains", @"fountain"),
            new("StreetFurniture/Hydrants", @"hydrant"),
            new("StreetFurniture/BusStops", @"busstop|bus.?platform|bus.?t[ae]rminal"),
            new("StreetFurniture/Lights", @"streetli(?:ght|nght)|street.?lamp|lamppost|roadsignal|traffic.?light"),
            new("StreetFurniture/Planters", @"plantbed|gardenblock|planter|flowerpot"),
            new("Buildings/Industrial", @"nuclearplant|generatingplant|powerplant|power.?station|factory|industries|refinery|crematory"),
            new("Buildings/Residential", @"treehouse|treetop.?village|housing|(?:^|_)house|bath.?house|villa|(?:^|_)apt(?:_|\d)|apartment"),
            new("Vehicles/Construction", @"bulldozer|towercrane|rail.?crane|excavator|forklift"),
            new("Infrastructure/Bridges", @"bridge"),
            new("Infrastructure/Fences", @"fence|fance|railing"),
            new("Infrastructure/Railways", @"monorail(?!.*car)|railroad|railway|railtrack"),
            new("Infrastructure/Roads", @"highway|sidewalk|parkingplace|crosswalk|(?:^|_)road|asphalt"),
            new("Infrastructure/Pipes", @"pipe|sewer|duct"),
            new("Infrastructure/Power", @"powerline|electric|antenna|antena|pylon|cable"),
            new("StreetFurniture/Benches", @"bench"), new("StreetFurniture/Seating", @"chair|sofa|stool"),
            new("StreetFurniture/Tables", @"(?:^|[_-])(?:table|desk)(?:[_-]|\d|$)|tabletop"), new("StreetFurniture/Bins", @"trash|garbage|dustbin"),
            new("StreetFurniture/Signs", @"sign|billboard|banner|poster"),
            new("Props/Graves", @"gravestone|tombstone|coffin|charnel"), new("Props/Statues", @"statue|monument|sculpture"),
            new("Props/Containers", @"container|barrel|crate|(?:^|_)box(?:_|$)|(?:^|_)drum"),
            new("Props/Sports", @"basketball|baseball|sports|goalpost|tennis"),
            new("Props/Household", @"toilet|bathtub|kitchen|vending|telephone|mailbox|clock"),
            new("Attractions/Rides", @"roller.?coaster|merry.?go.?round|ferris|bumpercar|bumperstage|circus"),
            new("Attractions/Stages", @"bandshell|stage|theater|theatre"),
            new("Vehicles/Aircraft", @"airplane|helicopter|airship|spaceship"), new("Vehicles/Boats", @"ship|boat|submarine"),
            new("Vehicles/Trains", @"monorail.*car|train"), new("Vehicles/Trucks", @"truck|firecar|ambulance|(?:^|_)bus(?:_|\d)"),
            new("Vehicles/Cars", @"passengercar|bumpercar|(?:^|_)car(?:_|\d)|taxi|weagon|wagon"),
            new("Buildings/Shops", @"store|shop|mall|market|hotel|restaurant"),
            new("Buildings/Civic", @"school|hospital|firestation|office|library|station"),
            new("Buildings/Landmarks", @"headquarters|pavilion|castle|building|(?:^|_)buil\d|(?:^|_)tower"),
            new("Architecture/Windows", @"window|glass"), new("Architecture/Doors", @"door|gate"),
            new("Architecture/Roofs", @"roof|chimney"), new("Architecture/Stairs", @"stair|ladder|steps"),
            new("Architecture/Columns", @"column|pillar|piller"), new("Architecture/Walls", @"wall"),
            new("Architecture/Floors", @"floor|tile"), new("Infrastructure/Platforms", @"platform|pier|wharf"),
            new("Vegetation/Cacti", @"cactus|cacti"), new("Vegetation/Vines", @"vine|ivy"),
            new("Vegetation/Deadwood", @"stump|treeroot|(?:^|_)log(?:_|$)|deadwood"),
            new("Vegetation/Grass", @"grass|fern|reed"), new("Vegetation/Flowers", @"flower|blossom"),
            new("Vegetation/Shrubs", @"bush|shrub|hedge|(?:^|_)plant(?:_|\d)"),
            new("Vegetation/Trees", @"(?:^|_)(?:dark|park|burnin_)?trees?(?:_|\d)|palm|bamboo"),
            new("Geology/Cliffs", @"cliff|canyon|mountain"), new("Geology/Rocks", @"rock|stone"),
            new("Geology/Islands", @"island"), new("Geology/Ground", @"ground|landscape|sand|(?:^|_)land(?:_|\d)"),
            new("Water/Surfaces", @"water|ocean|river|lake|pool|ffpoison"),
            new("Props/Industrial", @"machine|engine|generator|tank|turbine|gear"),
            new("Technical/Effects", @"effect|explosion|blackhole|(?:^|_)eff(?:_|\d)|(?:^|_)ef\d")
        };
        public static bool IsEnvironment(string identity) => identity != null && identity.Replace('\\', '/').StartsWith("objects/", StringComparison.Ordinal);
        public static string Classify(string identity, string displayName = null)
        {
            if (!IsEnvironment(identity)) throw new ArgumentException("Only map objects belong in the environment library.", nameof(identity));
            var parts = identity.Split('/');
            string text = (string.Join("_", parts.Skip(2)) + "_" + displayName).Replace(' ', '_');
            foreach (var rule in Rules) if (rule.Pattern.IsMatch(text)) return rule.Category;
            return "Review/Unclassified";
        }
        public static string DirectoryFor(string identity, string category)
        {
            if (!IsEnvironment(identity) || !Categories.Contains(category)) throw new ArgumentException("Invalid environment classification.");
            string relative = identity.Substring("objects/".Length);
            if (relative.EndsWith(".glb", StringComparison.OrdinalIgnoreCase)) relative = relative.Substring(0, relative.Length - 4);
            if (relative.Split('/').Any(p => p == ".." || p == "." || p == "")) throw new ArgumentException("Invalid source identity.");
            var parts = relative.Split('/');
            string family = parts.Length > 1 ? parts[1] : parts[0];
            string model = parts.Length > 3 ? parts[3] : parts[parts.Length - 1];
            string role = parts[parts.Length - 1];
            string key = UnityEngine.Hash128.Compute(identity).ToString().Substring(0, 12);
            return Root + "/" + category + "/" + family + "/" + model + "_" + role + "_" + key;
        }
    }
}
