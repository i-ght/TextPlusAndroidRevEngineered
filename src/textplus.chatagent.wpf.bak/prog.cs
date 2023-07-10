using Dapper;
using LibCyStd;
using LibTextPlus.ChatAgent;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SQLite;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace TextPlus.ChatAgent.Wpf
{
    internal static class Program
    {
        private static async Task Go()
        {
            var t = new List<dynamic>();
            for (var i = 0; i < 400000; i++)
            {
                var id = Guid.NewGuid().ToString();
                t.Add(new {Id=id});
            }

            var res = await Database.Agent.ExecuteSQL(
                sql => sql.Execute(@$"INSERT INTO ""HELLOTABLE"" (""Index"", ""Id"") VALUES (NULL, @Id)", t)
            ).ConfigureAwait(false);
        }

        [DllImport("kernel32", SetLastError = true)]
        private static extern IntPtr LoadLibraryW([MarshalAs(UnmanagedType.LPWStr)]string lpFileName);

        private static async void MainAsync()
        {
            var st = new Stopwatch();
            st.Start();
            await Database.Agent.ExecuteSQL(
@"CREATE TABLE IF NOT EXISTS ""HELLOTABLE"" (
    ""Index"" INTEGER PRIMARY KEY NOT NULL,
    ""Id"" TEXT COLLATE NOCASE NOT NULL DEFAULT ''
);").ConfigureAwait(false);
            await Database.Agent.ExecuteSQL("BEGIN TRANSACTION;").ConfigureAwait(false);
            var t = new List<Task>();
            t.Add(Go());
            await Task.WhenAll(t).ConfigureAwait(false);
            await Database.Agent.ExecuteSQL("COMMIT;").ConfigureAwait(false);
            st.Stop();
            Console.Out.WriteLine(st.Elapsed);
        }

        [STAThread]
        private static int Main()
        {
            Database.Init().GetAwaiter().GetResult();
            var app = new Application();
            var mWin = new MainWindow();
            return app.Run(mWin);
        }
    }
}
