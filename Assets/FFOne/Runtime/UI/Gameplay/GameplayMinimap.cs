using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FFOne.Content;
using FFOne.Tutorial;
using UnityEngine;
using UnityEngine.UIElements;

namespace FFOne.UI
{
    // cnGUINanocom.InitCycleFMBar overrides the serialized 174px window before RenderMenu.
    // The tile viewport shares the tutorial's 4x4 atlas and FM-ring renderer.
    internal sealed class GameplayMinimap
    {
        internal const string Art = "ui/en/gameplay/minimap/";
        public VisualElement View { get; }
        public VisualElement Overlay { get; }
        public Label PlaceName { get; }
        readonly TutorialFusionMeter meter;
        readonly VisualElement objective;
        readonly Label objectiveTitle, objectiveBody;
        readonly Image[] tiles = new Image[4];
        readonly int[] tileIds = new int[4];
        readonly List<TutorialMinimapLayout.Sample> samples = new(4);
        readonly Image camera, player;
        Vector3 lastPosition = Vector3.positiveInfinity;
        float lastRatio;

        public GameplayMinimap(VisualElement parent, string prefix, string title)
        {
            View = FFWidgets.Group(parent, prefix + "-minimap");
            FFWidgets.Rect(View, 0, -4, 187, 183);
            meter = new TutorialFusionMeter(FFWidgets.Texture(Art + "fusionmeter/regular"), FFWidgets.Texture(Art + "fusionmeter/regular_right"));
            meter.name = prefix + "-fusion-matter-meter";
            FFWidgets.Rect(meter, 12, 3, 176, 176); View.Add(meter);
            FFWidgets.Image(View, prefix + "-minimap-frame", Art + "fusionmatter_us", 7, -4, 187, 183);
            var viewport = FFWidgets.Group(View, prefix + "-minimap-viewport");
            FFWidgets.Rect(viewport, 26, 17, 148, 148); viewport.style.overflow = Overflow.Hidden;
            viewport.style.borderTopLeftRadius = viewport.style.borderTopRightRadius = viewport.style.borderBottomLeftRadius = viewport.style.borderBottomRightRadius = 74;
            for (int i = 0; i < tiles.Length; i++) tiles[i] = FFWidgets.Image(viewport, prefix + "-minimap-tile-" + i, null, 0, 0, 148, 148);
            Overlay = FFWidgets.Group(View, prefix + "-minimap-markers");
            FFWidgets.Rect(Overlay, 0, 0, 187, 183);
            camera = FFWidgets.Image(View, prefix + "-minimap-camera", Art + "camera", 26, 17, 148, 148);
            player = FFWidgets.Image(View, prefix + "-minimap-player", Art + "map_icon_00", 91, 82, 18, 18);
            FFWidgets.Image(View, prefix + "-minimap-line", Art + "line_minimap", 26, 17, 148, 148);
            PlaceName = FFWidgets.Label(View, prefix + "-minimap-name", title, 42, 121, 120, 60, true, 12);
            PlaceName.style.color = Color.yellow;

            objective = FFWidgets.Group(View, prefix + "-current-objective");
            FFWidgets.Rect(objective, 29, 186, 148, 264);
            objective.style.overflow = Overflow.Hidden;
            objectiveTitle = ObjectiveLabel(prefix + "-objective-title", 14);
            objectiveTitle.style.marginBottom = 10;
            objectiveBody = ObjectiveLabel(prefix + "-objective-body", 12);
            SetObjective(null, false);
        }

        Label ObjectiveLabel(string name, int size)
        {
            var label = new Label { name = name, pickingMode = PickingMode.Ignore, enableRichText = false };
            label.AddToClassList("ff-label");
            label.AddToClassList("ff-copy");
            FFWidgets.Font(label, false, size);
            label.style.whiteSpace = WhiteSpace.Normal;
            label.style.unityTextAlign = TextAnchor.UpperLeft;
            label.style.color = Color.white;
            label.style.flexShrink = 0;
            objective.Add(label);
            return label;
        }

