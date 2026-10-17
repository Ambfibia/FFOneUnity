using System;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;
using static FFOne.UI.FFWidgets;

namespace FFOne.UI
{
    public sealed partial class FrontendController
    {
        void Registration()
        {
            var panel = ModalPanel(510, 245);
            string fallback = language == "ru" ? "Учётную запись сервера с API нужно создать на сайте выбранного сервера. Локальный RustyFusion с разрешённым входом по паролю может создавать учётные записи при первом входе." :
                "For an API server, create your account on that server's website. A local RustyFusion server configured for password login can create accounts on first login.";
            var body = Label(panel, "registration-text", fallback, 30, 30, 450, 145, true, 16);
            body.style.whiteSpace = WhiteSpace.Normal;
            LoginButton(panel, "close-registration", T("ui.common.close", "CLOSE"), 165, 190, 180, 30, CloseModal);
        }

        void DeleteDialog()
        {
            if (busy || SelectedCharacter == null) return;
            var character = SelectedCharacter;
            var panel = ModalPanel(526, 164, Controls + "CSDeleteWindow");
            Label(panel, "delete-title", T("ui.character_select.delete", "DELETE CHARACTER"), 100, 12, 326, 22, false, 12);
            var prompt = Label(panel, "delete-prompt", T("ui.character_select.delete_prompt", "Please enter your character's first name to delete"), 45, 34, 436, 30, true, 12);
            prompt.style.whiteSpace = WhiteSpace.Normal;
            var confirmation = Field(panel, "delete-first-name", 75, 73, 378, 24);
            Font(confirmation, true, 12);
            confirmation.maxLength = 8;
            var input = confirmation.Q<VisualElement>("unity-text-input");
            if (input != null) input.style.unityTextAlign = TextAnchor.MiddleCenter;
            Button(panel, "cancel-delete", T("ui.common.cancel", "CANCEL"), 40, 125, 152, 27,
                Controls + "CancelNormal", Controls + "CancelNormal", CloseModal, 12);
            var confirm = Button(panel, "confirm-delete", T("ui.common.delete", "DELETE"), 335, 125, 152, 27,
                Controls + "red_button_normal", Controls + "red_button_over", async () => {
                    if (!string.Equals(confirmation.value.Trim(), character.FirstName, StringComparison.Ordinal)) return;
                    CloseModal();
                    await RunOperation(async () => {
                        await Session.DeleteCharacterAsync(character.Uid, lifetime.Token);
                        RefreshRoster(); SetStatus("");
                    }, language == "ru" ? "УДАЛЕНИЕ..." : "DELETING...");
                });
            confirm.SetEnabled(false);
            confirmation.RegisterValueChangedCallback(e => confirm.SetEnabled(string.Equals(e.newValue.Trim(), character.FirstName, StringComparison.Ordinal)));
            confirmation.Focus();
        }
    }
}
