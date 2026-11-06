// Ported from Windows Upgrade Tool by PcNinja (AppOperationGate, 1.3.0) for WinTool: one machine-wide gate for every WinTool operation.
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Runtime.InteropServices;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;

namespace WinTool.Core.Operations;

/// <summary>
/// Machine-wide, non-thread-affine exclusion between Windows preparation/Setup
/// and portable application replacement. This is not an OS-restart permission.
/// </summary>
public static class OperationGate
{
    private const string ProductionName = @"Global\PcNinja.WinTool.Operation.v2";
    private const string Administrators = "S-1-5-32-544";
    private const string SystemAccount = "S-1-5-18";
    private static readonly AsyncLocal<Scope?> LogicalScope = new();
    private static readonly Definition Production = new(ProductionName, Administrators,
        [SystemAccount, Administrators], RequireAdministrator);

    public static OperationLease EnterWindows() => Enter(Production, update: false);
    public static OperationLease EnterUpdate() => Enter(Production, update: true);

    /// <summary>
    /// Opens, but does not acquire/adopt, the already-held semaphore. Only a
    /// verified internal helper may use this after validating its handoff request.
    /// It must signal READY with this handle open, wait for GO, then AdoptHandoff.
    /// </summary>
    public static OperationLease OpenHandoff() => OpenHandoff(Production);

