using LibCyStd;
using LibCyStd.Net;
using LibCyStd.Seq;
using LibCyStd.Tasks;
using Newtonsoft.Json;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.WebSockets;
using System.Reactive.Disposables;
using System.Reactive.Subjects;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;

namespace LibTextPlus.Creator
{
    using StrKvp = ValueTuple<string, string>;

    public class GcmReqInfo
    {
        public GcmReqInfo(ReadOnlyCollection<(string, string)> headers, Dictionary<string, string> content)
        {
            Headers = headers;
            Content = content;
        }

        public ReadOnlyCollection<StrKvp> Headers { get; }
        public Dictionary<string, string> Content { get; }
    }

    public enum CreatorState
    {
        None,
        Created,
        GotPhoneNumber
    }

    public enum GcmProviderMethod
    {
        File,
        Request
    }

    public class Cfg
    {
        public int MaxWorkers { get; }
        public int MaxCreates { get; }
        public int MobileCountryCode { get; }
        public bool GetTextPlusNumber { get; }
        public GcmProviderMethod GcmProviderMethod { get; }
        public Option<GcmReqInfo> GcmReq { get; }
        public bool ConnectJabber { get; }
        public Cfg(int maxWorkers, int maxCreates, int mobileCountryCode, bool getTextPlusNumber, GcmProviderMethod gcmProviderMethod, Option<GcmReqInfo> gcmReq, bool connectJabber)
        {
            MaxWorkers = maxWorkers;
            MaxCreates = maxCreates;
            MobileCountryCode = mobileCountryCode;
            GetTextPlusNumber = getTextPlusNumber;
            GcmProviderMethod = gcmProviderMethod;
            GcmReq = gcmReq;
            ConnectJabber = connectJabber;
        }
    }

    public class Seqs
    {
        public Queue<TextPlusAndroidDevice> AndroidDevices { get; }
        public Queue<Proxy> Proxies { get; }
        public Queue<string> Words1 { get; }
        public Queue<string> Words2 { get; }
        public Queue<string> GcmTokens { get; }

        public Seqs(
            Queue<TextPlusAndroidDevice> androidDevices,
            Queue<Proxy> proxies,
            Queue<string> words1,
            Queue<string> words2,
            Queue<string> gcmTokens)
        {
            AndroidDevices = androidDevices;
            Proxies = proxies;
            Words1 = words1;
            Words2 = words2;
            GcmTokens = gcmTokens;
        }
    }

    public class AgentProps
    {
        public int Index { get; }
        public AgentUIUpdater UIUpdater { get; }
        public StatsUIUpdater StatsUIUpdater { get; }
        public CancellationToken CancellationToken { get; }
        public VerifyClientProvider VerifyClientProvider { get; }
        public string Username { get; }
        public string Password { get; }
        public string GcmToken { get; }
        public Option<GcmReqInfo> GcmTokenReq { get; }

        public AgentProps(
            int index,
            AgentUIUpdater uIUpdater,
            StatsUIUpdater statsUIUpdater,
            CancellationToken cancellationToken,
            VerifyClientProvider verifyClientProvider,
            string username,
            string password,
            string gcmToken,
            Option<GcmReqInfo> gcmTokenReq)
        {
            Index = index;
            UIUpdater = uIUpdater;
            StatsUIUpdater = statsUIUpdater;
            CancellationToken = cancellationToken;
            VerifyClientProvider = verifyClientProvider;
            Username = username;
            Password = password;
            GcmToken = gcmToken;
            GcmTokenReq = gcmTokenReq;
        }
    }

#pragma warning disable CS8618 // Non-nullable field is uninitialized.
    public class AgentState
    {
        public Cfg Cfg { get; set; }
        public AnonTextPlusSession AnonSession { get; set; }
        public AuthTextPlusSession AuthSession { get; set; }
        public string IssuedUsername { get; set; } = string.Empty;
        public string IssuedUserId { get; set; } = string.Empty;
        public Option<VerifyClient> VerifyClient { get; set; } = Option.None;
        public string TicketGrantingTicket { get; set; } = string.Empty;
        public string AuthTicket { get; set; } = string.Empty;
        public ReadOnlyCollection<TptnLocale> UsLocales { get; set; }
        public string VerificationCode { get; set; } = "";
        public CreatorState CreatorState { get; set; } = CreatorState.None;
        public string Status { get; set; } = "";
    }
#pragma warning restore CS8618 // Non-nullable field is uninitialized.

