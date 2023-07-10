using LibCyStd;
using LibCyStd.Seq;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Web;

namespace libtextplus.tests
{
    using StrKvp = ValueTuple<string, string>;

    internal static class ParseGcmReq
    {
        private static ReadOnlyCollection<StrKvp> TryParseHeaders(string input)
        {
            var headerItems = input.Split("\r\n".ToCharArray(), StringSplitOptions.RemoveEmptyEntries);
            if (headerItems.Length <= 1)
                return ReadOnlyCollectionModule.OfSeq(SeqModule.Empty<(string k, string v)>());

            var lst = new List<StrKvp>();
            for (var i = 1; i < headerItems.Length; i++)
            {
                var item = headerItems[i];
                if (!item.Contains(':'))
                    continue;
                var sp = item.Split(':', StringSplitOptions.RemoveEmptyEntries);
                if (sp.Length <= 1)
                    continue;

                var pair = (sp[0], string.Join("", sp.Skip(1)));
                lst.Add(pair);
            }

            return ReadOnlyCollectionModule.OfSeq(lst);
        }

        private static string GenerateAppId() => StringModule.Rando(Chars.DigitsAndLetters, 11);

        private static Option<EncodedFormValuesHttpContent> TryParseContent(string input)
        {
            var sp = input.Split('&', StringSplitOptions.RemoveEmptyEntries);
            if (sp.Length <= 1)
                return Option.None;

            var lst = new List<StrKvp>(sp.Length);
            foreach (var item in sp)
            {
                var s = item.Split('=');
                if (s.Length <= 1)
                    continue;
                var val = s[0].InvariantEquals("X-appid") ? GenerateAppId() : string.Concat(s.Skip(1));
                var pair = (s[0], HttpUtility.UrlDecode(val));
                lst.Add(pair);
            }

            if (lst.Count == 0)
                return Option.None;

            return new EncodedFormValuesHttpContent(lst);
        }

        internal static Option<HttpReq> TryParseGcmRegisterReq(string input)
        {
            if (string.IsNullOrWhiteSpace(input) || !input.Contains("/c2dm/register3"))
                return Option.None;

            var span = input.AsSpan();
            var indexOfDelim = span.IndexOf("\r\n\r\n".ToCharArray());
            if (indexOfDelim <= -1)
                return Option.None;

            var headersData = span.Slice(0, indexOfDelim);
            var headersStr = headersData.ToString();

            var headers = TryParseHeaders(input);
            if (headers.Count == 0)
                return Option.None;

            var contentData = span.Slice(indexOfDelim + 4);
            var contentStr = contentData.ToString();

            var contentOpt = TryParseContent(contentStr);
            if (contentOpt.IsNone)
                return Option.None;

            var filteredHeaders = headers.Choose(pair =>
            {
                if (pair.Item1.InvariantEquals("host") || pair.Item1.InvariantEquals("content-length"))
                    return Option.None;
                return Option.Some(pair);
            });
            if (!filteredHeaders.Any())
                return Option.None;

            var req = new HttpReq("POST", "https://android.clients.google.com/c2dm/register3")
            {
                Headers = headers,
                ContentBody = contentOpt.Value,
            };

            return req;
        }
    }
}
