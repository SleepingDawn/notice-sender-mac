using System.Globalization;
using System.IO.Compression;
using System.Text.Json;
using System.Xml.Linq;

namespace NoticeSender.Core;
public static partial class StudentFiles
{
    private sealed record WorkbookSheet(string Name, List<List<string>> Rows);
    private static bool HasStudentHeaders(List<List<string>> rows) => rows.Take(30).Any(row => new[] { "name", "school", "year", "room" }.All(key => row.Any(cell => Aliases[key].Contains(Normal(cell)))));
    private static List<WorkbookSheet> ReadAllSheets(string path)
    {
        using var zip = ZipFile.OpenRead(path);
        XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var shared = zip.GetEntry("xl/sharedStrings.xml")is { } ss ? Xml(ss).Descendants(ns + "si").Select(x => string.Concat(x.Descendants(ns + "t").Select(t => t.Value))).ToList() : [];
        var definitions = new List<(string Name, string Path)>();
        if (zip.GetEntry("xl/workbook.xml")is { } workbook && zip.GetEntry("xl/_rels/workbook.xml.rels")is { } relationships)
        {
            var rels = Xml(relationships).Descendants().Where(x => x.Name.LocalName == "Relationship").ToList();
            foreach (var sheet in Xml(workbook).Descendants(ns + "sheet"))
            {
                string? rid = sheet.Attributes().FirstOrDefault(x => x.Name.LocalName == "id")?.Value;
                var rel = rels.SingleOrDefault(x => (string? )x.Attribute("Id") == rid) ?? throw new InvalidDataException("XLSX 시트 관계를 찾을 수 없습니다.");
                if ((string? )rel.Attribute("TargetMode") == "External")
                    throw new InvalidDataException("외부 XLSX 시트는 지원하지 않습니다.");
                var target = ((string? )rel.Attribute("Target") ?? "").Replace('\\', '/');
                if (target.Length == 0 || target.Split('/').Contains("..") || target.Contains(':'))
                    throw new InvalidDataException("안전하지 않은 XLSX 관계 경로입니다.");
                definitions.Add(((string? )sheet.Attribute("name") ?? "", target.StartsWith('/') ? target.TrimStart('/') : "xl/" + target));
            }
        }
        else
        {
            definitions.AddRange(zip.Entries.Where(x => x.FullName.StartsWith("xl/worksheets/", StringComparison.Ordinal) && x.FullName.EndsWith(".xml", StringComparison.Ordinal)).OrderBy(x => x.FullName, StringComparer.Ordinal).Select(x => (System.IO.Path.GetFileNameWithoutExtension(x.FullName), x.FullName)));
        }

        var result = new List<WorkbookSheet>();
        foreach (var(name, target)in definitions)
        {
            var entry = zip.GetEntry(target) ?? throw new InvalidDataException("XLSX 워크시트를 찾을 수 없습니다.");
            var rows = new List<List<string>>();
            foreach (var row in Xml(entry).Descendants(ns + "row"))
            {
                int rowIndex = int.TryParse((string? )row.Attribute("r"), out var number) ? number : rows.Count + 1;
                if (rowIndex < 1 || rowIndex > 1_048_576)
                    throw new InvalidDataException("XLSX 행 범위를 초과했습니다.");
                while (rows.Count < rowIndex)
                    rows.Add([]);
                var cells = rows[rowIndex - 1];
                foreach (var cell in row.Elements(ns + "c"))
                {
                    var reference = (string? )cell.Attribute("r") ?? "";
                    int column = 0;
                    foreach (char ch in reference.TakeWhile(char.IsAsciiLetter))
                        column = checked(column * 26 + char.ToUpperInvariant(ch) - 'A' + 1);
                    if (column == 0)
                        column = cells.Count + 1;
                    if (column > 16384)
                        throw new InvalidDataException("XLSX 열 범위를 초과했습니다.");
                    while (cells.Count < column)
                        cells.Add("");
                    var text = cell.Element(ns + "v")?.Value ?? "";
                    var type = (string? )cell.Attribute("t");
                    if (type == "s")
                    {
                        if (!int.TryParse(text, out int index) || index < 0 || index >= shared.Count)
                            throw new InvalidDataException("XLSX 공유 문자열이 올바르지 않습니다.");
                        text = shared[index];
                    }
                    else if (type == "inlineStr")
                        text = string.Concat(cell.Descendants(ns + "t").Select(x => x.Value));
                    cells[column - 1] = text;
                }
            }

            result.Add(new(name, rows));
        }

        return result;
    }

