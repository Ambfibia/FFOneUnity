using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using FFOne.Content;
using UnityEngine;
using UnityEngine.UIElements;

namespace FFOne.UI
{
    internal sealed class GameplayRewardCounters
    {
        const string FusionMatter = "fusion-matter";
        const string Taros = "taros";
        readonly VisualElement root;
        readonly Counter fusionMatter, taros;
        readonly DropLane crates, potions, boosts;
        readonly VisualElement inventoryFull;
        readonly Label questNotice;
        readonly Queue<(int npc, int item, bool found)> questLoot = new();
        float questNoticeRemaining;
        bool questNoticeFound;
        bool bagIsFull, showingFull;
        float nextBagCheck, fullRemaining;
        readonly Action<string> playSound;
        string language;
        bool initialized;
        float stepClock;

        public GameplayRewardCounters(VisualElement hud, VisualElement notificationLayer, string language, Action<string> playSound)
        {
            this.playSound = playSound;
            root = FFView.RequireClass<VisualElement>(hud, "reward-counters");
            questNotice = root.Q<Label>(className: "reward-quest-notice");
            fusionMatter = new Counter(root, "reward-counter--fusion", FusionMatter);
            taros = new Counter(root, "reward-counter--taros", Taros);
            crates = new DropLane(root, "crate", "ui.gameplay.reward.crate");
            inventoryFull = root.Q(className: "reward-inventory-full");
            potions = new DropLane(root, "potion", "ui.gameplay.reward.potions");
            boosts = new DropLane(root, "boost", "ui.gameplay.reward.boosts");
            notificationLayer.Add(root);
            if (questNotice != null) notificationLayer.Add(questNotice);
            SetLanguage(language);
        }

        public void Remove()
        {
            questNotice?.RemoveFromHierarchy();
            root.RemoveFromHierarchy();
        }

        internal static string ArtAddress(string locale, string art) => "ui/" + locale + "/gameplay/rewards/" + art;

        public static async Task PreloadAsync(string language, CancellationToken cancellation)
        {
            async Task Load(string art)
            {
                if (language == "ru" && await ContentAssets.GetAsync<Texture2D>(ArtAddress("ru", art), cancellation)) return;
                await ContentAssets.GetAsync<Texture2D>(ArtAddress("en", art), cancellation);
            }
            await Task.WhenAll(Load(FusionMatter), Load(Taros));
        }

        public void SetLanguage(string language)
        {
            this.language = language;
            fusionMatter.SetLanguage(language);
            taros.SetLanguage(language);
        }

        public void Layout(float height)
        {
            if (!float.IsFinite(height) || height <= 0) return;
            float scale = RetrobutionLayout.Scale(height);
            root.style.scale = new Scale(new Vector3(scale, scale, 1));
        }

        public void Receive(GameplayUiState previous, GameplayUiState reward)
        {
            if (reward.IsTutorial) return;
            if (reward.Taros > previous.Taros) playSound?.Invoke("taros");
            if (reward.FusionMatter > previous.FusionMatter) playSound?.Invoke("fusionmatter");
            foreach (var item in reward.Inventory)
            {
                if (item.IsEmpty || item.ItemType != 9) continue;
                var old = previous.Inventory[item.Slot];
                if (old.ItemType == item.ItemType && old.ItemId == item.ItemId && old.Quantity >= item.Quantity) continue;
                if (GameplayUiCatalog.Load().Item(item.ItemType, item.ItemId, language)?.ChestKind == 0) crates.Enqueue(1);
            }
            potions.Enqueue(reward.NanoBattery - previous.NanoBattery);
            boosts.Enqueue(reward.WeaponBattery - previous.WeaponBattery);
            fusionMatter.Receive(previous.FusionMatter, reward.FusionMatter, initialized);
            taros.Receive(previous.Taros, reward.Taros, initialized);
            initialized = true;
        }

        public void SetInventoryFull(bool value) => bagIsFull = value;

        public void Reset()
        {
            questLoot.Clear();
            questNoticeRemaining = 0;
            questNotice?.RemoveFromClassList("reward-quest-notice--visible");
            bagIsFull = showingFull = false;
            nextBagCheck = fullRemaining = 0;
            inventoryFull?.RemoveFromClassList("reward-drop--visible");
            initialized = false;
            stepClock = 0;
            fusionMatter.Reset();
            taros.Reset();
            crates.Reset();
            potions.Reset();
            boosts.Reset();
        }

        public void QueueQuestLoot(int npc, int item, bool found) => questLoot.Enqueue((npc, item, found));

        void TickQuestLoot(float delta, bool visible)
        {
            if (questNotice == null) return;
            questNoticeRemaining = Mathf.Max(0, questNoticeRemaining - delta);
            if (questNoticeRemaining <= 0 && questLoot.Count > 0)
            {
                var (npc, item, found) = questLoot.Dequeue();
                string mob = LocalizationCatalog.Lookup("content.npc." + npc + ".name", language);
                string name = LocalizationCatalog.Lookup("content.quest_item." + item + ".name", language);
                string key = found ? "ui.gameplay.reward.quest_found" : "ui.gameplay.reward.quest_missing";
                questNotice.text = LocalizationCatalog.Lookup(key, language).Replace("{mob}", mob).Replace("{item}", name).ToUpperInvariant();
                questNoticeFound = found;
                questNotice.EnableInClassList("reward-quest-notice--found", found);
                questNotice.EnableInClassList("reward-quest-notice--missing", !found);
                questNoticeRemaining = 3;
            }
            questNotice.EnableInClassList("reward-quest-notice--visible", visible && questNoticeRemaining > 0);
            if (questNoticeRemaining > 0)
            {
                float start = questNotice.parent.resolvedStyle.height * .25f;
                float travel = Mathf.Clamp01(1 - questNoticeRemaining);
                questNotice.style.translate = new Translate(questNoticeFound ? 100 * travel : 0, (questNoticeFound ? 280 - start : 150) * travel);
                questNotice.style.opacity = Mathf.Min(1, questNoticeRemaining);
            }
        }

