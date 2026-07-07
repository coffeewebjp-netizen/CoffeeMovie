# Refactor Plan

CoffeeMovie has reached the point where feature work is faster than the current file structure. This plan keeps behavior stable while reducing risk for upcoming work such as mobile learning-state backup, richer library management, and more Reader controls.

## Principles

- Prefer behavior-preserving refactors before moving logic across service boundaries.
- Keep each step buildable and easy to review.
- Do not change data formats, Google Drive package formats, or Android signing behavior as part of structural cleanup.
- Move UI code only when the target boundary is obvious.
- Extract services only after a stable partial split makes the responsibilities visible.

## Current Hotspots

| Area | Current issue | First target |
| --- | --- | --- |
| Studio `MainWindow.xaml.cs` | The original all-in-one file has been split into focused partial files and service boundaries. Some WPF coordination and shared UI helpers intentionally remain in the main partial. | Keep stable; only extract further when a feature needs tests around a specific helper. |
| Reader `MoviePlayerPage.cs` | Player HTML, subtitles, fullscreen controls, and shadowing were split into partials/services. The remaining root page is mostly composition/state wiring. | Keep stable while adding features; avoid another broad shuffle until behavior tests exist. |
| Reader `MovieShelfPage.cs` | Drive sync workflow and tree rows were split into partials. The remaining root page is shelf composition and cache-state display. | Keep stable; future work should target mobile learning-state backup rather than more structural splitting. |
| Reader `GoogleDriveSyncService.cs` | The original OAuth/list/download service is now a small facade over auth, package listing, and download services. | Keep the public facade stable for the shelf UI. |

## Phase 1: Safe Partial Split

Status: completed for Studio on 2026-06-16. `MainWindow.xaml.cs` was reduced to about 810 lines, with behavior kept in partial files.

Goal: reduce file size without changing behavior.

1. Done: Move Studio nested display/data helper types into `MainWindow.Types.cs`.
2. Done: Move Studio preview playback/seek code into `MainWindow.Preview.cs`.
3. Done: Move Studio subtitle generation and AI note generation handlers into `MainWindow.SubtitleGeneration.cs`.
4. Done: Move Studio tag picker/filter helpers into `MainWindow.Tags.cs`.
5. Done: Move Studio Drive package export helpers into `MainWindow.DriveExport.cs`.
6. Done: Move Studio thumbnail capture helpers into `MainWindow.Thumbnails.cs`.
7. Done: Move Studio constants, default commands, and default AI prompts into `MainWindow.Constants.cs`.
8. Done: Move Studio AI note generation, import, focus relocation, and quality validation into `MainWindow.AiNotes.cs`.
9. Done: Move Studio shelf grouping, tag selector installation, video/subtitle import buttons, subtitle removal, and refresh handlers into `MainWindow.Shelf.cs`.
10. Done: Move Studio drag/drop import, title/metadata persistence, movie selection, and subtitle selection handlers into `MainWindow.Selection.cs`.
11. Done: Move Studio preview display toggles, playback buttons, scene edit handlers, timing controls, and full-preview handlers into `MainWindow.PreviewHandlers.cs`.
12. Done: Move Studio subtitle row rendering, row learning-state persistence, and cue timing edit logic into `MainWindow.SceneRows.cs`.

Verification after each slice:

```powershell
dotnet build src\CoffeeMovie.Studio\CoffeeMovie.Studio.csproj -c Debug
```

## Phase 2: Reader Partial Split

Status: completed for the first Reader partial split on 2026-06-16. Remaining work should focus on service extraction, not more UI file shuffling unless a file grows again.

Goal: make mobile player/shelf changes easier without changing app behavior.

