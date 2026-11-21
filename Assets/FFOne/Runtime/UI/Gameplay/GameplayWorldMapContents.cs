using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FFOne.Content;
using FFOne.Network;
using FFOne.World;
using UnityEngine;
using UnityEngine.UIElements;
using static FFOne.UI.FFWidgets;

namespace FFOne.UI
{
    public sealed partial class GameplayUiController
    {
        const string MapMarkerArt = "ui/en/world-map/markers/";
        static readonly int[][] MapFilters = { new[]{15,16}, new[]{10,4,5,6,7,8,9,31,33}, new[]{13}, new[]{12}, new[]{32}, new[]{22},
            new[]{21}, new[]{34}, new[]{24}, new[]{11}, new[]{30}, new[]{25} };
        static readonly int[] WaypointHues = { 235, 290, 60, 100, 30, 180 };
        [Serializable] sealed class SavedMap
        {
            public int filters = 4095;
            public Vector2[] positions = new Vector2[6];
            public int occupied;
        }
        SavedMap savedMap = new();
        readonly HashSet<int> presentNpcTypes = new();
        readonly Dictionary<int, (int type, Vector3 position)> pendingPlacements = new();
        (int type, Vector3 position)[] serverPlacements;
        static (int type, Vector3 position)[] authoredPlacements;
        readonly List<(Vector3 position, int icon, string name)> mapItems = new();
        readonly List<Image> mapMarkerPool = new();
        readonly Dictionary<(int icon, int color), Texture2D> waypointArt = new();
        Rect paintedUv, paintedTarget;
        Vector3 paintedPlayer;
        bool mapContentsDirty, mapProjectionDirty, mapSnapshotStarted, filtersVisible = true;
        ulong mapLastSync;
        float lastPresenceRequest = -5;
        VisualElement mapMarkers;
        VisualElement mapFilterStrip;
        VisualTreeAsset mapMarkerTemplate;
        IVisualElementScheduledItem presenceTimer;
        string MapIconRoute(int id) => MapMarkerArt + (id == 34 ? "map_icon_woosh" : "map_icon_" + id.ToString("00"));