    public abstract class Briefing
    {
        public AgentProps Props { get; }
        public AgentState State { get; }

        protected Briefing(
            AgentProps props,
            AgentState state)
        {
            Props = props;
            State = state;
        }
    }

    public class ObjectiveCompletedBriefing : Briefing
    {
        public ObjectiveCompletedBriefing(
            AgentProps props,
            AgentState state) : base(props, state)
        {
        }
    }

    public class ObjectiveFailedBriefing : Briefing
    {
        public Exception Error { get; }

        public ObjectiveFailedBriefing(
            AgentProps props,
            AgentState state,
            Exception error) : base(props, state)
        {
            Error = error;
        }
    }

    public class AgentConfig
    {
        public int Index { get; }
        public Cfg Cfg { get; }
        public Seqs Seqs { get; }
        public AgentUIUpdater AgentUIUpdater { get; }
        public StatsUIUpdater StatsUIUpdater { get; }
        public CancellationToken CancellationToken { get; }
        public VerifyClientProvider VerifyClientProvider { get; }
        public Option<GcmReqInfo> GcmReqInfo { get; }

        public AgentConfig(
            int index,
            Cfg cfg,
            Seqs seqs,
            AgentUIUpdater agentUIUpdater,
            StatsUIUpdater statsUIUpdater,
            CancellationToken cancellationToken,
            VerifyClientProvider verifyClientProvider,
            Option<GcmReqInfo> gcmReqInfo)
        {
            Index = index;
            Cfg = cfg;
            Seqs = seqs;
            AgentUIUpdater = agentUIUpdater;
            StatsUIUpdater = statsUIUpdater;
            CancellationToken = cancellationToken;
            VerifyClientProvider = verifyClientProvider;
            GcmReqInfo = gcmReqInfo;
        }
    }

    // exists to complete an objective
    public class Agent : IDisposable
    {
        private readonly Subject<Briefing> _whenObjAttmptCompleted;
        private readonly AgentProps _props;
        private readonly AgentState _state;

        public IObservable<Briefing> WhenObjectiveAttemptCompleted { get; }

        private void UpdateStatus(string status)
        {
            _state.Status = status;
            _props.UIUpdater.UpdateStatus(status);
        }

        public void Dispose()
        {
            _whenObjAttmptCompleted.OnCompleted();
            _whenObjAttmptCompleted.Dispose();
        }

        private static string GenerateAppId() => StringModule.Rando(Chars.DigitsAndLetters, 11);

        private async Task<VerifyClient> VerifyClient()
        {
            UpdateStatus("Getting verify client: ...");
            var vc = _props.VerifyClientProvider;
            var verifierClientResult = vc.IsAsync
                ? await vc.TryGetVerifyClientAsync().ConfigureAwait(false)
                : vc.TryGetVerifyClient();
            return verifierClientResult switch
            {
                (true, var c) => c,
                _ => throw new InvalidOperationException("failed to get or out of account verifier clients.")
            };
        }

        private void ValidateVerifier()
        {
            _ = _state.VerifyClient.Value switch
            {
                ConsoleVerifyClient _ => Unit.Value,
                AlwaysValidVerifyClient _ => Unit.Value,
                _ => throw new InvalidOperationException("cannot validate verifier client")
            };
        }

