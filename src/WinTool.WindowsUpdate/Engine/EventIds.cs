namespace WinTool.WindowsUpdate.Engine
{
    /// <summary>One fixed Windows event id per meaning (WU-83). Never reuse or renumber.</summary>
    public static class EventIds
    {
        public const int RunStarted = 2000;
        public const int RunFinished = 2001;
        public const int RunFailed = 2002;
        public const int RunSkipped = 2003;
        public const int CooldownActive = 2004;
        public const int RestartPending = 2010;
        public const int NetworkNotReady = 2011;
        public const int WindowsBusy = 2012;
        public const int WindowsUpdateSnoozed = 2013;
        public const int SearchResult = 2020;
        public const int SearchFailed = 2021;
        public const int UpdateFound = 2022;
        public const int UpdateSkippedFirmware = 2023;
        public const int UpdateSkippedScope = 2024;
        public const int SearchQueryStarted = 2025;
        public const int SearchQueryFinished = 2026;
        public const int SearchPlan = 2027;
        public const int VerificationResult = 2028;
        public const int DownloadResult = 2030;
        public const int InstallResult = 2031;
        public const int UpdateItemFailed = 2032;
        public const int CancelRequested = 2033;
        public const int RetryScheduled = 2040;
        public const int RetryNotScheduled = 2041;
        public const int RetryCancelled = 2042;
        public const int MicrosoftUpdateRegistration = 2050;
        public const int ResetStarted = 2060;
        public const int ResetFinished = 2061;
        public const int ResetFailed = 2062;
        public const int ScheduleSaved = 2070;
        public const int ScheduleRemoved = 2071;
        public const int ScheduleFailed = 2072;
        public const int LegacyTakeover = 2080;
    }
}
