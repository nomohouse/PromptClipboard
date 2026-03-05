namespace PromptClipboard.App.Handlers;

using System.Windows;
using PromptClipboard.App.Interfaces;
using Serilog;

public sealed class StartupErrorHandler : IStartupErrorHandler
{
    public void HandleFatalError(string title, string message, Exception ex)
    {
        Log.CloseAndFlush();
        MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Error);
        if (System.Windows.Application.Current != null)
            System.Windows.Application.Current.Shutdown(-1);
        else
            Environment.Exit(1);
    }
}
