using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace FFOne.Network
{
    /// <summary>One authenticated account, its retained login socket and current shard session.</summary>
    public sealed partial class FusionClient : IDisposable
    {
        private readonly SemaphoreSlim operations = new SemaphoreSlim(1, 1);
        private readonly ConcurrentQueue<NetworkEvent> events = new ConcurrentQueue<NetworkEvent>();
        private readonly CancellationTokenSource lifetime = new CancellationTokenSource();
        private IReadOnlyList<CharacterInfo> characters = Array.Empty<CharacterInfo>();
        private FrameConnection login, shard, retiringShard;
        private string loginId = "";
        private bool disposed;
        private WorldEntry world;
        private long shardGeneration;
        private readonly object bootstrapLock = new object();
        private readonly List<ProtocolFrame> loadingPackets = new List<ProtocolFrame>();
        public TimeSpan ConnectTimeout { get; set; } = TimeSpan.FromSeconds(10);
        public TimeSpan ReplyTimeout { get; set; } = TimeSpan.FromSeconds(30);
        public IReadOnlyList<CharacterInfo> Characters => characters;
        public bool IsLoggedIn => login != null && login.IsConnected;
        public bool IsInWorld => world != null && shard != null && shard.IsConnected;
        private bool worldLoaded;
        public bool IsWorldLoaded { get => worldLoaded && IsInWorld; private set => worldLoaded = value; }
        public int SelectedSlot { get; private set; }
        public WorldEntry World => IsInWorld ? world : null;
        public bool TryDequeueEvent(out NetworkEvent value)
        {
            while (events.TryDequeue(out value))
                if (!value.IsShard || value.ShardGeneration == Interlocked.Read(ref shardGeneration)) return true;
            value = null;
            return false;
        }

        public Task<IReadOnlyList<CharacterInfo>> LoginAsync(string host, int port, string username, string password, CancellationToken cancellationToken = default)
            => LoginCoreAsync(host, port, username, password, false, cancellationToken);
        public Task<IReadOnlyList<CharacterInfo>> LoginCookieAsync(string host, int port, string username, string cookie, CancellationToken cancellationToken = default)
            => LoginCoreAsync(host, port, username, cookie, true, cancellationToken);

        private async Task<IReadOnlyList<CharacterInfo>> LoginCoreAsync(string host, int port, string username, string secret, bool cookie, CancellationToken cancellation)
        {
            byte[] payload = Protocol0104.LoginPayload(username, secret, cookie);
            using (var token = CancellationTokenSource.CreateLinkedTokenSource(cancellation, lifetime.Token))
            {
                await operations.WaitAsync(token.Token).ConfigureAwait(false);
                try
                {
                    ThrowIfDisposed();
                    DisconnectSockets();
                    while (events.TryDequeue(out _)) { }
                    characters = Array.Empty<CharacterInfo>();
                    login = await FrameConnection.ConnectAsync(host, port, ConnectTimeout, token.Token).ConfigureAwait(false);
                    using (var handshake = CancellationTokenSource.CreateLinkedTokenSource(token.Token))
                    {
                        handshake.CancelAfter(ReplyTimeout);
                        await login.SendAsync(PacketId.Login, payload, handshake.Token).ConfigureAwait(false);
                        Array.Clear(payload, 0, payload.Length);
                        var response = await login.ReadHandshakeAsync(PacketId.LoginHeartbeat, PacketId.LoginHeartbeatReply, handshake.Token).ConfigureAwait(false);
                        if (response.PacketType == PacketId.Announcement) throw new IOException(Announcement(response.Payload));
                        if (response.PacketType == PacketId.LoginFailure)
                        { Protocol0104.RequireSize(response.Payload, 72); throw new ServerRejectedException("Login", Protocol0104.Read32(response.Payload, 0)); }
                        Expect(response, PacketId.LoginSuccess, 84);
                        int count = (sbyte)response.Payload[0];
                        if (count < 0 || count > 4) throw new ProtocolException("Invalid character count.");
                        SelectedSlot = (sbyte)response.Payload[1];
                        ulong key = Protocol0104.DeriveKey(unchecked((ulong)Protocol0104.Read64(response.Payload, 4)), count + 1, SelectedSlot + 1);
                        login.InboundKey = key; login.OutboundKey = key;
                        loginId = Protocol0104.ReadUtf16(response.Payload, 12, 33);
                        var roster = new List<CharacterInfo>(count);
                        while (roster.Count < count)
                        {
                            response = await login.ReadHandshakeAsync(PacketId.LoginHeartbeat, PacketId.LoginHeartbeatReply, handshake.Token).ConfigureAwait(false);
                            Expect(response, PacketId.CharacterInfo, 204);
                            var character = Protocol0104.DecodeCharacter(response.Payload);
                            if (character.Slot < 1 || character.Slot > 4 || roster.Any(x => x.Slot == character.Slot || x.Uid == character.Uid))
                                throw new ProtocolException("Duplicate or invalid character slot.");
                            roster.Add(character);
                        }
                        characters = roster.OrderBy(x => x.Slot).ToArray();
                    }
                    login.StartPump(PacketId.LoginHeartbeat, PacketId.LoginHeartbeatReply,
                        frame => Enqueue(new NetworkEvent(false, frame)), error => Enqueue(new NetworkEvent(false, error)));
                    return characters;
                }
                catch (OperationCanceledException) when (!cancellation.IsCancellationRequested && !lifetime.IsCancellationRequested)
                { DisconnectSockets(); throw new TimeoutException("Login timed out."); }
                catch { DisconnectSockets(); throw; }
                finally { Array.Clear(payload, 0, payload.Length); operations.Release(); }
            }
        }

        public Task<WorldEntry> EnterWorldAsync(long characterUid, CancellationToken cancellationToken = default)
            => EnterWorldCoreAsync(characterUid, false, cancellationToken);

        /// <summary>Flush the tutorial shard before SAVE_CHAR_TUTOR, then select on the retained login connection.</summary>
        public Task<WorldEntry> CompleteTutorialAsync(long characterUid, CancellationToken cancellationToken = default)
            => EnterWorldCoreAsync(characterUid, true, cancellationToken);

        private async Task<WorldEntry> EnterWorldCoreAsync(long characterUid, bool completeTutorial, CancellationToken cancellationToken)
        {
            using (var token = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, lifetime.Token))
            {
                await operations.WaitAsync(token.Token).ConfigureAwait(false);
                try
                {
                    RequireLogin();
                    var selected = OwnedCharacter(characterUid);
                    if (!selected.Style.AppearanceComplete) throw new InvalidOperationException("Complete character appearance before entering the world.");
                    if (completeTutorial)
                    {
                        if (selected.Style.TutorialComplete) throw new InvalidOperationException("This character already completed the tutorial.");
                        if (IsInWorld && world.CharacterUid != characterUid) throw new InvalidOperationException("The active tutorial belongs to another character.");
                        await LeaveWorldCoreAsync(token.Token).ConfigureAwait(false);
                        // pack(4): UID + flag + three zero tail bytes. There is no save acknowledgement.
                        byte[] save = new byte[12]; Protocol0104.Write64(save, 0, characterUid); save[8] = 1;
                        await login.SendAsync(PacketId.SaveTutorial, save, token.Token).ConfigureAwait(false);
                    }
                    else if (IsInWorld) throw new InvalidOperationException("Leave the current world first.");
                    var response = await login.RequestAsync(PacketId.Select, UidPayload(characterUid),
                        new[] { PacketId.SelectSuccess, PacketId.SelectFailure, PacketId.ShardSelectSuccess, PacketId.ShardSelectFailure }, ReplyTimeout, token.Token).ConfigureAwait(false);
                    Reject(response, "Character selection", PacketId.SelectFailure, PacketId.ShardSelectFailure);
                    if (response.PacketType == PacketId.SelectSuccess)
                    {
                        Protocol0104.RequireSize(response.Payload, 1);
                        response = await login.RequestAsync(PacketId.ShardSelect, new byte[1],
                            new[] { PacketId.ShardSelectSuccess, PacketId.ShardSelectFailure, PacketId.SelectFailure }, ReplyTimeout, token.Token).ConfigureAwait(false);
                        Reject(response, "Shard selection", PacketId.ShardSelectFailure, PacketId.SelectFailure);
                    }
                    Expect(response, PacketId.ShardSelectSuccess, 28);
                    int ipLength = Array.IndexOf(response.Payload, (byte)0, 0, 16); if (ipLength < 0) ipLength = 16;
                    string host = Encoding.ASCII.GetString(response.Payload, 0, ipLength);
                    int port = Protocol0104.Read32(response.Payload, 16);
                    if (!IPAddress.TryParse(host, out _) || port < 1 || port > 65535) throw new ProtocolException("Invalid shard endpoint.");
                    long serial = Protocol0104.Read64(response.Payload, 20);
                    shard?.Dispose(); shard = await FrameConnection.ConnectAsync(host, port, ConnectTimeout, token.Token).ConfigureAwait(false);
                    using (var handshake = CancellationTokenSource.CreateLinkedTokenSource(token.Token))
                    {
                        handshake.CancelAfter(ReplyTimeout);
                        byte[] enter = new byte[80]; Protocol0104.WriteUtf16(enter, 0, 33, loginId); Protocol0104.Write64(enter, 72, serial);
                        await shard.SendAsync(PacketId.Enter, enter, handshake.Token).ConfigureAwait(false);
                        shard.InboundKey = Protocol0104.FrontendKey;
                        var bootstrap = new List<ProtocolFrame>();
                        while (true)
                        {
                            byte[] raw = await shard.ReadRawAsync(handshake.Token).ConfigureAwait(false);
                            try { response = Protocol0104.DecodeFrame(raw, Protocol0104.FrontendKey); }
                            catch (ProtocolException)
                            {
                                response = Protocol0104.DecodeFrame(raw, Protocol0104.DefaultKey);
                                if (response.PacketType != PacketId.EnterFailure) throw new ProtocolException("Unexpected default-key shard packet.");
                            }
                            Reject(response, "World entry", PacketId.EnterFailure);
                            if (response.PacketType == PacketId.EnterSuccess) break;
                            Expect(response, PacketId.NanoBookSubset, 76);
                            if (bootstrap.Count >= 512) throw new ProtocolException("Excessive world-entry prelude.");
                            bootstrap.Add(response);
                        }
                        var entry = Protocol0104.DecodeWorldEntry(response.Payload, characterUid);
                        shard.OutboundKey = Protocol0104.DeriveKey(entry.ServerTime, unchecked(entry.PlayerId + 1), unchecked(entry.FusionMatter + 1));
                        entry.BootstrapPackets = bootstrap.ToArray();
                        lock (bootstrapLock) { loadingPackets.Clear(); loadingPackets.AddRange(bootstrap); IsWorldLoaded = false; }
                        // Apply only the authoritative entry snapshot; creation replies lack coordinates.
                        selected.X = entry.X; selected.Y = entry.Y; selected.Z = entry.Z; selected.Level = entry.Level;
                        selected.Style = Protocol0104.ReadStyle(entry.RawLoadData, 4, 80);
                        selected.Equipment = Protocol0104.ReadEquipment(entry.RawLoadData, 136);
                        if (completeTutorial && !selected.Style.TutorialComplete)
                            throw new ProtocolException("The shard did not confirm tutorial completion.");
                        SelectedSlot = selected.Slot; world = entry;
                    }
                    var activeShard = shard;
                    long activeGeneration = Interlocked.Increment(ref shardGeneration);
                    activeShard.StartPump(PacketId.ShardHeartbeat, PacketId.ShardHeartbeatReply,
                        frame => HandleShardFrame(activeShard, activeGeneration, frame), error => {
                            if (ReferenceEquals(shard, activeShard) && !ReferenceEquals(retiringShard, activeShard))
                                Enqueue(new NetworkEvent(true, error, activeGeneration));
                        });
                    return world;
                }
                catch { shard?.Dispose(); shard = null; world = null; throw; }
                finally { operations.Release(); }
            }
        }

        /// <summary>Call only after the authoritative map and local player have finished loading.</summary>
        public async Task<WorldEntry> CompleteLoadingAsync(CancellationToken cancellationToken = default)
        {
            using (var token = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, lifetime.Token))
            {
                await operations.WaitAsync(token.Token).ConfigureAwait(false);
                try
                {
                    if (!IsInWorld) throw new InvalidOperationException("Enter a shard before completing scene loading.");
                    if (IsWorldLoaded) return world;
                    var response = await shard.RequestAsync(PacketId.LoadingComplete, new byte[4], new[] { PacketId.LoadingSuccess }, ReplyTimeout, token.Token).ConfigureAwait(false);
                    Protocol0104.RequireSize(response.Payload, 4);
                    lock (bootstrapLock)
                    {
                        world.BootstrapPackets = loadingPackets.ToArray(); loadingPackets.Clear(); IsWorldLoaded = true;
                    }
                    return world;
                }
                catch { shard?.Dispose(); world = null; throw; }
                finally { operations.Release(); }
            }
        }

        private void HandleShardFrame(FrameConnection source, long generation, ProtocolFrame frame)
        {
            lock (bootstrapLock)
            {
                // A retired pump can finish a read while the next shard is connecting.
                // Its packets must never enter the replacement world's bootstrap/event stream.
                if (!ReferenceEquals(shard, source) || generation != Interlocked.Read(ref shardGeneration)) return;
                if (frame.PacketType == ProtocolTravel.MapInfo)
                {
                    ProtocolTravel.TryDecode(frame, out var map);
                    if (world != null) world.MapNumber = map.Map;
                    warpMap?.TrySetResult(map.Map);
                }
                if (frame.PacketType == PacketId.GotoSuccess)
                {
                    var position = Protocol0104.DecodePlayerPosition(frame.Payload);
                    if (world != null)
                    {
                        world.X = position.X; world.Y = position.Y; world.Z = position.Z;
                    }
                }
                if (!IsWorldLoaded)
                {
                    if (loadingPackets.Count >= 4096) throw new ProtocolException("Excessive world-loading prelude.");
                    loadingPackets.Add(frame);
                    return;
                }
            }
            Enqueue(new NetworkEvent(true, frame, generation));
        }

        public async Task LeaveWorldAsync(CancellationToken cancellationToken = default)
        {
            using (var token = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, lifetime.Token))
            {
                await operations.WaitAsync(token.Token).ConfigureAwait(false);
                try { await LeaveWorldCoreAsync(token.Token).ConfigureAwait(false); }
                finally { operations.Release(); }
            }
        }

        private async Task LeaveWorldCoreAsync(CancellationToken cancellation)
        {
            var active = shard;
            retiringShard = active;
            Interlocked.Increment(ref shardGeneration);
            try
            {
                if (world == null) return;
                if (active == null || !active.IsConnected)
                    throw new IOException("The shard disconnected before confirming the player's final save.");
                byte[] payload = new byte[4]; Protocol0104.Write32(payload, 0, world.PlayerId);
                var response = await active.RequestAsync(PacketId.Exit, payload, new[] { PacketId.ExitSuccess, PacketId.ExitFailure }, ReplyTimeout, cancellation).ConfigureAwait(false);
                Protocol0104.RequireSize(response.Payload, 8);
                if (Protocol0104.Read32(response.Payload, 0) != world.PlayerId) throw new ProtocolException("Exit reply player ID mismatch.");
                if (response.PacketType == PacketId.ExitFailure) throw new ServerRejectedException("World exit", Protocol0104.Read32(response.Payload, 4));
            }
            finally
            {
                active?.Dispose(); shard = null; world = null; retiringShard = null;
                lock (bootstrapLock) { loadingPackets.Clear(); IsWorldLoaded = false; }
            }
        }

        public async Task SendMovementAsync(int x, int y, int z, int angle, float velocityX, float velocityY, float velocityZ, int speed, byte keys, bool stopped, CancellationToken cancellationToken = default)
        {
            if (!IsInWorld || !IsWorldLoaded) throw new InvalidOperationException("The world has not finished loading.");
            byte[] payload = Protocol0104.MovementPayload(x, y, z, angle, velocityX, velocityY, velocityZ, speed, keys, stopped);
            await shard.SendAsync(stopped ? PacketId.Stop : PacketId.Move, payload, cancellationToken).ConfigureAwait(false);
        }

        public async Task SendJumpAsync(int x, int y, int z, int angle, int velocityX, int velocityY, int velocityZ, int jumpHeight, byte keys, float launchVelocity, uint jumpKey, CancellationToken cancellationToken = default)
        {
            if (!IsInWorld || !IsWorldLoaded) throw new InvalidOperationException("The world has not finished loading.");
            byte[] payload = Protocol0104.JumpPayload(x, y, z, angle, velocityX, velocityY, velocityZ, jumpHeight, keys, launchVelocity, jumpKey);
            await shard.SendAsync(PacketId.Jump, payload, cancellationToken).ConfigureAwait(false);
        }

        private static byte[] UidPayload(long uid) { byte[] p = new byte[8]; Protocol0104.Write64(p, 0, uid); return p; }
        private static void Expect(ProtocolFrame frame, uint type, int size)
        { if (frame.PacketType != type) throw new ProtocolException($"Unexpected packet 0x{frame.PacketType:x8}; expected 0x{type:x8}."); Protocol0104.RequireSize(frame.Payload, size); }
        private static void Reject(ProtocolFrame frame, string phase, params uint[] failures)
        { if (!failures.Contains(frame.PacketType)) return; Protocol0104.RequireSize(frame.Payload, 4); throw new ServerRejectedException(phase, Protocol0104.Read32(frame.Payload, 0)); }
        private static string Announcement(byte[] bytes)
        { int offset = bytes.Length >= 1032 ? 8 : 0; return Protocol0104.ReadUtf16(bytes, offset, Math.Min(512, (bytes.Length - offset) / 2)); }
        private CharacterInfo OwnedCharacter(long uid)
            => characters.FirstOrDefault(c => c.Uid == uid) ?? throw new ArgumentException("Character does not belong to this session.");
        private void RequireLogin() { ThrowIfDisposed(); if (!IsLoggedIn) throw new InvalidOperationException("Log in before using character operations."); }
        private void ThrowIfDisposed() { if (disposed) throw new ObjectDisposedException(nameof(FusionClient)); }
        private void Enqueue(NetworkEvent value)
        {
            if (events.Count >= 4096) throw new ProtocolException("Network event queue is full; the client must drain it every frame.");
            events.Enqueue(value);
        }
        private void DisconnectSockets() { Interlocked.Increment(ref shardGeneration); shard?.Dispose(); login?.Dispose(); shard = null; login = null; world = null; IsWorldLoaded = false; }
        public void Dispose() { if (disposed) return; disposed = true; lifetime.Cancel(); DisconnectSockets(); }
    }
}