        void LoadMapPreferences()
        {
            string json = PlayerPrefs.GetString("FFOne.WorldMap", "");
            if (json.Length == 0) return;
            try
            {
                var map = JsonUtility.FromJson<SavedMap>(json);
                if (map?.positions?.Length == 6 && map.filters >= 0 && map.filters <= 4095 && map.occupied >= 0 && map.occupied <= 63 &&
                    map.positions.All(p => float.IsFinite(p.x) && float.IsFinite(p.y) && p.x >= 0 && p.y >= 0 && p.x <= 8192 && p.y <= 8192)) savedMap = map;
            }
            catch (ArgumentException) { }
        }
        void StoreMapPreferences()
        {
            mapProjectionDirty = true;
            if (travelClient == null) return;
            PlayerPrefs.SetString("FFOne.WorldMap", JsonUtility.ToJson(savedMap)); PlayerPrefs.Save();
        }
        void BindMapInteractions(VisualElement root)
        {
            var filterTemplate = MapTemplate(root, "filter-template");
            mapMarkerTemplate = MapTemplate(root, "marker-template");
            var viewport = MapElement<VisualElement>(root, "viewport");
            mapMarkers = MapElement<VisualElement>(viewport, "markers");
            mapFilterStrip = MapElement<VisualElement>(root, "filters");
            mapMarkerPool.Clear();
            var tip = MapElement<VisualElement>(viewport, "tooltip");
            var tipLabel = MapElement<Label>(tip, "tooltip-caption");
            AutoFit(tipLabel);
            viewport.RegisterCallback<PointerMoveEvent>(e => {
                if (e.target is Image target && target.parent == mapMarkers && !string.IsNullOrEmpty(target.tooltip))
                {
                    var point = viewport.WorldToLocal(e.position);
                    tip.style.left = Mathf.Clamp(point.x + 8, 0, 750); tip.style.top = Mathf.Clamp(point.y + 12, 0, 604);
                    SetMapVisible(tip, true);
                    if (tipLabel.text != target.tooltip)
                    {
                        tipLabel.text = target.tooltip;
                        FitTree(tip);
                    }
                }
                else SetMapVisible(tip, false);
            });
            viewport.RegisterCallback<PointerLeaveEvent>(_ => SetMapVisible(tip, false));
            for (int i = 0; i < MapFilters.Length; i++)
            {
                int index = i;
                var button = CloneMapElement<Button>(filterTemplate, "filter");
                button.name = "map-filter-" + i;
                MapElement<Image>(button, "filter-icon").image = Texture(MapIconRoute(MapFilters[i][0]));
                button.clicked += () => {
                    savedMap.filters ^= 1 << index;
                    StoreMapPreferences();
                    UpdateMapFilters();
                    playSound?.Invoke("button");
                };
                mapFilterStrip.Add(button);
            }
            BindMapButton("show-filters", () => { filtersVisible = !filtersVisible; UpdateMapFilters(); }, true);
            bool dragging = false;
            Vector2 last = default;
            viewport.RegisterCallback<PointerDownEvent>(e => {
                if (gameGuide != null || interactionSuspended || prompt != null || mapZone < 0 || !mapImage.image) return;
                var p = viewport.WorldToLocal(e.position);
                if (e.button == 1) { ChangeWaypoint(p); e.StopPropagation(); return; }
                if (e.button != 0 || mapStep == 0 || !new Rect(50, 46, 870, 540).Contains(p)) return;
                last = p; dragging = true; viewport.CapturePointer(e.pointerId);
            });
            viewport.RegisterCallback<PointerMoveEvent>(e => {
                if (!dragging || !viewport.HasPointerCapture(e.pointerId) || !viewport.enabledInHierarchy) return;
                var p = viewport.WorldToLocal(e.position); var delta = p - last; last = p;
                mapTarget.position += new Vector2(-delta.x / 950 * mapTarget.width, delta.y / 624 * mapTarget.height);
                mapTarget = RetrobutionMapLayout.Clamp(mapZone, mapTarget);
            });
            viewport.RegisterCallback<PointerUpEvent>(e => { if (viewport.HasPointerCapture(e.pointerId)) viewport.ReleasePointer(e.pointerId); });
            viewport.RegisterCallback<PointerCaptureOutEvent>(_ => dragging = false);
            mapContentsDirty = mapProjectionDirty = true; UpdateMapFilters();
            _ = RequestMapPresence();
        }

        static VisualTreeAsset MapTemplate(VisualElement root, string role)
        {
            var instance = MapElement<TemplateContainer>(root, role);
            var template = instance.templateSource;
            instance.RemoveFromHierarchy();
            return template;
        }

        static T CloneMapElement<T>(VisualTreeAsset template, string role) where T : VisualElement
        {
            var element = MapElement<T>(template.Instantiate(), role);
            element.RemoveFromHierarchy();
            return element;
        }

