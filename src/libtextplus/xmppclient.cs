using LibCyStd;
using LibCyStd.Tasks;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.WebSockets;
using System.Reactive.Subjects;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;

namespace LibTextPlus
{
    public static class XmppClientUtils
    {
        public static string IqId() => StringModule.Rando(Chars.DigitsAndLetters, 6);

        public static XElement? TryFindElem(this XElement elem, Func<XElement, bool> predicate)
        {
            return elem.Elements().FirstOrDefault(predicate);
        }

        public static IEnumerable<XElement> TryFindElems(this XElement elem, Func<XElement, bool> predicate)
        {
            return elem.Elements().Where(predicate);
        }

        public static XAttribute? TryFindAttrib(this XElement elem, Func<XAttribute, bool> predicate)
        {
            return elem.Attributes().FirstOrDefault(predicate);
        }

        public static void EnsurePlainAuthMechanism(XElement stanza)
        {
            var mechs = stanza.TryFindElem(s => s.Name.LocalName == "mechanisms");
            if (mechs == null)
                throw new InvalidOperationException("no auth mechanisms found in <stream/> stanza.");
            var plain = mechs.TryFindElem(s => s.Value == "PLAIN");
            if (plain == null)
                throw new InvalidOperationException("no PLAIN auth mechanism found when parsing <stream/> stanza.");
        }

        public static void RaiseSetEx<T>(this TaskCompletionSource<T> tcs, Exception e)
        {
            try { throw e; }
            catch (Exception e2) { tcs.SetException(e2); }
        }
    }

    public class XmppAuthFailedException : InvalidOperationException
    {
        public XElement? Stanza { get; }
        public XmppAuthFailedException()
        {
        }

        public XmppAuthFailedException(string message) : base(message)
        {
        }

        public XmppAuthFailedException(string message, XElement stanza) : base(message)
        {
            Stanza = stanza;
        }

        public XmppAuthFailedException(string message, Exception innerException) : base(message, innerException)
        {
        }
    }

    internal class XmppWebSocketClientState
    {
#pragma warning disable CS8618 // Non-nullable field is uninitialized.
        public ReactiveWebSocketClient Socket { get; set; }
        public Subject<XElement> StanzaRcvr { get; set; }
        public IDisposable OnWebSocketDataRcvdSub { get; set; }
        public AuthTextPlusSession Session { get; set; }
#pragma warning restore CS8618 // Non-nullable field is uninitialized.
    }

    internal static class XmppClientModule
    {
        public static async Task Open(
            XmppWebSocketClientState state)
        {
            var tcs = new TaskCompletionSource<bool>();
            using var _ = state.StanzaRcvr.Subscribe(stanza =>
            {
                if (stanza.Name.LocalName != "features") return;
                try { XmppClientUtils.EnsurePlainAuthMechanism(stanza); tcs.SetResult(true); }
                catch (InvalidOperationException e) { tcs.SetException(e); }
            });
            await state.Socket.SendText(
                XmppPackets.Open(state.Session)
            ).ConfigureAwait(false);
            await tcs.Task.TimeoutAfter(TimeSpan.FromSeconds(15.0)).ConfigureAwait(false);
        }

        public static async Task AuthPlain(XmppWebSocketClientState state)
        {
            var tcs = new TaskCompletionSource<bool>();
            using var _ = state.StanzaRcvr.Subscribe(stanza =>
            {
                switch (stanza.Name.LocalName)
                {
                    case "success": tcs.SetResult(true); break;
                    case "failure": tcs.RaiseSetEx(new XmppAuthFailedException("xmpp authentication failure.", stanza)); break;
                }
            });
            await state.Socket.SendText(
                XmppPackets.Auth(state.Session)
            ).ConfigureAwait(false);
            await tcs.Task.TimeoutAfter(TimeSpan.FromSeconds(15.0)).ConfigureAwait(false);
        }

        public static async Task<XElement> RetrieveIqResp(
            XmppWebSocketClientState state,
            string id,
            ReadOnlyMemory<byte> data,
            TimeSpan timeout)
        {
            var tcs = new TaskCompletionSource<XElement>();
            using var _ = state.StanzaRcvr.Subscribe(stanza =>
            {
                if (stanza.Name.LocalName != "iq")
                    return;
                var iqId = stanza.TryFindAttrib(a => a.Name.LocalName == "id");
                if (iqId == null)
                {
                    tcs.RaiseSetEx(new InvalidOperationException($"expected iq element to have id attribute. stanza was {stanza}"));
                    return;
                }
                if (iqId.Value != id)
                    return;
                var type = stanza.TryFindAttrib(a => a.Name.LocalName == "type" && (a.Value == "result" || a.Value == "error"));
                if (type == null)
                {
                    tcs.RaiseSetEx(new InvalidOperationException($"expected iq element to have type attribute that is 'result' or 'error'. stanza was {stanza}"));
                    return;
                }
                tcs.SetResult(stanza);
            });
            await state.Socket.SendText(data).ConfigureAwait(false);
            return await tcs.Task.TimeoutAfter(timeout).ConfigureAwait(false);
        }

