using LibCyStd;
using LibCyStd.Wpf;
using LibTextPlus.ChatAgent;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using System.Windows.Controls;
using System.Windows.Data;

namespace TextPlus.ChatAgent.Wpf
{
    public class DataGridItem : ViewModelBase
    {
        private string _id;
        private string _status;
        private string _contacts;
        private string _greets;
        private string _in;
        private string _out;

        public string Id
        {
            get => _id;
            set
            {
                _id = value;
                OnPropertyChanged(nameof(Id));
            }
        }

        public string Status
        {
            get => _status;
            set
            {
                _status = value;
                OnPropertyChanged(nameof(Status));
            }
        }

        public string Contacts
        {
            get => _contacts;
            set
            {
                _contacts = value;
                OnPropertyChanged(nameof(Contacts));
            }
        }

        public string Greets
        {
            get => _greets;
            set
            {
                _greets = value;
                OnPropertyChanged(nameof(Greets));
            }
        }

        public string In
        {
            get => _in;
            set
            {
                _in = value;
                OnPropertyChanged(nameof(In));
            }
        }

        public string Out
        {
            get => _out;
            set
            {
                _out = value;
                OnPropertyChanged(nameof(Out));
            }
        }

        public DataGridItem(DataGrid dataGrid)
        {
            _id = "";
            _status = "";
            _contacts = "";
            _greets = "";
            _in = "";
            _out = "";

            var idBinding = new Binding("Id")
            {
                UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
            };
            var statusBinding = new Binding("Status")
            {
                UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
            };
            var contactsBinding = new Binding("Contacts")
            {
                UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
            };
            var greetsBinding = new Binding("Greets")
            {
                UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
            };
            var inBinding = new Binding("In")
            {
                UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
            };
            var outBinding = new Binding("Out")
            {
                UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
            };

            var (col0, col1, col2, col3, col4, col5) =
                ((DataGridTextColumn)dataGrid.Columns[0],
                 (DataGridTextColumn)dataGrid.Columns[1],
                 (DataGridTextColumn)dataGrid.Columns[2],
                 (DataGridTextColumn)dataGrid.Columns[3],
                 (DataGridTextColumn)dataGrid.Columns[4],
                 (DataGridTextColumn)dataGrid.Columns[5]);
            col0.Binding = idBinding;
            col1.Binding = statusBinding;
            col2.Binding = contactsBinding;
            col3.Binding = greetsBinding;
            col4.Binding = inBinding;
            col5.Binding = outBinding;

            Id = "";
            Status = "";
            Contacts = "(0/0)";
            Greets = "0";
            In = "0";
            Out = "0";
        }
    }

    public abstract class WpfUIUpdaterDirective
    {
    }

    public class WpfUIUpdaterUpdateDataGridItemDirective : WpfUIUpdaterDirective
    {
        public int Index { get; }
        public DataGridCol DataGridCol { get; }
        public string Value { get; }
        public WpfUIUpdaterUpdateDataGridItemDirective(int index, DataGridCol dataGridCol, string value)
        {
            Index = index;
            DataGridCol = dataGridCol;
            Value = value;
        }
    }

    public class WpfUIUpdaterAppendChatLog : WpfUIUpdaterDirective
    {
        public string Text { get; }
        public WpfUIUpdaterAppendChatLog(string text)
        {
            Text = text;
        }
    }

    public class WpfUIUpdaterUpdateStatDirective : WpfUIUpdaterDirective
    {
        public StatLabel StatLabel { get; }
        public long Amt { get; }

        public WpfUIUpdaterUpdateStatDirective(StatLabel statLabel, long amt)
        {
            StatLabel = statLabel;
            Amt = amt;
        }
    }

    public class WpfUIUpdaterUpdateWinTitleDirective : WpfUIUpdaterDirective
    {
        public string Title { get; }

        public WpfUIUpdaterUpdateWinTitleDirective(string title)
        {
            Title = title;
        }
    }

    public class WpfUIUpdater : UIUpdater, IDisposable
    {
        private readonly MainWindow _mWindow;
        private readonly ObservableCollection<DataGridItem> _items;
        private readonly WpfInt64Label _lblOnline;
        private readonly WpfInt64Label _lblGreets;
        private readonly WpfInt64Label _lblConvos;
        private readonly WpfInt64Label _lblIn;
        private readonly WpfInt64Label _lblOut;
        private readonly WpfInt64Label _lblLinks;
        private readonly WpfInt64Label _lblCompleted;
        private readonly WpfInt64Label _lblRestricts;
        private readonly TextBox _txtChatLog;
        private readonly BufferBlock<WpfUIUpdaterDirective> _agent;
        private readonly CancellationTokenSource _cTokSrc;

        private Unit UpdateDataGridItem(WpfUIUpdaterUpdateDataGridItemDirective d)
        {
            var (index, col, value) = (d.Index, d.DataGridCol, d.Value);
            _ = col switch
            {
                DataGridCol.Id => _items[index].Id = value,
                DataGridCol.Status => _items[index].Status = value,
                DataGridCol.Contacts => _items[index].Contacts = value,
                DataGridCol.Greets => _items[index].Greets = value,
                DataGridCol.In => _items[index].In = value,
                DataGridCol.Out => _items[index].Out = value,
                _ => throw new ArgumentOutOfRangeException(nameof(col))
            };
            return Unit.Value;
        }

