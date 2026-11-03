using NoticeSender.Core;
using System.IO.Compression;
using System.Text;
using System.Text.Json;

var temp = Path.Combine(Path.GetTempPath(), "notice-core-tests-" + Guid.NewGuid());
Directory.CreateDirectory(temp);
int passed = 0, failed = 0;
void Check(bool condition, string why)
{
    if (!condition)
        throw new Exception(why);
}

void Reject(Action action)
{
    try
    {
        action();
    }
    catch (Exception)
    {
        return;
    }

    throw new Exception("Expected rejection");
}

void Test(string name, Action action)
{
    try
    {
        action();
        passed++;
        Console.WriteLine("PASS " + name);
    }
    catch (Exception e)
    {
        failed++;
        Console.WriteLine("FAIL " + name + ": " + e.Message);
    }
}

Student Demo(string name = "가상학생") => new()
{
    Name = name,
    Nickname = "학생이",
    School = "가상고",
    AdmissionYear = 27,
    ChatRoomName = "연습방 " + name
};
Test("quoted CSV CRLF, embedded newline and escaped quotes", () =>
{
    var r = SheetParser.ParseDelimited("이름,학교\r\n\"가상,학생\",\"학교\n분교\"\r\n", ',');
    Check(r.Count == 2 && r[1][0] == "가상,학생" && r[1][1] == "학교\n분교", "CSV corruption");
    Check(SheetParser.ParseDelimited("\"a\"\"b\"", ',')[0][0] == "a\"b", "Escaped quote lost");
    Reject(() => SheetParser.ParseDelimited("\"unclosed", ','));
});
Test("schema8 original acronym keys and extension data roundtrip", () =>
{
    var p = Path.Combine(temp, "db.json");
    var id = Guid.NewGuid();
    File.WriteAllText(p, $$$"""{"schemaVersion":8,"students":[{"id":"{{{id}}}","name":"가상학생","nickname":"학생이","school":"가상고","admissionYear":27,"chatRoomName":"연습","chatID":"local-7","isActive":true,"futureStudent":42}],"classes":[{"id":"{{{Guid.NewGuid()}}}","name":"연습","school":"가상고","admissionYear":27,"members":[{"studentID":"{{{id}}}"}]}],"presets":[],"logs":[],"lessonPlans":[{"date":"2026-11-03T00:00:00Z"}],"futureDatabase":{"a":9}}""");
    var store = new DatabaseStore(p);
    var db = store.Load();
    Check(db.Students[0].ChatId == "local-7" && db.Classes[0].Members[0].StudentId == id, "ID mismatch");
    store.Save(db);
    using var doc = JsonDocument.Parse(File.ReadAllText(p));
    Check(doc.RootElement.GetProperty("futureDatabase").GetProperty("a").GetInt32() == 9 && doc.RootElement.GetProperty("students")[0].GetProperty("futureStudent").GetInt32() == 42, "Unknown data dropped");
    Check(File.ReadAllText(p).Contains("studentID"), "Original acronym key changed");
});
Test("invalid database never silently resets; import is read-only", () =>
{
    var p = Path.Combine(temp, "safe.json");
    var s = new DatabaseStore(p);
    var db = s.Load();
    Check(db.Presets.Count == 6, "Default presets missing");
    s.Save(db);
    var original = File.ReadAllText(p);
    var backup = Path.Combine(temp, "backup.json");
    s.ExportBackup(backup, new());
    s.ImportBackup(backup);
    Check(File.ReadAllText(p) == original, "Import changed live database");
    File.WriteAllText(backup, "{}");
    Reject(() => s.ImportBackup(backup));
    Check(File.ReadAllText(p) == original, "Bad backup changed database");
    File.WriteAllText(p, "broken");
    Reject(() => s.Load());
});
Test("CSV preserves UUID, quoted room, two digit cohort, inactive", () =>
{
    var a = Demo();
    a.ChatRoomName = "연습,\"방\"\n둘째줄";
    a.IsActive = false;
    var p = Path.Combine(temp, "students.csv");
    StudentFiles.WriteCsv(p, [a]);
    var r = StudentFiles.Read(p).Single();
    Check(r.Id == a.Id && r.ChatRoomName == a.ChatRoomName && !r.IsActive && r.AdmissionYear == 27, "Roundtrip changed identity");
});
Test("EUC-KR and full year", () =>
{
    Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    var p = Path.Combine(temp, "euc.csv");
    File.WriteAllBytes(p, Encoding.GetEncoding(51949).GetBytes("이름,학교,학번,톡방 이름\r\n가상학생,가상고,2027,연습방"));
    var s = StudentFiles.Read(p).Single();
    Check(s.Name == "가상학생" && s.AdmissionYear == 27, "Encoding/year mismatch");
});
Test("sparse XLSX shared rich strings and inline text", () =>
{
    var p = Path.Combine(temp, "sparse.xlsx");
    using (var z = ZipFile.Open(p, ZipArchiveMode.Create))
    {
        void E(string n, string t)
        {
            using var w = new StreamWriter(z.CreateEntry(n).Open());
            w.Write(t);
        }

        E("xl/sharedStrings.xml", "<sst xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><si><t>이름</t></si><si><t>학교</t></si><si><t>학번</t></si><si><t>톡방 이름</t></si><si><r><t>가상</t></r><r><t>학생</t></r></si></sst>");
        E("xl/worksheets/sheet1.xml", "<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheetData><row r=\"1\"><c r=\"A1\" t=\"s\"><v>0</v></c><c r=\"C1\" t=\"s\"><v>1</v></c><c r=\"D1\" t=\"s\"><v>2</v></c><c r=\"F1\" t=\"s\"><v>3</v></c></row><row r=\"4\"><c r=\"A4\" t=\"s\"><v>4</v></c><c r=\"C4\" t=\"inlineStr\"><is><t>가상고</t></is></c><c r=\"D4\"><v>2027</v></c><c r=\"F4\" t=\"inlineStr\"><is><t>연습방</t></is></c></row></sheetData></worksheet>");
    }

    var s = StudentFiles.Read(p).Single();
    Check(s.Name == "가상학생" && s.School == "가상고" && s.AdmissionYear == 27, "Sparse columns shifted");
});
Test("merge preserves identity and partial import nickname/status", () =>
{
    var a = Demo();
    a.IsActive = false;
    a.ChatId = "preserve";
    a.Nickname = "특별호칭";
    var db = new AppDatabase
    {
        Students = [a]
    };
    var p = Path.Combine(temp, "partial.csv");
    File.WriteAllText(p, "이름,학교,학번,톡방 이름\n가상학생,가상고,27,새연습방");
    Check(StudentFiles.Merge(db, StudentFiles.Read(p)) == 0 && db.Students.Count == 1 && a.ChatRoomName == "새연습방" && a.Nickname == "특별호칭" && a.ChatId == "preserve" && !a.IsActive, "Partial import erased data");
    var other = Demo();
    other.AdmissionYear = 28;
    Check(StudentFiles.Merge(db, [other]) == 1, "Cohorts collapsed");
});
Test("default templates render Korean scores footer", () =>
{
    var p = DefaultPresets.Create().Single(p => p.Kind == "일반 수업");
    var r = TemplateEngine.Render(p, new() { Nickname = "가상이", Date = "11/3", HomeworkScore = 8, HomeworkMaximum = 10, TestScore = 5, TestMaximum = 10 });
    Check(r.Contains("가상이의 [11/3]") && r.Contains("80.00%") && r.Contains("수업 집중력이 좋습니다.") && r.EndsWith(TemplateEngine.DefaultFooter), "Korean body/score/footer missing");
    foreach (var preset in DefaultPresets.Create())
        Check(TemplateEngine.Validate(preset).Count == 0, "Invalid default " + preset.Kind);
});
Test("template invalid category unknown malformed nonfinite", () =>
{
    var p = DefaultPresets.Create()[0];
    p.PresentTemplate = "{{homework_score}}";
    Check(TemplateEngine.Validate(p).Count > 0, "Direct allowed homework");
    p.PresentTemplate = "{{notice}";
    Check(TemplateEngine.Validate(p).Count > 0, "Malformed allowed");
    p.PresentTemplate = "{{unknown}}";
    Check(TemplateEngine.Validate(p).Count > 0, "Unknown allowed");
    p.PresentTemplate = "{{notice}}";
    p.HighThreshold = double.NaN;
    Check(TemplateEngine.Validate(p).Count > 0, "NaN allowed");
});
Test("render data is never recursively expanded and scores validate", () =>
{
    var p = DefaultPresets.Create()[0];
    p.FooterTemplate = "";
    Check(TemplateEngine.Render(p, new() { Notice = "{{teacher}} 그대로" }) == "{{teacher}} 그대로", "Student text recursively expanded");
    Reject(() => TemplateEngine.Render(p, new() { Attendance = "결석" }));
    Reject(() => TemplateEngine.Render(DefaultPresets.Create()[4], new() { TestScore = 11, TestMaximum = 10 }));
});
Test("mock individual scores excluded from video", () =>
{
    var p = DefaultPresets.Create().Single(x => x.Kind == "모의고사");
    var i = new LessonInput
    {
        Exams = [new()
        {
            Title = "연습회",
            Score = 45,
            Maximum = 50,
            Average = 31,
            Highest = 48,
            Rank = 2,
            Attendees = 10
        }

        ]
    };
    var r = TemplateEngine.Render(p, i);
    Check(r.Contains("학생 점수 : 45점") && r.Contains("학생 등수 : 2등"), "Missing results");
    i.Attendance = "동영상";
    r = TemplateEngine.Render(p, i);
    Check(!r.Contains("학생 점수") && r.Contains("평균 : 31"), "Video exposed results");
});
Test("attachment suffix full name year and depth", () =>
{
    var root = Path.Combine(temp, "files");
    Directory.CreateDirectory(root);
    var a = Demo("윤가상");
    var b = Demo("윤가상A");
    File.WriteAllText(Path.Combine(root, "가상고_27_윤가상A.pdf"), "test");
    File.WriteAllText(Path.Combine(root, "가상고_127_윤가상.pdf"), "test");
    var d = Directory.CreateDirectory(Path.Combine(root, "1", "2", "3"));
    File.WriteAllText(Path.Combine(d.FullName, "가상고_27_윤가상.pdf"), "too deep");
    File.WriteAllText(Path.Combine(root, "가상고_27_학생이.pdf"), "nickname");
    var r = AttachmentScanner.Scan(root, [a, b]);
    Check(r[a.Id].Count == 0 && r[b.Id].Count == 1, "Wrong suffix/depth/year/nickname");
});
Test("class archive reconnects identities and preset roundtrip", () =>
{
    var s = Demo();
    var db = new AppDatabase
    {
        Students = [s],
        Classes = [new()
        {
            Name = "연습반",
            Members = [new()
            {
                StudentId = s.Id,
                NicknameOverride = "연습이"
            }

            ]
        }

        ]
    };
    var p = Path.Combine(temp, "classes.json");
    ArchiveService.ExportClasses(p, db);
    var existing = Demo();
    var target = new AppDatabase
    {
        Students = [existing]
    };
    ArchiveService.ImportClasses(p, target);
    Check(target.Students.Count == 1 && target.Classes.Single().Members.Single().StudentId == existing.Id && target.Classes.Single().Members.Single().NicknameOverride == "연습이", "Wrong member link");
    var pp = Path.Combine(temp, "preset.json");
    ArchiveService.ExportPreset(pp, db.Presets[0]);
    Check(ArchiveService.ImportPreset(pp).Id == db.Presets[0].Id, "Preset ID changed");
});
Test("batch duplicate endpoints identity six-message limit uncertain retry", () =>
{
    var a = Demo();
    var b = Demo("다른가상");
    b.ChatRoomName = a.ChatRoomName;
    var db = new AppDatabase
    {
        Students = [a, b]
    };
    BatchItem B(Student s) => new()
    {
        StudentId = s.Id,
        StudentName = s.Name,
        ChatRoomName = s.ChatRoomName,
        Message = "연습"
    };
    Check(BatchValidator.Validate(db, [B(a), B(b)], true).Count > 0, "Duplicate room allowed");
    var i = B(a);
    Check(BatchValidator.Validate(db, [i], true).Count == 0, "Valid batch rejected");
    i.StudentName = "다른이름";
    Check(BatchValidator.Validate(db, [i], false).Count > 0, "Wrong name allowed");
    i = B(a);
    i.CommonMessages = Enumerable.Repeat("공통", 6).ToList();
    Check(BatchValidator.Validate(db, [i], false).Count > 0, "Seven messages allowed");
    i = B(a);
    i.Status = "확인 필요";
    Check(BatchValidator.Validate(db, [i], false).Count > 0, "Uncertain retry allowed");
});
// Regression: a collision late in an archive must not leave earlier students appended.
Test("archive import failure is transactional", () =>
{
    var a = Demo("첫가상");
    var b = Demo("둘가상");
    var source = new AppDatabase
    {
        Students = [a, b],
        Classes = [new()
        {
            Members = [new()
            {
                StudentId = a.Id
            }, new()
            {
                StudentId = b.Id
            }

            ]
        }

        ]
    };
    var path = Path.Combine(temp, "collision.json");
    ArchiveService.ExportClasses(path, source);
    var collision = Demo("충돌가상");
    collision.Id = b.Id;
    var target = new AppDatabase
    {
        Students = [collision]
    };
    Reject(() => ArchiveService.ImportClasses(path, target));
    Check(target.Students.Count == 1 && target.Classes.Count == 0, "Failed import partially mutated database");
});
Test("dry run permits room-only verification while live send blocks blank", () =>
{
    var s = Demo();
    var db = new AppDatabase
    {
        Students = [s]
    };
    var item = new BatchItem
    {
        StudentId = s.Id,
        StudentName = s.Name,
        ChatRoomName = s.ChatRoomName
    };
    Check(BatchValidator.Validate(db, [item], true).Count == 0, "Dry run requires unnecessary message");
    Check(BatchValidator.Validate(db, [item], false).Count > 0, "Live blank send allowed");
});
Test("student merge rejects late collisions without partial additions", () =>
{
    var existing = Demo("충돌가상");
    var db = new AppDatabase
    {
        Students = [existing]
    };
    var first = Demo("새가상");
    var collision = Demo("다른가상");
    collision.Id = existing.Id;
    Reject(() => StudentFiles.Merge(db, [first, collision]));
    Check(db.Students.Count == 1, "Partial roster import changed database");
});
Test("backup missing UUID fails instead of generating student identity", () =>
{
    var path = Path.Combine(temp, "missing-id.json");
    File.WriteAllText(path, "{\"schemaVersion\":8,\"students\":[{\"name\":\"가상\",\"school\":\"가상고\",\"admissionYear\":27}],\"classes\":[],\"presets\":[],\"logs\":[]}");
    Reject(() => new DatabaseStore(path).Load());
});
Test("dry run permits diagnostics for protected status without permitting actual retry", () =>
{
    var s = Demo();
    var db = new AppDatabase
    {
        Students = [s]
    };
    var i = new BatchItem
    {
        StudentId = s.Id,
        StudentName = s.Name,
        ChatRoomName = s.ChatRoomName,
        Message = "연습",
        Status = "확인 필요"
    };
    Check(BatchValidator.Validate(db, [i], true).Count == 0, "Protected state blocked diagnostics");
    Check(BatchValidator.Validate(db, [i], false).Count > 0, "Protected state allowed live retry");
});
Test("batch rejects spreadsheet errors and duplicate stable endpoints", () =>
{
    var a = Demo();
    var b = Demo("둘가상");
    a.ChatId = b.ChatId = "same-stable-id";
    var db = new AppDatabase
    {
        Students = [a, b]
    };
    BatchItem B(Student x) => new()
    {
        StudentId = x.Id,
        StudentName = x.Name,
        ChatRoomName = x.ChatRoomName,
        ChatId = x.ChatId,
        Message = "연습"
    };
    Check(BatchValidator.Validate(db, [B(a), B(b)], false).Count > 0, "Duplicate chatID permitted");
    var i = B(a);
    i.Message = "수식 결과 #REF!";
    Check(BatchValidator.Validate(db, [i], false).Count > 0, "Spreadsheet error sent");
    i.Message = "정상";
    i.Status = "실패";
    Check(BatchValidator.Validate(db, [i], false).Count == 0, "Definite pre-delivery failure cannot retry");
});
CompatibilityTests.Run(Test, Check, Reject, temp);
Directory.Delete(temp, true);
Console.WriteLine($"{passed} passed, {failed} failed");
return failed == 0 ? 0 : 1;
