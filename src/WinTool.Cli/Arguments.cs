using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace WinTool.Cli
{
    public sealed class UsageException : Exception
    {
        public UsageException(string message) : base(message) { }
    }

    /// <summary>
    /// Parsed options of one command. Options are "--name" (a switch) or
    /// "--name value". Unknown options are an error, never ignored, so a typo
    /// cannot silently change what a script does.
    /// </summary>
    public sealed class Arguments
    {
        private readonly HashSet<string> switches = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public List<string> Positional { get; } = new List<string>();

        public static Arguments Parse(IEnumerable<string> tokens, IEnumerable<string> allowedSwitches, IEnumerable<string> allowedValues)
        {
            var switchSet = new HashSet<string>(allowedSwitches ?? new string[0], StringComparer.OrdinalIgnoreCase);
            var valueSet = new HashSet<string>(allowedValues ?? new string[0], StringComparer.OrdinalIgnoreCase);
            var result = new Arguments();
            var list = tokens.ToList();
            for (var i = 0; i < list.Count; i++)
            {
                var token = list[i];
                if (!token.StartsWith("--", StringComparison.Ordinal))
                {
                    result.Positional.Add(token);
                    continue;
                }

                var name = token.Substring(2);
                string inlineValue = null;
                var equals = name.IndexOf('=');
                if (equals > 0)
                {
                    inlineValue = name.Substring(equals + 1);
                    name = name.Substring(0, equals);
                }

                if (switchSet.Contains(name) && inlineValue == null)
                {
                    result.switches.Add(name);
                }
                else if (valueSet.Contains(name))
                {
                    var value = inlineValue ?? (i + 1 < list.Count ? list[++i] : null);
                    if (value == null) throw new UsageException("Option --" + name + " needs a value.");
                    result.values[name] = value;
                }
                else
                {
                    throw new UsageException("Unknown option: " + token);
                }
            }

            return result;
        }

        public bool Has(string name) => switches.Contains(name);

        public string Value(string name)
        {
            string value;
            return values.TryGetValue(name, out value) ? value : null;
        }

        public int? Int(string name)
        {
            var text = Value(name);
            if (text == null) return null;
            int number;
            if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out number))
            {
                throw new UsageException("Option --" + name + " needs a whole number.");
            }

            return number;
        }

        /// <summary>A "--x" / "--no-x" pair. Both together is an error.</summary>
        public bool? Toggle(string name)
        {
            var on = Has(name);
            var off = Has("no-" + name);
            if (on && off) throw new UsageException("Use either --" + name + " or --no-" + name + ", not both.");
            return on ? true : off ? (bool?)false : null;
        }

        public void NoPositional(int allowed = 0)
        {
            if (Positional.Count > allowed) throw new UsageException("Unexpected argument: " + Positional[allowed]);
        }
    }
}
