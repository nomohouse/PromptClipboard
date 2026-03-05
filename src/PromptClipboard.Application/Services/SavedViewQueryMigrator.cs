namespace PromptClipboard.Application.Services;

/// <summary>
/// Migrates saved_views query_json between schema versions.
/// Returns null if migration is impossible (unknown future version).
/// </summary>
public static class SavedViewQueryMigrator
{
    public const int CurrentVersion = 1;

    /// <summary>
    /// Migrates query_json from fromVersion to CurrentVersion.
    /// Returns (migratedJson, newVersion) or null if migration is impossible.
    /// </summary>
    public static (string Json, int Version)? Migrate(string queryJson, int fromVersion)
    {
        if (fromVersion == CurrentVersion) return (queryJson, fromVersion);
        if (fromVersion > CurrentVersion) return null; // unknown future version
        // fromVersion < CurrentVersion → step-by-step migration:
        // No migrations defined yet (v1 is first and current)
        return null;
    }
}
