using LibCyStd;
using LibCyStd.IO;
using LibCyStd.Net;
using LibCyStd.Seq;
using LibCyStd.Wpf;
using LibTextPlus;
using LibTextPlus.Creator;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.WebSockets;
using System.Reactive.Linq;
using System.Reflection;
using System.Threading;
using System.Web;
using System.Windows;
using System.Windows.Controls;

namespace TextPlus.Creator.Wpf
{
    using StrKvp = ValueTuple<string, string>;

    public class MainWindow : Window
    {
        private static ReadOnlyCollection<TextPlusAndroidDevice> Devices { get; }

        static MainWindow()
        {
            Devices =
                ReadOnlyCollectionModule.OfSeq(
                    WpfModule.ReadResrcAsCollection("txtplusdev.csv")
                    .Choose(TextPlusAndroidDevice.TryParse)
                );
            if (Devices.Count == 0)
                ExnModule.InvalidOp("failed to load device info from resources.");
        }

        private readonly LibCyStd.Wpf.IniCfg _iniCfg;
        private readonly ConfigDataGrid _cfgDataGrid;
        private readonly WpfInt64Label _lblAttempts;
        private readonly WpfInt64Label _lblCreated;

        private Grid GrdCfgContent => (Grid)FindName("GrdCfgContent");
        private Label LblAttempts => (Label)FindName("LblAttempts");
        private Label LblCreated => (Label)FindName("LblCreated");
        private MenuItem CmdLaunch => (MenuItem)FindName("CmdLaunch");
        private DataGrid WorkerMonitor => (DataGrid)FindName("WorkerMonitor");

        private void MainWindow_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            _iniCfg.Save();
            Process.GetCurrentProcess().Kill();
        }

        private ConfigDataGrid CreateConfigDataGrid()
        {
            var items = ReadOnlyCollectionModule.OfSeq(new[]
            {
                new ConfigDataGridItem("Max workers", "int", 1),
                new ConfigDataGridItem("Max creates", "int", 1),
                new ConfigDataGridItem("Get textplus sms number?", "bool", false),
                new ConfigDataGridItem("gcm provider method (req | file)", "string", "req"),
                new ConfigDataGridItem("Connect to jabber server?", "bool", true),
                new ConfigDataGridItem("gcm token request", "sequence", ConfigSequence.Empty),
                new ConfigDataGridItem("proxies", "sequence", ConfigSequence.Empty),
                new ConfigDataGridItem("words1", "sequence", ConfigSequence.Empty),
                new ConfigDataGridItem("words2", "sequence", ConfigSequence.Empty),
                new ConfigDataGridItem("gcm tokens", "sequence", ConfigSequence.Empty),
            });
            return new ConfigDataGrid(items, GrdCfgContent, _iniCfg);
        }

        private static LibCyStd.Option<GcmProviderMethod> TryParseGcmProviderMethod(string input)
        {
            if (input.InvariantEquals("req")) return GcmProviderMethod.Request;
            if (input.InvariantEquals("file")) return GcmProviderMethod.File;
            if (!Enum.TryParse<GcmProviderMethod>(input, out var result))
                return LibCyStd.Option.None;
            return result;
        }

        private static ReadOnlyCollection<StrKvp> TryParseHeaders(string input)
        {
            var headerItems = input.Split("\r\n".ToCharArray(), StringSplitOptions.RemoveEmptyEntries);
            if (headerItems.Length <= 1)
                return ReadOnlyCollectionModule.OfSeq(SeqModule.Empty<(string k, string v)>());

            var lst = new List<StrKvp>();
            for (var i = 1; i < headerItems.Length; i++)
            {
                var item = headerItems[i];
                if (!item.Contains(":"))
                    continue;
                var sp = item.Split(":".ToCharArray(), StringSplitOptions.RemoveEmptyEntries);
                if (sp.Length <= 1)
                    continue;

                var pair = (sp[0], string.Join(":", sp.Skip(1)));
                lst.Add(pair);
            }

            return ReadOnlyCollectionModule.OfSeq(lst);
        }

