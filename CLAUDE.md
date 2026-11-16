# FrameForge AI — Claude Code Instructions

## Product
FrameForge AI is a Windows-first, local-first photography application focused on photo culling and, later, editing. Build an original product; do not copy another product's branding, UI assets, or proprietary implementation.

## Engineering principles
- Work milestone-by-milestone. Do not implement future milestones without explicit approval.
- Inspect the repository and existing conventions before changing files.
- Plan before substantial implementation; explain assumptions and risks.
- Keep changes focused and avoid unrelated refactors.
- Do not claim a build or test passed unless you actually ran it and saw the result.
- If a tool, package, model, or platform capability is uncertain, verify it or state the uncertainty.
- Prefer stable, supported dependencies. Check package compatibility with the selected .NET version.
- Do not add dependencies without explaining their purpose and trade-offs.
- Avoid unnecessary abstraction and speculative architecture.

## Technology baseline
- Windows desktop, WPF
- C# and .NET 10 LTS (verify SDK availability; if unavailable, discuss using the latest installed supported LTS)
- MVVM with CommunityToolkit.Mvvm
- SQLite with EF Core
- Serilog
- xUnit for tests
- ONNX Runtime for local inference when AI work begins
- OpenCV and LibRaw are candidates, not mandatory before their milestone

## Architecture
- Keep domain rules independent of WPF.
- UI communicates through ViewModels and application use cases.
- ViewModels must not access DbContext directly.
- Keep filesystem, database, imaging, and AI operations behind interfaces.
- Keep AI model implementations replaceable.
- Do not create a giant service or giant ViewModel.
- Avoid service locator patterns.
- Use dependency injection at the composition root.
- Use asynchronous APIs for I/O and long-running work.
- Use cancellation tokens for cancellable operations.
- Keep original image files read-only; never overwrite originals.

## UI and performance
- Never block the UI thread.
- Use virtualised collections for large image libraries.
- Do not load full-resolution images for every grid tile.
- Use thumbnails and bounded caches.
- Report progress for long operations.
- Support cancellation and graceful error reporting.
- Keyboard navigation is a first-class workflow.
- Follow `docs/ui-design-system.md`.

## Data and reliability
- Use versioned database migrations.
- Store paths and metadata; do not copy source images into the app database.
- Design import to be resumable/idempotent.
- Handle missing files, inaccessible folders, corrupt images, and duplicate imports.
- Do not silently swallow exceptions.
- Log useful diagnostic context without logging private image content.

## Testing
- Add unit tests for domain rules and application logic.
- Add integration tests for persistence and filesystem behavior where practical.
- Keep tests deterministic; avoid depending on a user's photo library.
- Use small generated or licensed fixtures.
- Build and test after each meaningful change.
- Report exact commands and results.

## Completion checklist
A milestone is complete only when:
1. Acceptance criteria are met.
2. The solution builds.
3. Relevant tests pass.
4. Documentation is updated.
5. Known limitations are listed.
6. No unrelated work was introduced.

## Interaction protocol
At the beginning of each milestone:
1. Read the milestone brief and relevant docs.
2. Inspect current code.
3. Propose a short plan and file list.
4. Identify risks and questions.
5. Wait for approval before implementation when the user asks for plan-first.

At the end:
- Summarise files changed.
- Show build/test commands and actual outcomes.
- Explain how to run or verify the feature.
- List limitations and recommend the next milestone, but do not start it.