        public void Tick(float delta, bool visible)
        {
            TickQuestLoot(delta, visible);
            crates.Tick(delta, visible, language);
            fullRemaining = Mathf.Max(0, fullRemaining - delta);
            nextBagCheck += delta;
            if (!crates.Active && nextBagCheck > 1.5f)
            {
                nextBagCheck = 0;
                if (bagIsFull && !showingFull) fullRemaining = 2;
                showingFull = bagIsFull;
            }
            if (inventoryFull != null)
            {
                inventoryFull.EnableInClassList("reward-drop--visible", visible && showingFull && !crates.Active);
                if (showingFull) PositionDrop(inventoryFull, fullRemaining);
            }
            potions.Tick(delta, visible, language);
            boosts.Tick(delta, visible, language);
            if (!fusionMatter.Visible && !taros.Visible) return;
            stepClock += delta;
            if (stepClock > .1f)
            {
                bool fmMoved = fusionMatter.Step(delta);
                bool tarosMoved = taros.Step(delta);
                if (fmMoved || tarosMoved) stepClock = 0;
            }
            // FFOneClient preserves the original FM timer gate on both lanes.
            bool fade = fusionMatter.Remaining < 1;
            fusionMatter.Draw(visible, fade);
            taros.Draw(visible, fade);
        }

        static void PositionDrop(VisualElement panel, float remaining)
        {
            float fraction = Mathf.Max(remaining * .5f, 0);
            float bounce = Mathf.Abs(Mathf.Sin(Mathf.PI * (.5f + Mathf.Pow(2 - fraction, 3)))) * fraction * fraction;
            panel.style.translate = new Translate(0, (bounce - 1) * 170);
        }

        sealed class DropLane
        {
            readonly VisualElement panel;
            readonly Label label;
            readonly string textKey;
            readonly Queue<int> counts = new();
            float remaining;
            string drawnLanguage;
            public bool Active => counts.Count != 0;

            public DropLane(VisualElement root, string role, string textKey)
            {
                this.textKey = textKey;
                panel = root.Q(className: "reward-drop--" + role);
                label = panel?.Q<Label>(className: "reward-drop-text");
            }

            public void Enqueue(int count)
            {
                if (count <= 0 || label == null) return;
                if (counts.Count == 0) remaining = 2;
                counts.Enqueue(count);
            }

            public void Reset()
            {
                counts.Clear();
                drawnLanguage = null;
                panel?.RemoveFromClassList("reward-drop--visible");
            }

            public void Tick(float delta, bool visible, string language)
            {
                if (counts.Count == 0) return;
                remaining -= delta;
                if (remaining < -2)
                {
                    counts.Dequeue();
                    drawnLanguage = null;
                    if (counts.Count == 0) { panel.RemoveFromClassList("reward-drop--visible"); return; }
                    remaining = 2;
                }
                panel.EnableInClassList("reward-drop--visible", visible);
                if (!visible) return;
                if (drawnLanguage != language)
                {
                    label.text = LocalizationCatalog.Lookup(textKey, language).TrimEnd()
                        .Replace("{count}", counts.Peek().ToString(CultureInfo.InvariantCulture));
                    drawnLanguage = language;
                }
                PositionDrop(panel, remaining);
                panel.style.opacity = Mathf.Clamp01(2 + remaining);
            }
        }

        sealed class Counter
        {
            readonly VisualElement panel;
            readonly Image background;
            readonly System.Collections.Generic.List<Label> digits;
            readonly string art;
            int current, target, drawn = -1;

            public Counter(VisualElement parent, string role, string art)
            {
                this.art = art;
                panel = FFView.RequireClass<VisualElement>(parent, role);
                background = FFView.RequireClass<Image>(panel, "reward-counter-art");
                digits = panel.Query<Label>(className: "reward-counter-digit").ToList();
            }

            public float Remaining { get; private set; }
            public bool Visible => current != target || Remaining > 0;

            public void SetLanguage(string language)
            {
                var localized = language == "ru" ? FFWidgets.Texture(ArtAddress("ru", art)) : null;
                background.image = localized ? localized : FFWidgets.Texture(ArtAddress("en", art));
            }

            public void Receive(int previous, int value, bool initialized)
            {
                current = initialized ? Math.Min(current, previous) : previous;
                target = value;
                if (target > previous) Remaining = 2;
            }

            public bool Step(float delta)
            {
                if (current == target)
                {
                    Remaining = Mathf.Max(0, Remaining - delta);
                    return false;
                }
                long difference = (long)target - current;
                current += Math.Sign(difference) * (int)Math.Clamp(Math.Abs(difference) / 5, 1, 10);
                return true;
            }

            public void Draw(bool visible, bool fade)
            {
                panel.EnableInClassList("reward-counter--visible", visible && Visible);
                if (!visible || !Visible) return;
                panel.style.opacity = fade ? Mathf.Clamp01(Remaining) : 1;
                if (drawn == current) return;
                drawn = current;
                int divisor = 100_000_000;
                for (int i = 0; i < digits.Count; i++, divisor /= 10)
                    digits[i].text = (i == 0 ? current / divisor : current / divisor % 10).ToString(CultureInfo.InvariantCulture);
            }

            public void Reset()
            {
                current = target = 0;
                drawn = -1;
                Remaining = 0;
                panel.RemoveFromClassList("reward-counter--visible");
            }
        }
    }
}
