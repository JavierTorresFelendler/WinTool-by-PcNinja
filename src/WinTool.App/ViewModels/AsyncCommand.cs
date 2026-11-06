using System;
using System.Threading.Tasks;
using System.Windows.Input;
using WinTool.Core.Diagnostics;

namespace WinTool.App.ViewModels
{
    /// <summary>A command that runs async work; disabled while it runs or when canExecute is false.</summary>
    public sealed class AsyncCommand : ICommand
    {
        private readonly Func<Task> execute;
        private readonly Func<bool> canExecute;
        private bool running;

        public AsyncCommand(Func<Task> execute, Func<bool> canExecute = null)
        {
            this.execute = execute;
            this.canExecute = canExecute;
        }

        public event EventHandler CanExecuteChanged
        {
            add { CommandManager.RequerySuggested += value; }
            remove { CommandManager.RequerySuggested -= value; }
        }

        public bool CanExecute(object parameter) => !running && (canExecute == null || canExecute());

        public async void Execute(object parameter)
        {
            running = true;
            CommandManager.InvalidateRequerySuggested();
            try
            {
                await execute();
            }
            catch (Exception ex)
            {
                AppLog.Current.Error("UI", "Command failed.", ex);
                Dialogs.Error(ex.Message);
            }
            finally
            {
                running = false;
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }
}
