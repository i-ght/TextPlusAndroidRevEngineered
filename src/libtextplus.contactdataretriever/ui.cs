using System;

namespace LibTextPlus.ContactDataRetriever
{
    public enum StatLabel
    {
        Online,
        Retrieved
    }

    public enum DataGridCol
    {
        Id,
        Status
    }


    public abstract class UIUpdater
    {
        public abstract void UpdateStat(StatLabel lbl, long amt);
        public abstract void UpdateWinTitle(string title);
        public abstract void UpdateDataGridItem(int index, DataGridCol col, string value);
    }


    public abstract class StatsUIUpdater
    {
        public abstract void IncrementOnline();
        public abstract void DecrementOnline();
        public abstract void IncrementRetrieved(long amt);
    }

    public abstract class AgentUIUpdater
    {
        public abstract void UpdateId(string value);
        public abstract void UpdateStatus(string value);
    }
}
