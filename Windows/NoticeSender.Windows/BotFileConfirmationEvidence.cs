using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using NoticeSender.Automation;

namespace NoticeSender.Windows;

/// <summary>Mandatory pre-dispatch evidence for the opt-in synthetic attachment experiment; never a delivery receipt.</summary>
internal sealed class BotFileConfirmationEvidence(string reportPath, IBotEvidenceCapture capture)
{
    internal void Capture(ChatTarget target, nint modal, IReadOnlyList<string> files)
    {
        string auditPath = Path.GetFullPath(reportPath) + ".native-file-confirmation.json";
        Directory.CreateDirectory(Path.GetDirectoryName(auditPath)!);
        // A consumed/failed attempt cannot overwrite its evidence or authorize a second dispatch.
        using var audit = new FileStream(auditPath, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
        var evidence = capture.Capture(target, "native-file-before-confirm", Path.GetDirectoryName(files[0])!);
        RequireImages(evidence, target.WindowHandle, modal);
        var manifest = files.Select(path =>
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            return new { path, length = stream.Length, sha256 = Convert.ToHexString(SHA256.HashData(stream)) };
        }).ToArray();
        var imageStreams = new List<FileStream>();
        try
        {
            var imageManifest = evidence.Images.Where(i => i.WindowHandle == target.WindowHandle.ToInt64() || i.WindowHandle == modal.ToInt64()).Select(i =>
            {
                // Keep the mandatory images immutable until their durable audit is committed.
                var stream = new FileStream(i.Path!, FileMode.Open, FileAccess.ReadWrite, FileShare.Read);
                imageStreams.Add(stream);
                string sha256 = Convert.ToHexString(SHA256.HashData(stream));
                stream.Flush(flushToDisk: true);
                return new { i.Path, i.WindowHandle, length = stream.Length, sha256 };
            }).ToArray();
            JsonSerializer.Serialize(audit, new
            {
                stage = "before-native-file-confirmation", deliveryConfirmed = false,
                room = target.RoomName, processId = target.ProcessId,
                rootWindow = target.WindowHandle.ToInt64(), modalWindow = modal.ToInt64(),
                files = manifest, images = imageManifest, evidence
            }, new JsonSerializerOptions { WriteIndented = true });
            audit.Flush(flushToDisk: true);
        }
        finally { foreach (var stream in imageStreams) stream.Dispose(); }
    }

    internal static void RequireImages(BotEvidenceCaptureResult evidence, nint root, nint modal)
    {
        if (root == 0 || modal == 0 || root == modal || evidence.Error != null || evidence.RootWindowHandle != root.ToInt64())
            throw new InvalidOperationException("파일 확인 직전의 봇 창 증거가 유효하지 않습니다.");
        foreach (var expected in new[] { (Handle: root.ToInt64(), Role: "bot-root"), (Handle: modal.ToInt64(), Role: "owned-window") })
        {
            var matches = evidence.Images.Where(i => i.WindowHandle == expected.Handle).ToArray();
            if (matches.Length != 1 || matches[0].Role != expected.Role || matches[0].Error != null || matches[0].Width <= 0 || matches[0].Height <= 0 ||
                matches[0].Path is not string path || !File.Exists(path) || new FileInfo(path).Length == 0)
                throw new InvalidOperationException("파일 확인 직전의 정확한 봇 창/첨부 창 이미지를 저장하지 못했습니다.");
        }
    }
}
