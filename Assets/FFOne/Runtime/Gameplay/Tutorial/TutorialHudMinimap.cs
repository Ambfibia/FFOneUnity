using System.Collections.Generic;
using FFOne.UI;
using UnityEngine;
using UnityEngine.UIElements;

namespace FFOne.Tutorial
{
    public sealed partial class TutorialController
    {
        const string TutorialMinimapArt = "ui/en/gameplay/minimap/";
        VisualElement tutorialMinimap, tutorialMinimapMenu;
        int tutorialObjectiveTask = -1;
        GameplayMissionPresentation tutorialObjective;
        Image tutorialMapWaypoint;
        GameplayMinimap minimapDisk;
        float tutorialMapRatio = 8;

        void CreateTutorialMinimap()
        {
            minimapDisk = new GameplayMinimap(tutorialHudRoot, "tutorial", Russian ? "СКВЕР ТЕХНОЛОГИЙ" : "TECH SQUARE");
            tutorialMinimap = minimapDisk.View;
            tutorialMapWaypoint = FFWidgets.Image(minimapDisk.Overlay, "tutorial-minimap-waypoint", TutorialMinimapArt + "map_icon_02", 91.5f, 82.5f, 17, 17);
            var menu = new VisualElement { name = "tutorial-minimap-menu" };
            tutorialMinimapMenu = menu;
            FFWidgets.Rect(menu, 151, 0, 36, 30); tutorialMinimap.Add(menu);
            menu.RegisterCallback<PointerDownEvent>(evt => {
                if (evt.button == 0 && Stage.Allows(8)) TutorialUiAction(() => mainMenu = !mainMenu);
            });
            tutorialObjectiveTask = -1;
        }

        void LayoutTutorialMinimap()
        {
            if (tutorialMinimap?.parent == tutorialHudRoot) GameplayMinimap.Layout(tutorialMinimap, tutorialHudRoot.resolvedStyle.width, tutorialHudRoot.resolvedStyle.height);
        }

        void TickTutorialMinimap()
        {
            if (tutorialMinimap == null) return;
            // The disk is part of the shared HUD. The lesson controls when its menu can open.
            tutorialMinimapMenu.style.display = mainMenu ? DisplayStyle.None : DisplayStyle.Flex;
            tutorialMinimapMenu.pickingMode = Stage.Allows(8) ? PickingMode.Position : PickingMode.Ignore;
            if (!Player) return;
            if (tutorialObjectiveTask != currentTask)
            {
                tutorialObjectiveTask = currentTask;
                tutorialObjective = GameplayUiCatalog.Load().Mission(currentTask, Language);
            }
            minimapDisk.SetObjective(tutorialObjective, currentTask > 0 && !mainMenu && !journal && interacting == 0);
            float fusion = auxiliarySequence == TutorialAuxiliarySequence.TalkButtercup1 ? auxiliaryFusionMatter / 220f
                : Stage.Chapter == 5 && Stage.Step >= 4 ? 0
                : Mathf.Clamp01(((tutorialHudEntry.FusionMatterFraction ?? 0) * 220 + tutorialEarnedFusionMatter) / 220);
            minimapDisk.Update(Player.position, tutorialMapRatio, fusion, Player.eulerAngles.y,
                ViewCamera ? ViewCamera.transform.eulerAngles.y : yaw, auxiliaryMyPoint ? Mathf.Abs(Mathf.Sin(Time.time * Mathf.PI)) : 1);
            tutorialMapWaypoint.tintColor = new Color(1, 1, 1, auxiliaryWaypointFlash ? Mathf.Abs(Mathf.Sin(Time.time * Mathf.PI)) : 1);
            var objective = Actor(ObjectiveNpc);
            tutorialMapWaypoint.style.display = objective ? DisplayStyle.Flex : DisplayStyle.None;
            if (!objective) return;
            Vector3 delta = objective.transform.position - Player.position;
            Vector2 offset = new(delta.x, -delta.z);
            offset *= 148 / (tutorialMapRatio * 32);
            offset = Vector2.ClampMagnitude(offset, 65);
            tutorialMapWaypoint.style.left = 26 + 74 + offset.x - 8.5f;
            tutorialMapWaypoint.style.top = 17 + 74 + offset.y - 8.5f;
        }

        void ReleaseTutorialMinimap()
        {
            tutorialMinimap = tutorialMinimapMenu = null; minimapDisk = null; tutorialMapWaypoint = null;
            tutorialObjective = null; tutorialObjectiveTask = -1;

        }
    }

