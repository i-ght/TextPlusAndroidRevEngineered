using LibCyStd;
using LibCyStd.IO;
using LibCyStd.Net;
using LibCyStd.Seq;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Reactive.Disposables;
using System.Reactive.Subjects;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using System.Xml.Linq;

namespace LibTextPlus.ChatAgent
{
    public class Cfg
    {
        public int MaxAgents { get; set; }
        public int MinDelayBeforeSendingGreet { get; set; }
        public int MaxDelayBeforeSendingGreet { get; set; }
        public int MinGreetsPerSession { get; set; }
        public int MaxGreetsPerSession { get; set; }
        public int MinDelayInMinutesAfterGreetSession { get; set; }
        public int MaxDelayInMinutesAfterGreetSession { get; set; }
        public int MinSendMsgDelayInSeconds { get; set; }
        public int MaxSendMsgDelayInSeconds { get; set; }
        public int MaxConcurrentMessageSends { get; set; }
        public int MaxTotalGreetsToSendPerAcct { get; set; }
        public bool ChatLogEnabled { get; set; }
        public bool DisableGreeting { get; set; }

        public static Cfg Copy(Cfg cfg)
        {
            return new Cfg {
                MaxAgents = cfg.MaxAgents,
                MinDelayBeforeSendingGreet = cfg.MinDelayBeforeSendingGreet,
                MaxDelayBeforeSendingGreet = cfg.MaxDelayBeforeSendingGreet,
                MinGreetsPerSession = cfg.MinGreetsPerSession,
                MaxGreetsPerSession = cfg.MaxGreetsPerSession,
                MinDelayInMinutesAfterGreetSession = cfg.MinDelayInMinutesAfterGreetSession,
                MaxDelayInMinutesAfterGreetSession = cfg.MaxDelayInMinutesAfterGreetSession,
                MinSendMsgDelayInSeconds = cfg.MinSendMsgDelayInSeconds,
                MaxSendMsgDelayInSeconds = cfg.MaxSendMsgDelayInSeconds,
                MaxConcurrentMessageSends = cfg.MaxConcurrentMessageSends,
                MaxTotalGreetsToSendPerAcct = cfg.MaxTotalGreetsToSendPerAcct,
                ChatLogEnabled = cfg.ChatLogEnabled,
                DisableGreeting = cfg.DisableGreeting
            };
        }
    }

    public class Sequences
    {
        public Sequences(
            ContactsStreamReader contacts,
            Queue<AuthTextPlusSession> sessions,
            Queue<Proxy> proxies,
            Queue<string> greets,
            ReadOnlyCollection<string> script,
            Queue<string> links,
            HashSet<string> restrictedExpressions,
            ReadOnlyDictionary<string,
            ReadOnlyCollection<string>> keywordReplies)
        {
            Contacts = contacts;
            Sessions = sessions;
            Proxies = proxies;
            Greets = greets;
            Script = script;
            Links = links;
            RestrictedExpressions = restrictedExpressions;
            KeywordReplies = keywordReplies;
        }

        public ContactsStreamReader Contacts { get; set; }
        public Queue<AuthTextPlusSession> Sessions { get; set; }
        public Queue<Proxy> Proxies { get; set; }
        public Queue<string> Greets { get; set; }
        public ReadOnlyCollection<string> Script { get; set; }
        public Queue<string> Links { get; set; }
        public HashSet<string> RestrictedExpressions { get; set; }
        public ReadOnlyDictionary<string, ReadOnlyCollection<string>> KeywordReplies { get; set; }

        public static Sequences Copy(Sequences seqs)
        {
            return new Sequences(
                seqs.Contacts,
                seqs.Sessions,
                seqs.Proxies,
                seqs.Greets,
                seqs.Script,
                seqs.Links,
                seqs.RestrictedExpressions,
                seqs.KeywordReplies
            );
        }
    }

    public enum ContactConnectionState
    {
        Online,
        Offline
    }

    public class SessionState
    {
        public SessionState(
            AuthTextPlusSession session)
        {
            Session = session;
            ScriptStates = new Dictionary<string, ScriptStateEntitiy>(100);
            Contacts = new Dictionary<string, ContactConnectionState>(10);
            PendingDirectives = new Queue<ChatAgentDirective>(100);
            ScheduledReplies = new Queue<XmppMessage>(100);
        }

        public AuthTextPlusSession Session { get; }
        public Dictionary<string, ScriptStateEntitiy> ScriptStates { get; set; }
        public Dictionary<string, ContactConnectionState> Contacts { get; set; }
        public Queue<ChatAgentDirective> PendingDirectives { get; }
        public Queue<XmppMessage> ScheduledReplies { get; }
        public long GreetsSent { get; set; }
        public long MessagesSent { get; set; }
        public long MessagesRcvd { get; set; }
        public bool SessionInvalid { get; set; }
        public int ConnectionAttempts { get; set; }
    }

    public enum ConnectionState
    {
        Disconnected,
        Connecting,
        Connected
    }

    public enum OutgoingMessageKind
    {
        Greet,
        Reply
    }

    public class AgentState : IDisposable
    {
        public int Index { get; }
        public SessionState SessionState { get; }
        public XmppWebSocketClient Socket { get; }
        public AgentUIUpdater UIUpdater { get; }
        public StatsUIUpdater StatsUIUpdater { get; }
        public SemaphoreSlim SendMessageLock { get; }
        public BufferBlock<ChatAgentDirective> Agent { get; }
        public CancellationTokenSource Disposer { get; }
        public Subject<Briefing> Disconnecter { get; }
        public SessionEntity SessionSQLiteEntity { get; }
        public Blacklist ChatBlacklist { get; }
        public Blacklist GreetBlacklist { get; }
        public Blacklist MsgBlacklist { get; }
        public SemaphoreSlim ConnectionGranter { get; }
        public IDisposable? WhenRcvdStanzaSub { get; set; }
        public bool IsDisposed { get; private set; }
        public ConnectionState ConnectionState { get; set; }
        public Dictionary<string, string> ThreadIds { get; }
        public string Status { get; set; } = "";
        public Cfg Cfg { get; set; }
        public Sequences Seqs { get; set; }
        public Exception? LastErr { get; set; }

        public void Dispose()
        {
            if (IsDisposed) return;
            IsDisposed = true;
            Disposer.Cancel();
            Socket.Dispose();
            WhenRcvdStanzaSub?.Dispose();
            SendMessageLock.Dispose();
            Disposer.Dispose();
            SendMessageLock.Dispose();
        }

