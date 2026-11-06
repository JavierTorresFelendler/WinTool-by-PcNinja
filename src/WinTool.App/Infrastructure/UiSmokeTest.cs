using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Threading;
using WinTool.App.Localization;
using WinTool.App.Theming;
using WinTool.App.ViewModels;
using WinTool.App.Views;
using WinTool.Core.Diagnostics;
using WinTool.Core.Settings;

namespace WinTool.App.Infrastructure
{
    /// <summary>
    /// "--ui-smoke": opens the real window off-screen and shows every page in
    /// every theme and both languages. Fails on any exception or data-binding
    /// error, which compiling alone does not catch. CI runs it.
    /// </summary>
    internal static class UiSmokeTest
    {
        public static int Run(TextWriter output)
        {
            var bindingErrors = new BindingErrorListener();
            PresentationTraceSources.Refresh();
            PresentationTraceSources.DataBindingSource.Listeners.Add(bindingErrors);
            PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Warning;

            var failures = new List<string>();
            // The real app gets this from Application.Run; without it, "await" resumes on a thread-pool
            // thread and pages change their lists off the UI thread (found by the Logs capture checks).
            System.Threading.SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
            if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WINTOOL_SMOKE_SCREENSHOTS"))) UseSampleLog();
            var app = new App();
            app.InitializeComponent();
            app.DispatcherUnhandledException += (sender, e) =>
            {
                failures.Add("Unhandled: " + e.Exception);
                e.Handled = true;
            };

            var exePath = Process.GetCurrentProcess().MainModule.FileName;
            var settings = new AppSettings();
            var updates = new AppUpdateViewModel(settings, exePath);
            var settingsPage = new SettingsPageViewModel(settings, () => { }, () => { }, updates);
            var model = new MainViewModel(settingsPage, new WinTool.WindowsUpdate.Platform.WindowsUpdateEnvironment(exePath, useEventLog: false), updates);
            var window = new MainWindow
            {
                DataContext = model,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = -20000,
                Top = -20000,
                ShowActivated = false,
                ShowInTaskbar = false
            };

            // The opening-size rule maximizes on small screens (D-019); an off-screen test window must be normal.
            window.WindowState = WindowState.Normal;

            try
            {
                ThemeManager.Apply(VisualTheme.Dark);
                window.Show();
                foreach (var theme in new[] { VisualTheme.Dark, VisualTheme.Light, VisualTheme.HighContrast })
                {
                    ThemeManager.Apply(theme);
                    foreach (var language in new[] { "en", "he", "es" })
                    {
                        Loc.Instance.SetLanguage(language);
                        foreach (var width in new[] { 1280.0, 1024.0 })
                        {
                            window.Width = width;
                            window.Height = 728;
                            foreach (var item in model.NavItems)
                            {
                                item.IsSelected = true;
                                Pump();
                                window.UpdateLayout();
                                output.WriteLine("  OK    " + theme + " / " + language + " / " + width + " / " + item.Key);
                            }
                        }
                    }
                }

                // Let the pages' background loads finish and report their binding errors too.
                var until = DateTime.UtcNow.AddSeconds(15);
                while (DateTime.UtcNow < until) Pump();

                // Screenshots for review (CI uploads them). Codex review of 06e0fa0: render at the exact
                // requested size (the runner's screen clamps the window), keep the window background and
                // right-to-left direction, and wait until each page has its data (Logs: sample entries).
                var shots = Environment.GetEnvironmentVariable("WINTOOL_SMOKE_SCREENSHOTS");
                if (!string.IsNullOrEmpty(shots))
                {
                    System.IO.Directory.CreateDirectory(shots);
                    // Render outside the window: the runner's small screen clamps the window, which clipped
                    // larger captures, and a right-to-left root renders mirrored unless a left-to-right parent
                    // holds it, as the window does (Codex T-002). The window's resources (page templates) move along.
                    var root = (FrameworkElement)window.Content;
                    var windowResources = window.Resources;
                    window.Content = null;
                    window.Resources = new ResourceDictionary();
                    root.Resources.MergedDictionaries.Add(windowResources);
                    root.DataContext = model;
                    root.SetBinding(FrameworkElement.FlowDirectionProperty, new System.Windows.Data.Binding("FlowDirection") { Source = Loc.Instance });
                    var host = new System.Windows.Controls.Border { Child = root, FlowDirection = FlowDirection.LeftToRight };
                    host.SetResourceReference(System.Windows.Controls.Border.BackgroundProperty, "Brush.Bg");
                    // What the window gives its content (Codex T-003: dark captures lost text colour and font).
                    host.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, "Brush.Text");
                    System.Windows.Documents.TextElement.SetFontFamily(host, window.FontFamily);
                    System.Windows.Documents.TextElement.SetFontSize(host, window.FontSize);
                    System.Windows.Media.TextOptions.SetTextFormattingMode(host, System.Windows.Media.TextOptions.GetTextFormattingMode(window));
                    host.UseLayoutRounding = window.UseLayoutRounding;
                    var sizes = new[]
                    {
                        new ShotSize("1280", 1280, 800, 96),
                        new ShotSize("1024", 1024, 728, 96),
                        // 1024x768 at 125 % scaling, less the taskbar: exactly 1024x718 pixels at 120 dpi (no partly covered edge pixels).
                        new ShotSize("1024at125", 819.2, 574.4, 120)
                    };
                    foreach (var theme in new[] { VisualTheme.Dark, VisualTheme.Light })
                    {
                        ThemeManager.Apply(theme);
                        foreach (var language in new[] { "en", "he", "es" })
                        {
                            Loc.Instance.SetLanguage(language);
                            foreach (var size in sizes)
                            {
                                model.IsCompact = WinTool.Core.Platform.WindowSizing.UseCompactSidebar(size.Width);
                                foreach (var item in model.NavItems)
                                {
                                    item.IsSelected = true;
                                    var name = theme + "-" + language + "-" + size.Name + "-" + item.Key;
                                    if (item.Page is LogsPageViewModel page)
                                    {
                                        // Populated: wait for the real reload, and fail rather than save an empty list.
                                        page.IsAll = true;
                                        if (!WaitFor(page.ReloadForCaptureAsync()) || page.IsEmpty)
                                            failures.Add("Logs screenshot " + name + " has no entries");
                                        failures.AddRange(Capture(host, size, System.IO.Path.Combine(shots, name + ".png")));
                                        // Empty state: the sample log has no Windows Upgrade lines.
                                        page.IsUpgrade = true;
                                        if (!WaitFor(page.ReloadForCaptureAsync()) || !page.IsEmpty)
                                            failures.Add("Logs empty-state screenshot " + name + " has entries");
                                        failures.AddRange(Capture(host, size, System.IO.Path.Combine(shots, name + "-empty.png")));
                                        page.IsAll = true;
                                    }
                                    else
                                    {
                                        PumpFor(TimeSpan.FromMilliseconds(700));
                                        failures.AddRange(Capture(host, size, System.IO.Path.Combine(shots, name + ".png")));
                                    }
                                }
                            }
                        }
                    }

                    output.WriteLine("  Screenshots saved to " + shots);
                }
            }
            catch (Exception ex)
            {
                failures.Add(ex.ToString());
            }
            finally
            {
                window.Close();
            }

