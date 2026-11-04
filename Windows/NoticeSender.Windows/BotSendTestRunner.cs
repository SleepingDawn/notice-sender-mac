using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using NoticeSender.Automation;
using NoticeSender.Core;

namespace NoticeSender.Windows;

internal sealed class BotSendTestReport
{
    public string Mode => SelfChat ? "self-chat-send-test" : "bot-send-test";
    public bool SelfChat { get; init; }
    public string Room { get; init; } = "";
    public string? OnlyStep { get; init; }
    public string TestStamp { get; } = DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + "-" + Guid.NewGuid().ToString("N")[..8];
    public DateTimeOffset StartedAt { get; } = DateTimeOffset.Now;
    public DateTimeOffset? FinishedAt { get; set; }
    public bool Completed { get; set; }
    public bool Cancelled { get; set; }
    public string? Failure { get; set; }
    public int RequestedCount => Steps.Count;
    public int SuccessCount => Steps.Count(s => s.Status == "passed");
    public bool Success => Completed && !Cancelled && Failure == null && Preparation.Status == "verified" && RequestedCount == (OnlyStep == null ? 7 : 1) && SuccessCount == RequestedCount;
    public bool FullSuitePassed => OnlyStep == null && Success;
    public int ExitCode => Cancelled ? 2 : Success ? 0 : 1;
    public string Note => "합성 데이터만 사용합니다. 전송 요청 / 앱 내 완료 확인은 서버 수신 확인이 아닙니다. 확인 필요 항목은 자동 재전송하지 않습니다.";
    public string? ArtifactDirectory { get; set; }
    public List<string> SyntheticFiles { get; } = new();
    public BotSendTestPreparation Preparation { get; } = new();
    public List<BotSendTestStep> Steps { get; } = new();
    public List<BotSendTestTrace> Trace { get; } = new();
    public bool EvidenceCaptureEnabled { get; init; }
    public List<BotEvidenceCaptureResult> Evidence { get; } = new();
    public List<SendLog> Logs { get; init; } = new();
}

internal sealed class BotSendTestPreparation
{
    public string Status { get; set; } = "pending";
    public bool? WasAlreadyOpen { get; set; }
    public int? ProcessId { get; set; }
    public long? WindowHandle { get; set; }
    public long? ComposerHandle { get; set; }
    public string? Error { get; set; }
}

internal sealed class BotSendTestStep
{
    public string Id { get; init; } = "";
    public string Title { get; init; } = "";
    public string Status { get; set; } = "pending";
    public string BatchStatus { get; set; } = "준비";
    public List<string> Messages { get; init; } = new();
    public List<string> AttachmentPaths { get; init; } = new();
    public bool ExpectedCancellation { get; set; }
    public int? RetryTransportCalls { get; set; }
    public int? RetryTraceEntries { get; set; }
    public string? Error { get; set; }
    [JsonIgnore]
    public string DisplayStatus => Status switch { "running" => "실행 중…", "passed" => ExpectedCancellation ? "통과 · 중단 / 재시도 차단" : "통과 · 앱 내 완료 확인", "failed" => "실패 · 중단", "cancelled" => "사용자 중지", _ => "대기" };
}

internal sealed record BotSendTestTrace(DateTimeOffset At, string Step, string Operation, string Phase,
    string? SyntheticText = null, IReadOnlyList<string>? SyntheticFiles = null, string? Error = null, string? DiagnosticException = null);

