using System;
using System.Linq;
using System.Threading.Tasks;
using FFOne.Content;
using FFOne.Network;
using UnityEngine;
using UnityEngine.UIElements;
using static FFOne.UI.FFWidgets;

namespace FFOne.UI
{
    public sealed partial class GameplayUiController
    {
        static readonly (int Id, string Art, string Key)[] Mentors =
        {
            (4, "ben", "ben_tennyson"),
            (2, "dexter", "dexter"),
            (3, "mojo", "mojo_jojo"),
            (1, "edd", "edd")
        };

        int mentorOwned, mentorSelected, mentorPending;
        bool mentorWarning, mentorConfirm, mentorDeparture;
        float mentorReplyDeadline;
        internal Func<int, Task> RequestMentor { private get; set; }
        static bool IsMentorCategory(int category) => category >= 18 && category <= 23;
        string MentorText(string key) => LocalizationCatalog.Lookup("ui.guide." + key, language);

        internal void OpenMentor(int category, int paymentFlag)
        {
            if (!IsMentorCategory(category) || disposed || interactionSuspended || prompt != null || mentorPending != 0 || TravelBusy) return;
            if (category == 23 && paymentFlag != 1)
            {
                if (paymentFlag == 0) Error?.Invoke(T("Membership is required to travel to the past.", "Для путешествия в прошлое требуется подписка."));
                return;
            }
            mentorOwned = session.Snapshot.Guide;
            if (mentorOwned < 1 || mentorOwned > 5) return;
            mentorSelected = mentorOwned == 5 ? 0 : mentorOwned;
            mentorWarning = mentorOwned == 5 && category == 23;
            mentorConfirm = false;
            Windows.Open(GameplayWindow.Mentor);
        }

        VisualElement BuildMentorView()
        {
            var root = FFView.InstantiateClass(SocialViewAddress, "mentor-root");
            var backdrop = SocialRole<VisualElement>(root, "mentor-background");
            var panel = SocialRole<VisualElement>(root, "mentor-window");
            var selection = SocialRole<VisualElement>(panel, "mentor-selection");
            var dialog = SocialRole<VisualElement>(panel, "mentor-dialog");
            var confirmArt = SocialRole<VisualElement>(dialog, "mentor-confirm-art");
            var title = SocialRole<Label>(dialog, "mentor-dialog-title");
            var body = SocialRole<Label>(dialog, "mentor-dialog-body");

            bool initial = mentorOwned == 5;
            string purpose = initial ? "choose" : "change";
            string cancel = LocalizationCatalog.Lookup("ui.common.cancel", language);
            string confirm = LocalizationCatalog.Lookup("ui.common.confirm", language);
            root.EnableInClassList("mentor-root--initial", initial);
            SocialRole<Label>(selection, "computress-name").text = FFStyle.Caption(MentorText("computress"));
            SocialRole<Label>(selection, "mentor-heading").text = FFStyle.Caption(MentorText("heading." + purpose));
            SocialRole<Label>(selection, "mentor-intro").text = MentorText("intro." + purpose);
            SocialRole<Label>(selection, "mentor-cost").text = FFStyle.Caption(MentorText("cost").Replace("{price}", "0"));

            var cards = new (int Id, string Art, VisualElement View)[Mentors.Length];
            for (int i = 0; i < Mentors.Length; i++)
            {
                var mentor = Mentors[i];
                var card = SocialRole<VisualElement>(selection, "mentor-card-" + mentor.Art);
                cards[i] = (mentor.Id, mentor.Art, card);
                card.EnableInClassList("mentor-card--current", mentor.Id == mentorOwned);
                SocialRole<Label>(card, "mentor-card-current").text = FFStyle.Caption(MentorText("current"));
                SocialRole<Label>(card, "mentor-card-name").text = FFStyle.Caption(MentorText("mentor." + mentor.Key));
                SocialRole<Label>(card, "mentor-card-body").text = LocalizationCatalog.Lookup(
                    "content.tabledata.guide.guide_string." + (mentor.Id + 5) + ".sz_string", language);
                BindSocialButton(card, "mentor-card-toggle", "", () =>
                {
                    if (mentorSelected == mentor.Id) return;
                    mentorSelected = mentor.Id;
                    playSound?.Invoke("button");
                    PaintMentor(root);
                }, "mentor-toggle-" + mentor.Art);
            }

            var no = BindSocialButton(dialog, "mentor-no", cancel, () =>
            {
                if (mentorWarning) { Dismiss("no_button"); return; }
                playSound?.Invoke("no_button");
                mentorConfirm = false;
                PaintMentor(root);
                SocialRole<Button>(selection, "mentor-primary").Focus();
            });
            var yes = BindSocialButton(dialog, "mentor-yes", confirm, () =>
            {
                playSound?.Invoke("yes_button");
                if (mentorWarning) { mentorWarning = false; PaintMentor(root); return; }
                mentorConfirm = false;
                if (mentorSelected == mentorOwned)
                {
                    PaintMentor(root);
                    Error?.Invoke(T("Can not change same guide.", "Этот гид уже выбран."));
                    return;
                }
                _ = SendMentor(root);
            });
            var primary = BindSocialButton(selection, "mentor-primary", MentorText("action." + purpose), () =>
            {
                mentorConfirm = true;
                playSound?.Invoke("yes_button");
                PaintMentor(root);
                no.Focus();
            });
            BindSocialButton(selection, "mentor-cancel", cancel, () => Dismiss("no_button"));
            BindSocialButton(selection, "mentor-close", "", () => Dismiss("button"));
            BindSocialButton(selection, "mentor-help", "", () => { playSound?.Invoke("button"); OpenGuide(44); });
            root.RegisterCallback<GeometryChangedEvent>(_ => Place());
            root.userData = (Action)RefreshMentor;
            PaintMentor(root);
            return root;

            void Dismiss(string cue)
            {
                playSound?.Invoke(cue);
                Windows.Close();
            }

            void Place()
            {
                float w = root.resolvedStyle.width, h = root.resolvedStyle.height;
                if (!(w > 0 && h > 0)) return;
                var pivot = new Vector2((int)w / 2, (int)h / 2);
                float scale = RetrobutionLayout.Scale(h);
                ScaledRect(panel, new Rect(((int)w - 1036) / 2, ((int)h - 654) / 2, 1036, 654), scale, pivot);
                ScaledRect(backdrop, new Rect(((int)w - 1920) / 2, ((int)h - 1440) / 2, 1920, 1440), scale, pivot);
            }

            void RefreshMentor()
            {
                root.EnableInClassList("mentor-root--warning", mentorWarning);
                dialog.EnableInClassList("social-hidden", !mentorWarning && !mentorConfirm);
                selection.SetEnabled(!mentorWarning && !mentorConfirm && mentorPending == 0 && !mentorDeparture);
                primary.SetEnabled(mentorSelected != 0);
                foreach (var card in cards)
                {
                    bool selected = card.Id == mentorSelected;
                    card.View.EnableInClassList("mentor-card--selected", selected);
                    confirmArt.EnableInClassList("mentor-confirm-art--" + card.Art, selected);
                }
                if (!mentorWarning && !mentorConfirm) return;
                string key = mentorWarning ? "warp" : "confirm." + (initial ? "initial" : "change");
                title.text = FFStyle.Caption(MentorText(key + ".title"));
                body.text = MentorText(key + ".body");
                if (!mentorWarning)
                {
                    var mentor = Mentors.First(item => item.Id == mentorSelected);
                    body.text = body.text.Replace("{mentor}", MentorText("mentor." + mentor.Key));
                }
                yes.text = FFStyle.Caption(mentorWarning ? MentorText("warp.action") : confirm);
            }
        }

