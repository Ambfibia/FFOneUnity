using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FFOne.UI;
using FFOne.World;

namespace FFOne.Content
{
    public static class NativeEffectLoading
    {
        public static async Task PrepareWorldAsync(CancellationToken cancellation)
        {
            await Task.WhenAll(NativeData.ObjectAsync("map/shared/projectiles/catalog",cancellation),NativeData.ObjectAsync("effects/skills/catalog",cancellation),
                WorldNanoEffectLibrary.PrepareAsync(cancellation));
            var addresses=new HashSet<string>();
            void Add(int id,bool projectile=false){if(id>0)addresses.Add(NativeEffectDefinition.Resource(id,projectile));}
            foreach(var bullet in WorldWeaponCatalog.Load().Bullets){Add(bullet.Particle,true);Add(bullet.Impact);}
            var ui=GameplayUiCatalog.Load();for(int i=1;i<=25;i++){Add(ui.BuffEffect(i));Add(ui.BuffEffect(i,true));}
            var skills=await NativeData.ObjectAsync("effects/skills/catalog",cancellation);
            foreach(var effect in skills["effects"])Add((int)effect["id"]);
            foreach(var npc in WorldNpcCatalog.Load().Definitions)Add(npc.Effect);
            Add(RecallPointPresentation.SelectedEffect);
            Add(10); Add(527); Add(528); Add(529);
            foreach(string style in new[]{"standard","board","cloud"})addresses.Add(NativeEffectDefinition.VehicleResource(style));
            await Task.WhenAll(addresses.Select(a=>ContentAssets.GetAsync<NativeEffectDefinition>(a,cancellation)));
        }
    }
}
