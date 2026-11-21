using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FFOne.Content;
using FFOne.Tutorial;
using FFOne.Network;
using FFOne.World;
using UnityEngine;
using UnityEngine.UIElements;

namespace FFOne.UI
{
    public sealed partial class GameplayUiController
    {
        internal NetworkWorldController NanoRewardWorld { private get; set; }
        WorldNanoCompanion rewardPreview;
        readonly Queue<int> upcomingRewards = new();
        VisualElement rewardView, rewardPanel;
        StyleEnum<DisplayStyle> savedRewardHud, savedRewardNotices;
        Vector3 rewardCameraPosition;
        Quaternion rewardCameraRotation;
        int rewardNano, pendingRewardSkill;
        float rewardStartedAt, revealedAt, rewardAckAt, rewardRequestedAt;
        bool creationEffectShown, creationTrailsShown;

        void StartNanoReward(int id)
        {
            if (!NanoRewardWorld || !NanoRewardWorld.Player || !NanoRewardWorld.View) return;
            if (rewardPreview)
            {
                if (rewardNano != id && !upcomingRewards.Contains(id)) upcomingRewards.Enqueue(id);
                return;
            }
            var catalog = GameplayUiCatalog.Load();
            var powers = catalog.Tunes(id);
            if (powers.Count != 3) return;
            rewardView = FFView.OptionalClass("ui/views/Retrobution/MissionJournal", "nano-reward-screen");
            if (rewardView == null) return;
            if (Windows.IsOpen) Windows.Close();
            rewardNano = id;
            pendingRewardSkill = 0;
            rewardStartedAt = Time.unscaledTime;
            creationEffectShown = creationTrailsShown = false;
            revealedAt = rewardAckAt = -1;
            var world = NanoRewardWorld;
            rewardCameraPosition = world.View.transform.position;
            rewardCameraRotation = world.View.transform.rotation;
            var anchor = new GameObject("Acquired Nano preview");
            anchor.transform.SetParent(world.Player.parent, false);
            anchor.transform.SetPositionAndRotation(world.Player.position + world.Player.right * 1.1f + Vector3.up * 1.22f, world.Player.rotation);
            rewardPreview = anchor.AddComponent<WorldNanoCompanion>();
            rewardPreview.Initialize(world.Replicas, world.View, false);
            rewardPreview.RewardPreview = true;
            rewardPanel = FFView.RequireClass<VisualElement>(rewardView, "nano-reward-panel");
            FFView.RequireClass<Label>(rewardPanel,"nano-reward-title").text = catalog.Nano(id,language).Name;
            var rows = rewardPanel.Query<VisualElement>(className:"nano-reward-power").ToList();
            for (int i = 0; i < powers.Count; i++)
            {
                var power = powers[i];
                FFView.RequireClass<Image>(rows[i],"nano-reward-icon").image = FFWidgets.Texture(catalog.SkillIcon(power.Skill));
                FFView.RequireClass<Label>(rows[i],"nano-reward-name").text = catalog.NanoPowerName(id,power.Skill,language);
                string key = "content.tabledata.nano.nano_tune_string." + power.TextIndex + ".str_comment";
                FFView.RequireClass<Label>(rows[i],"nano-reward-description").text = SpeechText(key,power.Description);
                var button = FFView.RequireClass<Button>(rows[i],"nano-reward-select");
                button.text = T("SELECT","ВЫБРАТЬ");
                button.clicked += () => _ = SelectRewardPower(power);
            }
            rewardPanel.style.display = DisplayStyle.None;
            var hud = layers.Host(UiLayer.Hud);
            var notices = layers.Host(UiLayer.Notification);
            savedRewardHud = hud.style.display;
            savedRewardNotices = notices.style.display;
            hud.style.display = notices.style.display = DisplayStyle.None;
            layers.Add(rewardView, UiLayer.Cinematic);
            world.RewardCamera = true;
            InputGateChanged?.Invoke();
        }

        async Task SelectRewardPower(GameplayUiCatalog.NanoTune power)
        {
            if (!rewardPreview || pendingRewardSkill != 0 || !rewardPreview.Model || disposed) return;
            pendingRewardSkill = power.Skill;
            rewardRequestedAt = Time.unscaledTime;
            rewardPanel.SetEnabled(false);
            playSound?.Invoke("add_attribute");
            try { await travelClient.TuneNanoAsync(rewardNano,power.Id,Array.Empty<int>(),travelToken); }
            catch (OperationCanceledException) when (travelToken.IsCancellationRequested) { }
            catch (Exception error)
            {
                if (!disposed && rewardPreview) ResetRewardSelection(error.Message);
            }
        }

