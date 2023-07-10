using LibCyStd;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace LibTextPlus.Creator
{
    internal static class Cons
    {
        public static SemaphoreSlim Lock { get; }

        static Cons()
        {
            Lock = new SemaphoreSlim(1, 1);
        }
    }

    public enum VerifyMethod
    {
        Any,
        Email,
        Sms,
    }

    public enum VerifyProviderKind
    {
        Console
    }

    public abstract class VerifyClient
    {
        public VerificationType VerifyMethod { get; }
        public string Id { get; }
        public abstract Task<Option<string>> TryGetCode();

        protected VerifyClient(
            VerificationType verifyMethod,
            string number)
        {
            VerifyMethod = verifyMethod;
            Id = number;
        }
    }

    public class ConsoleVerifyClient : VerifyClient
    {
        public ConsoleVerifyClient(VerificationType method, string id) : base(method, id)
        {
        }

        public override async Task<Option<string>> TryGetCode()
        {
            await Cons.Lock.WaitAsync().ConfigureAwait(false);
            try
            {
                Console.Out.WriteLine($"Enter code for id {Id}:");
                var line = await Console.In.ReadLineAsync().ConfigureAwait(false);
                return string.IsNullOrWhiteSpace(line) ? Option.None : Option.Some(line);
            }
            finally
            {
                Cons.Lock.Release();
            }
        }
    }

    public abstract class VerifyClientProvider
    {
        public VerifyMethod VerifyMethod { get; }
        public bool IsInfinite { get; }
        public bool IsAsync { get; }

        protected VerifyClientProvider(
            VerifyMethod verifyMethod,
            bool isInfinite,
            bool isAsync)
        {
            VerifyMethod = verifyMethod;
            IsInfinite = isInfinite;
            IsAsync = isAsync;
        }

        public abstract Task<Option<VerifyClient>> TryGetVerifyClientAsync();
        public abstract Option<VerifyClient> TryGetVerifyClient();
    }

    public class AlwaysValidVerifyClient : VerifyClient
    {
        public AlwaysValidVerifyClient() : base(VerificationType.PHONE, "")
        {
        }

        public override Task<Option<string>> TryGetCode()
        {
            return Task.FromResult(Option.Some(""));
        }
    }

    public class AlwaysValidVerifierClientProvider : VerifyClientProvider
    {
        public AlwaysValidVerifierClientProvider() : base(VerifyMethod.Any, isInfinite: true, isAsync: false)
        {
        }

        public override Option<VerifyClient> TryGetVerifyClient()
        {
            return new AlwaysValidVerifyClient();
        }

        public override Task<Option<VerifyClient>> TryGetVerifyClientAsync()
        {
            throw new NotImplementedException();
        }
    }

    public class ConsoleVerifyClientProvider : VerifyClientProvider
    {
        public int MobileCc { get; set; }

        public ConsoleVerifyClientProvider(int mobileCc)
            : base(VerifyMethod.Any, isInfinite: true, isAsync: true)
        {
            MobileCc = mobileCc;
        }

        private static string Parse(string input, int mobileCc)
        {
            if (input.Contains("@"))
                return input;

            if (input.Contains("-"))
                input = input.Replace("-", string.Empty);

            if (input.StartsWith("+"))
                return input;

            return $"+{mobileCc}{input}";
        }

        public override async Task<Option<VerifyClient>> TryGetVerifyClientAsync()
        {
            await Cons.Lock.WaitAsync().ConfigureAwait(false);
            try
            {
                Console.Out.WriteLine("Enter phone number or email address:");
                var line = await Console.In.ReadLineAsync().ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(line))
                    return Option.None;
                var parsed = Parse(line, MobileCc);
                var kind = parsed.Contains("@") ? VerificationType.EMAIL : VerificationType.PHONE;
                return new ConsoleVerifyClient(kind, parsed);
            }
            finally
            {
                Cons.Lock.Release();
            }
        }

        public override Option<VerifyClient> TryGetVerifyClient() => throw new NotImplementedException();
    }

    public class PvaException : InvalidOperationException
    {
        public PvaException(string msg) : base(msg)
        {
        }

        public PvaException(string msg, Exception inner) : base(msg, inner)
        {
        }

        public PvaException() : base()
        {
        }
    }

    public class WaitForCodeTimeOutException : TimeoutException
    {
        public WaitForCodeTimeOutException(string msg) : base(msg)
        {
        }

        public WaitForCodeTimeOutException(string msg, Exception inner) : base(msg, inner)
        {
        }

        public WaitForCodeTimeOutException() : base()
        {
        }
    }

    public class InvalidCodeException : InvalidOperationException
    {
        public InvalidCodeException(string msg) : base(msg)
        {
        }

        public InvalidCodeException(string msg, Exception inner) : base(msg, inner)
        {
        }

        public InvalidCodeException() : base()
        {
        }
    }

    public class InvalidNumberException : InvalidOperationException
    {
        public InvalidNumberException(string msg) : base(msg)
        {
        }

        public InvalidNumberException(string msg, Exception inner) : base(msg, inner)
        {
        }

        public InvalidNumberException() : base()
        {
        }
    }
}