        public static async Task Bind(XmppWebSocketClientState state)
        {
            var iqId = XmppClientUtils.IqId();
            var iq = XmppPackets.IqSetBind(iqId, state.Session);
            var resp = await RetrieveIqResp(state, iqId, iq, TimeSpan.FromSeconds(15.0)).ConfigureAwait(false);
            var type = resp.TryFindAttrib(a => a.Name.LocalName == "type");
            if (type!.Value == "error")
                throw new InvalidOperationException($"after sending bind iq request server returned error: {resp}");
        }

        public static async Task Session(XmppWebSocketClientState state)
        {
            var iqId = XmppClientUtils.IqId();
            var iq = XmppPackets.IqSetSession(iqId);
            var resp = await RetrieveIqResp(state, iqId, iq, TimeSpan.FromSeconds(15.0)).ConfigureAwait(false);
            var type = resp.TryFindAttrib(a => a.Name.LocalName == "type");
            if (type!.Value == "error")
                throw new InvalidOperationException($"after sending set sesdion iq request server returned error: {resp}");
        }

        public static async Task Presence(XmppWebSocketClientState state)
        {
            await state.Socket.SendText(XmppPackets.Presence()).ConfigureAwait(false);
        }
    }

    public class XmppWebSocketClient : IDisposable
    {
        private readonly XmppWebSocketClientState _state;

        public IObservable<XElement> OnRcvdStanza { get; }

        private void ParseStanza(WebSocketMessage msg)
        {
            if (msg.MsgType != WebSocketMessageType.Text)
            {
                Console.Error.WriteLine($"WARNING: unexpected websocket message type {msg.MsgType} received in XmppWebSocketClient.");
                return;
            }

            try
            {
                var s = StringModule.OfSpan(msg.Data.Span);
                var xElem = XElement.Parse(s);
                _state.StanzaRcvr.OnNext(xElem);
            }
            catch (XmlException e)
            {
                _state.StanzaRcvr.OnError(e);
            }
        }

        private void HandleRecvError(Exception e) =>
            _state.StanzaRcvr.OnError(e);

        public async Task Connect()
        {
            await _state.Socket.Connect(
                new Uri("wss://xmpp.prd.gii.me/"),
                TimeSpan.FromSeconds(15.0)
            ).ConfigureAwait(false);

            await XmppClientModule.Open(_state).ConfigureAwait(false);
            await XmppClientModule.AuthPlain(_state).ConfigureAwait(false);
            await XmppClientModule.Bind(_state).ConfigureAwait(false);
            await XmppClientModule.Session(_state).ConfigureAwait(false);
            await XmppClientModule.Presence(_state).ConfigureAwait(false);
        }

        public async Task Send(ReadOnlyMemory<byte> data, TimeSpan timeout)
        {
            await _state.Socket.SendText(data, timeout).ConfigureAwait(false);
        }

        public async Task<XElement> RetrieveIqResp(
            string id,
            ReadOnlyMemory<byte> data,
            TimeSpan timeout)
        {
            return await XmppClientModule.RetrieveIqResp(_state, id, data, timeout).ConfigureAwait(false);
        }

        public void Dispose()
        {
            _state.Socket.Dispose();
            _state.OnWebSocketDataRcvdSub.Dispose();
            _state.StanzaRcvr.Dispose();
        }

        public XmppWebSocketClient(AuthTextPlusSession session)
        {
            var socket = new ReactiveWebSocketClient(opt =>
            {
                opt.Proxy = new WebProxy(session.Proxy.Uri);
                opt.AddSubProtocol("xmpp");
                opt.AddSubProtocol("xmpp-framing");
            });
            _state = new XmppWebSocketClientState
            {
                Session = session,
                Socket = socket,
                StanzaRcvr = new Subject<XElement>(),
                OnWebSocketDataRcvdSub = socket.OnRcvdMessage.Subscribe(ParseStanza, HandleRecvError)
            };
            OnRcvdStanza = _state.StanzaRcvr;
        }
    }
}
