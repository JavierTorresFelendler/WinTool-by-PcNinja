using System;
using System.IO;
using Xunit.Abstractions;
using Xunit.Sdk;

namespace WinTool.Tests
{
    // Unit tests inject their log directly; stable builds must never depend on a test environment override.
    public sealed class IsolatedTestFramework : XunitTestFramework
    {
        public IsolatedTestFramework(IMessageSink sink) : base(sink)
        {
            WinTool.Core.Diagnostics.AppLog.Current = new WinTool.Core.Diagnostics.AppLog(
                Path.Combine(Path.GetTempPath(), "WinToolTests", "Logs", Guid.NewGuid().ToString("N")));
        }
    }
}