        public AgentState(
            int index,
            SessionState sessionState,
            AgentUIUpdater uIUpdater,
            StatsUIUpdater statsUIUpdater,
            SessionEntity sessionSQLiteEntity,
            Blacklist chatBlacklist,
            Blacklist greetBlacklist,
            Blacklist msgBlacklist,
            SemaphoreSlim connectionGranter,
            Cfg cfg,
            Sequences seqs)
        {
            ThreadIds = new Dictionary<string, string>();
            Index = index;
            SessionState = sessionState;
            Socket = new XmppWebSocketClient(sessionState.Session);
            UIUpdater = uIUpdater;
            StatsUIUpdater = statsUIUpdater;
            SendMessageLock = new SemaphoreSlim(cfg.MaxConcurrentMessageSends, cfg.MaxConcurrentMessageSends);
            Agent = new BufferBlock<ChatAgentDirective>();
            Disposer = new CancellationTokenSource();
            Disconnecter = new Subject<Briefing>();
            SessionSQLiteEntity = sessionSQLiteEntity;
            ChatBlacklist = chatBlacklist;
            GreetBlacklist = greetBlacklist;
            MsgBlacklist = msgBlacklist;
            ConnectionGranter = connectionGranter;
            Cfg = cfg;
            Seqs = seqs;
        }
    }

    internal static class AgentModule
    {
#if DEBUG
        public static void Debug(AgentState state, string msg)
        {
            var line = $"{state.Index}~{state.SessionState.Session.Username}: {msg}";
            using var writer = new StreamWriter($"{state.Index}debug.txt", true);
            writer.WriteLine(line);
            Console.Out.WriteLine(line);
        }
#endif

        public static void Warn(AgentState state, string msg)
        {
            var line = $"{state.Index}~{state.SessionState.Session.Username}~WARNING: {msg}";
#if DEBUG
            using var writer = new StreamWriter($"{state.Index}debug.txt", true);
            writer.WriteLine(line);
#endif
            Console.Error.WriteLine(line);
        }

        private static string MsgId()
        {
            using var md5 = new MD5CryptoServiceProvider();
            var hash = md5.ComputeHash(Guid.NewGuid().ToByteArray());
            var sb = new StringBuilder(hash.Length * 2);
            foreach (var b in hash)
                sb.Append(b.ToString("x2"));
            return sb.ToString();
        }

        public static async Task<Unit> SendReply(AgentState state)
        {
            var reply = state.SessionState.ScheduledReplies.Dequeue();
            var scriptState = state.SessionState.ScriptStates[reply.ToJidOnly];

            var link = false;
            try {
                reply.SendAttempts++;
                if (string.IsNullOrWhiteSpace(reply.ThreadId) && state.ThreadIds.ContainsKey(reply.ToJidOnly))
                    reply.ThreadId = state.ThreadIds[reply.ToJidOnly];
                else
                    reply.ThreadId = MsgId();
                var bod = StringModule.Spin(reply.Body);
                if (bod.Contains("%s")) {
                    bod = bod.Replace("%s", Link(state));
                    link = true;
                }
                var data = XmppPackets.SendMessage(reply.To, MsgId(), reply.ThreadId, bod);
                var socket = state.Socket;
                await socket.Send(data, TimeSpan.FromSeconds(30.0)).ConfigureAwait(false);
            }
            catch {
                if (reply.SendAttempts < 3)
                    state.SessionState.ScheduledReplies.Enqueue(reply);
                throw;
            }

            if (link)
                state.StatsUIUpdater.IncrementLinks();
            state.StatsUIUpdater.IncrementOut();
            state.SessionState.MessagesSent++;
            state.UIUpdater.UpdateOut(state.SessionState.MessagesSent.ToString());

            if (!scriptState.CurrentReplyIsInResponseToKw)
                scriptState.ScriptIndex++;
            scriptState.CurrentReplyIsInResponseToKw = false;
            scriptState.Pending = false;
            await ScriptStates.InsertOrReplace(scriptState).ConfigureAwait(false);

            var scr = await Scripts.TryFind(scriptState.ScriptChecksum).ConfigureAwait(false);
            if (scr == default)
                throw new InvalidOperationException($"failed to find script with checksum '{scriptState.ScriptChecksum}'.");

            if (scriptState.ScriptIndex < scr.Lines.Count)
                return Unit.Value;

            state.ChatBlacklist.ThreadSafeAdd(reply.ToJidOnly);
            state.StatsUIUpdater.IncrementCompleted();
            return Unit.Value;

        }

        public static async Task<Unit> SendTyping(AgentState state, string remoteJid)
        {
            var data = XmppPackets.Composing(remoteJid);
#if DEBUG
            Debug(state, $"=> sending typing to {remoteJid} {StringModule.OfSpan(data.Span)}");
#endif
            await state.Socket.Send(data, TimeSpan.FromSeconds(30.0)).ConfigureAwait(false);
            return Unit.Value;
        }

        public static async Task<XElement> RetrIqResp(AgentState state, string iqId, ReadOnlyMemory<byte> data)
        {
#if DEBUG
            Debug(state, $"=> sending iq request {StringModule.OfSpan(data.Span)}");
#endif
            var resp = await state.Socket.RetrieveIqResp(
                iqId,
                data,
                TimeSpan.FromSeconds(30.0)
            ).ConfigureAwait(false);
#if DEBUG
            Debug(state, $"<= rcvd iq response {resp}");
#endif
            //var type = resp.TryFindAttrib(a => a.Name.LocalName == "type");
            //if (type!.Value == "error")
            //    throw new InvalidOperationException($"after sending ping iq request server returned error: {resp}");
            return resp;
        }

        private static async Task AcceptPresenceSubscription(AgentState state, XmppPresence presence)
        {
            var subd = XmppPackets.PresenceSubscribed(presence.FromJidOnly);
            await state.Socket.Send(subd, TimeSpan.FromSeconds(30.0)).ConfigureAwait(false);
            var sub = XmppPackets.PresenceSubscribe(presence.FromJidOnly);
            await state.Socket.Send(sub, TimeSpan.FromSeconds(30.0)).ConfigureAwait(false);
        }

        private static void HandleRosterIqReq(AgentState state, XElement query)
        {
            var items = query.TryFindElems(e => e.Name.LocalName == "item");
            var contacts = state.SessionState.Contacts;
            foreach (var item in items) {
                var jid = TryParseJidFromItemElem(item);
                if (jid == null) {
                    Warn(state, "<item/> element did not contain jid attribute.");
                    continue;
                }
                contacts[jid] = ContactConnectionState.Offline;
            }
        }

        private static string ScriptStateId(AgentState state, string remoteJid)
        {
            return $"{state.SessionState.Session.Jid}~{remoteJid}";
        }

        private static string CleanseIncMsg(string msg)
        {
            var sb = new StringBuilder();
            foreach (var c in msg) {
                if (char.IsWhiteSpace(c) || char.IsLetterOrDigit(c)) {
                    sb.Append(c);
                }
            }
            return Regex.Replace(sb.ToString(), "\\s+", " ").Trim();
        }

        private static readonly Dictionary<string, List<string>> RestrictCache = new Dictionary<string, List<string>>();

