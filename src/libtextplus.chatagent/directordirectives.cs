using LibCyStd;
using System;
using System.Collections.Generic;
using System.Text;

namespace LibTextPlus.ChatAgent
{
    public class Briefing
    {
        public AgentState State { get; }
        public Exception? Error { get; }

        public Briefing(
            AgentState state,
            Exception? error)
        {
            State = state;
            Error = error;
        }
    }

    public abstract class DirectorDirective { }

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
        public Sequences Seqs { get; }

        public SetSeqsDirective(Sequences seqs) { Seqs = seqs; }
    }

    public class GetSeqsDirective : DirectorDirective
    {
        public Action<Sequences> Reply { get; }

        public GetSeqsDirective(Action<Sequences> replyChannel) { Reply = replyChannel; }
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
}