        static void PaintMentor(VisualElement root) => (root.userData as Action)?.Invoke();

        async Task SendMentor(VisualElement root)
        {
            if (mentorPending != 0 || RequestMentor == null) return;
            mentorPending = mentorSelected; mentorReplyDeadline = Time.unscaledTime + 12;
            PaintMentor(root);
            try { await RequestMentor(mentorPending); }
            catch (OperationCanceledException) when (travelToken.IsCancellationRequested) { }
            catch (Exception e) { if (!disposed) { mentorPending = 0; PaintMentor(root); Error?.Invoke(e.Message); } }
        }

        bool ReceiveMentor(ProtocolFrame f)
        {
            if (f.PacketType != 0x31000093 && f.PacketType != 0x31000094) return false;
            Protocol0104.RequireSize(f.Payload, 8);
            int mentor = Protocol0104.Read16(f.Payload, 0);
            if (mentorPending == 0 || mentor != mentorPending || session.Snapshot.Guide != mentorOwned)
                throw new ProtocolException("Mentor reply does not match the pending selection.");
            if (f.PacketType == 0x31000094)
            {
                mentorPending = 0;
                if (Windows.Current == GameplayWindow.Mentor) PaintMentor(Windows.View);
                Error?.Invoke(session.Snapshot.Level < 4
                    ? T("Reach level 4 to change your guide.", "Сменить наставника можно с 4-го уровня.")
                    : T("The server could not change your guide.", "Серверу не удалось сменить наставника."));
                return true;
            }
            int count = Protocol0104.Read16(f.Payload, 2), matter = Protocol0104.Read32(f.Payload, 4);
            if (count < 0 || matter < 0) throw new ProtocolException("Invalid mentor settlement.");
            mentorPending = 0;
            session.ConfirmMentor(mentor, count, matter);
            if (Windows.Current == GameplayWindow.Mentor) Windows.Close();
            QueueGuideMessage(mentor, false);
            if (mentorOwned == 5 && travelClient != null) _ = FirstMentorWarp();
            return true;
        }

        async Task FirstMentorWarp()
        {
            var warp = travelCatalog.Warps.FirstOrDefault(w => w.Id == 76 && w.Npc == 1425);
            var actors = socialPeers?.Invoke()?.Where(n => n.Kind == WorldEntityKind.Npc && n.NpcType == 1425).Take(2).ToArray();
            if (warp == null || actors?.Length != 1) return;
            mentorDeparture = true; InputGateChanged?.Invoke();
            try
            {
                await Task.Delay(1500, travelToken);
                if (!disposed && session.Snapshot.IsActive)
                    await BeginTravel(ProtocolTravel.WarpOk, () => travelClient.WarpNpcAsync(actors[0].Id, warp.Id, 4, 0, -1, travelToken));
            }
            catch (OperationCanceledException) when (travelToken.IsCancellationRequested) { }
            finally { mentorDeparture = false; if (!disposed) InputGateChanged?.Invoke(); }
        }
    }
}
