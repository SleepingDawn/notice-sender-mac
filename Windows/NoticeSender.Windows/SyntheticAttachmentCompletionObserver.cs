using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using NoticeSender.Automation;

namespace NoticeSender.Windows;

// Opt-in bot verification only: unique synthetic identities avoid matching earlier attachments.
// This observer has no input capability and is never used to choose a recipient or confirm a dialog.
internal sealed class SyntheticAttachmentCompletionObserver(string reportPath, bool acknowledgeSelfChat = false) : IAttachmentCompletionObserver
{
    // Verify the installed reader before the bot suite can send even its first text.
    private readonly AttachmentVisualRecognizer recognizer = new(Path.Combine(AppContext.BaseDirectory, "ocr", "tesseract.exe"), Path.Combine(AppContext.BaseDirectory, "ocr", "tessdata"));
    public IAttachmentCompletionSession Begin(ChatTarget target, IReadOnlyList<string> files, CancellationToken token)
    {
        BotSendTestOptions.ValidateDestination(target.RoomName, !acknowledgeSelfChat, acknowledgeSelfChat);
        return new Session(target, files, reportPath, recognizer, token);
    }
    private sealed class Session : IAttachmentCompletionSession
    {
        private readonly ExactTranscriptCapture capture;
        private readonly AttachmentVisualRecognizer recognizer;
        private readonly AttachmentCompletionScope scope;
        private readonly AttachmentExpectedFile[] expected;
        private readonly AttachmentCompletionGate gate;
        private readonly string folder;
        private readonly string stamp;
        private readonly byte[]? imageSource;
        private long sequence;
        private bool disposed;
        private bool faulted;
        private readonly object sync = new();
        internal Session(ChatTarget target, IReadOnlyList<string> files, string reportPath, AttachmentVisualRecognizer recognizer, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            expected = files.Select(path => new AttachmentExpectedFile(Path.GetFileName(path), new FileInfo(path).Length)).ToArray();
            var stamps = expected.Select(f => Regex.Match(f.Name, @"\ANoticeSender-SYNTHETIC-(\d{8}-\d{6}-\d{3}-[0-9a-f]{8})-")).ToArray();
            if (stamps.Length == 0 || stamps.Any(m => !m.Success) || stamps.Select(m => m.Groups[1].Value).Distinct().Count() != 1)
                throw new InvalidOperationException("고유한 합성 첨부 확인에 필요한 식별자가 없습니다.");
            stamp = stamps[0].Groups[1].Value;
            if (expected.Any(f => f.Name.EndsWith(".png", StringComparison.Ordinal)))
            {
                if (files.Count != 1) throw new InvalidOperationException("합성 이미지 검사는 이미지 한 개만 지원합니다.");
                imageSource = File.ReadAllBytes(files[0]);
            }
            folder = Path.GetFullPath(reportPath) + ".visual-" + Guid.NewGuid().ToString("N");
            this.recognizer = recognizer;
            capture = new ExactTranscriptCapture(target);
            scope = new(target.ProcessId, target.WindowHandle, target.ComposerHandle, capture.Transcript,
                string.Join("|", files.Select(path => { using var stream = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(stream)); })));
            gate = new AttachmentCompletionGate(scope, expected, Read(token));
        }
        private AttachmentCompletionObservation Read(CancellationToken token)
        {
            byte[] bytes = capture.Read(token);
            DateTimeOffset at = DateTimeOffset.UtcNow;
            AttachmentReceiptCount[] receipts;
            if (imageSource != null)
                receipts = [new(expected[0].Name, expected[0].Length, recognizer.CountSyntheticImageReceipts(bytes, imageSource, stamp, token))];
            else
                receipts = recognizer.ReadExpectedFileReceipts(bytes, expected.Select(file => new VisualAttachmentReceipt(file.Name, file.Length)).ToArray(), token)
                    .GroupBy(r => (r.Name, r.Length)).Select(g => new AttachmentReceiptCount(g.Key.Name, g.Key.Length, g.Count())).ToArray();
            // Revalidate after OCR as it may outlive a close/reopen or modal transition.
            token.ThrowIfCancellationRequested(); capture.RequireCurrent();
            long number = ++sequence;
            Directory.CreateDirectory(folder);
            string image = Path.Combine(folder, $"{number:D3}.png");
            using (var file = new FileStream(image, FileMode.CreateNew, FileAccess.Write, FileShare.Read)) { file.Write(bytes); file.Flush(true); }
            using (var audit = new FileStream(Path.Combine(folder, $"{number:D3}.json"), FileMode.CreateNew, FileAccess.Write, FileShare.Read))
            {
                JsonSerializer.Serialize(audit, new { capturedAt = at, sequence = number, scope.ProcessId,
                    root = scope.RootWindow.ToInt64(), composer = scope.Composer.ToInt64(), transcript = scope.Transcript.ToInt64(),
                    scope.BatchIdentity, receipts, image, sha256 = Convert.ToHexString(SHA256.HashData(bytes)) }); audit.Flush(true);
            }
            return new(scope, number, at, receipts);
        }
        public bool Observe(CancellationToken token)
        {
            lock (sync)
            {
                if (disposed) throw new ObjectDisposedException(nameof(Session));
                if (faulted) throw new InvalidOperationException("실패한 첨부 완료 검사를 다시 사용할 수 없습니다.");
                try
                {
                    var observation = Read(token);
                    token.ThrowIfCancellationRequested();
                    return gate.Observe(observation, token);
                }
                catch { faulted = true; throw; }
            }
        }
        public void Dispose() { lock (sync) disposed = true; }
    }
}