        private static void AddRestrictCache(string kw, IEnumerable<string> words)
        {
            lock (RestrictCache)
                RestrictCache.Add(kw, new List<string>(words));
        }

        private static List<string> RestrictWords(string r)
        {
            lock (RestrictCache) {
                if (RestrictCache.ContainsKey(r))
                    return RestrictCache[r];
            }

            var words = r.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            return new List<string>(words);
        }

        private static readonly ReadOnlyCollection<string> RestrictedPhrases = new List<string> {
        }.AsReadOnly();

        private static bool MessageContainsRestrictedExpression(AgentState state, string message)
        {
            var restricts = state.Seqs.RestrictedExpressions;
            var cleansed = CleanseIncMsg(message);
            if (cleansed.InvariantStartsWith("stop")) {
                return true;
            } else if (cleansed.InvariantEquals("please stop")) {
                return true;
            }
            else {
                foreach (var p in RestrictedPhrases)
                    restricts.Add(p);
                var words = message.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var expression in restricts) {
                    var expressionWords = RestrictWords(expression);
                    if (expressionWords.Count == 1 && words.Length == 1) {
                        if (cleansed.InvariantEquals(expression))
                            return true;
                    } else if (expressionWords.Count == 1) {
                        foreach (var word in words) {
                            if (word.InvariantEquals(expression))
                                return true;
                        }
                    } else {
                        if (cleansed.InvariantContains(expression))
                            return true;
                    }
                }

                return false;
            }

        }


        private static readonly Dictionary<string, List<string>> KwCache = new Dictionary<string, List<string>>();

        private static void AddKwCache(string kw, IEnumerable<string> words)
        {
            lock (KwCache) {
                KwCache.Add(kw, new List<string>());
                KwCache[kw].AddRange(words);
            }
        }

        private static List<string> KwWords(string kw)
        {
            lock (KwCache) {
                if (KwCache.ContainsKey(kw))
                    return KwCache[kw];
            }
            var kwWords = kw.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            AddKwCache(kw, kwWords);
            return new List<string>(kwWords);
        }

        private static string? KwResponse(
            AgentState state,
            ScriptStateEntitiy scriptState,
            string messageRcvd)
        {
            if (state.Seqs.KeywordReplies.Keys.Count == 0)
                return default;
            var cleansed = CleanseIncMsg(messageRcvd);
            var words = cleansed.SplitRemoveEmpty(" ");
            foreach (var kw in state.Seqs.KeywordReplies.Keys) {
                if (scriptState.KwsRespondedTo.Contains(kw))
                    continue;
                var kwWords = KwWords(kw);
                if (kwWords.Count == 1 && words.Length == 1) {
                    if (cleansed.InvariantEquals(kw)) {
                        scriptState.KeywordsRespondedTo += $"{kw},";
                        return state.Seqs.KeywordReplies[kw].Random();
                    }
                } else if (kwWords.Count == 1) {
                    foreach (var word in words) {
                        if (word.InvariantEquals(kw)) {
                            scriptState.KeywordsRespondedTo += $"{kw},";
                            return state.Seqs.KeywordReplies[kw].Random();
                        }
                    }
                } else {
                    if (cleansed.InvariantContains(kw)) {
                        scriptState.KeywordsRespondedTo += $"{kw},";
                        return state.Seqs.KeywordReplies[kw].Random();
                    }
                }
            }
            return default;
        }

        private static async Task CalculateReply(AgentState state, ScriptStateEntitiy scriptState, ScriptEntity scr, string messageRcvd)
        {
            var kwReply = KwResponse(state, scriptState, messageRcvd);
            if (kwReply != default) {
                scriptState.CurrentReply = kwReply;
                scriptState.CurrentReplyIsInResponseToKw = true;
            } else {
                scriptState.CurrentReply = scr.Lines[scriptState.ScriptIndex];
                scriptState.CurrentReplyIsInResponseToKw = false;
            }

            scriptState.Pending = true;
            await ScriptStates.InsertOrReplace(scriptState).ConfigureAwait(false);
        }

        private static void UpdateUIOnMsgRcvd(AgentState state, XmppMessage message)
        {
            state.UIUpdater.UpdateIn((++state.SessionState.MessagesRcvd).ToString());
            state.StatsUIUpdater.IncrementIn();
            if (!state.Cfg.ChatLogEnabled)
                return;
        }

        public static XmppMessage? TryParseMsgStanza(AgentState state, XElement stanza)
        {
            var msg = XmppMessage.TryParse(stanza);
            if (msg == null) {
                Warn(state, "failed to parse msg stanza.");
                return default;
            }

            if (msg.Type == "error") {
                Warn(state, $"send message error: {stanza}");
                return default;
            }

            if (string.IsNullOrWhiteSpace(msg.Body)) {
                Warn(state, $"body has no value in message.");
                return default;
            }

            if (msg.Body.InvariantContains("This phone number is no longer in service"))
                return default;

            if (string.IsNullOrWhiteSpace(msg.ThreadId)) {
                if (state.ThreadIds.ContainsKey(msg.FromJidOnly))
                    msg.ThreadId = state.ThreadIds[msg.FromJidOnly];
                else
                    Warn(state, $"thread id has no value in message.");
                //msg.ThreadId = MsgId();
            }
            return msg;
        }

        public static async Task HandleMsgStanza(AgentState state, XmppMessage msg)
        {
            UpdateUIOnMsgRcvd(state, msg);

            if (state.ChatBlacklist.ThreadSafeContains(msg.FromJidOnly))
                return;

            if (MessageContainsRestrictedExpression(state, msg.Body)) {
                state.ChatBlacklist.ThreadSafeAdd(msg.FromJidOnly);
                state.StatsUIUpdater.IncrementRestricts();
                return;
            }

            if (!string.IsNullOrWhiteSpace(msg.ThreadId)) {
                try {
                    await TextPlusApiClient.MarkMsgRead(
                        state.SessionState.Session,
                        state.SessionState.Session.Jid,
                        msg.ThreadId
                    ).ConfigureAwait(false);
                }
                catch (InvalidOperationException) {
                    Warn(state, $"WARNING: failed to mark {msg} as read");
                }
            }

            ScriptStateEntitiy scriptState;
            if (!state.SessionState.ScriptStates.ContainsKey(msg.FromJidOnly)) {
                scriptState = new ScriptStateEntitiy {
                    Id = ScriptStateId(state, msg.FromJidOnly),
                    LocalId = state.SessionState.Session.Jid,
                    RemoteId = msg.FromJidOnly,
                    ScriptChecksum = Scripts.Checksum(state.Seqs.Script),
                    ScriptIndex = 0,
                    Pending = false,
                    CurrentReply = "",
                    ThreadId = msg.ThreadId,
                    KeywordsRespondedTo = "",
                };
                state.SessionState.ScriptStates.Add(msg.FromJidOnly, scriptState);
                await ScriptStates.InsertOrReplace(scriptState).ConfigureAwait(false);
                state.StatsUIUpdater.IncrementConvos();
            } else {
                scriptState = state.SessionState.ScriptStates[msg.FromJidOnly];
            }

            if (scriptState.Pending)
                return;

            var scr = await Scripts.TryFind(scriptState.ScriptChecksum).ConfigureAwait(false);
            if (scr == null)
                throw new InvalidOperationException($"failed to find script with checksum '{scriptState.ScriptChecksum}'");

            if (scriptState.ScriptIndex >= scr.Lines.Count) {
                state.ChatBlacklist.ThreadSafeAdd(msg.FromJidOnly);
                return;
            }

            await CalculateReply(state, scriptState, scr, msg.Body).ConfigureAwait(false);

            var reply = new XmppMessage(
                msg.From,
                state.SessionState.Session.Jid,
                "chat",
                msg.ThreadId,
                scriptState.CurrentReply
            );
            state.SessionState.ScheduledReplies.Enqueue(reply);
            BeginReply(state, reply);
        }

