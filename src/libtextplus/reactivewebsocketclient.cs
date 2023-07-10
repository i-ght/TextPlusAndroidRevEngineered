using LibCyStd;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Net.WebSockets.Managed;
using System.Reactive.Subjects;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace LibTextPlus
{
    public readonly struct WebSocketMessage
    {
        public readonly System.Net.WebSockets.WebSocketMessageType MsgType { get; }
        public readonly Memory<byte> Data { get; }

        public WebSocketMessage(System.Net.WebSockets.WebSocketMessageType msgType, in Memory<byte> data)
        {
            MsgType = msgType;
            Data = data;
        }
    }

    internal class ReactiveWebSocketState : IDisposable
    {
        #pragma warning disable CS8618 // Non-nullable field is uninitialized.
        public System.Net.WebSockets.Managed.ClientWebSocket Socket { get; set; }
        public Subject<WebSocketMessage> MessageRcvr { get; set; }
        public CancellationTokenSource Cts { get; set; }
#pragma warning restore CS8618 // Non-nullable field is uninitialized.
        public bool IsDisposed { get; private set; }

        public void Dispose()
        {
            if (IsDisposed) return;
            IsDisposed = true;
            Socket.Dispose();
            MessageRcvr.OnCompleted();
            MessageRcvr.Dispose();
            Cts.Cancel();
            Cts.Dispose();
        }
    }

    public class ReactiveWebSocketClient : IDisposable
    {
        private readonly ReactiveWebSocketState _state;

        private bool _disposed;

        public IObservable<WebSocketMessage> OnRcvdMessage { get; }

        private void RaiseTimeout(OperationCanceledException e)
        {
            // OperationCancelledException will be thrown by ClientWebSocket.ReceiveAsync if timeout occurs.
            // Dispose can also trigger a OperationCancelledException if execution point is at ReceiveAsync in loop and Dispose is invoked.
            // if Dispose is what caused the OperationCancelledException, the error is ignored.
            if (_disposed) return;
            // You don't see the next two lines of this code. You don't see them because theyre not there.
            try { throw new TimeoutException("web socket timed out trying to recv.", e); }
            catch (TimeoutException e2) { _state.MessageRcvr.OnError(e2); }
        }

#if DEBUG
        private unsafe void Debug(in ReadOnlySpan<byte> data)
        {
            fixed (byte* ptr = data)
            {
                var s = new string((sbyte*)ptr, 0, data.Length);
            }
        }
#endif

        private async void Recv()
        {
            using var _ = _state;
            try
            {
                using var memOwner = MemoryPool<byte>.Shared.Rent(2048);
                var seg = ((ReadOnlyMemory<byte>)memOwner.Memory).AsArraySeg();
                using var memeOry = new MemoryStream(2048);
                while (!_state.Cts.IsCancellationRequested)
                {
                    var recvResult = await _state.Socket.ReceiveAsync(seg, _state.Cts.Token).ConfigureAwait(false);
                    var mem = new ReadOnlyMemory<byte>(seg.Array, 0, recvResult.Count);
                    memeOry.Write(mem.Span);
                    if (!recvResult.EndOfMessage)
                        continue;

                    Memory<byte> msgData = memeOry.ToArray();
//#if DEBUG
//                    Debug(msgData.Span);
//#endif

                    _state.MessageRcvr.OnNext(new WebSocketMessage(recvResult.MessageType, msgData));
                    memeOry.Position = 0;
                    memeOry.SetLength(0);
                }
            }
            catch (OperationCanceledException e) { RaiseTimeout(e); }
            catch (System.Net.WebSockets.WebSocketException e) { _state.MessageRcvr.OnError(e); }
            catch (Exception e)
            {
                Environment.FailFast(
                    $"unhandled exception occured in ReactiveWebSocketClient: {e.GetType().Name} ~ {e.Message}",
                    e
                );
            }
        }

        public async Task Connect(Uri uri, TimeSpan timeout)
        {
            using var cts = new CancellationTokenSource(timeout);
            // If timeout occurs, WebSocketException is thrown with OperationCanceledException as InnerException value.
            await _state.Socket.ConnectAsync(uri, cts.Token).ConfigureAwait(false);
            Recv();
        }

        public async Task Send(
            ReadOnlyMemory<byte> data,
            TimeSpan timeout,
            System.Net.WebSockets.WebSocketMessageType msgType,
            bool endOfMsg)
        {
            try
            {
                using var cts = new CancellationTokenSource(timeout);
                var arraySeg = data.AsArraySeg();
                await _state.Socket.SendAsync(
                    arraySeg,
                    msgType,
                    endOfMsg,
                    cts.Token
                ).ConfigureAwait(false);
            }
            catch (OperationCanceledException e)
            {
                throw new TimeoutException("web socket timed out trying to send.", e);
            }
        }

        public async Task SendText(
            ReadOnlyMemory<byte> data,
            TimeSpan timeout)
        {
            await Send(
                data,
                timeout,
                System.Net.WebSockets.WebSocketMessageType.Text,
                true
            ).ConfigureAwait(false);
        }

        public async Task SendText(
            ReadOnlyMemory<byte> data)
        {
            await SendText(data, Timeout.InfiniteTimeSpan).ConfigureAwait(false);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (!_state.IsDisposed)
                _state.Cts.Cancel();
        }

        public ReactiveWebSocketClient(
            Action<ClientWebSocketOptions>? conf = null)
        {
            _state = new ReactiveWebSocketState
            {
                Socket = new System.Net.WebSockets.Managed.ClientWebSocket(),
                MessageRcvr = new Subject<WebSocketMessage>(),
                Cts = new CancellationTokenSource(),
            };
            OnRcvdMessage = _state.MessageRcvr;
            conf?.Invoke(_state.Socket.Options);
        }
    }
}
