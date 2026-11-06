using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace WinTool.Upgrade;

internal static class PowerShellDiagnostics
{
    internal static string MountFailureMessage(string stderr)
    {
        const string fallback = "Windows could not prepare the ISO. Use Refresh, or select its mounted drive with Folder... if it is visible in Explorer. See View details/log.";
        var message = stderr.Trim();
        if (message.StartsWith("#< CLIXML", StringComparison.Ordinal) || message.StartsWith("<", StringComparison.Ordinal))
        {
            try
            {
                var xml = message.StartsWith("#< CLIXML", StringComparison.Ordinal) ? message[9..].TrimStart() : message;
                using var reader = XmlReader.Create(new StringReader(xml), new XmlReaderSettings
                {
                    DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 1_000_000
                });
                var document = XDocument.Load(reader);
                message = string.Join("\n", document.Descendants()
                    .Where(element => element.Name.LocalName == "S" && (string?)element.Attribute("S") == "Error")
                    .Select(element => element.Value));
                // Decode once: escaped literal escape sequences must not be decoded twice.
                message = Regex.Replace(message, "_x([0-9a-fA-F]{4})_", match =>
                    ((char)Convert.ToUInt16(match.Groups[1].Value, 16)).ToString());
            }
            catch (XmlException) { return fallback; }
        }
        if (message.Contains("PCNINJA_ISO_VOLUME_TIMEOUT", StringComparison.Ordinal) ||
            message.Contains("Windows did not assign a drive letter to the ISO.", StringComparison.Ordinal))
            return "Could not detect the ISO drive within 30 seconds. Use Refresh, or select the mounted drive with Folder... if it is visible in Explorer. Otherwise remount the ISO manually. No upgrade was started.";
        var firstLine = message.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault(line => !string.IsNullOrWhiteSpace(line))?.Trim();
        if (firstLine is null) return fallback;
        return "Windows could not prepare the ISO: " + firstLine[..Math.Min(firstLine.Length, 240)] + " See View details/log.";
    }
}
