namespace PromptClipboard.Infrastructure.Persistence;

public sealed class BackupFailedException : Exception
{
    public BackupFailedException(Exception inner)
        : base("Pre-migration backup failed", inner) { }
}