        void UpdateMapFilters()
        {
            bool allowed = mapZone >= 0 && mapStep != 3;
            SetMapVisible(mapButtons["show-filters"], allowed);
            SetMapVisible(mapFilterStrip, allowed && filtersVisible);
            for (int i = 0; i < mapFilterStrip.childCount; i++)
                mapFilterStrip[i].EnableInClassList("ff-world-map-filter-enabled", (savedMap.filters & (1 << i)) != 0);
        }
        async Task RequestMapPresence()
        {
            if (travelClient == null || mapZone < 0) return;
            // A reopened map may still have the current response in flight. RustyFusion rate-limits to 1 s.
            float wait = lastPresenceRequest + 1.05f - Time.unscaledTime;
            if (wait > 0)
            {
                presenceTimer ??= mapRoot?.schedule.Execute(() => { presenceTimer = null; _ = RequestMapPresence(); }).StartingIn((long)(wait * 1000) + 1);
                return;
            }
            lastPresenceRequest = Time.unscaledTime;
            try { await travelClient.RequestMapPresenceAsync(mapLastSync, travelToken); }
            catch (OperationCanceledException) when (travelToken.IsCancellationRequested) { }
            catch (Exception e) { if (!disposed) Error?.Invoke(e.Message); }
        }
        internal bool ReceiveMapPresence(ProtocolFrame f)
        {
            if (f.PacketType == ProtocolTravel.WarpOk || f.PacketType == ProtocolGameplay.RegenSuccess || f.PacketType == 0x31000141)
            {
                presentNpcTypes.Clear(); pendingPlacements.Clear(); serverPlacements = null;
                mapSnapshotStarted = false; mapContentsDirty = true; mapLastSync = 0;
                if (Windows.Current == GameplayWindow.Map) _ = RequestMapPresence();
                return f.PacketType == 0x31000141;
            }
            bool types = f.PacketType == 0x31000137;
            if (!types && f.PacketType != 0x3100013b) return false;
            var bytes = f.Payload;
            if (bytes.Length < 8) throw new ProtocolException("Truncated map presence header.");
            int flags = Protocol0104.Read32(bytes, 0), count = Protocol0104.Read32(bytes, 4);
            if (count < 0 || count > (types ? 1020 : 203)) throw new ProtocolException("Invalid map presence count.");
            Protocol0104.RequireSize(bytes, 8 + count * (types ? 4 : 20));
            if (types)
            {
                for (int i = 0; i < count; i++) if (Protocol0104.Read32(bytes, 8 + 4 * i) <= 0) throw new ProtocolException("Invalid map NPC type.");
                if (flags != 0) presentNpcTypes.Clear();
                for (int i = 0; i < count; i++) presentNpcTypes.Add(Protocol0104.Read32(bytes, 8 + 4 * i));
                mapLastSync = travelClient?.World?.ServerTime ?? 0;
                mapContentsDirty = true; return true;
            }
            if ((flags & ~3) != 0 || (!mapSnapshotStarted && (flags & 1) == 0)) throw new ProtocolException("Unexpected map placement chunk.");
            bool first = (flags & 1) != 0;
            var chunk = new Dictionary<int, (int type, Vector3 position)>(count);
            for (int i = 0; i < count; i++)
            {
                int o = 8 + 20 * i, id = Protocol0104.Read32(bytes, o), type = Protocol0104.Read32(bytes, o + 4);
                var position = WorldCoordinates.FromServer(Protocol0104.Read32(bytes, o + 8), Protocol0104.Read32(bytes, o + 12), Protocol0104.Read32(bytes, o + 16));
                if (id <= 0 || type <= 0 || (!first && pendingPlacements.ContainsKey(id)) || !chunk.TryAdd(id, (type, position)))
                    throw new ProtocolException("Invalid or duplicate NPC map placement.");
            }
            if (first) { pendingPlacements.Clear(); mapSnapshotStarted = true; }
            foreach (var placement in chunk) pendingPlacements.Add(placement.Key, placement.Value);
            if ((flags & 2) != 0) { serverPlacements = pendingPlacements.Values.ToArray(); pendingPlacements.Clear(); mapSnapshotStarted = false; mapContentsDirty = true; }
            return true;
        }
        static void LoadAuthoredPlacements()
        {
            authoredPlacements ??= NativeData.Object("data/missions/client-npc-waypoints")["rows"].Select(row => {
                    var p = row["clientPosition"]; return ((int)row["npcType"], new Vector3((float)p[0], (float)p[1], (float)p[2]));
                }).ToArray();
        }

