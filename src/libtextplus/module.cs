using LibCyStd;

namespace LibTextPlus
{
    public static class TextPlusModule
    {
        public static Unit TextPlusApiEx(in string msg, in TextPlusApiHttpCtx result) => throw new TextPlusApiException(msg, result);

        public static string GenAndroidId()
        {
            var bytes = RandomModule.NextBytes(8);
            return bytes.ToHex();
        }
    }
}
