using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using WinTool.WindowsUpdate.Schedule;

namespace WinTool.WindowsUpdate.Platform
{
    /// <summary>Task Scheduler 2.0 through COM late binding (Schedule.Service).</summary>
    public sealed class ComTaskScheduler : ITaskSchedulerService
    {
        private const int CreateOrUpdate = 6;
        private const int LogonServiceAccount = 5;
        private const int IncludeHidden = 1;

        public void Register(string name, string xml)
        {
            dynamic folder = GetOrCreateFolder(TaskNames.Folder);
            folder.RegisterTask(name, xml, CreateOrUpdate, "SYSTEM", null, LogonServiceAccount, null);
        }

        public bool Delete(string folderPath, string name)
        {
            try
            {
                dynamic folder = Connect().GetFolder(folderPath);
                folder.DeleteTask(name, 0);
                return true;
            }
            catch (Exception ex) when (IsNotFound(ex))
            {
                return false;
            }
        }

        public TaskEntry Get(string name)
        {
            try
            {
                dynamic folder = Connect().GetFolder(TaskNames.Folder);
                return ToEntry(TaskNames.Folder, folder.GetTask(name));
            }
            catch (Exception ex) when (IsNotFound(ex))
            {
                return null;
            }
        }

        public IReadOnlyList<TaskEntry> Find(Func<string, bool> nameMatches)
        {
            var result = new List<TaskEntry>();
            Walk(Connect().GetFolder(@"\"), nameMatches, result);
            return result;
        }

        public void Run(string name)
        {
            dynamic folder = Connect().GetFolder(TaskNames.Folder);
            dynamic task = folder.GetTask(name);
            task.Run(null);
        }

        private static void Walk(dynamic folder, Func<string, bool> nameMatches, List<TaskEntry> result)
        {
            string path = folder.Path;
            foreach (dynamic task in folder.GetTasks(IncludeHidden))
            {
                string name = task.Name;
                if (nameMatches(name)) result.Add(ToEntry(path, task));
            }

            foreach (dynamic child in folder.GetFolders(0))
            {
                try { Walk(child, nameMatches, result); }
                catch (UnauthorizedAccessException) { }
                catch (COMException) { }
            }
        }

        private static TaskEntry ToEntry(string folder, dynamic task)
        {
            var entry = new TaskEntry { Folder = folder, Name = (string)task.Name };
            try { entry.Xml = (string)task.Xml; } catch (COMException) { }
            try { entry.State = StateName(Convert.ToInt32(task.State)); } catch (COMException) { }
            try { entry.LastTaskResult = Convert.ToInt32(task.LastTaskResult); } catch (COMException) { }
            try { entry.LastRunTime = Valid((DateTime)task.LastRunTime); } catch (COMException) { }
            try { entry.NextRunTime = Valid((DateTime)task.NextRunTime); } catch (COMException) { }
            return entry;
        }

        private static DateTime? Valid(DateTime value) => value.Year < 2000 ? (DateTime?)null : value;

        private static string StateName(int state)
        {
            switch (state)
            {
                case 1: return "Disabled";
                case 2: return "Queued";
                case 3: return "Ready";
                case 4: return "Running";
                default: return "Unknown";
            }
        }

        private static dynamic GetOrCreateFolder(string path)
        {
            dynamic service = Connect();
            try
            {
                return service.GetFolder(path);
            }
            catch (Exception ex) when (IsNotFound(ex))
            {
                return service.GetFolder(@"\").CreateFolder(path.TrimStart('\\'), null);
            }
        }

        private static dynamic Connect()
        {
            dynamic service = Activator.CreateInstance(Type.GetTypeFromProgID("Schedule.Service", true));
            service.Connect();
            return service;
        }

        /// <summary>COM interop maps "not found" to FileNotFound/DirectoryNotFound, or leaves a COMException.</summary>
        private static bool IsNotFound(Exception ex)
        {
            if (ex is System.IO.FileNotFoundException || ex is System.IO.DirectoryNotFoundException) return true;
            var code = (uint)ex.HResult;
            return ex is COMException && (code == 0x80070002 || code == 0x80070003);
        }
    }
}