        private async Task Register()
        {
            UpdateStatus("Submitting registration info: ...");

            var devInfo = new TextPlusRegisterDevInfo {
                platformOSVersion = _state.AnonSession.Device.OsVersion,
                appName = "textplus",
                appVersion = Constants.VersionShort,
                deviceUDID = _state.AnonSession.AndroidId,
                model = _state.AnonSession.Device.Model,
                platform = "google",
                pushEnabled = true,
                pushTokenType = "ANDROID_GCM",
                pushType = 2
            };
            var regInfo = new TextPlusRegisterReqContent(
                "",
                "US",
                devInfo,
                "en_US",
                Constants.Network,
                1,
                _props.Password,
                1,
                _props.Username
            );
            using var result = await TextPlusApiClient.Register(_state.AnonSession, regInfo).ConfigureAwait(false);
            if (!result.HttpCtx.HttpResp.Headers.ContainsKey("location") || result.HttpCtx.HttpResp.Headers["location"].Count == 0)
                ExnModule.InvalidOp("did not find expected 'location' header in register response.");
            var loc = result.HttpCtx.HttpResp.Headers["location"][0];
            var sp = loc.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            _state.IssuedUserId = sp.Last();
            var response = result.Value;
            _state.IssuedUsername = response.Username;
        }

        private async Task ReqTicketGrantingTicket()
        {
            UpdateStatus("Getting ticket granting ticket: ...");
            using var result = await TextPlusApiClient.RequestTicketGranterTicket(
                _state.AnonSession,
                new TextPlusReqTicketGranterTicketReqContent(_state.IssuedUsername, _props.Password)
            ).ConfigureAwait(false);
            var response = result.Value;
            _state.TicketGrantingTicket = response.Ticket;
        }

        private async Task<string> GcmToken()
        {
            if (_state.Cfg.GcmProviderMethod == GcmProviderMethod.File || !_state.Cfg.GetTextPlusNumber)
                return _props.GcmToken;

            var contentDict = _props.GcmTokenReq.Value.Content.ToDictionary(k => k.Key, v => v.Value);
            contentDict["X-appid"] = GenerateAppId();

            var contentLst = new List<StrKvp>(contentDict.Count);
            foreach (var kvp in contentDict)
                contentLst.Add((kvp.Key, kvp.Value));

            using var content = new EncodedFormValuesHttpContent(contentLst);
            var req = new HttpReq("POST", "https://android.clients.google.com/c2dm/register3")
            {
                Headers = _props.GcmTokenReq.Value.Headers,
                ContentBody = content,
                Proxy = _state.AnonSession.Proxy
            };

            using var resp = await HttpModule.RetrRespAsync(req).ConfigureAwait(false);
            if (resp.StatusCode != LibCyStd.HttpStatusCode.OK || !resp.Content.InvariantStartsWith("token="))
                ExnModule.InvalidOp($"failed to retrieve gcm token. server returned unexpected response. status code is {resp.StatusCode}.");

            return resp.Content.Replace("token=", "");
        }

        private async Task ReqServiceTicket()
        {
            UpdateStatus("Getting service ticket: ...");
            using var result = await TextPlusApiClient.RequestServiceTicket(
                _state.AnonSession,
                new TextPlusRequestServiceTicketReqContent("nextplus", _state.TicketGrantingTicket)
            ).ConfigureAwait(false);
            var response = result.Value;
            _state.AuthTicket = response.Ticket;
            _state.AuthSession = new AuthTextPlusSession(
                _state.AnonSession.Device,
                _state.AnonSession.Proxy,
                _state.AnonSession.AndroidId,
                _state.IssuedUsername,
                _props.Password,
                _state.AuthTicket,
                await GcmToken().ConfigureAwait(false),
                "",
                "",
                "",
                ""
            );
        }
        private async Task RetrLocalUserInfo11()
        {
            UpdateStatus("Retrieving local user info: ...");
            using var result = await TextPlusApiClient.RetrLocalInfo11(_state.AuthSession, _state.IssuedUserId).ConfigureAwait(false);
            var response = result.Value;
            var id = response.Id;
            var primaryPersonaId = response.PrimaryPersona.Id;
            var jid = response.PrimaryPersona.Jid;

            _state.AuthSession = new AuthTextPlusSession(
                _state.AnonSession.Device,
                _state.AnonSession.Proxy,
                _state.AnonSession.AndroidId,
                _state.IssuedUsername,
                _props.Password,
                _state.AuthTicket,
                _state.AuthSession.PushToken,
                id,
                primaryPersonaId,
                phoneNumber: "",
                jid
            );

            _state.CreatorState = CreatorState.Created;
        }

