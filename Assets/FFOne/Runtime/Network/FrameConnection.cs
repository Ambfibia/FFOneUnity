using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace FFOne.Network
{
    // Exactly one reader and one serialized sequence/writer per socket. A canceled transaction
    // closes the connection: a late response can never satisfy a subsequent request.
    internal sealed class FrameConnection : IDisposable
    {
        private readonly TcpClient client;
        private readonly NetworkStream stream;
        private readonly SemaphoreSlim writes = new SemaphoreSlim(1, 1);
        private readonly CancellationTokenSource lifetime = new CancellationTokenSource();
        private readonly object pendingLock = new object();
        private TaskCompletionSource<ProtocolFrame> pending;
        private HashSet<uint> expected;
        private int disposed;
        private ushort sequence;
        private Action<ProtocolFrame> unsolicited;
        private Action<Exception> disconnected;
        private uint heartbeat, heartbeatReply;
        internal ulong InboundKey = Protocol0104.DefaultKey;
        internal ulong OutboundKey = Protocol0104.DefaultKey;
        internal bool IsConnected => Volatile.Read(ref disposed) == 0;

        private FrameConnection(TcpClient socket) { client = socket; stream = socket.GetStream(); }

        internal static async Task<FrameConnection> ConnectAsync(string host, int port, TimeSpan timeout, CancellationToken cancellation)
        {
            if (string.IsNullOrWhiteSpace(host) || port < 1 || port > 65535) throw new ArgumentException("Invalid server endpoint.");
            cancellation.ThrowIfCancellationRequested();
            var socket = new TcpClient { NoDelay = true };
            using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation))
            {
                deadline.CancelAfter(timeout);
                try
                {
                    using (deadline.Token.Register(() => socket.Close()))
                        await socket.ConnectAsync(host, port).ConfigureAwait(false);
                    deadline.Token.ThrowIfCancellationRequested();
                    return new FrameConnection(socket);
                }
                catch (Exception) when (deadline.IsCancellationRequested)
                { socket.Close(); cancellation.ThrowIfCancellationRequested(); throw new TimeoutException("Server connection timed out."); }
                catch { socket.Close(); throw; }
            }
        }

        internal async Task SendAsync(uint type, byte[] payload, CancellationToken cancellation)
        {
            using (var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellation, lifetime.Token))
            {
                await writes.WaitAsync(linked.Token).ConfigureAwait(false);
                try
                {
                    byte[] frame = Protocol0104.EncodeFrame(type, payload, OutboundKey, sequence);
                    sequence = (ushort)((sequence + 1) & 4095);
                    await stream.WriteAsync(frame, 0, frame.Length, linked.Token).ConfigureAwait(false);
                }
                catch { Dispose(); throw; }
                finally { writes.Release(); }
            }
        }

        internal async Task<byte[]> ReadRawAsync(CancellationToken cancellation)
        {
            byte[] header = new byte[4];
            await ReadExactAsync(header, cancellation).ConfigureAwait(false);
            uint length = unchecked((uint)Protocol0104.Read32(header, 0));
            Protocol0104.ValidateLength(length);
            byte[] body = new byte[length + 4];
            Buffer.BlockCopy(header, 0, body, 0, 4);
            await ReadExactAsync(body, cancellation, 4).ConfigureAwait(false);
            return body;
        }

        private async Task ReadExactAsync(byte[] buffer, CancellationToken cancellation, int offset = 0)
        {
            // Mono's socket cancellation is not reliable across versions. Closing the socket
            // guarantees a stalled partial frame is interrupted on cancellation and timeout.
            using (cancellation.Register(() => Dispose()))
            {
                try
                {
                    while (offset < buffer.Length)
                    {
                        int count = await stream.ReadAsync(buffer, offset, buffer.Length - offset, cancellation).ConfigureAwait(false);
                        if (count == 0) throw new EndOfStreamException("Server closed the connection.");
                        offset += count;
                    }
                }
                catch { cancellation.ThrowIfCancellationRequested(); throw; }
            }
        }

        internal async Task<ProtocolFrame> ReadHandshakeAsync(uint heartbeatType, uint replyType, CancellationToken cancellation)
        {
            while (true)
            {
                var frame = Protocol0104.DecodeFrame(await ReadRawAsync(cancellation).ConfigureAwait(false), InboundKey);
                if (frame.PacketType != heartbeatType) return frame;
                Protocol0104.RequireSize(frame.Payload, 4);
                await SendAsync(replyType, frame.Payload, cancellation).ConfigureAwait(false);
            }
        }

        internal void StartPump(uint heartbeatType, uint replyType, Action<ProtocolFrame> onPacket, Action<Exception> onDisconnect)
        {
            heartbeat = heartbeatType; heartbeatReply = replyType; unsolicited = onPacket; disconnected = onDisconnect;
            _ = PumpAsync();
        }

        private async Task PumpAsync()
        {
            try
            {
                while (!lifetime.IsCancellationRequested)
                {
                    var frame = await ReadHandshakeAsync(heartbeat, heartbeatReply, lifetime.Token).ConfigureAwait(false);
                    TaskCompletionSource<ProtocolFrame> receiver = null;
                    lock (pendingLock)
                    {
                        if (pending != null && expected.Contains(frame.PacketType))
                        { receiver = pending; pending = null; expected = null; }
                    }
                    if (receiver != null) receiver.TrySetResult(frame);
                    else unsolicited?.Invoke(frame);
                }
            }
            catch (Exception error)
            {
                bool report = IsConnected;
                FailPending(error);
                Dispose();
                if (report) disconnected?.Invoke(error);
            }
        }

        internal async Task<ProtocolFrame> RequestAsync(uint type, byte[] payload, uint[] replies, TimeSpan timeout, CancellationToken cancellation)
        {
            var completion = new TaskCompletionSource<ProtocolFrame>(TaskCreationOptions.RunContinuationsAsynchronously);
            cancellation.ThrowIfCancellationRequested();
            lock (pendingLock)
            {
                if (!IsConnected) throw new IOException("Server connection is closed.");
                if (pending != null) throw new InvalidOperationException("A transaction already owns this connection.");
                expected = new HashSet<uint>(replies); pending = completion;
            }
            using (var timer = new CancellationTokenSource(timeout))
            using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation, lifetime.Token, timer.Token))
            {
                using (deadline.Token.Register(() =>
                {
                    completion.TrySetCanceled();
                    Dispose();
                }))
                {
                    try
                    {
                        await SendAsync(type, payload, deadline.Token).ConfigureAwait(false);
                        return await completion.Task.ConfigureAwait(false);
                    }
                    catch (Exception) when (deadline.IsCancellationRequested)
                    {
                        cancellation.ThrowIfCancellationRequested();
                        if (timer.IsCancellationRequested) throw new TimeoutException("Server reply timed out; reconnect before retrying.");
                        throw new IOException("Server transaction canceled or connection closed; reconnect before retrying.");
                    }
                    finally
                    {
                        lock (pendingLock)
                        { if (pending == completion) { pending = null; expected = null; } }
                    }
                }
            }
        }

        private void FailPending(Exception error)
        {
            lock (pendingLock)
            { pending?.TrySetException(error); pending = null; expected = null; }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) != 0) return;
            lifetime.Cancel(); client.Close();
            FailPending(new IOException("Server connection closed."));
        }
    }
}