        string JournalContactDistrict(int type)
        {
            if (missionNpc?.NpcType == type)
                return CharacterLocations.District(CharacterLocations.Resolve(missionNpc.X, missionNpc.Y), language);
            LoadAuthoredPlacements();
            var points = (serverPlacements ?? Array.Empty<(int type, Vector3 position)>()).Where(p => p.type == type);
            if (!points.Any()) points = authoredPlacements.Where(p => p.type == type);
            var place = points.OrderBy(p => (p.position - session.Snapshot.WorldPosition).sqrMagnitude)
                .Select(p => CharacterLocations.ResolveWorld(p.position)).FirstOrDefault(p => p != null);
            return CharacterLocations.District(place, language);
        }
        void RefreshMapItems()
        {
            mapContentsDirty = false; mapProjectionDirty = true; mapItems.Clear();
            if (presentNpcTypes.Count == 0) return;
            var rules = WorldMissionCatalog.Load(); var state = session.Snapshot;
            var offers = new HashSet<int>(rules.Tasks.Where(r => MissionOffered(r, rules, state)).Select(r => r.GiverNpc));
            var advance = new HashSet<int>(state.RunningQuests.Where(q => q.TaskId > 0 && GameplayMissionProgression.ObjectivesReady(q, state.QuestInventory))
                .Select(q => rules.Find(q.TaskId)?.EndNpc ?? 0));
            bool radar = state.HasBuff(13);
            var catalog = WorldNpcCatalog.Load();
            void Add(int type, Vector3 point)
            {
                if (!presentNpcTypes.Contains(type) || new Rect(6144, 6144, 2048, 2048).Contains(new Vector2(point.x, point.z))) return;
                var npc = catalog.Find(type); if (npc == null || npc.Category == 0 && npc.Sound != 2 && !radar) return;
                int zone = RetrobutionMapLayout.Zone(0, point);
                if (zone != RetrobutionMapLayout.Overworld && zone != mapZone) return;
                var place = CharacterLocations.ResolveWorld(point);
                if (place == null || place.District.IndexOf("unknown", StringComparison.OrdinalIgnoreCase) >= 0) return;
                int icon = advance.Contains(type) ? 16 : offers.Contains(type) ? 15 : npc.MapIcon;
                if (icon > 0 && icon <= 34) mapItems.Add((point, icon, npc.Name));
            }
            if (serverPlacements != null) foreach (var p in serverPlacements) Add(p.type, p.position);
            if (serverPlacements == null || radar)
            {
                LoadAuthoredPlacements();
                foreach (var p in authoredPlacements)
                    if (serverPlacements == null || catalog.Find(p.type)?.Category == 0 && catalog.Find(p.type)?.Sound != 2) Add(p.type, p.position);
            }
        }
        readonly List<(int id, string name, Vector3 position)> mapGroupMembers = new();

        void UpdateGroupMapMarkers(NetworkWorldController world)
        {
            if (!world || !world.IsReady) return;
            var state = session.Snapshot;
            int count = 0;
            foreach (var member in social.Members)
            {
                if (member.Id == state.PlayerId || member.Map != state.MapNumber) continue;
                Vector3 position = WorldCoordinates.FromServer(member.X, member.Y, member.Z);
                foreach (var actor in world.Replicas.Actors)
                    if (actor.Entity.Kind == WorldEntityKind.Player && actor.Entity.Id == member.Id)
                    { position = actor.transform.position; break; }
                var entry = (member.Id, member.Name, position);
                if (count == mapGroupMembers.Count) { mapGroupMembers.Add(entry); mapProjectionDirty = true; }
                else if (mapGroupMembers[count] != entry) { mapGroupMembers[count] = entry; mapProjectionDirty = true; }
                count++;
            }
            if (mapGroupMembers.Count > count)
            {
                mapGroupMembers.RemoveRange(count, mapGroupMembers.Count - count);
                mapProjectionDirty = true;
            }
            minimap.SetGroup(mapGroupMembers, state.WorldPosition, minimapRatio);
        }