        public static void HandleIqStanza(AgentState state, XElement stanza)
        {
            var iqId = stanza.TryFindAttrib(a => a.Name.LocalName == "id");
            if (iqId == null) {
                Warn(state, $"failed to parse id from iq stanza: {stanza}");
                return;
            }

            var iqType = stanza.TryFindAttrib(a => a.Name.LocalName == "type");
            if (iqType == null) {
                Warn(state, $"failed to parse type from iq stanza: {stanza}");
                return;
            }

            var query = stanza.TryFindElem(s => s.Name.LocalName == "query");
            if (query == null && iqType.Value == "set") {
                Warn(state, $"expected iq type with set to have a query element: {stanza}");
                return;
            } else if (query == null) {
                return;
            }

            var ns = query.TryFindAttrib(a => a.Name.LocalName == "xmlns");
            if (ns == null) {
                Warn(state, "query did not contain expected xmlns attribute.");
                return;
            }

            switch (ns.Value) {
                case "jabber:iq:roster": HandleRosterIqReq(state, query); break;
            }
        }

        private static void HandleContactOnlineState(
            AgentState state,
            XmppPresence presence,
            ContactConnectionState newState)
        {
            if (!state.SessionState.Contacts.ContainsKey(presence.FromJidOnly)) {
                Warn(state, $"expected contacts dictionary to contain jid: {presence.FromJidOnly}");
                return;
            }

            state.SessionState.Contacts[presence.FromJidOnly] = newState;
            UpdateContacts(state);
        }

        public static async Task HandlePresenceStanza(AgentState state, XElement stanza)
        {
            var presence = XmppPresence.TryParse(stanza);
            if (presence == null) {
                Warn(state, $"failed to parse presence stanza: {stanza}");
                return;
            }

            if (presence.FromJidOnly == state.SessionState.Session.Jid)
                return;

            switch (presence.Type) {
                case "available":
                case "" when presence.FromJidOnly != state.SessionState.Session.Jid:
                    HandleContactOnlineState(state, presence, ContactConnectionState.Online); break;
                case "subscribe": await AcceptPresenceSubscription(state, presence).ConfigureAwait(false); break;
                case "unavailable": HandleContactOnlineState(state, presence, ContactConnectionState.Offline); break;
                default: Warn(state, $"unhandled precense stanza: {stanza}"); break;
            }
        }

        public static void Send(AgentState state, ChatAgentDirective directive)
        {
            if (!state.Agent.Post(directive))
                throw new InvalidOperationException("agent failed to post.");
        }

        public static void SendPing(AgentState state)
        {
            var iqId = XmppClientUtils.IqId();
            Send(
                state,
                new RetrIqRespDirective(
                    iqId,
                    XmppPackets.IqGetPing(iqId),
                    TimeSpan.FromSeconds(30.0)
                )
            );
        }

        private static async Task AdvisePing(AgentState state)
        {
            try {
                while (!state.Disposer.IsCancellationRequested) {
                    await Task.Delay(60000, state.Disposer.Token).ConfigureAwait(false);
                    SendPing(state);
                }
            }
            catch (Exception e) when (e is OperationCanceledException) { /*ignored*/ }
            catch (Exception e) {
                Environment.FailFast($"Unhandled exception in PingLoop: {e.GetType().Name} ~ {e.Message}", e);
            }
        }

        private static string Link(AgentState state)
        {
            string link;
            lock (state.Seqs.Links)
                link = state.Seqs.Links.DequeueEnqueue();
            return link;
        }

        private static string? Greet(AgentState state)
        {
            if (state.Seqs.Greets.Count == 0)
                return default;
            string greet;
            lock (state.Seqs.Greets)
                greet = state.Seqs.Greets.DequeueEnqueue();
            greet = StringModule.Spin(greet);
            if (greet.Contains("%s"))
                greet = greet.Replace("%s", Link(state));
            return greet;
        }

        public static async Task<Unit> SendGreet(AgentState state, SendGreetDirective directive)
        {
            await state.SendMessageLock.WaitAsync(state.Disposer.Token).ConfigureAwait(false);

            try {
                var greet = Greet(state);
                if (string.IsNullOrWhiteSpace(greet)) {
                    Warn(state, "no greets loaded.");
                    return Unit.Value;
                }
                var threadId = MsgId();
                state.ThreadIds[directive.Contact.Persona.Jid] = threadId;
                var msg = new XmppMessage(directive.Contact.Persona.Jid, state.SessionState.Session.Jid, "chat", threadId, greet!);
                var sendMsg = XmppPackets.SendMessage(msg.ToJidOnly, MsgId(), threadId, msg.Body);
                await state.Socket.Send(sendMsg, TimeSpan.FromSeconds(30.0)).ConfigureAwait(false);
                state.StatsUIUpdater.IncrementGreets();
                state.SessionState.GreetsSent++;
                state.UIUpdater.UpdateGreets(state.SessionState.GreetsSent.ToString());
                state.GreetBlacklist.ThreadSafeAdd(directive.Contact.Persona.Jid);
            }
            finally {
                state.SendMessageLock.Release();
            }

            return Unit.Value;
        }

        private static async Task DirectReply(AgentState state, XmppMessage msg)
        {
            try {
                // so only n amount of replies can be executing concurrently.
                await state.SendMessageLock.WaitAsync(state.Disposer.Token).ConfigureAwait(false);
                var readingDelaySecs = TimeSpan.FromSeconds(RandomModule.Next(8, 14));
                await Task.Delay(readingDelaySecs, state.Disposer.Token).ConfigureAwait(false);
                Send(state, new SendTypingDirective(msg.To));
                var secs = TimeSpan.FromSeconds(RandomModule.Next(15, 25));
                await Task.Delay(secs, state.Disposer.Token).ConfigureAwait(false);
                Send(state, SendReplyDirective.Value);
            }
            catch (Exception e) when (e is OperationCanceledException) { /*ignored*/ }
            catch (Exception e) {
                Environment.FailFast(
                    $"unhandled exception in DirectReply {e.GetType().Name} ~ {e.Message}",
                    e
                );
            }
            finally {
                state.SendMessageLock.Release();
            }
        }

