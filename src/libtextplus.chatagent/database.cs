using Dapper;
using LibCyStd;
using LibCyStd.Seq;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Data;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace LibTextPlus.ChatAgent
{
#pragma warning disable CS8618 // Non-nullable field is uninitialized.
    public class SessionEntity
    {
        private long _validAt;

        public long? Index { get; set; }
        public string Jid { get; set; }
        public string AuthToken { get; set; }
        public bool Invalid { get; set; }

        public long ValidAt
        {
            get => _validAt;
            set
            {
                _validAt = value;
                ValidAtDto = DateTimeOffset.FromUnixTimeMilliseconds((long)value!);
            }
        }

        [IgnoreDataMember]
        public DateTimeOffset ValidAtDto { get; private set; }
    }

    public class ScriptEntity
    {
        private readonly List<string> _lines;
        private readonly ReadOnlyCollection<string> _readonlyLines;

        public long? Index { get; set; }
        public string Checksum { get; set; }
        public string Data { get; set; }

        [IgnoreDataMember]
        public ReadOnlyCollection<string> Lines
        {
            get
            {
                if (Data == null || _readonlyLines.Count > 0)
                    return _readonlyLines;

                foreach (var line in Data.SplitRemoveEmpty(Environment.NewLine))
                    _lines.Add(line);
                return _readonlyLines;
            }
        }

        public ScriptEntity()
        {
            _lines = new List<string>();
            _readonlyLines = ReadOnlyCollectionModule.OfSeq(_lines);
        }
    }

    public class ScriptStateEntitiy
    {
        private readonly List<string> _kwsRespondedTo;
        private readonly ReadOnlyCollection<string> _roKwsRespondedTo;

        public string Id { get; set; }
        public string LocalId { get; set; }
        public string RemoteId { get; set; }
        public string ScriptChecksum { get; set; }
        public int ScriptIndex { get; set; }
        public bool Pending { get; set; }
        public string CurrentReply { get; set; }
        public bool CurrentReplyIsInResponseToKw { get; set; }
        public string ThreadId { get; set; }
        public string KeywordsRespondedTo { get; set; }

        [IgnoreDataMember]
        public ReadOnlyCollection<string> KwsRespondedTo
        {
            get
            {
                if (string.IsNullOrWhiteSpace(KeywordsRespondedTo))
                    return _roKwsRespondedTo;

                if (_roKwsRespondedTo.Count == 0)
                {
                    var sp = KeywordsRespondedTo.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                    foreach (var item in sp)
                        _kwsRespondedTo.Add(item);
                }
                return _roKwsRespondedTo;
            }
        }

        public ScriptStateEntitiy()
        {
            _kwsRespondedTo = new List<string>();
            _roKwsRespondedTo = ReadOnlyCollectionModule.OfSeq(_kwsRespondedTo);
        }
    }
#pragma warning restore CS8618 // Non-nullable field is uninitialized.

    public static class Database
    {
        public static SQLiteAgent Agent { get; }

        private static SQLiteConnection Connect(string c)
        {
            var conn = new SQLiteConnection(c);
            conn.Open();
            return conn;
        }

        static Database()
        {
            if (!File.Exists("chatagent.db"))
                SQLiteConnection.CreateFile("chatagent.db");
            var c = new SQLiteConnectionStringBuilder
            {
                ForeignKeys = true,
                SyncMode = SynchronizationModes.Normal,
                DataSource = "chatagent.db",
                JournalMode = SQLiteJournalModeEnum.Wal
            }.ConnectionString;

            Agent = new SQLiteAgent(new SQLiteAgentState(Connect, c));
        }

        private static async Task CreateScriptStatesTable()
        {
            const string cmd =
@"CREATE TABLE IF NOT EXISTS ""ScriptStates"" (
    ""Id"" TEXT PRIMARY KEY COLLATE NOCASE NOT NULL DEFAULT '',
    ""LocalId"" TEXT COLLATE NOCASE NOT NULL DEFAULT '',
    ""RemoteId"" TEXT COLLATE NOCASE NOT NULL DEFAULT '',
    ""ScriptChecksum"" TEXT COLLATE NOCASE NOT NULL DEFAULT '',
    ""ScriptIndex"" INTEGER NOT NULL DEFAULT 0,
    ""Pending"" INTEGER NOT NULL DEFAULT 0,
    ""CurrentReply"" TEXT NOT NULL DEFAULT '',
    ""CurrentReplyIsInResponseToKw"" INTEGER NOT NULL DEFAULT 0,
    ""ThreadId"" TEXT NOT NULL DEFAULT '',
    ""KeywordsRespondedTo"" TEXT NOT NULL DEFAULT '',
    FOREIGN KEY (""ScriptChecksum"") REFERENCES ""Scripts"" (""Checksum"")
);";
            _ = await Agent.ExecuteSQL(
                sql => sql.Execute(cmd)
            ).ConfigureAwait(false);

            const string idx1 = @"CREATE INDEX IF NOT EXISTS ""ScriptStatesLocalIdIndex"" ON ""ScriptStates"" (""LocalId"")";
            const string idx2 = @"CREATE INDEX IF NOT EXISTS ""ScriptStatesRemoteIdIndex"" ON ""ScriptStates"" (""RemoteId"")";
            const string idx3 = @"CREATE INDEX IF NOT EXISTS ""ScriptStatesScriptChecksumIndex"" ON ""ScriptStates"" (""ScriptChecksum"")";
            const string idx4 = @"CREATE INDEX IF NOT EXISTS ""ScriptStatesPendingIndex"" ON ""ScriptStates"" (""Pending"")";

            _ = await Agent.ExecuteSQL(idx1).ConfigureAwait(false);
            _ = await Agent.ExecuteSQL(idx2).ConfigureAwait(false);
            _ = await Agent.ExecuteSQL(idx3).ConfigureAwait(false);
            _ = await Agent.ExecuteSQL(idx4).ConfigureAwait(false);
        }

        private static async Task CreateScriptsTable()
        {
            const string cmd =
@"CREATE TABLE IF NOT EXISTS ""Scripts"" (
    ""Index"" INTEGER PRIMARY KEY NOT NULL,
    ""Checksum"" TEXT UNIQUE COLLATE NOCASE NOT NULL DEFAULT '',
    ""Data"" TEXT NOT NULL DEFAULT '' 
);";
            _ = await Agent.ExecuteSQL(cmd);

            const string cmd2 = @"CREATE INDEX IF NOT EXISTS ""ScriptsChecksumIndex"" ON ""Scripts"" (""Checksum"")";
            _ = await Agent.ExecuteSQL(cmd2).ConfigureAwait(false);
        }

        private static async Task CreateSessionsTable()
        {
            const string cmd =
@"CREATE TABLE IF NOT EXISTS ""Sessions"" (
    ""Index"" INTEGER PRIMARY KEY NOT NULL,
    ""Jid"" TEXT UNIQUE NOT NULL COLLATE NOCASE DEFAULT '',
    ""AuthToken"" TEXT NOT NULL DEFAULT '',
    ""ValidAt"" INTEGER NOT NULL DEFAULT 0,
    ""Invalid"" INTEGER NOT NULL DEFAULT 0
);";
            _ = await Agent.ExecuteSQL(sql => sql.Execute(cmd)).ConfigureAwait(false);

            const string idx0 = @"CREATE INDEX IF NOT EXISTS ""SessionsValidAtIndex"" ON ""Sessions"" (""ValidAt"");";
            const string idx1 = @"CREATE INDEX IF NOT EXISTS ""SessionsJidIndex"" ON ""Sessions"" (""Jid"");";
            const string idx2 = @"CREATE INDEX IF NOT EXISTS ""SessionsInvalidIndex"" ON ""Sessions"" (""Invalid"");";

            _ = await Agent.ExecuteSQL(idx0).ConfigureAwait(false);
            _ = await Agent.ExecuteSQL(idx1).ConfigureAwait(false);
            _ = await Agent.ExecuteSQL(idx2).ConfigureAwait(false);
        }

        public static async Task Init()
        {
            await CreateSessionsTable().ConfigureAwait(false);
            await CreateScriptsTable().ConfigureAwait(false);
            await CreateScriptStatesTable().ConfigureAwait(false);
        }

        //        private static void CreateChatBlacklistTable()
        //        {
        //            const string cmd =
        //@"CREATE TABLE IF NOT EXISTS ""ChatBlacklist"" (
        //    ""Index"" INTEGER PRIMARY KEY NOT NULL,
        //    ""Value"" TEXT NOT NULL COLLATE NOCASE
        //);
        //";
        //            _ = await Agent.ExecuteAsync(cmd);

        //            const string cmd2 = @"CREATE INDEX IF NOT EXISTS ""ChatValueIndex"" ON ""ChatBlacklist"" (""Value"");";
        //            _ = await Agent.ExecuteAsync(cmd2);
        //        }

        //        private static void CreateGreetBlacklistTable()
        //        {
        //            const string cmd =
        //@"CREATE TABLE IF NOT EXISTS ""GreetBlacklist"" (
        //""Index"" INTEGER PRIMARY KEY NOT NULL,
        //""Value"" TEXT NOT NULL COLLATE NOCASE
        //);
        //";
        //            _ = await Agent.ExecuteAsync(cmd);

        //            const string cmd2 = @"CREATE INDEX IF NOT EXISTS ""GreetValueIndex"" ON ""GreetBlacklist"" (""Value"")";
        //            _ = await Agent.ExecuteAsync(cmd2);
        //        }

    }

    public static class Sessions
    {
        public static async Task InsertOrReplace(SessionEntity entity, IDbTransaction? transaction = null)
        {
            var idx = entity.Index == null ? "NULL" : entity.Index.Value.ToString();
            var result = await Database.Agent.ExecuteSQL(
                sql => sql.Execute($@"INSERT OR REPLACE INTO ""Sessions"" (""Index"", ""Jid"", ""AuthToken"", ""ValidAt"", ""Invalid"") VALUES ({idx}, '{entity.Jid}', '{entity.AuthToken}', {entity.ValidAt}, {entity.Invalid})")
            ).ConfigureAwait(false);
            if (result != 1)
                throw new InvalidOperationException("SQLite INSERT failed");
        }

        public static async Task<SessionEntity?> TryFind(string jid)
        {
            var result = await Database.Agent.ExecuteSQL<SessionEntity?>(
                sql => sql.QueryFirstOrDefault<SessionEntity?>(@$"SELECT * FROM ""Sessions"" WHERE ""Jid"" = '{jid}' LIMIT 1;")
            ).ConfigureAwait(false);
            return result;
        }
    }

    public static class Scripts
    {
        public static string Checksum(in ReadOnlySpan<byte> data)
        {
            using var meme = MemoryPool<byte>.Shared.Rent(data.Length);
            data.CopyTo(meme.Memory.Span);
            ReadOnlyMemory<byte> memory = meme.Memory.Slice(0, data.Length);
            var arraySeg = memory.AsArraySeg();
            using var sha512 = new SHA512CryptoServiceProvider();
            var result = sha512.ComputeHash(arraySeg.Array, arraySeg.Offset, arraySeg.Count);
            var sb = new StringBuilder(result.Length * 2);
            foreach (var b in result)
                sb.Append(b.ToString("x2"));
            return sb.ToString();
        }

        public static string Checksum(IReadOnlyCollection<string> script)
        {
            Span<byte> data = Encoding.UTF8.GetBytes(string.Join(Environment.NewLine, script));
            return Checksum(data);
        }


        public static async Task<ScriptEntity?> TryFind(string checksum)
        {
            return await Database.Agent.ExecuteSQL(
                sql => sql.QueryFirstOrDefault<ScriptEntity>($@"SELECT * FROM ""Scripts"" WHERE ""Checksum"" = '{checksum}' LIMIT 1;")
            ).ConfigureAwait(false);
        }

        public static async Task<bool> InsertOrIgnore(IReadOnlyCollection<string> script)
        {
            var str = string.Join(Environment.NewLine, script);
            if (str.Contains('\''))
                str = str.Replace('\'', '\"');
            var checksum = Checksum(script);

            var query = $@"INSERT OR IGNORE INTO ""Scripts"" (""Checksum"", ""Data"") VALUES ('{checksum}', '{str}')";
            return await Database.Agent.ExecuteSQL(query).ConfigureAwait(false) == 1;
        }
    }

    public static class ScriptStates
    {
        public static async Task<ScriptStateEntitiy?> TryFind(string id)
        {

            return await Database.Agent.ExecuteSQL(
                sql => sql.QueryFirstOrDefault<ScriptStateEntitiy?>($@"SELECT * FROM ""ScriptStates"" WHERE ""Id"" = '{id}' LIMIT 1;")
            ).ConfigureAwait(false);
        }

        public static async Task<IEnumerable<ScriptStateEntitiy>> TryFindViaLocalId(string localId)
        {
            var r = await Database.Agent.ExecuteSQL(
                sql => sql.Query<ScriptStateEntitiy>($@"SELECT * FROM ""ScriptStates"" WHERE ""LocalId"" = '{localId}';")
            ).ConfigureAwait(false);
            return r;
        }

        public static async Task InsertOrReplace(ScriptStateEntitiy entity)
        {
            var pending = entity.Pending ? 1 : 0;
            var isKwResp = entity.CurrentReplyIsInResponseToKw ? 1 : 0;
            var query =
$@"INSERT OR REPLACE INTO ""ScriptStates"" (
    ""Id"",
    ""LocalId"",
    ""RemoteId"",
    ""ScriptChecksum"", 
    ""ScriptIndex"",
    ""Pending"",
    ""CurrentReply"",
    ""CurrentReplyIsInResponseToKw"",
    ""ThreadId"",
    ""KeywordsRespondedTo""
) VALUES (
    '{entity.Id}',
    '{entity.LocalId}',
    '{entity.RemoteId}', 
    '{entity.ScriptChecksum}', 
    '{entity.ScriptIndex}',
    '{pending}',
    '{entity.CurrentReply}',
    '{isKwResp}',
    '{entity.ThreadId}',
    '{entity.KeywordsRespondedTo}'
);";
            var result = await Database.Agent.ExecuteSQL(query).ConfigureAwait(false);
            if (result != 1)
                throw new InvalidOperationException("SQLite INSERT failed.");
        }
    }
}
