using Dapper;
using LibCyStd;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SQLite;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;

namespace LibTextPlus.ChatAgent
{
    public abstract class SQLiteDirective
    {

    }

    public class InvokeStateDirective : SQLiteDirective
    {
        public Action<SQLiteAgentState, object?> InvokeState { get; }
        public object? UserData { get; }
        public InvokeStateDirective(Action<SQLiteAgentState, object?> modifyState, object? userData = null)
        {
            InvokeState = modifyState;
            UserData = userData;
        }
    }

    public class SQLiteAgentState
    {
        private readonly Func<string, SQLiteConnection> _initConn;
        private readonly string _connectionStr;

        public BufferBlock<SQLiteDirective> Agent { get; } = new BufferBlock<SQLiteDirective>();
        public CancellationTokenSource Cts { get; } = new CancellationTokenSource();
        public SQLiteConnection? Conn { get; private set; }

        public void InitConn()
        {
            if (Conn != null)
                return;

            Conn = _initConn(_connectionStr);
        }

        public SQLiteAgentState(Func<string, SQLiteConnection> initConn, string connStr)
        {
            _connectionStr = connStr;
            _initConn = initConn;
        }
    }

    internal static class Utils
    {
    }

    internal static class SQLiteAgentModule
    {
        private static void Post(SQLiteAgentState state, SQLiteDirective dir)
        {
            if (!state.Agent.Post(dir))
                throw new InvalidOperationException("BufferBlock post returned false unexpectedly.");
        }

        public static async Task<TResult> ExecSQL<TResult>(
            SQLiteAgentState state,
            Func<SQLiteConnection, TResult> executor)
        {
            var tcs = new TaskCompletionSource<TResult>();
            static void HandleState(SQLiteAgentState state, object? userData)
            {
                (TaskCompletionSource<TResult> tcs, Func<SQLiteConnection, TResult> executor) = ((TaskCompletionSource<TResult>,  Func<SQLiteConnection, TResult>))(userData!);
                try
                {
                    var result = executor(state.Conn!);
                    tcs.SetResult(result);
                }
                catch (Exception e)
                {
                    tcs.SetException(e);
                }
            }
            var q = new InvokeStateDirective(
                HandleState,
                (tcs, executor)
            );
            Post(state, q);
            return await tcs.Task.ConfigureAwait(false);
        }
    }

    public class SQLiteAgent : IDisposable
    {
        private readonly SQLiteAgentState _state;

        private bool _disposed;

        public async Task<TResult> ExecuteSQL<TResult>(
            Func<SQLiteConnection, TResult> executor)
        {
            return await SQLiteAgentModule.ExecSQL<TResult>(
                _state,
                executor
            ).ConfigureAwait(false);
        }

        public async Task<int> ExecuteSQL(
            string sql)
        {
            int Exec(SQLiteConnection conn) => conn.Execute(sql);
            return await ExecuteSQL(Exec).ConfigureAwait(false);
        }

        private Unit Invoke(InvokeStateDirective inv)
        {
            inv.InvokeState(_state, inv.UserData);
            return Unit.Value;
        }

        private void ProcessDirective(SQLiteDirective directive)
        {
            _ = directive switch {
                InvokeStateDirective inv => Invoke(inv),
                _ => throw new ArgumentException(nameof(directive))
            };
        }

        private async Task Recv()
        {
            _state.InitConn();
            try
            {
                while (!_state.Cts.IsCancellationRequested)
                {
                    var directive = await _state.Agent.ReceiveAsync(_state.Cts.Token).ConfigureAwait(true);
                    ProcessDirective(directive);
                }
            }
            catch (OperationCanceledException) {/*ignored*/}
        }

        private async void Start()
        {
            await Recv().ConfigureAwait(false);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _state.Cts.Cancel();
        }

        public SQLiteAgent(SQLiteAgentState state)
        {
            _state = state;
            new Thread(Start).Start();
        }
    }
}