1. Done: Move player HTML generation and JavaScript string helpers into `MoviePlayerPage.Html.cs`.
2. Done: Move subtitle/memo overlay state, switch handling, cue binding, and subtitle position handling into `MoviePlayerPage.Subtitles.cs`.
3. Done: Move shadowing recognition, scoring, and metrics into `MoviePlayerPage.Shadowing.cs`.
4. Done: Move fullscreen and transport controls into `MoviePlayerPage.Controls.cs`.
5. Done: Move shelf tree row building into `MovieShelfPage.Tree.cs`.
6. Done: Move Drive sync UI workflow into `MovieShelfPage.Sync.cs`.
7. Done: Move Reader library Drive sidecar/package import, package extraction, merge, and thumbnail payload handling into `ReaderLibraryService.Package.cs`.

Verification:

```powershell
dotnet build src\CoffeeMovie.Reader\CoffeeMovie.Reader.csproj -c Debug -f net10.0-android
dotnet build src\CoffeeMovie.Reader\CoffeeMovie.Reader.csproj -c Release -f net10.0-android
```

## Phase 3: Service Extraction

Status: completed for the planned structural pass on 2026-06-16. Reader HTML generation, shadowing scoring, Drive auth/package listing/download, Drive sync partial boundaries, Reader package import boundaries, Studio subtitle generation jobs, Studio AI note import logic, tag filtering, thumbnail capture, subtitle generation process helpers, and shared movie metadata inference were extracted.

Goal: pull testable logic out of UI partials after responsibilities are visible.

Result: the planned extraction pass is complete. Future refactors should be feature-driven and smaller in scope.

Candidate services:

- Done: `LearningNotesImportService`
- Done: `LearningNotesJsonParser`
- Done: `LearningNotesQualityValidator`
- Done: `LearningNotesImportPlanner`
- Done: `LearningNotesTextService`
- Done: `SubtitleGenerationJobService`
- Done: `SubtitleGenerationOptions`
- Done: `SubtitleGenerationProcessService`
- Done: `SubtitleGenerationPathService`
- Done: `SubtitleGenerationCommandLineService`
- Done: `SubtitleGenerationExternalCommandFactory`
- Done: `SubtitleGenerationExternalProcessRunner`
- Done: `WhisperXSubtitleRunner`
- Done: `EnglishSubtitleReviewService`
- Done: `SubtitleAiCommandService`
- Done: `MovieMetadataInferenceService`
- Done: `ThumbnailCaptureService`
- Done: `TagFilterService`
- Done: `ReaderShadowingScorer`
- Done: `ReaderPlayerHtmlBuilder`
- Done: `GoogleDrivePackageListingService`
- Done: `DrivePackageDownloadService`
- Done: `GoogleDriveAuthService`
- Done: `GoogleDriveSyncService.Auth.cs` partial boundary for Google OAuth, token refresh, and folder ID parsing.
- Done: `GoogleDriveSyncService.Download.cs` partial boundary for package/sidecar download, resume, retry, and cache path handling.
- Done: `MainWindow.AiNotes.cs` partial boundary for sparse AI note generation, note import, focus relocation, and quality validation.
- Done: `MovieMetadataInferenceService` for shared filename metadata inference and `Sxx Exx` display formatting across Studio and Reader.
- Done: `ReaderLibraryService.Package.cs` partial boundary for Drive sidecar/package import, zip extraction, package/local state merge, and thumbnail payload handling.
- Done: `LearningNotesImportService` as the stable facade used by `MainWindow.AiNotes.cs`.
- Done: `LearningNotesJsonParser` for accepting AI note JSON arrays, `notes`, or `items` wrappers.
- Done: `LearningNotesQualityValidator` for sparse-note quality validation, index checks, focus checks, and low-quality placeholder detection.
- Done: `LearningNotesImportPlanner` for focus relocation planning, unresolved focus detection, and duplicate note merge.
- Done: `LearningNotesTextService` for CEFR note normalization and diagnostic sample formatting.
- Done: `TagFilterService` for shared tag parsing, movie shelf text/tag/subtitle-tag matching, and flag-tag matching.
- Done: `ThumbnailCaptureService` for ffmpeg path resolution, thumbnail capture process execution, timeout handling, and thumbnail output path creation.
- Done: `SubtitleGenerationProcessService` as a compatibility facade for older subtitle-generation helper calls.
- Done: `SubtitleGenerationPathService` for relative path formatting, working-directory file copies, backup, and output freshness checks.
- Done: `SubtitleGenerationCommandLineService` for argument-template expansion, command-line splitting, and process-command formatting.
- Done: `SubtitleGenerationExternalCommandFactory` for external process start-info creation and Codex CLI resolution.
- Done: `SubtitleGenerationExternalProcessRunner` for process start, stdout/stderr pumping, exit-code handling, and Studio log forwarding.
- Done: `WhisperXSubtitleRunner` for WhisperX command construction, execution, and generated SRT path detection.
- Done: `EnglishSubtitleReviewService` for experimental three-pass English subtitle comparison and optional AI merge fallback.
- Done: `SubtitleAiCommandService` for AI translation and AI learning-note command construction, prompt file writing, execution, and output validation.
- Done: `SubtitleGenerationJobService` for orchestrating English, Japanese, and AI learning-note generation jobs while delegating runner-specific work.
- Done: `SubtitleGenerationOptions` for keeping generation option/result records out of the job orchestration service.
- Done: `GoogleDrivePackageListingService` for Drive file listing, package/sidecar pairing, and `SyncMovieCandidate` creation while leaving OAuth/token and download behavior in the existing sync service.
- Done: `DrivePackageDownloadService` for package/sidecar download, `.partial` resume files, retry handling, legacy cache migration, and download-state reporting.
- Done: `GoogleDriveAuthService` for Google OAuth configuration, PKCE browser authorization, SecureStorage refresh-token persistence, access-token caching, reconnect detection, and Drive folder ID parsing.