        void ReceiveNanoReward(ProtocolFrame frame)
        {
            if (!rewardPreview || pendingRewardSkill == 0) return;
            if (frame.PacketType == 0x31000055) { ResetRewardSelection(T("Tuning failed.","Настройка не удалась.")); return; }
            if (frame.PacketType != 0x31000029 || Protocol0104.Read16(frame.Payload,0) != rewardNano ||
                Protocol0104.Read16(frame.Payload,2) != pendingRewardSkill) return;
            rewardAckAt = Time.unscaledTime;
            rewardPanel.style.display = DisplayStyle.None;
            rewardPreview.ShowSkill(rewardNano,pendingRewardSkill);
        }

        void ResetRewardSelection(string message)
        {
            pendingRewardSkill = 0;
            rewardPanel.SetEnabled(true);
            Error?.Invoke(message);
        }

        void TickNanoReward()
        {
            if (!rewardPreview) return;
            var world = NanoRewardWorld;
            if (!world || !world.Player || session.Snapshot.Hp <= 0) { EndNanoReward(); return; }
            float now = Time.unscaledTime;
            float intro = now - rewardStartedAt;
            if (!creationEffectShown && intro >= .5f)
            {
                creationEffectShown = true;
                var at = rewardPreview.transform.position - Vector3.up * 1.2f;
                NativeCombatEffect.Spawn(rewardPreview.transform,705,1,false,world.View,4,at);
            }
            if (!creationTrailsShown && intro >= 1.7f)
            {
                creationTrailsShown = true;
                var source = world.Player.position + Vector3.up;
                var rewardTarget = rewardPreview.transform.position;
                foreach (int type in new[] { 76, 77 })
                {
                    var bullet = WorldWeaponCatalog.Load().Bullet(type);
                    var trail = NativeCombatEffect.Spawn(rewardPreview.transform,bullet.Particle,bullet.Scale,true,world.View,6,source);
                    trail.AddComponent<TutorialOniProjectile>().Initialize(() => rewardTarget,true);
                }
            }
            if (intro >= 2.5f && rewardPreview.NanoId == 0) rewardPreview.SetNano(rewardNano);
            if (!rewardPreview.Model)
            {
                if (now - rewardStartedAt > 30) EndNanoReward();
                return;
            }
            if (revealedAt < 0) { revealedAt = now; return; }
            float age = now - revealedAt;
            // Show the acquired model in the existing world camera before moving the camera for selection.
            if (age < .2f) return;
            var focus = (world.Player.position + Vector3.up * 1.15f + rewardPreview.Model.transform.position) * .5f;
            var right = world.Player.right;
            var camera = world.View.transform;
            var destination = focus + world.Player.forward * 3.9f + Vector3.up * .45f + right * .8f;
            // Viewed from the front, the player's right projects to screen left.
            // Frame both models in the part of the image to the left of the power panel.
            var rotation = Quaternion.LookRotation(focus - right * 1.15f - Vector3.up * .28f - destination, Vector3.up);
            float t = Mathf.Clamp01((age - .2f) / .3f);
            camera.SetPositionAndRotation(Vector3.Lerp(rewardCameraPosition,destination,t), Quaternion.Slerp(rewardCameraRotation,rotation,t));
            if (pendingRewardSkill == 0 && age >= 2 && rewardPanel.style.display.value == DisplayStyle.None)
            {
                rewardPanel.style.display = DisplayStyle.Flex;
                rewardPanel.Q<Button>(className:"nano-reward-select").Focus();
            }
            if (rewardAckAt >= 0 && now - rewardAckAt >= 1 && rewardPreview.SkillPresentationFinished) EndNanoReward();
            else if (pendingRewardSkill != 0 && rewardAckAt < 0 && now-rewardRequestedAt > 12)
                ResetRewardSelection(T("No tuning response.","Нет ответа на настройку."));
        }

        void EndNanoReward()
        {
            if (rewardPreview)
            {
                rewardPreview.Clear();
                UnityEngine.Object.Destroy(rewardPreview.gameObject);
                if (NanoRewardWorld && NanoRewardWorld.View)
                {
                    NanoRewardWorld.RewardCamera = false;
                    NanoRewardWorld.View.transform.SetPositionAndRotation(rewardCameraPosition,rewardCameraRotation);
                }
            }
            if (rewardView != null)
            {
                rewardView.RemoveFromHierarchy();
                layers.Host(UiLayer.Hud).style.display = savedRewardHud;
                layers.Host(UiLayer.Notification).style.display = savedRewardNotices;
            }
            rewardPanel = rewardView = null;
            rewardPreview = null;
            pendingRewardSkill = 0;
            if (disposed) { upcomingRewards.Clear(); return; }
            InputGateChanged?.Invoke();
            if (upcomingRewards.Count > 0) StartNanoReward(upcomingRewards.Dequeue());
            else ShowMissionLine();
        }
    }
}
