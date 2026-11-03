# Windows Port Implementation Plan

> **For agentic workers:** Use the installed execution and verification skills. The user waived design review and authorized direct implementation. Independent components have explicit interfaces below and separate ownership.

**Goal:** Deliver a buildable native Windows notice sender on `codex/windows-port`.
**Architecture:** Native WPF shell over a portable BCL-only domain library and a Windows automation adapter. Keep all macOS sources intact.
**Tech Stack:** .NET 10, WPF, System.Text.Json, ZIP/XML XLSX, UIAutomationClient, targeted Win32.
**Spec:** `docs/superpowers/specs/2026-11-03-windows-port-design.md`.

## Global constraints
- Windows 10/11 x64. The user subsequently authorized synthetic live tests only in verified chatbot rooms (카카오톡 선물하기, 지모, KREAM); dry runs must never enter text or send files.
- Preserve schema 8 camelCase JSON, Korean raw values, UUIDs, ISO dates, and unknown fields.
- No main changes, forced pushes, coordinate automation or current-focus fallback.
- SDK: `C:/Users/seyon/OneDrive/바탕 화면/research-tools/dotnet/dotnet.exe`.

## Shared interfaces
All public domain classes use namespace `NoticeSender.Core`, mutable public properties, nullable enabled and camelCase JSON options.
- `Student`: `Guid Id`; `string Name,Nickname,School,ChatRoomName`; `int AdmissionYear`; `string? ChatId`; `bool IsActive`.
- `ClassMember`: `Guid StudentId`; `string? NicknameOverride`.
- `ClassGroup`: `Guid Id`; `string Name,School`; `int AdmissionYear`; `List<ClassMember> Members`; `Guid? DefaultPresetId`.
- `MessagePreset`: `Guid Id`; `string Kind,Name,TeacherName,AcademyName,PresentTemplate,VideoTemplate,AbsentTemplate`; `int Version`; `double HighThreshold,MiddleThreshold`; `int? MockExamCount`; `string? FooterTemplate`; computed `string Category` (`직접입력`, `일반 수업`, `모의고사`).
- `ExamInput`: `string Title,Comment`; `double? Score,Maximum,Average,Highest`; `int? Rank,Attendees`.
- `LessonInput`: `Guid StudentId`; `string StudentName,Nickname,Date,Attendance,Attitude,HomeworkComment,TestComment,Progress,Assignment,ExamUnit,Notice`; `double? HomeworkScore,HomeworkMaximum,TestScore,TestMaximum`; `List<ExamInput> Exams`.
- `BatchItem`: `Guid Id,StudentId`; `string StudentName,Nickname,ChatRoomName,Message,Status`; `string? ChatId`; `List<string> CommonMessages,AttachmentPaths`; computed `IReadOnlyList<string> AllMessages`. Status values: `준비`, `전송 중`, `드라이런 확인 완료`, `전송 요청 완료`, `실패`, `확인 필요`, `제외`.
- `SendLog`: `Guid Id,BatchId,StudentId`; `string StudentName,ChatRoomName,Result,MessageSHA256`; `DateTimeOffset SentAt`; `List<string>? Messages`; `string? Detail`.
- `AppDatabase`: `int SchemaVersion`; `List<Student> Students`; `List<ClassGroup> Classes`; `List<MessagePreset> Presets`; `List<SendLog> Logs`; `string? AttachmentRootPath`; preserve additional original fields with extension data.
- `DatabaseStore(string? path=null)`: `string Path`; `AppDatabase Load()`; `void Save(AppDatabase)`; `AppDatabase ImportBackup(string path)`; `void ExportBackup(string path,AppDatabase)`.
- `StudentFiles.Read(string path): List<Student>`; `WriteCsv(string path,IEnumerable<Student>)`; `Merge(AppDatabase,IEnumerable<Student>): int`.
- `ArchiveService.ExportClasses(string path,AppDatabase)`; `ImportClasses(string path,AppDatabase): string`; `ExportPreset(string path,MessagePreset)`; `ImportPreset(string path): MessagePreset`.
- `TemplateEngine.Render(MessagePreset,LessonInput): string`; `Validate(MessagePreset): List<string>`; `DefaultPresets.Create(): List<MessagePreset>`; `SheetParser.ParseDelimited(string text,char delimiter='\t'): List<List<string>>`.
- `AttachmentScanner.Scan(string root,IEnumerable<Student>): Dictionary<Guid,List<string>>`.
- `BatchValidator.Validate(AppDatabase,IReadOnlyList<BatchItem>,bool dryRun): List<string>`.

Automation namespace `NoticeSender.Automation`:
- `record ChatTarget(int ProcessId,nint WindowHandle,string RoomName,nint ComposerHandle)`.
- `IChatClient`: `Task<ChatTarget> ResolveAsync(string exactRoomName,CancellationToken)`; `Task SendTextAsync(ChatTarget,string,CancellationToken)`; `Task SendFilesAsync(ChatTarget,IReadOnlyList<string>,CancellationToken)`; `Task CompleteAsync(ChatTarget,CancellationToken)`.
- `WindowsKakaoClient : IChatClient` and `Task<string> DiagnoseAsync(CancellationToken)`.
- `SendCoordinator(IChatClient)`: `Task RunAsync(AppDatabase,IReadOnlyList<BatchItem>,bool dryRun,Action<BatchItem> progress,Action persist,CancellationToken)`.

## Review focus
- Ambiguous room titles, stale HWNDs, a disappearing composer: fail closed.
- Cancellation after a partial send: uncertain status, persisted log, never resend automatically.
- Imported identifiers and accented/Korean text: preserve exact identity and quoted newlines.
- Restoring invalid data: never replace an existing valid database with a blank default silently.
- Editing after preview: invalidate approval; take an immutable reviewed snapshot at send time.

## Tasks
- [x] Core: Write failing contract tests, implement models/persistence/import/export/templates/attachment validation, run console tests.
- [x] Automation: Write fake-transport tests proving dry-run and partial-delivery behavior; implement safe Windows adapter/coordinator; run tests.
- [x] WPF: Add editable tabs for diagnostics, students, classes, lesson/send, presets and logs; connect domain/automation interfaces, preview approval and isolated demo mode.
- [x] Integration: Build all projects, fix integration errors, verify UI launch/demo flow without messaging, add publish scripts/CI/Windows guide.
- [x] Release verification: Independent code review, 23 core + 25 automation tests, WPF smoke, self-contained Windows ZIP and extracted-package smoke (exit 0), metadata version 3.1.0/build 73. Publish only the feature branch; retain this checkout.
- [ ] Live delivery validation: real individual/common text, single/multiple/attachment-only files and partial-upload cancellation in verified chatbot rooms. Dry-run passed; actual entry blocked by the cue/draft guard, then physical Escape stopped Computer Use. See `Windows/TESTING.md`.
