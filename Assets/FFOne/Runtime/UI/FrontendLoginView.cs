using System;
using UnityEngine.UIElements;
using static FFOne.UI.FFWidgets;

namespace FFOne.UI
{
    public sealed partial class FrontendController
    {
        Button loginLanguage;

        void BuildLogin()
        {
            loginRoot = FFView.InstantiateClass("ui/views/Login", "ff-login-screen");
            Root.Insert(0, loginRoot);
            loginBackground = FFView.RequireClass<VisualElement>(loginRoot, "ff-login-art");
            loginPanel = FFView.RequireClass<VisualElement>(loginRoot, "ff-login-panel");
            var userCaption = FFView.RequireClass<Label>(loginPanel, "ff-login-user-caption");
            var passwordCaption = FFView.RequireClass<Label>(loginPanel, "ff-login-password-caption");
            userCaption.text = FFStyle.Caption(T("ui.login.username", "USERNAME :"));
            passwordCaption.text = FFStyle.Caption(T("ui.login.password", "PASSWORD :"));
            AutoFit(userCaption);
            AutoFit(passwordCaption);
            username = FFView.RequireClass<TextField>(loginPanel, "ff-login-username");
            password = FFView.RequireClass<TextField>(loginPanel, "ff-login-password");
            BindTextEditing(username);
            BindTextEditing(password);
            // Older published Login templates used an unrecognized password attribute.
            password.isPasswordField = true;
            submit = BindLoginButton("ff-login-submit", T("ui.login.submit", "LOG IN"), Login);
            loginLanguage = BindLoginButton("ff-login-language", T("ui.login.language_value", "LANGUAGE: {language}")
                .Replace("{language}", language == "ru" ? "РУССКИЙ" : "ENGLISH"), SwitchLanguage);
            BindLoginButton("ff-login-registration", T("ui.login.register", "HOW TO REGISTER?"), Registration);
            BindLoginProfileViews();
            BuildAccountControls();
            BuildServerBrowser();
        }

        Button BindLoginButton(string role, string text, Action clicked)
        {
            var button = FFView.RequireClass<Button>(loginPanel, role);
            button.text = FFStyle.Caption(text); button.clicked += clicked;
            AutoFit(button);
            return button;
        }
    }
}
