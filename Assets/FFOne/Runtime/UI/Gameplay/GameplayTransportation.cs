using System.Linq;
using System.Collections.Generic;
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
        const string TransportArt = "ui/en/transportation/", MapArtRoot = "ui/en/world-map/";
        TravelLocation transportHub;
        TravelTrip selectedTrip;
        bool transportDeparting, turboTravel;
        Rect transportCamera, transportTarget;
        readonly List<(VisualElement image,Vector3 location,float offset)> transportPins=new();
        string TransportText(string key) => LocalizationCatalog.Lookup("ui.transportation." + key, language);

        internal static string TransportLocationName(TravelLocation location, string language)
        {
            string table = location.Type == 1 ? "transportation_warp_string" : "broomstick_string";
            string key = $"content.tabledata.transportation.{table}.{location.Id}.str_location_name";
            string name = LocalizationCatalog.Lookup(key, language);
            return name == key || string.IsNullOrWhiteSpace(name) ? location.Name : name.Trim();
        }

        internal static string TransportRegionName(TravelLocation location, string language)
        {
            // Future is explicit in the transportation table, even outside named district bounds.
            string zone = location.IsFuture ? "The Future" : CharacterLocations.Resolve(location.X, location.Y)?.Zone;
            string key = zone switch
            {
                "The Future" => "future",
                "The Suburbs" => "suburbs",
                "Downtown" => "downtown",
                "The Wilds" => "wilds",
                "The Darklands" => "darklands",
                _ => "unknown"
            };
            return LocalizationCatalog.Lookup("content.transportation.region." + key, language);
        }

        const string TransportViewAddress = "ui/views/Retrobution/WorldMap";

        static TElement TransportRole<TElement>(VisualElement view, string role) where TElement : VisualElement =>
            FFView.RequireClass<TElement>(view, role);

        VisualElement BuildTransportView()
        {
            var root = FFView.InstantiateClass(TransportViewAddress, "transport-root");
            var panel = TransportRole<VisualElement>(root, "transport-window");
            var map = TransportRole<Image>(panel, "transport-map");
            var scan = TransportRole<VisualElement>(panel, "transport-scan");
            var large = TransportRole<VisualElement>(scan, "transport-scan-large");
            var small = TransportRole<VisualElement>(scan, "transport-scan-small");
            float opened = Time.time;
            scan.schedule.Execute(() =>
            {
                float age = Time.time - opened;
                large.style.top = Mathf.Repeat(age * .15f, 1 + 64f / 630) * 630 - 64;
                small.style.top = Mathf.Min(Mathf.Repeat(age * .5f, 1) * 630, 626);
                transportCamera.position = Vector2.Lerp(transportCamera.position, transportTarget.position, .2f);
                PlaceTransportMap(map);
            }).Every(16);
            void SetLabel(string role, string caption)
            {
                var label = TransportRole<Label>(panel, role);
                label.text = FFStyle.Caption(caption);
                AutoFit(label);
            }
            SetLabel("transport-title", TransportText("title"));
            SetLabel("transport-where", TransportText("where_to"));
            BindServicePortrait(TransportRole<Image>(panel, "transport-npc"), 90, 90, .5f, 1.2f, 0);
            TransportRole<Button>(panel, "transport-close").clicked += () =>
            {
                if (TravelBusy) return;
                playSound?.Invoke("button");
                Windows.Close();
            };
            var go = TransportRole<Button>(panel, "transport-go");
            go.text = FFStyle.Caption(TransportText("go_now"));
            go.clicked += () => _ = DepartTransport(root);
            AutoFit(go);
            var register = TransportRole<Button>(panel, "transport-register");
            register.name = "transport-register-" + transportHub.Type + "-" + transportHub.Id;
            register.text = T("REGISTER", "ОТКРЫТЬ");
            register.clicked += () => {
                playSound?.Invoke("button");
                _ = BeginTravel(ProtocolTravel.RegisterOk,
                    () => travelClient.RegisterTransportAsync(travelNpc.Id, transportHub.Type, transportHub.Id, travelToken));
            };
            AutoFit(register);
            var turbo = TransportRole<VisualElement>(panel, "transport-turbo-controls");
            turbo.EnableInClassList("transport-hidden", transportHub.Type != 2);
            if (transportHub.Type == 2)
            {
                SetLabel("transport-turbo-title", TransportText("turbo_travel"));
                TransportRole<Button>(turbo, "transport-turbo").clicked += () =>
                {
                    if (TravelBusy) return;
                    turboTravel = !turboTravel;
                    playSound?.Invoke("button");
                    FillTransport(root);
                };
            }
            else
                TransportRole<Button>(turbo, "transport-turbo").name = "";
            transportCamera = default;
            FillTransport(root);
            return root;
        }

        void FillTransport(VisualElement root)
        {
            var hub=transportHub; var panel=TransportRole<VisualElement>(root, "transport-window"); var list=TransportRole<ScrollView>(panel, "transport-routes");
            var scroll=list.scrollOffset;
            var markers=TransportRole<VisualElement>(panel, "transport-markers"); markers.Clear(); transportPins.Clear(); list.Clear();
            var start=WorldCoordinates.FromServer(hub.X,hub.Y,hub.Z);
            var player=session.Snapshot.WorldPosition; var point=new Vector2(player.x,player.z);
            bool future=new Rect(5632,512,2150.4f,2150.4f).Contains(point) || new Rect(0,0,1536,1536).Contains(point);
            bool dark=!future && new Rect(1024,5120,2560,2048).Contains(point);
            var selected=travelCatalog.Locations.FirstOrDefault(l=>selectedTrip!=null && l.Type==selectedTrip.Type && l.Id==selectedTrip.To);
            var focus=selected==null?start:WorldCoordinates.FromServer(selected.X,selected.Y,selected.Z);
            float w=future || hub.Type==2?.2880543f:.5761086f,h=future || hub.Type==2?.3075834f:.6151668f;
            float x=focus.x/8192-w/2,y=focus.z/8192-h/2;
            x=future?Mathf.Min(Mathf.Max(x,5632f/8192),7782.4f/8192-w):Mathf.Clamp(x,0,1-w);
            y=future?Mathf.Min(Mathf.Max(y,512f/8192),2662.4f/8192-h):Mathf.Clamp(y,0,1-h);
            transportTarget=new Rect(x,y,w,h); if(transportCamera.width==0)transportCamera=transportTarget;
            var map=TransportRole<Image>(panel, "transport-map");
            map.image=Texture(MapArtRoot+"maps/"+(future?"freezone_002":dark?"darkland_002":"payzone_004"));
            bool ready=!TravelBusy;
            bool registered=Unlocked(hub.Type,hub.Id);
            var subtitle=TransportRole<Label>(panel, "transport-subtitle");
            subtitle.text = string.IsNullOrEmpty(travelStatus)
                ? FFStyle.Caption(TransportLocationName(hub, language))
                : travelStatus;
            AutoFit(subtitle);
            var register = TransportRole<Button>(panel, "transport-register");
            register.EnableInClassList("transport-hidden", registered);
            register.SetEnabled(ready);
            var go = TransportRole<Button>(panel, "transport-go");
            go.SetEnabled(ready && registered && selectedTrip != null && session.Snapshot.Taros >= Fare(selectedTrip));
            TransportRole<VisualElement>(panel, "transport-turbo-check").EnableInClassList("transport-hidden", !turboTravel);
            TransportRole<Button>(panel, "transport-turbo").SetEnabled(ready);
            void Mark(TravelLocation loc, string sprite, float offset = 0)
            {
                var marker = (Image)FFView.InstantiateClass(TransportViewAddress, "transport-marker");
                marker.name = "marker-" + loc.Id;
                marker.image = Texture(TransportArt + "markers/" + sprite);
                markers.Add(marker);
                transportPins.Add((marker, WorldCoordinates.FromServer(loc.X, loc.Y, loc.Z), offset));
            }
            string kind=hub.Type==1?"warp":"wyvern";
            foreach(var trip in travelCatalog.Trips.Where(t=>t.Type==hub.Type && t.From==hub.Id && t.Npc==travelNpc.NpcType))
            {
                var dest=travelCatalog.Locations.FirstOrDefault(l=>l.Type==trip.Type && l.Id==trip.To);
                if(dest==null)continue;
                bool open=Unlocked(trip.Type,trip.To);
                bool isSelected=selectedTrip==trip;
                var row = (Button)FFView.InstantiateClass(TransportViewAddress, "transport-route-row");
                row.name = "transport-trip-" + trip.Id;
                row.clicked += () => { selectedTrip = trip; playSound?.Invoke("button"); FillTransport(root); };
                list.Add(row);
                TransportRole<VisualElement>(row, "transport-route-selection").EnableInClassList("transport-hidden", !isSelected);
                TransportRole<Image>(row, "transport-route-icon").image = Texture(dest.Icon is >= 0 and < 33
                    ? "icons/transport/transport_" + dest.Icon.ToString("00")
                    : TransportArt + "markers/fallback_route");
                var name = TransportRole<Label>(row, "transport-route-name");
                name.text = FFStyle.Caption(TransportLocationName(dest, language));
                AutoFit(name);
                var destination = WorldCoordinates.FromServer(dest.X, dest.Y, dest.Z);
                var region = TransportRole<Label>(row, "transport-route-region");
                region.text = FFStyle.Caption(TransportRegionName(dest, language));
                AutoFit(region);
                var fare = TransportRole<Label>(row, "transport-route-fare");
                fare.text = Fare(trip).ToString();
                fare.EnableInClassList("transport-fare--turbo", turboTravel);
                fare.EnableInClassList("transport-fare--unavailable", Fare(trip) > session.Snapshot.Taros);
                bool covered=NetworkWorldController.HasCoverage(ContentAssets.Load<WorldCatalog>("World/Catalog"),0,destination);
                row.SetEnabled(ready && registered && open && covered);
                Mark(dest,(open?"registered":"unregistered")+"_"+kind);
                if(isSelected && (hub.Type==2 || !future))Mark(dest,"selected_"+kind);
            }
            Mark(hub,"registered_"+kind); Mark(hub,"start_label",26);list.scrollOffset=scroll;PlaceTransportMap(map);
        }

        void PlaceTransportMap(Image map)
        {
            map.uv=transportCamera;
            foreach(var pin in transportPins)
            {
                float x=(pin.location.x/8192-transportCamera.x)/transportCamera.width*590-18+pin.offset;
                float y=(1-(pin.location.z/8192-transportCamera.y)/transportCamera.height)*630-18;
                pin.image.style.left=x;pin.image.style.top=y;
                pin.image.EnableInClassList("transport-hidden", x < 0 || x + 36 > 590 || y < 0 || y + 36 > 630);
            }
        }

        int Fare(TravelTrip trip) => trip.Cost * (turboTravel && trip.Type==2 ? 2 : 1);
        async Task DepartTransport(VisualElement root)
        {
            var trip=selectedTrip;
            if(!TravelAvailable || trip==null || !Unlocked(trip.Type,trip.To) || session.Snapshot.Taros<Fare(trip)) return;
            playSound?.Invoke("transportation_warp");
            ServiceVoice?.Invoke(travelNpc,"ClickMove");
            playSound?.Invoke("transportation_warp");
            transportDeparting=true; FillTransport(root);
            try
            {
                float start=Time.time;
                while(!disposed && Windows.View==root && Time.time-start<=1.5f)
                { if(travelToken.IsCancellationRequested)return; await Task.Yield(); }
            }
            finally { transportDeparting=false; }
            if(!disposed && Windows.View==root && session.Snapshot.Taros>=Fare(trip))
                await BeginTravel(ProtocolTravel.TripOk,()=>travelClient.TravelAsync(travelNpc.Id,trip.Id,travelToken));
        }
    }
}
