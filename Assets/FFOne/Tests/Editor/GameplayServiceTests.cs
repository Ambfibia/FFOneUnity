using System;
using System.Linq;
using FFOne.Network;
using FFOne.UI;
using FFOne.World;
using NUnit.Framework;

namespace FFOne.Tests
{
    public sealed class GameplayServiceTests
    {
        static GameplayUiSession Session()
        {
            var p = new byte[2700]; Protocol0104.Write32(p, 0, 51); Protocol0104.Write64(p, 8, 1234);
            Protocol0104.Write16(p, 1676, -1); var s = new GameplayUiSession(_ => null);
            s.SetWorld(Protocol0104.DecodeWorldEntry(p, 1234)); return s;
        }
        static byte[] Bank(int number)
        { var p = new byte[2404]; Protocol0104.Write32(p, 2400, number); return p; }
        [Test] public void BankUsesCurrentServerCapacityAndIdentityWithoutTreatingNumberAsAccessFlag()
        {
            var s = Session(); var p = Bank(4); GameplayInventoryTests.Item(p, 199 * 12, 1, 67, 1);
            s.ApplyFrame(new ProtocolFrame(ProtocolServices.BankOpened, p));
            Assert.AreEqual(4, s.Snapshot.BankNumber); Assert.AreEqual(200, s.Snapshot.Bank.Count); Assert.AreEqual(67, s.Snapshot.Bank[199].ItemId);
            Assert.DoesNotThrow(() => ProtocolInventory.ValidateSlot(ItemContainer.Bank, 199));
            Assert.Throws<ArgumentOutOfRangeException>(() => ProtocolInventory.ValidateSlot(ItemContainer.Bank, 200));
            Assert.Throws<ProtocolException>(() => s.ApplyFrame(new ProtocolFrame(ProtocolServices.BankOpened, new byte[1432])));
        }
        [Test] public void BankPageMustBeEntirelyValidBeforePublishing()
        {
            var s = Session(); var old = s.Snapshot; var p = Bank(2); Protocol0104.Write16(p, 199 * 12 + 2, -1);
            Assert.Throws<ProtocolException>(() => s.ApplyFrame(new ProtocolFrame(ProtocolServices.BankOpened, p))); Assert.AreSame(old, s.Snapshot);
        }
        [Test] public void BankMoveCommitsBothPostStatesAndCloseRetiresTheSnapshot()
        {
            var s = Session(); s.ApplyFrame(new ProtocolFrame(ProtocolServices.BankOpened, Bank(2))); var old = s.Snapshot;
            var p = new byte[40]; Protocol0104.Write32(p, 0, 1); Protocol0104.Write32(p, 4, 3);
            Protocol0104.Write32(p, 20, 3); Protocol0104.Write32(p, 24, 199); GameplayInventoryTests.Item(p, 28, 1, 67, 1);
            s.ApplyFrame(new ProtocolFrame(ProtocolInventory.MoveSuccess, p)); Assert.AreEqual(67, s.Snapshot.Bank[199].ItemId);
            Assert.AreEqual(0, old.Bank[199].ItemId); Assert.True(s.CanMoveItem(ItemContainer.Bank, 199, ItemContainer.Inventory, 0));
            s.ApplyFrame(new ProtocolFrame(ProtocolServices.BankClosed, new byte[4]));
            Assert.False(s.CanMoveItem(ItemContainer.Bank, 199, ItemContainer.Inventory, 0)); Assert.AreEqual(-1, s.Snapshot.BankNumber);
            var closed = s.Snapshot; Assert.Throws<ProtocolException>(() => s.ApplyFrame(new ProtocolFrame(ProtocolInventory.MoveSuccess, p))); Assert.AreSame(closed, s.Snapshot);
        }
        [Test] public void SaleUsesItemStayAndFailureDoesNotMutateTheBalance()
        {
            var s = Session(); var p = new byte[32]; Protocol0104.Write32(p, 0, 1200); Protocol0104.Write32(p, 4, 3);
            GameplayInventoryTests.Item(p, 8, 7, 1, 2); GameplayInventoryTests.Item(p, 20, 7, 1, 8);
            s.ApplyFrame(new ProtocolFrame(ProtocolServices.Sold, p)); Assert.AreEqual(8, s.Snapshot.Inventory[3].Quantity); Assert.AreEqual(1200, s.Snapshot.Taros);
            var old = s.Snapshot; s.ApplyFrame(new ProtocolFrame(ProtocolServices.Sold + 1, new byte[4])); Assert.AreSame(old, s.Snapshot);
            Protocol0104.Write32(p, 4, 50); Assert.Throws<ProtocolException>(() => s.ApplyFrame(new ProtocolFrame(ProtocolServices.Sold, p))); Assert.AreSame(old, s.Snapshot);
        }
        [Test] public void VendorRowsKeepPacketSlotSortAndServerPriceAndRejectInvalidLastRow()
        {
            var p = new byte[480]; Protocol0104.Write32(p, 19 * 24, 651); Array.Copy(BitConverter.GetBytes(294f), 0, p, 19 * 24 + 4, 4);
            GameplayInventoryTests.Item(p, 19 * 24 + 8, 0, 366, 1); Protocol0104.Write32(p, 19 * 24 + 20, 777);
            ProtocolServices.TryDecode(new ProtocolFrame(ProtocolServices.Listed, p), out var update);
            var row = update.Listings.Single(); Assert.AreEqual(19, row.SourceSlot); Assert.AreEqual(777, row.Sort); Assert.AreEqual(294, row.Price);
            Array.Copy(BitConverter.GetBytes(float.NaN), 0, p, 19 * 24 + 4, 4);
            Assert.Throws<ProtocolException>(() => ProtocolServices.TryDecode(new ProtocolFrame(ProtocolServices.Listed, p), out _));
        }
        [Test] public void SourceNpcServicesResolveByNativeCategoryAndVendorTable()
        {
            var catalog = WorldNpcCatalog.Load(); Assert.True(catalog.Find(651).IsVendor);
            Assert.True(catalog.Find(686).IsBank); Assert.True(catalog.Find(2586).IsBank);
            Assert.True(catalog.Find(686).HiddenMarker, "The ordinary bank is a source location probe; its ObjectNPC1 cube must stay hidden.");
            Assert.False(catalog.Find(1).IsVendor); Assert.False(catalog.Find(1).IsBank);
        }
    }
}