        void DrawMapContents(Rect draw)
        {
            if (mapMarkers == null) return;
            if (mapContentsDirty) RefreshMapItems();
            var player = session.Snapshot.WorldPosition;
            if (!mapProjectionDirty && paintedUv == mapUv && paintedTarget == mapTarget && paintedPlayer == player) return;
            mapProjectionDirty = false; paintedUv = mapUv; paintedTarget = mapTarget; paintedPlayer = player;
            int used = 0;
            void Mark(Vector3 point, int icon, string caption, Texture2D art = null, float angle = 0, int waypoint = -1)
            {
                var p = RetrobutionMapLayout.Marker(point, mapUv, draw);
                if (used == mapMarkerPool.Count)
                {
                    var next = CloneMapElement<Image>(mapMarkerTemplate, "marker");
                    mapMarkers.Add(next);
                    mapMarkerPool.Add(next);
                }
                var mark = mapMarkerPool[used++]; mark.image = art ? art : Texture(MapIconRoute(icon)); mark.tooltip = caption;
                mark.userData = waypoint;
                SetMapVisible(mark, true);
                mark.style.left = p.x; mark.style.top = p.y; mark.style.rotate = new Rotate(new Angle(angle));
            }
            if (mapZone >= 0)
            {
                foreach (var entry in mapItems)
                {
                    if (!mapTarget.Contains(new Vector2(entry.position.x, entry.position.z) / 8192)) continue;
                    if (mapStep == 3)
                    { if (Mathf.Abs(entry.position.x - player.x) > 475 || Mathf.Abs(entry.position.z - player.z) > 312) continue; }
                    else
                    {
                        int filter = Array.FindIndex(MapFilters, icons => Array.IndexOf(icons, entry.icon) >= 0);
                        if (filter < 0 || (savedMap.filters & (1 << filter)) == 0) continue;
                    }
                    Mark(entry.position, entry.icon, entry.name);
                }
                foreach (var member in mapGroupMembers)
                    if (mapUv.Contains(new Vector2(member.position.x, member.position.z) / 8192))
                        Mark(member.position, 17, member.name);
                for (int i = 0; i < 6; i++) if ((savedMap.occupied & (1 << i)) != 0)
                {
                    var point = savedMap.positions[i] / 8192;
                    var clipped = new Vector2(Mathf.Clamp(point.x, mapUv.xMin, mapUv.xMax), Mathf.Clamp(point.y, mapUv.yMin, mapUv.yMax));
                    bool edge = point != clipped;
                    float angle = edge ? Mathf.Atan2(-(clipped.y - mapUv.center.y) / mapUv.height, (clipped.x - mapUv.center.x) / mapUv.width) * 180 / 3.14f + 90 : 0;
                    int icon = edge ? 3 : 2;
                    Mark(new Vector3(clipped.x * 8192, 0, clipped.y * 8192), icon, LocalizationCatalog.Lookup("ui.world_map.custom_waypoint", language).Replace("{number}", (i + 1).ToString()), WaypointTexture(icon, i), angle, i);
                }
            }
            for (int i = used; i < mapMarkerPool.Count; i++) SetMapVisible(mapMarkerPool[i], false);
        }
        void ChangeWaypoint(Vector2 point)
        {
            var hit = mapMarkerPool.LastOrDefault(m => !m.ClassListContains(MapHiddenClass) && m.userData is int id && id >= 0 && m.layout.Contains(point));
            if (hit != null) savedMap.occupied &= ~(1 << (int)hit.userData);
            else
            {
                var draw = RetrobutionMapLayout.DrawRect(mapZone, mapStep, mapImage.image.width, mapImage.image.height);
                if (!draw.Contains(point) || savedMap.occupied == 63) return;
                int color = 0; while ((savedMap.occupied & (1 << color)) != 0) color++;
                savedMap.positions[color] = new Vector2(mapUv.x + (point.x - draw.x) / draw.width * mapUv.width,
                    mapUv.y + (1 - (point.y - draw.y) / draw.height) * mapUv.height) * 8192;
                savedMap.occupied |= 1 << color;
            }
            StoreMapPreferences(); DrawMap();
        }
        Texture2D WaypointTexture(int icon, int color)
        {
            if (waypointArt.TryGetValue((icon, color), out var texture)) return texture;
            var source = Texture(MapIconRoute(icon)); if (!source) return null;
            var rt = RenderTexture.GetTemporary(source.width, source.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var previous = RenderTexture.active;
            try
            {
                Graphics.Blit(source, rt); RenderTexture.active = rt;
                texture = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false);
                texture.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0);
                var pixels = texture.GetPixels();
                for (int i = 0; i < pixels.Length; i++)
                {
                    float a = pixels[i].a; Color.RGBToHSV(pixels[i], out _, out var s, out var v);
                    pixels[i] = Color.HSVToRGB(WaypointHues[color] / 360f, s, v); pixels[i].a = a;
                }
                texture.SetPixels(pixels); texture.Apply(false, true); waypointArt[(icon, color)] = texture;
                return texture;
            }
            finally { RenderTexture.active = previous; RenderTexture.ReleaseTemporary(rt); }
        }
    }
}
