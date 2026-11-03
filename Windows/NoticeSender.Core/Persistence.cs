using System.Text.Json;
using System.Text.Json.Serialization;

namespace NoticeSender.Core;
public static class DatabaseJson
{
    public static JsonSerializerOptions Options { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters =
        {
            new MacIsoDateConverter()
        }
    };

    public static AppDatabase Read(string path)
    {
        using var doc = JsonDocument.Parse(File.ReadAllBytes(path));
        var r = doc.RootElement;
        if (r.ValueKind != JsonValueKind.Object || !r.TryGetProperty("schemaVersion", out var v) || !v.TryGetInt32(out var schema) || schema < 1 || schema > 8 || !r.TryGetProperty("students", out _) || !r.TryGetProperty("classes", out _) || !r.TryGetProperty("presets", out _) || !r.TryGetProperty("logs", out _))
            throw new InvalidDataException("지원하지 않거나 잘못된 데이터베이스입니다.");
        foreach (var collection in new[]
        {
            "students",
            "classes",
            "presets",
            "logs"
        }

        )
        {
            if (r.GetProperty(collection).ValueKind != JsonValueKind.Array)
                throw new InvalidDataException("데이터베이스 목록 형식이 올바르지 않습니다.");
            foreach (var item in r.GetProperty(collection).EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object || !item.TryGetProperty("id", out var identifier) || !identifier.TryGetGuid(out var id) || id == Guid.Empty)
                    throw new InvalidDataException("데이터베이스 항목의 UUID가 누락되었거나 잘못되었습니다.");
            }
        }

        var db = JsonSerializer.Deserialize<AppDatabase>(r.GetRawText(), Options) ?? throw new InvalidDataException("데이터베이스가 비어 있습니다.");
        Validate(db);
        return db;
    }

    public static void Validate(AppDatabase db)
    {
        if (db.SchemaVersion is < 1 or > 8 || db.Students == null || db.Classes == null || db.Presets == null || db.Logs == null)
            throw new InvalidDataException("데이터베이스 구조가 올바르지 않습니다.");
        if (db.Students.Any(x => x.Id == Guid.Empty || string.IsNullOrWhiteSpace(x.Name) || string.IsNullOrWhiteSpace(x.School) || x.AdmissionYear is < 0 or > 99) || db.Students.Select(x => x.Id).Distinct().Count() != db.Students.Count)
            throw new InvalidDataException("학생 식별자·이름·학교·학번을 확인해주세요.");
        if (db.Classes.Select(x => x.Id).Distinct().Count() != db.Classes.Count || db.Presets.Select(x => x.Id).Distinct().Count() != db.Presets.Count)
            throw new InvalidDataException("반 또는 Preset 식별자가 중복됩니다.");
    }

    public static void AtomicWrite<T>(string path, T value)
    {
        var full = System.IO.Path.GetFullPath(path);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(full)!);
        var temp = full + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, value, Options);
                stream.Flush(true);
            }

            if (File.Exists(full))
                File.Replace(temp, full, full + ".bak", true);
            else
                File.Move(temp, full);
        }
        finally
        {
            if (File.Exists(temp))
                File.Delete(temp);
        }
    }
}

public sealed class DatabaseStore(string? path = null)
{
    public string Path { get; } = path ?? System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NoticeSender", "database.json");

    public AppDatabase Load() => File.Exists(Path) ? DatabaseJson.Read(Path) : new();
    public void Save(AppDatabase db)
    {
        DatabaseJson.Validate(db);
        DatabaseJson.AtomicWrite(Path, db);
    }

    public AppDatabase ImportBackup(string path) => DatabaseJson.Read(path);
    public void ExportBackup(string path, AppDatabase db)
    {
        DatabaseJson.Validate(db);
        DatabaseJson.AtomicWrite(path, db);
    }
}

public static class ArchiveService
{
    private sealed class Archive
    {
        public int SchemaVersion { get; set; } = 1;
        public DateTimeOffset ExportedAt { get; set; } = DateTimeOffset.UtcNow;
        public List<Student> Students { get; set; } = [];
        public List<ClassGroup> Classes { get; set; } = [];
        public List<MessagePreset> Presets { get; set; } = [];
        public MessagePreset? Preset { get; set; }
    }

