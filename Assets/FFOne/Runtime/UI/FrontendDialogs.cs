using System;
using UnityEngine;
using UnityEngine.UIElements;
using static FFOne.UI.FFWidgets;

namespace FFOne.UI
{
    public sealed partial class FrontendController
    {
        VisualElement frontendDialogPanel;

        void CloseModal()
        {
            modal?.RemoveFromHierarchy();
            modal = null;
            frontendDialogPanel = null;
        }

        VisualElement OpenFrontendDialog(string role)
        {
            CloseModal();
            var view = frontendDialogsAsset.Instantiate();
            modal = FFView.RequireClass<VisualElement>(view, "ff-frontend-dialog");
            for (int i = 0; i < view.styleSheets.count; i++)
                modal.styleSheets.Add(view.styleSheets[i]);
            modal.AddToClassList(role);
            frontendDialogPanel = FFView.RequireClass<VisualElement>(modal, "ff-frontend-dialog-panel");
            frontendDialogPanel.RegisterCallback<GeometryChangedEvent>(_ =>
                LayoutFrontendDialog(Root.resolvedStyle.width, Root.resolvedStyle.height));
            UiLayers.For(Root).Add(modal, UiLayer.Dialog);
            LayoutFrontendDialog(Root.resolvedStyle.width, Root.resolvedStyle.height);
            return frontendDialogPanel;
        }

        void LayoutFrontendDialog(float width, float height)
        {
            if (frontendDialogPanel == null || !(width > 0 && height > 0)) return;
            float panelWidth = frontendDialogPanel.resolvedStyle.width, panelHeight = frontendDialogPanel.resolvedStyle.height;
            if (!(panelWidth > 0 && panelHeight > 0)) return;
            float scale = Mathf.Min(FrontendLayout.CharacterScale(width, height),
                Mathf.Max(1, width - 24) / panelWidth, Mathf.Max(1, height - 24) / panelHeight);
            frontendDialogPanel.style.left = (width - panelWidth * scale) / 2;
            frontendDialogPanel.style.top = (height - panelHeight * scale) / 2;
            frontendDialogPanel.style.scale = new Scale(new Vector3(scale, scale, 1));
        }

        void Registration()
        {
            var panel = OpenFrontendDialog("ff-frontend-dialog--registration");
            var body = FFView.RequireClass<Label>(panel, "ff-frontend-registration-text");
            body.text = language == "ru"
                ? "Учётную запись для выбранного сервера создайте на его сайте. Если сервер разрешает регистрацию при входе, введите новое имя пользователя и пароль."
                : "Create your account on the selected server's website. If the server allows registration at login, enter a new username and password.";
            var close = FFView.RequireClass<Button>(panel, "ff-frontend-registration-close");
            close.text = FFStyle.Caption(T("ui.common.close", "CLOSE"));
            close.clicked += CloseModal;
            AutoFit(close);
            close.Focus();
        }

        void DeleteDialog()
        {
            if (busy || SelectedCharacter == null) return;
            var character = SelectedCharacter;
            var panel = OpenFrontendDialog("ff-frontend-dialog--delete");
            var title = FFView.RequireClass<Label>(panel, "ff-frontend-delete-title");
            title.text = FFStyle.Caption(T("ui.character_select.delete", "DELETE CHARACTER"));
            AutoFit(title);
            var prompt = FFView.RequireClass<Label>(panel, "ff-frontend-delete-prompt");
            prompt.text = T("ui.character_select.delete_prompt", "Please enter your character's first name to delete");
            var confirmation = FFView.RequireClass<TextField>(panel, "ff-frontend-delete-name");
            var cancel = FFView.RequireClass<Button>(panel, "ff-frontend-delete-cancel");
            cancel.text = FFStyle.Caption(T("ui.common.cancel", "CANCEL"));
            AutoFit(cancel);
            cancel.clicked += () => { PlayUiSound("no_button"); CloseModal(); };
            var confirm = FFView.RequireClass<Button>(panel, "ff-frontend-delete-confirm");
            confirm.text = FFStyle.Caption(T("ui.common.delete", "DELETE"));
            AutoFit(confirm);
            confirm.clicked += async () => {
                if (!MatchesFirstName(confirmation.value)) return;
                PlayUiSound("yes_button");
                CloseModal();
                await RunOperation(async () => {
                    await Session.DeleteCharacterAsync(character.Uid, lifetime.Token);
                    RefreshRoster();
                    SetStatus("");
                }, language == "ru" ? "УДАЛЕНИЕ..." : "DELETING...");
            };
            confirm.SetEnabled(false);
            confirmation.RegisterValueChangedCallback(e => confirm.SetEnabled(MatchesFirstName(e.newValue)));
            confirmation.Focus();

            bool MatchesFirstName(string candidate) => string.Equals(candidate.Trim(), character.FirstName, StringComparison.Ordinal);
        }
    }
}
