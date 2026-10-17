using ContentPaths = FFOne.Content.ContentPaths;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using UnityEditor;

namespace FFOne.Editor
{
    public static class EnvironmentPublicationValidation
    {
        static Queue<string> pending;
        static readonly List<object> failures = new();
        static int passed;
        public static bool Running => pending != null;
        public static string Status { get; private set; } = "";
        [MenuItem("FFOne/Environment/Validate Published Resources")]
        public static void Begin()
        {
            if (Running) return;
            pending = new Queue<string>(ContentPaths.GetFiles(NativeAssetPublisher.ManifestRoot + "/objects", "publication.json", SearchOption.AllDirectories));
            passed = 0; failures.Clear(); EditorApplication.update += Advance;
        }
        static void Advance()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode) return;
            var timer = System.Diagnostics.Stopwatch.StartNew();
            for (int i = 0; i < 24 && pending.Count > 0 && timer.ElapsedMilliseconds < 200; i++)
            {
                string file = pending.Dequeue();
                try
                {
                    var manifest = JsonConvert.DeserializeObject<NativeAssetPublisher.Manifest>(File.ReadAllText(ContentPaths.FilePath(file)));
                    var plan = NativeAssetPublisher.Preflight(manifest.SourcePath);
                    if (!plan.AlreadyPublished) throw new InvalidDataException("Missing publication");
                    passed++;
                }
                catch (Exception e) { failures.Add(new { file, error = e.Message }); }
            }
            Status = $"Validated {passed}; failures {failures.Count}; remaining {pending.Count}";
            if (pending.Count != 0) return;
            pending = null; EditorApplication.update -= Advance;
            File.WriteAllText(ContentPaths.FilePath("Logs/EnvironmentPublicationValidation.json"), JsonConvert.SerializeObject(new { passed, failures = failures.ToArray() }, Formatting.Indented));
        }
    }
}