        private static LibCyStd.Option<Dictionary<string, string>> TryParseContent(string input)
        {
            var sp = input.Split("&".ToCharArray(), StringSplitOptions.RemoveEmptyEntries);
            if (sp.Length <= 1)
                return LibCyStd.Option.None;

            var lst = new List<StrKvp>(sp.Length);
            foreach (var item in sp)
            {
                var s = item.Split('=');
                if (s.Length <= 1)
                    continue;
                var val = string.Concat(s.Skip(1));
                var pair = (s[0], Uri.UnescapeDataString(val));
                lst.Add(pair);
            }

            if (lst.Count == 0)
                return LibCyStd.Option.None;

            var dict = new Dictionary<string, string>(lst.Count);
#pragma warning disable CS8619 // Nullability of reference types in value doesn't match target type.
            foreach (var (k, v) in lst)
                dict.Add(k, v);
#pragma warning restore CS8619 // Nullability of reference types in value doesn't match target type.
            return dict;
        }

        internal static LibCyStd.Option<GcmReqInfo> TryParseGcmRegisterReq(string input)
        {
            if (string.IsNullOrWhiteSpace(input) || !input.Contains("/c2dm/register3"))
                return LibCyStd.Option.None;

            var span = input.AsSpan();
            var indexOfDelim = span.IndexOf("\r\n\r\n".ToCharArray());
            if (indexOfDelim <= -1)
                return LibCyStd.Option.None;

            var headersData = span.Slice(0, indexOfDelim);
            var headersStr = headersData.ToString();

            var headers = TryParseHeaders(headersStr);
            if (headers.Count == 0)
                return LibCyStd.Option.None;

            var contentData = span.Slice(indexOfDelim + 4);
            var contentStr = contentData.ToString();

            var contentOpt = TryParseContent(contentStr);
            if (contentOpt.IsNone)
                return LibCyStd.Option.None;

            var filteredHeaders = headers.Choose(pair =>
            {
                if (pair.Item1.InvariantEquals("host") || pair.Item1.InvariantEquals("content-length") || pair.Item1.InvariantEquals("connection"))
                    return LibCyStd.Option.None;
                return LibCyStd.Option.Some(pair);
            });
            if (!filteredHeaders.Any())
                return LibCyStd.Option.None;

            return new GcmReqInfo(headers, contentOpt.Value);
        }

        private static void ValidateSeqs(Cfg cfg, Seqs seqs)
        {
            if (seqs.Proxies.Count == 0)
                ExnModule.InvalidOp("proxies file does not exist, is empty, or has invalid lines");
            if (seqs.Words1.Count == 0)
                ExnModule.InvalidOp("words1 file does not exist or is empty.");
            if (seqs.Words2.Count == 0)
                ExnModule.InvalidOp("words2 file does not exist or is empty.");
            if (seqs.AndroidDevices.Count == 0)
                ExnModule.InvalidOp("android devices file not found.");
            if (cfg.GetTextPlusNumber && seqs.GcmTokens.Count == 0 && cfg.GcmProviderMethod == GcmProviderMethod.File)
                ExnModule.InvalidOp("gcm tokens required to get a sms phone number from textplus.");
        }

        private Seqs CreateSeqs(Cfg cfg)
        {
            var devices = QueueModule.OfSeq(Devices);
            var proxies = QueueModule.OfSeq(_cfgDataGrid.GetValue<ConfigSequence>("proxies").Items.Choose(SysModule.TryParseProxyOpt));
            var words1 = QueueModule.OfSeq(_cfgDataGrid.GetValue<ConfigSequence>("words1").Items);
            var words2 = QueueModule.OfSeq(_cfgDataGrid.GetValue<ConfigSequence>("words2").Items);
            var gcmTokens = QueueModule.OfSeq(_cfgDataGrid.GetValue<ConfigSequence>("gcm tokens").Items);

            devices.Shuffle();
            proxies.Shuffle();
            words1.Shuffle();
            words2.Shuffle();
            gcmTokens.Shuffle();

            var seqs = new Seqs(
                devices,
                proxies,
                words1,
                words2,
                gcmTokens
            );
            ValidateSeqs(cfg, seqs);
            return seqs;
        }

