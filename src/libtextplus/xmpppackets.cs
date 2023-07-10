using LibCyStd;
using System;
using System.Linq;
using System.Text;
using System.Xml.Linq;

namespace LibTextPlus
{
    public class XmppPresence
    {
        public XmppPresence(string to, string from, string type)
        {
            To = to;
            From = from;
            Type = type;
        }

        public string To { get; }
        public string From { get; }
        public string Type { get; }
        public string FromJidOnly => From.Split('/').First();

        public static XmppPresence? TryParse(XElement xElem)
        {
            var to = xElem.TryFindAttrib(a => a.Name.LocalName == "to");
            var from = xElem.TryFindAttrib(a => a.Name.LocalName == "from");
            var type = xElem.TryFindAttrib(a => a.Name.LocalName == "type");
            if (to == null || from == null)
                return default;
            if (string.IsNullOrWhiteSpace(from.Value))
                return default;
            return new XmppPresence(to.Value, from.Value, type == null ? "" : type.Value);
        }
    }

    public class XmppMessage
    {
        public string To { get; }
        public string From { get; }
        public string Type { get; }
        public string ThreadId { get; set; }
        public string Body { get; }
        public string FromJidOnly { get; }
        public string ToJidOnly { get; }
        public int SendAttempts { get; set; }

        public XmppMessage(string to, string from, string type, string threadId, string body)
        {
            To = to;
            From = from;
            Type = type;
            ThreadId = threadId;
            Body = body;
            FromJidOnly = from.Split('/').First();
            ToJidOnly = to.Split('/').First();
        }

        public override string ToString()
        {
            return $"{To}~{From}~{Type}~{ThreadId}~{Body}";
        }

        public static XmppMessage? TryParse(XElement xElem)
        {
            var to = xElem.TryFindAttrib(a => a.Name.LocalName == "to");
            if (to == null)
                return default;
            var from = xElem.TryFindAttrib(a => a.Name.LocalName == "from");
            if (from == null)
                return default;
            var type = xElem.TryFindAttrib(a => a.Name.LocalName == "type");
            if (type == null)
                return default;

            var body = xElem.TryFindElem(e => e.Name.LocalName == "body");
            var thr = xElem.TryFindElem(e => e.Name.LocalName == "thread");

            if (StringModule.AnyEmptyOrWhiteSpace(new[] { from.Value, type.Value }))
                return default;
            var thread = thr == null ? "" : thr.Value;
            var bod = body == null ? "" : body.Value;

            return new XmppMessage(to.Value, from.Value, type.Value, thread, bod);
        }
    }

    public static class XmppPackets
    {
        public static ReadOnlyMemory<byte> SendMessage(
            string remoteJid,
            string msgId,
            string threadId,
            string body)
        {
            var msg = $"<message to=\"{remoteJid}\" id=\"{msgId}\" type=\"chat\"><thread>{threadId}</thread><body>{body}</body><request xmlns=\"urn:xmpp:receipts\"/><active xmlns=\"http://jabber.org/protocol/chatstates\"/><request xmlns=\"urn:xmpp:receipts\"/></message>";
            return Encoding.UTF8.GetBytes(msg);
        }

        public static ReadOnlyMemory<byte> Composing(string remoteJid)
        {
            var msg = $"<message to=\"{remoteJid}\" type=\"chat\"><composing xmlns=\"http://jabber.org/protocol/chatstates\"/></message>";
            return Encoding.UTF8.GetBytes(msg);
        }

        public static ReadOnlyMemory<byte> AckMsgRcvd(string id, string remoteJid, string msgId)
        {
            var msg = $"<message id=\"{id}\" to=\"{remoteJid}\"><received xmlns=\"urn:xmpp:receipts\" id=\"{msgId}\"/></message>";
            return new ArraySegment<byte>(Encoding.UTF8.GetBytes(msg));
        }

