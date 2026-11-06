using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace WinTool.Upgrade;

public sealed record SetupFailureEvidence(string ResultCode, string ExtendedCode, string Phase,
    string Summary, string LogPath);

// Read-only, bounded evidence. Missing/unrelated/incomplete logs must stay unknown.
internal static class SetupOutcomeDiagnostics
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(250);
    private static Match Match(string text, string pattern) =>
        Regex.Match(text, pattern, RegexOptions.IgnoreCase | RegexOptions.Multiline, RegexTimeout);

    internal static SetupFailureEvidence? Read(UpgradeAttempt attempt, string windowsVolume)
    {
        try
        {
            var sources = Path.Combine(windowsVolume, "$WINDOWS.~BT", "Sources");
            var panther = Path.Combine(sources, "Panther", "setupact.log");
            var rollback = Path.Combine(sources, "Rollback", "setupact.log");
            return Parse(attempt, ReadWindow(panther, false), ReadWindow(panther, true),
                ReadWindow(rollback, true), rollback);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
            System.Security.SecurityException or RegexMatchTimeoutException or ArgumentException)
        {
            AppLog.Info($"Setup outcome evidence unavailable: {ex.GetType().Name}.");
            return null;
        }
    }

    internal static SetupFailureEvidence? Parse(UpgradeAttempt attempt, string header, string panther,
        string rollback, string logPath)
    {
        var begin = Match(header, @"^(?<time>\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}),[^\r\n]*SetupHost Logging Begin");
        if (!TryTime(begin, out var started) || started < attempt.StartedUtc.LocalDateTime.AddSeconds(-5) ||
            started > attempt.StartedUtc.LocalDateTime.AddMinutes(2)) return null;

        var media = Match(header, @"SetupHost::Initialize: MediaPath\s*=\s*\[(?<root>[^\]\r\n]+)\]");
        if (!string.Equals(media.Groups["root"].Value.TrimEnd('\\', '/'),
                attempt.SourceRoot.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase)) return null;
        var command = Match(header, @"SetupHost::Initialize: CmdLine\s*=\s*\[(?<args>[^\r\n]+)\]");
        if (!command.Success || !command.Groups["args"].Value.Contains(
                "\"" + attempt.ImagePath + "\"", StringComparison.OrdinalIgnoreCase) ||
            !Match(command.Groups["args"].Value, @"/imageindex""?\s+""?" + attempt.ImageIndex + @"(?=[""\s]|$)").Success)
            return null;

        // A later visible session means this is not the recorded attempt.
        var later = Regex.Matches(panther,
            @"^(?<time>\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}),[^\r\n]*SetupHost Logging Begin",
            RegexOptions.Multiline, RegexTimeout);
        if (later.Cast<Match>().Any(match => TryTime(match, out var time) && time > started)) return null;

        var failure = Match(panther,
            @"^(?<time>\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}),[^\r\n]*Execution phase \[(?<phase>[^\]\r\n]{1,60})\] exiting with HRESULT \[(?<code>0x[89ACDEF][0-9A-F]{7})\]");
        if (!TryTime(failure, out var failed) || failed < started || failed > started.AddDays(1)) return null;
        var code = failure.Groups["code"].Value.ToUpperInvariant().Replace("0X", "0x");
        var report = Match(rollback,
            @"^(?<time>\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}),[^\r\n]*Reporting post reboot event -> \[" +
            Regex.Escape(code) + @", (?<extended>0x[0-9A-F]{5,8})\]");
        var complete = Match(rollback,
            @"^(?<time>\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}),[^\r\n]*Rollback completed successfully\.");
        if (!TryTime(report, out var reported) || !TryTime(complete, out var completed) ||
            completed < failed || reported < failed || completed > failed.AddHours(2) ||
            reported > failed.AddHours(2)) return null;

        var summary = "Windows Setup rolled back. The previous Windows installation was restored.";
        if (code == "0x800700B7" && Match(panther,
                @"SPMoveFileWithShortName: Failed to move [^\r\n]+\\Recovery\\ReAgentOld\.xml to [^\r\n]+\\Recovery\\ReAgentOld\.xml, error: 0x000000B7").Success)
            summary = "Windows Setup rolled back after a Recovery file collision (ReAgentOld.xml). " +
                "No automatic cleanup was performed.";
        return new SetupFailureEvidence(code, report.Groups["extended"].Value,
            failure.Groups["phase"].Value, summary, logPath);
    }

    private static bool TryTime(Match match, out DateTime value) => DateTime.TryParseExact(
        match.Groups["time"].Value, "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture,
        DateTimeStyles.None, out value);

    private static string ReadWindow(string path, bool tail)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        var first = stream.ReadByte();
        var second = stream.ReadByte();
        Encoding encoding = (first, second) switch
        {
            (0xff, 0xfe) => Encoding.Unicode,
            (0xfe, 0xff) => Encoding.BigEndianUnicode,
            _ => Encoding.UTF8
        };
        var limit = tail ? 512 * 1024 : 16 * 1024;
        var offset = tail ? Math.Max(0, stream.Length - limit) : 0;
        if (encoding != Encoding.UTF8) offset -= offset % 2;
        stream.Position = offset;
        var buffer = new byte[(int)Math.Min(limit, stream.Length - offset)];
        var count = Fx.ReadAtLeast(stream, buffer, buffer.Length);
        var text = encoding.GetString(buffer, 0, count).TrimStart('\uFEFF');
        // Never parse a line cut by the bounded tail window.
        if (offset == 0) return text;
        var newline = text.IndexOf('\n');
        return newline >= 0 ? text[(newline + 1)..] : "";
    }
}
