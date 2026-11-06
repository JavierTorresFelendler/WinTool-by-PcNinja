// PcNinja shared single-instance guard.
//
// ONE SOURCE FILE shared by every PcNinja Windows tool (and the future unified
// tool). Keep this file byte-identical across repositories and keep it C# 5
// compatible: it is compiled by .NET 8 SDK projects, .NET Framework 4.8
// projects, the legacy csc.exe v4 compiler and PowerShell 5.1 Add-Type.
// No expression-bodied members, string interpolation, nameof, ?. or out var.
//
// Model (see shared/README.md for the full contract)
//   * The first GUI process creates two named per-session events and keeps
//     them for its lifetime (the kernel removes them on exit or crash):
//       Local\PcNinja.<AppId>.SingleInstance.v1       activation request
//       Local\PcNinja.<AppId>.SingleInstance.v1.Ack   activation acknowledgement
//   * A later launch is a "requester". It signals the request and waits for
//     the acknowledgement. The owner acknowledges only after its window was
//     really restored/focused, retrying while its window does not exist yet
//     (startup). While waiting, the requester re-signals periodically and
//     checks whether the owner has exited (closing); if so it becomes the
//     owner and starts normally. No request is lost silently.
//   * If the owner never acknowledges, the requester reports
//     ExistingNotResponding; the caller tells the user instead of starting a
//     second copy. An owner built before acknowledgements existed (no .Ack
//     event) is signalled once, as before.
//   * The owner activates its own window, avoiding UIPI limits between
//     elevated and standard processes. Event DACLs let interactive users
//     signal/wait and carry a Medium label, so an unelevated launch can reach
//     an elevated owner before asking for UAC.
//   * Fail open: if the guard cannot be created or the named object exists but
//     is unusable (inaccessible or a different object type), the app starts
//     normally and the outcome is GuardUnavailable with a diagnostic.
//
// Use only for GUI entry points. CLI/automation modes must not be blocked by
// an open window; protect shared operations with their own operation locks.

#pragma warning disable 8618, 8625, 8603

using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using Microsoft.Win32.SafeHandles;

namespace PcNinja.Shared
{
    /// <summary>Result of a single-instance check.</summary>
    public enum PcNinjaSingleInstanceOutcome
    {
        /// <summary>This process owns the instance; start the GUI.</summary>
        Owner = 0,
        /// <summary>Another instance acknowledged and focused its window; exit.</summary>
        ActivatedExisting = 1,
        /// <summary>Another instance exists but did not acknowledge in time; tell the user and exit.</summary>
        ExistingNotResponding = 2,
        /// <summary>The guard is unusable; start the GUI without it (fail open).</summary>
        GuardUnavailable = 3,
        /// <summary>This process already owns the instance (EXE host running the UI in-process).</summary>
        NestedOwner = 4,
        /// <summary>Probe only: no running instance; continue (for example, elevate).</summary>
        NoInstance = 5
    }

    /// <summary>Timing and test options. Defaults suit production use.</summary>
    public sealed class PcNinjaSingleInstanceOptions
    {
        public PcNinjaSingleInstanceOptions()
        {
            OwnerActivationRetryMilliseconds = 10000;
            AcknowledgementTimeoutMilliseconds = 15000;
            ResignalIntervalMilliseconds = 1000;
            PollIntervalMilliseconds = 100;
            AllowNestedOwner = true;
        }

        /// <summary>Owner: how long to retry when no window exists yet (startup).</summary>
        public int OwnerActivationRetryMilliseconds { get; set; }

        /// <summary>Requester: how long to wait for an acknowledgement. Keep above the owner retry.</summary>
        public int AcknowledgementTimeoutMilliseconds { get; set; }

        /// <summary>Requester: re-signal interval, so concurrent requesters are never starved.</summary>
        public int ResignalIntervalMilliseconds { get; set; }

        public int PollIntervalMilliseconds { get; set; }

        /// <summary>Recognise a second entry from the same owning process (default true).</summary>
        public bool AllowNestedOwner { get; set; }

