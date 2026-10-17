using System;
using System.Collections.Generic;
using System.Linq;

namespace FFOne.Network
{
    public sealed class MailAttachment
    {
        public int InventorySlot { get; }
        public EquipmentItem Item { get; }
        public MailAttachment(int slot, EquipmentItem item) { InventorySlot = slot; Item = item; }
    }
    public sealed class MailSummary
    {
        public long Index, SenderUid;
        public string Sender, Subject;
        public bool Read, HasAttachments;
    }
    public sealed class MailUpdate
    {
        public uint Type;
        public long Index, RecipientUid;
        public int Page, Unread, Taros, Error, InventorySlot, AttachmentSlot;
        public string Body;
        public bool Failed;
        public IReadOnlyList<MailSummary> Summaries;
        public IReadOnlyList<EquipmentItem> Items;
        public IReadOnlyList<long> Deleted;
    }
    /// <summary>RustyFusion email.rs / ffproto 0104 pack(4). Pages and attachment slots are one-based.</summary>
    public static class ProtocolMail
    {
        public const uint Check = 0x1300007b, Read = 0x1300007c, List = 0x1300007d, Delete = 0x1300007e,
            Send = 0x1300007f, TakeItem = 0x13000080, TakeTaros = 0x13000081, TakeAll = 0x13000089;
        public const uint New = 0x310000e0, ReadOk = 0x310000e1, ListOk = 0x310000e3, DeleteOk = 0x310000e5,
            SendOk = 0x310000e7, ItemOk = 0x310000e9, TarosOk = 0x310000eb, AllOk = 0x310000f7;
        public static byte[] Index(long index)
        { if (index <= 0 || index > int.MaxValue) throw new ArgumentOutOfRangeException(nameof(index)); var p = new byte[8]; Protocol0104.Write64(p, 0, index); return p; }
        public static byte[] Page(int page)
        { if (page < 1 || page > 127) throw new ArgumentOutOfRangeException(nameof(page)); return new[] { (byte)page }; }
        public static byte[] DeletePayload(long index)
        { var p = new byte[40]; Array.Copy(Index(index), p, 8); return p; }
        public static byte[] ItemPayload(long index, int inventorySlot, int attachmentSlot)
        {
            ProtocolInventory.ValidateSlot(ItemContainer.Inventory, inventorySlot);
            if (attachmentSlot < 1 || attachmentSlot > 4) throw new ArgumentOutOfRangeException(nameof(attachmentSlot));
            var p = new byte[16]; Array.Copy(Index(index), p, 8); Protocol0104.Write32(p, 8, inventorySlot); Protocol0104.Write32(p, 12, attachmentSlot); return p;
        }
        public static byte[] Compose(long recipient, string subject, string body, int taros, IReadOnlyList<MailAttachment> attachments)
        {
            if (recipient <= 0 || taros < 0 || attachments == null || attachments.Count > 4) throw new ArgumentException("Invalid mail recipient/attachments/currency.");
            var p = new byte[1164]; Protocol0104.Write64(p, 0, recipient);
            Protocol0104.WriteUtf16(p, 8, 32, subject ?? ""); Protocol0104.WriteUtf16(p, 72, 512, body ?? "");
            var seen = new HashSet<int>();
            for (int i = 0; i < attachments.Count; i++)
            {
                var a = attachments[i]; ProtocolInventory.ValidateSlot(ItemContainer.Inventory, a.InventorySlot);
                if (!seen.Add(a.InventorySlot) || a.Item.ItemId <= 0 || (a.Item.Option & 0xffff) == 0) throw new ArgumentException("Invalid or repeated mail attachment.");
                int o = 1096 + i * 16; Protocol0104.Write32(p, o, a.InventorySlot);
                Protocol0104.Write16(p, o + 4, a.Item.ItemType); Protocol0104.Write16(p, o + 6, a.Item.ItemId);
                Protocol0104.Write32(p, o + 8, a.Item.Option); Protocol0104.Write32(p, o + 12, a.Item.TimeLimit);
            }
            Protocol0104.Write32(p, 1160, taros); return p;
        }
        public static int Cost(int taros, int count)
        { if (taros < 0 || count < 0 || count > 4) throw new ArgumentOutOfRangeException(); return checked(taros + 50 + count * 20); }
        public static bool TryDecode(ProtocolFrame frame, out MailUpdate update)
        {
            update = null; if (frame == null) return false;
            var p = frame.Payload; var u = new MailUpdate { Type = frame.PacketType };
            switch (frame.PacketType)
            {
                case New: Size(p, 4); u.Unread = Nonnegative(I(p, 0)); break;
                case ListOk:
                    Size(p, 1024); u.Page = p[0]; if (u.Page < 1 || u.Page > 127) throw new ProtocolException("Invalid email page."); var summaries = new List<MailSummary>(); var ids = new HashSet<long>();
                    for (int i = 0; i < 5; i++)
                    {
                        int o = 4 + i * 204; long id = Protocol0104.Read64(p, o); if (id == 0) continue;
                        if (id < 1 || id > int.MaxValue || !ids.Add(id)) throw new ProtocolException("Invalid/repeated email index.");
                        summaries.Add(new MailSummary { Index = id, SenderUid = Protocol0104.Read64(p, o + 8),
                            Sender = (Protocol0104.ReadUtf16(p, o + 16, 9) + " " + Protocol0104.ReadUtf16(p, o + 34, 17)).Trim(),
                            Subject = Protocol0104.ReadUtf16(p, o + 68, 32), Read = I(p, o + 132) != 0, HasAttachments = I(p, o + 200) != 0 });
                    }
                    u.Summaries = summaries.AsReadOnly(); break;
                case ReadOk:
                    Size(p, 1084); u.Index = ValidIndex(p); u.Body = Protocol0104.ReadUtf16(p, 8, 512);
                    u.Items = Array.AsReadOnly(Enumerable.Range(0, 4).Select(i => ProtocolServices.Item(p, 1032 + i * 12)).ToArray());
                    u.Taros = Nonnegative(I(p, 1080)); break;
                case DeleteOk: case DeleteOk + 1:
                    Size(p, frame.PacketType == DeleteOk ? 40 : 44); u.Deleted = Array.AsReadOnly(Enumerable.Range(0, 5).Select(i => Protocol0104.Read64(p, i * 8)).ToArray());
                    u.Failed = frame.PacketType != DeleteOk; if (u.Failed) u.Error = I(p, 40); break;
                case SendOk: Size(p, 76); u.RecipientUid = Protocol0104.Read64(p, 0); u.Taros = Nonnegative(I(p, 8)); break;
                case SendOk + 1: Size(p, 12); u.Failed = true; u.RecipientUid = Protocol0104.Read64(p, 0); u.Error = I(p, 8); break;
                case ItemOk:
                    Size(p, 16); u.Index = ValidIndex(p); u.InventorySlot = I(p, 8); u.AttachmentSlot = I(p, 12);
                    if (u.InventorySlot < 0 || u.InventorySlot >= 50 || u.AttachmentSlot < 1 || u.AttachmentSlot > 4) throw new ProtocolException("Invalid mail item receipt slot."); break;
                case AllOk: Size(p, 8); u.Index = ValidIndex(p); break;
                case TarosOk: Size(p, 12); u.Index = ValidIndex(p); u.Taros = Nonnegative(I(p, 8)); break;
                case ListOk + 1: Size(p, 8); u.Failed = true; u.Page = p[0]; u.Error = I(p, 4); break;
                case ReadOk + 1: case TarosOk + 1: case AllOk + 1:
                    Size(p, 12); u.Failed = true; u.Index = ValidIndex(p); u.Error = I(p, 8); break;
                case ItemOk + 1: Size(p, 20); u.Failed = true; u.Index = ValidIndex(p); u.Error = I(p, 16); break;
                default: return false;
            }
            update = u; return true;
        }
        static long ValidIndex(byte[] p) { long i = Protocol0104.Read64(p, 0); if (i <= 0 || i > int.MaxValue) throw new ProtocolException("Invalid mail index."); return i; }
        static int Nonnegative(int i) { if (i < 0) throw new ProtocolException("Negative mail count/currency."); return i; }
        static int I(byte[] p, int o) => Protocol0104.Read32(p, o);
        static void Size(byte[] p, int n) => Protocol0104.RequireSize(p, n);
    }
}
