using System;
using System.Threading.Tasks;
using UnityEngine.UIElements;

namespace FFOne.UI
{
    /// <summary>Shared native consent/amount dialog, kept separate from imported source screen geometry.</summary>
    public sealed class GameplayPrompt : IDisposable
    {
        public VisualElement View { get; }
        public event Action Closed;
        bool disposed;
        bool running;
        Action cancelAction;
        public GameplayPrompt(VisualElement parent, string title, string body, bool russian, string initialValue,
            Func<string, Task> confirm, Func<Task> cancel, Action<string> error)
        {
            View = FFView.Instantiate("ui/views/GameplayPrompt", "gameplay-prompt");
            FFView.Require<Label>(View, "prompt-title").text = title;
            FFView.Require<Label>(View, "prompt-body").text = body;
            var value = FFView.Require<TextField>(View, "prompt-value");
            if (initialValue != null) { value.style.display = DisplayStyle.Flex; value.value = initialValue; value.maxLength = 10; }
            var yes = FFView.Require<Button>(View, "prompt-confirm");
            var no = FFView.Require<Button>(View, "prompt-cancel");
            View.Query<TextElement>().ForEach(text => FFWidgets.Font(text, false, 14));
            yes.text = russian ? "ПРИНЯТЬ" : "ACCEPT"; no.text = russian ? "ОТМЕНА" : "CANCEL";
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
            yes.clicked += () => _ = Run(() => confirm(value.value));
            cancelAction = () => _ = Run(cancel);
            no.clicked += cancelAction;
            parent.Add(View);
            if (initialValue != null) value.Focus(); else no.Focus();
        }
        public void Cancel() => cancelAction?.Invoke();
        public void Dispose()
        {
            if (disposed) return;
            disposed = true; View.RemoveFromHierarchy(); Closed?.Invoke(); Closed = null;
        }
    }
}
