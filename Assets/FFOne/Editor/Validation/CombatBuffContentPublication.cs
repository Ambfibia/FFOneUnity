using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using FFOne.Content;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace FFOne.Editor
{
    /// <summary>Bounded publication of final, editable buff closures produced by FusionForge.</summary>
    public static class CombatBuffContentPublication
    {
        static readonly int[] Effects = { 368, 370, 411, 412, 429, 431, 434, 435, 436, 441, 447, 714, 805 };
        const string DataRoot = "Assets/FFOne/Data/Tables/";
        const string ReceiptRoot = "Assets/FFOne/Editor/ImportMetadata/NativeDataPublications/";
        sealed class Plan
        {
            public string Identity, AssetPath, ReceiptPath, Json, Canonical, SourceHash;
            public NativeDataDocument Existing;
            public bool HasReceipt;
        }
        static string Hash(byte[] bytes)
        { using var sha = SHA256.Create(); return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", ""); }
        static string TextHash(string value) => Hash(Encoding.UTF8.GetBytes(value));

        static Plan Preflight(int id)
        {
            string identity = "map/shared/effects/es" + id + ".closure.json";
            // This adapter consumes final native publications only. It never reads bundles or dumps.
            string source = Path.GetFullPath("../FFOneClient/assets/game/" + identity);
            byte[] bytes = File.ReadAllBytes(source);
            string json = Encoding.UTF8.GetString(bytes).TrimStart('\ufeff');
            var value = JObject.Parse(json, new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error });
            if ((string)value["schema"] != "ffone.tutorial-effect-closure.v1" || (int?)value["effectId"] != id ||
                value["objects"] is not JArray objects || objects.Count == 0 || (long?)value["rootPathId"] is not long root || root <= 0)
                throw new InvalidDataException("Invalid final buff closure identity: " + identity);
            var objectIds = new HashSet<long>();
            bool foundRoot = false;
            foreach (var item in objects)
            {
                if ((long?)item["pathId"] is not long pathId || pathId <= 0 || !objectIds.Add(pathId) || item["value"] is not JObject)
                    throw new InvalidDataException("Invalid or repeated buff closure object identity: " + identity);
                if (pathId == root && (string)item["objectType"] == "GameObject") foundRoot = true;
            }
            if (!foundRoot) throw new InvalidDataException("Buff closure root GameObject is missing: " + identity);
            foreach (var reference in value.Descendants())
                if (reference is JObject pointer && pointer["fileId"] != null && pointer["pathId"] != null &&
                    (long)pointer["pathId"] != 0 && !objectIds.Contains((long)pointer["pathId"]))
                    throw new InvalidDataException("Buff closure has an unresolved dependency: " + identity + " / " + pointer["pathId"]);
            var plan = new Plan {
                Identity = identity, AssetPath = DataRoot + identity + ".asset", ReceiptPath = ReceiptRoot + identity,
                Json = json, SourceHash = Hash(bytes)
            };
            var probe = ScriptableObject.CreateInstance<NativeDataDocument>();
            try { probe.Import(json); plan.Canonical = probe.ToJson(); }
            finally { UnityEngine.Object.DestroyImmediate(probe); }
            plan.Existing = AssetDatabase.LoadAssetAtPath<NativeDataDocument>(plan.AssetPath);
            if (File.Exists(plan.AssetPath) && (!plan.Existing || plan.Existing.Identity != identity || plan.Existing.ToJson() != plan.Canonical))
                throw new InvalidDataException("Accepted buff data differs; publication preserved it: " + plan.AssetPath);
            if (!plan.Existing && File.Exists(plan.AssetPath + ".meta"))
                throw new InvalidDataException("Orphan buff data GUID: " + plan.AssetPath);
            plan.HasReceipt = File.Exists(plan.ReceiptPath);
            if (plan.HasReceipt)
            {
                var receipt = JObject.Parse(File.ReadAllText(plan.ReceiptPath));
                if (!plan.Existing || (string)receipt["Guid"] != AssetDatabase.AssetPathToGUID(plan.AssetPath) ||
                    (string)receipt["SourceHash"] != plan.SourceHash || (string)receipt["InitialValueHash"] != TextHash(plan.Canonical))
                    throw new InvalidDataException("Accepted buff publication provenance differs: " + identity);
            }
            return plan;
        }

        [MenuItem("FFOne/Content/Publish Combat Buff Closures")]
        public static void Publish()
        {
            var plans = new List<Plan>();
            // No folder, asset, receipt, or catalog mutation until every input has passed.
            foreach (int id in Effects) plans.Add(Preflight(id));
            int created = 0;
            foreach (var plan in plans)
            {
                if (plan.Existing) continue;
                CompleteContentPublication.Folder(Path.GetDirectoryName(plan.AssetPath));
                var document = ScriptableObject.CreateInstance<NativeDataDocument>();
                document.Identity = plan.Identity; document.Import(plan.Json);
                AssetDatabase.CreateAsset(document, plan.AssetPath);
                plan.Existing = document; created++;
            }
            AssetDatabase.SaveAssets();
            foreach (var plan in plans)
            {
                if (plan.HasReceipt) continue;
                string guid = AssetDatabase.AssetPathToGUID(plan.AssetPath);
                if (string.IsNullOrEmpty(guid)) throw new InvalidDataException("Published buff data has no GUID: " + plan.AssetPath);
                CompleteContentPublication.Folder(Path.GetDirectoryName(plan.ReceiptPath));
                var receipt = new JObject { ["SourceHash"] = plan.SourceHash, ["InitialValueHash"] = TextHash(plan.Canonical), ["Guid"] = guid };
                using var stream = new FileStream(plan.ReceiptPath, FileMode.CreateNew, FileAccess.Write);
                using var writer = new StreamWriter(stream, new UTF8Encoding(false));
                writer.Write(receipt.ToString(Formatting.None));
            }
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            Debug.Log("COMBAT BUFF CLOSURES PUBLISHED: " + plans.Count + " reviewed inputs, " + created + " new documents; accepted GUIDs and values preserved.");
        }
    }
}