        /// <summary>Test seam: skip the custom security descriptor.</summary>
        public bool ForceDefaultSecurity { get; set; }

        /// <summary>Owner: window activation; returns true once a window was focused. Test seam.</summary>
        public Func<bool> ActivateWindow { get; set; }
    }

    public sealed class PcNinjaSingleInstance : IDisposable
    {
        /// <summary>Process environment marker: "AppId|ProcessId" of the holder.</summary>
        public const string HeldEnvironmentName = "PCNINJA_SINGLE_INSTANCE_HELD";

        private const int ErrorFileNotFound = 2;
        private const int ErrorAccessDenied = 5;
        private const int ErrorAlreadyExists = 183;
        private const uint Synchronize = 0x00100000;
        private const uint EventModifyState = 0x0002;
        private const int AsfwAny = -1;

        private static Action<string> diagnosticSink;

        private readonly string appId;
        private readonly PcNinjaSingleInstanceOutcome outcome;
        private readonly PcNinjaSingleInstanceOptions options;
        private readonly WaitHandle activationEvent;
        private readonly IntPtr acknowledgementHandle;
        private readonly ManualResetEvent stopEvent;
        private readonly Thread listener;
        private int disposed;
        private int activationCount;

        /// <summary>Optional callback raised on a background thread after an acknowledged activation.</summary>
        public Action ActivationRequested { get; set; }

        /// <summary>Receives guard diagnostics (default: Trace). Set once at startup.</summary>
        public static Action<string> DiagnosticSink
        {
            get { return diagnosticSink; }
            set { diagnosticSink = value; }
        }

        public string AppId { get { return appId; } }

        public PcNinjaSingleInstanceOutcome Outcome { get { return outcome; } }

        /// <summary>True when the guard is active (false for fail-open and nested holders).</summary>
        public bool IsGuarded { get { return activationEvent != null; } }

        /// <summary>Number of acknowledged activation requests served by this owner.</summary>
        public int ActivationCount { get { return Interlocked.CompareExchange(ref activationCount, 0, 0); } }

        private PcNinjaSingleInstance(string appId, PcNinjaSingleInstanceOutcome outcome, IntPtr handle, PcNinjaSingleInstanceOptions options)
        {
            this.appId = appId;
            this.outcome = outcome;
            this.options = options;
            if (handle == IntPtr.Zero) return; // Fail-open or nested holder: nothing to listen to.

            activationEvent = new NativeWaitHandle(handle);
            int error;
            string unused;
            acknowledgementHandle = CreateNamedEvent(GetAcknowledgementName(appId), options.ForceDefaultSecurity, out error, out unused);
            if (acknowledgementHandle == IntPtr.Zero)
                Diagnostic("acknowledgement event unavailable (" + Describe(error) + "); requesters fall back to signal-only.");

            stopEvent = new ManualResetEvent(false);
            listener = new Thread(Listen);
            listener.IsBackground = true;
            listener.Name = "PcNinja single-instance listener";
            listener.Start();
            Environment.SetEnvironmentVariable(HeldEnvironmentName, Marker(appId));
        }

        /// <summary>
        /// Name of the per-session request event for an app id. Ids are fixed
        /// product identifiers such as "WinUpgradeTool"; changing one breaks
        /// detection between old and new builds, so never derive it from a version.
        /// </summary>
        public static string GetEventName(string appId)
        {
            if (appId == null || !Regex.IsMatch(appId, "^[A-Za-z0-9][A-Za-z0-9._-]{0,63}$"))
                throw new ArgumentException("The single-instance app id is invalid.", "appId");
            return @"Local\PcNinja." + appId + ".SingleInstance.v1";
        }

        public static string GetAcknowledgementName(string appId)
        {
            return GetEventName(appId) + ".Ack";
        }

        /// <summary>
        /// Probe for a launcher that is about to self-elevate. Returns true only
        /// when a running instance acknowledged the activation; exit then.
        /// </summary>
        public static bool TryActivateExisting(string appId)
        {
            PcNinjaSingleInstanceOutcome result;
            TryActivateExisting(appId, null, out result);
            return result == PcNinjaSingleInstanceOutcome.ActivatedExisting;
        }

