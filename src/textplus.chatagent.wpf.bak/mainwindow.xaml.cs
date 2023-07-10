using LibCyStd;
using LibCyStd.IO;
using LibCyStd.Net;
using LibCyStd.Seq;
using LibCyStd.Wpf;
using LibTextPlus;
using LibTextPlus.ChatAgent;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reactive.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace TextPlus.ChatAgent.Wpf
{
    public class MainWindow : Window
    {
        private readonly IniCfg _iniCfg;
        private readonly ConfigDataGrid _cfgDataGrid;
        private readonly SemaphoreSlim _updateCfgLock;

        private Director? _director;
        private Blacklist? _chatBlacklist;
        private Blacklist? _greetBlacklist;

        private Label LblOnline => (Label)FindName(nameof(LblOnline));
        private Label LblGreets => (Label)FindName(nameof(LblGreets));
        private Label LblConvos => (Label)FindName(nameof(LblConvos));
        private Label LblIn => (Label)FindName(nameof(LblIn));
        private Label LblOut => (Label)FindName(nameof(LblOut));
        private Label LblLinks => (Label)FindName(nameof(LblLinks));
        private Label LblCompleted => (Label)FindName(nameof(LblCompleted));
        private Label LblRestricts => (Label)FindName(nameof(LblRestricts));
        private TextBox TxtChatLog => (TextBox)FindName(nameof(TxtChatLog));
        private Grid GrdCfgContent => (Grid)FindName(nameof(GrdCfgContent));
        private MenuItem CmdLaunch => (MenuItem)FindName(nameof(CmdLaunch));
        private MenuItem CmdClearChatLog => (MenuItem)FindName(nameof(CmdClearChatLog));
        private DataGrid WorkerMonitor => (DataGrid)FindName(nameof(WorkerMonitor));

        private ConfigDataGrid CreateConfigDataGrid()
        {
            var items = ReadOnlyCollectionModule.OfSeq(new[] {
                new ConfigDataGridItem("Max agents", "int", 1),
                new ConfigDataGridItem("Min greet delay (seconds)", "int", 20),
                new ConfigDataGridItem("Max greet delay (seconds)", "int", 20),
                new ConfigDataGridItem("Min greets per session", "int", 5),
                new ConfigDataGridItem("Max greets per session", "int", 10),
                new ConfigDataGridItem("Min delay in minutes after greet session", "int", 20),
                new ConfigDataGridItem("Max delay in minutes after greet session", "int", 40),
                new ConfigDataGridItem("Min delay in seconds before each message send", "int", 20),
                new ConfigDataGridItem("Max delay in seconds before each message send", "int", 40),
                new ConfigDataGridItem("Max concurrent message sends", "int", 5),
                new ConfigDataGridItem("Max total greets to send mer account?", "int", 10),
                new ConfigDataGridItem("Enable chat log?", "bool", false),
                new ConfigDataGridItem("Sessions", "sequence", ConfigSequence.Empty),
                new ConfigDataGridItem("Proxies", "sequence", ConfigSequence.Empty),
                new ConfigDataGridItem("Contacts", "stream", ConfigFile.Empty),
                new ConfigDataGridItem("Greets", "sequence", ConfigSequence.Empty),
                new ConfigDataGridItem("Script", "sequence", ConfigSequence.Empty),
                new ConfigDataGridItem("Links", "sequence", ConfigSequence.Empty),
                new ConfigDataGridItem("Keywords", "sequence", ConfigSequence.Empty),
                new ConfigDataGridItem("Restricts", "sequence", ConfigSequence.Empty),
            });
            return new ConfigDataGrid(items, GrdCfgContent, _iniCfg);
        }

        private static void ValidateMinMax(int min, int max, string name)
        {
            if (min <= max)
                return;

            throw new InvalidOperationException($"{name} min must be <= max.");
        }

        private static void ValidateCfg(Cfg cfg)
        {
            if (cfg.MaxAgents <= 0)
                throw new InvalidOperationException("max agents must be >= 1");
            ValidateMinMax(cfg.MinDelayBeforeSendingGreet, cfg.MaxDelayBeforeSendingGreet, "delay before sending greet");
            ValidateMinMax(cfg.MinGreetsPerSession, cfg.MaxGreetsPerSession, "greets per session");
            ValidateMinMax(cfg.MinDelayInMinutesAfterGreetSession, cfg.MaxDelayInMinutesAfterGreetSession, "greet session delay in minutes");
            ValidateMinMax(cfg.MinSendMsgDelayInSeconds, cfg.MaxSendMsgDelayInSeconds, "send message delay");
        }

        private static void ValidateSeqs(Sequences seqs)
        {
            if (seqs.Sessions.Count == 0)
                throw new InvalidOperationException("sessions file does not exist, has invalid lines or is empty");
            if (seqs.Proxies.Count == 0)
                throw new InvalidOperationException("proxies file does not exist, has invalid lines or is empty");
            if (seqs.Script.Count == 0)
                throw new InvalidOperationException("script file does not exist or is empty.");
        }

        private static ReadOnlyDictionary<string, ReadOnlyCollection<string>> ParseKeywords(
            IEnumerable<string> items)
        {
            var dict = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in items)
            {
                var sp = item.SplitRemoveEmpty('|');
                if (sp.Length < 2)
                    continue;
                var key = sp[0];
                var responses = sp.Skip(1);
                if (!dict.ContainsKey(key))
                    dict[key] = new List<string>(1);

                dict[key].AddRange(responses);
            }
            var dict2 = new Dictionary<string, ReadOnlyCollection<string>>(dict.Count, StringComparer.OrdinalIgnoreCase);
            foreach (var kvp in dict)
                dict2.Add(kvp.Key, ReadOnlyCollectionModule.OfSeq(kvp.Value));
            return ReadOnlyDictModule.OfDict(dict2);
        }

        private Cfg CreateCfg()
        {
            var cfg = new Cfg {
                MaxAgents = _cfgDataGrid.GetValue<int>("Max agents"),
                MinDelayBeforeSendingGreet = _cfgDataGrid.GetValue<int>("Min greet delay (seconds)"),
                MaxDelayBeforeSendingGreet = _cfgDataGrid.GetValue<int>("Max greet delay (seconds)"),
                MinGreetsPerSession = _cfgDataGrid.GetValue<int>("Min greets per session"),
                MaxGreetsPerSession = _cfgDataGrid.GetValue<int>("Max greets per session"),
                MinDelayInMinutesAfterGreetSession = _cfgDataGrid.GetValue<int>("Min delay in minutes after greet session"),
                MaxDelayInMinutesAfterGreetSession = _cfgDataGrid.GetValue<int>("Max delay in minutes after greet session"),
                MinSendMsgDelayInSeconds = _cfgDataGrid.GetValue<int>("Min delay in seconds before each message send"),
                MaxSendMsgDelayInSeconds = _cfgDataGrid.GetValue<int>("Max delay in seconds before each message send"),
                MaxConcurrentMessageSends = _cfgDataGrid.GetValue<int>("Max concurrent message sends"),
                MaxTotalGreetsToSendPerAcct = _cfgDataGrid.GetValue<int>("Max total greets to send mer account?"),
                ChatLogEnabled = _cfgDataGrid.GetValue<bool>("Enable chat log?")
            };
            ValidateCfg(cfg);
            return cfg;
        }

        private ContactsStreamReader GetContacts(ConfigFile file, Blacklist chatBlacklist, Blacklist greetBlacklist)
        {
            var result = _cfgDataGrid.TryGetValue<ConfigFile>("Contacts");
            if (result.IsNone)
                return new ContactsStreamReader(new MemoryStream(), chatBlacklist, greetBlacklist);

            ContactsStreamReader contacts;
            if (result.Value.FileInfo.Exists)
            {
                contacts = new ContactsStreamReader(
                    new FileStream(file.FileInfo.FullName, FileMode.Open),
                    chatBlacklist,
                    greetBlacklist
                );
            }
            else
            {
                contacts = new ContactsStreamReader(
                    new MemoryStream(),
                    chatBlacklist,
                    greetBlacklist
                );
            }

            return contacts;
        }

        private static Option<Proxy> TryParseOpt(string inp)
        {
            var (succ, p) = Proxy.TryParse(inp);
            if (succ)
                return p;
            return Option.None;
        }

        private Sequences CreateSeqs(Blacklist chatBlacklist, Blacklist greetBlacklist)
        {
            var sessions = QueueModule.OfSeq(
               _cfgDataGrid.GetValue<ConfigSequence>("Sessions").Items.Choose(AuthTextPlusSession.TryParseFromJson)
           );
            var proxies = QueueModule.OfSeq(
                _cfgDataGrid.GetValue<ConfigSequence>("Proxies").Items.Choose(TryParseOpt)
            );

            var contacts = GetContacts(_cfgDataGrid.GetValue<ConfigFile>("Contacts"), chatBlacklist, greetBlacklist);
            var greets = QueueModule.OfSeq(
                _cfgDataGrid.GetValue<ConfigSequence>("Greets").Items
            );
            var script = ReadOnlyCollectionModule.OfSeq(
                _cfgDataGrid.GetValue<ConfigSequence>("Script").Items
            );
            var links = QueueModule.OfSeq(
                _cfgDataGrid.GetValue<ConfigSequence>("Links").Items
            );
            var restricts = ReadOnlyCollectionModule.OfSeq(
                _cfgDataGrid.GetValue<ConfigSequence>("Restricts").Items
            );
            var kws = ParseKeywords(
                _cfgDataGrid.GetValue<ConfigSequence>("Keywords").Items
            );
            var seqs = new Sequences(
                contacts, sessions, proxies, greets, script, links, restricts, kws
            );
            ValidateSeqs(seqs);
            return seqs;
        }

        private static ObservableCollection<DataGridItem> CreateDataGridItems(int cnt, DataGrid dataGrid)
        {
            var lst = new List<DataGridItem>(cnt);
            for (var i = 0; i < cnt; i++)
                lst.Add(new DataGridItem(dataGrid));
            return new ObservableCollection<DataGridItem>(lst);
        }

        private static ReadOnlyDictionary<int, AgentUIUpdater> CreateAgentUIUpdaters(int cnt, WpfUIUpdater uiUpdater)
        {
            var d = new Dictionary<int, AgentUIUpdater>(cnt);
            for (var i = 0; i < cnt; i++)
                d.Add(i, new WpfAgentUIUpdater(i, uiUpdater));
            return ReadOnlyDictModule.OfDict(d);
        }

        private static Option<(string json, string jid)> TryDeserializeContact(string input)
        {
            var result = JsonConvert.DeserializeObject<TextPlusMatchableSearchResponse>(input);
            if (result == null)
                return Option.None;
            if (result.Persona == null || string.IsNullOrWhiteSpace(result.Persona.Jid))
                return Option.None;
            return Option.Some((input, result.Persona.Jid));
        }

        private static HashSet<TextPlusMatchableSearchResponse> ItemsNotInGreetBlacklist(
            HashSet<string> greetBlacklist,
            IEnumerable<TextPlusMatchableSearchResponse> items)
        {
            var contains = new HashSet<TextPlusMatchableSearchResponse>();
            foreach (var item in items)
            {
                if (!greetBlacklist.Contains(item.Persona.Jid))
                    contains.Add(item);
            }
            return contains;
        }

        //private static void AddContactsDb(Sequences seqs)
        //{
        //    Console.Out.WriteLine("Adding contacts to database: ...");
        //    var tmp = new List<ContactEntity>();

        //    void Add()
        //    {
        //        if (tmp.Count == 0)
        //            return;
        //        using var transaction = Database.BeginTransaction();
        //        Contacts.AddOrUpdateViaJid(tmp, transaction);
        //        tmp.Clear();
        //        transaction.Commit();
        //    }

        //    while (true)
        //    {
        //        var item = seqs.Contacts.DequeueLine();
        //        if (item == null)
        //            break;
        //        if (StringModule.AnyEmptyOrWhiteSpace(new[] { item.Id, item.Persona.Id, item.Persona.Jid, item.Persona.Handle }))
        //        {
        //            Console.Error.WriteLine($"WARNING: Found incomplete contact info for contact {item}");
        //            continue;
        //        }
        //        tmp.Add(new ContactEntity
        //        {
        //            Handle = item.Persona.Handle,
        //            Id = item.Id,
        //            Jid = item.Persona.Jid,
        //            LastSeen = item.Persona.LastSeen,
        //            PersonaId = item.Persona.Id
        //        });
        //        if (tmp.Count < 100000)
        //            continue;
        //        Console.WriteLine($"Adding {tmp.Count}");
        //        Add();
        //        Console.WriteLine($"Added {tmp.Count}");
        //    }

        //    Add();
        //}

        //private static void AdvanceContacts(Sequences seqs)
        //{
        //    Console.Out.WriteLine("Finding unblacklisted position of contacts stream: ...");
        //    using var transaction = Database.BeginTransaction();

        //    try
        //    {
        //        HashSet<string>? tmp = null;
        //        var cnt = 0;
        //        try
        //        {
        //            var s = new Stopwatch();
        //            var s1 = new Stopwatch();
        //            s.Start();
        //            while (true)
        //            {
        //                var items = seqs.Contacts.DequeueLines(5000);
        //                if (items.Count == 0)
        //                    return;

        //                cnt += items.Count;

        //                if (tmp == null)
        //                {
        //                    Console.Out.WriteLine("Loading greet blacklist into memory: ...");
        //                    tmp = new HashSet<string>(Blacklist.SelectGreetValues(transaction));
        //                    Console.Out.WriteLine($"Greet blacklist loaded:  {tmp.Count} unique items.");
        //                    Console.Out.WriteLine("Advancing contacts stream to next item not in greet blacklist: ...");
        //                }

        //                var jidsNotInBlacklist = ItemsNotInGreetBlacklist(tmp, items);
        //                if (jidsNotInBlacklist.Count == 0)
        //                    continue;

        //                Console.Out.WriteLine($"Position {cnt} not in greet blacklist.");

        //                var contacts = new List<ContactEntity>();
        //                foreach (var contact in items)
        //                {
        //                    contacts.Add(new ContactEntity
        //                    {
        //                        Handle = contact.Persona.Handle,
        //                        Id = contact.Id,
        //                        Jid = contact.Persona.Jid,
        //                        LastSeen = contact.Persona.LastSeen,
        //                        PersonaId = contact.Persona.Id
        //                    });
        //                }

        //                s1.Start();
        //                Contacts.AddOrUpdateViaJid(contacts);
        //                s1.Stop();
        //                Console.WriteLine($"s1: {s1.Elapsed}");
        //                //seqs.Contacts.Enqueue(jidsNotInBlacklist);

        //                s.Stop();
        //                Console.WriteLine(s.Elapsed);
        //                return;
        //            }
        //        }
        //        finally {
        //            tmp?.Clear();
        //            tmp = null;
        //            transaction.Commit();
        //        }

        //    }
        //    catch
        //    {
        //        transaction.Rollback();
        //        throw;
        //    }
        //}

        private async void HandleConfigValueUpdated((string name, ConfigValue value) kvp)
        {
            await _updateCfgLock.WaitAsync().ConfigureAwait(false);
            var (name, value) = kvp;

            try
            {
                async Task UpdateCfg(Action<Cfg> fn)
                {
                    var cfg = await _director!.Cfg.ConfigureAwait(false);
                    var copy = Cfg.Copy(cfg);
                    try
                    {
                        fn(copy);
                        ValidateCfg(copy);
                        _director!.Cfg = Task.FromResult(copy);
                    }
                    catch (InvalidOperationException) { /*ignored*/ }
                }

                async Task UpdateSeqs(Action<Sequences> fn)
                {
                    var seqs = await _director!.Sequences.ConfigureAwait(false);
                    var copy = Sequences.Copy(seqs);
                    try
                    {
                        fn(copy);
                        ValidateSeqs(copy);
                        _director!.Sequences = Task.FromResult(copy);
                    }
                    catch (InvalidOperationException) { }
                }

                switch (name)
                {
                    case "Min greet delay (seconds)":
                        await UpdateCfg((cfg) => cfg.MinDelayBeforeSendingGreet = (int)value.ObjValue).ConfigureAwait(false);
                        break;
                    case "Max greet delay (seconds)":
                        await UpdateCfg((cfg) => cfg.MaxDelayBeforeSendingGreet = (int)value.ObjValue).ConfigureAwait(false);
                        break;
                    case "Min greets per session":
                        await UpdateCfg((cfg) => cfg.MinGreetsPerSession = (int)value.ObjValue).ConfigureAwait(false);
                        break;
                    case "Max greets per session":
                        await UpdateCfg((cfg) => cfg.MaxGreetsPerSession = (int)value.ObjValue).ConfigureAwait(false);
                        break;
                    case "Min delay in minutes after greet session":
                        await UpdateCfg((cfg) => cfg.MinDelayInMinutesAfterGreetSession = (int)value.ObjValue).ConfigureAwait(false);
                        break;
                    case "Max delay in minutes after greet session":
                        await UpdateCfg((cfg) => cfg.MaxDelayInMinutesAfterGreetSession = (int)value.ObjValue).ConfigureAwait(false);
                        break;
                    case "Min delay in seconds before each message send":
                        await UpdateCfg((cfg) => cfg.MinSendMsgDelayInSeconds = (int)value.ObjValue).ConfigureAwait(false);
                        break;
                    case "Max delay in seconds before each message send":
                        await UpdateCfg((cfg) => cfg.MaxSendMsgDelayInSeconds = (int)value.ObjValue).ConfigureAwait(false);
                        break;
                    case "Enable chat log?":
                        await UpdateCfg(cfg => cfg.ChatLogEnabled = (bool)value.ObjValue).ConfigureAwait(false);
                        break;
                    case "Max concurrent message sends":
                        await UpdateCfg((cfg) => cfg.MaxConcurrentMessageSends = (int)value.ObjValue).ConfigureAwait(false);
                        break;
                    case "Proxies":
                        await UpdateSeqs(seqs => seqs.Proxies = QueueModule.OfSeq(((ConfigSequenceValue)value).Value.Items.Choose(TryParseOpt))).ConfigureAwait(false);
                        break;
                    case "Greets":
                        await UpdateSeqs(seqs => seqs.Greets = QueueModule.OfSeq(((ConfigSequenceValue)value).Value.Items)).ConfigureAwait(false);
                        break;
                    case "Script":
                        await UpdateSeqs(seqs => seqs.Script = ReadOnlyCollectionModule.OfSeq(((ConfigSequenceValue)value).Value.Items)).ConfigureAwait(false);
                        break;
                    case "Links":
                        await UpdateSeqs(seqs => seqs.Links = QueueModule.OfSeq(((ConfigSequenceValue)value).Value.Items)).ConfigureAwait(false);
                        break;
                    case "Keywords":
                        await UpdateSeqs(seqs => seqs.KeywordReplies = ParseKeywords(((ConfigSequenceValue)value).Value.Items)).ConfigureAwait(false);
                        break;
                    case "Restricts":
                        await UpdateSeqs(seqs => seqs.RestrictedExpressions = ReadOnlyCollectionModule.OfSeq(((ConfigSequenceValue)value).Value.Items)).ConfigureAwait(false);
                        break;
                    case "Contacts":
                        var seqs = await _director!.Sequences.ConfigureAwait(false);
                        var copy = Sequences.Copy(seqs);
                        try
                        {
                            copy.Contacts = GetContacts(((ConfigFileValue)value).Value, _chatBlacklist!, _greetBlacklist!);
                            ValidateSeqs(copy);
                            //await Task.Run(() => AdvanceContacts(seqs)).ConfigureAwait(false);
                            _director!.Sequences = Task.FromResult(copy);
                        }
                        catch (InvalidOperationException) { }
                        break;
                }
            }
            finally
            {
                _updateCfgLock.Release();
            }
        }

        private async void HandleCmdLaunchClick(object sender, RoutedEventArgs e)
        {
            if (!CmdLaunch.IsEnabled)
                return;

            CmdLaunch.IsEnabled = false;

            try
            {
                using var chatBlacklist = new Blacklist(new FileBlacklist("textplus-chat-blacklist.txt"));
                using var greetBlacklist = new Blacklist(new FileBlacklist("textplus-greet-blacklist.txt"));

                _chatBlacklist = chatBlacklist;
                _greetBlacklist = greetBlacklist;
                await Task.Run(() => greetBlacklist.Load()).ConfigureAwait(true);
                await Task.Run(() => chatBlacklist.Load()).ConfigureAwait(true);

                chatBlacklist.Add("c233b4dca5054b1ca09ddc530021b988@app.nextplus.me");

                var cfg = CreateCfg();
                var seqs = CreateSeqs(chatBlacklist, greetBlacklist);

                foreach (var session in seqs.Sessions) {
                    var scriptStates = await ScriptStates.TryFindViaLocalId(session.Jid).ConfigureAwait(true);
                    foreach (var s in scriptStates) {
                        if (s.Pending) {
                            s.Pending = false;
                            await ScriptStates.InsertOrReplace(s).ConfigureAwait(false);
                        }
                    }
                }

                var gridItems = CreateDataGridItems(cfg.MaxAgents, WorkerMonitor);
                WorkerMonitor.ItemsSource = gridItems;

                //await Task.Run(() => AddContactsDb(seqs)).ConfigureAwait(false);
                //await Task.Run(() => AdvanceContacts(seqs)).ConfigureAwait(true);
                //GC.Collect();

                var lblOnline = new WpfInt64Label("Online", LblOnline);
                var lblGreets = new WpfInt64Label("Greets", LblGreets);
                var lblConvos = new WpfInt64Label("Convos", LblConvos); ;
                var lblIn = new WpfInt64Label("In", LblIn);
                var lblOut = new WpfInt64Label("Out", LblOut);
                var lblLinks = new WpfInt64Label("Links", LblLinks);
                var lblCompleted = new WpfInt64Label("Completed", LblCompleted);
                var lblRestricts = new WpfInt64Label("Restricts", LblRestricts);

                using var uiUpdater = new WpfUIUpdater(
                    this,
                    gridItems,
                    lblOnline,
                    lblGreets,
                    lblConvos,
                    lblIn,
                    lblOut,
                    lblLinks,
                    lblCompleted,
                    lblRestricts,
                    TxtChatLog
                );
                var statsUI = new WpfStatsUIUpdater(uiUpdater);
                var agentsUI = CreateAgentUIUpdaters(cfg.MaxAgents, uiUpdater);

                using var cts = new CancellationTokenSource();
                using var cg = new SemaphoreSlim(10, 10);
                var directorState = new DirectorState(
                    activeAgents: cfg.MaxAgents,
                    cfg: cfg,
                    sequences: seqs,
                    statsUIUpdater: statsUI,
                    agentUIUpdaters: agentsUI,
                    chatBlacklist: chatBlacklist,
                    greetBlacklist: greetBlacklist,
                    maxConcurrentConnections: 10
                );


                using var director = new Director(directorState);
                _director = director;
                using var _ = _cfgDataGrid.WhenValueChanged.Subscribe(HandleConfigValueUpdated);

                director.ActivateAgents(cfg.MaxAgents);

                await director.WorkCompleted.FirstAsync();
            }
            catch (InvalidOperationException ex)
            {
                MessageBox.Show(
                    ex.Message,
                    "configuration error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error
                );
            }
            finally
            {
                _director = null;
                _chatBlacklist = null;
                _greetBlacklist = null;
                CmdLaunch.IsEnabled = true;
                WorkerMonitor.ItemsSource = SeqModule.Empty<DataGridItem>();
            }

            MessageBox.Show("Director exited");
        }

        private void WorkerMonitor_LoadingRow(object sender, DataGridRowEventArgs e)
        {
            e.Row.Header = (e.Row.GetIndex() + 1);
        }

        private void HandleWindowClosing(object sender, CancelEventArgs e)
        {
            var result = MessageBox.Show("Exit the program?", "Exit", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
            if (result == MessageBoxResult.No || result == MessageBoxResult.Cancel)
            {
                e.Cancel = true;
                return;
            }

            Process.GetCurrentProcess().Kill();
        }

        private void HandleCmdClearChatLogClick(object sender, RoutedEventArgs e)
        {
            TxtChatLog.Clear();
        }

        private void HandleCfgValChanged((string name, ConfigValue value) tuple)
        {
            var (name, value) = tuple;
            if (name != "Script")
                return;

            var seq = (ConfigSequenceValue)value;
            if (!seq.Value.Items.Any())
                return;
            _ = Scripts.InsertOrIgnore(ReadOnlyCollectionModule.OfSeq(seq.Value.Items));
        }

        public MainWindow()
        {
            WpfModule.InjectXaml(this, "mainwindow.xaml");
            _updateCfgLock = new SemaphoreSlim(1, 1);
            _iniCfg = new IniCfg("textplus_chat_agent.ini");
            _cfgDataGrid = CreateConfigDataGrid();
            _ = _cfgDataGrid.WhenValueChanged.Subscribe(HandleCfgValChanged);
            Closing += HandleWindowClosing;
            CmdLaunch.Click += HandleCmdLaunchClick;
            CmdClearChatLog.Click += HandleCmdClearChatLogClick;
            WorkerMonitor.LoadingRow += WorkerMonitor_LoadingRow;
            Title = $"{Title} - {Assembly.GetExecutingAssembly().GetName().Version}";
        }
    }
}
