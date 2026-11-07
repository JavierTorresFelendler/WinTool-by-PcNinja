using System;
using System.IO;
using WinTool.App.Infrastructure;
using WinTool.App.Localization;
using WinTool.Core.Diagnostics;
using WinTool.WindowsUpdate.Engine;
using WinTool.WindowsUpdate.Storage;

namespace WinTool.App.ViewModels
{
    /// <summary>
    /// After WinTool's installer took over from WinUpdate Tool in the background, the next window
    /// start says once what happened (owner request, D-025).
    /// </summary>
    internal static class TakeoverNotice
    {
        public static void ShowOnce(IWindowsUpdateEnvironment env)
        {
            try
            {
                var state = env.Store.LoadState();
                var kind = state.PendingTakeoverNotice;
                if (string.IsNullOrEmpty(kind)) return;
                state.PendingTakeoverNotice = null;
                env.Store.SaveState(state);
                var t = Loc.Instance;
                var schedule = ScheduleWords.Describe(env.Store.LoadSettings(), t);
                var text = kind == TakeoverNotices.Imported ? t.Format("WU_Takeover_Imported", schedule)
                    : kind == TakeoverNotices.Kept ? t.Format("WU_Takeover_Kept", schedule)
                    : t["WU_Takeover_Removed"];
                AppLog.Current.Info("Schedule", "Shown the WinUpdate Tool takeover notice (" + kind + ").");
                Dialogs.Info(text);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                AppLog.Current.Warn("Schedule", "The WinUpdate Tool takeover notice could not be read: " + ex.Message);
            }
        }

        public static void Clear(IWindowsUpdateEnvironment env)
        {
            try
            {
                var state = env.Store.LoadState();
                if (string.IsNullOrEmpty(state.PendingTakeoverNotice)) return;
                state.PendingTakeoverNotice = null;
                env.Store.SaveState(state);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                AppLog.Current.Warn("Schedule", "The WinUpdate Tool takeover notice could not be cleared: " + ex.Message);
            }
        }
    }
}
