// Unity MCP execute_code METHOD BODY. Read-only asset inspection; evidence is not a benchmark.
// Run only while the coordinated Editor is idle, outside tests/builds/play mode.
if (UnityEditor.EditorApplication.isCompiling || UnityEditor.EditorApplication.isUpdating || UnityEditor.EditorApplication.isPlaying)
    throw new System.InvalidOperationException("Terrain validation requires an idle Edit Mode Editor.");
var errors = new System.Collections.Generic.List<string>();
var tiles = new System.Collections.Generic.List<object>();
var resources = new System.Collections.Generic.Dictionary<string, System.Collections.Generic.HashSet<string>>();
var checks = new System.Collections.Generic.Dictionary<string, long>();
string current = "catalog";
int failures = 0;
System.Action<bool, string> Check = (pass, label) =>
{
    if (!checks.ContainsKey(label)) checks[label] = 0;
    checks[label]++;
    if (!pass) { failures++; if (errors.Count < 500) errors.Add(current + ": " + label); }
};
System.Action<string, UnityEngine.Object> Resource = (role, asset) =>
{
    if (!asset) { Check(false, "resource present: " + role); return; }
    if (!resources.ContainsKey(role)) resources[role] = new System.Collections.Generic.HashSet<string>();
    var path = UnityEditor.AssetDatabase.GetAssetPath(asset);
    Check(!string.IsNullOrEmpty(path), "resource has immutable asset path: " + role);
    if (!string.IsNullOrEmpty(path)) resources[role].Add(path);
};
System.Func<string, Newtonsoft.Json.Linq.JObject> Read = path => Newtonsoft.Json.Linq.JObject.Parse(System.IO.File.ReadAllText(FFOne.Content.ContentPaths.Resolve(path)));
System.Func<string, string, string> Relative = (folder, path) => path.StartsWith("map/", System.StringComparison.Ordinal) || path.StartsWith("world/", System.StringComparison.Ordinal)
    ? "Assets/Resources/" + path : folder + "/" + path;
