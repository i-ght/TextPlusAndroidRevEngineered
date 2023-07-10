using System;

namespace LibTextPlus.ChatAgent
{
    public enum StatLabel
    {
        Online,
        Greets,
        Conversations,
        In,
        Out,
        Links,
        Completed,
        Restricts
    }

    public enum DataGridCol
    {
        Id,
        Status,
        Contacts,
        Greets,
        In,
        Out
    }


    public abstract class UIUpdater
    {
        public abstract void UpdateStat(StatLabel lbl, long amt);
        public abstract void UpdateWinTitle(string title);
        public abstract void UpdateDataGridItem(int index, DataGridCol col, string value);
        public abstract void AppendChatLog(string text);
    }


    public abstract class StatsUIUpdater
    {
        public abstract void IncrementOnline();
        public abstract void DecrementOnline();
        public abstract void IncrementGreets();
        public abstract void IncrementConvos();
        public abstract void IncrementIn();
        public abstract void IncrementOut();
        public abstract void IncrementLinks();
        public abstract void IncrementCompleted();
        public abstract void IncrementRestricts();
    }

    public abstract class AgentUIUpdater
    {
        public abstract void UpdateId(string value);
        public abstract void UpdateStatus(string value);
        public abstract void UpdateContacts(string value);
        public abstract void UpdateGreets(string value);
        public abstract void UpdateIn(string value);
        public abstract void UpdateOut(string value);
    }
}