        //private async Task RetrLocalUserInfo()
        //{
        //    UpdateStatus("Retrieving local user info: ...");
        //    using var result = await TextPlusApiClient.RetrieveLocalInfo(_state.AuthSession).ConfigureAwait(false);
        //}

        private async Task RetrieveLocales()
        {
            UpdateStatus("Retrieving locales: ...");
            using var result = await TextPlusApiClient.RetrieveLocales(_state.AuthSession).ConfigureAwait(false);
            var response = result.Value;
            if (response.TptnLocales.Count == 0)
                ExnModule.InvalidOp("textplus api server returned 0 locales.");
            var usLocales = response.TptnLocales.Where(l => l.Country == "US" && l.Available);
            if (!usLocales.Any())
                ExnModule.InvalidOp("texytplus api server returned 0 US locales.");
            _state.UsLocales = ReadOnlyCollectionModule.OfSeq(usLocales);
        }

        private async Task ReqVerificationCode()
        {
            UpdateStatus("Requesting verification code: ...");
            var vClient = _state.VerifyClient.Value;
            using var _ = await TextPlusApiClient.RequestVerificationCode(
                _state.AuthSession,
                new TextPlusSendVerificationInfoReqContent($"https://ums.prd.gii.me/users/{_state.AuthSession.UserId}", vClient.Id, vClient.VerifyMethod)
            ).ConfigureAwait(false);
        }

        private async Task GetVerificationCode()
        {
            UpdateStatus("Waiting for verification code: ...");
            var vClient = _state.VerifyClient.Value;
            async Task FindCode()
            {
                while (true)
                {
                    var result = await vClient.TryGetCode().ConfigureAwait(false);
                    if (result.IsNone)
                    {
                        await Task.Delay(900).ConfigureAwait(false);
                        continue;
                    }

                    _state.VerificationCode = result.Value;
                    return;
                }
            }

            try
            {
                await FindCode().TimeoutAfter(TimeSpan.FromSeconds(120.0)).ConfigureAwait(false);
            }
            catch (TimeoutException e)
            {
                throw new WaitForCodeTimeOutException("timed out waiting for verification code.", e);
            }
        }

        private async Task SubmitVerification()
        {
            UpdateStatus("Submitting verification: ...");
            using var result = await TextPlusApiClient.VerifyPhoneCode(
                _state.AuthSession,
                _state.VerifyClient.Value.Id,
                _state.VerificationCode
            ).ConfigureAwait(false);
        }

        private async Task SubmitDeviceInfo()
        {
            UpdateStatus("Submitting device info: ...");
            if (string.IsNullOrWhiteSpace(_state.AuthSession.PushToken))
                return;
            
            using var _ = await TextPlusApiClient.SendDeviceInfo(
                _state.AuthSession,
                new TextPlusSendDeviceReqContent(
                    _state.AuthSession.Device.OsVersion,
                    "textplus",
                    Constants.VersionShort,
                    _state.AuthSession.AndroidId,
                    _state.AuthSession.Device.Model,
                    "google",
                    true,
                    _state.AuthSession.PushToken,
                    "ANDROID_GCM",
                    2,
                    $"https://ums.prd.gii.me/users/{_state.AuthSession.UserId}"
                )
            ).ConfigureAwait(false);
        }

        private async Task GetPhoneNumber()
        {
            var locale = _state.UsLocales.Random();

            UpdateStatus($"Getting phone number in locale {locale}: ...");

            //var sln = await TwoCaptchaModule.SolveRecaptchav2("bf6508d23a4c6a1bbc52311df313c47e", "6Lfg_WkUAAAAAJs-uaZ32JxY6Q7A2SicSLTW2heg", "com.gogii.textplus", TimeSpan.FromSeconds(300), false, _state.AuthSession.Proxy);

            using var result = await TextPlusApiClient.AllocPersona(_state.AuthSession, new TextPlusAllocPersonaReqContent(
                _state.AuthSession.AndroidId,
                locale.LocaleId,
                "google",
                _state.AuthSession.PushToken//,
                /*sln*/)
            ).ConfigureAwait(false);

            _state.AuthSession.PhoneNumber = result.Value.PhoneNumber;
            _state.CreatorState = CreatorState.GotPhoneNumber;
        }

