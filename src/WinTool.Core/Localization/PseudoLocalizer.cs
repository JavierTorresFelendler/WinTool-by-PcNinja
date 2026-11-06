using System.Text;

namespace WinTool.Core.Localization
{
    /// <summary>
    /// Builds the pseudo-language: accented letters, about 40% longer, in
    /// brackets. Cut-off, overlapping or bracket-less (hard-coded) text shows
    /// up on screen long before a real translation exists.
    /// </summary>
    public static class PseudoLocalizer
    {
        private const string Plain = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ";
        private const string Accented = "åƀçðéƒĝĥîĵķļɱñöþǫŕšţûṽŵẋýžÅƁÇÐÉƑĜĤÎĴĶĻṀÑÖÞǪŔŠŢÛṼŴẊÝŽ";

        public static string Transform(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;

            var builder = new StringBuilder(text.Length * 2);
            builder.Append('[');
            var insidePlaceholder = false;
            foreach (var ch in text)
            {
                if (ch == '{') insidePlaceholder = true;
                if (!insidePlaceholder)
                {
                    var index = Plain.IndexOf(ch);
                    builder.Append(index >= 0 ? Accented[index] : ch);
                }
                else
                {
                    builder.Append(ch);
                }

                if (ch == '}') insidePlaceholder = false;
            }

            var padding = (int)System.Math.Ceiling(text.Length * 0.4);
            builder.Append(' ');
            builder.Append('·', padding);
            builder.Append(']');
            return builder.ToString();
        }
    }
}
