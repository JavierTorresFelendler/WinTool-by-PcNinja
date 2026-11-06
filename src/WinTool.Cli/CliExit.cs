namespace WinTool.Cli
{
    /// <summary>
    /// Exit codes of commands other than "update run", which uses
    /// WinTool.WindowsUpdate.Engine.ExitCodes. The numbers never overlap in meaning.
    /// </summary>
    public static class CliExit
    {
        public const int Ok = 0;
        public const int Failed = 1;
        public const int Usage = 2;
        public const int Cancelled = 3;
        public const int AdministratorRequired = 5;
        public const int Warning = 11;
        public const int Busy = 21;

        // Windows Upgrade (decision D-021).
        public const int UpgradeBlocked = 30;
        public const int DownloadFailed = 31;
        public const int SetupFailed = 32;
        public const int SetupOutcomeUnconfirmed = 33;
    }
}