        private async Task ConnectWebSocket()
        {
            using var client = new System.Net.WebSockets.Managed.ClientWebSocket();
            client.Options.Proxy = new WebProxy(_state.AuthSession.Proxy.Uri);
            client.Options.AddSubProtocol("xmpp");
            client.Options.AddSubProtocol("xmpp-framing");
            
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30.0));
            await client.ConnectAsync(new Uri("wss://xmpp.prd.gii.me/"), cts.Token).ConfigureAwait(false);

            var open = XmppPackets.Open(_state.AuthSession);
            await client.SendAsync(open.AsArraySeg(), WebSocketMessageType.Text, true, cts.Token).ConfigureAwait(false);

            using var mem = MemoryPool<byte>.Shared.Rent(2048);
            var seg = ((ReadOnlyMemory<byte>)mem.Memory).AsArraySeg();

            WebSocketReceiveResult recvResult;
            string txt;
            for (var i = 0; i < 2; i++)
            {
                recvResult = await client.ReceiveAsync(seg, cts.Token).ConfigureAwait(false);
                if (i == 0)
                    continue;
                txt = Encoding.UTF8.GetString(seg.Array, 0, recvResult.Count);
                if (!txt.Contains("PLAIN"))
                    ExnModule.InvalidOp("Failed connecting to xmpp server: unexpected response after sending <open/>");
            }