        private static void ValidateCfg(Cfg cfg)
        {
            if (cfg.MaxCreates <= 0)
                ExnModule.InvalidOp("Max creates must be > 0.");
            if (cfg.MaxWorkers <= 0)
                ExnModule.InvalidOp("Max workers must be > 0.");
            if (cfg.GcmProviderMethod == GcmProviderMethod.Request && cfg.GcmReq.IsNone)
                ExnModule.InvalidOp("failed to parse gcm token request.");
        }

        private Cfg CreateCfg()
        {
            var maxWorkers = _cfgDataGrid.GetValue<int>("Max workers");
            var maxCreates = _cfgDataGrid.GetValue<int>("Max creates");
            var getNum = _cfgDataGrid.GetValue<bool>("Get textplus sms number?");
            var connJabber = _cfgDataGrid.GetValue<bool>("Connect to jabber server?");
            var gcmProviderTypeStr = _cfgDataGrid.GetValue<string>("gcm provider method (req | file)");
            var gcmProvOpt = TryParseGcmProviderMethod(gcmProviderTypeStr);
            if (gcmProvOpt.IsNone)
                ExnModule.InvalidOp("invalid gcm provider type. valid values are req or file.");
            var gcmReqSeq = _cfgDataGrid.GetValue<ConfigSequence>("gcm token request");
            var gcmLines = string.Join("\r\n", gcmReqSeq.Items);
            var gcmReq = TryParseGcmRegisterReq(gcmLines);
            var cfg = new Cfg(maxWorkers, maxCreates, mobileCountryCode: 1, getNum, gcmProvOpt.Value, gcmReq, connJabber);
            ValidateCfg(cfg);
            return cfg;
        }

        private static ObservableCollection<WpfTxtPlusCreatorDataGridItem> CreateDataGridItems(int cnt, DataGrid dataGrid)
        {
            var lst = new List<WpfTxtPlusCreatorDataGridItem>(cnt);
            for (var i = 0; i < cnt; i++)
                lst.Add(new WpfTxtPlusCreatorDataGridItem(dataGrid));
            return new ObservableCollection<WpfTxtPlusCreatorDataGridItem>(lst);
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
                var seqs = CreateSeqs(cfg);
                var gridItems = CreateDataGridItems(cfg.MaxWorkers, WorkerMonitor);
                WorkerMonitor.ItemsSource = gridItems;
                using var uiUpdater = new WpfUIUpdater(this, gridItems, _lblAttempts, _lblCreated);
                var statsUI = new WpfStatsUIUpdater(uiUpdater);
                var agentUIUpdaters = CreateAgentUIUpdaters(cfg.MaxWorkers, uiUpdater);
                using var cts = new CancellationTokenSource();
                var dCfg = new DirectorCfg(
                    statsUI,
                    agentUIUpdaters,
                    cts.Token,
                    cfg,
                    seqs,
                    new AlwaysValidVerifierClientProvider()
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
                CmdLaunch.IsEnabled = true;
                WorkerMonitor.ItemsSource = SeqModule.Empty<WpfTxtPlusCreatorDataGridItem>();
            }
        }

        private void WorkerMonitor_LoadingRow(object sender, DataGridRowEventArgs e)
        {
            e.Row.Header = (e.Row.GetIndex() + 1);
        }

        public MainWindow()
        {
            WpfModule.InjectXaml(this, "mainwindow.xaml");
            _iniCfg = new LibCyStd.Wpf.IniCfg("textplus_creator_settings.ini");
            _cfgDataGrid = CreateConfigDataGrid();
            _lblAttempts = new WpfInt64Label("Attempts", LblAttempts);
            _lblCreated = new WpfInt64Label("Created", LblCreated);
            Closing += MainWindow_Closing;
            CmdLaunch.Click += CmdLaunch_Click;
            WorkerMonitor.LoadingRow += WorkerMonitor_LoadingRow;
            Title = $"{Title} - {Assembly.GetExecutingAssembly().GetName().Version}";
        }
    }
}
