using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using System.Text;

namespace LibTextPlus
{
    public class TextPlusException : InvalidOperationException
    {
        public TextPlusException()
        {
        }

        public TextPlusException(in string message) : base(message)
        {
        }

        public TextPlusException(in string message, in Exception innerException) : base(message, innerException)
        {
        }

        protected TextPlusException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }
    }

#pragma warning disable RCS1194 // Implement exception constructors.
    public class TextPlusApiException : TextPlusException
#pragma warning restore RCS1194 // Implement exception constructors.
    {
        public TextPlusApiHttpCtx HttpCtx { get; }

        public TextPlusApiException(in string message, TextPlusApiHttpCtx httpCtx) : base(message)
        {
            HttpCtx = httpCtx;
        }
    }
}
