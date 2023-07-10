using LibCyStd;
using LibCyStd.IO;
using LibCyStd.Net;
using LibCyStd.Seq;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Reactive.Disposables;
using System.Reactive.Subjects;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;

namespace LibTextPlus.ContactSearcher
{
    public enum SearchType
    {
        Username,
        Email,
        CellPhone
    }

    public class Cfg
    {
        public int MaxWorkers { get; }
        public int MinSearchesPerSession { get; }
        public int MaxSearchesPerSession { get; }
        public int DelayInMinsBetweenSearches { get; }
        public int MinDelayBetweenContactSearch { get; }
        public int MaxDelayBetweenContactSearch { get; }
        public SearchType SearchType { get; }

        public Cfg(
            int maxWorkers,
            int minSearchesPerSession,
            int maxSearchesPerSession,
            int delayInMinsBetweenSearches,
            int minDelayBetweenContactSearch,
            int maxDelayBetweenContactSearch,
            SearchType searchType)
        {
            MaxWorkers = maxWorkers;
            MinSearchesPerSession = minSearchesPerSession;
            MaxSearchesPerSession = maxSearchesPerSession;
            DelayInMinsBetweenSearches = delayInMinsBetweenSearches;
            MinDelayBetweenContactSearch = minDelayBetweenContactSearch;
            MaxDelayBetweenContactSearch = maxDelayBetweenContactSearch;
            SearchType = searchType;
        }
    }

    public class Seqs : IDisposable
    {
        public StreamReader Contacts { get; }
        public Queue<AuthTextPlusSession> Sessions { get; }
        public Queue<Proxy> Proxies { get; }

        public Seqs(StreamReader contacts, Queue<AuthTextPlusSession> sessions, Queue<Proxy> proxies)
        {
            Contacts = contacts;
            Sessions = sessions;
            Proxies = proxies;
        }

        public void Dispose() => Contacts.Dispose();
    }

    public class AgentProps
    {
        public int Index { get; }
        public AgentUIUpdater UIUpdater { get; }
        public StatsUIUpdater StatsUIUpdater { get; }
        public CancellationToken CancellationToken { get; }
        public AuthTextPlusSession Session { get; }
        public WriteWorker MaleOutput { get; }
        public WriteWorker FemaleOutput { get; }
        public WriteWorker UnkOutput { get; }

        public AgentProps(
            int index,
            AgentUIUpdater uIUpdater,
            StatsUIUpdater statsUIUpdater,
            CancellationToken cancellationToken,
            AuthTextPlusSession session,
            WriteWorker maleOutput,
            WriteWorker femaleOutput,
            WriteWorker unkOutput)
        {
            Index = index;
            UIUpdater = uIUpdater;
            StatsUIUpdater = statsUIUpdater;
            CancellationToken = cancellationToken;
            Session = session;
            MaleOutput = maleOutput;
            FemaleOutput = femaleOutput;
            UnkOutput = unkOutput;
        }
    }

    public class AgentState
    {
#pragma warning disable CS8618 // Non-nullable field is uninitialized.
        public Cfg Cfg { get; set; }
        public Queue<string> Contacts { get; set; }
        public bool SessionIsInvalid { get; set; }
        public Blacklist ContactBlacklist { get; set; }
        public bool Connected { get; set; }
#pragma warning restore CS8618 // Non-nullable field is uninitialized.
    }

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
        public AuthTextPlusSession Session { get; }
        public Queue<string> Contacts { get; }
        public AgentUIUpdater AgentUIUpdater { get; }
        public StatsUIUpdater StatsUIUpdater { get; }
        public CancellationToken CancellationToken { get; }
        public WriteWorker MaleOutput { get; }
        public WriteWorker FemaleOutput { get; }
        public WriteWorker UnkOutput { get; }
        public Blacklist ContactBlacklist { get; }