        /// <summary>
        /// Probe with a detailed outcome: ActivatedExisting or ExistingNotResponding
        /// (exit), NoInstance or GuardUnavailable (continue, for example elevate).
        /// </summary>
        public static bool TryActivateExisting(string appId, PcNinjaSingleInstanceOptions options, out PcNinjaSingleInstanceOutcome outcome)
        {
            string name = GetEventName(appId);
            options = options ?? new PcNinjaSingleInstanceOptions();
            outcome = PcNinjaSingleInstanceOutcome.NoInstance;
            try
            {
                IntPtr handle = NativeMethods.OpenEvent(Synchronize | EventModifyState, false, name);
                if (handle == IntPtr.Zero)
                {
                    int error = Marshal.GetLastWin32Error();
                    if (error != ErrorFileNotFound)
                    {
                        // Exists but unusable from here (other type, inaccessible): let the
                        // elevated Acquire decide instead of guessing.
                        Diagnostic("probe could not open the request event (" + Describe(error) + "); continuing.");
                        outcome = PcNinjaSingleInstanceOutcome.GuardUnavailable;
                    }

                    return false;
                }

                IntPtr owned;
                outcome = RequestActivation(appId, handle, options, false, out owned);
                return outcome == PcNinjaSingleInstanceOutcome.ActivatedExisting;
            }
            catch (Exception ex)
            {
                Diagnostic("probe failed open: " + ex.Message);
                outcome = PcNinjaSingleInstanceOutcome.GuardUnavailable;
                return false;
            }
        }

        /// <summary>
        /// Takes single-instance ownership. Returns null when the caller must exit
        /// (another instance was activated or is not responding). Dispose the
        /// returned holder as soon as the main window has closed.
        /// </summary>
        public static PcNinjaSingleInstance Acquire(string appId)
        {
            PcNinjaSingleInstanceOutcome unused;
            return Acquire(appId, null, out unused);
        }

        /// <summary>
        /// Acquire with a detailed outcome. Null for ActivatedExisting and
        /// ExistingNotResponding; a holder for Owner, NestedOwner and
        /// GuardUnavailable (fail open: start the GUI).
        /// </summary>
        public static PcNinjaSingleInstance Acquire(string appId, PcNinjaSingleInstanceOptions options, out PcNinjaSingleInstanceOutcome outcome)
        {
            string name = GetEventName(appId);
            options = options ?? new PcNinjaSingleInstanceOptions();

            // The same process may enter twice (an EXE host that runs the
            // PowerShell UI in-process). Child processes inherit the variable,
            // so the marker also carries the holder's process id.
            if (options.AllowNestedOwner &&
                string.Equals(Environment.GetEnvironmentVariable(HeldEnvironmentName), Marker(appId), StringComparison.Ordinal))
            {
                outcome = PcNinjaSingleInstanceOutcome.NestedOwner;
                return new PcNinjaSingleInstance(appId, outcome, IntPtr.Zero, options);
            }

            try
            {
                int error;
                string security;
                IntPtr handle = CreateNamedEvent(name, options.ForceDefaultSecurity, out error, out security);
                if (handle != IntPtr.Zero && error != ErrorAlreadyExists)
                {
                    if (security != "custom") Diagnostic("guard uses " + security + " security; an unelevated launch may need UAC before it can reach this instance.");
                    outcome = PcNinjaSingleInstanceOutcome.Owner;
                    return new PcNinjaSingleInstance(appId, outcome, handle, options);
                }

                if (handle == IntPtr.Zero && error == ErrorAccessDenied)
                {
                    // Exists, but this account cannot open it fully; signal/wait rights may still work.
                    handle = NativeMethods.OpenEvent(Synchronize | EventModifyState, false, name);
                    if (handle == IntPtr.Zero)
                    {
                        Diagnostic("request event exists but is inaccessible (" + Describe(Marshal.GetLastWin32Error()) +
                            "); starting without the single-instance guard.");
                        outcome = PcNinjaSingleInstanceOutcome.GuardUnavailable;
                        return new PcNinjaSingleInstance(appId, outcome, IntPtr.Zero, options);
                    }
                }
                else if (handle == IntPtr.Zero)
                {
                    Diagnostic("request event unavailable (" + Describe(error) + "); starting without the single-instance guard.");
                    outcome = PcNinjaSingleInstanceOutcome.GuardUnavailable;
                    return new PcNinjaSingleInstance(appId, outcome, IntPtr.Zero, options);
                }

                IntPtr owned;
                outcome = RequestActivation(appId, handle, options, true, out owned);
                if (outcome == PcNinjaSingleInstanceOutcome.Owner)
                    return new PcNinjaSingleInstance(appId, outcome, owned, options);
                return null;
            }
            catch (Exception ex)
            {
                Diagnostic("guard failed open: " + ex.Message);
                outcome = PcNinjaSingleInstanceOutcome.GuardUnavailable;
                return new PcNinjaSingleInstance(appId, outcome, IntPtr.Zero, options);
            }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) != 0) return;
            if (stopEvent != null)
            {
                stopEvent.Set();
                if (listener != null && listener != Thread.CurrentThread) listener.Join(2000);
            }

