using System;
using WinTool.Core.Operations;

namespace WinTool.WindowsUpdate.Schedule
{
    public static class MaintenancePolicy
    {
        public static bool NonFatal(Exception ex) => !(ex is OutOfMemoryException) && !(ex is StackOverflowException) && !(ex is AccessViolationException);

        public static bool RefreshOnCommand(string[] args)
        {
            if (args.Length < 2) return false;
            var command = (args[0] + " " + args[1]).ToLowerInvariant();
            switch (command)
            {
                case "update run": case "update reset": case "update start-as-system":
                case "schedule set": case "schedule import": case "schedule import-v2":
                    return true;
                default: return false;
            }
        }

        public static int Execute(Action operation, bool removal, Action<Exception> warning)
        {
            try { operation(); return 0; }
            catch (OperationBusyException ex) { warning(ex); return removal ? 21 : 1; }
            catch (Exception ex) when (NonFatal(ex)) { warning(ex); return removal ? 0 : 1; }
        }
    }
}