            var auth = XmppPackets.Auth(_state.AuthSession);
            await client.SendAsync(auth.AsArraySeg(), WebSocketMessageType.Text, true, cts.Token).ConfigureAwait(false);
            recvResult = await client.ReceiveAsync(seg, cts.Token).ConfigureAwait(false);
            txt = Encoding.UTF8.GetString(seg.Array, 0, recvResult.Count);
            if (!txt.StartsWith("<success"))
                ExnModule.InvalidOp("xmpp server auth failed.");
        }

        public async Task Exec()
        {
            try
            {
                _props.StatsUIUpdater.IncrementAttempts();
                _props.UIUpdater.UpdateId(_props.Username);

                _state.VerifyClient = _state.VerifyClient.IsNone
                    ? await VerifyClient().ConfigureAwait(false)
                    : _state.VerifyClient;

                ValidateVerifier();

                await Register().ConfigureAwait(false);
                await ReqTicketGrantingTicket().ConfigureAwait(false);
                await ReqServiceTicket().ConfigureAwait(false);
                await RetrLocalUserInfo11().ConfigureAwait(false);
                await SubmitDeviceInfo().ConfigureAwait(false);

                if (_state.Cfg.GetTextPlusNumber)
                {
                    await RetrieveLocales().ConfigureAwait(false);
                    //await ReqVerificationCode().ConfigureAwait(false);
                    //await GetVerificationCode().ConfigureAwait(false);
                    //await SubmitVerification().ConfigureAwait(false);
                    await GetPhoneNumber().ConfigureAwait(false);
                }

                if (_state.Cfg.ConnectJabber) {
                    UpdateStatus("Connecting to jabber server: ...");
                    await ConnectWebSocket().ConfigureAwait(false);
                }
                UpdateStatus("Account created");
#if DEBUG
                Console.Out.WriteLine(_state.AuthSession.Proxy);
#endif
                _whenObjAttmptCompleted.OnNext(new ObjectiveCompletedBriefing(_props, _state));
            }
            catch (Exception e) when (
                e is InvalidOperationException
                || e is TimeoutException
                || e is OperationCanceledException
                || e is WebSocketException)
            {
                UpdateStatus($"{e.GetType().Name} occured ~ {e.Message}");
                _whenObjAttmptCompleted.OnNext(new ObjectiveFailedBriefing(_props, _state, e));
            }
            catch (Exception e) {
                Environment.FailFast($"Unhandled exception in agent: {e.GetType().Name} ~ {e.Message}.", e);
            }
        }

        public Agent(AgentConfig agentCfg)
        {
            _whenObjAttmptCompleted = new Subject<Briefing>();
            WhenObjectiveAttemptCompleted = _whenObjAttmptCompleted;
            var seqs = agentCfg.Seqs;
            var dev = seqs.AndroidDevices.DequeueEnqueue();
            var prox = seqs.Proxies.DequeueEnqueue();
            var (username, password) = (
                (seqs.Words1.DequeueEnqueue() + seqs.Words2.DequeueEnqueue() + RandomModule.Next(100)).ToLower(), StringModule.Rando(Chars.DigitsAndLetters, RandomModule.Next(8, 16))
            );
            var gcmToken = seqs.GcmTokens.Count == 0 ? "" : seqs.GcmTokens.DequeueEnqueue();

            var props = new AgentProps(
                agentCfg.Index,
                agentCfg.AgentUIUpdater,
                agentCfg.StatsUIUpdater,
                agentCfg.CancellationToken,
                agentCfg.VerifyClientProvider,
                username,
                password,
                gcmToken,
                agentCfg.GcmReqInfo
            );
            var state = new AgentState
            {
                Cfg = agentCfg.Cfg,
                AnonSession = new AnonTextPlusSession(dev, prox, TextPlusModule.GenAndroidId()),
            };
            _props = props;
            _state = state;
        }
    }

    public abstract class DirectorDirective
    {
    }

    public class ActivateAgentDirective : DirectorDirective
    {
        public int Index { get; }

        public ActivateAgentDirective(int index)
        {
            Index = index;
        }
    }

    public class SetCfgDirective : DirectorDirective
    {
        public Cfg Cfg { get; }

        public SetCfgDirective(Cfg cfg) { Cfg = cfg; }
    }

    public class GetCfgDirective : DirectorDirective
    {
        public Action<Cfg> Reply { get; }

        public GetCfgDirective(Action<Cfg> replyChannel) { Reply = replyChannel; }
    }

    public class SetSeqsDirective : DirectorDirective
    {
        public Seqs Seqs { get; }

        public SetSeqsDirective(Seqs seqs) { Seqs = seqs; }
    }

    public class GetSeqsDirective : DirectorDirective
    {
        public Action<Seqs> Reply { get; }

        public GetSeqsDirective(Action<Seqs> replyChannel) { Reply = replyChannel; }
    }

    public class AgentBriefingDirective : DirectorDirective
    {
        public Briefing Briefing { get; }

        public AgentBriefingDirective(
            Briefing briefing)
        {
            Briefing = briefing;
        }
    }

    public class DirectorProps
    {
        public StatsUIUpdater StatsUIUpdater { get; }
        public ReadOnlyDictionary<int, AgentUIUpdater> AgentUIUpdaters { get; }
        public CancellationToken CancellationToken { get; }

        public DirectorProps(
            StatsUIUpdater statsUIUpdater,
            ReadOnlyDictionary<int, AgentUIUpdater> agentUIUpdaters,
            CancellationToken cancellationToken)
        {
            StatsUIUpdater = statsUIUpdater;
            AgentUIUpdaters = agentUIUpdaters;
            CancellationToken = cancellationToken;
        }
    }

#pragma warning disable CS8618 // Non-nullable field is uninitialized.
    public class DirectorState
    {
        public int ActiveAgents { get; set; }
        public Dictionary<int, Agent> Agents { get; set; }
        public Cfg Cfg { get; set; }
        public Seqs Seqs { get; set; }
        public int Created { get; set; }
        public Queue<VerifyClient> VerifyClients { get; }
        public Dictionary<int, IDisposable> DeadDrop { get; set; }
        public VerifyProviderKind VerifyProviderKind { get; set; }
        public VerifyClientProvider VerifyClientProvider { get; set; }
    }
