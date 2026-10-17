using ContentPaths = FFOne.Content.ContentPaths;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;

namespace FFOne.Editor
{
    public static class EnvironmentPublicationUpgrade
    {
        static Queue<string> pending;
        static readonly HashSet<string> registered = new();
        public static string Status { get; private set; } = "";
        public static bool Running => pending != null;
        static int completed;
        // Explicit migration accepts today's source as the baseline without changing any published artist work.
        public static void Begin()
        {
            if (Running) throw new InvalidOperationException("Upgrade is running.");
            EnvironmentSharedAssets.ResetIndex(); registered.Clear(); completed = 0;
            pending = new Queue<string>(ContentPaths.GetFiles(NativeAssetPublisher.ManifestRoot + "/objects", "publication.json", SearchOption.AllDirectories));
            EditorApplication.update += Advance;
            Status = "Upgrading environment publication metadata";
        }
        public static void Advance()
        {
            if (!Running || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            try
            {
                var timer = System.Diagnostics.Stopwatch.StartNew();
                for (int i = 0; i < 32 && pending.Count > 0 && timer.ElapsedMilliseconds < 250; i++)
                {
                    string file = pending.Peek();
                    var manifest = JsonConvert.DeserializeObject<NativeAssetPublisher.Manifest>(File.ReadAllText(ContentPaths.FilePath(file)));
                    if (!manifest.SourcePath.StartsWith("Assets/Resources/objects/", StringComparison.Ordinal)) throw new InvalidDataException("Not environment.");
                    foreach (var entry in manifest.Assets.Where(e => e.Type == "UnityEngine.Mesh" || e.Type == "UnityEngine.Material"))
                    {
                        if (AssetDatabase.AssetPathToGUID(ContentPaths.Resolve(entry.Path)) != entry.Guid) throw new InvalidDataException("Shared GUID mismatch: " + entry.Path);
                        if (registered.Add(entry.Guid)) EnvironmentSharedAssets.Register(AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(ContentPaths.Resolve(entry.Path)));
                    }
                    if (string.IsNullOrEmpty(manifest.SourceContentHash))
                    {
                        manifest.SourceContentHash = EnvironmentSharedAssets.SourceContentHash(manifest.SourcePath);
                        manifest.SourceDependencyHash = AssetDatabase.GetAssetDependencyHash(ContentPaths.Resolve(manifest.SourcePath)).ToString();
                        File.WriteAllText(ContentPaths.FilePath(file), JsonConvert.SerializeObject(manifest, Formatting.Indented));
                    }
                    completed++; pending.Dequeue();
                }
                Status = $"Publication metadata: {completed} complete, {pending.Count} remaining; {registered.Count} shared resources indexed";
                if (pending.Count == 0)
                {
                    EnvironmentSharedAssets.SaveIndex();
                    pending = null; EditorApplication.update -= Advance;
                    File.WriteAllText(ContentPaths.FilePath("Logs/EnvironmentPublicationUpgrade.json"), JsonConvert.SerializeObject(new { completed, shared = registered.Count, status = "complete" }, Formatting.Indented));
                    Debug.Log(Status);
                }
            }
            catch (Exception e) { pending = null; EditorApplication.update -= Advance; Status = e.ToString(); Debug.LogException(e); }
        }
    }
}