            failures.AddRange(bindingErrors.Errors.Distinct().Select(e => "Binding: " + e));
            foreach (var failure in failures) output.WriteLine("  FAIL  " + failure);
            output.WriteLine(failures.Count == 0 ? "UI smoke test passed." : "UI smoke test failed: " + failures.Count + " problem(s).");
            AppLog.Current.Info("UiSmoke", failures.Count == 0 ? "Passed" : "Failed: " + failures.Count);
            return failures.Count == 0 ? Program.ExitOk : 1;
        }

        private sealed class ShotSize
        {
            public ShotSize(string name, double width, double height, double dpi)
            {
                Name = name;
                Width = width;
                Height = height;
                Dpi = dpi;
            }

            public string Name { get; }
            public double Width { get; }
            public double Height { get; }
            public double Dpi { get; }
        }

        /// <summary>
        /// Lays the window content out at exactly the requested size and draws it on the window
        /// background through a VisualBrush, which keeps right-to-left text as the screen shows it.
        /// Returns a failure when the result does not have the requested size.
        /// </summary>
        private static IEnumerable<string> Capture(FrameworkElement host, ShotSize size, string path)
        {
            host.Width = size.Width;
            host.Height = size.Height;
            host.Measure(new Size(size.Width, size.Height));
            host.Arrange(new Rect(0, 0, size.Width, size.Height));
            host.UpdateLayout();
            PumpFor(TimeSpan.FromMilliseconds(150));
            host.UpdateLayout();
            if (Math.Abs(host.ActualWidth - size.Width) > 0.5 || Math.Abs(host.ActualHeight - size.Height) > 0.5)
            {
                yield return "Screenshot " + System.IO.Path.GetFileName(path) + " laid out at " + host.ActualWidth + "x" + host.ActualHeight +
                             ", expected " + size.Width + "x" + size.Height;
                yield break;
            }

            var scale = size.Dpi / 96.0;
            var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(
                (int)Math.Round(size.Width * scale), (int)Math.Round(size.Height * scale), size.Dpi, size.Dpi, System.Windows.Media.PixelFormats.Pbgra32);
            bitmap.Render(host);
            var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
            encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
            using (var file = System.IO.File.Create(path)) encoder.Save(file);
        }

