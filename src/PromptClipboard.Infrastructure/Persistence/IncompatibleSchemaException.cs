namespace PromptClipboard.Infrastructure.Persistence;

public sealed class IncompatibleSchemaException : Exception
{
    public IncompatibleSchemaException(string message)
        : base(message) { }

    public IncompatibleSchemaException(string message, Exception inner)
        : base(message, inner) { }
}