    /// <summary>The original left/right FM ring textures, clipped at the source 87,88 pixel pivot.</summary>
    sealed class TutorialFusionMeter : VisualElement
    {
        readonly Texture2D left, right;
        float fraction;
        public TutorialFusionMeter(Texture2D left, Texture2D right)
        {
            name = "tutorial-fusion-matter-meter"; pickingMode = PickingMode.Ignore;
            this.left = left; this.right = right;
            generateVisualContent += context => { Draw(context, left); Draw(context, right); };
        }
        public void SetFraction(float value)
        {
            value = float.IsFinite(value) ? Mathf.Clamp01(value) : 0;
            if (Mathf.Approximately(value, fraction)) return;
            fraction = value; MarkDirtyRepaint();
        }
        void Draw(MeshGenerationContext context, Texture2D texture)
        {
            if (!texture || fraction <= 0) return;
            int segments = Mathf.Max(1, Mathf.CeilToInt(fraction * 128));
            var mesh = context.Allocate(segments + 2, segments * 3, texture);
            Vector2 pivot = new(87, 88);
            Vertex Point(Vector2 point) => new() {
                position = new Vector3(point.x, point.y, Vertex.nearZ), tint = Color.white,
                uv = mesh.uvRegion.position + Vector2.Scale(mesh.uvRegion.size, new Vector2(point.x / 176, 1 - point.y / 176))
            };
            mesh.SetNextVertex(Point(pivot));
            for (int i = 0; i <= segments; i++)
            {
                float angle = fraction * Mathf.PI * 2 * i / segments;
                Vector2 direction = new(Mathf.Sin(angle), -Mathf.Cos(angle));
                float dx = Mathf.Abs(direction.x) < .000001f ? float.PositiveInfinity : (direction.x > 0 ? 176 - pivot.x : pivot.x) / Mathf.Abs(direction.x);
                float dy = Mathf.Abs(direction.y) < .000001f ? float.PositiveInfinity : (direction.y > 0 ? 176 - pivot.y : pivot.y) / Mathf.Abs(direction.y);
                mesh.SetNextVertex(Point(pivot + direction * Mathf.Min(dx, dy)));
            }
            for (int i = 0; i < segments; i++)
            { mesh.SetNextIndex(0); mesh.SetNextIndex((ushort)(i + 1)); mesh.SetNextIndex((ushort)(i + 2)); }
        }
    }

    /// <summary>Source cnGUINanocom.RenderMaps: four 2048m tiles per axis, 148px viewport.</summary>
    public static class TutorialMinimapLayout
    {
        public readonly struct Sample
        {
            public readonly int Tile;
            public readonly Rect Uv, Destination;
            public Sample(int tile, Rect uv, Rect destination) { Tile = tile; Uv = uv; Destination = destination; }
        }

        public static void Tiles(Vector3 player, float ratio, List<Sample> result)
        {
            result.Clear();
            if (!float.IsFinite(player.x) || !float.IsFinite(player.z) || !float.IsFinite(ratio)) return;
            ratio = Mathf.Clamp(ratio, 4, 16);
            float x = Mathf.Clamp01(player.x / 8192), z = Mathf.Clamp01(player.z / 8192), half = ratio / 512;
            float left = Mathf.Clamp01(x - half), right = Mathf.Clamp01(x + half), bottom = Mathf.Clamp01(z - half), top = Mathf.Clamp01(z + half);
            float width = Mathf.Max(float.Epsilon, right - left), height = Mathf.Max(float.Epsilon, top - bottom);
            int firstCol = Mathf.Clamp(Mathf.FloorToInt(left * 4), 0, 3), lastCol = Mathf.Clamp(Mathf.FloorToInt((right - 1e-7f) * 4), 0, 3);
            int firstRow = Mathf.Clamp(Mathf.FloorToInt(bottom * 4), 0, 3), lastRow = Mathf.Clamp(Mathf.FloorToInt((top - 1e-7f) * 4), 0, 3);
            for (int row = firstRow; row <= lastRow; row++)
                for (int col = firstCol; col <= lastCol; col++)
                {
                    float x0 = Mathf.Max(left, col / 4f), x1 = Mathf.Min(right, (col + 1) / 4f);
                    float z0 = Mathf.Max(bottom, row / 4f), z1 = Mathf.Min(top, (row + 1) / 4f);
                    if (x1 <= x0 || z1 <= z0) continue;
                    result.Add(new Sample((3 - row) * 4 + col + 1,
                        new Rect(x0 * 4 - col, z0 * 4 - row, (x1 - x0) * 4, (z1 - z0) * 4),
                        new Rect((x0 - left) / width * 148, (top - z1) / height * 148, (x1 - x0) / width * 148, (z1 - z0) / height * 148)));
                }
        }
    }
}
