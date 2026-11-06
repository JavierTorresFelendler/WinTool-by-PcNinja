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
        public const int AdministratorRequired = 5;
        public const int Warning = 11;
        public const int Busy = 21;
    }
}
