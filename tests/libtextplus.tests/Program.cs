using LibCyStd;
using LibCyStd.Net;
using LibCyStd.Seq;
using LibTextPlus;
using LibTextPlus.ChatAgent;
//using LibTextPlus.Creator;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.WebSockets.Managed;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace libtextplus.tests
{
    //internal class ConsoleStatsUIUpdater : StatsUIUpdater
    //{
    //    private int _attemtpts;
    //    private int _created;
    //    public override void IncrementAttempts()
    //    {
    //        Console.Out.WriteLine($"Attempts = {++_attemtpts}");
    //    }

    //    public override void IncrementCreated()
    //    {
    //        Console.Out.WriteLine($"Created = {++_created}");
    //    }
    //}

    //internal class ConsoleAgentUIUpdater : AgentUIUpdater
    //{
    //    private readonly int _index;

    //    public ConsoleAgentUIUpdater(int index) => _index = index;

    //    public override void UpdateId(string value)
    //    {
    //        Console.Out.WriteLine($"[*{_index}].Id = {value}");
    //    }

    //    public override void UpdateStatus(string value)
    //    {
    //        Console.Out.WriteLine($"[*{_index}].Status = {value}");
    //    }
    //}

    internal static class Program
    {
        //private static ReadOnlyDictionary<int, AgentUIUpdater> CreateAgentUIs(int cnt)
        //{
        //    var d = new Dictionary<int, AgentUIUpdater>(cnt);
        //    for (var i = 0; i < cnt; i++)
        //    {
        //        d.Add(i, new ConsoleAgentUIUpdater(i));
        //    }

        //    return ReadOnlyDictModule.OfDict(d);
        //}

        //private static async Task Create()
        //{
        //    var cfg = new Cfg(maxWorkers: 3, maxCreates: 1, mobileCountryCode: 1, getTextPlusNumber: false, GcmProviderMethod.File, Option.None);
        //    var sts = new ConsoleStatsUIUpdater();
        //    var agentUiUpdaters = CreateAgentUIs(cfg.MaxWorkers);
        //    using var cts = new CancellationTokenSource();

        //    var dev = new Queue<TextPlusAndroidDevice>(new[]
        //    {
        //        new TextPlusAndroidDevice("samsung", "galaxy", "galaxy", "8.1.0")
        //    });
        //    var proxies = new Queue<Proxy>(new[]
        //    {
        //        Proxy.TryParseOpt("socks5://192.168.2.112:8889").Value
        //    });
        //    var words1 = new Queue<string>(new[] { "hdhew26" });
        //    var words2 = new Queue<string>(new[] { "asdfsfad2" });
        //    var gcmTokens = new Queue<string>(new[] { "" });
        //    var seqs = new Seqs(dev, proxies, words1, words2, gcmTokens);
        //    var consoleProvider = new AlwaysValidVerifierClientProvider();

        //    var dCfg = new DirectorCfg(
        //        sts,
        //        agentUiUpdaters,
        //        cts.Token,
        //        cfg,
        //        seqs,
        //        consoleProvider
        //    );
        //    var director = new Director(dCfg);
        //    director.ActivateAgents();
        //    await director.WhenWorkComplete.FirstAsync();
        //}

        //private static string DecodeUrlSafeBase64(string input)
        //{
        //    string incoming = input
        //        .Replace('_', '/').Replace('-', '+');
        //    switch (input.Length % 4)
        //    {
        //        case 2: incoming += "=="; break;
        //        case 3: incoming += "="; break;
        //    }
        //    byte[] bytes = Convert.FromBase64String(incoming);
        //    string originalText = Encoding.ASCII.GetString(bytes);
        //    return originalText;
        //}

        //private static async Task MAinAsync()
        //{
        //    using var client = new ClientWebSocket();
        //    using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(10));
        //    await client.ConnectAsync(new Uri("wss://echo.websocket.org"), CancellationToken.None).ConfigureAwait(false);

        //    var data = new ArraySegment<byte>(Encoding.UTF8.GetBytes("hello_world"));
        //    await client.SendAsync(data, System.Net.WebSockets.WebSocketMessageType.Text, true, cts.Token).ConfigureAwait(false);
        //}

        //private static XElement? TryFindElem(this XElement elem, Func<XElement, bool> predicate)
        //{
        //    return elem.Elements().FirstOrDefault(predicate);
        //}

        //public static void RaiseSetEx<T>(this TaskCompletionSource<T> tcs, Exception e)
        //{
        //    try { throw e; }
        //    catch (Exception e2) { tcs.SetException(e2); }
        //}

        //private static async Task Test()
        //{
        //    var tcs = new TaskCompletionSource<bool>();
        //    tcs.SetException(new InvalidOperationException("fuck"));
        //    await tcs.Task.ConfigureAwait(false);
        //}

        //private static async Task Test2()
        //{
        //    await Test().ConfigureAwait(false);
        //    Console.WriteLine();
        //}

        private static async Task MainAsync()
        {
            var s0 = new System.Net.WebSockets.Managed.ClientWebSocket();
            var s1 = new System.Net.WebSockets.Managed.ClientWebSocket();
            await s0.ConnectAsync(new Uri("wss://echo.websocket.org/"), CancellationToken.None).ConfigureAwait(false);
            await s0.ConnectAsync(new Uri("wss://echo.websocket.org/"), CancellationToken.None).ConfigureAwait(false);
            await s0.ConnectAsync(new Uri("wss://echo.websocket.org/"), CancellationToken.None).ConfigureAwait(false);
            await s0.ConnectAsync(new Uri("wss://echo.websocket.org/"), CancellationToken.None).ConfigureAwait(false);
        }

        private static void Main(string[] args)
        {
            var cts = new CancellationTokenSource();
            cts.Dispose();
            cts.Cancel();
            MainAsync().GetAwaiter().GetResult();
        }
    }
}
