using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace FFOne.Network
{
    public sealed partial class FusionClient
    {
        internal Task BlockNearbyAsync(int id,long uid,CancellationToken token) =>
            SendGameplayAsync(0x13000070,ProtocolSocial.Identity(id,uid),token);
        public Task CheckMailAsync(CancellationToken token = default) => SendGameplayAsync(ProtocolMail.Check, new byte[1], token);
        public Task ListMailAsync(int page, CancellationToken token = default) => SendGameplayAsync(ProtocolMail.List, ProtocolMail.Page(page), token);
        public Task ReadMailAsync(long index, CancellationToken token = default) => SendGameplayAsync(ProtocolMail.Read, ProtocolMail.Index(index), token);
        public Task DeleteMailAsync(long index, CancellationToken token = default) => SendGameplayAsync(ProtocolMail.Delete, ProtocolMail.DeletePayload(index), token);
        public Task TakeMailTarosAsync(long index, CancellationToken token = default) => SendGameplayAsync(ProtocolMail.TakeTaros, ProtocolMail.Index(index), token);
        public Task TakeAllMailItemsAsync(long index, CancellationToken token = default) => SendGameplayAsync(ProtocolMail.TakeAll, ProtocolMail.Index(index), token);
        public Task TakeMailItemAsync(long index, int slot, int attachment, CancellationToken token = default) => SendGameplayAsync(ProtocolMail.TakeItem, ProtocolMail.ItemPayload(index, slot, attachment), token);
        public Task SendMailAsync(long recipient, string subject, string body, int taros, IReadOnlyList<MailAttachment> items, CancellationToken token = default)
            => SendGameplayAsync(ProtocolMail.Send, ProtocolMail.Compose(recipient, subject, body, taros, items), token);
        public Task RefreshFriendsAsync(CancellationToken token = default) => SendGameplayAsync(0x1300003c, new byte[1], token);
        public Task RequestFriendAsync(int id, long uid, CancellationToken token = default) => SendGameplayAsync(0x13000035, ProtocolSocial.Identity(id, uid), token);
        public Task WarpToBuddyAsync(int slot, long uid, CancellationToken token = default) => SendGameplayAsync(0x13000051, ProtocolSocial.Slot(slot, uid), token);
        public Task AcceptFriendAsync(int id, bool accept, CancellationToken token = default) => SendGameplayAsync(0x13000036, ProtocolSocial.Accept(id, accept), token);
        public Task RequestFriendByNameAsync(string first, string last, CancellationToken token = default) => SendGameplayAsync(0x1300008e, ProtocolSocial.Name(first, last), token);
        public Task AcceptNamedFriendAsync(long uid, string first, string last, bool accept, CancellationToken token = default)
            => SendGameplayAsync(0x1300008f, ProtocolSocial.AcceptName(uid, first, last, accept), token);
        public Task RemoveFriendAsync(int slot, long uid, bool block = false, CancellationToken token = default)
            => SendGameplayAsync(block ? 0x1300003au : 0x1300003bu, ProtocolSocial.Slot(slot, uid), token);
        public Task InviteGroupAsync(int id, CancellationToken token = default) => SendGameplayAsync(0x1300004c, ProtocolSocial.Id(id), token);
        public Task AnswerGroupAsync(int host, bool accept, CancellationToken token = default) => SendGameplayAsync(accept ? 0x1300004eu : 0x1300004du, ProtocolSocial.Id(host), token);
        public Task LeaveGroupAsync(CancellationToken token = default) => SendGameplayAsync(0x1300004f, new byte[1], token);
        public Task SendGroupChatAsync(string text, CancellationToken token = default) => SendGameplayAsync(0x13000063, ProtocolChat.FreeChatPayload(text), token);
        public Task SendFriendChatAsync(int slot, long uid, string text, CancellationToken token = default)
            => SendGameplayAsync(0x13000037, ProtocolSocial.Whisper(slot, uid, text), token);
        public Task SendMenuChatAsync(string text, int emote, CancellationToken token = default)
        {
            var payload = ProtocolChat.FreeChatPayload(text); Protocol0104.Write32(payload, 256, emote);
            return SendGameplayAsync(ProtocolChat.MenuRequest, payload, token);
        }
        public Task SendGroupMenuChatAsync(string text, CancellationToken token) => SendGameplayAsync(0x13000066, ProtocolChat.FreeChatPayload(text), token);
        public Task SendFriendMenuChatAsync(int slot, long uid, string text, CancellationToken token) => SendGameplayAsync(0x13000038, ProtocolSocial.Whisper(slot, uid, text), token);
        public Task SendAvatarEmoteAsync(int player, int emote, CancellationToken token) => SendGameplayAsync(ProtocolChat.EmoteRequest, ProtocolTravel.Words(player, emote), token);
    }
}
