using System;
using System.Collections.Generic;

namespace WinTool.WindowsUpdate.Schedule
{
    public sealed class TaskEntry
    {
        public string Folder { get; set; }
        public string Name { get; set; }
        public string State { get; set; }
        public DateTime? LastRunTime { get; set; }
        public DateTime? NextRunTime { get; set; }
        public int? LastTaskResult { get; set; }
        public string Xml { get; set; }
        public string FullPath => (Folder == @"\" ? string.Empty : Folder) + @"\" + Name;
    }

    /// <summary>Windows Task Scheduler. The real one uses the Schedule.Service COM API; tests use a fake.</summary>
    public interface ITaskSchedulerService
    {
        /// <summary>Creates or replaces a task in \PcNinja, running as SYSTEM.</summary>
        void Register(string name, string xml);

        /// <summary>Returns false when the task did not exist.</summary>
        bool Delete(string folder, string name);

        TaskEntry Get(string name);

        /// <summary>All tasks in every folder whose name matches.</summary>
        IReadOnlyList<TaskEntry> Find(Func<string, bool> nameMatches);

        void Run(string name);
    }
}
