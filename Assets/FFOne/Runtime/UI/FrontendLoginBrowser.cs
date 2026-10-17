using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FFOne.Content;
using FFOne.Network;
using UnityEngine;
using UnityEngine.UIElements;
using static FFOne.UI.FFWidgets;

namespace FFOne.UI
{
    public sealed partial class FrontendController
    {
        const string LoginFieldArt = "ui/en/launcher/login/ff-textfield-normal";
        LoginProfiles profiles;
        readonly Dictionary<string, ServerHealth> serverHealth = new Dictionary<string, ServerHealth>();
        VisualElement browserPanel, accountControls;
        TextField serverInput;
        string savedAccount, returnAccount, browserMessage = "", serverInputValue = "";
        bool rememberAccount = true, addingServer;
        int serverPage, healthRevision;
        float nextServerProbe;

        void InitializeProfiles()
        {
            profiles = new LoginProfiles(Integration.DevelopmentConnection.ProfilesPath(Application.persistentDataPath), new WindowsLoginSecrets());
            Integration.DevelopmentConnection.SelectServer(profiles);
            browserMessage = profiles.LoadError ?? "";
            savedAccount = profiles.CurrentAccounts.FirstOrDefault()?.Username;
        }
        void SelectServer(int index)
        {
            if (busy) return;
            try { profiles.Select(index); browserMessage = ""; }
            catch (Exception error) { browserMessage = error.Message; }
            savedAccount = profiles.CurrentAccounts.FirstOrDefault()?.Username;
            returnAccount = null; username.value = savedAccount ?? ""; password.value = "";
            SetStatus(""); BuildAccountControls(); BuildServerBrowser();
        }
        void BuildAccountControls()
        {
            accountControls?.RemoveFromHierarchy();
            accountControls = Group(loginPanel, "account-controls"); Rect(accountControls, 45, 15, 280, 138);
            var accounts = profiles.CurrentAccounts.ToArray();
            bool saved = savedAccount != null;
            foreach (var name in new[] { "username-label", "password-label", "username", "password" })
                loginPanel.Q<VisualElement>(name).style.display = saved ? DisplayStyle.None : DisplayStyle.Flex;
            username.style.width = accounts.Length == 0 ? 280 : 249;
            if (saved)
            {
                username.value = savedAccount; password.value = "";
                Label(accountControls, "account-caption", T("ui.login.account_caption", "ACCOUNT"), 0, 0, 200, 22, false, 14, false).style.color = Color.white;
                ProfileButton(accountControls, "new-account", "+", 255, 25, 25, 25, () => EditAccount(null));
                var area = Group(accountControls, "saved-account-frame"); Rect(area, 0, 25, 249, 85);
                FFStyle.Background(area, LoginFieldArt);
                var scroll = new ScrollView(ScrollViewMode.Vertical) { name = "saved-accounts" };
                Rect(scroll, 3, 3, 222, 79); scroll.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
                scroll.verticalScrollerVisibility = ScrollerVisibility.Hidden; area.Add(scroll);
                foreach (var account in accounts)
                {
                    string name = account.Username;
                    var row = Group(scroll.contentContainer, "account-row");
                    row.style.height = 25; row.style.flexShrink = 0; row.style.marginBottom = 3;
                    var select = ProfileButton(row, "saved-account-" + name, name, 0, 0, 193, 25, () => {
                        savedAccount = name; username.value = name; password.value = ""; SetStatus(""); BuildAccountControls(); });
                    if (name == savedAccount) Skin(select, "ui/en/server-selection/ff-button-active", "ui/en/server-selection/ff-button-active");
                    var edit = ProfileButton(row, "edit-account-" + name, "", 197, 0, 25, 25, () => EditAccount(name));
                    LoginIcon(edit, "pencil");
                }
                AccountScrollbar(area, scroll, accounts.Length);
            }
            else
            {
                if (accounts.Length != 0)
                {
                    var back = ProfileButton(accountControls, "back-to-accounts", "", 255, 25, 25, 25, () => {
                        savedAccount = returnAccount ?? profiles.CurrentAccounts.FirstOrDefault()?.Username;
                        username.value = savedAccount ?? ""; password.value = ""; SetStatus(""); BuildAccountControls(); });
                    LoginIcon(back, "back");
                }
                var remember = Button(accountControls, "remember-account", "", 152, 118, 128, 20, null, null, () => {
                    rememberAccount = !rememberAccount; BuildAccountControls(); });
                Image(remember, "remember-check", "ui/en/option/" + (rememberAccount ? "radio-checked" : "radio-empty"), 0, 1, 17, 17);
                Label(remember, "remember-caption", T("ui.login.remember_caption", "Remember"), 22, 0, 106, 20, false, 14, false).style.color = Color.white;
            }
            accountControls.SetEnabled(!busy);
        }
        static void AccountScrollbar(VisualElement parent, ScrollView scroll, int count)
        {
            float maximum = Mathf.Max(0, count * 28 - 3 - 79);
            if (maximum <= 0) return;
            var track = new VisualElement { name = "account-scroll-track" }; parent.Add(track); Rect(track, 232, 3, 14, 79);
            FFStyle.Background(track, "ui/en/user-equip/scroll-track");
            var thumb = Group(track, "account-scroll-thumb"); Rect(thumb, 0, 0, 14, 20);
            FFStyle.Background(thumb, "ui/en/user-equip/scroll-thumb");
            scroll.verticalScroller.valueChanged += value => thumb.style.top = Mathf.Clamp01(value / maximum) * 59;
            bool dragging = false;
            void Drag(float screenY)
            {
                float local = (screenY - track.worldBound.y) * 79 / track.worldBound.height;
                scroll.scrollOffset = new Vector2(0, Mathf.Clamp01((local - 10) / 59) * maximum);
            }
            track.RegisterCallback<PointerDownEvent>(e => { if (e.button != 0) return; dragging = true; track.CapturePointer(e.pointerId); Drag(e.position.y); });
            track.RegisterCallback<PointerMoveEvent>(e => { if (dragging) Drag(e.position.y); });
            track.RegisterCallback<PointerUpEvent>(e => { dragging = false; track.ReleasePointer(e.pointerId); });
            track.RegisterCallback<PointerCaptureOutEvent>(_ => dragging = false);
        }
        void EditAccount(string name)
        {
            if (busy) return;
            returnAccount = savedAccount; savedAccount = null;
            username.value = name ?? ""; password.value = ""; rememberAccount = true;
            SetStatus(""); BuildAccountControls();
            if (name == null) username.Focus(); else password.Focus();
        }
        Button ProfileButton(VisualElement parent, string name, string title, float x, float y, float w, float h, Action action)
        {
            var button = Button(parent, name, title, x, y, w, h, LoginFieldArt, "ui/en/server-selection/ff-button-normal", () => { if (!busy) action(); }, 14);
            button.style.paddingLeft = button.style.paddingRight = 5;
            button.style.color = Color.white;
            return button;
        }
        void BuildServerBrowser()
        {
            browserPanel?.RemoveFromHierarchy();
            browserPanel = Group(loginRoot, "server-browser");
            FFStyle.Background(browserPanel, "ui/en/launcher/login/login_screen_button_bg");
            Label(browserPanel, "server-browser-title", T(addingServer ? "ui.login.add_server" : "ui.login.servers", addingServer ? "Add server" : "SERVERS"), 14, 14, 170, 26, false, 14, false).style.color = new Color(.7f, .78f, .82f);
            if (!addingServer)
            {
                var refresh = ProfileButton(browserPanel, "refresh-servers", "", 200, 12, 30, 30, RefreshServerHealth);
                LoginIcon(refresh, "refresh");
            }
            ProfileButton(browserPanel, "add-server-page", addingServer ? "<" : "+", 236, 12, 30, 30, () => {
                addingServer = !addingServer; BuildServerBrowser(); if (addingServer) serverInput.Focus(); });
            if (addingServer)
            {
                serverInput = Field(browserPanel, "server-address", 14, 46, 252, 25);
                serverInput.maxLength = 240; serverInput.value = serverInputValue;
                serverInput.RegisterValueChangedCallback(e => serverInputValue = e.newValue);
                serverInput.RegisterCallback<KeyDownEvent>(e => { if (e.keyCode == KeyCode.Return) { AddServer(); e.StopPropagation(); } });
                LoginButton(browserPanel, "add-server", T("ui.login.add_server", "Add server"), 14, 77, 252, 35, AddServer);
            }
            else
            {
                int row = 0;
                for (int i = serverPage * 3; i < Math.Min(profiles.Servers.Count, serverPage * 3 + 3); i++, row++)
                {
                    int index = i; var server = profiles.Servers[i];
                    var button = ProfileButton(browserPanel, "server-" + i, "", 14, 46 + row * 42, 252, 36, () => SelectServer(index));
                    var health = serverHealth.TryGetValue(server.Key, out var value) ? value : new ServerHealth();
                    var indicator = Group(button, "server-health"); Rect(indicator, 10, 10, 5, 16);
                    indicator.style.backgroundColor = health.Online == true ? new Color(.7f, 1, .05f) : health.Online == false ? new Color(1, .25f, .42f) : new Color(1, .78f, 0);
                    Label(button, "server-name", server.Name, 25, 0, 170, 36, false, 14, false).style.color = i == profiles.Selected ? Color.white : new Color(.7f, .78f, .82f);
                    if (health.PlayerCount.HasValue) Label(button, "server-count", health.PlayerCount.Value.ToString(), 206, 0, 36, 36, false, 14).style.color = new Color(.7f, 1, .05f);
                }
                if (profiles.Servers.Count > 3)
                    ProfileButton(browserPanel, "more-servers", T("ui.login.more_servers", "More servers"), 14, 46 + row * 42, 252, 30,
                        () => { serverPage = (serverPage + 1) % ((profiles.Servers.Count + 2) / 3); BuildServerBrowser(); });
            }
            if (browserMessage.Length != 0)
            {
                var error = Label(browserPanel, "profile-error", T("ui.login.profile_error", "Account settings: {error}").Replace("{error}", browserMessage),
                    14, BrowserHeight() - 44, 252, 40, false, 12, false);
                error.style.whiteSpace = WhiteSpace.Normal; error.style.color = new Color(1, .6f, .5f);
            }
            browserPanel.SetEnabled(!busy); Layout();
        }
        float BrowserHeight() => 28 + (addingServer ? 3 : 1 + Math.Min(profiles.Servers.Count, 3) + (profiles.Servers.Count > 3 ? 1 : 0)) * 36 + (browserMessage.Length == 0 ? 0 : 44);
        void AddServer()
        {
            if (busy) return;
            try
            {
                profiles.Add(serverInputValue); serverPage = profiles.Selected / 3;
                addingServer = false; serverInputValue = ""; browserMessage = "";
                SelectServer(profiles.Selected); RefreshServerHealth();
            }
            catch (Exception error) { browserMessage = error.Message; BuildServerBrowser(); }
        }
        void RefreshServerHealth()
        {
            nextServerProbe = Time.unscaledTime + 20;
            int revision = ++healthRevision;
            foreach (var server in profiles.Servers)
            { serverHealth[server.Key] = new ServerHealth(); ProbeServer(server, revision); }
            if (!addingServer) BuildServerBrowser();
        }
        async void ProbeServer(LoginServer server, int revision)
        {
            try
            {
                var health = await server.ProbeAsync(lifetime.Token);
                if (this == null || lifetime.IsCancellationRequested || revision != healthRevision) return;
                serverHealth[server.Key] = health;
                if (!addingServer) BuildServerBrowser();
            }
            catch (OperationCanceledException) { }
            catch (ObjectDisposedException) { }
        }
        static void LoginIcon(Button parent, string icon)
        {
            var root = Group(parent, "icon-" + icon); Rect(root, (parent.style.width.value.value - 18) / 2, (parent.style.height.value.value - 18) / 2, 18, 18);
            void Stroke(float x, float y, float width, float height, float degrees)
            {
                var line = Group(root, "stroke"); Rect(line, x, y, width, height);
                line.style.backgroundColor = Color.white; line.style.rotate = new Rotate(degrees);
            }
            if (icon == "pencil") { Stroke(4, 7, 12, 4, -45); Stroke(2.5f, 13, 3, 2, -45); }
            else if (icon == "back") { Stroke(4, 8, 11, 2, 0); Stroke(2, 5.5f, 7, 2, -45); Stroke(2, 10.5f, 7, 2, 45); }
            else
            {
                for (int i = 0; i < 9; i++) { float degrees = i * 30 + 15; float angle = degrees * Mathf.Deg2Rad; Stroke(7.5f + 6 * Mathf.Cos(angle), 8 + 6 * Mathf.Sin(angle), 3.8f, 2, degrees + 90); }
                Stroke(12, 3, 5, 2, 45); Stroke(14, 4, 2, 5, 0);
            }
        }
    }
}