/// <summary>Only a fresh in-memory database and generated synthetic payloads enter this isolated test suite.</summary>
internal sealed class BotSendTestRunner(IChatClient client, IDryRunChatClient opener, IBotEvidenceCapture? evidenceCapture = null)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };

    internal async Task<BotSendTestReport> RunAsync(string room, bool acknowledged, string reportPath, Action<BotSendTestReport>? progress, CancellationToken cancellationToken, string? onlyStep = null, bool acknowledgeSelfChat = false)
    {
        BotSendTestOptions.ValidateDestination(room, acknowledged, acknowledgeSelfChat);
        BotSendTestOptions.ValidateStep(onlyStep);
        var database = new AppDatabase();
        var report = new BotSendTestReport { Room = room, SelfChat = acknowledgeSelfChat, OnlyStep = onlyStep, Logs = database.Logs, EvidenceCaptureEnabled = evidenceCapture != null };
        if (onlyStep == "E-KOREAN") report.Steps.Add(new() { Id = "E-KOREAN", Title = "메시지 + 한글 파일명 TXT 단독" });
        else
            foreach (var (id, title) in new[] { ("A", "개별 + 공통 메시지 5개"), ("B", "메시지 + 단일 TXT"), ("C", "메시지 + TXT / CSV"),
                ("D", "첨부 전용 단일 TXT"), ("E-PDF", "메시지 + PDF 단독"), ("E-PNG", "메시지 + PNG 단독 / 이미지 미리보기"), ("F", "첫 메시지 후 제어 중단 / 재시도 차단") })
                report.Steps.Add(new() { Id = id, Title = title });
        void Save()
        {
            string path = Path.GetFullPath(reportPath);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(report, JsonOptions), Encoding.UTF8);
            File.Move(path + ".tmp", path, overwrite: true);
            progress?.Invoke(report);
        }
        var trace = new TraceClient(client, opener, report, Save, evidenceCapture);
        BotSendTestStep? active = null;
        Save();
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            PrepareSyntheticPayloads(report);
            if (onlyStep != null) report.Steps.RemoveAll(s => s.Id != onlyStep);
            Save();
            report.Preparation.Status = "opening";
            Save();
            var opened = await trace.OpenAsync(room, cancellationToken);
            report.Preparation.WasAlreadyOpen = opened.WasAlreadyOpen;
            var strict = await trace.ResolveAsync(room, cancellationToken);
            report.Preparation.ProcessId = strict.ProcessId;
            report.Preparation.WindowHandle = strict.WindowHandle.ToInt64();
            report.Preparation.ComposerHandle = strict.ComposerHandle.ToInt64();
            report.Preparation.Status = "verified";
            trace.CapturePreparationEvidence();
            Save();
            var coordinator = new SendCoordinator(trace);
            foreach (var step in report.Steps)
            {
                cancellationToken.ThrowIfCancellationRequested();
                active = step;
                trace.Step = step.Id;
                step.Status = "running";
                Save();
                var batch = new BatchItem { Id = Guid.NewGuid(), StudentId = Guid.NewGuid(), StudentName = "합성 봇 테스트",
                    ChatRoomName = room, Message = step.Messages.FirstOrDefault() ?? "", CommonMessages = step.Messages.Skip(1).ToList(), AttachmentPaths = step.AttachmentPaths.ToList(), PreserveMessageWhitespace = true };
                void Progress(BatchItem item) { step.BatchStatus = item.Status; Save(); }
                if (step.Id != "F")
                {
                    await coordinator.RunAsync(database, new[] { batch }, false, Progress, Save, cancellationToken);
                    cancellationToken.ThrowIfCancellationRequested();
                    if (batch.Status != "전송 요청 완료") throw new InvalidOperationException(report.Trace.LastOrDefault(t => t.Phase == "failed")?.Error ?? database.Logs.LastOrDefault()?.Detail ?? "전송 요청을 완료하지 못했습니다: " + batch.Status);
                }
                else
                {
                    using var controlled = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    int textReturnsBefore = trace.TextReturnCount;
                    bool controlledCancellation = false;
                    trace.AfterTextReturned = () => { controlledCancellation = true; controlled.Cancel(); };
                    try { await coordinator.RunAsync(database, new[] { batch }, false, Progress, Save, controlled.Token); }
                    finally { trace.AfterTextReturned = null; }
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!controlledCancellation || trace.TextReturnCount != textReturnsBefore + 1 || batch.Status != "확인 필요"
                        || database.Logs.LastOrDefault(l => l.BatchId == batch.Id)?.Result != "확인 필요")
                        throw new InvalidOperationException("첫 메시지 반환 후 제어 중단 상태를 확인하지 못했습니다.");
                    int beforeRetry = trace.TransportCallCount;
                    int traceBeforeRetry = report.Trace.Count;
                    // A new coordinator and a ready item with the same identity must still be protected by the in-memory durable log.
                    var retry = new BatchItem { Id = batch.Id, StudentId = batch.StudentId, StudentName = batch.StudentName,
                        ChatRoomName = room, Message = batch.Message, CommonMessages = batch.CommonMessages.ToList(), AttachmentPaths = batch.AttachmentPaths.ToList() };
                    await new SendCoordinator(trace).RunAsync(database, new[] { retry }, false, _ => { }, Save, cancellationToken);
                    step.RetryTransportCalls = trace.TransportCallCount - beforeRetry;
                    step.RetryTraceEntries = report.Trace.Count - traceBeforeRetry;
                    if (step.RetryTransportCalls != 0 || step.RetryTraceEntries != 0) throw new InvalidOperationException("확인 필요 항목의 재시도에서 외부 동작이 발생했습니다.");
                    step.ExpectedCancellation = true;
                }
                step.Status = "passed";
                active = null;
                Save();
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            report.Cancelled = true;
            if (active != null) active.Status = "cancelled";
            if (report.Preparation.Status != "verified") report.Preparation.Status = "cancelled";
        }
        catch (Exception ex)
        {
            report.Failure = WindowsTrustFailure.Describe(ex);
            if (active != null) { active.Status = "failed"; active.Error = report.Failure; }
            else { report.Preparation.Status = "failed"; report.Preparation.Error = report.Failure; }
        }
        report.Completed = true;
        report.FinishedAt = DateTimeOffset.Now;
        Save();
        return report;
    }

    private static void PrepareSyntheticPayloads(BotSendTestReport report)
    {
        string stamp = report.TestStamp;
        string folder = Path.Combine(Path.GetTempPath(), "NoticeSender-BotSendTest-" + stamp);
        Directory.CreateDirectory(folder);
        report.ArtifactDirectory = folder;
        string FilePath(string suffix) => Path.Combine(folder, "NoticeSender-SYNTHETIC-" + stamp + "-" + suffix);
        string Text(string step, string detail) => $"[NoticeSender 합성 봇 테스트 {stamp} / {step}] {detail}";
        if (report.OnlyStep == "E-KOREAN")
        {
            string korean = FilePath("E-한글자료.txt");
            File.WriteAllText(korean, "NoticeSender synthetic bot-only attachment test. No student data. " + stamp + " " + Path.GetFileName(korean), Encoding.UTF8);
            report.SyntheticFiles.Add(korean);
            report.Steps.Single().Messages.Add(Text("E-KOREAN", "한글 파일명 TXT 단독 첨부"));
            report.Steps.Single().AttachmentPaths.Add(korean);
            return;
        }
        string single = FilePath("B-single.txt"), multiple = FilePath("C-first.txt"), only = FilePath("D-only.txt"),
            csv = FilePath("C-second.csv"), pdf = FilePath("E-document.pdf"), png = FilePath("E-image.png");
        foreach (string txt in new[] { single, multiple, only })
            File.WriteAllText(txt, "NoticeSender synthetic bot-only attachment test. No student data. " + stamp + " " + Path.GetFileName(txt), Encoding.UTF8);
        File.WriteAllText(csv, "test,value\nsynthetic,1\n", Encoding.UTF8);
        File.WriteAllBytes(pdf, CreateSyntheticPdf(stamp));
        CreateSyntheticPng(png, stamp);
        report.SyntheticFiles.AddRange(new[] { single, multiple, csv, only, pdf, png });
        report.Steps[0].Messages.Add(Text("A", "개별 메시지"));
        report.Steps[0].Messages.AddRange(Enumerable.Range(1, 5).Select(i => Text("A", $"공통 메시지 {i}/5")));
        report.Steps[1].Messages.Add(Text("B", "단일 TXT 첨부")); report.Steps[1].AttachmentPaths.Add(single);
        report.Steps[2].Messages.Add(Text("C", "TXT / CSV 다중 첨부")); report.Steps[2].AttachmentPaths.AddRange(new[] { multiple, csv });
        report.Steps[3].AttachmentPaths.Add(only);
        report.Steps[4].Messages.Add(Text("E-PDF", "PDF 단독 첨부")); report.Steps[4].AttachmentPaths.Add(pdf);
        report.Steps[5].Messages.Add(Text("E-PNG", "PNG 단독 첨부 / 이미지 미리보기")); report.Steps[5].AttachmentPaths.Add(png);
        report.Steps[6].Messages.Add(Text("F", "이 첫 메시지가 반환된 직후 제어 중단합니다."));
        report.Steps[6].Messages.Add(Text("F", "이 두 번째 메시지는 중단 / 재시도 차단으로 전송되면 안 됩니다."));
    }

    private static void CreateSyntheticPng(string path, string stamp)
    {
        File.WriteAllBytes(path, SyntheticBotImage.Create(stamp));
    }

    private static byte[] CreateSyntheticPdf(string stamp)
    {
        string content = "BT /F1 12 Tf 15 50 Td (NoticeSender synthetic bot test " + stamp + ") Tj ET\n";
        var objects = new[] { "<< /Type /Catalog /Pages 2 0 R >>", "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 600 100] /Resources << /Font << /F1 5 0 R >> >> /Contents 4 0 R >>",
            "<< /Length " + Encoding.ASCII.GetByteCount(content) + " >>\nstream\n" + content + "endstream", "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>" };
        var text = new StringBuilder("%PDF-1.4\n");
        var offsets = new List<int>();
        for (int index = 0; index < objects.Length; index++)
        {
            offsets.Add(Encoding.ASCII.GetByteCount(text.ToString()));
            text.Append(index + 1).Append(" 0 obj\n").Append(objects[index]).Append("\nendobj\n");
        }
        int xref = Encoding.ASCII.GetByteCount(text.ToString());
        text.Append("xref\n0 6\n0000000000 65535 f \n");
        foreach (int offset in offsets) text.Append(offset.ToString("D10", System.Globalization.CultureInfo.InvariantCulture)).Append(" 00000 n \n");
        text.Append("trailer\n<< /Size 6 /Root 1 0 R >>\nstartxref\n").Append(xref).Append("\n%%EOF\n");
        return Encoding.ASCII.GetBytes(text.ToString());
    }

    private sealed class TraceClient(IChatClient inner, IDryRunChatClient opener, BotSendTestReport report, Action save, IBotEvidenceCapture? evidenceCapture) : IChatClient
    {
        private ChatTarget? pinnedTarget;
        private ChatTarget? verifiedTarget;
        private bool failureCaptured;
        internal string Step { get; set; } = "prepare";
        internal int TransportCallCount { get; private set; }
        internal int TextReturnCount { get; private set; }
        internal void CapturePreparationEvidence() => CaptureEvidence("prepared-before-send");
        internal Action? AfterTextReturned { get; set; }
        private void CaptureEvidence(string stage)
        {
            if (evidenceCapture == null || verifiedTarget == null || report.ArtifactDirectory == null) return;
            try { report.Evidence.Add(evidenceCapture.Capture(verifiedTarget, stage, report.ArtifactDirectory)); }
            catch (Exception ex) { report.Evidence.Add(new(DateTimeOffset.Now, stage, verifiedTarget.WindowHandle.ToInt64(), Array.Empty<BotEvidenceImage>(), ex.Message)); }
        }
        private void Target(ChatTarget target, bool openingCandidate = false)
        {
            if (target.RoomName != report.Room || target.ProcessId <= 0 || target.WindowHandle == 0 || target.ComposerHandle == 0)
                throw new InvalidOperationException("허용 봇 방의 창 / 입력란 소유권을 검증하지 못했습니다.");
            if (pinnedTarget == null ? !openingCandidate : target != pinnedTarget)
                throw new InvalidOperationException("처음 검증한 봇 방의 프로세스 / 창 / 입력란 식별자가 변경되었습니다. 자동으로 다시 연결하지 않습니다.");
        }
        private async Task<T> Call<T>(string operation, Func<Task<T>> action, CancellationToken ct, string? text = null, IReadOnlyList<string>? files = null)
        {
            ct.ThrowIfCancellationRequested();
            TransportCallCount++;
            report.Trace.Add(new(DateTimeOffset.Now, Step, operation, "started", text, files)); save();
            try
            {
                var result = await action();
                if (operation is "send-text" or "send-files") CaptureEvidence(Step + "-" + operation + "-returned");
                report.Trace.Add(new(DateTimeOffset.Now, Step, operation, "returned", text, files)); save();
                return result;
            }
            catch (Exception ex)
            {
                if (!failureCaptured) { failureCaptured = true; CaptureEvidence(Step + "-" + operation + "-failed"); }
                report.Trace.Add(new(DateTimeOffset.Now, Step, operation, "failed", Error: WindowsTrustFailure.Describe(ex), DiagnosticException: ex.ToString())); save(); throw;
            }
        }
        internal Task<RoomOpenDryRunResult> OpenAsync(string room, CancellationToken ct) => Call("open-room", async () =>
        {
            var result = await opener.OpenRoomDryRunAsync(room, ct);
            if (result.RoomName != report.Room) throw new InvalidOperationException("열린 방 이름이 허용 봇과 다릅니다.");
            Target(result.Target, openingCandidate: true);
            pinnedTarget ??= result.Target with { };
            return result;
        }, ct);
        public Task<ChatTarget> ResolveAsync(string room, CancellationToken ct) => Call("resolve", async () =>
        {
            if (room != report.Room) throw new InvalidOperationException("허용 봇 이외의 방을 확인할 수 없습니다.");
            var target = await inner.ResolveAsync(room, ct); Target(target); verifiedTarget ??= target with { }; return target;
        }, ct);
        public async Task SendTextAsync(ChatTarget target, string text, CancellationToken ct)
        {
            Target(target);
            if (!report.Steps.SelectMany(s => s.Messages).Contains(text, StringComparer.Ordinal)) throw new InvalidOperationException("합성 테스트 문구 이외의 전송을 차단했습니다.");
            await Call("send-text", async () => { await inner.SendTextAsync(target, text, ct); return true; }, ct, text: text);
            TextReturnCount++;
            AfterTextReturned?.Invoke();
        }
        public async Task SendFilesAsync(ChatTarget target, IReadOnlyList<string> paths, CancellationToken ct)
        {
            Target(target);
            if (paths.Any(p => !report.SyntheticFiles.Contains(p, StringComparer.Ordinal))) throw new InvalidOperationException("생성한 합성 파일 이외의 전송을 차단했습니다.");
            await Call("send-files", async () =>
            {
                await inner.SendFilesAsync(target, paths, ct);
                if (inner is IAttachmentCompletionOutcomeSource source && source.LastAttachmentCompletion != null)
                    throw new InvalidOperationException("합성 자동 시험에서는 사용자 확인 결과를 자동 완료로 처리할 수 없습니다.");
                return true;
            }, ct, files: paths.ToArray());
        }
        public async Task CompleteAsync(ChatTarget target, CancellationToken ct)
        { Target(target); await Call("complete", async () => { await inner.CompleteAsync(target, ct); return true; }, ct); }
    }
}
