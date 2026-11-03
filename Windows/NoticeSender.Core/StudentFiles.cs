using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace NoticeSender.Core;
public static partial class StudentFiles
{
    static string Normal(string s) => string.Concat(s.Where(char.IsLetterOrDigit)).ToLowerInvariant();
    static readonly Dictionary<string, string[]> Aliases = new()
    {
        ["id"] = ["studentid", "학생id", "학생고유id", "uuid"],
        ["name"] = ["이름", "성명", "성명홍길동", "name", "studentname", "학생이름"],
        ["nickname"] = ["호칭", "별칭", "부르는이름", "성명길동이", "nickname", "학생호칭"],
        ["school"] = ["학교", "school", "학교명"],
        ["year"] = ["학번", "입학년도", "입학연도", "기수", "year", "admissionyear"],
        ["room"] = ["톡방이름", "카카오톡", "카카오톡채팅방", "채팅방", "채팅방이름", "톡방", "chatroom", "chatroomname", "kakaotalk"],
        ["chat"] = ["chatid", "톡방id", "채팅방id", "kmsgchatid"],
        ["active"] = ["상태", "활성상태", "status", "active", "isactive"]
    };
    public static List<Student> Read(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        List<List<string>> rows;
        if (ext == ".xlsx")
            rows = ReadXlsx(path);
        else if (ext is ".csv" or ".tsv")
        {
            var bytes = File.ReadAllBytes(path);
            string text;
            if (bytes.Length >= 2 && (bytes[0] == 255 && bytes[1] == 254 || bytes[0] == 254 && bytes[1] == 255))
            {
                text = (bytes[0] == 255 ? Encoding.Unicode : Encoding.BigEndianUnicode).GetString(bytes);
            }
            else
            {
                try
                {
                    text = new UTF8Encoding(false, true).GetString(bytes);
                }
                catch (DecoderFallbackException)
                {
                    Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
                    text = Encoding.GetEncoding(51949, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback).GetString(bytes);
                }
            }

            var first = text.Split('\n').Take(5);
            char d = ext == ".tsv" || first.Sum(x => x.Count(c => c == '\t')) > first.Sum(x => x.Count(c => c == ',')) ? '\t' : ',';
            rows = SheetParser.ParseDelimited(text, d);
        }
        else
            throw new InvalidDataException("지원 형식은 .xlsx, .csv, .tsv입니다.");
        return FromRows(rows);
    }

    static List<Student> FromRows(List<List<string>> rows)
    {
        Dictionary<string, int>? map = null;
        int start = 0;
        for (; start < Math.Min(30, rows.Count); start++)
        {
            var m = new Dictionary<string, int>();
            for (int c = 0; c < rows[start].Count; c++)
            {
                var n = Normal(rows[start][c]);
                foreach (var alias in Aliases)
                    if (alias.Value.Contains(n))
                        m[alias.Key] = c;
            }

            if (new[]
            {
                "name",
                "school",
                "year",
                "room"
            }.All(m.ContainsKey))
            {
                map = m;
                break;
            }
        }

        if (map == null)
            throw new InvalidDataException("이름·학교·학번·톡방 이름 열을 찾을 수 없습니다.");
        List<Student> result = [];
        foreach (var row in rows.Skip(start + 1))
        {
            string V(string k) => map.TryGetValue(k, out int c) && c < row.Count ? row[c].Trim() : "";
            var name = V("name");
            var school = V("school");
            var room = V("room");
            var raw = V("year").Replace(",", "");
            if (!double.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out double y))
            {
                var digits = string.Concat(raw.Where(char.IsAsciiDigit));
                if (!double.TryParse(digits, out y))
                    continue;
            }

            if (y != Math.Truncate(y) || y < 0 || y > 9999)
                continue;
            int year = (int)y;
            if (year >= 1000)
                year %= 100;
            if (year > 99 || name.Length == 0 || school.Length == 0 || room.Length == 0)
                continue;
            var s = new Student
            {
                Name = name,
                School = school,
                ChatRoomName = room,
                AdmissionYear = year,
                Nickname = V("nickname")
            };
            if (Guid.TryParse(V("id"), out var id))
                s.Id = id;
            if (s.Nickname.Length == 0)
                s.Nickname = GenerateNickname(name);
            s.ChatId = V("chat");
            if (s.ChatId.Length == 0)
                s.ChatId = null;
            s.IsActive = V("active").ToLowerInvariant()is not ("비활성" or "inactive" or "false" or "no" or "n" or "0");
            // Retain import presence so partial rosters cannot erase an existing nickname/status/chat identifier.
            var supplied = new[]
            {
                ("nickname", map.ContainsKey("nickname")),
                ("chat", map.ContainsKey("chat")),
                ("active", map.ContainsKey("active"))
            };
            ImportPresence.Add(s, supplied.Where(x => x.Item2).Select(x => x.Item1).ToHashSet());
            result.Add(s);
        }