    private static void RequireAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        if (!new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator))
            throw new UnauthorizedAccessException("Administrator permission is required to acquire the application operation gate.");
    }

    private static OperationLease Enter(Definition definition, bool update)
    {
        definition.RequireAdministrator();
        var previous = ActiveScope();
        if (previous is not null)
        {
            if (update || previous.State.Update || previous.State.Name != definition.Name)
                throw new InvalidOperationException("An application-update operation cannot be nested with Windows preparation or another application update.");
            previous.State.AddNested();
            var nested = new Scope(previous.State, previous);
            LogicalScope.Value = nested;
            return new(previous.State, nested, root: false);
        }

        var handle = Native.Create(definition);
        var acquired = false;
        try
        {
            var wait = Native.WaitForSingleObject(handle, 0);
            if (wait == 258) throw new OperationBusyException();
            if (wait != 0) throw new Win32Exception(Marshal.GetLastWin32Error(), "The application operation gate could not be acquired.");
            acquired = true;
            var state = new State(definition.Name, handle, update, handoff: false);
            var scope = new Scope(state, previous);
            LogicalScope.Value = scope;
            return new(state, scope, root: true);
        }
        catch
        {
            try { if (acquired) Native.ReleaseChecked(handle); }
            finally { handle.Dispose(); }
            throw;
        }
    }

    private static OperationLease OpenHandoff(Definition definition)
    {
        definition.RequireAdministrator();
        if (ActiveScope() is not null)
            throw new InvalidOperationException("A handoff helper must not already own a Windows or application-update operation.");
        var handle = Native.OpenExisting(definition);
        var state = new State(definition.Name, handle, update: true, handoff: true);
        var scope = new Scope(state, previous: null);
        LogicalScope.Value = scope;
        return new(state, scope, root: true);
    }

    private static Scope? ActiveScope()
    {
        var scope = LogicalScope.Value;
        while (scope is not null && (scope.Disposed || !scope.State.Active)) scope = scope.Previous;
        return scope;
    }

    internal static void Leave(Scope scope)
    {
        scope.Disposed = true;
        if (ReferenceEquals(LogicalScope.Value, scope)) LogicalScope.Value = scope.Previous;
    }

    internal sealed class Scope(State state, Scope? previous)
    {
        internal State State { get; } = state;
        internal Scope? Previous { get; } = previous;
        internal volatile bool Disposed;
    }

    internal sealed class State(string name, SafeWaitHandle handle, bool update, bool handoff)
    {
        private readonly object _sync = new();
        private int _references = 1;
        private bool _active = true;
        private bool _release = !handoff;
        private bool _transferred;
        private bool _adopted;
        internal string Name { get; } = name;
        internal bool Update { get; } = update;
        internal bool Handoff { get; } = handoff;
        internal bool Active { get { lock (_sync) return _active; } }
        internal bool Transferred { get { lock (_sync) return _transferred; } }
        internal bool Adopted { get { lock (_sync) return _adopted; } }

        internal void AddNested()
        {
            lock (_sync)
            {
                Compat.ThrowIfDisposed(!_active, this);
                if (Update || _transferred) throw new InvalidOperationException("This operation cannot accept a nested Windows scope.");
                checked { _references++; }
            }
        }

        internal void Transfer(bool root)
        {
            lock (_sync)
            {
                Compat.ThrowIfDisposed(!_active, this);
                if (!root || !Update || Handoff || _transferred || !_release || _references != 1)
                    throw new InvalidOperationException("Only an exclusive root application-update lease can be transferred once.");
                _release = false;
                _transferred = true;
            }
        }

        internal void Adopt(bool root)
        {
            lock (_sync)
            {
                Compat.ThrowIfDisposed(!_active, this);
                if (!root || !Handoff || _adopted || _transferred || _references != 1)
                    throw new InvalidOperationException("Only an unadopted helper handoff lease can be adopted once after GO.");
                _adopted = true;
                _release = true;
            }
        }

        internal void ReleaseReference()
        {
            lock (_sync)
            {
                if (--_references != 0) return;
                _active = false;
                try { if (_release) Native.ReleaseChecked(handle); }
                finally { handle.Dispose(); }
            }
        }
    }

    private sealed record Definition(string Name, string OwnerSid, string[] AllowedSids,
        Action RequireAdministrator)
    {
        internal string Sddl => $"O:{OwnerSid}G:{OwnerSid}D:P" +
            string.Concat(AllowedSids.Select(sid => $"(A;;GA;;;{sid})"));
    }

    // Tests use ONLY a unique Local object and a current-user ACL. Permission
    // probes are injected; no production Global gate is touched.
    internal sealed class TestEndpoint
    {
        private readonly Definition _definition;
        internal TestEndpoint(string name, string sid, Action? administratorProbe = null)
        {
            if (!name.StartsWith(@"Local\PcNinja.WinTool.OperationGate.Tests.", StringComparison.Ordinal) || name.Length > 200)
                throw new ArgumentException("A unique Local test gate name is required.", nameof(name));
            var canonicalSid = new SecurityIdentifier(sid).Value;
            _definition = new(name, canonicalSid, [canonicalSid], administratorProbe ?? (() => { }));
        }
        internal OperationLease EnterWindows() => Enter(_definition, update: false);
        internal OperationLease EnterUpdate() => Enter(_definition, update: true);
        internal OperationLease OpenHandoff() => OperationGate.OpenHandoff(_definition);
    }

    private static class Native
    {
        private const uint SemaphoreAllAccess = 0x001F0003;
        private const uint GenericAll = 0x10000000;

        internal static SafeWaitHandle Create(Definition definition)
        {
            if (!ConvertStringSecurityDescriptorToSecurityDescriptorW(definition.Sddl, 1, out var security, out _))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            try
            {
                var attributes = new SecurityAttributes { Length = Marshal.SizeOf<SecurityAttributes>(), Descriptor = security };
                return Validate(CreateSemaphoreW(ref attributes, 1, 1, definition.Name), definition);
            }
            finally { _ = LocalFree(security); }
        }

        internal static SafeWaitHandle OpenExisting(Definition definition) =>
            Validate(OpenSemaphoreW(0x00120002, false, definition.Name), definition);

        private static SafeWaitHandle Validate(SafeWaitHandle handle, Definition definition)
        {
            if (handle.IsInvalid)
            {
                var error = Marshal.GetLastWin32Error();
                handle.Dispose();
                throw new Win32Exception(error, "The protected application operation gate is unavailable.");
            }
            try { ValidateSecurity(handle, definition); return handle; }
            catch { handle.Dispose(); throw; }
        }

        private static void ValidateSecurity(SafeWaitHandle handle, Definition definition)
        {
            var result = GetSecurityInfo(handle, 6, 1 | 2 | 4, out var owner, out var group,
                out var dacl, out _, out var descriptor);
            if (result != 0) throw new Win32Exception((int)result, "Operation gate security could not be verified.");
            try
            {
                if (owner == IntPtr.Zero || group == IntPtr.Zero || dacl == IntPtr.Zero ||
                    new SecurityIdentifier(owner).Value != definition.OwnerSid ||
                    new SecurityIdentifier(group).Value != definition.OwnerSid ||
                    !GetSecurityDescriptorControl(descriptor, out var control, out _) || (control & 0x1000) == 0)
                    throw new UnauthorizedAccessException("The operation gate has an unexpected owner or unprotected ACL.");
                var acl = Marshal.PtrToStructure<AclHeader>(dacl);
                if (acl.AceCount != definition.AllowedSids.Length)
                    throw new UnauthorizedAccessException("The operation gate has unexpected access entries.");
                var seen = new HashSet<string>(StringComparer.Ordinal);
                for (var index = 0; index < acl.AceCount; index++)
                {
                    if (!GetAce(dacl, index, out var ace)) throw new Win32Exception(Marshal.GetLastWin32Error());
                    var size = unchecked((ushort)Marshal.ReadInt16(ace, 2));
                    var mask = unchecked((uint)Marshal.ReadInt32(ace, 4));
                    var sid = IntPtr.Add(ace, 8);
                    if (Marshal.ReadByte(ace) != 0 || Marshal.ReadByte(ace, 1) != 0 || size < 16 ||
                        (mask != GenericAll && mask != SemaphoreAllAccess) || !IsValidSid(sid) || 8 + GetLengthSid(sid) > size)
                        throw new UnauthorizedAccessException("The operation gate has an unexpected access rule.");
                    var value = new SecurityIdentifier(sid).Value;
                    if (!definition.AllowedSids.Contains(value, StringComparer.Ordinal) || !seen.Add(value))
                        throw new UnauthorizedAccessException("The operation gate permits an unexpected principal.");
                }
            }
            finally { _ = LocalFree(descriptor); }
        }

        internal static void ReleaseChecked(SafeWaitHandle handle)
        {
            if (!ReleaseSemaphore(handle, 1, out _))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "The application operation gate could not be released safely.");
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct SecurityAttributes
        {
            public int Length;
            public IntPtr Descriptor;
            [MarshalAs(UnmanagedType.Bool)] public bool Inherit;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct AclHeader
        {
            public byte Revision;
            public byte Reserved;
            public ushort Size;
            public ushort AceCount;
            public ushort Reserved2;
        }

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true, ExactSpelling = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool ConvertStringSecurityDescriptorToSecurityDescriptorW(string text, uint revision,
            out IntPtr descriptor, out uint size);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr LocalFree(IntPtr memory);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, ExactSpelling = true)]
        private static extern SafeWaitHandle CreateSemaphoreW(ref SecurityAttributes attributes, int initial, int maximum, string name);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, ExactSpelling = true)]
        private static extern SafeWaitHandle OpenSemaphoreW(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, string name);
        [DllImport("kernel32.dll", SetLastError = true)] internal static extern uint WaitForSingleObject(SafeWaitHandle handle, uint milliseconds);
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)] private static extern bool ReleaseSemaphore(SafeWaitHandle handle, int release, out int previous);
        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern uint GetSecurityInfo(SafeWaitHandle handle, uint type, uint information, out IntPtr owner,
            out IntPtr group, out IntPtr dacl, out IntPtr sacl, out IntPtr descriptor);
        [DllImport("advapi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetSecurityDescriptorControl(IntPtr descriptor, out ushort control, out uint revision);
        [DllImport("advapi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetAce(IntPtr acl, int index, out IntPtr ace);
        [DllImport("advapi32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsValidSid(IntPtr sid);
        [DllImport("advapi32.dll")] private static extern uint GetLengthSid(IntPtr sid);
    }
}

/// <summary>A lease can be disposed after an await; it has no owning-thread rule.</summary>
public sealed class OperationLease : IDisposable
{
    private readonly OperationGate.State _state;
    private readonly OperationGate.Scope _scope;
    private readonly bool _root;
    private int _disposed;

    internal OperationLease(OperationGate.State state, OperationGate.Scope scope, bool root)
    { _state = state; _scope = scope; _root = root; }

    public bool IsHandoff => _state.Handoff;
    public bool IsTransferred => _state.Transferred;
    public bool IsAdopted => _state.Adopted;

    /// <summary>
    /// Call only after the verified helper has opened the same semaphore and
    /// acknowledged READY, and BEFORE writing GO. Parent disposal then closes
    /// its handle without releasing count 0; the helper keeps the object alive.
    /// After GO, the helper adopts release responsibility. A failed GO must not
    /// resume work until the helper has exited/timed out. A semaphore has no
    /// abandoned-owner recovery: count 0 persists until adopted/released or all
    /// handles close. Busy attempts close their handles immediately.
    /// </summary>
    public void TransferToHelper()
    {
        Compat.ThrowIfDisposed(Volatile.Read(ref _disposed) != 0, this);
        _state.Transfer(_root);
    }

    /// <summary>Only the verified helper calls this after receiving authenticated GO.</summary>
    public void AdoptHandoff()
    {
        Compat.ThrowIfDisposed(Volatile.Read(ref _disposed) != 0, this);
        _state.Adopt(_root);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        OperationGate.Leave(_scope);
        _state.ReleaseReference();
    }
}

/// <summary>Another WinTool operation holds the machine-wide gate (CLI exit code 21).</summary>
public sealed class OperationBusyException : InvalidOperationException
{
    public OperationBusyException()
        : base("Another WinTool operation is running. Wait for it to finish, then try again.") { }
}
