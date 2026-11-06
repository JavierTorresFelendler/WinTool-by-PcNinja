namespace WinTool.App.ViewModels
{
    /// <summary>
    /// A page that can run a long operation. Closing WinTool asks these pages to cancel
    /// and waits until none is busy (Codex review F1): an installer that cannot be
    /// interrupted is never killed, and WinTool keeps its operation lock until it ends.
    /// </summary>
    public interface IOperationPage
    {
        bool IsBusy { get; }

        /// <summary>Cancels what can be cancelled; an uninterruptible step just continues.</summary>
        void RequestCancel();
    }
}
