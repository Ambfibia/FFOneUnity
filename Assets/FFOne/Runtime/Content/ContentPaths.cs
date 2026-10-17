#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace FFOne.Content
{
    /// <summary>
    /// Import-boundary compatibility for accepted source identities. Runtime uses
    /// addresses, never this physical path map. New tools should use GUIDs directly.
    /// </summary>
    public static class ContentPaths
    {
        public const string Receipt = "Tools/ContentMigration/layout.json.gz";
        [Serializable] public sealed class Entry { public string old, path, guid, sha256; }
        [Serializable] public sealed class Layout { public Entry[] entries; public Dictionary<string,string> directories, previousPaths; }
        static Layout layout;
        static Dictionary<string,string> forward, reverse;
        static Dictionary<string,Entry> originals, resources, byGuid;
        static string root;
        public static IReadOnlyList<Entry> Entries { get { Ensure(); return layout.entries; } }
        static void Ensure()
        {
            if (layout != null) return;
            root = System.IO.Directory.GetCurrentDirectory().Replace('\\','/').TrimEnd('/') + "/";
            string file=System.IO.File.Exists(Receipt) ? Receipt : "Tools/ContentMigration/layout.json";
            if(System.IO.File.Exists(file))
            {
                using var source=System.IO.File.OpenRead(file);
                using Stream decoded=file.EndsWith(".gz",StringComparison.Ordinal) ? new System.IO.Compression.GZipStream(source,System.IO.Compression.CompressionMode.Decompress) : source;
                using var reader=new StreamReader(decoded);
                using var json=new JsonTextReader(reader);
                layout=new JsonSerializer().Deserialize<Layout>(json);
            }
            else layout=new Layout { entries=Array.Empty<Entry>() };
            forward = new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
            reverse = new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
            resources = new Dictionary<string,Entry>(StringComparer.Ordinal);
            originals = new Dictionary<string,Entry>(StringComparer.Ordinal);
            byGuid = new Dictionary<string,Entry>(StringComparer.Ordinal);
            foreach (var e in layout.entries)
            {
                forward[e.old] = e.path;
                originals[e.old]=e;
                if(!string.IsNullOrEmpty(e.guid))byGuid[e.guid]=e;
                reverse[e.path] = e.old;
                forward[e.old+".meta"] = e.path+".meta";
                reverse[e.path+".meta"] = e.old+".meta";
                const string prefix="Assets/Resources/";
                if(e.old.StartsWith(prefix,StringComparison.Ordinal))
                    resources.TryAdd(e.old.Substring(prefix.Length,e.old.LastIndexOf('.')-prefix.Length),e);
            }
            // Preserve physical paths used by earlier authoring receipts as aliases.
            // Stable source identities and GUIDs remain authoritative.
            if(layout.previousPaths!=null) foreach(var alias in layout.previousPaths)
                if(originals.TryGetValue(alias.Value,out var entry))
                {
                    originals[alias.Key]=entry;
                    forward[alias.Key]=entry.path;reverse[alias.Key]=entry.old;
                    forward[alias.Key+".meta"]=entry.path+".meta";reverse[alias.Key+".meta"]=entry.old+".meta";
                }
            if (layout.directories != null) foreach (var p in layout.directories)
            { forward.TryAdd(p.Key,p.Value); reverse.TryAdd(p.Value,p.Key); }
        }
        static string Translate(string path, bool backwards)
        {
            if (string.IsNullOrEmpty(path)) return path;
            Ensure();
            string normalized = path.Replace('\\','/');
            if(normalized.StartsWith("//?/",StringComparison.Ordinal)) normalized=normalized.Substring(4);
            bool absolute = Path.IsPathRooted(normalized);
            if (absolute)
            {
                normalized = Path.GetFullPath(normalized).Replace('\\','/');
                if (!normalized.StartsWith(root,StringComparison.OrdinalIgnoreCase)) return path;
                normalized = normalized.Substring(root.Length);
            }
            normalized = normalized.TrimEnd('/');
            string suffix=normalized.EndsWith(".meta",StringComparison.Ordinal) ? ".meta" : "";
            string asset=suffix.Length==0 ? normalized : normalized.Substring(0,normalized.Length-suffix.Length);
            if(!backwards && originals.TryGetValue(asset,out var accepted))
            {
                string current=CurrentPath(accepted)+suffix;
                return absolute ? root+current : current;
            }
            if(backwards && asset.StartsWith("Assets/FFOne/",StringComparison.Ordinal) &&
                byGuid.TryGetValue(UnityEditor.AssetDatabase.AssetPathToGUID(asset),out accepted))
                return (absolute ? root : "")+accepted.old+suffix;
            var map = backwards ? reverse : forward;
            if (!map.TryGetValue(normalized,out var result) && (result=SharedPoolPath(normalized,backwards))==null)
            {
                const string incoming="Assets/FFOne/Content/Incoming/Resources/";
                if(backwards && normalized.StartsWith(incoming,StringComparison.Ordinal)) result="Assets/Resources/"+normalized.Substring(incoming.Length);
                else if(!backwards && normalized.StartsWith("Assets/Resources/",StringComparison.Ordinal)) result=incoming+normalized.Substring("Assets/Resources/".Length);
                else if((!backwards && (normalized.StartsWith("Assets/Editor/",StringComparison.Ordinal) || normalized.StartsWith("Assets/Generated/",StringComparison.Ordinal))) ||
                        (backwards && normalized.StartsWith("Assets/FFOne/Editor/ImportMetadata/",StringComparison.Ordinal)))
                {
                    string parent=normalized;
                    while(parent.Contains('/'))
                    {
                        parent=parent.Substring(0,parent.LastIndexOf('/'));
                        if(map.TryGetValue(parent,out string owner)) { result=owner+normalized.Substring(parent.Length); break; }
                    }
                    if(result==null)return path;
                }
                else return path;
            }
            return absolute ? root + result : result;
        }
        static string SharedPoolPath(string path,bool backwards)
        {
            // New shared meshes/materials have an established owner. Routing only the
            // child into Incoming would disagree with the already mapped parent folder.
            foreach(string original in new[]{"Assets/Resources/Environment/Shared/Meshes","Assets/Resources/Environment/Shared/Materials",
                "Assets/Generated/WorldTerrainShared","Assets/Generated/WorldModelTextures"})
            {
                if(!forward.TryGetValue(original,out string current))continue;
                string prefix=backwards ? current : original;
                if(path.StartsWith(prefix+"/",StringComparison.OrdinalIgnoreCase))
                    return (backwards ? original : current)+path.Substring(prefix.Length);
            }
            return null;
        }
        public static string Resolve(string path) => Translate(path,false);
        public static string CurrentPath(Entry entry)
        {
            string path=string.IsNullOrEmpty(entry.guid) ? null : UnityEditor.AssetDatabase.GUIDToAssetPath(entry.guid);
            return string.IsNullOrEmpty(path) ? entry.path : path;
        }
        public static string FilePath(string path)
        {
            string resolved=Resolve(path);
            if(string.IsNullOrEmpty(resolved) || resolved.StartsWith(@"\\?\",StringComparison.Ordinal))return resolved;
            string full=Path.GetFullPath(resolved);
            return Path.DirectorySeparatorChar=='\\' && full.Length>=248 ? @"\\?\"+full : resolved;
        }
        public static string[] Resolve(string[] paths) => paths?.Select(Resolve).ToArray();
        public static string Legacy(string path) => Translate(path,true);
        public static string[] Legacy(string[] paths) => paths?.Select(Legacy).ToArray();
        public static IEnumerable<string> Legacy(IEnumerable<string> paths) => paths?.Select(Legacy);
        public static bool DirectoryExists(string path)
        {
            if (System.IO.Directory.Exists(Resolve(path))) return true;
            Ensure(); string prefix = Relative(Legacy(path)).TrimEnd('/') + "/";
            return layout.entries.Any(e => e.old.StartsWith(prefix,StringComparison.OrdinalIgnoreCase) && System.IO.File.Exists(CurrentPath(e)));
        }
        static string Relative(string path)
        {
            Ensure(); path=path.Replace('\\','/');
            return path.StartsWith(root,StringComparison.OrdinalIgnoreCase) ? path.Substring(root.Length) : path;
        }
        public static string[] GetFiles(string path, string pattern = "*", SearchOption option = SearchOption.TopDirectoryOnly)
        {
            Ensure(); string legacy=Legacy(path), prefix=Relative(legacy).TrimEnd('/')+"/";
            if (!prefix.StartsWith("Assets/",StringComparison.Ordinal)) return System.IO.Directory.GetFiles(path,pattern,option);
            bool absolute=Path.IsPathRooted(path);
            var result=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var e in layout.entries)
            {
                if (!e.old.StartsWith(prefix,StringComparison.OrdinalIgnoreCase) || !System.IO.File.Exists(CurrentPath(e))) continue;
                string tail=e.old.Substring(prefix.Length);
                if (option==SearchOption.TopDirectoryOnly && tail.Contains('/')) continue;
                if (!Matches(Path.GetFileName(tail),pattern)) continue;
                result.Add(absolute ? root+e.old : e.old);
            }
            string physical=Resolve(path);
            if (System.IO.Directory.Exists(physical)) foreach (string file in System.IO.Directory.GetFiles(physical,pattern,option))
            {
                string old=Legacy(file);
                if (Relative(old).StartsWith(prefix,StringComparison.OrdinalIgnoreCase)) result.Add(old);
            }
            return result.OrderBy(p=>p,StringComparer.Ordinal).ToArray();
        }
        static bool Matches(string name,string pattern) => System.Text.RegularExpressions.Regex.IsMatch(name,"^"+System.Text.RegularExpressions.Regex.Escape(pattern).Replace("\\*",".*").Replace("\\?",".")+"$",System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        public static string[] GetDirectories(string path)
        {
            Ensure(); string prefix=Relative(Legacy(path)).TrimEnd('/')+"/";
            if (!prefix.StartsWith("Assets/",StringComparison.Ordinal)) return System.IO.Directory.GetDirectories(path);
            var names=new HashSet<string>(layout.entries.Where(e=>e.old.StartsWith(prefix,StringComparison.OrdinalIgnoreCase))
                .Select(e=>e.old.Substring(prefix.Length)).Where(p=>p.Contains('/')).Select(p=>prefix+p.Substring(0,p.IndexOf('/'))),StringComparer.OrdinalIgnoreCase);
            var roots=new List<string>{Resolve(path)};
            if(prefix=="Assets/Resources/")roots.Add("Assets/FFOne/Content/Incoming/Resources");
            foreach(string physical in roots.Distinct(StringComparer.OrdinalIgnoreCase))
                if(System.IO.Directory.Exists(physical))foreach(string child in System.IO.Directory.GetDirectories(physical))
                {
                    string old=Relative(Legacy(child));
                    if(old.StartsWith(prefix,StringComparison.OrdinalIgnoreCase) && !old.Substring(prefix.Length).Contains('/'))names.Add(old);
                }
            return names.Select(p=>Path.IsPathRooted(path)?root+p:p).OrderBy(p=>p,StringComparer.Ordinal).ToArray();
        }
        public static string Resource(string path)
        {
            path=Legacy(path).Replace('\\','/');
            const string prefix="Assets/Resources/";
            if (!path.StartsWith(prefix,StringComparison.Ordinal)) throw new InvalidDataException("Asset has no accepted runtime identity: "+path);
            return path.Substring(prefix.Length,path.LastIndexOf('.')-prefix.Length);
        }
        public static string ResourcePath(string key)
        {
            if(key.StartsWith("ffone/",StringComparison.Ordinal))return UnityEditor.AssetDatabase.GUIDToAssetPath(key.Substring(6));
            Ensure(); if(resources.TryGetValue(key,out var entry))return CurrentPath(entry);
            // New authored views have stable addresses independent of the legacy migration receipt.
            foreach(var route in new[]{("ui/views/", "Views/", ".uxml"), ("ui/styles/", "Styles/", ".uss")})
                if(key.StartsWith(route.Item1,StringComparison.Ordinal))
                {
                    string native="Assets/FFOne/Content/UI/"+route.Item2+key.Substring(route.Item1.Length)+route.Item3;
                    if(System.IO.File.Exists(native)) return native;
                }
            if(key.StartsWith("ui/en/",StringComparison.Ordinal))
            {
                string native="Assets/FFOne/Content/UI/Artwork/en/"+key.Substring("ui/en/".Length)+".png";
                if(System.IO.File.Exists(native))return native;
            }
            // New native tables retain their document identity without requiring
            // a fictitious entry in the historical Resources migration receipt.
            if(key.StartsWith("NativeData/",StringComparison.Ordinal))
            {
                string native="Assets/FFOne/Data/Tables/"+key.Substring("NativeData/".Length)+".asset";
                if(System.IO.File.Exists(native))return native;
            }
            string path;
            foreach(string extension in new[]{".prefab",".asset",".glb",".json",".png",".ogg",".ttf",".otf",".uxml",".uss",".bytes",".txt"})
            {
                path=Resolve("Assets/Resources/"+key+extension);
                if(System.IO.File.Exists(path))return path;
            }
            return null;
        }
        public static string CreateFolder(string parent,string name)
        {
            string path=Resolve(parent.TrimEnd('/')+"/"+name);
            if(UnityEditor.AssetDatabase.IsValidFolder(path))return UnityEditor.AssetDatabase.AssetPathToGUID(path);
            string physicalParent=Path.GetDirectoryName(path).Replace('\\','/');
            if(!UnityEditor.AssetDatabase.IsValidFolder(physicalParent))
                CreateFolder(Path.GetDirectoryName(physicalParent).Replace('\\','/'),Path.GetFileName(physicalParent));
            return UnityEditor.AssetDatabase.CreateFolder(physicalParent,Path.GetFileName(path));
        }
        /// <summary>Permit the recorded layout-only change without approving changed source data.</summary>
        public static bool AcceptedSourceUnchanged(string source)
        {
            Ensure(); source=Legacy(source);
            if(!originals.ContainsKey(source))return false;
            var dependencies=UnityEditor.AssetDatabase.GetDependencies(Resolve(source),true).Select(Legacy)
                .Where(p=>p==source || p.StartsWith("Assets/Generated/WorldModelTextures/",StringComparison.Ordinal) ||
                    new[]{".png",".jpg",".jpeg",".dds",".tga"}.Contains(Path.GetExtension(p).ToLowerInvariant())).Append(source).Distinct();
            using var sha=System.Security.Cryptography.SHA256.Create();
            foreach(var path in dependencies)
            {
                if(!originals.TryGetValue(path,out var entry) || !System.IO.File.Exists(CurrentPath(entry)))return false;
                using var stream=System.IO.File.OpenRead(CurrentPath(entry));
                string actual=BitConverter.ToString(sha.ComputeHash(stream)).Replace("-","").ToLowerInvariant();
                if(actual!=entry.sha256)return false;
            }
            return true;
        }
    }
}
#endif
