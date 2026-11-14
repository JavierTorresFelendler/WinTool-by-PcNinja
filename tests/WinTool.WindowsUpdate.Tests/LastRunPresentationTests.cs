using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Xml.Linq;
using WinTool.App.Presentation;
using WinTool.WindowsUpdate.Engine;
using WinTool.WindowsUpdate.Storage;
using Xunit;

namespace WinTool.WindowsUpdate.Tests
{
    public class LastRunPresentationTests
    {
        private static WindowsUpdateState Saved() => new WindowsUpdateState { LastRunStarted = "2026-11-11T08:12:00", LastRunFinished = "2026-11-11T08:13:45", LastRunType = "Scheduled", LastResult = "Succeeded", LastMessage = "do not display raw state messages" };
        private static Dictionary<string, string> Text(string language) => XDocument.Load(Path.Combine(AppContext.BaseDirectory, "LastRunResources", language == "en" ? "Strings.resx" : "Strings." + language + ".resx"))
            .Root.Elements("data").ToDictionary(e => e.Attribute("name").Value, e => e.Element("value").Value);

        [Theory]
        [InlineData("en")]
        [InlineData("he")]
        [InlineData("es")]
        public void Existing_run_shows_saved_date_type_and_localized_result(string language)
        {
            var calls = 0; var state = Saved(); var view = LastRunPresentation.Read(() => { calls++; return state; });
            var text = Text(language); var culture = new CultureInfo(language);
            var formatted = view.Format(k => text[k], culture);
            Assert.Equal(1, calls); Assert.Equal(new DateTime(2026, 11, 11, 8, 13, 45), view.Stamp);
            Assert.Contains(view.Stamp.Value.ToString("g", culture), formatted);
            Assert.Contains(text["History_Type_Scheduled"], formatted); Assert.Contains(text["History_Result_Success"], formatted);
            Assert.DoesNotContain(state.LastMessage, formatted);
        }
        [Theory]
        [InlineData("en")]
        [InlineData("he")]
        [InlineData("es")]
        public void Absent_empty_corrupt_or_unreadable_state_uses_empty_line(string language)
        {
            var text = Text(language);
            var readers = new Func<WindowsUpdateState>[] { () => null, () => new WindowsUpdateState(), () => throw new IOException("unreadable"), () => throw new UnauthorizedAccessException(), () => throw new SerializationException("corrupt") };
            foreach (var read in readers) Assert.Equal(text["History_Empty"], LastRunPresentation.Read(read).Format(k => text[k], new CultureInfo(language)));
        }
        [Theory]
        [InlineData("bad", "Scheduled", "Succeeded")]
        [InlineData("2026-11-11T08:13:45", "injected", "Succeeded")]
        [InlineData("2026-11-11T08:13:45", "0", "Succeeded")]
        [InlineData("2026-11-11T08:13:45", "Manual", "0")]
        [InlineData("2026-11-11T08:13:45", "Manual", "injected")]
        public void Invalid_fields_are_not_displayed_as_history(string stamp, string type, string outcome)
        {
            var state = Saved(); state.LastRunFinished = stamp; state.LastRunType = type; state.LastResult = outcome;
            Assert.Null(LastRunPresentation.Read(() => state).Stamp);
        }
        [Fact]
        public void In_progress_record_uses_start_not_previous_finish_and_marks_completion_unknown()
        {
            var state = Saved(); state.LastRunStarted = "2026-11-12T10:00:00"; state.LastResult = "Running";
            var result = LastRunPresentation.Read(() => state);
            Assert.Equal(new DateTime(2026, 11, 12, 10, 0, 0), result.Stamp);
            Assert.Equal("History_Result_Incomplete", result.OutcomeKey);
        }
        [Fact]
        public void Contradictory_completed_timestamps_show_empty_state()
        {
            var state = Saved(); state.LastRunStarted = "2026-11-12T10:00:00";
            Assert.Null(LastRunPresentation.Read(() => state).Stamp);
        }
        [Theory]
        [InlineData("en")]
        [InlineData("he")]
        [InlineData("es")]
        public void All_known_run_types_and_outcomes_have_localized_presentation(string language)
        {
            var text = Text(language);
            foreach (RunType type in Enum.GetValues(typeof(RunType)))
            foreach (RunResult outcome in Enum.GetValues(typeof(RunResult)))
            {
                var state = Saved(); state.LastRunType = type.ToString(); state.LastResult = outcome.ToString();
                var view = LastRunPresentation.Read(() => state);
                Assert.NotNull(view.Stamp); Assert.True(text.ContainsKey(view.TypeKey)); Assert.True(text.ContainsKey(view.OutcomeKey));
                Assert.False(string.IsNullOrWhiteSpace(view.Format(k => text[k], new CultureInfo(language))));
            }
            Assert.True(text.ContainsKey("History_Result_Incomplete"));
        }
    }
}
