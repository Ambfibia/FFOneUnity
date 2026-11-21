using System;
using System.Collections.Generic;
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

        public static int Zone(int mapNumber, Vector3 position, bool episode = false)
        {
            if ((mapNumber != 0 && !episode) || !float.IsFinite(position.x) || !float.IsFinite(position.z)) return Unavailable;
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
        const string MapRolePrefix = "ff-world-map-";
        const string MapHiddenClass = MapRolePrefix + "hidden";
        static readonly string[] MapPanRoles = { "pan-left", "pan-right", "pan-up", "pan-down" };
        VisualElement mapRoot;
        Image mapImage, mapPlayer;
        Label mapLocation;
        VisualElement mapZoomBar;
        readonly Dictionary<string, Button> mapButtons = new();
        readonly Dictionary<string, Image> mapArtwork = new();
        readonly VisualElement[] mapZoomHighlights = new VisualElement[3];
        Rect mapTarget, mapUv;
        int mapZone = RetrobutionMapLayout.Unavailable, mapStep;
        Vector2 mapPan;
        float mapHeading;

        int CurrentMapZone()
        {
            var state = session.Snapshot;
            return RetrobutionMapLayout.Zone(state.MapNumber, state.WorldPosition, travelCatalog?.EpisodeMaps.Contains(state.MapNumber) == true);
        }

        void BindMap(VisualElement root)
        {
            mapRoot = root;
            RetrobutionImage.BindAll(root);
            mapImage = MapElement<Image>(root, "image");
            mapPlayer = MapElement<Image>(root, "player");
            mapLocation = MapElement<Label>(root, "location");
            mapZoomBar = MapElement<VisualElement>(root, "zoom-bar");
            mapArtwork.Clear();
            foreach (var family in new[] { "payzone", "freezone", "darkzone" })
            {
                int count = family == "payzone" ? 4 : 2;
                for (int i = 0; i < count; i++)
                {
                    string role = family + "-" + i;
                    mapArtwork.Add(role, MapElement<Image>(root, role));
                }
            }
            mapArtwork.Add("unavailable", MapElement<Image>(root, "unavailable"));
            mapButtons.Clear();
            mapPan = Vector2.zero;
            mapZone = CurrentMapZone();
            mapStep = RetrobutionMapLayout.InitialStep(mapZone);
            mapTarget = mapUv = RetrobutionMapLayout.Camera(mapZone, mapStep, MapPosition());
            BindMapButton("close", () => Windows.Close(), true);
            BindMapButton("help", () => OpenGuide(15), true);
            BindMapInteractions(root);
            BindMapButton("zoom-in", () => SetMapStep(Mathf.Min(mapStep + 1, 2), mapTarget.center));
            BindMapButton("zoom-out", () => SetMapStep(Mathf.Max(mapStep - 1, 0), mapTarget.center));
            for (int index = 0; index < 3; index++)
            {
                int step = index;
                BindMapButton("zoom-step-" + index, () => SetMapStep(step, mapTarget.center));
                mapZoomHighlights[index] = MapElement<VisualElement>(root, $"zoom-step-{index}-selected");
            }
            BindMapButton("my-view", () => SetMapStep(3, MapPosition()));
            BindMapButton("world-view", () => SetMapStep(mapZone == RetrobutionMapLayout.Overworld ? 0 : 2, MapPosition()));
            BindMapPan(root, "pan-left", Vector2.left); BindMapPan(root, "pan-right", Vector2.right);
            BindMapPan(root, "pan-up", Vector2.up); BindMapPan(root, "pan-down", Vector2.down);
            if (language == "ru")
            {
                MapElement<Label>(root, "my-view-label").text = "МОЯ ПОЗИЦИЯ";
                MapElement<Label>(root, "world-view-label").text = "ВЕСЬ МИР";
                MapElement<Label>(root, "show-filters-label").text = "ФИЛЬТРЫ";
                MapElement<Label>(root, "location-heading").text = "местоположение:";
            }
            // UI scheduling pauses with panel detachment, so closed views do not retain an update loop.
            root.schedule.Execute(() => {
                if (disposed || mapRoot != root || Windows.Current != GameplayWindow.Map) return;
                if (interactionSuspended || prompt != null || gameGuide != null) { mapPan = Vector2.zero; return; }
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
                mapMarkers = mapFilterStrip = null;
                mapMarkerTemplate = null;
                mapMarkerPool.Clear();
                mapButtons.Clear();
                mapArtwork.Clear();
                Array.Clear(mapZoomHighlights, 0, mapZoomHighlights.Length);
                mapLocation = null;
                mapZoomBar = null;
                presenceTimer?.Pause(); presenceTimer = null;
                mapPan = Vector2.zero; mapRoot = null; mapImage = null; mapPlayer = null;
            });
            RefreshMap(true);
        }

        static T MapElement<T>(VisualElement root, string role) where T : VisualElement =>
            FFView.RequireClass<T>(root, MapRolePrefix + role);

        static void SetMapVisible(VisualElement element, bool visible) =>
            element.EnableInClassList(MapHiddenClass, !visible);

        void BindMapButton(string role, Action action, bool sound = false)
        {
            var button = MapElement<Button>(mapRoot, role);
            mapButtons.Add(role, button);
            button.clicked += () => {
                if (sound) playSound?.Invoke("button");
                action();
            };
        }

        void BindMapPan(VisualElement root, string role, Vector2 direction)
        {
            var button = MapElement<Button>(root, role);
            mapButtons.Add(role, button);
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

        internal sealed class PadCursor : VisualElement
        {
            public PadCursor()
            {
                pickingMode = PickingMode.Ignore;
                style.position = Position.Absolute;
                style.width = style.height = 18;
                generateVisualContent += context =>
                {
                    var pen = context.painter2D;
                    pen.strokeColor = Color.white; pen.lineWidth = 2;
                    pen.BeginPath(); pen.Arc(new Vector2(9, 9), 7, 0, 360); pen.ClosePath(); pen.Stroke();
                    pen.fillColor = Color.white;
                    pen.BeginPath(); pen.Arc(new Vector2(9, 9), 2, 0, 360); pen.ClosePath(); pen.Fill();
                };
            }
        }

        PadCursor padMapCursor;
        Vector2 padMapPosition = new(485, 316);

        internal void HidePadMapCursor() { if (padMapCursor != null) padMapCursor.visible = false; }

        internal bool NavigateMap(UnityEngine.InputSystem.Gamepad pad, float dt)
        {
            if (Windows.Current != GameplayWindow.Map || mapRoot == null || interactionSuspended || gameGuide != null || prompt != null) return false;
            if (padMapCursor?.panel == null)
            {
                var viewport = MapElement<VisualElement>(mapRoot, "viewport");
                padMapPosition = new Vector2(485, 316);
                padMapCursor = new PadCursor(); viewport.Add(padMapCursor);
            }
            padMapCursor.visible = true;
            Vector2 pan = pad.rightStick.ReadValue();
            if (pan.sqrMagnitude > 0 && mapStep > 0)
            {
                mapTarget.position += Vector2.Scale(pan, mapTarget.size) * (dt / 3);
                mapTarget = RetrobutionMapLayout.Clamp(mapZone, mapTarget);
            }
            var cursor = pad.leftStick.ReadValue();
            padMapPosition += new Vector2(cursor.x, -cursor.y) * (360 * dt);
            padMapPosition = new Vector2(Mathf.Clamp(padMapPosition.x, 50, 919), Mathf.Clamp(padMapPosition.y, 46, 585));
            padMapCursor.style.left = padMapPosition.x - 9; padMapCursor.style.top = padMapPosition.y - 9;
            if (mapZone < 0 || !mapImage.image) return true;
            if (pad.dpad.right.wasPressedThisFrame) SetMapStep(3, MapPosition());
            else if (pad.dpad.left.wasPressedThisFrame && mapZone != RetrobutionMapLayout.Tutorial)
                SetMapStep(mapZone == RetrobutionMapLayout.Overworld ? 0 : 2, MapPosition());
            else if (pad.dpad.up.wasPressedThisFrame && mapZone == RetrobutionMapLayout.Overworld)
                SetMapStep(Mathf.Min(2, mapStep + 1), mapTarget.center);
            else if (pad.dpad.down.wasPressedThisFrame && mapZone == RetrobutionMapLayout.Overworld)
                SetMapStep(Mathf.Clamp(mapStep - 1, 0, 2), mapTarget.center);
            if (pad.buttonSouth.wasPressedThisFrame) ChangeWaypoint(padMapPosition);
            return true;
        }

        void SetMapStep(int step, Vector2 center)
        {
            if (mapZone == RetrobutionMapLayout.Unavailable || interactionSuspended || prompt != null) return;
            playSound?.Invoke("button");
            mapStep = step; mapTarget = RetrobutionMapLayout.Camera(mapZone, step, center);
            mapProjectionDirty = true; UpdateMapFilters();
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
            int zone = CurrentMapZone();
            if (zone != mapZone)
            {
                mapZone = zone; mapStep = RetrobutionMapLayout.InitialStep(zone);
                mapTarget = mapUv = RetrobutionMapLayout.Camera(zone, mapStep, MapPosition());
                mapContentsDirty = true; UpdateMapFilters();
                _ = RequestMapPresence();
            }
            bool available = mapZone != RetrobutionMapLayout.Unavailable;
            string family = mapZone switch { RetrobutionMapLayout.Future or RetrobutionMapLayout.Tutorial => "freezone", RetrobutionMapLayout.Dark => "darkzone", _ => "payzone" };
            int texture = mapZone == RetrobutionMapLayout.Overworld ? mapStep : mapStep - 2;
            string binding = available ? $"{family}-{texture}" : "unavailable";
            mapImage.image = mapArtwork[binding].image;
            mapButtons["my-view"].SetEnabled(available && mapStep != 3);
            mapButtons["world-view"].SetEnabled(available && mapStep == 3 && mapZone != RetrobutionMapLayout.Tutorial);
            bool zoom = available && mapZone == RetrobutionMapLayout.Overworld && mapStep != 3;
            mapButtons["zoom-in"].SetEnabled(zoom && mapStep < 2);
            mapButtons["zoom-out"].SetEnabled(zoom && mapStep > 0);
            foreach (string role in MapPanRoles) mapButtons[role].SetEnabled(available);
            bool overworld = mapZone == RetrobutionMapLayout.Overworld;
            for (int i = 0; i < 3; i++)
            {
                var button = mapButtons["zoom-step-" + i];
                button.SetEnabled(zoom && mapStep != i);
                SetMapVisible(button, overworld);
                SetMapVisible(mapZoomHighlights[i], zoom && mapStep == i);
            }
            SetMapVisible(mapButtons["zoom-in"], overworld);
            SetMapVisible(mapButtons["zoom-out"], overworld);
            SetMapVisible(mapZoomBar, overworld);
            mapLocation.text = available ? minimap.PlaceName.text : (language == "ru" ? "Карта недоступна" : "Map unavailable");
            DrawMap();
        }

        void DrawMap()
        {
            if (mapRoot == null || mapImage.image == null) return;
            var draw = RetrobutionMapLayout.DrawRect(mapZone, mapStep, mapImage.image.width, mapImage.image.height);
            mapImage.style.left = draw.x; mapImage.style.top = draw.y;
            mapImage.style.width = draw.width; mapImage.style.height = draw.height;
            mapImage.uv = mapZone == RetrobutionMapLayout.Unavailable ? new Rect(0, 0, 1, 1) : mapUv;
            DrawMapContents(draw);
            bool visible = mapZone != RetrobutionMapLayout.Unavailable && mapTarget.Contains(MapPosition());
            SetMapVisible(mapPlayer, visible);
            if (!visible) return;
            Vector2 marker = RetrobutionMapLayout.Marker(session.Snapshot.WorldPosition, mapUv, draw);
            mapPlayer.style.left = marker.x; mapPlayer.style.top = marker.y;
            mapPlayer.style.rotate = new Rotate(new Angle(mapHeading, AngleUnit.Degree));
        }
    }
}
