using System.Text.Json;
using System.Text.Json.Serialization;

namespace NoticeSender.Core;
public abstract class ExtensibleModel
{
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }
}

public class Student : ExtensibleModel
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string Nickname { get; set; } = "";
    public string School { get; set; } = "";
    public int AdmissionYear { get; set; }
    public string ChatRoomName { get; set; } = "";

    [JsonPropertyName("chatID")]
    public string? ChatId { get; set; }
    public bool IsActive { get; set; } = true;
}

public class ClassMember : ExtensibleModel
{
    [JsonPropertyName("studentID")]
    public Guid StudentId { get; set; }
    public string? NicknameOverride { get; set; }
}

public class ClassGroup : ExtensibleModel
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string School { get; set; } = "";
    public int AdmissionYear { get; set; }
    public List<ClassMember> Members { get; set; } = [];

    [JsonPropertyName("defaultPresetID")]
    public Guid? DefaultPresetId { get; set; }
}

public class MessagePreset : ExtensibleModel
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Kind { get; set; } = "일반 수업";
    public string Name { get; set; } = "";
    public string TeacherName { get; set; } = "김요섭";
    public string AcademyName { get; set; } = "SNT";
    public string PresentTemplate { get; set; } = "";
    public string VideoTemplate { get; set; } = "";
    public string AbsentTemplate { get; set; } = "";
    public int Version { get; set; } = 1;
    public double HighThreshold { get; set; } = .8;
    public double MiddleThreshold { get; set; } = .5;
    public int? MockExamCount { get; set; }
    public string? FooterTemplate { get; set; }
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    [JsonIgnore]
    public string Category => Kind is "직접입력" or "모의고사" ? Kind : "일반 수업";
}

public class ExamInput : ExtensibleModel
{
    public string Title { get; set; } = "";
    public string Comment { get; set; } = "";
    public double? Score { get; set; }
    public double? Maximum { get; set; }
    public double? Average { get; set; }
    public double? Highest { get; set; }
    public int? Rank { get; set; }
    public int? Attendees { get; set; }
}

public class LessonInput : ExtensibleModel
{
    [JsonPropertyName("studentID")]
    public Guid StudentId { get; set; }
    public string StudentName { get; set; } = "";
    public string Nickname { get; set; } = "";
    public string Date { get; set; } = "";
    public string Attendance { get; set; } = "출석";
    public string Attitude { get; set; } = "3";
    public string HomeworkComment { get; set; } = "";
    public string TestComment { get; set; } = "";
    public string Progress { get; set; } = "";
    public string Assignment { get; set; } = "";
    public string ExamUnit { get; set; } = "";
    public string Notice { get; set; } = "";
    public double? HomeworkScore { get; set; }
    public double? HomeworkMaximum { get; set; }
    public double? TestScore { get; set; }
    public double? TestMaximum { get; set; }
    public List<ExamInput> Exams { get; set; } = [];
}

public class BatchItem : ExtensibleModel
{
    public Guid Id { get; set; } = Guid.NewGuid();

    [JsonPropertyName("studentID")]
    public Guid StudentId { get; set; }
    public string StudentName { get; set; } = "";
    public string Nickname { get; set; } = "";
    public string ChatRoomName { get; set; } = "";
    public string Message { get; set; } = "";
    public string Status { get; set; } = "준비";

    [JsonPropertyName("chatID")]
    public string? ChatId { get; set; }

    [JsonPropertyName("additionalMessages")]
    public List<string> CommonMessages { get; set; } = [];
    public List<string> AttachmentPaths { get; set; } = [];
    public bool? PreserveMessageWhitespace { get; set; }

    [JsonIgnore]
    public IReadOnlyList<string> AllMessages => new[]
    {
        Message
    }.Concat(CommonMessages).Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => PreserveMessageWhitespace == true ? x : x.Trim()).ToList();
}

public class SendLog : ExtensibleModel
{
    public Guid Id { get; set; } = Guid.NewGuid();

    [JsonPropertyName("batchID")]
    public Guid BatchId { get; set; }

    [JsonPropertyName("studentID")]
    public Guid StudentId { get; set; }
    public string StudentName { get; set; } = "";
    public string ChatRoomName { get; set; } = "";
    public string Result { get; set; } = "";

    [JsonPropertyName("messageSHA256")]
    public string MessageSHA256 { get; set; } = "";
    public DateTimeOffset SentAt { get; set; } = DateTimeOffset.UtcNow;
    public List<string>? Messages { get; set; }
    public string? Detail { get; set; }
}

public class AppDatabase : ExtensibleModel
{
    public int SchemaVersion { get; set; } = 8;
    public List<Student> Students { get; set; } = [];
    public List<ClassGroup> Classes { get; set; } = [];
    public List<MessagePreset> Presets { get; set; } = DefaultPresets.Create();
    public List<SendLog> Logs { get; set; } = [];
    public string? AttachmentRootPath { get; set; }
}
