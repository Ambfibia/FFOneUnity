using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using FFOne.Content;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace FFOne.Editor
{
    /// <summary>Explicitly publish the reviewed server XDT revision without replacing its GUID.</summary>
    public static class InstanceRoutesPublication
    {
        const string Source = "Assets/FFOne/Data/Gameplay/Source/tables/xdt.json";
        const string Asset = "Assets/FFOne/Data/Tables/data/tables/xdt.json.asset";
        const string Receipt = "Assets/FFOne/Editor/ImportMetadata/NativeDataPublications/data/tables/xdt.json";
        static string Hash(byte[] bytes)
        { using var hash = SHA256.Create(); return BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-", ""); }
        static string TextHash(string value) => Hash(Encoding.UTF8.GetBytes(value));

        public static void Publish()
        {
            string server = Path.GetFullPath("../RustyFusion/tabledata/xdt.json");
            var receipt = JObject.Parse(File.ReadAllText(Receipt));
            var data = AssetDatabase.LoadAssetAtPath<NativeDataDocument>(Asset);
            if (!data || data.Identity != "data/tables/xdt.json" ||
                AssetDatabase.AssetPathToGUID(Asset) != (string)receipt["Guid"])
                throw new InvalidDataException("The accepted XDT publication identity has changed.");
            if (Hash(File.ReadAllBytes(Source)) != (string)receipt["SourceHash"] ||
                TextHash(data.ToJson()) != (string)receipt["InitialValueHash"])
                throw new InvalidDataException("Authored XDT changes require review before server route publication.");
            byte[] bytes = File.ReadAllBytes(server);
            string json = Encoding.UTF8.GetString(bytes).TrimStart('\ufeff');
            var input = JObject.Parse(json);
            var maps = (JArray)input["m_pInstanceTable"]["m_pInstanceData"];
            var acceptedMaps = (JArray)data.ToToken()["m_pInstanceTable"]["m_pInstanceData"];
            if (!maps.Select(m => (int)m["m_iInstanceNameID"]).OrderBy(n => n).SequenceEqual(
                acceptedMaps.Select(m => (int)m["m_iInstanceNameID"]).OrderBy(n => n)))
                throw new InvalidDataException("Instance route repair cannot change accepted map identities.");
            data.Import(json);
            File.WriteAllBytes(Source, bytes);
            EditorUtility.SetDirty(data);
            AssetDatabase.SaveAssets();
            receipt["SourceHash"] = Hash(bytes);
            receipt["InitialValueHash"] = TextHash(data.ToJson());
            File.WriteAllText(Receipt, receipt.ToString(Formatting.None));
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            Debug.Log("INSTANCE ROUTE XDT PUBLISHED: " + receipt["SourceHash"]);
        }
    }
}
