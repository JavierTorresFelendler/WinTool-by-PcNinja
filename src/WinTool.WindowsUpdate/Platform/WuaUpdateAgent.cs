using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using WinTool.WindowsUpdate.Engine;
using WinTool.WindowsUpdate.Model;

namespace WinTool.WindowsUpdate.Platform
{
    /// <summary>
    /// The Windows Update Agent through COM late binding (WU-01). Late binding
    /// avoids a generated interop assembly, so WinTool-by-PcNinja.exe stays one file.
    /// </summary>
    public sealed class WuaUpdateAgent : IUpdateAgent
    {
        public const string ClientApplicationId = "WinTool by PcNinja";
        public const string MicrosoftUpdateServiceId = "7971f918-a847-4430-9279-4a52d1efe18d";

        private readonly dynamic session;
        private readonly IRunLog log;
        private bool? microsoftUpdateRegistered;

        public WuaUpdateAgent(IRunLog log)
        {
            this.log = log;
            session = Create("Microsoft.Update.Session");
            session.ClientApplicationID = ClientApplicationId;
        }

        public bool EnsureMicrosoftUpdateRegistered()
        {
            if (microsoftUpdateRegistered.HasValue) return microsoftUpdateRegistered.Value;
            try
            {
                if (IsMicrosoftUpdateRegistered())
                {
                    log.Write(EventIds.MicrosoftUpdateRegistration, EventLevel.Information, "Microsoft Update is already registered.");
                }
                else
                {
                    dynamic manager = Create("Microsoft.Update.ServiceManager");
                    manager.ClientApplicationID = ClientApplicationId;
                    manager.AddService2(MicrosoftUpdateServiceId, 7, string.Empty);
                    log.Write(EventIds.MicrosoftUpdateRegistration, EventLevel.Information, "Microsoft Update registered for broader update coverage.");
                }

                microsoftUpdateRegistered = true;
            }
            catch (Exception ex) when (IsComFailure(ex))
            {
                log.Write(EventIds.MicrosoftUpdateRegistration, EventLevel.Warning, "Microsoft Update registration was skipped or failed: " + ex.Message);
                microsoftUpdateRegistered = false;
            }

            return microsoftUpdateRegistered.Value;
        }

        /// <summary>Read-only check, used by the health check.</summary>
        public static bool IsMicrosoftUpdateRegistered()
        {
            dynamic manager = Create("Microsoft.Update.ServiceManager");
            foreach (dynamic service in manager.Services)
            {
                if (string.Equals((string)service.ServiceID, MicrosoftUpdateServiceId, StringComparison.OrdinalIgnoreCase)) return true;
            }

            return false;
        }

        public IReadOnlyList<UpdateItem> Search(string criteria)
        {
            try
            {
                dynamic searcher = session.CreateUpdateSearcher();
                dynamic result = searcher.Search(criteria);
                var updates = result.Updates;
                int count = updates.Count;
                var list = new List<UpdateItem>(count);
                for (var i = 0; i < count; i++) list.Add(Map(updates.Item(i)));
                return list;
            }
            catch (Exception ex) when (IsComFailure(ex))
            {
                throw new UpdateAgentException("Windows Update search failed: " + ex.Message, ex.HResult, ex);
            }
        }

        public void AcceptEula(UpdateItem update)
        {
            try
            {
                dynamic native = update.Native;
                native.AcceptEula();
                update.EulaAccepted = true;
            }
            catch (Exception ex) when (IsComFailure(ex))
            {
                log.Write(EventIds.UpdateItemFailed, EventLevel.Warning, "Could not accept the license for " + update.Title + ": " + ex.Message);
            }
        }

        public ItemResult Download(UpdateItem update, CancellationToken cancellation)
        {
            try
            {
                dynamic collection = Create("Microsoft.Update.UpdateColl");
                collection.Add(update.Native);
                dynamic downloader = session.CreateUpdateDownloader();
                downloader.Updates = collection;
                dynamic result = downloader.Download();
                dynamic native = update.Native;
                update.IsDownloaded = (bool)native.IsDownloaded;
                return new ItemResult(update, (OperationResult)Convert.ToInt32(result.ResultCode), Convert.ToInt32(result.HResult));
            }
            catch (Exception ex) when (IsComFailure(ex))
            {
                return new ItemResult(update, OperationResult.Failed, ex.HResult);
            }
        }

        public InstallOutcome Install(IReadOnlyList<UpdateItem> updates)
        {
            try
            {
                dynamic collection = Create("Microsoft.Update.UpdateColl");
                foreach (var update in updates) collection.Add(update.Native);
                dynamic installer = session.CreateUpdateInstaller();
                installer.Updates = collection;
                dynamic result = installer.Install();

                var items = new List<ItemResult>(updates.Count);
                for (var i = 0; i < updates.Count; i++)
                {
                    dynamic itemResult = result.GetUpdateResult(i);
                    items.Add(new ItemResult(updates[i], (OperationResult)Convert.ToInt32(itemResult.ResultCode), Convert.ToInt32(itemResult.HResult)));
                }

                return new InstallOutcome((OperationResult)Convert.ToInt32(result.ResultCode), (bool)result.RebootRequired, items);
            }
            catch (Exception ex) when (IsComFailure(ex))
            {
                throw new UpdateAgentException("Windows Update installation failed: " + ex.Message, ex.HResult, ex);
            }
        }

        private static UpdateItem Map(dynamic update)
        {
            dynamic identity = Try(() => update.Identity);
            string id = identity == null ? string.Empty : Try(() => (string)identity.UpdateID) ?? string.Empty;
            int revision = identity == null ? 0 : Try(() => Convert.ToInt32(identity.RevisionNumber));
            var item = new UpdateItem(id, revision, Try(() => (string)update.Title) ?? string.Empty) { Native = update };

            Try(() =>
            {
                foreach (object kb in update.KBArticleIDs) item.KbArticles.Add("KB" + kb);
                return 0;
            });
            Try(() =>
            {
                foreach (dynamic category in update.Categories) item.Categories.Add((string)category.Name);
                return 0;
            });

            item.IsDriverType = Try(() => Convert.ToInt32(update.Type)) == 2;
            item.BrowseOnly = Try(() => (bool)update.BrowseOnly);
            item.IsMandatory = Try(() => (bool)update.IsMandatory);
            item.EulaAccepted = Try(() => (bool)update.EulaAccepted);
            item.IsDownloaded = Try(() => (bool)update.IsDownloaded);
            item.MaxDownloadSize = Try(() => Convert.ToInt64(update.MaxDownloadSize));
            if (item.IsDriverType) item.DriverClass = Try(() => (string)update.DriverClass);
            return item;
        }

        /// <summary>One unreadable property must not hide the whole update.</summary>
        private static T Try<T>(Func<T> read)
        {
            try { return read(); }
            catch (Exception ex) when (IsComFailure(ex)) { return default(T); }
        }

        private static dynamic Create(string progId)
        {
            var type = Type.GetTypeFromProgID(progId, true);
            return Activator.CreateInstance(type);
        }

        private static bool IsComFailure(Exception ex)
        {
            return ex is COMException || ex is InvalidCastException || ex is InvalidComObjectException
                || ex is Microsoft.CSharp.RuntimeBinder.RuntimeBinderException || ex is UnauthorizedAccessException
                || ex is ArgumentException || ex is NotSupportedException;
        }
    }
}