        public static async void BeginReply(AgentState state, XmppMessage msg)
        {
            await DirectReply(state, msg).ConfigureAwait(false);
        }

        private static async Task AdviseGreets(AgentState state)
        {
            try {
                var index = 0;
                while (!state.Disposer.IsCancellationRequested) {
                    var seconds = RandomModule.Next(state.Cfg.MinDelayBeforeSendingGreet, state.Cfg.MaxDelayBeforeSendingGreet);
                    var ts = TimeSpan.FromSeconds(seconds);
                    await Task.Delay(ts, state.Disposer.Token).ConfigureAwait(false);

                    if (state.SessionState.GreetsSent >= state.Cfg.MaxTotalGreetsToSendPerAcct || state.Cfg.DisableGreeting)
                        continue;

                    var contact = state.Seqs.Contacts.DequeueLine();
                    if (contact == default)
                        continue;

                    Send(state, new SendGreetDirective(contact));

                    index++;
                    if (index >= RandomModule.Next(state.Cfg.MinGreetsPerSession, state.Cfg.MaxGreetsPerSession)) {
                        index = 0;
                        var minutes = RandomModule.Next(state.Cfg.MinDelayInMinutesAfterGreetSession, state.Cfg.MaxDelayInMinutesAfterGreetSession);
                        await Task.Delay(TimeSpan.FromMinutes(minutes), state.Disposer.Token).ConfigureAwait(false);
                    }
                }
            }
            catch (Exception e) when (e is OperationCanceledException) { /*ignored*/ }
            catch (Exception e) {
                Environment.FailFast($"Unhandled exception in greet advisor loop: {e.GetType().Name} ~ {e.Message}", e);
            }
        }

        public static async void BeginGreetTimer(AgentState state)
        {
            await AdviseGreets(state).ConfigureAwait(false);
        }

        public static async void BeginPingTimer(AgentState state)
        {
            await AdvisePing(state).ConfigureAwait(false);
        }

        public static void UpdateId(AgentState state)
        {
            if (string.IsNullOrWhiteSpace(state.SessionState.Session.PhoneNumber))
                state.UIUpdater.UpdateId(state.SessionState.Session.Username);
            else
                state.UIUpdater.UpdateId(state.SessionState.Session.PhoneNumber);
        }

        public static void UpdateStatus(AgentState state, string val)
        {
            state.Status = val;
            state.UIUpdater.UpdateStatus(val);
        }

        public static void UpdateContacts(AgentState state)
        {
            var contacts = state.SessionState.Contacts;
            state.UIUpdater.UpdateContacts(
                $"({contacts.Count(c => c.Value == ContactConnectionState.Online)}/" +
                $"{contacts.Count})"
            );
        }

        private static async Task ProcInbox(AgentState state)
        {
            using var r = await TextPlusApiClient.RetrieveMessages(state.SessionState.Session, DateTimeOffset.UtcNow);
            foreach (var msg in r.Value.Embedded.Messages) {
                if (msg.FromJid == state.SessionState.Session.Jid)
                    continue;
                if (msg.MessageType != "USER")
                    continue;
                if (state.MsgBlacklist.ThreadSafeContains(msg.Id))
                    continue;
                state.MsgBlacklist.ThreadSafeAdd(msg.Id);

                await TextPlusApiClient.MarkMsgRead(state.SessionState.Session, state.SessionState.Session.Jid, msg.ConversationId).ConfigureAwait(false);

                var xMsg = new XmppMessage(state.SessionState.Session.Jid, msg.FromJid, "chat", msg.ConversationId, msg.Body);
                await HandleMsgStanza(state, xMsg).ConfigureAwait(false);
            }
        }

        public static async Task Connected(AgentState state)
        {
            while (state.SessionState.ScheduledReplies.Count > 0)
                BeginReply(state, state.SessionState.ScheduledReplies.Dequeue());

            await ProcInbox(state).ConfigureAwait(false);

            state.ConnectionState = ConnectionState.Connected;
            state.SessionState.ConnectionAttempts = 0;
            BeginPingTimer(state);
            BeginGreetTimer(state);
            UpdateStatus(state, "Connected");
            state.StatsUIUpdater.IncrementOnline();
            if (state.SessionSQLiteEntity.AuthToken != state.SessionState.Session.AuthToken)
                state.SessionSQLiteEntity.AuthToken = state.SessionState.Session.AuthToken;
            state.SessionSQLiteEntity.ValidAt = DateTimeOffsetModule.UnixMillis();
            await Sessions.InsertOrReplace(state.SessionSQLiteEntity).ConfigureAwait(false);
        }

        private static bool ShouldCacheDirective(ChatAgentDirective directive)
        {
            return directive is ProcessStanzaDirective;
        }

        private static void TryEnqueueUnhandledDirectives(AgentState state)
        {
            if (!state.Agent!.TryReceiveAll(out var directives))
                return;
            foreach (var directive in directives) {
                if (ShouldCacheDirective(directive))
                    state.SessionState.PendingDirectives.Enqueue(directive);
            }
        }

        public static void Terminate(
            AgentState state)
        {
            if (state.ConnectionState == ConnectionState.Connected)
                state.StatsUIUpdater.DecrementOnline();
            state.Disconnecter.OnNext(new Briefing(state, state.LastErr!));
            state.SessionState.Contacts.Clear();
            state.ConnectionState = ConnectionState.Disconnected;
            state.Disconnecter.OnCompleted();
            TryEnqueueUnhandledDirectives(state);
            state.Agent.Complete();
            state.Dispose();
        }

        public static async Task ValidateSession(AgentState state)
        {
            using var _ = await TextPlusApiClient.RetrieveLocalInfo(
                state.SessionState.Session
            ).ConfigureAwait(false);
        }

        private static async Task<string> RetrieveTicketGrantingTicket(AgentState state)
        {
            using var resp = await TextPlusApiClient.RequestTicketGranterTicket(
                state.SessionState.Session,
                new TextPlusReqTicketGranterTicketReqContent(state.SessionState.Session.Username, state.SessionState.Session.Password)
            ).ConfigureAwait(false);
            return resp.Value.Ticket;
        }

        private static async Task<string> RetrieveAuthToken(AgentState state, string ticketGrantingTicket)
        {
            using var resp = await TextPlusApiClient.RequestServiceTicket(
                state.SessionState.Session,
                new TextPlusRequestServiceTicketReqContent("nextplus", ticketGrantingTicket)
            ).ConfigureAwait(false);
            return resp.Value.Ticket;
        }

