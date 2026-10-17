using ContentPaths = FFOne.Content.ContentPaths;
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using FFOne.Content;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace FFOne.Editor
{
    public sealed class LocalizationEditorWindow : EditorWindow
    {
        JObject english, russian;
        string englishOriginal, russianOriginal, key = "", en = "", ru = "", search = "", status = "";
        Vector2 scroll;
        bool dirty;
        static string PathFor(string language) => NativeContentImport.Destination + "/localization/" + language + ".json";
        static string Read(string language) => NativeData.Load("localization/" + language) ? NativeData.Read("localization/" + language) : File.ReadAllText(ContentPaths.FilePath(PathFor(language)));
        [MenuItem("FFOne/Localization EN-RU")]
        static void Open() => GetWindow<LocalizationEditorWindow>("FFOne EN/RU");
        void OnEnable() => Load();
        void Load()
        {
            if (!File.Exists(ContentPaths.FilePath(PathFor("en"))) || !File.Exists(ContentPaths.FilePath(PathFor("ru")))) { status = "Import native content first."; return; }
            englishOriginal = Read("en");
            russianOriginal = Read("ru");
            english = JObject.Parse(englishOriginal);
            russian = JObject.Parse(russianOriginal);
            dirty = false;
        }
        void OnGUI()
        {
            EditorGUILayout.LabelField("Bilingual runtime localization", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Edits stay in FFOneUnity. Native FFOneClient source files remain read-only. Preserve both languages and matching placeholders.", MessageType.Info);
            if (english == null) { EditorGUILayout.LabelField(status); if (GUILayout.Button("Reload")) Load(); return; }
            search = EditorGUILayout.TextField("Filter keys", search);
            scroll = EditorGUILayout.BeginScrollView(scroll, GUILayout.Height(position.height * .35f));
            foreach (var property in ((JObject)english["entries"]!).Properties().Where(p => p.Name.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0).Take(150))
                if (GUILayout.Button(property.Name, key == property.Name ? EditorStyles.boldLabel : EditorStyles.label)) Select(property.Name);
            EditorGUILayout.EndScrollView();
            EditorGUI.BeginChangeCheck();
            key = EditorGUILayout.TextField("Key", key);
            EditorGUILayout.LabelField("English"); en = EditorGUILayout.TextArea(en, GUILayout.MinHeight(60));
            EditorGUILayout.LabelField("Русский"); ru = EditorGUILayout.TextArea(ru, GUILayout.MinHeight(60));
            dirty |= EditorGUI.EndChangeCheck();
            using (new EditorGUI.DisabledScope(!dirty)) if (GUILayout.Button("Save both languages")) Save();
            EditorGUILayout.LabelField(status, EditorStyles.wordWrappedLabel);
        }
        void Select(string value)
        {
            if (dirty && !EditorUtility.DisplayDialog("Unsaved translation", "Discard the unsaved translation?", "Discard", "Keep editing")) return;
            key = value; en = (string)english["entries"]![key] ?? ""; ru = (string)russian["entries"]![key] ?? ""; dirty = false;
        }
        void Save()
        {
            try
            {
                if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(en) || string.IsNullOrWhiteSpace(ru)) throw new InvalidDataException("Key and both translations are required.");
                string[] Tokens(string value) => Regex.Matches(value, @"\{[^{}]+\}|%[0-9]*[sdf]").Cast<Match>().Select(m => m.Value).OrderBy(x => x).ToArray();
                if (!Tokens(en).SequenceEqual(Tokens(ru))) throw new InvalidDataException("EN/RU placeholder sets differ.");
                if (Read("en") != englishOriginal || Read("ru") != russianOriginal) throw new IOException("Localization changed outside this window. Reopen before saving.");
                var nextEnglish = (JObject)english.DeepClone(); var nextRussian = (JObject)russian.DeepClone();
                nextEnglish["entries"]![key] = en; nextRussian["entries"]![key] = ru;
                var utf8 = new UTF8Encoding(false);
                var enText = nextEnglish.ToString(Formatting.Indented) + "\n"; var ruText = nextRussian.ToString(Formatting.Indented) + "\n";
                var enAsset = NativeData.Load("localization/en"); var ruAsset = NativeData.Load("localization/ru");
                if (enAsset && ruAsset)
                {
                    Undo.RecordObjects(new UnityEngine.Object[] { enAsset, ruAsset }, "Edit bilingual native data");
                    enAsset.Import(enText); ruAsset.Import(ruText);
                    EditorUtility.SetDirty(enAsset); EditorUtility.SetDirty(ruAsset);
                    AssetDatabase.SaveAssetIfDirty(enAsset); AssetDatabase.SaveAssetIfDirty(ruAsset);
                }
                else
                {
                    if (enAsset || ruAsset) throw new IOException("Complete both language publications before editing.");
                    File.WriteAllText(ContentPaths.FilePath(PathFor("en")), enText, utf8);
                    try { File.WriteAllText(ContentPaths.FilePath(PathFor("ru")), ruText, utf8); }
                    catch { File.WriteAllText(ContentPaths.FilePath(PathFor("en")), englishOriginal, utf8); throw; }
                    AssetDatabase.ImportAsset(ContentPaths.Resolve(PathFor("en"))); AssetDatabase.ImportAsset(ContentPaths.Resolve(PathFor("ru")));
                }
                english = nextEnglish; russian = nextRussian; englishOriginal = Read("en"); russianOriginal = Read("ru");
                LocalizationCatalog.Reload(); dirty = false; status = "Saved EN/RU. Reopen the current screen to refresh its labels.";
            }
            catch (Exception exception) { status = exception.Message; }
        }
    }
}
