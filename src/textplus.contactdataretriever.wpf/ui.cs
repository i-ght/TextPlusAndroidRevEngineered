using LibCyStd;
using LibCyStd.Wpf;
using LibTextPlus.ContactDataRetriever;
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
using Unit = LibCyStd.Unit;


namespace TextPlus.ContactDataRetriever.Wpf
{
    public class DataGridItem : ViewModelBase
    {
        private string _id;
        private string _status;

        public DataGridItem(DataGrid dataGrid)
        {
            _id = "";
            _status = "";

            var idBinding = new Binding("Id")
            {
                UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
            };
            var statusBinding = new Binding("Status")
            {
                UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
            };
            var (col0, col1) = ((DataGridTextColumn)dataGrid.Columns[0], (DataGridTextColumn)dataGrid.Columns[1]);
            col0.Binding = idBinding;
            col1.Binding = statusBinding;
        }

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
        private readonly WpfInt64Label _onlineLbl;
        private readonly WpfInt64Label _retrLbl;
        private readonly BufferBlock<WpfUIUpdaterDirective> _agent;
        private readonly CancellationTokenSource _cTokSrc;

        private Unit UpdateDataGridItem(WpfUIUpdaterUpdateDataGridItemDirective d)
        {
            var (index, col, value) = (d.Index, d.DataGridCol, d.Value);
            _ = col switch
            {
                DataGridCol.Id => _items[index].Id = value,
                DataGridCol.Status => _items[index].Status = value,
                _ => throw new ArgumentOutOfRangeException(nameof(col))
            };
            return Unit.Value;
        }

        private Unit UpdateStat(WpfUIUpdaterUpdateStatDirective d)
        {
            var wpfLbl = d.StatLabel switch
            {
                StatLabel.Online => _onlineLbl,
                StatLabel.Retrieved => _retrLbl,
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

        private void Process(WpfUIUpdaterDirective directive)
        {
            _ = directive switch
            {
                WpfUIUpdaterUpdateDataGridItemDirective d => UpdateDataGridItem(d),
                WpfUIUpdaterUpdateStatDirective d => UpdateStat(d),
                WpfUIUpdaterUpdateWinTitleDirective d => UpdateWinTitle(d),
                _ => throw new InvalidOperationException("invalid wpf ui updater directive rcvd")
            };
        }

        private async Task Recv()
        {
            try
            {
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

        public void Dispose()
        {
            _cTokSrc.Cancel();
            _cTokSrc.Dispose();
        }

        public override void UpdateDataGridItem(int index, DataGridCol col, string value) =>
            _agent.Post(new WpfUIUpdaterUpdateDataGridItemDirective(index, col, value));

        public override void UpdateStat(StatLabel lbl, long amt) =>
            _agent.Post(new WpfUIUpdaterUpdateStatDirective(lbl, amt));

        public override void UpdateWinTitle(string title) =>
            _agent.Post(new WpfUIUpdaterUpdateWinTitleDirective(title));

        public WpfUIUpdater(
            MainWindow mWindow,
            ObservableCollection<DataGridItem> items,
            WpfInt64Label onlineLbl,
            WpfInt64Label retrLbl)
        {
            _mWindow = mWindow;
            _onlineLbl = onlineLbl;
            _retrLbl = retrLbl;
            _items = items;
            _agent = new BufferBlock<WpfUIUpdaterDirective>();
            _cTokSrc = new CancellationTokenSource();
            _ = Recv();
        }
    }

    public class WpfStatsUIUpdater : StatsUIUpdater
    {
        private readonly WpfUIUpdater _uiUpdater;
        public WpfStatsUIUpdater(WpfUIUpdater uiUpdater) { _uiUpdater = uiUpdater; }
        public override void DecrementOnline() => _uiUpdater.UpdateStat(StatLabel.Online, -1);
        public override void IncrementOnline() => _uiUpdater.UpdateStat(StatLabel.Online, 1);

        public override void IncrementRetrieved(long amt) => _uiUpdater.UpdateStat(StatLabel.Retrieved, amt);
    }

    public class WpfAgentUIUpdater : AgentUIUpdater
    {
        private readonly int _index;
        private readonly WpfUIUpdater _uiUpdater;

        public WpfAgentUIUpdater(
            int index,
            WpfUIUpdater uiUpdater)
        {
            _index = index;
            _uiUpdater = uiUpdater;
        }

        public override void UpdateId(string value) =>
            _uiUpdater.UpdateDataGridItem(_index, DataGridCol.Id, value);


        public override void UpdateStatus(string value) =>
            _uiUpdater.UpdateDataGridItem(_index, DataGridCol.Status, value);
    }
}
