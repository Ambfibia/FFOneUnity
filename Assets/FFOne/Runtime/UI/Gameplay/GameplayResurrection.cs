using System;
using System.Linq;
using System.Threading.Tasks;
using FFOne.Content;
using UnityEngine;
using UnityEngine.UIElements;
using static FFOne.UI.FFWidgets;

namespace FFOne.UI
{
    // ResurrectMode, sharedassets0.assets:1438. Use Item covers the Phoenix button in OnGUI.
    internal sealed class GameplayResurrection : IDisposable
    {
        public VisualElement View { get; }
        readonly VisualElement panel, phoenix;
        readonly Button item;
        readonly Label countdown;
        readonly string format;
        readonly string timeout;
        readonly Func<int, int, Task> request;
        readonly Action<string> error, sound;
        GameplayUiState state;
        int itemSlot = -1, displayedSeconds = -1;
        internal int SecondsRemaining => displayedSeconds;
        bool sent, automaticAttempted, disposed;
        float elapsed, requestTime;
        const float CountdownSeconds = 60;

        public GameplayResurrection(VisualElement parent, string language, Func<int, int, Task> request, Action<string> error, Action<string> sound)
        {
            this.request = request; this.error = error; this.sound = sound;
            string Copy(string key) => LocalizationCatalog.Lookup("ui.resurrect." + key, language);
            View = FFView.InstantiateClass("ui/views/GameplayPrompt", "ff-resurrection");
            panel = FFView.RequireClass<VisualElement>(View, "ff-resurrection-window");
            var title = FFView.RequireClass<Label>(panel, "ff-resurrection-title");
            title.text = FFStyle.Caption(Copy("title"));
            AutoFit(title);
            var question = FFView.RequireClass<Label>(panel, "ff-resurrection-question");
            question.text = Copy("question");
            AutoFit(question);
            format = Copy("countdown");
            timeout = language == "ru" ? "Сервер не подтвердил возрождение." : "The server did not confirm revival.";
            countdown = FFView.RequireClass<Label>(panel, "ff-resurrection-countdown");
            AutoFit(countdown);
            BindChoice(panel, "ff-resurrection-go", Copy("go"), 1);
            phoenix = FFView.RequireClass<VisualElement>(panel, "ff-resurrection-phoenix");
            BindChoice(phoenix, "ff-resurrection-alternative", Copy("revive"), 3);
            item = BindChoice(panel, "ff-resurrection-use-item", Copy("use_item"), 6);
            View.RegisterCallback<GeometryChangedEvent>(_ => {
                float height = View.resolvedStyle.height;
                if (height > 0)
                {
                    float scale = RetrobutionLayout.Scale(height);
                    panel.style.scale = new Scale(new Vector3(scale, scale, 1));
                }
            });
            parent.Add(View);
        }

        Button BindChoice(VisualElement root, string role, string caption, int type)
        {
            var choice = FFView.RequireClass<Button>(root, role);
            choice.text = FFStyle.Caption(caption);
            choice.clicked += () => _ = Send(type, true);
            AutoFit(choice);
            return choice;
        }

        public void Tick(GameplayUiState current, bool ready, bool modal, float delta)
        {
            if (disposed) return;
            if (state == null || !ReferenceEquals(state.Inventory, current.Inventory))
                itemSlot = current.Inventory.Where(i => !i.IsEmpty && i.ItemType == 7 && GameplayUiCatalog.Load().Item(7, i.ItemId)?.GeneralKind == 10)
                    .Select(i => i.Slot).DefaultIfEmpty(-1).Last();
            state = current;
            if (ready) elapsed = Mathf.Min(CountdownSeconds, elapsed + delta);
            int seconds = Mathf.CeilToInt(CountdownSeconds - elapsed);
            if (seconds != displayedSeconds) { displayedSeconds = seconds; countdown.text = format.Replace("{seconds}", seconds.ToString()); }
            phoenix.EnableInClassList("ff-resurrection-hidden", !state.HasBuff(5));
            item.EnableInClassList("ff-resurrection-hidden", itemSlot < 0);
            if (sent && Time.unscaledTime - requestTime > 10) { sent = false; error?.Invoke(timeout); }
            panel.SetEnabled(!sent && !modal && ready);
            if (ready && !sent && !automaticAttempted && elapsed >= CountdownSeconds)
            {
                automaticAttempted = true;
                _ = Send(1, false);
            }
        }

        async Task Send(int type, bool manual)
        {
            if (sent || disposed || state == null || state.Hp != 0 || (manual && !panel.enabledInHierarchy) ||
                (type == 3 && !state.HasBuff(5)) || (type == 6 && itemSlot < 0)) return;
            sent = true; requestTime = Time.unscaledTime; panel.SetEnabled(false);
            if (manual) sound?.Invoke("button");
            try { await request(type, itemSlot); }
            catch (Exception failure) { if (!disposed) { sent = false; error?.Invoke(failure.Message); } }
        }

        public void Dispose() { disposed = true; View.RemoveFromHierarchy(); }
    }
}
