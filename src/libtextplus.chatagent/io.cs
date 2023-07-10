using LibCyStd;
using LibCyStd.IO;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Text;

namespace LibTextPlus.ChatAgent
{
    public class ContactsStreamReader : IDisposable
    {
        private readonly Queue<TextPlusMatchableSearchResponse> _cache;
        private readonly StreamReader _streamReader;
        private readonly Blacklist _chatBlacklist;
        private readonly Blacklist _greetBlacklist;

        private static Option<TextPlusMatchableSearchResponse> TryDeserializeContact(string input)
        {
            try
            {
                //if (input.StartsWith("+")) {

                //    var c = new TextPlusMatchableSearchResponse("", "", "", new TextPlusMatchablePersona("", $"{input}@sms.nextplus.com", "", "", "", "", -1), false, "");
                //    return Option.Some(c);
                //}

                var result = JsonConvert.DeserializeObject<TextPlusMatchableSearchResponse>(input);
                if (result == null)
                    return Option.None;
                if (result.Persona == null || string.IsNullOrWhiteSpace(result.Persona.Jid))
                    return Option.None;
                return Option.Some(result);
            }
            catch (JsonException)
            {
                return Option.None;
            }
        }

        public TextPlusMatchableSearchResponse? DequeueLine()
        {
            if (_cache.Count > 0)
                return _cache.Dequeue();

            if (_streamReader.EndOfStream)
                return default;

            while (!_streamReader.EndOfStream && _cache.Count < 10000)
            {
                var line = _streamReader.ReadLine();
                var parseResult = TryDeserializeContact(line);
                if (parseResult.IsSome
                    && !_greetBlacklist.ThreadSafeContains(parseResult.Value.Persona.Jid)
                    && !_chatBlacklist.ThreadSafeContains(parseResult.Value.Persona.Jid))
                {
                    _cache.Enqueue(parseResult.Value);
                }
            }

            return _cache.Count > 0 ? _cache.Dequeue() : default!;
        }

        public List<TextPlusMatchableSearchResponse> DequeueLines(int cnt)
        {
            var lst = new List<TextPlusMatchableSearchResponse>();
            for (var i = 0; i < cnt; i++)
            {
                var contact = DequeueLine();
                if (contact == null)
                    return lst;
                lst.Add(contact);
            }
            return lst;
        }

        public void Enqueue(TextPlusMatchableSearchResponse contact) => _cache.Enqueue(contact);

        public void Enqueue(IEnumerable<TextPlusMatchableSearchResponse> contacts)
        {
            foreach (var contact in contacts)
                _cache.Enqueue(contact);
        }


        public void Dispose()
        {
            _streamReader.Dispose();
        }

        public ContactsStreamReader(Stream sr, Blacklist chatBlacklist, Blacklist greetBlacklist)
        {
            _streamReader = new StreamReader(sr);
            _cache = new Queue<TextPlusMatchableSearchResponse>(10000);
            _chatBlacklist = chatBlacklist;
            _greetBlacklist = greetBlacklist;
        }
    }
}