        /// <summary>Pumps until the task finishes (at most 5 s); false on timeout or failure.</summary>
        private static bool WaitFor(System.Threading.Tasks.Task task)
        {
            var until = DateTime.UtcNow.AddSeconds(5);
            while (!task.IsCompleted && DateTime.UtcNow < until) Pump();
            Pump();
            return task.Status == System.Threading.Tasks.TaskStatus.RanToCompletion;
        }

        private static void PumpFor(TimeSpan time)
        {
            var until = DateTime.UtcNow + time;
            while (DateTime.UtcNow < until) Pump();
        }

        /// <summary>
        /// Deterministic log content for the Logs screenshots: short and long, INFO/WARN/ERROR,
        /// a stack trace and mixed Hebrew/English. Written to a private folder, never the real log.
        /// </summary>
        private static void UseSampleLog()
        {
            var folder = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "WinTool-UiSmoke-Log-" + Guid.NewGuid().ToString("N"));
            AppLog.Current = new AppLog(folder);
            var log = AppLog.Current;
            log.Info("WindowsUpdate", "Scan started (Manual).");
            log.Info("WindowsUpdate", "4 updates found: 3 important, 1 optional. 2026-11 Cumulative Update for Windows 11 Version 24H2 for x64-based Systems (KB5070001), Microsoft Defender Antivirus antimalware platform update (KB4052623), Intel - System - 2025.1.2.3, Windows Malicious Software Removal Tool x64 - v5.130 (KB890830).");
            log.Warn("WindowsUpdate", "A restart is pending. Restart the computer to finish installing updates.");
            log.Error("WindowsUpdate", "Install failed for KB5070001 | COMException: 0x80240022 (WU_E_ALL_UPDATES_FAILED)    at WinTool.WindowsUpdate.Platform.WuaUpdateAgent.Install(IReadOnlyList`1 updates, CancellationToken token)    at WinTool.WindowsUpdate.Engine.UpdateRunner.RunPass(RunRequest request, Int32 pass)");
            log.Info("Office", "Office LTSC Professional Plus 2024 · עברית (he-il) + English (en-us) · Visio");
            log.Info("Office", "officedeploymenttool.exe signed by CN=Microsoft Corporation, O=Microsoft Corporation, L=Redmond, S=Washington, C=US");
            log.Info("App", "WinTool by PcNinja " + WinTool.Core.ProductInfo.InformationalVersion + "; mode: GUI");
        }

        private static void Pump()
        {
            Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.Background, new Action(() => { }));
        }

        private sealed class BindingErrorListener : TraceListener
        {
            public List<string> Errors { get; } = new List<string>();

            public override void Write(string message)
            {
            }

            public override void WriteLine(string message)
            {
                if (message != null && message.IndexOf("Error", StringComparison.Ordinal) >= 0) Errors.Add(message);
            }
        }
    }
}