    private static string Cell(List<List<string>> rows, int row, int column) => row <= rows.Count && column <= rows[row - 1].Count ? rows[row - 1][column - 1].Trim() : "";
    private static int? ImportedYear(string raw)
    {
        raw = raw.Trim().Replace(",", "");
        if (!double.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out double value))
        {
            if (!double.TryParse(string.Concat(raw.Where(char.IsAsciiDigit)), out value))
                return null;
        }

        if (!double.IsFinite(value) || value != Math.Truncate(value) || value < 0 || value > 9999)
            return null;
        int year = (int)value;
        if (year >= 1000)
            year %= 100;
        return year <= 99 ? year : null;
    }

    /// <summary>Imports the original multi-sheet workbook, staging all changes until validation succeeds.</summary>
    public static string ImportWorkbook(string path, AppDatabase db)
    {
        var sheets = ReadAllSheets(path);
        var roster = sheets.FirstOrDefault(s => s.Name.Trim() == "학생 DB") ?? throw new InvalidDataException("‘학생 DB’ 시트를 찾을 수 없습니다.");
        var imported = new List<Student>();
        for (int row = 5; row <= roster.Rows.Count; row++)
        {
            var name = Cell(roster.Rows, row, 2);
            var school = Cell(roster.Rows, row, 3);
            var year = ImportedYear(Cell(roster.Rows, row, 4));
            var room = Cell(roster.Rows, row, 5);
            if (name.Length == 0 || school.Length == 0 || year == null || room.Length == 0)
                continue;
            imported.Add(new() { Name = name, Nickname = GenerateNickname(name), School = school, AdmissionYear = year.Value, ChatRoomName = room });
        }

        if (imported.Count == 0)
            throw new InvalidDataException("학생 DB에 가져올 학생이 없습니다.");
        var staged = JsonSerializer.Deserialize<AppDatabase>(JsonSerializer.Serialize(db, DatabaseJson.Options), DatabaseJson.Options)!;
        var localIds = new Dictionary<Guid, Guid>();
        int additions = 0, classAdditions = 0, skipped = 0;
        foreach (var student in imported)
        {
            var existing = staged.Students.Where(s => s.Name == student.Name && s.School == student.School && s.AdmissionYear == student.AdmissionYear && s.ChatRoomName == student.ChatRoomName).ToList();
            if (existing.Count > 1)
            {
                skipped++;
                continue;
            }

            if (existing.Count == 1)
                localIds[student.Id] = existing[0].Id;
            else
            {
                staged.Students.Add(student);
                localIds[student.Id] = student.Id;
                additions++;
            }
        }

        foreach (var sheet in sheets.Where(s => s.Name.Trim() != "학생 DB"))
        {
            var school = new[]
            {
                "한성",
                "세종",
                "인천",
                "경기",
                "대구"
            }.FirstOrDefault(sheet.Name.Contains);
            var digits = string.Concat(sheet.Name.Where(char.IsAsciiDigit));
            if (school == null || digits.Length < 2 || !int.TryParse(digits[..2], out int year) || staged.Classes.Any(c => c.Name == sheet.Name))
                continue;
            var members = new List<ClassMember>();
            for (int row = 4; row <= sheet.Rows.Count; row++)
            {
                var fullName = Cell(sheet.Rows, row, 2);
                if (fullName.Length == 0)
                    continue;
                var candidates = imported.Where(s => s.Name == fullName && s.School == school && s.AdmissionYear == year).ToList();
                if (candidates.Count != 1 || !localIds.TryGetValue(candidates[0].Id, out var id))
                {
                    skipped++;
                    continue;
                }

                if (members.All(m => m.StudentId != id))
                    members.Add(new() { StudentId = id });
            }

            if (members.Count == 0)
                continue;
            members = members.OrderBy(m => staged.Students.Single(s => s.Id == m.StudentId).Name, StringComparer.CurrentCulture).ToList();
            staged.Classes.Add(new() { Name = sheet.Name, School = school, AdmissionYear = year, Members = members });
            classAdditions++;
        }

        DatabaseJson.Validate(staged);
        db.Students = staged.Students;
        db.Classes = staged.Classes;
        return $"학생 {additions}명과 반 {classAdditions}개를 추가했습니다. 모호하거나 연결하지 못한 명단 {skipped}건은 제외했습니다.";
    }
}
