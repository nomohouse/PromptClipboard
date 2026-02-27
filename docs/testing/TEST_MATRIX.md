# Test Matrix

Coverage status for all public APIs.

**Legend:** DONE = tested, TODO = needs tests, N/A = not testable in isolation

## Domain Layer

| Class | Method/Property | Status |
|-------|----------------|--------|
| `Prompt` | `GetTags()` | DONE |
| `Prompt` | `SetTags()` | DONE |
| `Prompt` | `HasTemplateVariables()` | DONE |

## Application Layer

| Class | Method | Status |
|-------|--------|--------|
| `PastePromptUseCase` | `ExecuteAsync` — no target window | DONE |
| `PastePromptUseCase` | `ExecuteAsync` — window closed | DONE |
| `PastePromptUseCase` | `ExecuteAsync` — elevated target | DONE |
| `PastePromptUseCase` | `ExecuteAsync` — integrity check fails | DONE |
| `PastePromptUseCase` | `ExecuteAsync` — focus restore fails | DONE |
| `PastePromptUseCase` | `ExecuteAsync` — focus lost before paste | DONE |
| `PastePromptUseCase` | `ExecuteAsync` — SendInput fails | DONE |
| `PastePromptUseCase` | `ExecuteAsync` — success path | DONE |
| `ImportExportUseCase` | `ExportAsync` — empty repo | DONE |
| `ImportExportUseCase` | `ExportAsync` — includes all fields | DONE |
| `ImportExportUseCase` | `ImportAsync` — valid JSON | DONE |
| `ImportExportUseCase` | `ImportAsync` — empty prompts | DONE |
| `ImportExportUseCase` | `ImportAsync` — invalid JSON | DONE |
| `ImportExportUseCase` | Round-trip export/import | DONE |
| `SearchRankingService` | `ParseQuery` | DONE |
| `SearchRankingService` | `SearchAsync` | DONE |
| `SearchRankingService` | `GetDefaultListAsync` | DONE |
| `TemplateEngine` | `ExtractVariables` | DONE |
| `TemplateEngine` | `Resolve` | DONE |

## Infrastructure Layer

| Class | Method | Status |
|-------|--------|--------|
| `SqlitePromptRepository` | CRUD operations | DONE |
| `SqlitePromptRepository` | FTS5 search | DONE |
| `SqlitePromptRepository` | Pinned/Recent queries | DONE |
| `SqlitePromptRepository` | Contract tests (7) | DONE |
| `SettingsService` | `Load` — missing file | DONE |
| `SettingsService` | `Load` — corrupt JSON | DONE |
| `SettingsService` | `Save` — creates directory | DONE |
| `SettingsService` | Round-trip save/load | DONE |
| `MigrationRunner` | `RunAll` — idempotent | DONE |
| `MigrationRunner` | `RunAll` — creates tables | DONE |
| `SqliteConnectionFactory` | Disk-based constructor | TODO |
| `SqliteConnectionFactory` | In-memory constructor | DONE (via other tests) |

## App Layer (ViewModels)

| Class | Method/Scenario | Status |
|-------|----------------|--------|
| `PaletteViewModel` | Search debounce | DONE |
| `PaletteViewModel` | MoveUp/MoveDown | DONE |
| `PaletteViewModel` | Error handling | DONE |
| `PaletteViewModel` | Expand/collapse | DONE |
| `PaletteViewModel` | Snapshot test | DONE |
| `PromptItemViewModel` | Preview text | DONE |
| `PromptItemViewModel` | Meta label | DONE |
| `PromptItemViewModel` | Toggle expanded | DONE |
| `EditorViewModel` | LoadForCreate | DONE |
| `EditorViewModel` | LoadForEdit | DONE |
| `EditorViewModel` | SaveAsync — validation | DONE |
| `EditorViewModel` | SaveAsync — create | DONE |
| `EditorViewModel` | SaveAsync — edit | DONE |
| `EditorViewModel` | DeleteAsync | DONE |
| `EditorViewModel` | Cancel | DONE |
| `EditorViewModel` | Tags parsing | DONE |
| `TemplateDialogViewModel` | LoadVariables | DONE |
| `TemplateDialogViewModel` | GetValues | DONE |
| `TemplateDialogViewModel` | Confirm/Cancel | DONE |
| `TemplateDialogViewModel` | Clear on reload | DONE |
| `BodyPreviewHelper` | IsLongBody | DONE |
| `BodyPreviewHelper` | GetCollapsedPreview | DONE |
| `BodyPreviewHelper` | GetExpandedPreview | DONE |
| `BodyPreviewHelper` | GetMetaLabel | DONE |

## Contract Tests (IPromptRepository)

| Test | FakePromptRepository | SqlitePromptRepository |
|------|---------------------|----------------------|
| `Create_AssignsPositiveId` | DONE | DONE |
| `GetById_AfterCreate_ReturnsPrompt` | DONE | DONE |
| `GetById_NonExistent_ReturnsNull` | DONE | DONE |
| `Delete_RemovesPrompt` | DONE | DONE |
| `GetCount_ReflectsCreateAndDelete` | DONE | DONE |
| `GetPinned_ReturnsPinnedOnly` | DONE | DONE |
| `GetAll_ReturnsAll` | DONE | DONE |

## E2E Tests

| Test | Status | Runner |
|------|--------|--------|
| `App_Launches_WithoutCrashing` | DONE (scaffold) | self-hosted |
| `App_TrayIcon_Exists` | DONE (scaffold) | self-hosted |

## Visual Regression Tests

| Test | Status | Runner |
|------|--------|--------|
| `PaletteCard_DefaultState` | DONE (scaffold) | self-hosted |
| `PaletteCard_ExpandedState` | DONE (scaffold) | self-hosted |
| `PaletteCard_PinnedWithTags` | DONE (scaffold) | self-hosted |
