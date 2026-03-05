namespace PromptClipboard.App.Handlers;

using System.Security.Cryptography;
using System.Text;
using PromptClipboard.Domain.Entities;
using PromptClipboard.Domain.Interfaces;
using Serilog;

internal static class PromptSeeder
{
    private const int LatestSeedVersion = 1;

    public static async Task SeedIfNeededAsync(
        IPromptRepository repo, ISettingsService settingsService, ILogger? log, CancellationToken ct = default)
    {
        var settings = settingsService.Load();
        settings.SeedAppliedKeys ??= [];

        if (settings.SeedVersion >= LatestSeedVersion) return;

        var existingCount = await repo.GetCountAsync(ct);

        // Bootstrap marker: set before first insert on empty DB
        if (settings.SeedVersion == 0 && existingCount == 0 && !settings.SeedBootstrapStarted)
        {
            settings.SeedBootstrapStarted = true;
            settingsService.Save(settings);
        }

        // Legacy DB: user data exists, no seed checkpoints, bootstrap never started
        if (settings.SeedVersion == 0 && existingCount > 0 && settings.SeedAppliedKeys.Count == 0 && !settings.SeedBootstrapStarted)
        {
            settings.SeedVersion = LatestSeedVersion;
            settingsService.Save(settings);
            return;
        }

        if (settings.SeedVersion == 0)
        {
            await SeedV1Async(repo, settingsService, settings, log, ct);
        }

        var allV1KeysApplied = SeedCatalog.V1StableItems
            .Select(x => x.SeedKey)
            .All(k => settings.SeedAppliedKeys.Contains(k, StringComparer.OrdinalIgnoreCase));

        if (allV1KeysApplied)
        {
            settings.SeedVersion = LatestSeedVersion;
            settings.SeedBootstrapStarted = false;
            settingsService.Save(settings);
        }
    }

    // Backward-compatible entry point for existing callers
    public static Task SeedIfEmptyAsync(IPromptRepository repo, ILogger? log) =>
        SeedIfEmptyAsync(repo, log, CancellationToken.None);

    public static async Task SeedIfEmptyAsync(IPromptRepository repo, ILogger? log, CancellationToken ct)
    {
        var count = await repo.GetCountAsync(ct);
        if (count > 0) return;

        log?.Information("Seeding initial prompts (legacy path)...");

        foreach (var (_, title, body, tagsJson, lang) in SeedCatalog.V1StableItems)
        {
            var p = new Prompt
            {
                Title = title,
                Body = body,
                TagsJson = tagsJson,
                TagsText = tagsJson,
                Lang = lang,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            await repo.CreateAsync(p, ct);
        }

        log?.Information("Seeded {Count} prompts", SeedCatalog.V1StableItems.Count);
    }

    private static async Task SeedV1Async(
        IPromptRepository repo,
        ISettingsService settingsService,
        AppSettings settings,
        ILogger? log,
        CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var existing = await repo.GetAllAsync(ct);
        var existingSeedSignatures = new HashSet<string>(
            existing.Select(ComputeSeedSignature),
            StringComparer.Ordinal);
        var seedAppliedKeys = settings.SeedAppliedKeys ?? [];
        var applied = new HashSet<string>(seedAppliedKeys, StringComparer.OrdinalIgnoreCase);

        foreach (var (seedKey, title, body, tags, lang) in SeedCatalog.V1StableItems)
        {
            if (applied.Contains(seedKey)) continue;

            var itemSignature = ComputeSeedSignature(title, body, tags, lang);
            if (existingSeedSignatures.Contains(itemSignature))
            {
                (settings.SeedAppliedKeys ??= []).Add(seedKey);
                settingsService.Save(settings);
                applied.Add(seedKey);
                log?.Debug("Seed checkpoint backfilled from existing row: {SeedKey}", seedKey);
                continue;
            }

            await repo.CreateAsync(new Prompt
            {
                Title = title,
                Body = body,
                TagsJson = tags,
                TagsText = tags,
                Lang = lang,
                CreatedAt = now,
                UpdatedAt = now
            }, ct);

            (settings.SeedAppliedKeys ??= []).Add(seedKey);
            settingsService.Save(settings);
            applied.Add(seedKey);
            existingSeedSignatures.Add(itemSignature);
            log?.Debug("Seed key applied: {SeedKey}", seedKey);
        }
    }

    private static string ComputeSeedSignature(Prompt p)
        => ComputeSeedSignature(p.Title, p.Body, p.TagsJson, p.Lang);

    private static string ComputeSeedSignature(string title, string body, string tagsJson, string lang)
    {
        var normalized = string.Join(
            "\u001F",
            NormalizeSeedField(title),
            NormalizeSeedField(body),
            NormalizeSeedField(lang),
            NormalizeSeedField(tagsJson));
        return Sha256Hex(normalized);
    }

    private static string NormalizeSeedField(string value)
        => (value ?? string.Empty).Trim().ToLowerInvariant();

    private static string Sha256Hex(string input)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