        private Unit UpdateStat(WpfUIUpdaterUpdateStatDirective d)
        {
            var wpfLbl = d.StatLabel switch
            {
                StatLabel.Online => _lblOnline,
                StatLabel.Greets => _lblGreets,
                StatLabel.Conversations => _lblConvos,
                StatLabel.In => _lblIn,
                StatLabel.Out => _lblOut,
                StatLabel.Links => _lblLinks,
                StatLabel.Completed => _lblCompleted,
                StatLabel.Restricts => _lblRestricts,
                _ => throw new InvalidOperationException("invalid StatLabel enum")
            };
            wpfLbl.Value += d.Amt;
            return Unit.Value;
        }

        private Unit UpdateWinTitle(WpfUIUpdaterUpdateWinTitleDirective d)
        {
            _mWindow.Dispatcher.Invoke(
                () => _mWindow.Title = d.Title
            );
            return Unit.Value;
        }

        private Unit AppendChatLog(WpfUIUpdaterAppendChatLog d)
        {
            _txtChatLog.Dispatcher.Invoke(
                () => _txtChatLog.AppendText(d.Text)
            );
            return Unit.Value;
        }

        private void Process(WpfUIUpdaterDirective directive)
        {
            _ = directive switch
            {
                WpfUIUpdaterUpdateDataGridItemDirective d => UpdateDataGridItem(d),
                WpfUIUpdaterUpdateStatDirective d => UpdateStat(d),
                WpfUIUpdaterUpdateWinTitleDirective d => UpdateWinTitle(d),
                WpfUIUpdaterAppendChatLog d => AppendChatLog(d),
                _ => throw new InvalidOperationException("invalid wpf ui updater directive rcvd")
            };
        }

        private async Task Recv()
        {
            try
            {
                using var _ = _cTokSrc;
                while (!_cTokSrc.IsCancellationRequested)
                {
                    var directive = await _agent.ReceiveAsync().ConfigureAwait(false);
                    Process(directive);
                }
            }
            catch (Exception e)
            {
                Environment.FailFast($"wpf ui updater failed with error: {e.GetType().Name} ~ {e.Message}");
            }
        }

        public void Dispose() => _cTokSrc.Cancel();

        public override void UpdateDataGridItem(int index, DataGridCol col, string value) =>
            _agent.Post(new WpfUIUpdaterUpdateDataGridItemDirective(index, col, value));

        public override void UpdateStat(StatLabel lbl, long amt) =>
            _agent.Post(new WpfUIUpdaterUpdateStatDirective(lbl, amt));

        public override void UpdateWinTitle(string title) =>
            _agent.Post(new WpfUIUpdaterUpdateWinTitleDirective(title));

        public override void AppendChatLog(string text) =>
            _agent.Post(new WpfUIUpdaterAppendChatLog(text));

        public WpfUIUpdater(
            MainWindow mWindow,
            ObservableCollection<DataGridItem> items,
            WpfInt64Label lblOnline,
            WpfInt64Label lblGreets,
            WpfInt64Label lblConvos,
            WpfInt64Label lblIn,
            WpfInt64Label lblOut,
            WpfInt64Label lblLinks,
            WpfInt64Label lblCompleted,
            WpfInt64Label lblRestricts,
            TextBox txtChatLog)
        {
            _mWindow = mWindow;
            _items = items;
            _lblOnline = lblOnline;
            _lblGreets = lblGreets;
            _lblConvos = lblConvos;
            _lblIn = lblIn;
            _lblOut = lblOut;
            _lblLinks = lblLinks;
            _lblCompleted = lblCompleted;
            _lblRestricts = lblRestricts;
            _txtChatLog = txtChatLog;
            _agent = new BufferBlock<WpfUIUpdaterDirective>();
            _cTokSrc = new CancellationTokenSource();
            _ = Recv();
        }
    }

    public class WpfStatsUIUpdater : StatsUIUpdater
    {
        private readonly UIUpdater _uiUpdater;

        public override void IncrementCompleted() => _uiUpdater.UpdateStat(StatLabel.Completed, 1);

        public override void IncrementConvos() => _uiUpdater.UpdateStat(StatLabel.Conversations, 1);

        public override void IncrementGreets() => _uiUpdater.UpdateStat(StatLabel.Greets, 1);

        public override void IncrementIn() => _uiUpdater.UpdateStat(StatLabel.In, 1);

        public override void IncrementLinks() => _uiUpdater.UpdateStat(StatLabel.Links, 1);

        public override void IncrementOnline() => _uiUpdater.UpdateStat(StatLabel.Online, 1);

        public override void DecrementOnline() => _uiUpdater.UpdateStat(StatLabel.Online, -1);

        public override void IncrementOut() => _uiUpdater.UpdateStat(StatLabel.Out, 1);

        public override void IncrementRestricts() => _uiUpdater.UpdateStat(StatLabel.Restricts, 1);

        public WpfStatsUIUpdater(UIUpdater ui) => _uiUpdater = ui;
    }

    public class WpfAgentUIUpdater : AgentUIUpdater
    {
        private readonly int _index;
        private readonly UIUpdater _uiUpdater;

        public override void UpdateContacts(string value) => _uiUpdater.UpdateDataGridItem(_index, DataGridCol.Contacts, value);

        public override void UpdateGreets(string value) => _uiUpdater.UpdateDataGridItem(_index, DataGridCol.Greets, value);

        public override void UpdateId(string value) => _uiUpdater.UpdateDataGridItem(_index, DataGridCol.Id, value);

        public override void UpdateIn(string value) => _uiUpdater.UpdateDataGridItem(_index, DataGridCol.In, value);

        public override void UpdateOut(string value) => _uiUpdater.UpdateDataGridItem(_index, DataGridCol.Out, value);

        public override void UpdateStatus(string value) => _uiUpdater.UpdateDataGridItem(_index, DataGridCol.Status, value);

        public WpfAgentUIUpdater(int index, UIUpdater ui)
        {
            _index = index;
            _uiUpdater = ui;
        }
    }
}
