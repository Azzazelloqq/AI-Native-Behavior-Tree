using System.Text;

namespace AIBT
{
    /// <summary>
    /// P11-016 item 4: a JSON string literal, quoted and escaped exactly as Newtonsoft.Json's
    /// <c>JsonConvert.ToString(string)</c> writes it, so <c>AIBT.Runtime</c> does not need Newtonsoft for the
    /// verifier's canonical default text. Escaped: <c>"</c> and <c>\</c>; <c>\b \f \n \r \t</c> by their short
    /// form; every other code unit below U+0020 and U+0085, U+2028, U+2029 as <c>\u</c> with four lowercase hex
    /// digits. Everything else, U+007F and lone surrogates included, is written as it is. Null is written
    /// as <c>""</c>. Parity with Newtonsoft over all 65,536 code units is pinned in the Editor tests.
    /// </summary>
    internal static class JsonStringLiteralV1
    {
        private const string Hex = "0123456789abcdef";

        internal static string Quote(string value)
        {
            if (string.IsNullOrEmpty(value)) return "\"\"";
            var first = FirstToEscape(value);
            if (first < 0) return "\"" + value + "\"";

            var text = new StringBuilder(value.Length + 16);
            text.Append('"').Append(value, 0, first);
            for (var index = first; index < value.Length; index++)
            {
                var character = value[index];
                switch (character)
                {
                    case '"': text.Append("\\\""); break;
                    case '\\': text.Append("\\\\"); break;
                    case '\b': text.Append("\\b"); break;
                    case '\f': text.Append("\\f"); break;
                    case '\n': text.Append("\\n"); break;
                    case '\r': text.Append("\\r"); break;
                    case '\t': text.Append("\\t"); break;
                    default:
                        if (NeedsEscape(character)) AppendUnicode(text, character);
                        else text.Append(character);
                        break;
                }
            }
            return text.Append('"').ToString();
        }

        private static int FirstToEscape(string value)
        {
            for (var index = 0; index < value.Length; index++)
                if (NeedsEscape(value[index])) return index;
            return -1;
        }

        private static bool NeedsEscape(char character)
            => character < ' ' || character == '"' || character == '\\'
               || character == '\u0085' || character == '\x2028' || character == '\x2029';

        private static void AppendUnicode(StringBuilder text, char character)
            => text.Append('\\').Append('u')
                .Append(Hex[character >> 12 & 0xF]).Append(Hex[character >> 8 & 0xF])
                .Append(Hex[character >> 4 & 0xF]).Append(Hex[character & 0xF]);
    }
}
