using System;
using System.Collections.Generic;
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
        const int ServersPerPage = 3;
        const float OfflineServerRetrySeconds = 1;
        LoginProfiles profiles;
        readonly Dictionary<string, ServerHealth> serverHealth = new();
        readonly Dictionary<string, int> serverProbes = new();
        readonly Dictionary<int, (Button Button, VisualElement Health, Label Count)> visibleServers = new();
        VisualElement browserPanel, accountControls, serverRows;
        VisualElement accountScrollTrack, accountScrollThumb;
        ScrollView accountScroll;
        Label accountCaption, rememberCaption, browserTitle, browserError;
        Button moreServers, serverPageToggle;
        TextField serverInput;
        VisualTreeAsset accountRowAsset, serverRowAsset, frontendDialogsAsset;
        string savedAccount, returnAccount, browserMessage = "", serverInputValue = "";
        bool rememberAccount = true, addingServer, draggingAccountScroll;
        int serverPage, healthRevision;
        float nextServerProbe, nextOfflineServerProbe, accountScrollMaximum;

        void InitializeProfiles()
        {
            profiles = new LoginProfiles(Integration.DevelopmentConnection.ProfilesPath(Application.persistentDataPath), new WindowsLoginSecrets());
            Integration.DevelopmentConnection.SelectServer(profiles);
            browserMessage = profiles.LoadError ?? "";
            savedAccount = profiles.CurrentAccounts.FirstOrDefault()?.Username;
        }

        void BindLoginProfileViews()
        {
            accountRowAsset = TakeTemplate("ff-account-row-template");
            serverRowAsset = TakeTemplate("ff-server-row-template");
            frontendDialogsAsset = TakeTemplate("ff-frontend-dialog-template");

            accountControls = FFView.RequireClass<VisualElement>(loginPanel, "ff-account-controls");
            accountCaption = FFView.RequireClass<Label>(accountControls, "ff-account-caption");
            rememberCaption = FFView.RequireClass<Label>(accountControls, "ff-remember-caption");
            accountCaption.text = FFStyle.Caption(T("ui.login.account_caption", "ACCOUNT"));
            rememberCaption.text = FFStyle.Caption(T("ui.login.remember_caption", "Remember"));
            AutoFit(accountCaption);
            AutoFit(rememberCaption);
            FFView.RequireClass<Button>(accountControls, "ff-account-new").clicked += () => EditAccount(null);
            FFView.RequireClass<Button>(accountControls, "ff-account-back").clicked += BackToAccounts;
            FFView.RequireClass<Button>(accountControls, "ff-remember-account").clicked += () => {
                rememberAccount = !rememberAccount;
                BuildAccountControls();
            };
            accountScroll = FFView.RequireClass<ScrollView>(accountControls, "ff-account-scroll");
            accountScrollTrack = FFView.RequireClass<VisualElement>(accountControls, "ff-account-scroll-track");
            accountScrollThumb = FFView.RequireClass<VisualElement>(accountScrollTrack, "ff-account-scroll-thumb");
            BindAccountScrollbar();

            browserPanel = FFView.RequireClass<VisualElement>(loginRoot, "ff-server-browser");
            serverRows = FFView.RequireClass<VisualElement>(browserPanel, "ff-server-rows");
            browserTitle = FFView.RequireClass<Label>(browserPanel, "ff-server-browser-title");
            browserError = FFView.RequireClass<Label>(browserPanel, "ff-server-error");
            serverPageToggle = FFView.RequireClass<Button>(browserPanel, "ff-server-page-toggle");
            moreServers = FFView.RequireClass<Button>(browserPanel, "ff-server-more");
            var add = FFView.RequireClass<Button>(browserPanel, "ff-server-add");
            add.text = FFStyle.Caption(T("ui.login.add_server", "Add server"));
            moreServers.text = FFStyle.Caption(T("ui.login.more_servers", "More servers"));
            AutoFit(browserTitle);
            AutoFit(moreServers);
            AutoFit(add);
            add.clicked += AddServer;
            moreServers.clicked += AdvanceServerPage;
            serverPageToggle.clicked += ToggleServerPage;
            FFView.RequireClass<Button>(browserPanel, "ff-server-refresh").clicked += RefreshServerHealth;
            serverInput = FFView.RequireClass<TextField>(browserPanel, "ff-server-address");
            serverInput.SetValueWithoutNotify(serverInputValue);
            serverInput.RegisterValueChangedCallback(e => serverInputValue = e.newValue);
            serverInput.RegisterCallback<KeyDownEvent>(e => {
                if (e.keyCode != KeyCode.Return && e.keyCode != KeyCode.KeypadEnter) return;
                AddServer();
                e.StopPropagation();
            }, TrickleDown.TrickleDown);
            visibleServers.Clear();
        }

        VisualTreeAsset TakeTemplate(string role)
        {
            var template = FFView.RequireClass<TemplateContainer>(loginRoot, role);
            var asset = template.templateSource;
            template.RemoveFromHierarchy();
            return asset;
        }

        void SelectServer(int index)
        {
            if (busy) return;
            try { profiles.Select(index); browserMessage = ""; }
            catch (Exception error) { browserMessage = error.Message; }
            savedAccount = profiles.CurrentAccounts.FirstOrDefault()?.Username;
            returnAccount = null;
            username.value = savedAccount ?? "";
            password.value = "";
            SetStatus("");
            BuildAccountControls();
            BuildServerBrowser();
        }

        void BuildAccountControls()
        {
            var accounts = profiles.CurrentAccounts.ToArray();
            bool saved = savedAccount != null;
            loginPanel.EnableInClassList("ff-login-panel--saved-account", saved);
            loginPanel.EnableInClassList("ff-login-panel--has-accounts", accounts.Length != 0);
            accountControls.EnableInClassList("ff-account-controls--remember", rememberAccount);
            accountScroll.Clear();
            accountScrollMaximum = Mathf.Max(0, accounts.Length * 28 - 3 - 79);
            accountScrollTrack.EnableInClassList("ff-account-scroll-track--needed", accountScrollMaximum > 0);
            accountScroll.scrollOffset = Vector2.zero;
            accountScrollThumb.style.translate = new Translate(0, 0);
            if (saved)
            {
                username.value = savedAccount;
                password.value = "";
                foreach (var account in accounts)
                {
                    string name = account.Username;
                    var row = accountRowAsset.Instantiate();
                    var select = FFView.RequireClass<Button>(row, "ff-account-select");
                    select.name = "saved-account-" + name;
                    select.text = FFStyle.Caption(name);
                    select.EnableInClassList("ff-profile-button--selected", name == savedAccount);
                    select.clicked += () => {
                        if (busy) return;
                        savedAccount = name;
                        username.value = name;
                        password.value = "";
                        SetStatus("");
                        BuildAccountControls();
                    };
                    AutoFit(select);
                    var edit = FFView.RequireClass<Button>(row, "ff-account-edit");
                    edit.name = "edit-account-" + name;
                    edit.clicked += () => EditAccount(name);
                    accountScroll.Add(row);
                }
            }
            accountControls.SetEnabled(!busy);
        }

        void BindAccountScrollbar()
        {
            var track = accountScrollTrack;
            draggingAccountScroll = false;
            // UITK exposes the scroll value, but the two textures form a separate authored rail.
            accountScroll.verticalScroller.valueChanged += value => {
                float progress = accountScrollMaximum > 0 ? Mathf.Clamp01(value / accountScrollMaximum) : 0;
                accountScrollThumb.style.translate = new Translate(0, progress * 59);
            };
            void Drag(Vector2 panelPosition)
            {
                float local = track.WorldToLocal(panelPosition).y;
                accountScroll.scrollOffset = new Vector2(0, Mathf.Clamp01((local - 10) / 59) * accountScrollMaximum);
            }
            track.RegisterCallback<PointerDownEvent>(e => {
                if (e.button != 0) return;
                draggingAccountScroll = true;
                track.CapturePointer(e.pointerId);
                Drag(e.position);
            });
            track.RegisterCallback<PointerMoveEvent>(e => { if (draggingAccountScroll) Drag(e.position); });
            track.RegisterCallback<PointerUpEvent>(e => {
                draggingAccountScroll = false;
                track.ReleasePointer(e.pointerId);
            });
            track.RegisterCallback<PointerCaptureOutEvent>(_ => draggingAccountScroll = false);
        }

        void EditAccount(string name)
        {
            if (busy) return;
            returnAccount = savedAccount;
            savedAccount = null;
            username.value = name ?? "";
            password.value = "";
            rememberAccount = true;
            SetStatus("");
            BuildAccountControls();
            if (name == null) username.Focus(); else password.Focus();
        }

        void BackToAccounts()
        {
            if (busy) return;
            savedAccount = returnAccount ?? profiles.CurrentAccounts.FirstOrDefault()?.Username;
            username.value = savedAccount ?? "";
            password.value = "";
            SetStatus("");
            BuildAccountControls();
        }

        void ToggleServerPage()
        {
            if (busy) return;
            addingServer = !addingServer;
            BuildServerBrowser();
            if (addingServer) serverInput.Focus();
        }

        void AdvanceServerPage()
        {
            if (busy) return;
            serverPage = (serverPage + 1) % ((profiles.Servers.Count + ServersPerPage - 1) / ServersPerPage);
            BuildServerBrowser();
        }

        void BuildServerBrowser()
        {
            var focus = Root.panel?.focusController.focusedElement;
            int? focusedIndex = visibleServers.Where(entry => ReferenceEquals(entry.Value.Button, focus))
                .Select(entry => (int?)entry.Key).FirstOrDefault();
            visibleServers.Clear();
            serverRows.Clear();
            browserPanel.EnableInClassList("ff-server-browser--adding", addingServer);
            moreServers.EnableInClassList("ff-server-more--visible", profiles.Servers.Count > ServersPerPage);
            browserTitle.text = FFStyle.Caption(T(addingServer ? "ui.login.add_server" : "ui.login.servers", addingServer ? "Add server" : "SERVERS"));
            serverPageToggle.text = addingServer ? "<" : "+";
            // The draft survives switching between server list and form.
            serverInput.SetValueWithoutNotify(serverInputValue);
            if (!addingServer)
            {
                int start = serverPage * ServersPerPage;
                for (int i = start; i < Math.Min(profiles.Servers.Count, start + ServersPerPage); i++)
                    BuildServerRow(i);
            }
            UpdateServerBrowserMessage();
            browserPanel.SetEnabled(!busy);
            if (focusedIndex is int index && visibleServers.TryGetValue(index, out var row)) row.Button.Focus();
        }

        void BuildServerRow(int index)
        {
            var server = profiles.Servers[index];
            var row = serverRowAsset.Instantiate();
            var button = FFView.RequireClass<Button>(row, "ff-server-row");
            button.name = "server-" + index;
            button.userData = server.Key;
            button.clicked += () => SelectServer(index);
            button.EnableInClassList("ff-server-row--selected", index == profiles.Selected);
            var name = FFView.RequireClass<Label>(row, "ff-server-name");
            name.text = server.Name;
            AutoFit(name);
            var count = FFView.RequireClass<Label>(row, "ff-server-count");
            AutoFit(count);
            var health = FFView.RequireClass<VisualElement>(row, "ff-server-health");
            visibleServers.Add(index, (button, health, count));
            ApplyServerHealth(health, count, serverHealth.TryGetValue(server.Key, out var value) ? value : new ServerHealth());
            serverRows.Add(row);
        }

        static void ApplyServerHealth(VisualElement indicator, Label count, ServerHealth health)
        {
            indicator.EnableInClassList("ff-server-health--online", health.Online == true);
            indicator.EnableInClassList("ff-server-health--offline", health.Online == false);
            count.text = health.PlayerCount?.ToString() ?? "";
        }

        void UpdateVisibleServerHealth(string key, ServerHealth health)
        {
            foreach (var row in visibleServers.Values)
                if (row.Button.userData as string == key) ApplyServerHealth(row.Health, row.Count, health);
        }

        void UpdateServerBrowserMessage()
        {
            browserError.text = browserMessage.Length == 0 ? "" :
                FFStyle.Caption(T("ui.login.profile_error", "Account settings: {error}").Replace("{error}", browserMessage));
            browserError.EnableInClassList("ff-server-error--visible", browserMessage.Length != 0);
            Layout();
        }

        float BrowserHeight()
        {
            int rows = Math.Min(ServersPerPage, profiles.Servers.Count - serverPage * ServersPerPage);
            float height = addingServer ? 124 : 46 + rows * 42 + (profiles.Servers.Count > ServersPerPage ? 30 : 0) + 12;
            return height + (browserMessage.Length == 0 ? 0 : 44);
        }

        void AddServer()
        {
            if (busy) return;
            try
            {
                profiles.Add(serverInputValue);
                serverPage = profiles.Selected / ServersPerPage;
                addingServer = false;
                serverInputValue = "";
                browserMessage = "";
                SelectServer(profiles.Selected);
                RefreshServerHealth();
            }
            catch (Exception error) { browserMessage = error.Message; UpdateServerBrowserMessage(); }
        }

        void RefreshServerHealth()
        {
            nextServerProbe = Time.unscaledTime + 20;
            foreach (var server in profiles.Servers) ProbeServer(server);
        }

        void TickServerHealth()
        {
            if (profiles == null || loginRoot.style.display == DisplayStyle.None) return;
            float now = Time.unscaledTime;
            if (now >= nextServerProbe) RefreshServerHealth();
            var current = profiles.Current;
            if (now < nextOfflineServerProbe || !serverHealth.TryGetValue(current.Key, out var health) || health.Online != false) return;
            nextOfflineServerProbe = now + OfflineServerRetrySeconds;
            ProbeServer(current);
        }

        void MarkServerOffline(LoginServer server)
        {
            // A reply to a probe sent before the disconnect may still arrive.
            serverProbes.Remove(server.Key);
            SetServerHealth(server.Key, new ServerHealth(false));
            nextOfflineServerProbe = Time.unscaledTime + OfflineServerRetrySeconds;
        }

        void SetServerHealth(string key, ServerHealth health)
        {
            serverHealth[key] = health;
            UpdateVisibleServerHealth(key, health);
            if (health.Online == true && connectionErrorServerKey == key && profiles.Current.Key == key &&
                loginRoot.style.display != DisplayStyle.None)
                SetStatus("");
        }

        async void ProbeServer(LoginServer server)
        {
            string key = server.Key;
            int revision = ++healthRevision;
            if (!serverProbes.TryAdd(key, revision)) return;
            try
            {
                var health = await server.ProbeAsync(lifetime.Token);
                if (this && !lifetime.IsCancellationRequested && serverProbes.TryGetValue(key, out int active) && active == revision)
                    SetServerHealth(key, health);
            }
            catch (OperationCanceledException) { }
            catch (ObjectDisposedException) { }
            finally
            {
                if (serverProbes.TryGetValue(key, out int active) && active == revision) serverProbes.Remove(key);
            }
        }
    }
}
