using System;
using System.Collections.Generic;
using System.Text;
using System.Xml.Linq;

namespace LibTextPlus.ChatAgent
{
    public abstract class ChatAgentDirective { }

    public sealed class ConnectDirective : ChatAgentDirective
    {
        private ConnectDirective() { }
        public static ConnectDirective Value { get; }
        static ConnectDirective() => Value = new ConnectDirective();
    }

    public class UpdateCfgDirective : ChatAgentDirective
    {
        public Cfg Cfg { get; }
        public UpdateCfgDirective(Cfg cfg)
        {
            Cfg = cfg;
        }
    }

    public class UpdateSeqsDirective : ChatAgentDirective
    {
        public Sequences Seqs { get; }
        public UpdateSeqsDirective(Sequences seqs)
        {
            Seqs = seqs;
        }
    }

    public class ErrorDirective : ChatAgentDirective
    {
        public Exception Error { get; }

        public ErrorDirective(Exception error)
        {
            Error = error;
        }
    }

    public class ProcessStanzaDirective : ChatAgentDirective
    {
        public XElement Stanza { get; }

        public ProcessStanzaDirective(XElement stanza)
        {
            Stanza = stanza;
        }
    }

    public sealed class RetrIqRespDirective : ChatAgentDirective
    {
        public string IqId { get; }
        public ReadOnlyMemory<byte> Data { get; }
        public TimeSpan Timeout { get; }
        public Action<XElement>? ResponseHandler { get; }

        public RetrIqRespDirective(string iqId, in ReadOnlyMemory<byte> data, in TimeSpan timeout, Action<XElement>? respHandler = null)
        {
            IqId = iqId;
            Data = data;
            Timeout = timeout;
            ResponseHandler = respHandler;
        }
    }

    public class SendTypingDirective : ChatAgentDirective
    {
        public string RemoteJid { get; }

        public SendTypingDirective(string remoteJid)
        {
            RemoteJid = remoteJid;
        }
    }

    public sealed class SendReplyDirective : ChatAgentDirective
    {
        private SendReplyDirective() { }
        public static SendReplyDirective Value { get; }
        static SendReplyDirective() => Value = new SendReplyDirective();
    }

    public class SendGreetDirective : ChatAgentDirective
    {
        public SendGreetDirective(TextPlusMatchableSearchResponse contact)
        {
            Contact = contact;
        }

        public TextPlusMatchableSearchResponse Contact { get; }

    }
}
