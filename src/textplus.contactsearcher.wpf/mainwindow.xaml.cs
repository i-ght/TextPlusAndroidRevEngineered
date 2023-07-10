using LibCyStd;
using LibCyStd.IO;
using LibCyStd.Net;
using LibCyStd.Seq;
using LibCyStd.Wpf;
using LibTextPlus;
using LibTextPlus.ContactSearcher;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reactive.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace TextPlus.ContactSearcher.Wpf
{
    public class MainWindow : Window
    {
        private readonly LibCyStd.Wpf.IniCfg _iniCfg;
        private readonly ConfigDataGrid _cfgDataGrid;
        private readonly WpfInt64Label _lblOnline;
        private readonly WpfInt64Label _lblSearches;
        private readonly WpfInt64Label _lblValid;

        private LibCyStd.Option<WriteWorker> _maleWriteWorkerOpt;
        private LibCyStd.Option<WriteWorker> _femaleWriteWorkerOpt;
        private LibCyStd.Option<WriteWorker> _unkWriteWorkerOpt;

        private Label LblOnline => (Label)FindName(nameof(LblOnline));
        private Label LblSearches => (Label)FindName(nameof(LblSearches));
        private Label LblValid => (Label)FindName(nameof(LblValid));
        public Grid GrdCfgContent => (Grid)FindName(nameof(GrdCfgContent));
        private MenuItem CmdLaunch => (MenuItem)FindName(nameof(CmdLaunch));
        private DataGrid WorkerMonitor => (DataGrid)FindName(nameof(WorkerMonitor));

        private ConfigDataGrid CreateConfigDataGrid()
        {
            var items = ReadOnlyCollectionModule.OfSeq(new[]
            {
                new ConfigDataGridItem("Max workers", "int", 1),
                new ConfigDataGridItem("Min searches per session", "int", 20),
                new ConfigDataGridItem("Max searches per session", "int", 44),
                new ConfigDataGridItem("Min delay before each search in seconds", "int", 5),
                new ConfigDataGridItem("Max delay before each search in seconds", "int", 11),
                new ConfigDataGridItem("Cool down in minutes after each account finishes session", "int", 5),
                new ConfigDataGridItem("Sessions", "Sequence", ConfigSequence.Empty),
                new ConfigDataGridItem("Proxies", "Sequence", ConfigSequence.Empty),
                new ConfigDataGridItem("Contacts", "Sequence", ConfigFile.Empty)
            });
            return new ConfigDataGrid(items, GrdCfgContent, _iniCfg);
        }

        private static void ValidateCfg(Cfg cfg)
        {
            if (cfg.MaxWorkers <= 0)
                ExnModule.InvalidOp("max workers must be > 0.");
            if (cfg.MinSearchesPerSession <= 0)
                ExnModule.InvalidOp("min searches must be > 0");
            if (cfg.MaxSearchesPerSession <= 0)
                ExnModule.InvalidOp("min searches must be > 0");
            if (cfg.MaxSearchesPerSession < cfg.MinSearchesPerSession)
                ExnModule.InvalidOp("min searches must be <= max searches.");
            if (cfg.MinDelayBetweenContactSearch <= 0)
                ExnModule.InvalidOp("min delay must be > 0");
            if (cfg.MaxDelayBetweenContactSearch <= 0)
                ExnModule.InvalidOp("max delay must be > 0");
            if (cfg.MaxDelayBetweenContactSearch < cfg.MinDelayBetweenContactSearch)
                ExnModule.InvalidOp("min delay must be <= max delay.");
            if (cfg.DelayInMinsBetweenSearches <= 0)
                ExnModule.InvalidOp("cool down must be > 0");
        }

        public static void ValidateSeqs(Seqs seqs)
        {
            if (seqs.Sessions.Count == 0)
                ExnModule.InvalidOp("sessions file does not exist, has invalid lines or is empty");
            if (seqs.Proxies.Count == 0)
                ExnModule.InvalidOp("proxies file does not exist, has invalid lines or is empty");
            if (seqs.Contacts.EndOfStream)
                ExnModule.InvalidOp("cannot read contacts stream. is file empty?");
        }

        private Seqs CreateSeqs()
        {
            var sessions = QueueModule.OfSeq(
                _cfgDataGrid.GetValue<ConfigSequence>("Sessions").Items.Choose(AuthTextPlusSession.TryParseFromJson)
            );
            var proxies = QueueModule.OfSeq(
                _cfgDataGrid.GetValue<ConfigSequence>("Proxies").Items.Choose(SysModule.TryParseProxyOpt)
            );
            var contacts = new StreamReader(
                _cfgDataGrid.GetValue<ConfigFile>("Contacts").FileInfo.FullName
            );

            var seqs = new Seqs(
                contacts,
                sessions,
                proxies
            );
            ValidateSeqs(seqs);
            return seqs;
        }


        private Cfg CreateCfg()
        {
            var maxWorkers = _cfgDataGrid.GetValue<int>("Max workers");
            var minSearches = _cfgDataGrid.GetValue<int>("Min searches per session");
            var maxSearches = _cfgDataGrid.GetValue<int>("Max searches per session");
            var minDelay = _cfgDataGrid.GetValue<int>("Min delay before each search in seconds");
            var maxDelay = _cfgDataGrid.GetValue<int>("Max delay before each search in seconds");
            var cooldown = _cfgDataGrid.GetValue<int>("Cool down in minutes after each account finishes session");
            var cfg = new Cfg(
                maxWorkers,
                minSearches,
                maxSearches,
                cooldown,
                minDelay,
                maxDelay,
                SearchType.Username
            );
            ValidateCfg(cfg);
            return cfg;
        }


        private void MainWindow_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            _iniCfg.Save();
            if (_maleWriteWorkerOpt.IsSome)
                _maleWriteWorkerOpt.Value.Write();
            if (_femaleWriteWorkerOpt.IsSome)
                _femaleWriteWorkerOpt.Value.Write();
            if (_unkWriteWorkerOpt.IsSome)
                _unkWriteWorkerOpt.Value.Write();
            Process.GetCurrentProcess().Kill();
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

        private async void CmdLaunch_Click(object sender, RoutedEventArgs e)
        {
            if (!CmdLaunch.IsEnabled)
                return;

            CmdLaunch.IsEnabled = false;

            try
            {
                var cfg = CreateCfg();
                using var seqs = CreateSeqs();
                var gridItems = CreateDataGridItems(cfg.MaxWorkers, WorkerMonitor);
                WorkerMonitor.ItemsSource = gridItems;

                using var uiUpdater = new WpfUIUpdater(this, gridItems, _lblOnline, _lblSearches, _lblValid);
                using var contactBlacklist = new Blacklist(new FileBlacklist("txtplus-search-contact-blacklist.txt"));
                await Task.Run(() => contactBlacklist.Load()).ConfigureAwait(true);
                using var maleOutput = new WriteWorker("textplus-contacts-male.txt");
                using var femaleOutput = new WriteWorker("textplus-contacts-female.txt");
                using var unkOutput = new WriteWorker("textplus-contacts-unk.txt");

                _maleWriteWorkerOpt = maleOutput;
                _femaleWriteWorkerOpt = femaleOutput;
                _unkWriteWorkerOpt = unkOutput;
                var statsUI = new WpfStatsUIUpdater(uiUpdater);
                var agentUIUpdaters = CreateAgentUIUpdaters(cfg.MaxWorkers, uiUpdater);
                using var cts = new CancellationTokenSource();
                var dCfg = new DirectorCfg(
                    statsUI,
                    agentUIUpdaters,
                    cts.Token,
                    cfg,
                    seqs,
                    contactBlacklist,
                    maleOutput,
                    femaleOutput,
                    unkOutput
                );
                var director = new Director(dCfg);
                director.ActivateAgents();
                await director.WhenWorkComplete.FirstAsync();
                MessageBox.Show("Work complete", "Work complete");
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
                _maleWriteWorkerOpt = LibCyStd.Option.None;
                _femaleWriteWorkerOpt = LibCyStd.Option.None;
                _unkWriteWorkerOpt = LibCyStd.Option.None;
                CmdLaunch.IsEnabled = true;
                WorkerMonitor.ItemsSource = SeqModule.Empty<DataGridItem>();
            }
        }

        public MainWindow()
        {
            WpfModule.InjectXaml(this, "mainwindow.xaml");
            _iniCfg = new LibCyStd.Wpf.IniCfg("textplus_contact_searcher_settings.ini");
            _cfgDataGrid = CreateConfigDataGrid();
            _lblOnline = new WpfInt64Label("Online", LblOnline);
            _lblSearches = new WpfInt64Label("Searches", LblSearches);
            _lblValid = new WpfInt64Label("Valid", LblValid);
            Closing += MainWindow_Closing;
            CmdLaunch.Click += CmdLaunch_Click;
            Title = $"{Title} - {Assembly.GetExecutingAssembly().GetName().Version}";
            _maleWriteWorkerOpt = LibCyStd.Option.None;
            _femaleWriteWorkerOpt = LibCyStd.Option.None;
            _unkWriteWorkerOpt = LibCyStd.Option.None;
        }
    }
}
