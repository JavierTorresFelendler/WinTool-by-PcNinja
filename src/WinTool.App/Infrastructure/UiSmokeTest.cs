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

                // Screenshots for review (CI uploads them): every page, loaded, in the main combinations.
                var shots = Environment.GetEnvironmentVariable("WINTOOL_SMOKE_SCREENSHOTS");
                if (!string.IsNullOrEmpty(shots))
                {
                    System.IO.Directory.CreateDirectory(shots);
                    foreach (var theme in new[] { VisualTheme.Dark, VisualTheme.Light })
                    {
                        ThemeManager.Apply(theme);
                        foreach (var language in new[] { "en", "he", "es" })
                        {
                            Loc.Instance.SetLanguage(language);
                            foreach (var size in new[] { new Size(1280, 800), new Size(1024, 728) })
                            {
                                window.Width = size.Width;
                                window.Height = size.Height;
                                foreach (var item in model.NavItems)
                                {
                                    item.IsSelected = true;
                                    for (var i = 0; i < 5; i++) Pump();
                                    window.UpdateLayout();
                                    Capture(window, System.IO.Path.Combine(shots,
                                        theme + "-" + language + "-" + (int)size.Width + "-" + item.Key + ".png"));
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

        private static void Capture(Window window, string path)
        {
            var root = (FrameworkElement)window.Content;
            var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(
                (int)Math.Ceiling(root.ActualWidth), (int)Math.Ceiling(root.ActualHeight), 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
            bitmap.Render(root);
            var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
            encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
            using (var file = System.IO.File.Create(path)) encoder.Save(file);
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