        private static async Task Unsubscribe(AgentState state, string remoteJid)
        {
            var unsub = XmppPackets.PresenceUnSubscribe(remoteJid);
            await state.Socket.Send(unsub, TimeSpan.FromSeconds(30.0)).ConfigureAwait(false);

            var iqId0 = XmppClientUtils.IqId();
            var iq0 = XmppPackets.RemoveRoster(iqId0, remoteJid);
            var resp0 = await RetrIqResp(state, iqId0, iq0).ConfigureAwait(false);
        }

#nullable enable
        private static string? TryParseJidFromItemElem(XElement item)
        {
            var jid = item.TryFindAttrib(a => a.Name.LocalName == "jid");
            if (jid == null)
                return null;
            return jid.Value;
        }
#nullable disable

        private static async Task Roster(AgentState state)
        {
            var iqId = XmppClientUtils.IqId();
            var iq = XmppPackets.Roster(iqId);
            var resp = await RetrIqResp(state, iqId, iq).ConfigureAwait(false);

            var query = resp.TryFindElem(elem => elem.Name.LocalName == "query");
            if (query == null)
                throw new InvalidOperationException("failed to parse roster query.");

            var items = query.TryFindElems(elem => elem.Name.LocalName == "item");
            foreach (var item in items) {
                var jid = TryParseJidFromItemElem(item);
                if (jid == null)
                    continue;
                state.SessionState.Contacts[jid] = ContactConnectionState.Offline;
            }
            UpdateContacts(state);
        }

        private static async Task ConnectXmpp(AgentState state, Action<XElement> stanzaHandler, Action<Exception> stanzaErrHandler)
        {
            var socket = state.Socket;
            state.WhenRcvdStanzaSub = socket.OnRcvdStanza.Subscribe(stanzaHandler, stanzaErrHandler);
            await socket.Connect().ConfigureAwait(false);
            //await socket.Send(XmppPackets.PresenceSubscribe("346e308a70554052a4c8196aeac56d4f@app.nextplus.me"), TimeSpan.FromSeconds(15.0));
            //await Task.Delay(100);
            //await Unsubscribe(state, "346e308a70554052a4c8196aeac56d4f@app.nextplus.me").ConfigureAwait(false);
            await Roster(state).ConfigureAwait(false);
        }

        public static async Task ConnectViaExistingSession(AgentState state, Action<XElement> stanzaHandler, Action<Exception> stanzaErrHandle)
        {
            await ValidateSession(state).ConfigureAwait(true);
            await ConnectXmpp(state, stanzaHandler, stanzaErrHandle).ConfigureAwait(false);
        }

