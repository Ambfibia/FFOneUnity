using System.Collections.Generic;
using System.Linq;
using FFOne.Network;
using UnityEngine;

namespace FFOne.World
{
    public sealed partial class NetworkWorldController
    {
        readonly Dictionary<int, WorldEruption> eruptions = new();

        readonly Dictionary<int, GameObject> corruptionWarnings = new();
        static readonly string[] CorruptionSounds = { "CorruptionAtypeCasting", "CorruptionBtypeCasting", "CorruptionCtypeCasting" };

        internal void ReceiveCorruptionReady(byte[] payload)
        {
            Protocol0104.RequireSize(payload, 20);
            int npc = Protocol0104.Read32(payload, 0), style = Protocol0104.Read16(payload, 6);
            if (style < 0 || style >= CorruptionSounds.Length) throw new ProtocolException("Unknown corruption style.");
            CancelEruption(npc);
            var caster = Replicas?.Actors.FirstOrDefault(a => a.Entity.Kind == WorldEntityKind.Npc && a.Entity.Id == npc);
            if (!caster || caster.Definition == null) return;
            var center = Link(caster.transform, "center");
            var group = new GameObject("Corruption warning " + npc);
            group.transform.SetParent(center ? center : caster.transform, false);
            if (center) group.transform.localRotation = Quaternion.Euler(90, 0, 0);
            corruptionWarnings[npc] = group;
            void Add(int id)
            {
                var definition = WorldCombatEffects.Find(id);
                if (definition) NativeCombatEffect.Spawn(group.transform, definition, View, null, caster.Definition.Radius * 2);
            }
            Add(530 + style);
            if (caster.Definition.Sound == 2) Add(592);
            NativeCombatEffect.PlayImpactSound(caster.transform, CorruptionSounds[style], caster.transform.position);
        }

        internal void ReceiveEruption(ProtocolFrame frame)
        {
            var p = frame.Payload;
            if (frame.PacketType == 0x31000025)
            {
                Protocol0104.RequireSize(p, 4);
                CancelEruption(Protocol0104.Read32(p, 0));
                return;
            }
            if (frame.PacketType == 0x31000020) Protocol0104.RequireSize(p, 20);
            else if (p == null || p.Length < 28) throw new ProtocolException("Missing NPC skill hit header.");
            int npc = Protocol0104.Read32(p, 0), skill = Protocol0104.Read16(p, 4);
            var caster = Replicas?.Actors.FirstOrDefault(a => a.Entity.Kind == WorldEntityKind.Npc && a.Entity.Id == npc);
            if (!caster || caster.Definition == null || skill <= 0 || caster.Definition.EruptionSkill != skill) return;
            var point = WorldCoordinates.FromServer(Protocol0104.Read32(p, 8), Protocol0104.Read32(p, 12), Protocol0104.Read32(p, 16));
            CancelEruption(npc);
            if (frame.PacketType == 0x31000020)
            {
                float diameter = (WorldSkillCatalog.Load().Find(skill)?.Area ?? 0) * 2f * 1.414f * 1.3f;
                if (diameter <= 0) return;
                var mark = new GameObject("Eruption warning " + npc).AddComponent<WorldEruption>();
                mark.transform.SetParent(ownedRoot.transform, false);
                eruptions[npc] = mark;
                mark.Show(point, diameter);
                NativeCombatEffect.PlayImpactSound(caster.transform, "MegaAttackSpell", caster.transform.position);
            }
            else
            {
                var effect = WorldCombatEffects.Find(766);
                if (effect) NativeCombatEffect.Spawn(ownedRoot.transform, effect, View, point, .6f);
                NativeCombatEffect.PlayImpactSound(caster.transform, "MegaAttackMissile", point);
            }
        }

        internal void CancelEruption(int npc)
        {
            if (corruptionWarnings.Remove(npc, out var warning) && warning) Destroy(warning);
            if (eruptions.Remove(npc, out var mark) && mark) Destroy(mark.gameObject);
        }
        internal void ClearEruptions()
        {
            foreach (var warning in corruptionWarnings.Values) if (warning) Destroy(warning);
            corruptionWarnings.Clear();
            foreach (var mark in eruptions.Values) if (mark) Destroy(mark.gameObject);
            eruptions.Clear();
        }
    }

    // Sample across the original warning artwork so ridges and ramps cannot cover a flat quad.
    internal sealed class WorldEruption : MonoBehaviour
    {
        readonly RaycastHit[] hits = new RaycastHit[16];
        Mesh surface;
        internal void Show(Vector3 center, float diameter)
        {
            transform.position = center;
            int sections = Mathf.Clamp(Mathf.CeilToInt(diameter / .35f), 4, 64), width = sections + 1;
            var points = new Vector3[width * width]; var uv = new Vector2[points.Length];
            var colors = new Color32[points.Length]; var onGround = new bool[points.Length];
            float above = Mathf.Max(1, diameter * .5f);
            for (int z = 0; z < width; z++)
                for (int x = 0; x < width; x++)
                {
                    int i = z * width + x;
                    uv[i] = new Vector2((float)x / sections, (float)z / sections);
                    var at = center + new Vector3((uv[i].x - .5f) * diameter, above, (uv[i].y - .5f) * diameter);
                    int count = Physics.RaycastNonAlloc(at, Vector3.down, hits, above + 8, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
                    float nearest = float.PositiveInfinity;
                    for (int j = 0; j < count; j++)
                    {
                        var hit = hits[j];
                        if (hit.collider is not (MeshCollider or TerrainCollider) || hit.normal.y < .05f) continue;
                        float distance = Mathf.Abs(hit.point.y - center.y);
                        if (distance >= nearest) continue;
                        nearest = distance;
                        points[i] = hit.point - center + hit.normal * .035f;
                        colors[i] = new Color32(255, 255, 255, 255);
                        onGround[i] = true;
                    }
                }
            var faces = new List<int>(sections * sections * 6);
            void Face(int a, int b, int c) { if (onGround[a] && onGround[b] && onGround[c]) { faces.Add(a); faces.Add(b); faces.Add(c); } }
            for (int z = 0; z < sections; z++)
                for (int x = 0; x < sections; x++)
                {
                    int i = width * z + x;
                    Face(i, i + width, i + 1); Face(i + 1, i + width, i + width + 1);
                }
            surface = new Mesh { name = "Surface of eruption warning", vertices = points, uv = uv, colors32 = colors };
            surface.SetTriangles(faces, 0); surface.RecalculateBounds();
            gameObject.AddComponent<MeshFilter>().sharedMesh = surface;
            gameObject.AddComponent<MeshRenderer>().sharedMaterial = Resources.Load<Material>("EruptionGround");
            Destroy(gameObject, 3);
        }
        void OnDestroy() { if (surface) Destroy(surface); }
    }
}