        public void SetObjective(GameplayMissionPresentation mission, bool visible)
        {
            objective.style.display = visible && mission != null ? DisplayStyle.Flex : DisplayStyle.None;
            objectiveTitle.text = mission?.Title ?? "";
            objectiveBody.text = mission?.Objective ?? "";
        }

        public static void Layout(VisualElement view, float width, float height)
        {
            if (view == null || !(width > 0 && height > 0)) return;
            float scale = RetrobutionLayout.Scale(height);
            view.style.left = width - 187 * scale; view.style.top = -4 * scale;
            view.style.transformOrigin = new TransformOrigin(0, 0);
            view.style.scale = new Scale(new Vector3(scale, scale, 1));
        }

        static string TileAddress(int tile) => Art + "tiles/all_minimap_" + tile.ToString("00");

        public static Task PreloadTilesAsync(Vector3 position, CancellationToken cancellation)
        {
            var initial = new List<TutorialMinimapLayout.Sample>(4);
            TutorialMinimapLayout.Tiles(position, 8, initial);
            return Task.WhenAll(initial.Select(t => ContentAssets.GetAsync<Texture2D>(TileAddress(t.Tile), cancellation)));
        }

        async Task BindTileAsync(int index, int tile)
        {
            try
            {
                var texture = await ContentAssets.GetAsync<Texture2D>(TileAddress(tile));
                if (tileIds[index] == tile) tiles[index].image = texture;
            }
            catch (Exception failure)
            {
                if (tileIds[index] != tile) return;
                tileIds[index] = 0;
                Debug.LogException(failure);
            }
        }

        readonly List<Image> groupMarkers = new();
        public void SetGroup(IReadOnlyList<(int id, string name, Vector3 position)> members, Vector3 playerPosition, float ratio)
        {
            // The atlas viewport is 148px wide, centered on the player and spans 32*ratio world units.
            float scale = 148f / (32 * Mathf.Clamp(ratio, 4, 16));
            for (int i = 0; i < members.Count; i++)
            {
                if (i == groupMarkers.Count)
                    groupMarkers.Add(FFWidgets.Image(Overlay, "minimap-group-" + i, "ui/en/world-map/markers/map_icon_17", 0, 0, 16, 16));
                var icon = groupMarkers[i];
                Vector3 offset = (members[i].position - playerPosition) * scale;
                bool visible = offset.x * offset.x + offset.z * offset.z < 74 * 74;
                icon.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
                if (!visible) continue;
                icon.style.left = 92 + offset.x;
                icon.style.top = 83 - offset.z;
                icon.tooltip = members[i].name;
            }
            for (int i = members.Count; i < groupMarkers.Count; i++) groupMarkers[i].style.display = DisplayStyle.None;
        }

        public void Update(Vector3 position, float ratio, float fusion, float heading, float cameraHeading, float alpha = 1)
        {
            meter.SetFraction(fusion);
            player.style.rotate = new Rotate(new Angle(heading));
            camera.style.rotate = new Rotate(new Angle(cameraHeading));
            player.tintColor = new Color(1, 1, 1, alpha);
            if (position == lastPosition && ratio == lastRatio) return;
            lastPosition = position; lastRatio = ratio;
            TutorialMinimapLayout.Tiles(position, ratio, samples);
            for (int i = 0; i < tiles.Length; i++)
            {
                var image = tiles[i];
                image.style.display = i < samples.Count ? DisplayStyle.Flex : DisplayStyle.None;
                if (i >= samples.Count) continue;
                var sample = samples[i];
                if (tileIds[i] != sample.Tile)
                {
                    tileIds[i] = sample.Tile;
                    image.image = null;
                    _ = BindTileAsync(i, sample.Tile);
                }
                FFWidgets.Rect(image, sample.Destination); image.uv = sample.Uv;
            }
        }
    }
}
