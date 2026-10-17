#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections;
using System.Linq;
using FFOne.World;
using UnityEngine;
using UnityEngine.UIElements;

namespace FFOne.Content
{
    /// <summary>Opt-in player validation of real bundles, without connecting to a server.</summary>
    public sealed class ContentLayoutSmoke : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Run()
        {
            if(!Environment.GetCommandLineArgs().Contains("--content-layout-smoke"))return;
            new GameObject("Content layout smoke").AddComponent<ContentLayoutSmoke>();
        }
        IEnumerator Start()
        {
            yield return null;
            Validate();
        }
        static void Validate()
        {
            Debug.Log("CONTENT LAYOUT PLAYER SMOKE STARTED");
            try
            {
                Require(ContentAssets.Load<NativeContentCatalog>("NativeContent"),"native catalog");
                Require(ContentAssets.Load<VisualTreeAsset>("ui/views/Login"),"login view");
                Require(ContentAssets.Load<AudioClip>("audio/music/00_main_theme"),"music");
                var model=NativeModelAssets.Load("characters/mobs/mob_almonster/mob_almonster");
                Require(model,"published mob");
                if(model.GetComponentsInChildren<Renderer>(true).Length==0)throw new InvalidOperationException("Published mob has no renderers");
                var models=ContentAssets.Load<NativeModelCatalog>("NativeEditable/Index");
                Require(models,"model catalog");
                var nanos=models.Bindings.Where(b=>b.OriginalResource.StartsWith("characters/nanos/",StringComparison.Ordinal) || b.OriginalResource.StartsWith("characters/nano/",StringComparison.Ordinal)).ToArray();
                if(nanos.Length!=69)throw new InvalidOperationException("Expected 69 accepted Nano model identities");
                foreach(var nano in nanos)
                {
                    var prefab=NativeModelAssets.Load(nano.OriginalResource);Require(prefab,nano.OriginalResource);
                    var renderers=prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                    if(renderers.Length==0 || renderers.Any(r=>!r.sharedMesh || r.sharedMaterials.Any(m=>!m)))
                        throw new InvalidOperationException("Missing Nano render dependency: "+nano.OriginalResource);
                }
                Debug.Log("CONTENT NANO MODELS PASSED: "+nanos.Length+" stable identities");
                var catalog=ContentAssets.Load<WorldCatalog>("World/Catalog");
                Require(catalog,"world definition");
                var reference=catalog.Tiles.First(t=>t.Visuals>0 && t.Colliders>0);
                ContentAssets.Read<WorldTileContent,bool>(reference.Resource,tile=> {
                    Require(tile,"world cell "+reference.Id);
                    Require(tile.Terrain,"terrain");
                    foreach(var placement in tile.Visuals)Require(placement.Model,"cell model "+placement.Name);
                    foreach(var collider in tile.Colliders)Require(collider.Mesh,"collision "+collider.Name);
                    return true;
                });
                Debug.Log("CONTENT LAYOUT PLAYER SMOKE PASSED: catalogs, UI, audio, character, world terrain, placements and collision dependencies.");
                Application.Quit(0);
            }
            catch(Exception error)
            {
                Debug.LogException(error);
                Application.Quit(1);
            }
        }
        static void Require(UnityEngine.Object asset,string label)
        {
            if(!asset)throw new InvalidOperationException("Missing content: "+label);
        }
    }
}
#endif
