using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NoticeSender.Automation;

namespace NoticeSender.Windows;

internal sealed class RoomDryRunReport
{
    public string Mode => "room-open-only";
    public DateTimeOffset StartedAt { get; } = DateTimeOffset.Now;
    public DateTimeOffset? FinishedAt { get; set; }
    public bool Completed { get; set; }
    public bool Cancelled { get; set; }
    public int RequestedCount => Rooms.Count;
    public int SuccessCount => Rooms.Count(r => r.Status is "already-open" or "newly-opened");
    public int FailedCount => Rooms.Count(r => r.Status == "failed");
    public bool Success => Completed && !Cancelled && SuccessCount == RequestedCount;
    public int ExitCode => Cancelled ? 2 : Success ? 0 : 1;
    public List<RoomDryRunStep> Rooms { get; init; } = new();
}

internal sealed class RoomDryRunStep
{
    public string RoomName { get; init; } = "";
    public string Status { get; set; } = "pending";
    [System.Text.Json.Serialization.JsonIgnore]
    public string DisplayStatus => Status switch
    {
        "opening" => "방 여는 중…",
        "already-open" => "기존 창 · 확인 완료",
        "newly-opened" => "새로 열린 창 · 확인 완료",
        "failed" => "실패 · 중단",
        "cancelled" => "중지됨",
        _ => "대기"
    };
    public bool? WasAlreadyOpen { get; set; }
    public int? ProcessId { get; set; }
    public long? WindowHandle { get; set; }
    public long? ComposerHandle { get; set; }
    public string? Error { get; set; }
}

/// <summary>Room opening has a capability boundary that exposes no message or file delivery methods.</summary>
internal sealed class RoomDryRunRunner(IDryRunChatClient client)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };

    internal static string[] ValidateRooms(IEnumerable<string> rooms)
    {
        var names = rooms.ToArray();
        if (names.Length is < 1 or > 50 || names.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException("정확한 채팅방 이름을 1~50개 입력하세요. 빈 이름은 사용할 수 없습니다.");
        if (names.Distinct(StringComparer.Ordinal).Count() != names.Length)
            throw new ArgumentException("같은 채팅방 이름이 중복되어 있습니다.");
        return names;
    }

    internal static string DefaultReportPath() => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "NoticeSender", "DryRunReports", "room-open-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N") + ".json");

    internal async Task<RoomDryRunReport> RunAsync(IEnumerable<string> rooms, string reportPath, Action<RoomDryRunReport>? progress, CancellationToken cancellationToken)
    {
        var names = ValidateRooms(rooms);
        var report = new RoomDryRunReport { Rooms = names.Select(name => new RoomDryRunStep { RoomName = name }).ToList() };
        void Publish()
        {
            string fullPath = Path.GetFullPath(reportPath);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            string temporary = fullPath + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(report, JsonOptions));
            File.Move(temporary, fullPath, overwrite: true);
            progress?.Invoke(report);
        }
        Publish();
        RoomDryRunStep? active = null;
        try
        {
            foreach (var step in report.Rooms)
            {
                cancellationToken.ThrowIfCancellationRequested();
                active = step;
                step.Status = "opening";
                Publish();
                var result = await client.OpenRoomDryRunAsync(step.RoomName, cancellationToken);
                if (result.RoomName != step.RoomName || result.Target.RoomName != step.RoomName || result.Target.ProcessId <= 0 || result.Target.WindowHandle == 0 || result.Target.ComposerHandle == 0)
                    throw new InvalidOperationException("요청한 방의 창과 입력란을 검증하지 못했습니다.");
                step.WasAlreadyOpen = result.WasAlreadyOpen;
                step.ProcessId = result.Target.ProcessId;
                step.WindowHandle = result.Target.WindowHandle.ToInt64();
                step.ComposerHandle = result.Target.ComposerHandle.ToInt64();
                step.Status = result.WasAlreadyOpen ? "already-open" : "newly-opened";
                active = null;
                Publish();
            }
            cancellationToken.ThrowIfCancellationRequested();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            report.Cancelled = true;
            if (active != null) active.Status = "cancelled";
        }
        catch (Exception ex)
        {
            if (active == null) throw;
            active.Status = "failed";
            active.Error = ex.Message;
        }
        report.Completed = true;
        report.FinishedAt = DateTimeOffset.Now;
        Publish();
        return report;
    }
}
