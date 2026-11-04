using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using NoticeSender.Automation;

namespace NoticeSender.Windows;

internal sealed class ReviewedFileConfirmationEvidence(string folder, ApprovedAttachmentBatch approved)
{
    private readonly Guid runId = Guid.NewGuid();
    internal void Capture(ChatTarget target, nint modal, IReadOnlyList<string> files)
    {
        void RequireScope()
        {
            ExactTranscriptCapture.RequireRoot(target, false);
            if (!ExactTranscriptCapture.IsOwnedModal(modal, target)) throw new InvalidOperationException("검토한 첨부 확인 창이 변경되었습니다.");
        }
        RequireScope();
        var reviewed = approved.RequireMatch(target, files);
        // Complete both reads before creating evidence; a timed-out worker can never authorize a send.
        var images = BotWindowEvidence.WithDeadline(() => new[] {
            ExactTranscriptCapture.Capture(target.WindowHandle, RequireScope),
            ExactTranscriptCapture.Capture(modal, RequireScope)
        }, TimeSpan.FromSeconds(8));
        RequireScope();
        Directory.CreateDirectory(folder);
        string id = Guid.NewGuid().ToString("N");
        var paths = new[] { Path.Combine(folder, id + "-room.png"), Path.Combine(folder, id + "-confirmation.png") };
        var imageStreams = new List<FileStream>();
        try
        {
            for (int i = 0; i < paths.Length; i++)
            {
                var file = new FileStream(paths[i], FileMode.CreateNew, FileAccess.ReadWrite, FileShare.Read);
                imageStreams.Add(file); file.Write(images[i]); file.Flush(true); file.Position = 0;
            }
            using var audit = new FileStream(Path.Combine(folder, id + ".json"), FileMode.CreateNew, FileAccess.Write, FileShare.Read);
            JsonSerializer.Serialize(audit, new {
                stage = "reviewed-file-before-confirmation", deliveryConfirmed = false, at = DateTimeOffset.Now,
                runId, batchId = reviewed.BatchId, target.RoomName, target.ProcessId, root = target.WindowHandle.ToInt64(), modal = modal.ToInt64(), composer = target.ComposerHandle.ToInt64(),
                files = files.Select(path => { using var stream = File.OpenRead(path); return new { path, length = stream.Length, sha256 = Convert.ToHexString(SHA256.HashData(stream)) }; }).ToArray(),
                images = paths.Select((path, i) => new { path, sha256 = Convert.ToHexString(SHA256.HashData(imageStreams[i])) }).ToArray()
            });
            audit.Flush(true); RequireScope();
        }
        finally { foreach (var stream in imageStreams) stream.Dispose(); }
    }
}
