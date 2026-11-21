using System;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UIElements;

namespace FFOne.UI
{
    /// <summary>Runtime presentation of cnSystemMessageManager's shared confirmation dialog.</summary>
    public sealed class GameplayPrompt : IDisposable
    {
        public VisualElement View { get; }
        public event Action Closed;
        bool disposed;
        bool running;
        Action cancelAction;
        public GameplayPrompt(VisualElement parent, string title, string body, bool russian, string initialValue,
            Func<string, Task> confirm, Func<Task> cancel, Action<string> error, Action<string> sound = null,
            string icon = "ui/en/gameplay/journal/messicon_warning", bool deleteMission = false, bool deleteItem = false, int quantity = 0)
        {
            View = FFView.InstantiateClass("ui/views/GameplayPrompt", "gameplay-prompt");
            View.EnableInClassList("gameplay-prompt--mission", deleteMission);
            View.EnableInClassList("gameplay-prompt--destructive", deleteMission || deleteItem);
            View.EnableInClassList("gameplay-prompt--input", initialValue != null);
            var panel = FFView.RequireClass<VisualElement>(View, "gameplay-prompt-panel");
            View.RegisterCallback<GeometryChangedEvent>(_ => {
                float w = View.resolvedStyle.width, h = View.resolvedStyle.height;
                if (w > 0 && h > 0) FFWidgets.ScaledRect(panel, new Rect(((int)w - 600) / 2, ((int)h - 164) / 2, 600, 164),
                    Mathf.Min(RetrobutionLayout.Scale(h), w / 600f), new Vector2((int)w / 2, (int)h / 2));
            });
            FFView.RequireClass<Label>(View, "gameplay-prompt-title").text = title;
            FFView.RequireClass<Label>(View, "gameplay-prompt-body").text = body;
            FFView.RequireClass<Image>(View, "gameplay-prompt-icon").image = FFWidgets.Texture(icon);
            if (deleteItem && quantity > 1)
                FFView.RequireClass<Label>(View, "gameplay-prompt-stack").text = quantity.ToString();
            var value = FFView.RequireClass<TextField>(View, "gameplay-prompt-value");
            if (initialValue != null) value.value = initialValue;
            var yes = FFView.RequireClass<Button>(View, "gameplay-prompt-confirm");
            var no = FFView.RequireClass<Button>(View, "gameplay-prompt-cancel");
            yes.text = deleteMission ? (russian ? "УДАЛИТЬ ЗАДАНИЕ" : "DELETE MISSION") :
                deleteItem ? (russian ? "УДАЛИТЬ" : "DELETE") : (russian ? "ОК" : "OKAY");
            no.text = russian ? "ОТМЕНА" : "CANCEL";
            async Task Run(Func<Task> action)
            {
                if (running || disposed) return;
                running = true;
                yes.SetEnabled(false); no.SetEnabled(false);
                try { if (action != null) await action(); Dispose(); }
                catch (Exception failure)
                {
                    if (disposed) return;
                    running = false; error?.Invoke(failure.Message); yes.SetEnabled(true); no.SetEnabled(true);
                }
            }
            yes.clicked += () => _ = Run(() => { sound?.Invoke(deleteMission ? "abandon_mission" : "yes_button"); return confirm(value.value); });
            cancelAction = () => _ = Run(cancel);
            no.clicked += () => { sound?.Invoke("no_button"); cancelAction(); };
            parent.Add(View);
            if (initialValue != null) value.Focus(); else no.Focus();
        }
        internal void EnableQuantityInput(int maximum)
        {
            var field = FFView.RequireClass<TextField>(View, "gameplay-prompt-value");
            field.AddToClassList("ff-pad-quantity");
            field.userData = maximum;
        }

        public void Cancel() => cancelAction?.Invoke();
        public void Dispose()
        {
            if (disposed) return;
            disposed = true; View.RemoveFromHierarchy(); Closed?.Invoke(); Closed = null;
        }
    }
}
