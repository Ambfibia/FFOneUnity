using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.UIElements;

namespace FFOne.UI
{
    public sealed partial class SettingsWindow
    {
        string captureAction, captureSlot;
        double captureDeadline;
        IVisualElementScheduledItem captureTimer;
        InputActionRebindingExtensions.RebindingOperation rebind;
        static readonly string[][] ActionGroups = {
            new[] { "Up", "Down", "RightTurn", "LeftTurn", "Right", "Left", "Jump", "AutoRun" },
            new[] { "NanoBook", "Inventory", "Journal", "Email", "WorldMap" },
            new[] { "CameraUp", "CameraDown", "CameraLeft", "CameraRight", "ZoomIn", "ZoomOut" },
            new[] { "Fire1", "WeaponChange", "Fire2", "NanoCharge", "Nano1", "Nano2", "Nano3", "VehicleToggle" }
        };
        static readonly string[][] ActionLabels = {
            new[] { "FORWARD", "BACKWARD", "TURN RIGHT", "TURN LEFT", "SIDESTEP RIGHT", "SIDESTEP LEFT", "JUMP", "AUTORUN" },
            new[] { "NANO COM", "MY STUFF", "JOURNAL", "E-MAIL", "MAP" },
            new[] { "CAMERA UP", "CAMERA DOWN", "CAMERA LEFT", "CAMERA RIGHT", "ZOOM IN", "ZOOM OUT" },
            new[] { "ATTACK/USE TARGET", "SWITCH WEAPON", "NANO POWER", "NANO BOOST", "SUMMON NANO #1", "SUMMON NANO #2", "SUMMON NANO #3", "VEHICLE" }
        };
        void BindMappings()
        {
            var list = FFView.RequireClass<ScrollView>(window, "option-role-key-mappings");
            list.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            var groups = list.Query<VisualElement>(className: "option-mapping-group").ToList();
            var slots = new[] { "primary", "alternate", "pad" };
            for (int group = 0; group < ActionGroups.Length; group++)
            {
                var rows = groups[group].Query<TemplateContainer>(className: "option-mapping-row-instance").ToList();
                for (int row = 0; row < ActionGroups[group].Length; row++)
                {
                    string action = ActionGroups[group][row];
                    var label = FFView.RequireClass<Label>(rows[row], "option-mapping-caption");
                    captions[label] = ActionLabels[group][row];
                    foreach (string slot in slots)
                    {
                        var binding = FFView.RequireClass<Button>(rows[row], "option-mapping-" + slot);
                        binding.name = "binding-" + action + "-" + slot;
                        binding.clicked += () => BeginCapture(action, slot);
                        refreshers.Add(() =>
                        {
                            string path = SettingsInputDefaults.ControlPath(Preferences.Get<string>("input.bindings." + action + "." + slot), slot);
                            binding.text = InputControlPath.ToHumanReadableString(path, InputControlPath.HumanReadableStringOptions.OmitDevice);
                        });
                    }
                }
            }
            B("default-bindings").clicked += () => { Preferences.Set("input.bindings",SettingsInputDefaults.Create()); Preferences.Set("input.pad_profile",0); Refresh(); };
            overlay.RegisterCallback<WheelEvent>(e =>
            {
                if (captureAction == null || captureSlot == "pad") return;
                if (e.delta.y == 0) return;
                SubmitBinding(e.delta.y < 0 ? "<Mouse>/scroll/up" : "<Mouse>/scroll/down");
                e.StopImmediatePropagation();
            },TrickleDown.TrickleDown);
        }
        void BeginCapture(string action,string slot)
        {
            if (modalOpen || captureAction != null) return;
            captureAction = action;
            captureSlot = slot;
            captureDeadline = Time.realtimeSinceStartupAsDouble + 8;
            UpdateCapturePrompt();
            captureTimer = overlay.schedule.Execute(UpdateCapturePrompt).Every(100);
            overlay.Focus();

            // Capture a path for the options draft. The live action is overridden
            // only when the player applies that draft.
            string capturedPath = null;
            rebind = new InputActionRebindingExtensions.RebindingOperation()
                .WithExpectedControlType<ButtonControl>()
                .WithMatchingEventsBeingSuppressed()
                .WithTimeout(8)
                .OnApplyBinding((operation, path) =>
                {
                    var key = operation.selectedControl as KeyControl;
                    capturedPath = key?.keyCode is Key.Delete or Key.Backspace ? "" : path;
                })
                .OnComplete(_ => SubmitBinding(capturedPath))
                .OnCancel(_ => EndCapture());
            if (slot == "pad")
                rebind.WithControlsHavingToMatchPath("<Gamepad>");
            else
            {
                rebind.WithControlsHavingToMatchPath("<Keyboard>").WithControlsHavingToMatchPath("<Mouse>");
                rebind.WithControlsExcluding("<Keyboard>/anyKey");
            }
            rebind.Start();
        }
        void UpdateCapturePrompt()
        {
            string label = captureAction;
            for (int group = 0; group < ActionGroups.Length; group++)
            {
                int index = Array.IndexOf(ActionGroups[group], captureAction);
                if (index >= 0) { label = Local(ActionLabels[group][index]); break; }
            }
            FFView.RequireClass<Label>(window, "option-role-capture-prompt").text = T("ui.option.controls.capture_prompt", "Select the key or button to map to {action} ({seconds})")
                .Replace("{action}",label).Replace("{seconds}",Mathf.Max(0, Mathf.CeilToInt((float)(captureDeadline-Time.realtimeSinceStartupAsDouble))).ToString());
        }
        void EndCapture()
        {
            captureAction = captureSlot = null; captureTimer?.Pause(); captureTimer = null;
            rebind?.Dispose();
            rebind = null;
            var prompt = window?.Q<Label>(className: "option-role-capture-prompt"); if (prompt != null) prompt.text = "";
        }
        void SubmitBinding(string binding)
        {
            string action = captureAction, slot = captureSlot;
            if (action == null || binding == null) { EndCapture(); return; }
            var rows = (JObject)Preferences.Draft["input"]["bindings"];
            bool duplicate = binding.Length > 0 && slot != "pad" && rows.Properties().Any(p => p.Name != action &&
                (string.Equals(SettingsInputDefaults.ControlPath((string)p.Value["primary"], "primary"), binding, StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(SettingsInputDefaults.ControlPath((string)p.Value["alternate"], "alternate"), binding, StringComparison.OrdinalIgnoreCase)));
            EndCapture();
            if (duplicate) { Message(T("ui.option.popup.duplicate_binding", "This input is already assigned to another action.")); return; }
            Preferences.Set("input.bindings."+action+"."+slot,binding); Refresh();
        }
        void OnKey(KeyDownEvent e)
        {
            if (captureAction != null)
            {
                if (e.keyCode == KeyCode.Backspace || e.keyCode == KeyCode.Delete) SubmitBinding("");
                e.StopImmediatePropagation(); return;
            }
            if (e.keyCode != KeyCode.Escape) return;
            if (modalOpen) DismissPopup(); else Close();
            e.StopImmediatePropagation();
        }
        void BindPalettes()
        {
            var rgb = new[] { new Color(1,0,0),new Color(1,.6f,0),new Color(.5f,.66f,0),new Color(0,.39f,0),new Color(0,.47f,.47f),new Color(.5f,0,.5f),
                Color.yellow,Color.black,Color.magenta,new Color(1,.78f,0),Color.yellow,Color.green,Color.cyan,new Color(0,.7f,1),new Color(.56f,.12f,.35f),new Color(.39f,.39f,.39f),new Color(1,.6f,.8f),new Color(1,.8f,.6f) };
            foreach (string channel in new[] { "general","buddy","group" })
            {
                var parent = FFView.RequireClass<VisualElement>(window, "option-role-palette-" + channel);
                var swatches = parent.Query<Button>(className: "option-swatch").ToList();
                string key = "colors." + channel;
                for (int index = 0; index < rgb.Length; index++)
                {
                    int value = index;
                    var button = swatches[index];
                    button.style.unityBackgroundImageTintColor = rgb[index];
                    button.clicked += () => { Preferences.Set(key, value); Refresh(); };
                    refreshers.Add(() => button.EnableInClassList("selected", Preferences.Get<int>(key) == value));
                }
            }
        }
    }
}