System.Func<Newtonsoft.Json.Linq.JToken, Newtonsoft.Json.Linq.JToken[]> Mips = value =>
{
    var array = value["mips"] as Newtonsoft.Json.Linq.JArray;
    if (array == null || array.Count == 0) return new[] { value };
    var values = new Newtonsoft.Json.Linq.JToken[array.Count];
    for (int i = 0; i < array.Count; i++) values[i] = array[i];
    return values;
};
System.Func<Newtonsoft.Json.Linq.JToken, UnityEngine.Color> ColorOf = array => new UnityEngine.Color((float)array[0], (float)array[1], (float)array[2], (float)array[3]);
System.Func<UnityEngine.Vector4, UnityEngine.Vector4, bool> Near = (a, b) => (a - b).sqrMagnitude <= .0001f * .0001f;
System.Func<Newtonsoft.Json.Linq.JToken, UnityEngine.Matrix4x4> Matrix = value =>
{
    var p = value["translation"]; var q = value["rotation"]; var s = value["scale"];
    return UnityEngine.Matrix4x4.TRS(new UnityEngine.Vector3(-(float)p[0], (float)p[1], (float)p[2]),
        new UnityEngine.Quaternion((float)q[0], -(float)q[1], -(float)q[2], (float)q[3]),
        new UnityEngine.Vector3((float)s[0], (float)s[1], (float)s[2]));
};
System.Func<string, string> GuidPath = line =>
{
    var match = System.Text.RegularExpressions.Regex.Match(line, "guid: ([0-9a-f]{32})");
    return match.Success ? UnityEditor.AssetDatabase.GUIDToAssetPath(match.Groups[1].Value) : "";
};
System.Func<byte[], string> Digest = bytes =>
{
    using (var sha = System.Security.Cryptography.SHA256.Create())
        return System.BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
};
var textureIdentityPaths = new System.Collections.Generic.Dictionary<string, string>();
System.Func<Newtonsoft.Json.Linq.JToken, string, string> GrassTexturePath = (doc, folder) =>
{
    var mipData = new System.Collections.Generic.List<object>();
    foreach (var mip in Mips(doc)) mipData.Add(new { level = (int?)mip["level"] ?? 0, width = (int)mip["width"], height = (int)mip["height"],
        bytes = Digest(System.IO.File.ReadAllBytes(FFOne.Content.ContentPaths.Resolve(Relative(folder, (string)mip["path"])))) });
    string identity = Newtonsoft.Json.JsonConvert.SerializeObject(new { Revision = "native-grid-v1", role = "GrassTexture", encoding = "hardware-srgb",
        images = new[] { new { width = (int)doc["width"], height = (int)doc["height"], colorSpace = (string)doc["colorSpace"], sampler = doc["sampler"], mips = mipData } } });
    return "Assets/Generated/WorldTerrainShared/GrassTexture_" + Digest(System.Text.Encoding.UTF8.GetBytes(identity)) + ".asset";
};
System.Action<UnityEngine.Texture2D, Newtonsoft.Json.Linq.JToken, string> TextureSampler = (texture, doc, role) =>
{
    Check(texture != null, role + " texture exists");
    if (!texture) return;
    Resource(role, texture);
    Check(texture.width == (int)doc["width"] && texture.height == (int)doc["height"], role + " source dimensions");
    Check(texture.mipmapCount == Mips(doc).Length, role + " exact source mip count");
    int filter = (int?)doc["sampler"]?["filterMode"]?["serializedValue"] ?? 1;
    int wrap = (int?)doc["sampler"]?["wrapMode"]?["serializedValue"] ?? 1;
    Check((int)texture.filterMode == filter, role + " source filter");
    Check(texture.wrapMode == (wrap == 0 ? UnityEngine.TextureWrapMode.Repeat : UnityEngine.TextureWrapMode.Clamp), role + " source wrap");
    Check(texture.anisoLevel == ((int?)doc["sampler"]?["anisoLevel"] ?? 1), role + " source anisotropy");
    Check(UnityEngine.Mathf.Abs(texture.mipMapBias - ((float?)doc["sampler"]?["mipBias"] ?? 0)) < .00001f, role + " source mip bias");
    bool expectedSrgb = role == "grassTextures" && (string)doc["sampler"]?["usageColorSpace"] != "linear";
    Check(texture.graphicsFormat.ToString().Contains("SRGB") == expectedSrgb, role + " native interpolation color space");
};
var catalog = UnityEditor.AssetDatabase.LoadAssetAtPath<FFOne.World.WorldCatalog>(FFOne.Content.ContentPaths.Resolve("Assets/Resources/World/Catalog.asset"));
if (!catalog) throw new System.IO.InvalidDataException("Imported World Catalog is missing.");
Check(catalog.Tiles.Length == 170, "170 world tiles");
Check(catalog.Atmospheres.Length == 170, "170 atmosphere records");
Check(catalog.SourceFiles == 37779 && catalog.SourceBytes == 1952567907L, "source receipt files and bytes");
var ids = new System.Collections.Generic.HashSet<string>();
long verticesTotal = 0, indicesTotal = 0, grassTotal = 0, visualTotal = 0, colliderTotal = 0, animationTotal = 0, emitterTotal = 0;
long grassVertices = 0, grassIndices = 0, grassMeshes = 0, sourceGrassTotal = 0, shiftTotal = 0;
int prefabs = 0, contentAssets = 0, environments = 0, maxLayers = 0;
int sharedTerrainColliders = 0;
float maxGeometryError = 0, maxUvError = 0, maxNormalError = 0, maxColorError = 0;
foreach (var tile in catalog.Tiles)
{
    current = tile.Id;
    int initialErrors = failures;
    long actualGrass = 0, expectedGrass = 0;
    try
    {
        Check(ids.Add(tile.Id), "unique tile ID");
        string folder = "Assets/Resources/map/tiles/" + tile.Id;
        string prefabPath = "Assets/Resources/World/Tiles/" + tile.Id + "/Terrain.prefab";
        string contentPath = "Assets/Resources/World/Tiles/" + tile.Id + "/Content.asset";
        var scene = Read(folder + "/scene.json");
        var instance = scene["nativeTerrain"];
        Check(instance != null && instance.Type != Newtonsoft.Json.Linq.JTokenType.Null, "native terrain owner exists");
        var definition = Read("Assets/Resources/" + (string)instance["path"]);
        string terrainFolder = System.IO.Path.GetDirectoryName("Assets/Resources/" + (string)instance["path"]).Replace('\\', '/');
        var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>(FFOne.Content.ContentPaths.Resolve(prefabPath));
        Check(prefab != null, "imported terrain prefab exists");
        if (!prefab) continue;
        prefabs++; Resource("terrainPrefabs", prefab);
        var filter = prefab.GetComponent<UnityEngine.MeshFilter>();
        var renderer = prefab.GetComponent<UnityEngine.MeshRenderer>();
        var collider = prefab.GetComponent<UnityEngine.MeshCollider>();
        var parameters = prefab.GetComponent<FFOne.Runtime.World.WorldTerrainParameters>();
        Check(filter && renderer && collider && parameters, "terrain component contract");
        var mesh = filter.sharedMesh;
        Check(mesh && mesh.isReadable && mesh.vertexCount == 16641 && mesh.GetIndexCount(0) == 98304, "129 grid full geometry");
        Check(mesh.indexFormat == UnityEngine.Rendering.IndexFormat.UInt32 && mesh.subMeshCount == 1, "full grid topology format");
        Check(collider.sharedMesh == mesh && !collider.convex && !collider.isTrigger, "shared full terrain collider mesh");
        if (collider.sharedMesh == mesh && !collider.convex && !collider.isTrigger) sharedTerrainColliders++;
        Resource("terrainMeshes", mesh);
        var positions = mesh.vertices; var uv = mesh.uv; var normals = mesh.normals; var colors = mesh.colors; var indices = mesh.triangles;
        verticesTotal += mesh.vertexCount; indicesTotal += indices.LongLength;
        Check(positions.Length == 16641 && uv.Length == 16641 && normals.Length == 16641 && colors.Length == 16641, "all terrain vertex attributes retained");
        var sourceHeights = FFOne.Editor.TutorialHeightmap.Read(System.IO.File.ReadAllBytes(FFOne.Content.ContentPaths.Resolve(Relative(terrainFolder, (string)definition["heightmap"]["path"]))), 129);
        float sx = (float)definition["scale"]["sampleSpacingX"], sz = (float)definition["scale"]["sampleSpacingZ"], sy = (float)definition["scale"]["heightScale"];
        Check((int)definition["dimensions"]["width"] == 129 && (int)definition["dimensions"]["height"] == 129 && sx == 4 && sz == 4 && sy == 600, "native dimensions and scale");
        var expectedPositions = new UnityEngine.Vector3[16641]; var expectedUv = new UnityEngine.Vector2[16641];
        for (int row = 0; row < 129; row++) for (int column = 0; column < 129; column++)
        {
            int i = row * 129 + column;
            expectedPositions[i] = new UnityEngine.Vector3(column * sx, sourceHeights[row, column] * sy, row * sz);
            expectedUv[i] = new UnityEngine.Vector2(column / 128f, 1f - row / 128f);
        }
        foreach (var shift in definition["heightmap"]["vertexShifts"] ?? new Newtonsoft.Json.Linq.JArray())
        {
            int flags = (int)shift["flags"], i = (int)shift["row"] * 129 + (int)shift["column"];
            int dx = ((flags & 2) != 0 ? 1 : 0) - ((flags & 1) != 0 ? 1 : 0);
            int dz = ((flags & 8) != 0 ? 1 : 0) - ((flags & 4) != 0 ? 1 : 0);
            expectedPositions[i] += new UnityEngine.Vector3(dx * sx, 0, dz * sz);
            expectedUv[i] += new UnityEngine.Vector2(dx / 256f, -dz / 256f); shiftTotal++;
        }
        float geometryError = 0, uvError = 0, normalError = 0, colorError = 0;
        for (int i = 0; i < positions.Length; i++)
        {
            geometryError = UnityEngine.Mathf.Max(geometryError, (positions[i] - expectedPositions[i]).magnitude);
            uvError = UnityEngine.Mathf.Max(uvError, (uv[i] - expectedUv[i]).magnitude);
        }
        bool winding = true; int at = 0;
        var expectedNormals = new UnityEngine.Vector3[16641];
        for (int row = 0; row < 128; row++) for (int column = 0; column < 128; column++)
        {
            int a = row * 129 + column, b = a + 1, c = a + 129, d = c + 1;
            int[] expected = { a, c, b, b, c, d };
            for (int n = 0; n < 6; n++) if (indices[at + n] != expected[n]) winding = false;
            for (int n = 0; n < 6; n += 3)
            {
                int ia = expected[n], ib = expected[n + 1], ic = expected[n + 2];
                var face = UnityEngine.Vector3.Cross(expectedPositions[ib] - expectedPositions[ia], expectedPositions[ic] - expectedPositions[ia]);
                if (face.sqrMagnitude > 1.1920929e-7f) { expectedNormals[ia] += face; expectedNormals[ib] += face; expectedNormals[ic] += face; }
            }
            at += 6;
        }
        for (int i = 0; i < positions.Length; i++)
        {
            var normal = expectedNormals[i].normalized;
            normalError = UnityEngine.Mathf.Max(normalError, (normals[i] - normal).magnitude);
            float channel = UnityEngine.Mathf.Clamp01(.2f + UnityEngine.Mathf.Max(UnityEngine.Vector3.Dot(normal, new UnityEngine.Vector3(-.89370085f, .30901706f, .32528046f)), 0));
            colorError = UnityEngine.Mathf.Max(colorError, ((UnityEngine.Vector4)colors[i] - new UnityEngine.Vector4(channel, channel, channel, 1)).magnitude);
        }
        Check(geometryError <= .00001f && uvError <= .000001f && winding, "every position shift UV and reflected triangle exact");
        Check(normalError <= .00001f && colorError <= .00001f, "every native normal and legacy vertex illumination exact");
        maxGeometryError = UnityEngine.Mathf.Max(maxGeometryError, geometryError); maxUvError = UnityEngine.Mathf.Max(maxUvError, uvError);
        maxNormalError = UnityEngine.Mathf.Max(maxNormalError, normalError); maxColorError = UnityEngine.Mathf.Max(maxColorError, colorError);
        var rootMatrix = Matrix(instance["transform"]);
        foreach (var node in instance["rootChain"]?["nodes"] ?? new Newtonsoft.Json.Linq.JArray()) rootMatrix = Matrix(node["nativeLocalTransform"]) * rootMatrix;
        Check((prefab.transform.localPosition - (UnityEngine.Vector3)rootMatrix.GetColumn(3)).magnitude < .001f &&
            (prefab.transform.localScale - rootMatrix.lossyScale).magnitude < .0001f &&
            UnityEngine.Mathf.Abs(UnityEngine.Quaternion.Dot(prefab.transform.localRotation, rootMatrix.rotation)) > .99999f, "rootChain mirrored pose exact");
        var material = renderer.sharedMaterial; Resource("terrainMaterials", material);
        Check(material.shader.name == "FFOne/WorldTerrain", "native terrain compositor shader");
        var layers = (Newtonsoft.Json.Linq.JArray)definition["splat"]["layers"];
        var weights = (Newtonsoft.Json.Linq.JArray)definition["splat"]["weightMaps"];
        maxLayers = System.Math.Max(maxLayers, layers.Count);
        var layerArray = material.GetTexture("_Layers") as UnityEngine.Texture2DArray;
        var weightArray = material.GetTexture("_Weights") as UnityEngine.Texture2DArray;
        Resource("layerTextureArrays", layerArray); Resource("weightTextureArrays", weightArray);
        Check(layerArray && weightArray && layerArray.depth == layers.Count && weightArray.depth == weights.Count, "terrain array slices retained");
        Check(layerArray.filterMode == UnityEngine.FilterMode.Point && weightArray.filterMode == UnityEngine.FilterMode.Point &&
            layerArray.wrapMode == UnityEngine.TextureWrapMode.Repeat && weightArray.wrapMode == UnityEngine.TextureWrapMode.Clamp, "manual native texel filtering arrays");
        Check(!layerArray.graphicsFormat.ToString().Contains("SRGB") && !weightArray.graphicsFormat.ToString().Contains("SRGB"), "terrain arrays native gamma interpolation");
        var fields = instance["terrainRenderContract"]?["serializedFields"];
        var metadata = new UnityEngine.Vector4(layers.Count, (float?)fields?["m_RenderMode"] ?? 1, (float?)fields?["m_SplatMapDistance"] ?? 150, 0);
        Check(Near(material.GetVector("_Metadata"), metadata) && material.GetFloat("_AnisotropicTaps") == 4, "native shader mode distance and source filtering budget");
        var serialized = new UnityEditor.SerializedObject(parameters);
        var scaleParams = serialized.FindProperty("scales"); var sizeParams = serialized.FindProperty("sizes");
        var tintParams = serialized.FindProperty("tints"); var weightParams = serialized.FindProperty("weights");
        Check(scaleParams.arraySize == 17 && sizeParams.arraySize == 17 && tintParams.arraySize == 17 && weightParams.arraySize == 5, "serialized 17-layer 5-weight parameter capacity");
        bool layerParameters = true, weightParameters = true;
        int layerWidth = 0, layerHeight = 0, layerMips = 0, weightWidth = 0, weightHeight = 0, weightMips = 0;
        for (int i = 0; i < 17; i++)
        {
            var scale = UnityEngine.Vector4.zero; var size = UnityEngine.Vector4.zero; var tint = UnityEngine.Vector4.one;
            if (i < layers.Count)
            {
                var layer = layers[i]; var image = layer["albedo"];
                scale = new UnityEngine.Vector4((float)definition["scale"]["extentX"] / (float)layer["tileSize"]["x"], (float)definition["scale"]["extentZ"] / (float)layer["tileSize"]["y"], (float)layer["mode"], 0);
                size = new UnityEngine.Vector4((float)image["width"], (float)image["height"], Mips(image).Length - 1, (float?)image["sampler"]?["mipBias"] ?? 0);
                var tc = layer["editorTint"] as Newtonsoft.Json.Linq.JArray;
                if (tc != null) tint = new UnityEngine.Vector4((float)tc[0], (float)tc[1], (float)tc[2], 1);
                layerParameters &= (int)layer["weight"]["mapIndex"] == i / 4 && (int)layer["weight"]["channelIndex"] == i % 4;
                layerWidth = System.Math.Max(layerWidth, (int)image["width"]); layerHeight = System.Math.Max(layerHeight, (int)image["height"]); layerMips = System.Math.Max(layerMips, Mips(image).Length);
            }
            layerParameters &= Near(scaleParams.GetArrayElementAtIndex(i).vector4Value, scale) && Near(sizeParams.GetArrayElementAtIndex(i).vector4Value, size) && Near(tintParams.GetArrayElementAtIndex(i).vector4Value, tint);
        }
        for (int i = 0; i < 5; i++)
        {
            var value = UnityEngine.Vector4.zero;
            if (i < weights.Count)
            {
                var image = weights[i]; value = new UnityEngine.Vector4((float?)image["sampler"]?["filterMode"]?["serializedValue"] ?? 1, Mips(image).Length - 1, (float?)image["sampler"]?["mipBias"] ?? 0, (float)image["width"]);
                weightWidth = System.Math.Max(weightWidth, (int)image["width"]); weightHeight = System.Math.Max(weightHeight, (int)image["height"]); weightMips = System.Math.Max(weightMips, Mips(image).Length);
            }
            weightParameters &= Near(weightParams.GetArrayElementAtIndex(i).vector4Value, value);
        }
        Check(layerParameters && weightParameters, "every source scale mode tint filter mip and quartet route");
        Check(layerArray.width == layerWidth && layerArray.height == layerHeight && layerArray.mipmapCount == layerMips &&
            weightArray.width == weightWidth && weightArray.height == weightHeight && weightArray.mipmapCount == weightMips, "source terrain array dimensions and mip closure");
        if ((string)definition["lightmap"]?["status"] == "exported") TextureSampler(material.GetTexture("_Lightmap") as UnityEngine.Texture2D, definition["lightmap"], "lightmaps");
        var environment = prefab.GetComponent<FFOne.Runtime.World.WorldTerrainEnvironment>();
        var ambience = Read(Relative(terrainFolder, (string)definition["environment"]["path"]))["ambience"];
        var grid = new UnityEngine.Vector2Int((int)ambience["gridCoordinates"][0], (int)ambience["gridCoordinates"][1]);
        Check(environment && environment.Grid == grid && grid.x == tile.X && grid.y == tile.Z &&
            UnityEngine.Mathf.Abs(environment.FogDepth - (float)ambience["fogDepth"]) < .00001f &&
            Near(environment.FogColor, ColorOf(ambience["fogColor"])) && Near(environment.SkyColor, ColorOf(ambience["skyColor"])) && Near(environment.LightColor, ColorOf(ambience["lightColor"])), "source environment admitted exact");
        bool atmosphere = false;
        foreach (var entry in catalog.Atmospheres) if (entry.Grid == grid)
            atmosphere = UnityEngine.Mathf.Abs(entry.FogDepth - environment.FogDepth) < .00001f && Near(entry.Fog, environment.FogColor) && Near(entry.Sky, environment.SkyColor) && Near(entry.Light, environment.LightColor);
        Check(atmosphere, "persistent catalog atmosphere matches actual terrain"); environments++;
        var expectedGrassTextures = new System.Collections.Generic.Dictionary<string, long>();
        var grassTextureDocs = new System.Collections.Generic.Dictionary<string, Newtonsoft.Json.Linq.JToken>();
        var actualGrassByTexture = new System.Collections.Generic.Dictionary<string, long>();
        foreach (var prototype in definition["detailAndTrees"]?["prototypes"] ?? new Newtonsoft.Json.Linq.JArray())
        {
            long count = (long)prototype["density"]["densityTotal"]; expectedGrass += count;
            if (count == 0) continue;
            string docPath = Relative(terrainFolder, (string)prototype["prototypeTexture"]["documentPath"]);
            var doc = Read(docPath);
            if (!textureIdentityPaths.ContainsKey(docPath)) textureIdentityPaths[docPath] = GrassTexturePath(doc, terrainFolder);
            string key = textureIdentityPaths[docPath];
            if (!expectedGrassTextures.ContainsKey(key)) expectedGrassTextures[key] = 0; expectedGrassTextures[key] += count;
            grassTextureDocs[key] = doc;
            Check(((int?)prototype["serialized"]["usePrototypeMesh"] ?? 0) == 0, "cross-quad grass prototype contract");
        }
        bool grassGeometry = true; int tileGrassMeshes = 0;
        foreach (var grassRenderer in prefab.GetComponentsInChildren<UnityEngine.MeshRenderer>(true))
        {
            if (grassRenderer == renderer) continue;
            Check(grassRenderer.sharedMaterial && grassRenderer.sharedMaterial.shader.name == "FFOne/WorldTerrainGrass", "grass native shader");
            var grass = grassRenderer.GetComponent<UnityEngine.MeshFilter>().sharedMesh;
            Resource("grassMeshes", grass); Resource("grassMaterials", grassRenderer.sharedMaterial);
            var grassTexture = grassRenderer.sharedMaterial.GetTexture("_MainTex") as UnityEngine.Texture2D;
            Resource("grassTextures", grassTexture);
            var visibility = grassRenderer.GetComponent<FFOne.Runtime.World.WorldTerrainGrassVisibility>();
            Check(visibility && UnityEngine.Mathf.Abs(visibility.Distance - ((float?)fields?["m_DetailObjectDistance"] ?? 80)) < .00001f, "native grass presentation distance");
            var gi = grass.triangles; var gu = grass.uv;
            long blades = grass.vertexCount / 8; actualGrass += blades; grassVertices += grass.vertexCount; grassIndices += gi.LongLength;
            string grassTexturePath = UnityEditor.AssetDatabase.GetAssetPath(grassTexture);
            if (!actualGrassByTexture.ContainsKey(grassTexturePath)) actualGrassByTexture[grassTexturePath] = 0;
            actualGrassByTexture[grassTexturePath] += blades;
            Check(expectedGrassTextures.ContainsKey(grassTexturePath), "grass content-addressed source texture identity");
            if (grassTextureDocs.ContainsKey(grassTexturePath)) TextureSampler(grassTexture, grassTextureDocs[grassTexturePath], "grassTextures");
            tileGrassMeshes++; grassMeshes++;
            grassGeometry &= grass.vertexCount % 8 == 0 && blades <= 8192 && gi.LongLength == blades * 12 &&
                grass.normals.Length == grass.vertexCount && grass.colors.Length == grass.vertexCount && gu.Length == grass.vertexCount &&
                grassRenderer.shadowCastingMode == UnityEngine.Rendering.ShadowCastingMode.Off && !grassRenderer.receiveShadows;
            int[] quad = { 0, 2, 1, 0, 3, 2 };
            for (int q = 0; q < grass.vertexCount / 4; q++)
            {
                for (int n = 0; n < 6; n++) grassGeometry &= gi[q * 6 + n] == q * 4 + quad[n];
                grassGeometry &= gu[q * 4] == new UnityEngine.Vector2(0, 1) && gu[q * 4 + 1] == new UnityEngine.Vector2(1, 1) &&
                    gu[q * 4 + 2] == new UnityEngine.Vector2(1, 0) && gu[q * 4 + 3] == new UnityEngine.Vector2(0, 0);
            }
        }
        foreach (var expected in expectedGrassTextures) Check(actualGrassByTexture.ContainsKey(expected.Key) && actualGrassByTexture[expected.Key] == expected.Value,
            "grass density per exact shared prototype texture");
        Check(grassGeometry && actualGrass == expectedGrass, "all crossed grass topology UV and density instances retained");
        Check(((int?)definition["detailAndTrees"]?["trees"]?["instanceCount"] ?? 0) == 0, "native terrain tree array empty");
        grassTotal += actualGrass; sourceGrassTotal += expectedGrass;
        Check(System.IO.File.Exists(contentPath), "actual serialized tile Content exists");
        Check("Assets/Resources/" + tile.Resource + ".asset" == contentPath, "catalog resource selects actual admitted Content asset");
        string contentYaml = System.IO.File.ReadAllText(FFOne.Content.ContentPaths.Resolve(contentPath));
        contentAssets++;
        int visualCount = 0, colliderCount = 0; string section = "";
        string behaviourPath = "", environmentPath = "";
        foreach (var line in contentYaml.Split('\n'))
        {
            if (line.StartsWith("  Visuals:")) section = "visuals";
            else if (line.StartsWith("  Colliders:")) section = "colliders";
            else if (line.StartsWith("  - Name:")) { if (section == "visuals") visualCount++; if (section == "colliders") colliderCount++; }
            if (line.StartsWith("  Terrain:")) Check(GuidPath(line) == prefabPath, "Content references admitted terrain prefab GUID");
            if (line.StartsWith("  Behaviour:")) behaviourPath = GuidPath(line);
            if (line.StartsWith("  Environment:")) environmentPath = GuidPath(line);
            if (line.StartsWith("    Model:") || line.StartsWith("    Mesh:"))
            {
                var path = GuidPath(line); Check(!string.IsNullOrEmpty(path) && System.IO.File.Exists(path), "every visual and authored collider reference resolves");
                var role = line.StartsWith("    Model:") ? "sourceVisualModelAssets" : "sourceAuthoredColliderAssets";
                if (!resources.ContainsKey(role)) resources[role] = new System.Collections.Generic.HashSet<string>(); resources[role].Add(path);
            }
        }
        Check(visualCount == tile.Visuals && colliderCount == tile.Colliders, "actual Content placement counts match admitted catalog");
        Check(System.IO.File.Exists(behaviourPath) && System.IO.File.Exists(environmentPath), "Content behaviour and environment GUIDs resolve");
        var behaviour = Read(behaviourPath);
        int animations = ((Newtonsoft.Json.Linq.JArray)behaviour["animations"]).Count, emitters = ((Newtonsoft.Json.Linq.JArray)behaviour["effectEmitters"]).Count;
        Check(animations == tile.Animations && emitters == tile.Effects, "actual referenced animation and emitter record counts");
        visualTotal += visualCount; colliderTotal += colliderCount; animationTotal += animations; emitterTotal += emitters;
        tiles.Add(new { id = tile.Id, grid = new[] { tile.X, tile.Z }, prefab = prefabPath, mesh = UnityEditor.AssetDatabase.GetAssetPath(mesh), vertices = mesh.vertexCount,
            indices = indices.Length, sharedTerrainCollider = collider.sharedMesh == mesh, layers = layers.Count, weightMaps = weights.Count,
            grassInstances = actualGrass, sourceGrassInstances = expectedGrass, grassMeshes = tileGrassMeshes,
            visualPlacements = visualCount, authoredColliders = colliderCount, animations, effectEmitters = emitters,
            maxPositionError = geometryError, maxUvError = uvError, maxNormalError = normalError, maxVertexColorError = colorError,
            passed = failures == initialErrors });
    }
    catch (System.Exception exception) { failures++; if (errors.Count < 500) errors.Add(tile.Id + ": inspection exception: " + exception.ToString()); }
}
current = "world";
Check(prefabs == 170 && contentAssets == 170 && environments == 170, "all imported tiles inspected");
Check(ids.Contains("map_01_01"), "map_01_01 remains included in main world");
Check(verticesTotal == 2828970 && indicesTotal == 16711680, "full world terrain geometry totals");
Check(grassTotal == 709789 && grassTotal == sourceGrassTotal, "709789 actual grass instances");
Check(visualTotal == 144478 && colliderTotal == 59465 && animationTotal == 17856 && emitterTotal == 8836, "world actual presentation collider animation emitter coverage totals");
Check(maxLayers == 17, "17 layer maximum preserved");
var resourceSummary = new System.Collections.Generic.Dictionary<string, object>();
foreach (var entry in resources)
{
    var paths = new string[entry.Value.Count]; entry.Value.CopyTo(paths); System.Array.Sort(paths);
    resourceSummary[entry.Key] = new { uniqueAssets = entry.Value.Count, paths };
}
var report = new {
    schema = "ffone.terrain-import-validation.v1", capturedUtc = System.DateTime.UtcNow.ToString("O"), validationOnly = true, perfAccepted = false,
    passed = failures == 0, errorCount = failures, unityVersion = UnityEngine.Application.unityVersion, sourceCatalogSha256 = catalog.SourceCatalogSha256,
    sourceFiles = catalog.SourceFiles, sourceBytes = catalog.SourceBytes,
    inspectionPolicy = "Actual Catalog and Terrain.prefab objects loaded through Unity AssetDatabase; actual serialized Content asset GUIDs resolved; all geometry compared against imported Gray16/descriptor, no performance samples.",
    totals = new { tiles = prefabs, contentAssets, environments, terrainVertices = verticesTotal, terrainIndices = indicesTotal, terrainTriangles = indicesTotal / 3,
        terrainColliderMeshesShared = sharedTerrainColliders, grassInstances = grassTotal, sourceGrassInstances = sourceGrassTotal, grassVertices, grassIndices, grassMeshes,
        visualPlacements = visualTotal, authoredColliders = colliderTotal, animations = animationTotal, effectEmitters = emitterTotal, vertexShiftRecords = shiftTotal, maxTerrainLayers = maxLayers },
    maxErrors = new { position = maxGeometryError, uv = maxUvError, normal = maxNormalError, vertexColor = maxColorError },
    checks, resources = resourceSummary, errors, tileResults = tiles,
    limitations = new[] { "GPU texture texel equivalence and shader output are evaluated by separate paired-frame captures; this inspection checks serialized texture descriptors, mip closure and sampler states.",
        "Authored collider and model GUID admission is inspected without eagerly loading all source GLB graphs; runtime readiness is checked by the benchmark harness.",
        "Grass verification checks every quad topology/UV and per-tile density total; full per-instance position/color equivalence remains covered by the source port and paired-frame inspection." }
};
string output = "Builds/WorldBenchmark/terrain-import-validation.json";
System.IO.Directory.CreateDirectory("Builds/WorldBenchmark");
System.IO.File.WriteAllText(output, Newtonsoft.Json.JsonConvert.SerializeObject(report, Newtonsoft.Json.Formatting.Indented));
return new { passed = failures == 0, output, tiles = prefabs, terrainVertices = verticesTotal, terrainIndices = indicesTotal, grassInstances = grassTotal,
    visualPlacements = visualTotal, authoredColliders = colliderTotal, animations = animationTotal, effectEmitters = emitterTotal, errorCount = failures,
    errorPreview = errors.GetRange(0, System.Math.Min(20, errors.Count)), unloadRequiredBeforeBenchmark = true };
