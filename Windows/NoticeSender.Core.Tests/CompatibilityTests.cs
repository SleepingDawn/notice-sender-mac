using NoticeSender.Core;
using System.IO.Compression;
using System.Text.Json;
using System.Xml.Linq;

static class CompatibilityTests
{
    public static void Run(Action<string, Action> test, Action<bool, string> check, Action<Action> reject, string temp)
    {
        string Build(string filename, bool legacy)
        {
            var path = Path.Combine(temp, filename);
            using var z = ZipFile.Open(path, ZipArchiveMode.Create);
            void E(string name, string text)
            {
                using var w = new StreamWriter(z.CreateEntry(name).Open());
                w.Write(text);
            }

            E("xl/workbook.xml", "<workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"><sheets><sheet name=\"설명\" r:id=\"r1\"/><sheet name=\"학생 DB\" r:id=\"r2\"/><sheet name=\"한성27 정규반\" r:id=\"r3\"/></sheets></workbook>");
            E("xl/_rels/workbook.xml.rels", "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"r1\" Target=\"worksheets/sheet1.xml\"/><Relationship Id=\"r2\" Target=\"worksheets/sheet2.xml\"/><Relationship Id=\"r3\" Target=\"worksheets/sheet3.xml\"/></Relationships>");
            string C(string r, string value) => $"<c r=\"{r}\" t=\"inlineStr\"><is><t>{value}</t></is></c>";
            string Sheet(string rows) => "<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheetData>" + rows + "</sheetData></worksheet>";
            E("xl/worksheets/sheet1.xml", Sheet("<row r=\"1\">" + C("A1", "도움말") + "</row>"));
            string headers = "<row r=\"4\">" + C("B4", "이름") + C("C4", "학교") + C("D4", "학번") + C("E4", "톡방 이름") + "</row>";
            string data = "<row r=\"5\">" + C("B5", "가상학생") + C("C5", "한성") + C("D5", "27") + C("E5", "연습방1") + "</row><row r=\"7\">" + C("B7", "중복가상") + C("C7", "한성") + C("D7", "27") + C("E7", "연습방2") + "</row><row r=\"8\">" + C("B8", "중복가상") + C("C8", "한성") + C("D8", "27") + C("E8", "연습방3") + "</row>";
            E("xl/worksheets/sheet2.xml", Sheet((legacy ? "" : headers) + data));
            E("xl/worksheets/sheet3.xml", Sheet("<row r=\"4\">" + C("B4", "가상학생") + "</row><row r=\"5\">" + C("B5", "중복가상") + "</row><row r=\"9\">" + C("B9", "가상학생") + "</row>"));
            return path;
        }

        test("XLSX student import searches all worksheets", () =>
        {
            var rows = StudentFiles.Read(Build("all-sheets.xlsx", false));
            check(rows.Count == 3 && rows[0].Name == "가상학생", "Later student sheet was ignored");
        });
        test("legacy workbook migrates fixed sparse rows and unambiguous class membership transactionally", () =>
        {
            var path = Build("migration.xlsx", true);
            var existing = new Student
            {
                Name = "가상학생",
                School = "한성",
                AdmissionYear = 27,
                ChatRoomName = "연습방1",
                Nickname = "맞춤호칭"
            };
            var db = new AppDatabase
            {
                Students = [existing]
            };
            var method = typeof(StudentFiles).GetMethod("ImportWorkbook") ?? throw new Exception("Missing workbook migration API");
            method.Invoke(null, [path, db]);
            check(db.Students.Count == 3, "Ambiguous source identities collapsed");
            var group = db.Classes.Single();
            check(group.Name == "한성27 정규반" && group.School == "한성" && group.AdmissionYear == 27 && group.Members.Count == 1 && group.Members[0].StudentId == existing.Id, "Wrong or ambiguous class membership");
            check(db.Students.Single(x => x.Id == existing.Id).Nickname == "맞춤호칭", "Migration erased custom nickname");
            var count = db.Students.Count;
            reject(() => method.Invoke(null, [Path.Combine(temp, "missing.xlsx"), db]));
            check(db.Students.Count == count, "Failed migration changed DB");
        });
        test("all exports write whole-second UTC dates compatible with Swift ISO8601", () =>
        {
            var preset = DefaultPresets.Create()[0];
            preset.UpdatedAt = DateTimeOffset.Parse("2026-11-03T12:34:56.1234567+09:00");
            var db = new AppDatabase
            {
                Presets = [preset],
                Logs = [new()
                {
                    SentAt = DateTimeOffset.Parse("2026-11-03T12:34:56.987+09:00")
                }

                ]
            };
            var backup = Path.Combine(temp, "date-backup.json");
            new DatabaseStore().ExportBackup(backup, db);
            using var d = JsonDocument.Parse(File.ReadAllText(backup));
            check(d.RootElement.GetProperty("presets")[0].GetProperty("updatedAt").GetString() == "2026-11-03T03:34:56Z", "Fractional or offset date emitted");
            check(d.RootElement.GetProperty("logs")[0].GetProperty("sentAt").GetString() == "2026-11-03T03:34:56Z", "Log date incompatible");
            var file = Path.Combine(temp, "date-preset.json");
            ArchiveService.ExportPreset(file, preset);
            using var p = JsonDocument.Parse(File.ReadAllText(file));
            check(p.RootElement.GetProperty("preset").GetProperty("updatedAt").GetString() == "2026-11-03T03:34:56Z", "Preset date incompatible");
            check(!p.RootElement.GetProperty("exportedAt").GetString()!.Contains('.'), "Export date fractional");
            var parsed = JsonSerializer.Deserialize<MessagePreset>("{\"updatedAt\":\"2026-11-03T12:34:56.123+09:00\"}", DatabaseJson.Options)!;
            check(parsed.UpdatedAt.Offset == TimeSpan.FromHours(9) && parsed.UpdatedAt.Millisecond == 123, "Date reader rejected offset/fractions");
        });
    }
}