#pragma warning restore CS8618 // Non-nullable field is uninitialized.

    public class DirectorCfg
    {
        public StatsUIUpdater StatsUIUpdater { get; }
        public ReadOnlyDictionary<int, AgentUIUpdater> AgentUIUpdaters { get; }
        public CancellationToken CancellationToken { get; }
        public Cfg Cfg { get; }
        public Seqs Seqs { get; }
        public VerifyClientProvider VerifyClientProvider { get; }

        public DirectorCfg(
            StatsUIUpdater statsUIUpdater,
            ReadOnlyDictionary<int, AgentUIUpdater> agentUIUpdaters,
            CancellationToken cancellationToken,
            Cfg cfg,
            Seqs seqs,
            VerifyClientProvider pvaClientProvider)
        {
            StatsUIUpdater = statsUIUpdater;
            AgentUIUpdaters = agentUIUpdaters;
            CancellationToken = cancellationToken;
            Cfg = cfg;
            Seqs = seqs;
            VerifyClientProvider = pvaClientProvider;
        }
    }

    public class Director
    {
        private readonly Subject<Unit> _whenWorkComplete;
        private readonly DirectorProps _props;
        private readonly BufferBlock<DirectorDirective> _agent;
        private readonly DirectorState _state;

        public IObservable<Unit> WhenWorkComplete { get; }

        private bool _Active()
        {
            if (_state.Created >= _state.Cfg.MaxCreates)
                return false;

            return true;
        }

        private bool Active => _Active();

        public void SetCfg(Cfg cfg) => _agent.Post(new SetCfgDirective(cfg));
        public void SetSeqs(Seqs seqs) => _agent.Post(new SetSeqsDirective(seqs));

        public Task<Cfg> GetCfg()
        {
            var tcs = new TaskCompletionSource<Cfg>();
            _agent.Post(new GetCfgDirective(cfg => tcs.SetResult(cfg)));
            return tcs.Task;
        }

        public Task<Seqs> GetSeqs()
        {
            var tcs = new TaskCompletionSource<Seqs>();
            _agent.Post(new GetSeqsDirective(seqs => tcs.SetResult(seqs)));
            return tcs.Task;
        }

        private Unit ExecDirectiveIn(TimeSpan t, DirectorDirective d)
        {
            async Task Send()
            {
                try
                {
                    await Task.Delay(t, _props.CancellationToken).ConfigureAwait(false);
                    _agent.Post(d);
                }
                catch (OperationCanceledException) { return; }
            }
            _ = Send();
            return Unit.Value;
        }

        private Unit SetCfg(SetCfgDirective d)
        {
            _state.Cfg = d.Cfg;
            return Unit.Value;
        }

        private Unit GetCfg(GetCfgDirective d)
        {
            d.Reply(_state.Cfg);
            return Unit.Value;
        }

        private Unit SetSeqs(SetSeqsDirective d)
        {
            _state.Seqs = d.Seqs;
            return Unit.Value;
        }

        private Unit GetSeqs(GetSeqsDirective d)
        {
            d.Reply(_state.Seqs);
            return Unit.Value;
        }

        private Unit ActivateAgent(ActivateAgentDirective d)
        {
            var agentCfg = new AgentConfig(
                d.Index,
                _state.Cfg,
                _state.Seqs,
                _props.AgentUIUpdaters[d.Index],
                _props.StatsUIUpdater,
                _props.CancellationToken,
                _state.VerifyClientProvider,
                _state.Cfg.GcmReq
            );
            var agent = new Agent(agentCfg);

            _state.Agents[d.Index] = agent;

            if (!_state.DeadDrop.ContainsKey(d.Index))
                _state.DeadDrop.Add(d.Index, Disposable.Empty);

            _state.DeadDrop[d.Index] = agent.WhenObjectiveAttemptCompleted.Subscribe(
                briefing => _agent.Post(new AgentBriefingDirective(briefing))
            );

            _ = agent.Exec();

            return Unit.Value;
        }

        private Unit DebriefAgent(AgentProps props, AgentState _)
        {
            if (!_state.Agents.Remove(props.Index))
                ExnModule.InvalidOp($"Director did not contain agent with index {props.Index}");
            _state.DeadDrop[props.Index].Dispose();
            _state.DeadDrop[props.Index] = Disposable.Empty;
            return Unit.Value;
        }

        private Unit SaveAccount(Briefing briefing, bool hasPhoneNumber)
        {
            var fileName = hasPhoneNumber ? "textplus-accts-with-sms-numbers.txt" : "textplus-accts-without-sms-number.txt";
            using var writer = new StreamWriter(fileName, append: true);
            writer.WriteLine(
                JsonConvert.SerializeObject(briefing.State.AuthSession, Formatting.None)
            );
            return Unit.Value;
        }

        private Unit HandleObjectiveCompleted(ObjectiveCompletedBriefing briefing)
        {
            SaveAccount(briefing, briefing.State.CreatorState == CreatorState.GotPhoneNumber);
            _props.StatsUIUpdater.IncrementCreated();
            _state.Created++;
            return DebriefAgent(briefing.Props, briefing.State);
        }

        private Unit HandleObjectiveFailed(ObjectiveFailedBriefing briefing)
        {
            using var writer = new StreamWriter("errors.txt", append: true);
            writer.WriteLine(briefing.Error.ToLogMessage());

            if (briefing.Error is TextPlusApiException apiEx)
                apiEx.HttpCtx.Dispose();

            _ = briefing.State.CreatorState switch
            {
                CreatorState.Created => SaveAccount(briefing, hasPhoneNumber: false),
                CreatorState.GotPhoneNumber => SaveAccount(briefing, hasPhoneNumber: true),
                _ => Unit.Value
            };

            return DebriefAgent(briefing.Props, briefing.State);
        }

        private Unit DecrementActiveAgents()
        {
            _state.ActiveAgents--;
            return Unit.Value;
        }

        private Unit DebriefAgent(AgentBriefingDirective d)
        {
            _ = d.Briefing switch
            {
                ObjectiveCompletedBriefing b => HandleObjectiveCompleted(b),
                ObjectiveFailedBriefing b => HandleObjectiveFailed(b),
                _ => throw new InvalidOperationException("unhandled Briefing")
            };

            if (!Active)
                return DecrementActiveAgents();

            return ExecDirectiveIn(
                TimeSpan.FromSeconds(9.0d),
                new ActivateAgentDirective(d.Briefing.Props.Index)
            );
        }

        private Unit Process(DirectorDirective directive)
        {
            return directive switch
            {
                SetCfgDirective d => SetCfg(d),
                GetCfgDirective d => GetCfg(d),
                SetSeqsDirective d => SetSeqs(d),
                GetSeqsDirective d => GetSeqs(d),
                ActivateAgentDirective d => ActivateAgent(d),
                AgentBriefingDirective d => DebriefAgent(d),
                _ => throw new ArgumentException($"Can't handle {directive}")
            };
        }

        private async Task Recv()
        {
            try
            {
                while (_state.ActiveAgents > 0 && !_props.CancellationToken.IsCancellationRequested)
                {
                    var msg = await _agent.ReceiveAsync().ConfigureAwait(false);
                    Process(msg);
                }

                _whenWorkComplete.OnNext(Unit.Value);
                _whenWorkComplete.OnCompleted();
            }
            catch (Exception e)
            {
                Environment.FailFast($"director crashed with message: {e.GetType().Name} ~ {e.Message}", e);
                throw;
            }
        }

        public Unit ActivateAgents()
        {
            for (var i = 0; i < _state.Cfg.MaxWorkers; i++)
                _agent.Post(new ActivateAgentDirective(i));
            return Unit.Value;
        }

        public Director(DirectorCfg cfg)
        {
            _whenWorkComplete = new Subject<Unit>();
            var props = new DirectorProps(cfg.StatsUIUpdater, cfg.AgentUIUpdaters, cfg.CancellationToken);
            var state = new DirectorState
            {
                ActiveAgents = cfg.Cfg.MaxWorkers,
                Agents = new Dictionary<int, Agent>(cfg.Cfg.MaxWorkers),
                Cfg = cfg.Cfg,
                Seqs = cfg.Seqs,
                Created = 0,
                DeadDrop = new Dictionary<int, IDisposable>(cfg.Cfg.MaxWorkers),
                VerifyClientProvider = cfg.VerifyClientProvider
            };
            _agent = new BufferBlock<DirectorDirective>();

            _props = props;
            _state = state;
            WhenWorkComplete = _whenWorkComplete;

            _ = Recv();
        }
    }
}