    public static void ExportClasses(string path, AppDatabase db)
    {
        if (db.Classes.Count == 0)
            throw new InvalidDataException("내보낼 반이 없습니다.");
        var ids = db.Classes.SelectMany(x => x.Members).Select(x => x.StudentId).ToHashSet();
        var ps = db.Classes.Select(x => x.DefaultPresetId).ToHashSet();
        DatabaseJson.AtomicWrite(path, new Archive { Classes = db.Classes, Students = db.Students.Where(x => ids.Contains(x.Id)).ToList(), Presets = db.Presets.Where(x => ps.Contains(x.Id)).ToList() });
    }

    private static Archive Read(string path)
    {
        var a = JsonSerializer.Deserialize<Archive>(File.ReadAllText(path), DatabaseJson.Options) ?? throw new InvalidDataException("파일이 비어 있습니다.");
        if (a.SchemaVersion != 1)
            throw new InvalidDataException("지원하지 않는 파일 버전입니다.");
        return a;
    }

    public static string ImportClasses(string path, AppDatabase db)
    {
        var copy = JsonSerializer.Deserialize<AppDatabase>(JsonSerializer.Serialize(db, DatabaseJson.Options), DatabaseJson.Options)!;
        var summary = ImportClassesWorking(path, copy);
        db.Students = copy.Students;
        db.Classes = copy.Classes;
        db.Presets = copy.Presets;
        return summary;
    }

    private static string ImportClassesWorking(string path, AppDatabase db)
    {
        var a = Read(path);
        if (a.Classes.Count == 0)
            throw new InvalidDataException("반 파일에 반이 없습니다.");
        DatabaseJson.Validate(new AppDatabase { Students = a.Students, Classes = a.Classes, Presets = a.Presets });
        var map = new Dictionary<Guid, Guid>();
        int added = 0, skipped = 0;
        foreach (var s in a.Students)
        {
            var local = db.Students.FirstOrDefault(x => x.Id == s.Id);
            if (local != null && (local.Name != s.Name || local.School != s.School || local.AdmissionYear != s.AdmissionYear))
                throw new InvalidDataException("동일 식별자의 학생 정보가 다릅니다.");
            local ??= db.Students.FirstOrDefault(x => x.Name == s.Name && x.School == s.School && x.AdmissionYear == s.AdmissionYear);
            if (local == null)
            {
                db.Students.Add(s);
                local = s;
                added++;
            }

            map[s.Id] = local.Id;
        }

        foreach (var p in a.Presets)
        {
            var ix = db.Presets.FindIndex(x => x.Id == p.Id);
            if (ix < 0)
                db.Presets.Add(p);
            else
                db.Presets[ix] = p;
        }

        foreach (var c in a.Classes)
        {
            c.Members = c.Members.Where(m =>
            {
                if (!map.ContainsKey(m.StudentId))
                {
                    skipped++;
                    return false;
                }

                m.StudentId = map[m.StudentId];
                return true;
            }).DistinctBy(x => x.StudentId).ToList();
            var ix = db.Classes.FindIndex(x => x.Id == c.Id);
            if (ix < 0)
                db.Classes.Add(c);
            else
                db.Classes[ix] = c;
        }

        return $"학생 {added}명 추가, 반 {a.Classes.Count}개 연결, 연결하지 못한 명단 {skipped}건 제외했습니다.";
    }

    public static void ExportPreset(string path, MessagePreset p)
    {
        var errors = TemplateEngine.Validate(p);
        if (errors.Count > 0)
            throw new InvalidDataException(string.Join("\n", errors));
        DatabaseJson.AtomicWrite(path, new Archive { Preset = p });
    }

    public static MessagePreset ImportPreset(string path)
    {
        var p = Read(path).Preset ?? throw new InvalidDataException("Preset이 없습니다.");
        var e = TemplateEngine.Validate(p);
        if (e.Count > 0)
            throw new InvalidDataException(string.Join("\n", e));
        return p;
    }
}
