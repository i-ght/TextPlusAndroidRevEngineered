using System;

namespace LibTextPlus.Creator
{
    public enum StatLabel
    {
        Attempts,
        Created
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
        public abstract void IncrementAttempts();
        public abstract void IncrementCreated();
    }

    public abstract class AgentUIUpdater
    {
        public abstract void UpdateId(string value);
        public abstract void UpdateStatus(string value);
    }
}