        public AgentConfig(
            int index,
            Cfg cfg,
            AuthTextPlusSession session,
            Queue<string> contacts,
            AgentUIUpdater agentUIUpdater,
            StatsUIUpdater statsUIUpdater,
            CancellationToken cancellationToken,
            WriteWorker maleOutput,
            WriteWorker femaleOutput,
            WriteWorker unkOutput,
            Blacklist contactBlacklist)
        {
            Index = index;
            Cfg = cfg;
            Session = session;
            Contacts = contacts;
            AgentUIUpdater = agentUIUpdater;
            StatsUIUpdater = statsUIUpdater;
            CancellationToken = cancellationToken;
            MaleOutput = maleOutput;
            FemaleOutput = femaleOutput;
            UnkOutput = unkOutput;
            ContactBlacklist = contactBlacklist;
        }
    }

    public class Agent : IDisposable
    {
        private readonly Subject<Briefing> _whenObjAttmptCompleted;
        private readonly AgentProps _props;
        private readonly AgentState _state;

        public IObservable<Briefing> WhenObjectiveAttemptCompleted { get; }

        private void UpdateStatus(string status)
        {
            _props.UIUpdater.UpdateStatus(status);
        }

        private async Task DelayBeforeSearch()
        {
            var seconds = RandomModule.Next(_state.Cfg.MinDelayBetweenContactSearch, _state.Cfg.MaxDelayBetweenContactSearch);
            UpdateStatus($"delaying {seconds} seconds before executing next search: ...");
            await Task.Delay(TimeSpan.FromSeconds(seconds), _props.CancellationToken).ConfigureAwait(false);
        }

        private async Task<int> Search(string contact)
        {
            //contact = contact.Replace(" ", "");
            //if (contact.Contains("@"))
            //    contact = contact.Split('@')[0];

            UpdateStatus($"searching for {contact}: ...");

            var cnt = 0;
            for (var i = 1; i < 23; i++)
            {
                using var resp = await TextPlusApiClient.SearchMatchable(_props.Session, $"{contact}-{i}").ConfigureAwait(false);
                if (resp.Value.Count == 0)
                    break;

                foreach (var result in resp.Value)
                {
                    var txt = JsonConvert.SerializeObject(result);
                    if (string.IsNullOrWhiteSpace(result.Persona.Sex))
                        _props.UnkOutput.Add(txt);
                    else if (result.Persona.Sex.InvariantStartsWith("m"))
                        _props.MaleOutput.Add(txt);
                    else if (result.Persona.Sex.InvariantStartsWith("f"))
                        _props.FemaleOutput.Add(txt);
                    else
                        _props.UnkOutput.Add(txt);
                }

                cnt++;
            }

            return cnt;
        }

        public async Task ValidateSession()
        {
            using var _ = await TextPlusApiClient.RetrieveLocalInfo(
                _props.Session
            ).ConfigureAwait(false);
        }

        private async Task<string> RetrieveTicketGrantingTicket()
        {
            using var resp = await TextPlusApiClient.RequestTicketGranterTicket(
                _props.Session,
                new TextPlusReqTicketGranterTicketReqContent(_props.Session.Username, _props.Session.Password)
            ).ConfigureAwait(false);
            return resp.Value.Ticket;
        }


        private async Task<string> RetrieveAuthToken(string ticketGrantingTicket)
        {
            using var resp = await TextPlusApiClient.RequestServiceTicket(
                _props.Session,
                new TextPlusRequestServiceTicketReqContent("nextplus", ticketGrantingTicket)
            ).ConfigureAwait(false);
            return resp.Value.Ticket;
        }

        private async Task Connect()
        {
            UpdateStatus("Connecting: ...");
            try {
                await ValidateSession().ConfigureAwait(false);
            }
            catch (TextPlusApiException e) when ((int)e.HttpCtx.HttpResp.StatusCode == 401 /*unauthorized*/) {
                e.HttpCtx.Dispose();
                try {
                    var ticket = await RetrieveTicketGrantingTicket().ConfigureAwait(false);
                    _props.Session.AuthToken = await RetrieveAuthToken(ticket).ConfigureAwait(false);
                } catch (TextPlusApiException) {
                    _state.SessionIsInvalid = true;
                    throw;
                }
            }

            _props.StatsUIUpdater.IncrementOnline();
            _state.Connected = true;
        }

        public async Task Exec()
        {
            try
            {
                _props.UIUpdater.UpdateId(_props.Session.Username);
                await Connect().ConfigureAwait(false);
                try
                {
                    var ttl = 0;
                    while (_state.Contacts.Count > 0)
                    {
                        await DelayBeforeSearch().ConfigureAwait(false);
                        var contact = _state.Contacts.Dequeue();
                        var found = await Search(contact).ConfigureAwait(false);
                        if (found > 0)
                        {
                            for (var i = 0; i < found; i++)
                                _props.StatsUIUpdater.IncrementValid();
                            ttl += found;
                        }
                        _props.StatsUIUpdater.IncrementSearches();
                        _state.ContactBlacklist.ThreadSafeAdd(contact);
                    }

                    if (ttl > 0)
                    {
                        var word = ttl == 1 ? "contact" : "contacts";
                        UpdateStatus($"found {ttl} {word}.");
                        await Task.Delay(3000).ConfigureAwait(false);
                    }

                    _whenObjAttmptCompleted.OnNext(new ObjectiveCompletedBriefing(_props, _state));
                }
                finally
                {
                    _props.StatsUIUpdater.DecrementOnline();
                }
            }
            catch (Exception e) when (
                e is InvalidOperationException
                || e is TimeoutException
                || e is OperationCanceledException)
            {
                UpdateStatus($"Error: {e.GetType().Name} occured ~ {e.Message}");
                _whenObjAttmptCompleted.OnNext(new ObjectiveFailedBriefing(_props, _state, e));
            }
            catch (Exception e) {
                Environment.FailFast($"Unhandled exception in agent: {e.GetType().Name} ~ {e.Message}.", e);
            }
        }

        public void Dispose()
        {
            _whenObjAttmptCompleted.OnCompleted();
            _whenObjAttmptCompleted.Dispose();
        }

        public Agent(AgentConfig agentCfg)
        {
            _whenObjAttmptCompleted = new Subject<Briefing>();
            WhenObjectiveAttemptCompleted = _whenObjAttmptCompleted;
            var props = new AgentProps(
                agentCfg.Index,
                agentCfg.AgentUIUpdater,
                agentCfg.StatsUIUpdater,
                agentCfg.CancellationToken,
                agentCfg.Session,
                agentCfg.MaleOutput,
                agentCfg.FemaleOutput,
                agentCfg.UnkOutput
            );
            var state = new AgentState
            {
                Cfg = agentCfg.Cfg,
                Contacts = agentCfg.Contacts,
                ContactBlacklist = agentCfg.ContactBlacklist
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
        public Blacklist ContactBlacklist { get; }
        public WriteWorker MaleOutput { get; }
        public WriteWorker FemaleOutput { get; }
        public WriteWorker UnkOutput { get; }

        public DirectorProps(
            StatsUIUpdater statsUIUpdater,
            ReadOnlyDictionary<int, AgentUIUpdater> agentUIUpdaters,
            CancellationToken cancellationToken,
            Blacklist contactBlacklist,
            WriteWorker maleOutput,
            WriteWorker femaleOutput,
            WriteWorker unkOutput)
        {
            StatsUIUpdater = statsUIUpdater;
            AgentUIUpdaters = agentUIUpdaters;
            CancellationToken = cancellationToken;
            ContactBlacklist = contactBlacklist;
            MaleOutput = maleOutput;
            FemaleOutput = femaleOutput;
            UnkOutput = unkOutput;
        }
    }

    public class DirectorState
#pragma warning restore CS8618 // Non-nullable field is uninitialized.
    {
        public int ActiveAgents { get; set; }
#pragma warning disable CS8618 // Non-nullable field is uninitialized.
        public Dictionary<int, Agent> Agents { get; set; }
        public Cfg Cfg { get; set; }
        public Seqs Seqs { get; set; }
        public Dictionary<int, IDisposable> DeadDrop { get; set; }
        public Dictionary<string, DateTimeOffset> SessionTimes { get; set; }
        public Queue<string> ContactCache { get; set; }
        public Dictionary<string, int> LoginErrors { get; set; }
#pragma warning restore CS8618 // Non-nullable field is uninitialized.
    }

    public class DirectorCfg
    {
        public StatsUIUpdater StatsUIUpdater { get; }
        public ReadOnlyDictionary<int, AgentUIUpdater> AgentUIUpdaters { get; }
        public CancellationToken CancellationToken { get; }
        public Cfg Cfg { get; }
        public Seqs Seqs { get; }
        public Blacklist ContactBlacklist { get; }
        public WriteWorker MaleOutput { get; }
        public WriteWorker FemaleOutput { get; }
        public WriteWorker UnkOutput { get; }

        public DirectorCfg(
            StatsUIUpdater statsUIUpdater,
            ReadOnlyDictionary<int, AgentUIUpdater> agentUIUpdaters,
            CancellationToken cancellationToken,
            Cfg cfg,
            Seqs seqs,
            Blacklist contactBlacklist,
            WriteWorker maleOutput,
            WriteWorker femaleOutput,
            WriteWorker unkOutput)
        {
            StatsUIUpdater = statsUIUpdater;
            AgentUIUpdaters = agentUIUpdaters;
            CancellationToken = cancellationToken;
            Cfg = cfg;
            Seqs = seqs;
            ContactBlacklist = contactBlacklist;
            MaleOutput = maleOutput;
            FemaleOutput = femaleOutput;
            UnkOutput = unkOutput;
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
            if (_state.Seqs.Contacts.EndOfStream && _state.ContactCache.Count == 0)
                return false;
            if (_state.Seqs.Sessions.Count == 0)
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

        private Queue<string> GetContacts()
        {
            var cfg = _state.Cfg;
            var cnt = RandomModule.Next(cfg.MinSearchesPerSession, cfg.MaxSearchesPerSession);
            var contacts = _state.Seqs.Contacts;
            var queue = new Queue<string>(cnt);
            while (_state.ContactCache.Count > 0 && queue.Count < cnt)
            {
                var contact = _state.ContactCache.Dequeue();
                if (!_props.ContactBlacklist.Contains(contact))
                    queue.Enqueue(contact);
            }
            while (!contacts.EndOfStream && queue.Count < cnt)
            {
                var contact = contacts.ReadLine();
                if (!_props.ContactBlacklist.Contains(contact))
                    queue.Enqueue(contact);
            }
            return queue;
        }

        private bool ShouldSearchWithSession(AuthTextPlusSession session)
        {
            if (!_state.SessionTimes.ContainsKey(session.UserId))
                return true;
            var sessionEndedAt = _state.SessionTimes[session.UserId];
            var added = sessionEndedAt + TimeSpan.FromMinutes(_state.Cfg.DelayInMinsBetweenSearches);
            var result = added <= DateTimeOffset.Now;
            return result;
        }

        private void UpdateAgentUI(int index, string id, string status)
        {
            var ui = _props.AgentUIUpdaters[index];
            ui.UpdateId(id);
            ui.UpdateStatus(status);
        }

        private Unit StopAgent(int index)
        {
            UpdateAgentUI(index, id: "", status: "");
            return Unit.Value;
        }

        private Unit ActivateAgent(ActivateAgentDirective d)
        {
            var contacts = GetContacts();
            if (contacts.Count == 0)
                return StopAgent(d.Index);

            var session = _state.Seqs.Sessions.Dequeue();

            if (!ShouldSearchWithSession(session))
            {
                _state.Seqs.Sessions.Enqueue(session);
                UpdateAgentUI(d.Index, session.Username, $"this session is scheduled to search at {_state.SessionTimes[session.UserId].AddMinutes(_state.Cfg.DelayInMinsBetweenSearches)}");
                return ExecDirectiveIn(TimeSpan.FromSeconds(9.0), new ActivateAgentDirective(d.Index));
            }

            session.Proxy = _state.Seqs.Proxies.DequeueEnqueue();

            var agentCfg = new AgentConfig(
                d.Index,
                _state.Cfg,
                session,
                contacts,
                _props.AgentUIUpdaters[d.Index],
                _props.StatsUIUpdater,
                _props.CancellationToken,
                _props.MaleOutput,
                _props.FemaleOutput,
                _props.UnkOutput,
                _props.ContactBlacklist
            );
            var agent = new Agent(agentCfg);

            _state.Agents.AddOrUpdate(d.Index, agent);

            _state.DeadDrop.AddOrUpdate(
                d.Index,
                agent.WhenObjectiveAttemptCompleted.Subscribe(
                    briefing => _agent.Post(new AgentBriefingDirective(briefing))
                )
            );

            _ = agent.Exec();

            return Unit.Value;
        }

        private bool AgentIsInvalid(AgentProps props, AgentState state)
        {
            if (state.SessionIsInvalid)
                return true;

            if (!_state.LoginErrors.ContainsKey(props.Session.UserId))
                return false;

            return _state.LoginErrors[props.Session.UserId] > 3;
        }

        private Unit DebriefAgent(AgentProps props, AgentState state)
        {
            if (!_state.Agents.Remove(props.Index))
                ExnModule.InvalidOp($"Director did not contain agent with index {props.Index}");

            if (!AgentIsInvalid(props, state))
            {
                _state.Seqs.Sessions.Enqueue(props.Session);
                _state.SessionTimes.AddOrUpdate(props.Session.UserId, DateTimeOffset.Now);
            }

            _state.DeadDrop[props.Index].Dispose();
            _state.DeadDrop[props.Index] = Disposable.Empty;

            return Unit.Value;
        }

        private Unit HandleObjectiveCompleted(ObjectiveCompletedBriefing briefing) =>
            DebriefAgent(briefing.Props, briefing.State);

        private Unit HandleObjectiveFailed(ObjectiveFailedBriefing briefing)
        {
            using var writer = new StreamWriter("errors.txt", append: true);
            writer.WriteLine(briefing.Error.ToLogMessage());
            if (briefing.Error is TextPlusApiException apiEx) {
                apiEx.HttpCtx.Dispose();
            }

            if (!briefing.State.Connected)
            {
                if (!_state.LoginErrors.ContainsKey(briefing.Props.Session.UserId))
                    _state.LoginErrors.Add(briefing.Props.Session.UserId, 0);
                _state.LoginErrors[briefing.Props.Session.UserId]++;
            }

            return DebriefAgent(briefing.Props, briefing.State);
        }

        private Unit DecrementActiveAgents()
        {
            _state.ActiveAgents--;
            return Unit.Value;
        }

        private Unit HandleAgentBriefing(AgentBriefingDirective d)
        {
            _ = d.Briefing switch
            {
                ObjectiveCompletedBriefing b => HandleObjectiveCompleted(b),
                ObjectiveFailedBriefing b => HandleObjectiveFailed(b),
                _ => throw new InvalidOperationException("unhandled Briefing")
            };

            if (!Active)
            {
                StopAgent(d.Briefing.Props.Index);
                return DecrementActiveAgents();
            }

            return ExecDirectiveIn(
                TimeSpan.FromSeconds(9.0),
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
                AgentBriefingDirective d => HandleAgentBriefing(d),
                _ => throw new InvalidOperationException($"Invalid directive received: {directive}")
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
                Environment.FailFast($"unhandled exception in director: {e.GetType().Name} ~ {e.Message}", e);
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
            var props = new DirectorProps(cfg.StatsUIUpdater, cfg.AgentUIUpdaters, cfg.CancellationToken, cfg.ContactBlacklist, cfg.MaleOutput, cfg.FemaleOutput, cfg.UnkOutput);
            var state = new DirectorState
            {
                ActiveAgents = cfg.Cfg.MaxWorkers,
                Agents = new Dictionary<int, Agent>(cfg.Cfg.MaxWorkers),
                Cfg = cfg.Cfg,
                Seqs = cfg.Seqs,
                DeadDrop = new Dictionary<int, IDisposable>(cfg.Cfg.MaxWorkers),
                SessionTimes = new Dictionary<string, DateTimeOffset>(cfg.Seqs.Sessions.Count),
                ContactCache = new Queue<string>(100),
                LoginErrors = new Dictionary<string, int>(cfg.Seqs.Sessions.Count)
            };
            _agent = new BufferBlock<DirectorDirective>();

            _props = props;
            _state = state;
            WhenWorkComplete = _whenWorkComplete;
            _ = Recv();
        }
    }
}