        public static ReadOnlyMemory<byte> Open(AuthTextPlusSession session)
        {
            var open = $"<open from='{session.Jid}' to='app.nextplus.me' version='1.0' xmlns='urn:ietf:params:xml:ns:xmpp-framing'/>";
            return new ArraySegment<byte>(Encoding.UTF8.GetBytes(open));
        }

        public static ReadOnlyMemory<byte> Auth(AuthTextPlusSession session)
        {
            var credentials = $"\0{session.Jid.Split('@')[0]}\0{session.AuthToken}";
            var base64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(credentials));
            var auth = $"<auth xmlns=\"urn:ietf:params:xml:ns:xmpp-sasl\" mechanism=\"PLAIN\">{base64}</auth>";
            return new ArraySegment<byte>(Encoding.UTF8.GetBytes(auth));
        }

        public static ReadOnlyMemory<byte> IqGetPing(string iqId)
        {
            var ping = $"<iq to=\"app.nextplus.me\" id=\"{iqId}\" type=\"get\"><ping xmlns=\"urn:xmpp:ping\"/></iq>";
            return new ArraySegment<byte>(Encoding.UTF8.GetBytes(ping));
        }

        public static ReadOnlyMemory<byte> IqSetBind(string iqId, AuthTextPlusSession session)
        {
            var bind = $"<iq xmlns=\"jabber:client\" id=\"{iqId}\" type=\"set\"><bind xmlns=\"urn:ietf:params:xml:ns:xmpp-bind\"><resource>{session.AndroidId}</resource></bind></iq>";
            return new ArraySegment<byte>(Encoding.UTF8.GetBytes(bind));
        }

        public static ReadOnlyMemory<byte> IqSetSession(string iqId)
        {
            var session = $"<iq xmlns=\"jabber:client\" id=\"{iqId}\" type=\"set\"><session xmlns=\"urn:ietf:params:xml:ns:xmpp-session\"/></iq>";
            return new ArraySegment<byte>(Encoding.UTF8.GetBytes(session));
        }

        public static ReadOnlyMemory<byte> Presence()
        {
            const string presence = "<presence/>";
            return new ArraySegment<byte>(Encoding.UTF8.GetBytes(presence));
        }

        public static ReadOnlyMemory<byte> PresenceSubscribed(string remoteJid)
        {
            var presence = $"<presence to=\"{remoteJid}\" type=\"subscribed\"/>";
            return new ArraySegment<byte>(Encoding.UTF8.GetBytes(presence));
        }

        public static ReadOnlyMemory<byte> PresenceSubscribe(string remoteJid)
        {
            var presence = $"<presence to=\"{remoteJid}\" type=\"subscribe\"/>";
            return new ArraySegment<byte>(Encoding.UTF8.GetBytes(presence));
        }

        public static ReadOnlyMemory<byte> PresenceUnSubscribed(string remoteJid)
        {
            var presence = $"<presence to=\"{remoteJid}\" type=\"unsubscribed\"/>";
            return new ArraySegment<byte>(Encoding.UTF8.GetBytes(presence));
        }

        public static ReadOnlyMemory<byte> PresenceUnSubscribe(string remoteJid)
        {
            var presence = $"<presence to=\"{remoteJid}\" type=\"unsubscribe\"/>";
            return new ArraySegment<byte>(Encoding.UTF8.GetBytes(presence));
        }

        public static ReadOnlyMemory<byte> Roster(string iqId)
        {
            var roster = $"<iq id=\"{iqId}\" type=\"get\"><query xmlns=\"jabber:iq:roster\"/></iq>";
            return new ArraySegment<byte>(Encoding.UTF8.GetBytes(roster));
        }

        public static ReadOnlyMemory<byte> RemoveRoster(string iqId, string remoteJid)
        {
            var iq = $"<iq id=\"{iqId}\" type=\"set\"><query xmlns=\"jabber:iq:roster\"><item jid=\"{remoteJid}\" subscription=\"remove\"/></query></iq>";
            return new ArraySegment<byte>(Encoding.UTF8.GetBytes(iq));
        }
    }
}