        public static async Task ConnectViaNewSession(AgentState state, Action<XElement> stanzaHandler, Action<Exception> stanzaErrHandle)
        {
            try {
                var ticket = await RetrieveTicketGrantingTicket(state).ConfigureAwait(true);
                state.SessionState.Session.AuthToken = await RetrieveAuthToken(state, ticket).ConfigureAwait(false);
                await ConnectXmpp(state, stanzaHandler, stanzaErrHandle).ConfigureAwait(false);
            }
            catch (TextPlusApiException e) when (
                (int)e.HttpCtx.HttpResp.StatusCode == 400 /*bad request*/ ||
                (int)e.HttpCtx.HttpResp.StatusCode == 401 /*unauthorized*/
            ) {
                //access denied
                e.HttpCtx.Dispose();
                state.SessionState.SessionInvalid = true;
                throw;
            }

        }
    }

    public class Agent : IDisposable
    {
        private readonly AgentState _state;

        public IObservable<Briefing> Disconnected { get; }
        public bool IsDisposed { get; private set; }

        public Cfg Cfg { set { if (!_state.IsDisposed) _state.Agent.Post(new UpdateCfgDirective(value)); } }
        public Sequences Seqs { set { if (!_state.IsDisposed) _state.Agent.Post(new UpdateSeqsDirective(value)); } }

        public void Dispose()
        {
            if (IsDisposed) return;
            IsDisposed = true;
            if (!_state.IsDisposed)
                _state.Disposer.Cancel();
        }

        private void HandleStanza(XElement stanza)
        {
            if (_state.ConnectionState != ConnectionState.Connected)
                return;

            _state.Agent.Post(new ProcessStanzaDirective(stanza));
        }

        private void HandleXmppErr(Exception e)
        {
            _state.Agent.Post(new ErrorDirective(e));
        }

        private async Task<Unit> Connect()
        {
            AgentModule.UpdateId(_state);
            AgentModule.UpdateStatus(_state, "awaiting permission to connect: ...");
            await _state.ConnectionGranter.WaitAsync().ConfigureAwait(false);

            try {
                AgentModule.UpdateStatus(_state, "Connecting: ...");
                _state.SessionState.ConnectionAttempts++;
                _state.ConnectionState = ConnectionState.Connecting;
                var result = false;

                try {
                    await AgentModule.ConnectViaExistingSession(
                        _state,
                        HandleStanza,
                        HandleXmppErr
                    ).ConfigureAwait(false);
                    result = true;
                }
                catch (TextPlusApiException e) when
                    ((int)e.HttpCtx.HttpResp.StatusCode == 401 /*unauthorized*/) {
                    e.HttpCtx.Dispose();
                    await AgentModule.ConnectViaNewSession(
                        _state,
                        HandleStanza,
                        HandleXmppErr
                    ).ConfigureAwait(false);
                    result = true;
                }
                finally {
                    if (result) {
                        while (_state.SessionState.PendingDirectives.Count > 0)
                            await ProcessDirective(_state.SessionState.PendingDirectives.Dequeue()).ConfigureAwait(false);
                        await AgentModule.Connected(_state).ConfigureAwait(false);
                    }
                }

            }
            finally {
                _state.ConnectionGranter.Release();
            }

            return Unit.Value;
        }

        private Unit RaiseErr(ErrorDirective err)
        {
            ExnModule.Reraise(err.Error);
            return Unit.Value;
        }

        private async Task<Unit> HandleStanza(ProcessStanzaDirective directive)
        {
#if DEBUG
            AgentModule.Debug(_state, $"<= {directive.Stanza}");
#endif
            var stanza = directive.Stanza;
            switch (stanza.Name.LocalName) {
                case "presence":
                    await AgentModule.HandlePresenceStanza(_state, stanza).ConfigureAwait(false);
                    break;

                case "iq":
                    AgentModule.HandleIqStanza(_state, stanza);
                    break;

                case "message":
                    var msg = AgentModule.TryParseMsgStanza(_state, stanza);
                    if (msg != default)
                        await AgentModule.HandleMsgStanza(_state, msg).ConfigureAwait(false);
                    break;
            }

            return Unit.Value;
        }

        private async Task<Unit> RetrIqResp(RetrIqRespDirective directive)
        {
            var resp = await AgentModule.RetrIqResp(_state, directive.IqId, directive.Data).ConfigureAwait(false);
            directive.ResponseHandler?.Invoke(resp);
            return Unit.Value;
        }

        private Unit SetSeqs(UpdateSeqsDirective d)
        {
            _state.Seqs = d.Seqs;
            return Unit.Value;
        }

        private Unit SetCfg(UpdateCfgDirective cfg)
        {
            _state.Cfg = cfg.Cfg;
            return Unit.Value;
        }

        private async Task ProcessDirective(ChatAgentDirective directive)
        {
            _ = directive switch
            {
                ConnectDirective _ => await Connect().ConfigureAwait(false),
                RetrIqRespDirective iq => await RetrIqResp(iq).ConfigureAwait(false),
                ErrorDirective err => RaiseErr(err),
                ProcessStanzaDirective stanza => await HandleStanza(stanza).ConfigureAwait(false),
                UpdateSeqsDirective seqs => SetSeqs(seqs),
                UpdateCfgDirective cfg => SetCfg(cfg),
                SendTypingDirective st => await AgentModule.SendTyping(_state, st.RemoteJid).ConfigureAwait(false),
                SendReplyDirective _ => await AgentModule.SendReply(_state).ConfigureAwait(false),
                SendGreetDirective s => await AgentModule.SendGreet(_state, s).ConfigureAwait(false),
                _ => throw new ArgumentException("invalid directive")
            };
        }

        private async Task<bool> IterateRecvLoop()
        {
            try {
                var directive = await _state.Agent.ReceiveAsync(_state.Disposer.Token).ConfigureAwait(false);
                await ProcessDirective(directive).ConfigureAwait(true);
                return true;
            }
            catch (Exception e) when
                (e is InvalidOperationException
                || e is TimeoutException
                || e is WebSocketException
                || e is SQLiteException
                || e is IOException
                || e is SocketException) {
                AgentModule.UpdateStatus(_state, $"Disconnected | Error occured: {e.GetType().Name} ~ {e.Message}");
                _state.LastErr = e;
            }
            return false;
        }

        private async void Recv()
        {
            try {
                while (!_state.Disposer.IsCancellationRequested) {
                    if (!await IterateRecvLoop().ConfigureAwait(false))
                        return;
                }
            }
            catch (OperationCanceledException) {/*ignored*/}
            catch (Exception e) {
                Environment.FailFast(
                    $"Unhandled exception in Agent recv loop: {e.GetType().Name} ~ {e.Message}",
                    e
                );
            }
            finally {
                AgentModule.Terminate(_state);
                if (!IsDisposed) IsDisposed = true;
            }
        }

        public void Exe()
        {
            if (!_state.Agent.Post(ConnectDirective.Value))
                throw new InvalidOperationException("agent post directive failed");
        }

        public Agent(AgentState state)
        {
            _state = state;
            Disconnected = _state.Disconnecter;
            Recv();
        }
    }

    public class DirectorState : IDisposable
    {
        public int ActiveAgents { get; set; }
        public Dictionary<int, Agent> Agents { get; }
        public Dictionary<string, SessionState> SessionStates { get; }
        public Cfg Cfg { get; set; }
        public Sequences Sequences { get; set; }
        public Dictionary<int, IDisposable> DeadDrop { get; }
        public StatsUIUpdater StatsUIUpdater { get; }
        public ReadOnlyDictionary<int, AgentUIUpdater> AgentUIUpdaters { get; }
        public Blacklist ChatBlacklist { get; }
        public Blacklist GreetBlacklist { get; }
        public Blacklist MsgBlacklist { get; }
        public SemaphoreSlim ConnectionGranter { get; }
        public CancellationTokenSource Disposer { get; }
        public AsyncSubject<Unit> WorkComplete { get; }
        public BufferBlock<DirectorDirective> Agent { get; }
        public Dictionary<string, List<ScriptStateEntitiy>> ScriptStates { get; }

        public bool IsDisposed { get; private set; }

        void IDisposable.Dispose()
        {
            if (IsDisposed) return;
            IsDisposed = true;
            Disposer.Cancel();
            ConnectionGranter.Dispose();
            Disposer.Dispose();
            WorkComplete.Dispose();
        }

        public DirectorState(
            int activeAgents,
            Cfg cfg,
            Sequences sequences,
            StatsUIUpdater statsUIUpdater,
            ReadOnlyDictionary<int, AgentUIUpdater> agentUIUpdaters,
            Blacklist chatBlacklist,
            Blacklist greetBlacklist,
            Blacklist msgBlacklist,
            int maxConcurrentConnections,
            Dictionary<string, List<ScriptStateEntitiy>> scriptStates)
        {
            ActiveAgents = activeAgents;
            Agents = new Dictionary<int, Agent>(activeAgents);
            SessionStates = new Dictionary<string, SessionState>(activeAgents);
            Cfg = cfg;
            Sequences = sequences;
            DeadDrop = new Dictionary<int, IDisposable>(activeAgents);
            StatsUIUpdater = statsUIUpdater;
            AgentUIUpdaters = agentUIUpdaters;
            ChatBlacklist = chatBlacklist;
            GreetBlacklist = greetBlacklist;
            MsgBlacklist = msgBlacklist;
            ConnectionGranter = new SemaphoreSlim(maxConcurrentConnections, maxConcurrentConnections);
            Disposer = new CancellationTokenSource();
            WorkComplete = new AsyncSubject<Unit>();
            Agent = new BufferBlock<DirectorDirective>();
            ScriptStates = scriptStates;
        }
    }

    internal static class DirectorModule
    {
        public static void Post(DirectorState state, DirectorDirective d)
        {
            if (!state.Agent.Post(d))
                throw new InvalidOperationException("failed to post directive");
        }

        public static Unit ExecDirectiveIn(
            DirectorState state,
            TimeSpan t,
            DirectorDirective d)
        {
            async void Send()
            {
                try {
                    await Task.Delay(t, state.Disposer.Token).ConfigureAwait(false);
                    Post(state, d);
                }
                catch (OperationCanceledException) { }
            }
            Send();
            return Unit.Value;
        }

        public static Unit SetCfg(DirectorState state, SetCfgDirective directive)
        {
            state.Cfg = directive.Cfg;
            foreach (var agent in state.Agents.Values)
                agent.Cfg = directive.Cfg;
            return Unit.Value;
        }

        public static Unit GetCfg(DirectorState state, GetCfgDirective directive)
        {
            directive.Reply(state.Cfg);
            return Unit.Value;
        }

        public static Unit SetSeqs(DirectorState state, SetSeqsDirective directive)
        {
#nullable enable
            ContactsStreamReader? oldContacts = null;
#nullable disable
            if (state.Sequences.Contacts != directive.Seqs.Contacts)
                oldContacts = state.Sequences.Contacts;
            state.Sequences = directive.Seqs;
            oldContacts?.Dispose();

            foreach (var agent in state.Agents.Values)
                agent.Seqs = directive.Seqs;

            return Unit.Value;
        }

        public static Unit GetSeqs(DirectorState state, GetSeqsDirective directive)
        {
            directive.Reply(state.Sequences);
            return Unit.Value;
        }

        private static AgentState AgentState(
            DirectorState state,
            AuthTextPlusSession session,
            int index,
            SessionEntity entity)
        {
            session.Proxy = state.Sequences.Proxies.DequeueEnqueue();

            SessionState sessionState;
            if (state.SessionStates.ContainsKey(session.Jid)) {
                sessionState = state.SessionStates[session.Jid];
            } else {
                sessionState = new SessionState(session);
                state.SessionStates.Add(session.Jid, sessionState);
                if (state.ScriptStates.ContainsKey(session.Jid)) {
                    foreach (var s in state.ScriptStates[session.Jid]) {
                        sessionState.ScriptStates.Add(s.RemoteId, s);
                    }
                }
            }
            return new AgentState(
                index: index,
                sessionState: sessionState,
                uIUpdater: state.AgentUIUpdaters[index],
                statsUIUpdater: state.StatsUIUpdater,
                sessionSQLiteEntity: entity,
                chatBlacklist: state.ChatBlacklist,
                greetBlacklist: state.GreetBlacklist,
                msgBlacklist: state.MsgBlacklist,
                connectionGranter: state.ConnectionGranter,
                cfg: state.Cfg,
                seqs: state.Sequences
            );
        }

        public static async Task<Unit> ActivateAgent(
            DirectorState state,
            ActivateAgentDirective directive)
        {
            var session = state.Sequences.Sessions.Dequeue();
            var sessionEntity = await Sessions.TryFind(session.Jid).ConfigureAwait(false);
            if (sessionEntity == null) {
                sessionEntity = new SessionEntity {
                    AuthToken = session.AuthToken,
                    Invalid = false,
                    Jid = session.Jid,
                };
            }

            if (session.AuthToken != sessionEntity.AuthToken)
                session.AuthToken = sessionEntity.AuthToken;

            var agentState = AgentState(state, session, directive.Index, sessionEntity);

            var agent = new Agent(agentState);
            state.Agents[directive.Index] = agent;

            state.DeadDrop[directive.Index] = agent.Disconnected.Subscribe(briefing =>
                Post(state, new AgentBriefingDirective(briefing))
            );

            agent.Exe();

            return Unit.Value;
        }

        private static bool AgentStateIsValid(AgentState state)
        {
            if (state.SessionState.ConnectionAttempts >= 5 || state.SessionState.SessionInvalid)
                return false;

            return true;
        }

        public static Unit DebriefAgent(
            DirectorState state,
            AgentBriefingDirective directive)
        {
            if (directive.Briefing.Error != null) {
                using var writer = new StreamWriter("errors.txt", true);
                writer.WriteLine(directive.Briefing.Error.ToLogMessage());
            }

            if (directive.Briefing.Error is TextPlusApiException e)
                e.HttpCtx.Dispose();

            state.DeadDrop[directive.Briefing.State.Index].Dispose();
            state.DeadDrop[directive.Briefing.State.Index] = Disposable.Empty;

            if (!state.Agents.Remove(directive.Briefing.State.Index)) {
                throw new InvalidOperationException(
                    $"failed to remove agent with index {directive.Briefing.State.Index}"
                );
            }

            if (AgentStateIsValid(directive.Briefing.State)) {
                state.Sequences.Sessions.Enqueue(directive.Briefing.State.SessionState.Session);
                ExecDirectiveIn(
                    state,
                    TimeSpan.FromSeconds(9.1),
                    new ActivateAgentDirective(directive.Briefing.State.Index)
                );
            } else {
                state.ActiveAgents--;
            }

            return Unit.Value;
        }
    }

    public class Director : IDisposable
    {
        private readonly DirectorState _state;

        public IObservable<Unit> WorkCompleted { get; }
        public bool IsDisposed { get; private set; }

        public Task<Sequences> Sequences
        {
            get {
                var tcs = new TaskCompletionSource<Sequences>();
                DirectorModule.Post(_state, new GetSeqsDirective(sequences => tcs.SetResult(sequences)));
                return tcs.Task;
            }
            set => DirectorModule.Post(_state, new SetSeqsDirective(value.Result));
        }

        public Task<Cfg> Cfg
        {
            get {
                var tcs = new TaskCompletionSource<Cfg>();
                DirectorModule.Post(_state, new GetCfgDirective(cfg => tcs.SetResult(cfg)));
                return tcs.Task;
            }
            set => DirectorModule.Post(_state, new SetCfgDirective(value.Result));
        }

        public void Dispose()
        {
            if (IsDisposed) return;
            IsDisposed = true;
            if (!_state.IsDisposed)
                _state.Disposer.Cancel();
        }

        private async Task ProcessDirective(DirectorDirective directive)
        {
            _ = directive switch
            {
                SetCfgDirective d => DirectorModule.SetCfg(_state, d),
                GetCfgDirective d => DirectorModule.GetCfg(_state, d),
                SetSeqsDirective d => DirectorModule.SetSeqs(_state, d),
                GetSeqsDirective d => DirectorModule.GetSeqs(_state, d),
                ActivateAgentDirective d => await DirectorModule.ActivateAgent(_state, d).ConfigureAwait(false),
                AgentBriefingDirective d => DirectorModule.DebriefAgent(_state, d),
                _ => throw new ArgumentException("Invalid director directive.")
            };
        }

        private void Terminate()
        {
            _state.WorkComplete.OnNext(Unit.Value);
            _state.WorkComplete.OnCompleted();
            _state.Agent.Complete();

            foreach (var idisp in _state.DeadDrop.Values)
                idisp.Dispose();

            foreach (var agent in _state.Agents.Values)
                agent.Dispose();

            using var _ = _state;
        }

        private async void Recv()
        {
            try {
                while (_state.ActiveAgents > 0 && !_state.Disposer.IsCancellationRequested) {
                    var directive = await _state.Agent.ReceiveAsync(_state.Disposer.Token).ConfigureAwait(false);
                    await ProcessDirective(directive).ConfigureAwait(true);
                }
            }
            catch (OperationCanceledException) {/*ignored*/}
            catch (Exception e) {
                Environment.FailFast(
                    $"Unhandled exception occured in director: {e.GetType().Name} ~ {e.Message}",
                    e
                );
            }

            Terminate();
            if (!IsDisposed) IsDisposed = true;
        }

        public void ActivateAgents(int cnt)
        {
            for (var i = 0; i < cnt; i++)
                _state.Agent.Post(new ActivateAgentDirective(i));
        }

        public Director(DirectorState initialState)
        {
            _state = initialState;
            WorkCompleted = initialState.WorkComplete;
            Recv();
        }
    }
}
