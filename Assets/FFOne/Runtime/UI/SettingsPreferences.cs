using System;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace FFOne.UI
{
    // Local preferences only. Server-owned account, roster and blocked-list state never belong here.
    public sealed class SettingsPreferences
    {
        public const string Schema = "ffone.unity-options.v1";
        readonly string path;
        readonly int displayWidth, displayHeight;
        public JObject Committed { get; private set; }
        public JObject Draft { get; private set; }
        JObject opening;
        public bool Clean { get; private set; } = true;
        public string LoadError { get; private set; }
        public event Action Changed;

        public SettingsPreferences(string path = null, int displayWidth = 1920, int displayHeight = 1080)
        {
            this.path = path;
            this.displayWidth = displayWidth; this.displayHeight = displayHeight;
            Committed = Defaults(displayWidth, displayHeight);
            if (path != null && File.Exists(path))
            {
                try
                {
                    var saved = JObject.Parse(File.ReadAllText(path));
                    if ((string)saved["schema"] != Schema) throw new InvalidDataException("Unsupported options schema.");
                    var merged = Defaults(displayWidth, displayHeight);
                    merged.Merge(saved, new JsonMergeSettings { MergeArrayHandling = MergeArrayHandling.Replace, MergeNullValueHandling = MergeNullValueHandling.Merge });
                    (merged["display"] as JObject)?.Remove("new_chat");
                    Validate(merged);
                    var bindings = merged["input"]["bindings"];
                    foreach (var action in GameplayInput.Map.actions)
                    {
                        var row = bindings[action.name];
                        foreach (var slot in action.bindings)
                            row[slot.name] = SettingsInputDefaults.ControlPath((string)row[slot.name], slot.name);
                    }
                    Committed = merged;
                }
                catch (Exception e) { LoadError = e.Message; }
            }
            Open();
        }
        public static JObject Defaults(int displayWidth = 1920, int displayHeight = 1080)
        {
            var defaults = JObject.Parse(@"{
          'schema':'ffone.unity-options.v1', 'text_locale':'en', 'voice_locale':'en',
          'graphics':{'width':1920,'height':1080,'windowed':true,'detail':2,'visibility':5,'particles':2,
            'toon':true,'glow':true,'anisotropic':true,'fade':true,'vegetation':true,'shadow':0,'texture':0},
          'sound':{'master':5,'effects':5,'voice':5,'ambient':5,'music':5,
            'master_enabled':true,'effects_enabled':true,'voice_enabled':true,'ambient_enabled':true,'music_enabled':true},
          'display':{'my_name':false,'other_name':false,'group_name':false,'npc_name':false,'monster_name':false,
            'floating_numbers':false,'balloon':true,'scale_ui':true,'game_hint':true,
            'npc_messages_in_chat':true,'combat_in_chat':false,'animated_nanos':true},
          'colors':{'general':1,'buddy':2,'group':3},
          'social':{'group':true,'buddy':true,'trade':true},
          'input':{'invert_y':false,'sensitivity':5,'pad_invert_y':false,'pad_sensitivity':5,'pad_profile':0}
        }");
            defaults["input"]["bindings"] = SettingsInputDefaults.Create();
            var resolution = FrontendLayout.DefaultResolution(displayWidth, displayHeight);
            defaults["graphics"]["width"] = resolution.x;
            defaults["graphics"]["height"] = resolution.y;
            return defaults;
        }
        public void Open() { opening = Copy(Committed); Draft = Copy(Committed); Clean = true; }
        public T Get<T>(string key) => Draft.SelectToken(key).Value<T>();
        public void Set(string key, object value)
        {
            var token = JToken.FromObject(value);
            if (JToken.DeepEquals(Draft.SelectToken(key), token)) return;
            Put(Draft, key, token); Clean = false;
            // Sound is applied eagerly, but dirty cancellation restores the opening options snapshot.
            if (key.StartsWith("sound.", StringComparison.Ordinal))
            { Committed["sound"] = Draft["sound"].DeepClone(); Persist(); }
        }
        public void SetLocale(string key, string locale)
        {
            if (key != "text_locale" && key != "voice_locale") throw new ArgumentException(nameof(key));
            if (locale != "en" && locale != "ru") throw new ArgumentException(nameof(locale));
            Draft[key] = Committed[key] = opening[key] = locale;
            Persist();
        }
        public void Reset(string group)
        {
            var bindings = Draft["input"]["bindings"].DeepClone();
            var pad = Draft["input"]["pad_profile"].DeepClone();
            Draft[group] = Defaults(displayWidth, displayHeight)[group].DeepClone(); Clean = false;
            if (group == "input") { Draft[group]["bindings"] = bindings; Draft[group]["pad_profile"] = pad; }
            if (group == "sound") { Committed[group] = Draft[group].DeepClone(); Persist(); }
        }
        public void Preset(int index)
        {
            if (index < 0 || index > 4) return;
            Set("graphics.detail", index);
            Set("graphics.visibility", new[] { 10, 7.5f, 5, 2.5f, 0 }[index]);
            Set("graphics.particles", new[] { 3, 3, 2, 1, 0 }[index]);
            foreach (var key in new[] { "toon", "glow" }) Set("graphics." + key, index < 4);
            foreach (var key in new[] { "anisotropic", "fade" }) Set("graphics." + key, index < 3);
            Set("graphics.vegetation", index < 2); Set("graphics.shadow", 0);
            Set("graphics.texture", index < 3 ? 0 : index - 2);
        }
        public void Apply() { Committed = Copy(Draft); Persist(); Clean = true; }
        public void Cancel()
        {
            if (Clean) { Apply(); return; }
            var input = Committed["input"].DeepClone();
            Committed = Copy(opening); Committed["input"] = input;
            Draft = Copy(Committed); Persist(); Clean = true;
        }
        void Persist()
        {
            if (LoadError != null) throw new IOException("Options file was not overwritten: " + LoadError);
            Validate(Committed);
            if (path != null)
            {
                var directory = Path.GetDirectoryName(Path.GetFullPath(path));
                Directory.CreateDirectory(directory);
                var temporary = path + ".tmp";
                File.WriteAllText(temporary, Committed.ToString(Formatting.Indented));
                if (File.Exists(path)) File.Replace(temporary, path, null);
                else File.Move(temporary, path);
            }
            Changed?.Invoke();
        }
        static JObject Copy(JObject value) => (JObject)value.DeepClone();
        static void Validate(JObject value)
        {
            void Shape(JToken actual, JToken expected, string key)
            {
                if (expected is JObject obj)
                {
                    if (!(actual is JObject)) throw new InvalidDataException("Invalid options object: " + key);
                    foreach (var property in obj.Properties()) Shape(actual[property.Name], property.Value, key + "." + property.Name);
                    return;
                }
                bool numeric = expected.Type == JTokenType.Integer || expected.Type == JTokenType.Float;
                if (actual == null || (numeric ? actual.Type != JTokenType.Integer && actual.Type != JTokenType.Float : actual.Type != expected.Type))
                    throw new InvalidDataException("Invalid options value: " + key);
                if (numeric && (double.IsNaN((double)actual) || double.IsInfinity((double)actual))) throw new InvalidDataException("Non-finite options value: " + key);
            }
            void Range(string key, double min, double max, bool integral = true)
            {
                double number = value.SelectToken(key).Value<double>();
                if (number < min || number > max || integral && number != Math.Truncate(number)) throw new InvalidDataException("Out-of-range options value: " + key);
            }
            Shape(value, Defaults(), "options");
            if ((string)value["text_locale"] != "en" && (string)value["text_locale"] != "ru" ||
                (string)value["voice_locale"] != "en" && (string)value["voice_locale"] != "ru") throw new InvalidDataException("Unsupported options locale.");
            Range("graphics.width", 320, 16384); Range("graphics.height", 200, 16384); Range("graphics.detail", 0, 5);
            Range("graphics.visibility", 0, 10, false); Range("graphics.particles", 0, 3); Range("graphics.shadow", 0, 1); Range("graphics.texture", 0, 2);
            foreach (string key in new[] { "master", "effects", "voice", "ambient", "music" }) Range("sound." + key, 0, 10);
            foreach (string key in new[] { "general", "buddy", "group" }) Range("colors." + key, 0, 17);
            Range("input.sensitivity", 1, 10); Range("input.pad_sensitivity", 1, 10); Range("input.pad_profile", 0, 2);
        }
        static void Put(JObject root, string key, JToken value)
        {
            int split = key.LastIndexOf('.');
            var owner = split < 0 ? root : (JObject)root.SelectToken(key.Substring(0, split));
            owner[key.Substring(split + 1)] = value;
        }
    }
}
