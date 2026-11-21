using System;
using System.Threading;
using System.Threading.Tasks;
using FFOne.Integration;
using UnityEngine;
using UnityEngine.UIElements;

namespace FFOne.UI
{
    public sealed partial class FrontendController
    {
        bool selectionPreparing;
        int selectionPresentationVersion;
        CancellationTokenSource selectionLoading;
        [SerializeField] PreviewStage rosterPreview;
        public bool SelectionReady => showingSelection && !selectionPreparing && rosterPreview && rosterPreview.SelectionReady;

        internal void BindRosterPreview(PreviewStage stage) => rosterPreview = stage;

        void RefreshSelectionPreviewStatus()
        {
            if (!rosterPreview) return;
            bool pending = SelectedCharacter != null && !rosterPreview.SelectedPreviewReady;
            avatarGroup.EnableInClassList("selection-preview-pending", pending);
            if (!pending) return;
            avatarStatus.text = rosterPreview.SelectedPreviewFailed
                ? (language == "ru" ? "Предпросмотр недоступен" : "Character preview unavailable")
                : (language == "ru" ? "Загрузка персонажа..." : "Loading character preview...");
        }

        public async Task ShowSelectionAsync()
        {
            selectionLoading?.Cancel(); selectionLoading?.Dispose();
            selectionLoading = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
            var cancellation = selectionLoading.Token;
            int version = ++selectionPresentationVersion;
            selectionPreparing = true; SetBusy(true);
            audio?.SetMode(RetrobutionAudioController.Mode.Lobby);
            try
            {
                await ShowLoadingAsync(FrontendLoadingStage.CharacterSelection);
                cancellation.ThrowIfCancellationRequested();
                showingSelection = true;
                loginRoot.style.display = DisplayStyle.None;
                // Lay out the real controls and render previews behind the loader.
                selectionRoot.style.display = DisplayStyle.Flex;
                CloseModal(); SetStatus(""); RefreshRoster(); Layout();
                var previews = rosterPreview;
                if (!previews) throw new InvalidOperationException("Character selection preview is not configured.");
                float started = Time.realtimeSinceStartup;
                while (!previews.SelectionReady)
                {
                    cancellation.ThrowIfCancellationRequested();
                    if (!string.IsNullOrEmpty(previews.SelectionError)) throw new InvalidOperationException(previews.SelectionError);
                    if (Time.realtimeSinceStartup - started > 120) throw new TimeoutException("Character selection did not render its avatars.");
                    float progress = previews.SelectionResourceProgress;
                    SetLoadingProgress(progress, progress);
                    SetLoadingResourceProgressVisible(progress < 1);
                    await Task.Yield();
                }
                SetLoadingProgress(1, 1);
                RefreshSelectionPreviewStatus();
            }
            finally
            {
                if (this && version == selectionPresentationVersion)
                { selectionPreparing = false; HideLoading(); SetBusy(false); }
            }
        }
    }
}
