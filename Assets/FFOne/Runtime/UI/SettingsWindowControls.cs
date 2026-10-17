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
            var list = window.Q<ScrollView>("key-mappings"); list.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            list.contentContainer.style.height = 1140;
            var headings = new[] { "MOVEMENT", "INTERFACE", "CAMERA", "COMBAT" };
            var titleY = new[] { 20,349,584,844 }; var rowsY = new[] { 60,390,625,885 };
            var headerRects = new[] { new Rect(10,12,169,36), new Rect(10,340,169,36), new Rect(10,575,139,36), new Rect(10,835,139,36) };
            var bodyRects = new[] { new Rect(10,35,900,280), new Rect(10,363,900,180), new Rect(10,595,870,220), new Rect(10,855,870,280) };
            for (int group = 0; group < ActionGroups.Length; group++)
            {
                var body = new VisualElement { pickingMode = PickingMode.Ignore }; body.AddToClassList("option-side"); SetRect(body,bodyRects[group]); list.Add(body);
                var header = new VisualElement { pickingMode = PickingMode.Ignore }; header.AddToClassList("option-connector"); SetRect(header,headerRects[group]); list.Add(header);
                var title = new Label(headings[group]); title.AddToClassList("option-title"); SetRect(title,new Rect(20,titleY[group],160,24)); captions[title] = title.text; list.Add(title);
                for (int slot = 0; slot < 3; slot++)
                {
                    var label = new Label("SETTING " + (slot + 1)); label.AddToClassList("option-small"); SetRect(label,new Rect(180+slot*220,titleY[group],180,24)); captions[label] = label.text; list.Add(label);
                }
                for (int row = 0; row < ActionGroups[group].Length; row++)
                {
                    string action = ActionGroups[group][row]; float y = rowsY[group] + row * 30;
                    var label = new Label(ActionLabels[group][row]); label.AddToClassList("option-small"); SetRect(label,new Rect(20,y,155,25)); captions[label] = label.text; list.Add(label);
                    for (int column = 0; column < 3; column++)
                    {
                        string slot = new[] { "primary", "alternate", "pad" }[column];
                        var binding = new Button(() => BeginCapture(action,slot)) { name = "binding-"+action+"-"+slot }; binding.AddToClassList("ff-button"); binding.AddToClassList("option-button");
                        SetRect(binding,new Rect(180+column*220,y,180,25)); list.Add(binding);
                        refreshers.Add(() => binding.text = Preferences.Get<string>("input.bindings."+action+"."+slot));
                    }
                }
            }
            B("default-bindings").clicked += () => { Preferences.Set("input.bindings",SettingsInputDefaults.Create()); Preferences.Set("input.pad_profile",0); Refresh(); };
            overlay.RegisterCallback<WheelEvent>(e =>
            {
                if (captureAction == null || captureSlot == "pad") return;
                SubmitBinding(e.delta.y < 0 ? "Wheel+" : "Wheel-"); e.StopImmediatePropagation();
            },TrickleDown.TrickleDown);
        }
        void BeginCapture(string action,string slot)
        {
            if (modalOpen || captureAction != null) return;
            captureAction = action; captureSlot = slot; captureDeadline = Time.realtimeSinceStartupAsDouble + 8;
            UpdateCapturePrompt();
            captureTimer = overlay.schedule.Execute(() =>
            {
                if (Time.realtimeSinceStartupAsDouble >= captureDeadline) { EndCapture(); return; }
                UpdateCapturePrompt();
                if (captureSlot != "pad" || Gamepad.current == null) return;
                foreach (var control in Gamepad.current.allControls.OfType<ButtonControl>())
                    if (control.wasPressedThisFrame) { SubmitBinding(control.path.Substring(Gamepad.current.path.Length + 1)); break; }
            }).Every(16);
            overlay.Focus();
        }
        void UpdateCapturePrompt()
        {
            string label = captureAction;
            for (int group = 0; group < ActionGroups.Length; group++)
            {
                int index = Array.IndexOf(ActionGroups[group], captureAction);
                if (index >= 0) { label = Local(ActionLabels[group][index]); break; }
            }
            window.Q<Label>("capture-prompt").text = T("ui.option.controls.capture_prompt", "Select the key or button to map to {action} ({seconds})")
                .Replace("{action}",label).Replace("{seconds}",Mathf.CeilToInt((float)(captureDeadline-Time.realtimeSinceStartupAsDouble)).ToString());
        }
        void EndCapture()
        {
            captureAction = captureSlot = null; captureTimer?.Pause(); captureTimer = null;
            var prompt = window?.Q<Label>("capture-prompt"); if (prompt != null) prompt.text = "";
        }
        void SubmitBinding(string binding)
        {
            string action = captureAction, slot = captureSlot;
            if (action == null) return;
            var rows = (JObject)Preferences.Draft["input"]["bindings"];
            bool duplicate = binding.Length > 0 && slot != "pad" && rows.Properties().Any(p => p.Name != action &&
                ((string)p.Value["primary"] == binding || (string)p.Value["alternate"] == binding));
            EndCapture();
            if (duplicate) { Message(T("ui.option.popup.duplicate_binding", "This input is already assigned to another action.")); return; }
            Preferences.Set("input.bindings."+action+"."+slot,binding); Refresh();
        }
        void OnKey(KeyDownEvent e)
        {
            if (captureAction != null)
            {
                if (e.keyCode == KeyCode.Backspace || e.keyCode == KeyCode.Delete) SubmitBinding("");
                else if (captureSlot != "pad" && e.keyCode != KeyCode.None) SubmitBinding(e.keyCode.ToString());
                e.StopImmediatePropagation(); return;
            }
            if (e.keyCode != KeyCode.Escape) return;
            if (modalOpen) DismissPopup(); else Close();
            e.StopImmediatePropagation();
        }
        void OnPointer(PointerDownEvent e)
        {
            if (captureAction == null || captureSlot == "pad") return;
            SubmitBinding("Mouse"+e.button); e.StopImmediatePropagation();
        }
        void BindPalettes()
        {
            var rgb = new[] { new Color(1,0,0),new Color(1,.6f,0),new Color(.5f,.66f,0),new Color(0,.39f,0),new Color(0,.47f,.47f),new Color(.5f,0,.5f),
                Color.yellow,Color.black,Color.magenta,new Color(1,.78f,0),Color.yellow,Color.green,Color.cyan,new Color(0,.7f,1),new Color(.56f,.12f,.35f),new Color(.39f,.39f,.39f),new Color(1,.6f,.8f),new Color(1,.8f,.6f) };
            foreach (string channel in new[] { "general","buddy","group" })
            {
                var parent = window.Q("palette-"+channel); string key = "colors."+channel;
                for (int index = 0; index < 18; index++)
                {
                    int value = index; var button = new Button(() => { Preferences.Set(key,value); Refresh(); }) { text = "Aa" };
                    button.AddToClassList("ff-button"); button.AddToClassList("option-swatch"); button.style.left = (index%6)*30; button.style.top = (index/6)*30;
                    button.style.unityBackgroundImageTintColor = rgb[index]; parent.Add(button);
                    var marker = new VisualElement { pickingMode = PickingMode.Ignore };
                    SetRect(marker,new Rect(-2,-2,24,24)); marker.style.backgroundImage = new StyleBackground(FFOne.Content.ContentAssets.Load<Texture2D>("ui/en/option/color-selected")); button.Add(marker);
                    refreshers.Add(() => marker.style.display = Preferences.Get<int>(key) == value ? DisplayStyle.Flex : DisplayStyle.None);
                }
            }
        }
    }
}
