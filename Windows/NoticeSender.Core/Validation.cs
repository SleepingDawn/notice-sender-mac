using System.Text;
using System.Text.RegularExpressions;

namespace NoticeSender.Core;
public static class AttachmentScanner
{
    static string Normal(string s) => string.Concat(s.Normalize(NormalizationForm.FormC).ToLowerInvariant().Where(c => !char.IsWhiteSpace(c)));
    public static Dictionary<Guid, List<string>> Scan(string root, IEnumerable<Student> students)
    {
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
            throw new DirectoryNotFoundException("입력한 주소가 접근 가능한 폴더가 아닙니다.");
        root = Path.GetFullPath(root);
        if ((File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0)
            throw new IOException("심볼릭 링크 폴더는 사용할 수 없습니다.");
        var roster = students.ToList();
        var result = roster.ToDictionary(s => s.Id, _ => new List<string>());
        void Walk(string folder, int depth)
        {
            foreach (var p in Directory.EnumerateFileSystemEntries(folder))
            {
                var a = File.GetAttributes(p);
                if ((a & (FileAttributes.Hidden | FileAttributes.ReparsePoint)) != 0 || Path.GetFileName(p).StartsWith('.'))
                    continue;
                if ((a & FileAttributes.Directory) != 0)
                {
                    if (depth + 1 < 3)
                        Walk(p, depth + 1);
                    continue;
                }

                var name = Normal(Path.GetFileNameWithoutExtension(p));
                var years = Regex.Matches(name, @"\d+").Select(m => m.Value).ToHashSet();
                var matches = roster.Where(s => !string.IsNullOrWhiteSpace(s.Name) && !string.IsNullOrWhiteSpace(s.School) && name.Contains(Normal(s.Name)) && name.Contains(Normal(s.School)) && (years.Contains(s.AdmissionYear.ToString("D2")) || years.Contains("20" + s.AdmissionYear.ToString("D2")))).ToList();
                foreach (var s in matches)
                {
                    var nm = Normal(s.Name);
                    if (matches.Any(o => o.Id != s.Id && Normal(o.Name).Length > nm.Length && Normal(o.Name).StartsWith(nm)))
                        continue;
                    result[s.Id].Add(Path.GetFullPath(p));
                }
            }
        }

        Walk(root, 0);
        foreach (var list in result.Values)
            list.Sort(StringComparer.CurrentCulture);
        return result;
    }
}

public static class BatchValidator
{
    public static List<string> Validate(AppDatabase db, IReadOnlyList<BatchItem> items, bool dryRun)
    {
        List<string> errors = [];
        var active = items.Where(x => x.Status != "제외").ToList();
        if (active.Count == 0)
            errors.Add("전송할 학생이 없습니다.");
        var ids = new HashSet<Guid>();
        var rooms = new HashSet<string>(StringComparer.Ordinal);
        var chatIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in active)
        {
            var prefix = item.StudentName + ": ";
            var student = db.Students.SingleOrDefault(x => x.Id == item.StudentId);
            if (student == null || !student.IsActive)
                errors.Add(prefix + "활성 학생을 찾을 수 없습니다.");
            else
            {
                if (student.Name != item.StudentName)
                    errors.Add(prefix + "학생 이름이 일치하지 않습니다.");
                if (student.ChatRoomName != item.ChatRoomName || student.ChatId != item.ChatId)
                    errors.Add(prefix + "DB의 톡방 정보와 다릅니다.");
                if (student.AdmissionYear is < 0 or > 99)
                    errors.Add(prefix + "학번 범위는 00~99입니다.");
            }

            if (!ids.Add(item.StudentId))
                errors.Add(prefix + "학생이 중복됩니다.");
            if (string.IsNullOrWhiteSpace(item.ChatRoomName) || !rooms.Add(item.ChatRoomName.Trim()))
                errors.Add(prefix + "톡방 이름이 비어 있거나 중복됩니다.");
            if (!string.IsNullOrWhiteSpace(item.ChatId) && !chatIds.Add(item.ChatId.Trim()))
                errors.Add(prefix + "톡방 식별자가 중복됩니다.");
            if (!dryRun && item.Status is not ("준비" or "드라이런 확인 완료" or "실패"))
                errors.Add(prefix + "이전 전송 상태를 확인하고 새 미리보기를 만들어주세요.");
            if (!dryRun && item.AllMessages.Any(message => new[] { "#REF!", "#DIV/0!", "#VALUE!", "#N/A", "#NAME?", "#NUM!", "#NULL!", "#ERROR!" }.Any(error => message.Contains(error, StringComparison.OrdinalIgnoreCase))))
                errors.Add(prefix + "스프레드시트 수식 오류가 포함되어 있습니다.");
            if (!dryRun && item.AllMessages.Count == 0 && item.AttachmentPaths.Count == 0)
                errors.Add(prefix + "메시지와 첨부파일이 비어 있습니다.");
            if (item.AllMessages.Count > 6)
                errors.Add(prefix + "메시지는 학생별 최대 6개입니다.");
            if (item.AttachmentPaths.Distinct(StringComparer.OrdinalIgnoreCase).Count() != item.AttachmentPaths.Count)
                errors.Add(prefix + "첨부파일이 중복됩니다.");
            foreach (var path in item.AttachmentPaths)
            {
                if (!Path.IsPathFullyQualified(path) || !File.Exists(path))
                {
                    errors.Add(prefix + "첨부파일을 찾을 수 없습니다: " + path);
                    continue;
                }

                var a = File.GetAttributes(path);
                if ((a & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0)
                    errors.Add(prefix + "첨부파일은 일반 파일이어야 합니다.");
            }
        }

        return errors;
    }
}
