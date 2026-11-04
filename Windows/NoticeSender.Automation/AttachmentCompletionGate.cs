using System.Text.RegularExpressions;

namespace NoticeSender.Automation;

public sealed record AttachmentCompletionScope(int ProcessId, nint RootWindow, nint Composer, nint Transcript, string BatchIdentity);
public sealed record AttachmentExpectedFile(string Name, long Length);
public sealed record AttachmentReceiptCount(string Name, long Length, int Count);
public sealed record AttachmentCompletionObservation(AttachmentCompletionScope Scope, long CaptureSequence,
    DateTimeOffset CapturedAt, IReadOnlyList<AttachmentReceiptCount> Receipts);

/// <summary>Fail-closed proof gate for fresh receipts of one unique generated synthetic file batch; no UI or transport.</summary>
public sealed class AttachmentCompletionGate
{
    private static readonly Regex SyntheticName = new(@"^NoticeSender-SYNTHETIC-(\d{8}-\d{6}-\d{3}-[0-9a-f]{8})-(B-single\.txt|C-first\.txt|C-second\.csv|D-only\.txt|E-document\.pdf|E-image\.png|E-한글자료\.txt)$", RegexOptions.CultureInvariant);
    private readonly object gate = new();
    private readonly AttachmentCompletionScope scope;
    private readonly AttachmentExpectedFile[] files;
    private int[] lastCounts;
    private long lastSequence;
    private DateTimeOffset lastCapture;
    private DateTimeOffset? completeSince;
    private bool faulted;

    public AttachmentCompletionGate(AttachmentCompletionScope scope, IReadOnlyList<AttachmentExpectedFile> expected,
        AttachmentCompletionObservation baseline)
    {
        Require(scope != null && scope.ProcessId > 0 && scope.RootWindow != 0 && scope.Composer != 0 && scope.Transcript != 0 &&
            !string.IsNullOrWhiteSpace(scope.BatchIdentity), "첨부 완료 증거의 정확한 창과 파일 식별자가 필요합니다.");
        this.scope = scope!;
        Require(expected != null && expected.Count > 0, "첨부 완료 증거의 파일 목록이 비어 있습니다.");
        files = expected!.ToArray();
        var names = new HashSet<string>(StringComparer.Ordinal);
        string? stamp = null;
        foreach (var file in files)
        {
            Require(file != null && file.Length >= 0 && !string.IsNullOrEmpty(file.Name), "첨부 완료 증거의 파일 정보가 올바르지 않습니다.");
            var match = SyntheticName.Match(file!.Name);
            Require(match.Success && names.Add(file.Name), "완료 증거는 고유한 합성 시험 파일 이름만 지원합니다.");
            stamp ??= match.Groups[1].Value;
            Require(stamp == match.Groups[1].Value, "서로 다른 합성 시험 회차의 파일입니다.");
        }
        Require(baseline != null && baseline.CaptureSequence >= 0 && baseline.CapturedAt != default, "첨부 완료 기준 캡처가 올바르지 않습니다.");
        lastCounts = ReadCounts(baseline!);
        Require(lastCounts.All(count => count == 0), "고유한 합성 파일의 기존 완료 증거가 있어 이 시험을 진행할 수 없습니다.");
        lastSequence = baseline!.CaptureSequence; lastCapture = baseline.CapturedAt;
    }

    public bool Observe(AttachmentCompletionObservation observation, CancellationToken cancellationToken)
    {
        lock (gate)
        {
            Require(!faulted, "유효하지 않은 첨부 완료 증거를 다시 사용할 수 없습니다.");
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                Require(observation != null && observation.CaptureSequence > lastSequence && observation.CapturedAt > lastCapture,
                    "첨부 완료 캡처가 새롭지 않거나 시각이 역행했습니다.");
                var counts = ReadCounts(observation!);
                for (int i = 0; i < counts.Length; i++)
                {
                    Require(counts[i] >= lastCounts[i], "첨부 완료 증거가 사라지거나 이전 수량으로 돌아갔습니다.");
                    Require(counts[i] <= 1, "같은 합성 파일의 완료 증거가 중복되어 중단했습니다.");
                }
                cancellationToken.ThrowIfCancellationRequested();
                lastSequence = observation!.CaptureSequence; lastCapture = observation.CapturedAt; lastCounts = counts;
                if (!counts.All(count => count == 1)) return false;
                completeSince ??= observation.CapturedAt;
                return observation.CapturedAt - completeSince.Value >= TimeSpan.FromMilliseconds(300);
            }
            catch { faulted = true; throw; }
        }
    }

    private int[] ReadCounts(AttachmentCompletionObservation observation)
    {
        Require(observation.Scope == scope, "첨부 완료 증거의 프로세스, 창, 대화 내역 또는 파일 묶음이 변경되었습니다.");
        Require(observation.Receipts != null, "첨부 완료 수량 증거가 없습니다.");
        var receipts = new Dictionary<string, AttachmentReceiptCount>(StringComparer.Ordinal);
        foreach (var receipt in observation.Receipts!.ToArray())
        {
            Require(receipt != null && !string.IsNullOrEmpty(receipt.Name) && receipt.Length >= 0 && receipt.Count >= 0,
                "첨부 완료 수량 증거가 올바르지 않습니다.");
            Require(receipts.TryAdd(receipt!.Name, receipt), "같은 파일 이름의 첨부 완료 증거가 여러 개입니다.");
        }
        return files.Select(file =>
        {
            if (!receipts.TryGetValue(file.Name, out var receipt)) return 0;
            Require(receipt.Length == file.Length, "첨부 완료 증거의 파일 크기가 원본과 다릅니다.");
            return receipt.Count;
        }).ToArray();
    }

    private static void Require(bool condition, string reason) { if (!condition) throw new InvalidOperationException(reason); }
}
