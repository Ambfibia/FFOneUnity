using System.Collections.Generic;
using System.Linq;
using FFOne.Editor;
using FFOne.World;
using NUnit.Framework;
using UnityEngine;
using UnityEditor;

namespace FFOne.Tests
{
    public sealed class WorldSourceBindingsTests
    {
        const string Document = "{\"id\":\"fixture\",\"billboards\":[{\"node\":\"billboard-node\",\"models\":[\"published\",\"not-exported\"],\"worldMatrix\":[[1,0,0,0],[0,1,0,0],[0,0,1,0],[0,0,0,1]]}]}";
        [Test]
        public void PublishedOmissionsMatchAcceptedSourcesForEveryAuthoredCell()
        {
            var definitions = AssetDatabase.FindAssets("t:WorldCellDefinition", new[] { "Assets/FFOne/Worlds" });
            Assert.AreEqual(170, definitions.Length);
            foreach (string guid in definitions)
            {
                var definition = AssetDatabase.LoadAssetAtPath<WorldCellDefinition>(AssetDatabase.GUIDToAssetPath(guid));
                Assert.AreEqual(1, definition.SourceBindingsVersion, definition.TileId);
                Assert.That(definition.UnpublishedModelIds, Is.EqualTo(WorldCellPublication.Calculate(definition)), definition.TileId);
            }
        }
        [TestCase(true)]
        [TestCase(false)]
        public void UnpublishedSourceGeometryIsReportedButAuthoredTargetDeletionRemainsAnError(bool keepAuthoredTarget)
        {
            var accepted=ScriptableObject.CreateInstance<WorldTileContent>();
            var root=new GameObject("Source binding fixture");
            try
            {
                accepted.Visuals=new[]{new WorldPlacement{LegacyId="published"}};
                var definition = ScriptableObject.CreateInstance<WorldCellDefinition>();
                definition.UnpublishedModelIds = WorldSourceBindings.FindUnpublishedModels(Document,accepted).ToArray();
                var omissions = new HashSet<string>(definition.UnpublishedModelIds);
                Object.DestroyImmediate(definition);
                Assert.That(omissions,Is.EquivalentTo(new[]{"not-exported"}));
                var bindings=new Dictionary<string,List<Transform>>();
                if(keepAuthoredTarget)
                {
                    var target=new GameObject("Authored target");target.transform.SetParent(root.transform);
                    bindings.Add("published",new List<Transform>{target.transform});
                }
                var behaviour=WorldBehaviour.Attach(root,Document,bindings,new Dictionary<string,List<Transform>>(),null,omissions);
                Assert.That(behaviour.Ready,Is.True);
                Assert.That(behaviour.SourceCoverageGaps,Is.EqualTo(1));
                Assert.That(behaviour.SourceCoverageNotes.Count,Is.EqualTo(1));
                Assert.That(behaviour.SourceCoverageNotes[0],Does.Contain("not-exported"));
                Assert.That(behaviour.Errors.Count,Is.EqualTo(keepAuthoredTarget ? 0 : 1));
                if(!keepAuthoredTarget)Assert.That(behaviour.Errors[0],Does.Contain("Missing model published"));
            }
            finally{Object.DestroyImmediate(root);Object.DestroyImmediate(accepted);}
        }
    }
}
