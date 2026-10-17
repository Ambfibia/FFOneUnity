using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace FFOne.UI
{
    /// <summary>Primary WorldMapMode UV and draw geometry; original map units are world metres.</summary>
    public static class RetrobutionMapLayout
    {
        public const int Unavailable = -1, Future = 0, Dark = 1, Tutorial = 2, Overworld = 3;
        static readonly float[] Zoom = { 1f, .5f, .25f, 950f / 8192f };
        public static readonly Rect Viewport = new Rect(20, 14, 950, 624);

        public static int Zone(int mapNumber, Vector3 position)
        {
            // Runtime only establishes overworld map 0. Retrobution uses bInsMap+iEp_ID
            // for its no-map gate; an arbitrary server map number is not a source zone ID.
            if (mapNumber != 0 || !float.IsFinite(position.x) || !float.IsFinite(position.z)) return Unavailable;
            var point = new Vector2(position.x, position.z);
            if (new Rect(5632, 512, 2048, 2048).Contains(point)) return Future;
            if (new Rect(1024, 5120, 2560, 2048).Contains(point)) return Dark;
            if (new Rect(0, 0, 1536, 1536).Contains(point)) return Tutorial;
            return Overworld;
        }

        public static Rect Bounds(int zone)
        {
            Rect world = zone switch {
                Future => new Rect(5632, 512, 2048, 2048),
                Dark => new Rect(1024, 5120, 2560, 2048),
                Tutorial => new Rect(0, 0, 1536, 1536),
                _ => new Rect(0, 0, 8192, 8192)
            };
            return new Rect(world.x / 8192, world.y / 8192, world.width / 8192, world.height / 8192);
        }

        public static int InitialStep(int zone) => zone == Tutorial ? 3 : zone == Overworld ? 0 : 2;

        public static Rect Camera(int zone, int step, Vector2 center)
        {
            if (step < 0 || step > 3) throw new ArgumentOutOfRangeException(nameof(step));
            float width = Zoom[step], height = step == 0 ? 1 : width * .6568421f;
            return Clamp(zone, new Rect(center.x - width / 2, center.y - height / 2, width, height));
        }

        public static Rect Clamp(int zone, Rect uv)
        {
            Rect bounds = Bounds(zone);
            uv.x = Mathf.Clamp(uv.x, bounds.xMin, Mathf.Max(bounds.xMin, bounds.xMax - uv.width));
            uv.y = Mathf.Clamp(uv.y, bounds.yMin, Mathf.Max(bounds.yMin, bounds.yMax - uv.height));
            return uv;
        }

        public static Rect DrawRect(int zone, int step, float textureWidth, float textureHeight)
        {
            var result = new Rect(0, 0, Viewport.width, Viewport.height);
            if (zone != Overworld || step != 0 || textureWidth <= 0 || textureHeight <= 0) return result;
            // Source DoWindow intentionally distributes unused space asymmetrically.
            if (textureWidth / result.width > textureHeight / result.height)
            {
                float gap = result.height - textureHeight * result.width / textureWidth;
                result.y += gap / 3; result.height -= gap * 2 / 3;
            }
            else
            {
                float gap = result.width - textureWidth * result.height / textureHeight;
                result.x += gap / 3; result.width -= gap * 2 / 3;
            }
            return result;
        }

        public static Vector2 Marker(Vector3 position, Rect uv, Rect draw) => new Vector2(
            draw.x + (position.x / 8192 - uv.x) / uv.width * draw.width - 8,
            draw.y + draw.height - (position.z / 8192 - uv.y) / uv.height * draw.height - 8);
    }

    public sealed partial class GameplayUiController
    {
        VisualElement mapRoot;
        Image mapImage, mapPlayer;
        Rect mapTarget, mapUv;
        int mapZone = RetrobutionMapLayout.Unavailable, mapStep;
        Vector2 mapPan;
        float mapHeading;

        void BindMap(VisualElement root)
        {
            mapRoot = root;
            RetrobutionImage.BindAll(root);
            mapImage = FFView.Require<Image>(root, "map-image");
            mapPlayer = FFView.Require<Image>(root, "map-player");
            mapPan = Vector2.zero;
            mapZone = RetrobutionMapLayout.Zone(session.Snapshot.MapNumber, session.Snapshot.WorldPosition);
            mapStep = RetrobutionMapLayout.InitialStep(mapZone);
            mapTarget = mapUv = RetrobutionMapLayout.Camera(mapZone, mapStep, MapPosition());
            ActionButton(root, "close", () => Windows.Close());
            FFView.Require<Button>(root, "help").SetEnabled(false);
            FFView.Require<Button>(root, "show-filters").SetEnabled(false);
            ActionButton(root, "zoom-in", () => SetMapStep(Mathf.Min(mapStep + 1, 2), mapTarget.center));
            ActionButton(root, "zoom-out", () => SetMapStep(Mathf.Max(mapStep - 1, 0), mapTarget.center));
            for (int index = 0; index < 3; index++)
            {
                int step = index;
                ActionButton(root, "zoom-step-" + index, () => SetMapStep(step, mapTarget.center));
            }
            ActionButton(root, "my-view", () => SetMapStep(3, MapPosition()));
            ActionButton(root, "world-view", () => SetMapStep(mapZone == RetrobutionMapLayout.Overworld ? 0 : 2, MapPosition()));
            BindMapPan(root, "pan-left", Vector2.left); BindMapPan(root, "pan-right", Vector2.right);
            BindMapPan(root, "pan-up", Vector2.up); BindMapPan(root, "pan-down", Vector2.down);
            root.Query<TextElement>().ForEach(text => FFWidgets.Font(text, false, 12));
            if (language == "ru")
            {
                FFView.Require<Label>(root, "my-view-label").text = "МОЯ ПОЗИЦИЯ";
                FFView.Require<Label>(root, "world-view-label").text = "ВЕСЬ МИР";
                FFView.Require<Label>(root, "show-filters-label").text = "ФИЛЬТРЫ";
                FFView.Require<Label>(root, "map-location-heading-label").text = "местоположение:";
            }
            // UI scheduling pauses with panel detachment, so closed views do not retain an update loop.
            root.schedule.Execute(() => {
                if (disposed || mapRoot != root || Windows.Current != GameplayWindow.Map) return;
                if (interactionSuspended || prompt != null) { mapPan = Vector2.zero; return; }
                if (mapPan != Vector2.zero)
                {
                    mapTarget.position += Vector2.Scale(mapPan, mapTarget.size) * (Time.deltaTime / 8);
                    mapTarget = RetrobutionMapLayout.Clamp(mapZone, mapTarget);
                }
                mapUv = new Rect(Mathf.Lerp(mapUv.x, mapTarget.x, .2f), Mathf.Lerp(mapUv.y, mapTarget.y, .2f),
                    Mathf.Lerp(mapUv.width, mapTarget.width, .2f), Mathf.Lerp(mapUv.height, mapTarget.height, .2f));
                DrawMap();
            }).Every(16);
            root.RegisterCallback<DetachFromPanelEvent>(_ => {
                if (mapRoot != root) return;
                mapPan = Vector2.zero; mapRoot = null; mapImage = null; mapPlayer = null;
            });
            RefreshMap(true);
        }

        void BindMapPan(VisualElement root, string name, Vector2 direction)
        {
            var button = FFView.Require<Button>(root, name);
            button.RegisterCallback<PointerDownEvent>(evt => {
                if (evt.button != 0 || !button.enabledInHierarchy || interactionSuspended || prompt != null) return;
                mapPan = direction; button.CapturePointer(evt.pointerId);
            });
            button.RegisterCallback<PointerUpEvent>(evt => {
                mapPan = Vector2.zero;
                if (button.HasPointerCapture(evt.pointerId)) button.ReleasePointer(evt.pointerId);
            });
            button.RegisterCallback<PointerCaptureOutEvent>(_ => mapPan = Vector2.zero);
            button.RegisterCallback<NavigationSubmitEvent>(_ => {
                if (interactionSuspended || prompt != null || !button.enabledInHierarchy) return;
                mapTarget.position += Vector2.Scale(direction, mapTarget.size) / 8;
                mapTarget = RetrobutionMapLayout.Clamp(mapZone, mapTarget);
            });
        }

        Vector2 MapPosition()
        {
            var position = session.Snapshot.WorldPosition;
            return float.IsFinite(position.x) && float.IsFinite(position.z) ? new Vector2(position.x, position.z) / 8192 : new Vector2(.5f, .5f);
        }

        void SetMapStep(int step, Vector2 center)
        {
            if (mapZone == RetrobutionMapLayout.Unavailable || interactionSuspended || prompt != null) return;
            mapStep = step; mapTarget = RetrobutionMapLayout.Camera(mapZone, step, center);
            RefreshMap();
        }

        /// <summary>Call from the gameplay movement owner; the entry UI snapshot has no heading field.</summary>
        public void SetMapHeading(float yaw)
        {
            if (float.IsFinite(yaw)) mapHeading = yaw;
        }

        void RefreshMap(bool initial = false)
        {
            if (mapRoot == null || (!initial && mapRoot.panel == null)) return;
            int zone = RetrobutionMapLayout.Zone(session.Snapshot.MapNumber, session.Snapshot.WorldPosition);
            if (zone != mapZone)
            {
                mapZone = zone; mapStep = RetrobutionMapLayout.InitialStep(zone);
                mapTarget = mapUv = RetrobutionMapLayout.Camera(zone, mapStep, MapPosition());
            }
            bool available = mapZone != RetrobutionMapLayout.Unavailable;
            string family = mapZone switch { RetrobutionMapLayout.Future or RetrobutionMapLayout.Tutorial => "freezone", RetrobutionMapLayout.Dark => "darkzone", _ => "payzone" };
            int texture = mapZone == RetrobutionMapLayout.Overworld ? mapStep : mapStep - 2;
            string binding = available ? $"map-{family}-{texture}" : "map-unavailable";
            mapImage.image = FFView.Require<Image>(mapRoot, binding).image;
            FFView.Require<Button>(mapRoot, "my-view").SetEnabled(available && mapStep != 3);
            FFView.Require<Button>(mapRoot, "world-view").SetEnabled(available && mapStep == 3 && mapZone != RetrobutionMapLayout.Tutorial);
            bool zoom = available && mapZone == RetrobutionMapLayout.Overworld && mapStep != 3;
            FFView.Require<Button>(mapRoot, "zoom-in").SetEnabled(zoom && mapStep < 2);
            FFView.Require<Button>(mapRoot, "zoom-out").SetEnabled(zoom && mapStep > 0);
            foreach (string name in new[] { "pan-left", "pan-right", "pan-up", "pan-down" }) FFView.Require<Button>(mapRoot, name).SetEnabled(available);
            for (int i = 0; i < 3; i++)
            {
                var button = FFView.Require<Button>(mapRoot, "zoom-step-" + i);
                button.SetEnabled(zoom && mapStep != i);
                button.style.display = mapZone == RetrobutionMapLayout.Overworld ? DisplayStyle.Flex : DisplayStyle.None;
                FFView.Require<VisualElement>(mapRoot, $"zoom-step-{i}-selected").style.display = zoom && mapStep == i ? DisplayStyle.Flex : DisplayStyle.None;
            }
            foreach (string name in new[] { "zoom-in", "zoom-out", "zoom-bar" })
                FFView.Require<VisualElement>(mapRoot, name).style.display = mapZone == RetrobutionMapLayout.Overworld ? DisplayStyle.Flex : DisplayStyle.None;
            // No verified dong-name lookup exists here. Do not manufacture a district name from coordinates.
            FFView.Require<Label>(mapRoot, "map-location-label").text = available ? "" : (language == "ru" ? "Карта недоступна" : "Map unavailable");
            DrawMap();
        }

        void DrawMap()
        {
            if (mapRoot == null || mapImage.image == null) return;
            var draw = RetrobutionMapLayout.DrawRect(mapZone, mapStep, mapImage.image.width, mapImage.image.height);
            mapImage.style.left = draw.x; mapImage.style.top = draw.y;
            mapImage.style.width = draw.width; mapImage.style.height = draw.height;
            mapImage.uv = mapZone == RetrobutionMapLayout.Unavailable ? new Rect(0, 0, 1, 1) : mapUv;
            bool visible = mapZone != RetrobutionMapLayout.Unavailable && mapTarget.Contains(MapPosition());
            mapPlayer.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
            if (!visible) return;
            Vector2 marker = RetrobutionMapLayout.Marker(session.Snapshot.WorldPosition, mapUv, draw);
            mapPlayer.style.left = marker.x; mapPlayer.style.top = marker.y;
            mapPlayer.style.rotate = new Rotate(new Angle(mapHeading, AngleUnit.Degree));
        }
    }
}