        result = result.DistinctBy(s => (s.Name, s.School, s.AdmissionYear, s.ChatRoomName)).ToList();
        if (result.Count == 0)
            throw new InvalidDataException("필수값이 모두 채워진 학생 행이 없습니다.");
        return result;
    }

    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Student, HashSet<string>> ImportPresence = new();
    static string GenerateNickname(string name)
    {
        var s = Regex.Replace(name, "[A-Za-z]", "").Trim();
        if (s.Length > 1)
            s = s[1..];
        if (s.Length > 0 && s[^1] >= '가' && s[^1] <= '힣' && (s[^1] - '가') % 28 != 0)
            s += "이";
        return s;
    }

    public static void WriteCsv(string path, IEnumerable<Student> students)
    {
        string Q(string s) => "\"" + s.Replace("\"", "\"\"") + "\"";
        var lines = new List<string>
        {
            "student_id,이름,호칭,학교,학번,톡방 이름,chat_id,상태"
        };
        lines.AddRange(students.OrderBy(s => s.School).ThenBy(s => s.AdmissionYear).ThenBy(s => s.Name).Select(s => string.Join(",", new[] { s.Id.ToString(), s.Name, s.Nickname, s.School, s.AdmissionYear.ToString("D2"), s.ChatRoomName, s.ChatId ?? "", s.IsActive ? "활성" : "비활성" }.Select(Q))));
        File.WriteAllText(path, string.Join("\r\n", lines), new UTF8Encoding(true));
    }

    public static int Merge(AppDatabase db, IEnumerable<Student> students)
    {
        var incoming = students.ToList();
        // Validate the complete roster before changing any existing records.
        var identifiers = db.Students.ToDictionary(s => s.Id, s => (s.Name, s.School, s.AdmissionYear));
        foreach (var student in incoming)
        {
            var identity = (student.Name, student.School, student.AdmissionYear);
            if (student.Id == Guid.Empty || string.IsNullOrWhiteSpace(student.Name) || string.IsNullOrWhiteSpace(student.School) || student.AdmissionYear is < 0 or > 99)
                throw new InvalidDataException("학생 식별자·이름·학교·학번을 확인해주세요.");
            if (identifiers.TryGetValue(student.Id, out var existing) && existing != identity)
                throw new InvalidDataException("동일 학생 식별자의 이름·학교·학번이 다릅니다.");
            identifiers[student.Id] = identity;
        }

        int added = 0;
        foreach (var s in incoming)
        {
            var local = db.Students.FirstOrDefault(x => x.Id == s.Id);
            if (local != null && (local.Name != s.Name || local.School != s.School || local.AdmissionYear != s.AdmissionYear))
                throw new InvalidDataException("동일 학생 식별자의 이름·학교·학번이 다릅니다.");
            local ??= db.Students.FirstOrDefault(x => x.Name == s.Name && x.School == s.School && x.AdmissionYear == s.AdmissionYear);
            if (local == null)
            {
                db.Students.Add(s);
                added++;
            }
            else
            {
                local.ChatRoomName = s.ChatRoomName;
                var partial = ImportPresence.TryGetValue(s, out var supplied);
                if (!partial || supplied!.Contains("nickname"))
                    local.Nickname = s.Nickname;
                if (!partial || supplied!.Contains("chat"))
                    local.ChatId = s.ChatId;
                if (!partial || supplied!.Contains("active"))
                    local.IsActive = s.IsActive;
            }
        }

        return added;
    }

    static XDocument Xml(ZipArchiveEntry e)
    {
        if (e.Length > 32_000_000)
            throw new InvalidDataException("XLSX XML 크기가 너무 큽니다.");
        using var stream = e.Open();
        using var reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 32_000_000 });
        return XDocument.Load(reader);
    }

    static List<List<string>> ReadXlsx(string path)
    {
        foreach (var sheet in ReadAllSheets(path))
        {
            if (HasStudentHeaders(sheet.Rows))
                return sheet.Rows;
        }

        throw new InvalidDataException("이름·학교·학번·톡방 이름 열을 찾을 수 없습니다.");
    }
}
