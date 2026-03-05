namespace PromptClipboard.App.Interfaces;

public interface IStartupErrorHandler
{
    void HandleFatalError(string title, string message, Exception ex);
}