Notes:

- The Drive sync service was split as partial files first instead of immediately moving dependencies into new services. This keeps the Google Drive package format, resume behavior, and OAuth flow unchanged while making the next extraction step reviewable.
- `GoogleDriveSyncService` is now a small facade over auth, listing, and download services; its public API remains stable for the shelf UI.
- `GoogleDriveSyncService.Download.cs` is now a thin compatibility wrapper over `DrivePackageDownloadService`, preserving the existing public methods used by the shelf UI.
- `MainWindow.SubtitleGeneration.cs` still coordinates WPF state, selected movie state, import refresh behavior, and user-facing errors. Subtitle process work now sits behind `SubtitleGenerationJobService`, `WhisperXSubtitleRunner`, `EnglishSubtitleReviewService`, `SubtitleAiCommandService`, and `SubtitleGenerationExternalProcessRunner`.
- Future Drive changes should preserve `restartDownload` behavior, partial `.part` resume files, and sidecar/package timestamp comparison semantics.

## Phase 4: Data Safety Features

Goal: reduce risk from reinstall/device loss.

Status: started on 2026-06-16. Reader now has a local JSON learning-state backup format and shelf UI entrypoint.

1. Done: Define mobile learning-state backup format in `LearningStateBackup`.
2. Done: Export/import mobile-only notes, tags, playback state, and shadowing metrics through Reader.
3. Pending: Add Drive upload/download flow for mobile learning-state backups.
4. Done: Document local backup recovery steps after reinstall or device migration in `docs/ANDROID_SIGNING.md`.

Notes:

- The backup intentionally excludes video bytes and subtitle cue text. It stores only movie identity, movie tags, playback state, subtitle track identity, cue learning states, free-form notes, AI notes, and listening/shadowing metrics.
- Import matches movies by movie ID first, then content fingerprint, then Drive package URI. This keeps reinstall recovery aligned with PC-exported Drive packages.
- The first implementation exports a shareable JSON file from Android. Drive-native automatic backup should build on the same JSON format.

## Non-Goals For Initial Refactor

- No UI redesign.
- No package format migration unless explicitly required.
- No Android signing changes.
- No Google Drive OAuth behavior changes.
- No large ViewModel rewrite until the partial split and services are stable.