            if (activationEvent != null)
            {
                activationEvent.Close();
                if (acknowledgementHandle != IntPtr.Zero) NativeMethods.CloseHandle(acknowledgementHandle);
                if (string.Equals(Environment.GetEnvironmentVariable(HeldEnvironmentName), Marker(appId), StringComparison.Ordinal))
                    Environment.SetEnvironmentVariable(HeldEnvironmentName, null);
            }
        }

        /// <summary>Restores and focuses the main visible window of the current process.</summary>
        public static bool ActivateCurrentProcessWindow()
        {
            IntPtr main = FindMainWindow((uint)Process.GetCurrentProcess().Id);
            if (main == IntPtr.Zero) return false;

            if (NativeMethods.IsIconic(main)) NativeMethods.ShowWindow(main, 9); // SW_RESTORE
            else NativeMethods.ShowWindow(main, 5); // SW_SHOW

            // A modal dialog owned by the main window should receive focus.
            IntPtr target = NativeMethods.GetLastActivePopup(main);
            if (target == IntPtr.Zero || !NativeMethods.IsWindowVisible(target)) target = main;

            NativeMethods.BringWindowToTop(target);
            if (NativeMethods.SetForegroundWindow(target)) return true;

            // Windows refused the focus change: flash the taskbar button instead.
            // The window exists and was restored, so the request is still served.
            NativeMethods.FLASHWINFO flash = new NativeMethods.FLASHWINFO();
            flash.cbSize = (uint)Marshal.SizeOf(typeof(NativeMethods.FLASHWINFO));
            flash.hwnd = target;
            flash.dwFlags = 3 | 12; // FLASHW_ALL | FLASHW_TIMERNOFG
            flash.uCount = 3;
            NativeMethods.FlashWindowEx(ref flash);
            return true;
        }

        // Owner side ---------------------------------------------------------

        private void Listen()
        {
            WaitHandle[] handles = new WaitHandle[] { stopEvent, activationEvent };
            while (true)
            {
                int index;
                try { index = WaitHandle.WaitAny(handles); }
                catch (Exception) { return; }
                if (index != 1) return;
                if (!ServeRequest()) return;
            }
        }

        // Returns false when the holder is being disposed.
        private bool ServeRequest()
        {
            Stopwatch elapsed = Stopwatch.StartNew();
            while (true)
            {
                bool activated = false;
                try
                {
                    Func<bool> activate = options.ActivateWindow;
                    activated = activate != null ? activate() : ActivateCurrentProcessWindow();
                }
                catch (Exception ex)
                {
                    Diagnostic("window activation failed: " + ex.Message);
                }

                if (activated)
                {
                    // Requests queued while retrying are satisfied by this activation;
                    // a requester whose signal is drained here re-signals and is served.
                    activationEvent.WaitOne(0);
                    Interlocked.Increment(ref activationCount);
                    if (acknowledgementHandle != IntPtr.Zero) NativeMethods.SetEvent(acknowledgementHandle);
                    try
                    {
                        Action callback = ActivationRequested;
                        if (callback != null) callback();
                    }
                    catch (Exception ex)
                    {
                        Diagnostic("activation callback failed: " + ex.Message);
                    }

                    return true;
                }

                // No window yet (startup) or any more (closing): retry, bounded.
                if (elapsed.ElapsedMilliseconds >= options.OwnerActivationRetryMilliseconds)
                {
                    Diagnostic("activation request not served: no window within " +
                        options.OwnerActivationRetryMilliseconds.ToString(System.Globalization.CultureInfo.InvariantCulture) + " ms.");
                    return true;
                }

                if (stopEvent.WaitOne(options.PollIntervalMilliseconds)) return false;
            }
        }

        // Requester side -----------------------------------------------------

        // Takes ownership of requestHandle (an opened request event) and closes it.
        private static PcNinjaSingleInstanceOutcome RequestActivation(string appId, IntPtr requestHandle,
            PcNinjaSingleInstanceOptions options, bool canBecomeOwner, out IntPtr ownedHandle)
        {
            ownedHandle = IntPtr.Zero;
            string name = GetEventName(appId);
            IntPtr ackHandle = NativeMethods.OpenEvent(Synchronize | EventModifyState, false, GetAcknowledgementName(appId));
            if (ackHandle == IntPtr.Zero)
            {
                // Owner built before acknowledgements (or ack creation failed): legacy signal-only.
                try { Signal(requestHandle); }
                finally { NativeMethods.CloseHandle(requestHandle); }
                return PcNinjaSingleInstanceOutcome.ActivatedExisting;
            }

            NativeWaitHandle ack = new NativeWaitHandle(ackHandle);
            try
            {
                ack.WaitOne(0); // Drain a stale acknowledgement left by an abandoned requester.
                Signal(requestHandle);
                NativeMethods.CloseHandle(requestHandle); // Do not keep the owner's object alive.

                Stopwatch elapsed = Stopwatch.StartNew();
                long lastSignal = 0;
                while (true)
                {
                    if (ack.WaitOne(options.PollIntervalMilliseconds)) return PcNinjaSingleInstanceOutcome.ActivatedExisting;

                    // Has the owner exited (for example it was closing)?
                    bool createdNew;
                    IntPtr handle = OpenOrCreate(name, options, canBecomeOwner, out createdNew);
                    if (handle == IntPtr.Zero)
                    {
                        if (!canBecomeOwner) return PcNinjaSingleInstanceOutcome.NoInstance;
                    }
                    else if (createdNew)
                    {
                        ownedHandle = handle;
                        Diagnostic("previous instance exited while being activated; this instance takes over.");
                        return PcNinjaSingleInstanceOutcome.Owner;
                    }
                    else
                    {
                        // Re-signal periodically so concurrent requesters are never starved.
                        if (elapsed.ElapsedMilliseconds - lastSignal >= options.ResignalIntervalMilliseconds)
                        {
                            Signal(handle);
                            lastSignal = elapsed.ElapsedMilliseconds;
                        }

                        NativeMethods.CloseHandle(handle);
                    }

                    if (elapsed.ElapsedMilliseconds >= options.AcknowledgementTimeoutMilliseconds)
                    {
                        Diagnostic("running instance did not acknowledge activation within " +
                            options.AcknowledgementTimeoutMilliseconds.ToString(System.Globalization.CultureInfo.InvariantCulture) + " ms.");
                        return PcNinjaSingleInstanceOutcome.ExistingNotResponding;
                    }
                }
            }
            finally
            {
                ack.Close();
            }
        }

        private static IntPtr OpenOrCreate(string name, PcNinjaSingleInstanceOptions options, bool create, out bool createdNew)
        {
            createdNew = false;
            if (!create) return NativeMethods.OpenEvent(Synchronize | EventModifyState, false, name);

            int error;
            string unused;
            IntPtr handle = CreateNamedEvent(name, options.ForceDefaultSecurity, out error, out unused);
            if (handle != IntPtr.Zero && error != ErrorAlreadyExists) createdNew = true;
            if (handle == IntPtr.Zero && error == ErrorAccessDenied)
                handle = NativeMethods.OpenEvent(Synchronize | EventModifyState, false, name);
            return handle;
        }

        private static bool Signal(IntPtr handle)
        {
            // Must run while this process still owns the user's foreground
            // permission (the user just launched it).
            NativeMethods.AllowSetForegroundWindow(AsfwAny);
            return NativeMethods.SetEvent(handle);
        }

        // Shared helpers -----------------------------------------------------

        private static IntPtr CreateNamedEvent(string name, bool forceDefaultSecurity, out int error, out string security)
        {
            IntPtr handle;
            IntPtr descriptor = IntPtr.Zero;
            error = 0;
            if (!forceDefaultSecurity)
            {
                try
                {
                    // SYSTEM and Administrators: full; interactive users: wait + signal;
                    // creator: full. Medium label so unelevated launches can signal.
                    string userSid;
                    using (System.Security.Principal.WindowsIdentity identity = System.Security.Principal.WindowsIdentity.GetCurrent())
                    {
                        var user = identity.User;
                        if (user == null)
                            throw new InvalidOperationException("The current Windows user SID is unavailable.");
                        userSid = user.Value;
                    }
                    string sddl = "D:(A;;GA;;;SY)(A;;GA;;;BA)(A;;0x00100002;;;IU)(A;;GA;;;" + userSid + ")S:(ML;;NW;;;ME)";
                    uint size;
                    if (NativeMethods.ConvertStringSecurityDescriptorToSecurityDescriptor(sddl, 1, out descriptor, out size))
                    {
                        NativeMethods.SECURITY_ATTRIBUTES attributes = new NativeMethods.SECURITY_ATTRIBUTES();
                        attributes.nLength = Marshal.SizeOf(typeof(NativeMethods.SECURITY_ATTRIBUTES));
                        attributes.lpSecurityDescriptor = descriptor;
                        attributes.bInheritHandle = false;
                        handle = NativeMethods.CreateEventEx(ref attributes, name, 0, Synchronize | EventModifyState);
                        error = Marshal.GetLastWin32Error();
                        // Existing objects, access denial and type conflicts are final answers.
                        if (handle != IntPtr.Zero || error == ErrorAccessDenied || error == 6)
                        {
                            security = "custom";
                            return handle;
                        }

                        Diagnostic("custom event security rejected (" + Describe(error) + "); using default security.");
                    }
                    else
                    {
                        Diagnostic("security descriptor unavailable (" + Describe(Marshal.GetLastWin32Error()) + "); using default security.");
                    }
                }
                finally
                {
                    if (descriptor != IntPtr.Zero) NativeMethods.LocalFree(descriptor);
                }
            }

            handle = NativeMethods.CreateEventExDefault(IntPtr.Zero, name, 0, Synchronize | EventModifyState);
            error = Marshal.GetLastWin32Error();
            security = "default";
            return handle;
        }

        private static IntPtr FindMainWindow(uint processId)
        {
            IntPtr found = IntPtr.Zero;
            NativeMethods.EnumWindows(delegate (IntPtr window, IntPtr unused)
            {
                uint owner;
                NativeMethods.GetWindowThreadProcessId(window, out owner);
                if (owner != processId || !NativeMethods.IsWindowVisible(window)) return true;
                if (NativeMethods.GetWindow(window, 4) != IntPtr.Zero) return true; // GW_OWNER: skip owned dialogs.
                found = window; // EnumWindows walks the Z order; first match is the top one.
                return false;
            }, IntPtr.Zero);
            return found;
        }

        private static void Diagnostic(string message)
        {
            string line = "PcNinja single-instance: " + message;
            try
            {
                Action<string> sink = diagnosticSink;
                if (sink != null) sink(line);
                else Trace.WriteLine(line);
            }
            catch (Exception)
            {
                // Diagnostics must never break startup.
            }
        }

        private static string Describe(int error)
        {
            return "Win32 " + error.ToString(System.Globalization.CultureInfo.InvariantCulture) + ": " + new Win32Exception(error).Message;
        }

        private static string Marker(string appId)
        {
            return appId + "|" + Process.GetCurrentProcess().Id.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        private sealed class NativeWaitHandle : WaitHandle
        {
            public NativeWaitHandle(IntPtr handle)
            {
                SafeWaitHandle = new SafeWaitHandle(handle, true);
            }
        }

        private static class NativeMethods
        {
            internal delegate bool EnumWindowsProc(IntPtr window, IntPtr parameter);

            [StructLayout(LayoutKind.Sequential)]
            internal struct SECURITY_ATTRIBUTES
            {
                public int nLength;
                public IntPtr lpSecurityDescriptor;
                public bool bInheritHandle;
            }

            [StructLayout(LayoutKind.Sequential)]
            internal struct FLASHWINFO
            {
                public uint cbSize;
                public IntPtr hwnd;
                public uint dwFlags;
                public uint uCount;
                public uint dwTimeout;
            }

            [DllImport("kernel32.dll", EntryPoint = "CreateEventExW", SetLastError = true, CharSet = CharSet.Unicode)]
            internal static extern IntPtr CreateEventEx(ref SECURITY_ATTRIBUTES attributes, string name, uint flags, uint desiredAccess);

            [DllImport("kernel32.dll", EntryPoint = "CreateEventExW", SetLastError = true, CharSet = CharSet.Unicode)]
            internal static extern IntPtr CreateEventExDefault(IntPtr attributes, string name, uint flags, uint desiredAccess);

            [DllImport("kernel32.dll", EntryPoint = "OpenEventW", SetLastError = true, CharSet = CharSet.Unicode)]
            internal static extern IntPtr OpenEvent(uint desiredAccess, bool inheritHandle, string name);

            [DllImport("kernel32.dll", SetLastError = true)]
            internal static extern bool SetEvent(IntPtr handle);

            [DllImport("kernel32.dll", SetLastError = true)]
            internal static extern bool CloseHandle(IntPtr handle);

            [DllImport("kernel32.dll")]
            internal static extern IntPtr LocalFree(IntPtr memory);

            [DllImport("advapi32.dll", EntryPoint = "ConvertStringSecurityDescriptorToSecurityDescriptorW", SetLastError = true, CharSet = CharSet.Unicode)]
            internal static extern bool ConvertStringSecurityDescriptorToSecurityDescriptor(string sddl, uint revision, out IntPtr descriptor, out uint size);

            [DllImport("user32.dll")]
            internal static extern bool AllowSetForegroundWindow(int processId);

            [DllImport("user32.dll")]
            internal static extern bool EnumWindows(EnumWindowsProc callback, IntPtr parameter);

            [DllImport("user32.dll")]
            internal static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

            [DllImport("user32.dll")]
            internal static extern bool IsWindowVisible(IntPtr window);

            [DllImport("user32.dll")]
            internal static extern bool IsIconic(IntPtr window);

            [DllImport("user32.dll")]
            internal static extern IntPtr GetWindow(IntPtr window, uint command);

            [DllImport("user32.dll")]
            internal static extern IntPtr GetLastActivePopup(IntPtr window);

            [DllImport("user32.dll")]
            internal static extern bool ShowWindow(IntPtr window, int command);

            [DllImport("user32.dll")]
            internal static extern bool BringWindowToTop(IntPtr window);

            [DllImport("user32.dll")]
            internal static extern bool SetForegroundWindow(IntPtr window);

            [DllImport("user32.dll")]
            internal static extern bool FlashWindowEx(ref FLASHWINFO info);
        }
    }
}
