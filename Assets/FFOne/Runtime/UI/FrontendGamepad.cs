using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace FFOne.UI
{
    public sealed partial class FrontendController
    {
        FFOne.Creation.CharacterCreationController padCreator;
        VisualElement padScope, padControl, padFocusFrame;
        readonly List<VisualElement> padChoices = new();
        Vector2 padDirection;
        float padNextMove, padNextSubmit;
        bool padOwnsFocus, sendingPadEvent, padHooks;
        int padWorldInteractFrame = -1;
        string padControlName;
        GameplayUiController.PadCursor menuCursor;
        bool menuCursorActive;
        Vector2 menuCursorPosition;

        void HideMenuCursor()
        {
            menuCursorActive = false;
            if (menuCursor != null) menuCursor.RemoveFromHierarchy();
        }

        bool NavigateMenuCursor(Gamepad pad, VisualElement scope, float scrollAxis)
        {
            // The stick click switches between the established button navigation and a freely positioned cursor.
            if (pad.leftStickButton.wasPressedThisFrame)
            {
                if (menuCursorActive) { HideMenuCursor(); return true; }
                menuCursorActive = true;
                menuCursor ??= new GameplayUiController.PadCursor();
                Root.Add(menuCursor);
                menuCursorPosition = Root.WorldToLocal(padControl != null && scope.Contains(padControl) && VisiblePadElement(padControl)
                    ? padControl.worldBound.center : scope.worldBound.center);
            }
            if (!menuCursorActive) return false;
            if (pad.dpad.ReadValue().sqrMagnitude > .3f) { HideMenuCursor(); return false; }
            gameplayUi?.HidePadMapCursor();
            var stick = pad.leftStick.ReadValue();
            menuCursorPosition += new Vector2(stick.x, -stick.y) * (Mathf.Max(240, Root.contentRect.height) * Time.unscaledDeltaTime);
            menuCursorPosition.x = Mathf.Clamp(menuCursorPosition.x, 0, Root.contentRect.width - 1);
            menuCursorPosition.y = Mathf.Clamp(menuCursorPosition.y, 0, Root.contentRect.height - 1);
            menuCursor.style.left = menuCursorPosition.x - 9;
            menuCursor.style.top = menuCursorPosition.y - 9;
            var hit = Root.panel.Pick(Root.LocalToWorld(menuCursorPosition));
            if (hit != null && scope.Contains(hit))
                ScrollWithGamepad(hit as ScrollView ?? hit.GetFirstAncestorOfType<ScrollView>(), scrollAxis);
            var button = hit as Button ?? hit?.GetFirstAncestorOfType<Button>();
            if (button != null && scope.Contains(button) && VisiblePadElement(button))
            {
                if (button != padControl || padFocusFrame?.parent != button) SelectPadControl(button);
                if (pad.buttonSouth.wasPressedThisFrame) SubmitPadControl(button);
            }
            else { HidePadFocus(); padControl = null; }
            padOwnsFocus = true; padScope = scope;
            return true;
        }

        void ScrollWithGamepad(ScrollView scroll, float amount)
        {
            if (scroll == null || Mathf.Abs(amount) <= .32f) return;
            scroll.scrollOffset += Vector2.down * (amount * Mathf.Max(240, scroll.contentViewport.worldBound.height) * Time.unscaledDeltaTime);
        }

        VisualElement GamepadScope()
        {
            var layers = UiLayers.For(Root);
            foreach (var layer in new[] { UiLayer.Settings, UiLayer.Dialog, UiLayer.Guide })
            {
                var host = layers.Host(layer);
                for (int i = host.childCount - 1; i >= 0; i--)
                    if (VisiblePadElement(host[i]) && host[i].Query<Button>().ToList().Any(VisiblePadElement)) return host[i];
            }
            if (padCreator && padCreator.IsOpen) return padCreator.GamepadView;
            if (gameplayUi?.Windows.View != null) return gameplayUi.ActiveInventoryDetail ?? gameplayUi.Windows.View;
            if (tutorial) return tutorial.GamepadWindow;
            if (networkWorld) return null;
            if (modal != null && VisiblePadElement(modal)) return modal;
            if (showingSelection && VisiblePadElement(selectionRoot)) return selectionRoot;
            return Root;
        }

        static bool VisiblePadElement(VisualElement element)
        {
            if (element?.panel == null || !element.enabledInHierarchy || element.worldBound.width <= 0 || element.worldBound.height <= 0) return false;
            for (var node = element; node != null; node = node.parent)
                if (node.resolvedStyle.display == DisplayStyle.None || node.resolvedStyle.visibility == Visibility.Hidden) return false;
            return true;
        }

        static bool PadClose(VisualElement element) => element.GetClasses().Any(c => c.EndsWith("-close", StringComparison.Ordinal) || c.Contains("-close-"));
        void HidePadFocus()
        {
            if (padFocusFrame?.parent != null) padFocusFrame.RemoveFromHierarchy();
        }

        void SelectPadControl(VisualElement element)
        {
            if (element == null) return;
            HidePadFocus();
            padControl = element; padControlName = element.name;
            padFocusFrame ??= new VisualElement { name = "gamepad-focus-frame", pickingMode = PickingMode.Ignore };
            padFocusFrame.AddToClassList("ff-pad-focus");
            // Several authored buttons set their own border to zero. Draw inside the focused button.
            element.hierarchy.Add(padFocusFrame);
            padFocusFrame.StretchToParentSize();
            element.Focus();
            for (var scroll = element.GetFirstAncestorOfType<ScrollView>(); scroll != null; scroll = scroll.GetFirstAncestorOfType<ScrollView>())
            {
                // Reveal the new selection once. ScrollTo keeps a deferred target on this engine version,
                // which would repeatedly undo subsequent right-stick input.
                var viewport = scroll.contentViewport;
                var first = viewport.WorldToLocal(element.worldBound.min);
                var last = viewport.WorldToLocal(element.worldBound.max);
                var area = viewport.contentRect;
                float x = first.x < area.xMin ? first.x - area.xMin : Mathf.Max(0, last.x - area.xMax);
                float y = first.y < area.yMin ? first.y - area.yMin : Mathf.Max(0, last.y - area.yMax);
                scroll.scrollOffset += new Vector2(x, y);
            }
        }

        void SubmitPadControl(VisualElement element)
        {
            if (element == null || !element.enabledInHierarchy) return;
            sendingPadEvent = true;
            try { using var evt = NavigationSubmitEvent.GetPooled(); evt.target = element; element.SendEvent(evt); }
            finally { sendingPadEvent = false; }
        }

        void MovePadControl(Vector2 dir)
        {
            Vector2 center = padControl.worldBound.center;
            VisualElement best = null; float bestScore = float.PositiveInfinity;
            foreach (var candidate in padChoices)
            {
                if (candidate == padControl) continue;
                Vector2 offset = candidate.worldBound.center - center;
                float forward = Vector2.Dot(offset, dir);
                if (forward <= 1) continue;
                float lateral = Mathf.Abs(offset.x * dir.y - offset.y * dir.x);
                float score = forward + lateral * 3 + lateral * lateral / forward;
                if (score < bestScore) { best = candidate; bestScore = score; }
            }
            if (best != null) SelectPadControl(best);
        }

        void TickGamepadNavigation()
        {
            if (!padHooks)
            {
                padHooks = true;
                TryGetComponent(out padCreator);
                var navigate = InputSystem.actions.FindAction("UI/Navigate");
                if (navigate != null)
                    for (int i = 0; i < navigate.bindings.Count; i++)
                        if (navigate.bindings[i].path.StartsWith("<Gamepad>/", StringComparison.OrdinalIgnoreCase))
                            navigate.ApplyBindingOverride(i, new InputBinding { overridePath = string.Empty });
                Root.RegisterCallback<NavigationMoveEvent>(evt => {
                    var pad = Gamepad.current;
                    if (pad != null && (pad.dpad.ReadValue().sqrMagnitude > .2f || pad.leftStick.ReadValue().sqrMagnitude > .2f || pad.rightStick.ReadValue().sqrMagnitude > .2f))
                        evt.StopImmediatePropagation();
                }, TrickleDown.TrickleDown);
                Root.RegisterCallback<NavigationSubmitEvent>(evt => {
                    if (!sendingPadEvent && Gamepad.current?.buttonSouth.wasPressedThisFrame == true) evt.StopImmediatePropagation();
                }, TrickleDown.TrickleDown);
                Root.RegisterCallback<NavigationCancelEvent>(evt => {
                    if (Gamepad.current?.buttonEast.wasPressedThisFrame == true) evt.StopImmediatePropagation();
                }, TrickleDown.TrickleDown);
            }
            var gamepad = Gamepad.current;
            if (gamepad == null || busy) return;
            Vector2 raw = gamepad.dpad.ReadValue();
            if (raw.sqrMagnitude == 0) raw = gamepad.leftStick.ReadValue();
            Vector2 dir = Mathf.Max(Mathf.Abs(raw.x), Mathf.Abs(raw.y)) < .55f ? Vector2.zero :
                Mathf.Abs(raw.x) > Mathf.Abs(raw.y) ? new Vector2(Mathf.Sign(raw.x), 0) : new Vector2(0, -Mathf.Sign(raw.y));
            bool south = gamepad.buttonSouth.wasPressedThisFrame && padWorldInteractFrame != Time.frameCount, back = gamepad.buttonEast.wasPressedThisFrame;
            bool shoulders = gamepad.leftShoulder.wasPressedThisFrame || gamepad.rightShoulder.wasPressedThisFrame;
            if (Mouse.current?.delta.ReadValue().sqrMagnitude > 4 && dir == Vector2.zero && !south && !back && !shoulders)
            {
                gameplayUi?.HidePadMapCursor();
                HideMenuCursor();
                padOwnsFocus = false; HidePadFocus();
            }
            bool input = dir != Vector2.zero || south || back || shoulders || gamepad.leftStickButton.wasPressedThisFrame || gamepad.rightStick.ReadValue().sqrMagnitude > .3f;
            if (!padOwnsFocus && !input) { padDirection = Vector2.zero; return; }
            var scope = GamepadScope();
            if (scope == null) { HideMenuCursor(); HidePadFocus(); padControl = padScope = null; padDirection = Vector2.zero; return; }
            if (back)
            {
                if (settings?.Visible == true) settings.Close();
                else if (worldMenu != null) CloseWorldMenu();
                else if (modal != null) CloseModal();
                else if (padCreator && padCreator.IsOpen) padCreator.Close();
                else gameplayUi?.CloseWindow();
                return;
            }
            var look = gamepad.rightStick.ReadValue();
            bool barber = gameplayUi?.Windows.Current == GameplayWindow.Barber && scope == gameplayUi.Windows.View;
            if (padCreator && (barber || padCreator.GamepadView == scope) &&
                padCreator.LookWithGamepad(look, Time.unscaledDeltaTime, barber)) look = Vector2.zero;
            else if (showingSelection && scope == selectionRoot)
            {
                if (look.x != 0) RotatePreview?.Invoke(look.x * 100 * Time.unscaledDeltaTime);
                look = Vector2.zero;
            }
            if (NavigateMenuCursor(gamepad, scope, look.y)) return;
            if (gameplayUi?.NavigateMap(gamepad, Time.unscaledDeltaTime) == true)
            {
                padOwnsFocus = true; padScope = scope; padControl = null; return;
            }
            bool newScope = padScope != scope;
            bool needsFocus = !padOwnsFocus || newScope || padControl == null || !scope.Contains(padControl) || !VisiblePadElement(padControl);
            bool move = dir != Vector2.zero && (dir != padDirection || Time.unscaledTime >= padNextMove);
            if (needsFocus || move || shoulders)
            {
                padChoices.Clear();
                scope.Query<Button>().ForEach(b => { if (VisiblePadElement(b) && b.focusable) padChoices.Add(b); });
                scope.Query<TextField>(className: "ff-pad-quantity").ForEach(field => { if (VisiblePadElement(field)) padChoices.Add(field); });
                if (padChoices.Count == 0) return;
            }
            padOwnsFocus = true; padScope = scope;
            if (needsFocus)
            {
                VisualElement preferred = gameplayUi?.TakeDetailFocus(scope);
                preferred ??= !newScope && !string.IsNullOrEmpty(padControlName) ? padChoices.FirstOrDefault(b => b.name == padControlName) : null;
                preferred ??= padChoices.FirstOrDefault(choice => choice.ClassListContains("ff-pad-quantity"));
                if (preferred == null && showingSelection && scope == selectionRoot) preferred = slotButtons.FirstOrDefault(b => padChoices.Contains(b));
                preferred ??= padChoices.FirstOrDefault(b => b.ClassListContains("npc-service-action") || b.ClassListContains("tutorial-npc-mission-button") || b.ClassListContains("mentor-card-toggle") || b.ClassListContains("inventory-item-button") || b.ClassListContains("inventory-nano-card"));
                SelectPadControl(preferred ?? padChoices.FirstOrDefault(b => !PadClose(b)) ?? padChoices[0]);
            }
            if (move)
            {
                if (!needsFocus && padControl is TextField amount && amount.userData is int maximum && dir.x != 0)
                {
                    int.TryParse(amount.value, out int count);
                    amount.value = Mathf.Clamp(count + (dir.x > 0 ? 1 : -1), 1, maximum).ToString();
                }
                else if (!needsFocus) MovePadControl(dir);
                else padControl?.Focus();
                padNextMove = Time.unscaledTime + (dir == padDirection ? .11f : .32f);
            }
            if (padControl?.panel != null && !ReferenceEquals(padControl, Root.panel.focusController.focusedElement) && padOwnsFocus)
                padControl.Focus();
            padDirection = dir;
            if (shoulders && gameplayUi != null)
            {
                var tabs = padChoices.Where(b => b.GetClasses().Any(c => c == "inventory-tab" || c == "mail-folder-tab" || c.StartsWith("ff-tab-", StringComparison.Ordinal) || c.StartsWith("ff-mail-tab-", StringComparison.Ordinal))).ToArray();
                if (tabs.Length > 1)
                {
                    int from = Array.FindIndex(tabs, b => b == padControl || b.GetClasses().Any(c => c.EndsWith("--active", StringComparison.Ordinal) || c == "retro-on" || c.EndsWith("-selected", StringComparison.Ordinal)));
                    int index = (Mathf.Max(0, from) + (gamepad.rightShoulder.wasPressedThisFrame ? 1 : -1) + tabs.Length) % tabs.Length;
                    SelectPadControl(tabs[index]); SubmitPadControl(tabs[index]);
                }
            }
            float scrollY = look.y;
            if (Mathf.Abs(scrollY) > .32f)
            {
                var scroll = padControl as ScrollView ?? padControl?.GetFirstAncestorOfType<ScrollView>();
                if (scroll == null || scroll.verticalScroller.highValue <= scroll.verticalScroller.lowValue)
                    scroll = scope.Query<ScrollView>().ToList().FirstOrDefault(s => VisiblePadElement(s) &&
                        s.verticalScroller.highValue > s.verticalScroller.lowValue);
                ScrollWithGamepad(scroll, scrollY);
            }
            bool repeat = padControl != null && padControl.ClassListContains("ff-pad-repeat");
            if (south || repeat && gamepad.buttonSouth.isPressed && Time.unscaledTime >= padNextSubmit)
            {
                padNextSubmit = Time.unscaledTime + (south ? .35f : .12f);
                SubmitPadControl(padControl);
            }
        }
    }
}
