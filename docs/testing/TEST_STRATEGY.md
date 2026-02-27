# Test Strategy

## Test Pyramid

| Layer | Type | Target Coverage | CI Gate |
|-------|------|----------------|---------|
| Domain | Unit | 95% | Global ≥25% |
| Application | Unit + Integration | 90% | Global ≥25% |
| Infrastructure | Integration | 85% | Global ≥25% |
| App (ViewModels) | Unit | 80% | Global ≥25% |
| Snapshot (text) | Verify library | N/A | PR |
| Snapshot (visual) | PNG diff | N/A | Nightly |
| E2E | Desktop UI (FlaUI) | N/A | Nightly (self-hosted) |
| Packaging | Install + launch smoke | N/A | Release (self-hosted) |

## Coverage Thresholds

**CI Gate (global):** single threshold for entire solution.
Coverage is collected only from projects with SRC references (Domain, Application, Infrastructure, App).
E2E.Tests and TestContracts are excluded (no coverlet.msbuild, and empty reports are filtered by `lines-valid="0"`).

| Date | Global Gate | Action |
|------|------------|--------|
| Start | 25% | Initial (actual ~29%, 4% buffer) |
| +1 month | 35% | After test backfill |
| +2 months | 50% | Continue filling gaps |
| +3 months | 65% | Introduce per-layer gates |

**Per-layer targets** are documented goals, NOT CI blockers until global reaches 65%.

## Naming Conventions

- Test class: `{ClassUnderTest}Tests` (e.g., `PastePromptUseCaseTests`)
- Test method: `{Method}_{Scenario}_{Expected}` (e.g., `Execute_NoTargetWindow_AbortsPaste`)
- Fakes: `Fake{Interface}` in `Fakes/` directory (e.g., `FakeClipboardService`)

## Trait Categories

| Trait | Runs On | Filter |
|-------|---------|--------|
| (none) | PR, Nightly, Release | Default: unit/integration |
| `[Trait("Category", "Snapshot")]` | PR, Nightly | Text Verify snapshots |
| `[Trait("Category", "Visual")]` | Nightly (self-hosted) | PNG diff regression |
| `[Trait("Category", "E2E")]` | Nightly (self-hosted) | FlaUI desktop tests |
| `[Trait("Category", "Smoke")]` | Release (self-hosted) | Packaging install/launch |

## Anti-Flake Policy

- **No retry mechanism** — flake = bug, fix root cause
- `debounceMs: 0` for ViewModel tests
- `FocusSettleDelayMs=0`, `PrePasteDelayMs=0`, `PostPasteDelayMs=0` for PastePromptUseCase tests
- Unique Guid-based names for in-memory SQLite databases
- Guid-based temp directories for file I/O tests
- E2E tests run sequentially (`parallelizeTestCollections: false`)
- Visual tests use `[Collection("WpfVisual")]` for serial execution

## How to Run Tests Locally

### All unit/integration tests (no interactive desktop needed):
```bash
dotnet test --filter "Category!=E2E&Category!=Visual&Category!=Smoke"
```

### By layer:
```bash
dotnet test tests/PromptClipboard.Domain.Tests
dotnet test tests/PromptClipboard.Application.Tests
dotnet test tests/PromptClipboard.Infrastructure.Tests
dotnet test tests/PromptClipboard.App.Tests
```

### By category:
```bash
dotnet test --filter "Category=Snapshot"
dotnet test --filter "Category=Visual"   # requires interactive desktop
dotnet test --filter "Category=E2E"      # requires interactive desktop
```

### With coverage:
```bash
dotnet test --filter "Category!=E2E&Category!=Visual&Category!=Smoke" \
  /p:CollectCoverage=true \
  /p:CoverletOutputFormat=cobertura \
  /p:ExcludeByAttribute="GeneratedCodeAttribute" \
  /p:ExcludeByFile="**/obj/**/*.cs"
```

## How to Update Snapshot Baselines

### Text snapshots (Verify):
1. Run tests — new/changed snapshots create `.received.txt` files
2. Review diff between `.received.txt` and `.verified.txt`
3. Accept: copy `.received.txt` → `.verified.txt`
4. Commit updated `.verified.txt` via PR

### Visual snapshots (PNG):
1. Run visual tests on self-hosted runner
2. Review `.received.png` vs `.verified.png`
3. Accept: copy `.received.png` → `.verified.png`
4. Commit updated `.verified.png` via PR

## Monthly Review Checklist

- [ ] Review flaky test list (should be empty)
- [ ] Evaluate coverage thresholds (raise if goals met)
- [ ] Update TEST_MATRIX.md with new/removed public APIs
- [ ] Check E2E/Visual test baseline freshness
