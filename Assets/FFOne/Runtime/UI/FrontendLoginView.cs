using System;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;
using static FFOne.UI.FFWidgets;

namespace FFOne.UI
{
    public sealed partial class FrontendController
    {
        void BuildLogin()
        {
            var template = FFOne.Content.ContentAssets.Load<VisualTreeAsset>("ui/views/Login");
            if (template == null) throw new InvalidOperationException("Missing login UXML.");
            var tree = template.Instantiate();
            loginRoot = tree.Q<VisualElement>("login-screen");
            loginRoot.RemoveFromHierarchy(); Root.Add(loginRoot);
            loginBackground = loginRoot.Q<Image>("login-background");
            loginBackground.image = Texture("ui/en/launcher/login/login_screen_bg_16x10");
            loginBackground.scaleMode = ScaleMode.StretchToFill;
            loginPanel = loginRoot.Q<VisualElement>("login-panel");
            FFStyle.Background(loginPanel, "ui/en/launcher/login/login_screen_button_bg");
            foreach (var key in new[] { "username", "password" })
            {
                var label = loginPanel.Q<Label>(key + "-label");
                label.text = FFStyle.Caption(T("ui.login." + key, key == "username" ? "USERNAME :" : "PASSWORD :"));
                Font(label); AutoFit(label);
                var field = loginPanel.Q<TextField>(key);
                Font(field, true); FFStyle.Background(field, "ui/en/launcher/login/ff-textfield-normal");
            }
            username = loginPanel.Q<TextField>("username"); password = loginPanel.Q<TextField>("password"); password.maskChar = '*';
            submit = BindLoginButton("login-submit", T("ui.login.submit", "LOG IN"), Login);
            BindLoginButton("login-language", T("ui.login.language_value", "LANGUAGE: {language}")
                .Replace("{language}", language == "ru" ? "РУССКИЙ" : "ENGLISH"), SwitchLanguage);
            BindLoginButton("registration-help", T("ui.login.register", "HOW TO REGISTER?"), Registration);
            BuildAccountControls();
            BuildServerBrowser();
        }

        Button BindLoginButton(string name, string text, Action clicked)
        {
            var button = loginPanel.Q<Button>(name);
            button.text = FFStyle.Caption(text); button.clicked += clicked;
            Font(button); AutoFit(button);
            FFStyle.Button(button, "ui/en/server-selection/ff-button-normal", "ui/en/server-selection/ff-button-hover", "ui/en/server-selection/ff-button-active");
            return button;
        }

        Button LoginButton(VisualElement parent, string name, string text, float x, float y, float width, float height, Action action)
        {
            var button = Button(parent, name, text, x, y, width, height,
                "ui/en/server-selection/ff-button-normal", "ui/en/server-selection/ff-button-hover", action, 14);
            Padding(button, 8, 8, 4, 4);
            FFStyle.Button(button, "ui/en/server-selection/ff-button-normal", "ui/en/server-selection/ff-button-hover", "ui/en/server-selection/ff-button-active");
            return button;
        }

    }
}
