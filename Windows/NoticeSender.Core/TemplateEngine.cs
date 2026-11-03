using System.Globalization;
using System.Text.RegularExpressions;

namespace NoticeSender.Core;
public static class TemplateEngine
{
    public const string DefaultFooter = "문의사항 있으시면 해당 카톡으로 언제든 연락주세요. 감사합니다.";
    static readonly Regex Token = new(@"\{\{([^{}]+)\}\}", RegexOptions.Compiled);
    static readonly HashSet<string> Identity = ["academy", "teacher", "student_name", "nickname", "date", "attendance", "notice"];
    static readonly HashSet<string> Shared = ["attitude_stars", "attitude_comment", "progress", "assignment"];
    static readonly HashSet<string> Regular = ["homework_stars", "test_stars", "homework_score", "homework_max", "homework_percent", "test_score", "test_max", "test_percent", "homework_comment", "test_comment", "homework_comment_line", "test_comment_line", "exam_unit"];
    static HashSet<string> Allowed(string category)
    {
        var a = new HashSet<string>(Identity);
        if (category == "직접입력")
            return a;
        a.UnionWith(Shared);
        if (category == "일반 수업")
            a.UnionWith(Regular);
        else
        {
            a.UnionWith(["mock_exam_name", "exam_sections", "exam_summary_sections"]);
            for (int i = 1; i <= 3; i++)
                foreach (var s in new[]
                {
                    "title",
                    "score",
                    "max",
                    "percent",
                    "average",
                    "highest",
                    "rank",
                    "attendees",
                    "comment",
                    "comment_line"
                }

                )
                    a.Add($"mock{i}_{s}");
        }

        return a;
    }

    public static List<string> Validate(MessagePreset p)
    {
        List<string> e = [];
        if (!double.IsFinite(p.HighThreshold) || !double.IsFinite(p.MiddleThreshold) || p.MiddleThreshold < 0 || p.HighThreshold > 1 || p.MiddleThreshold >= p.HighThreshold)
            e.Add("별점 기준은 0~1 사이이며 중간 기준이 높은 기준보다 작아야 합니다.");
        if (p.Kind is not ("직접입력" or "첫 수업" or "시험만" or "숙제만" or "일반 수업" or "모의고사"))
            e.Add("지원하지 않는 Preset 종류입니다.");
        var allowed = Allowed(p.Category);
        foreach (var(name, t)in new[]
        {
            ("출석", p.PresentTemplate),
            ("동영상", p.VideoTemplate),
            ("공지문 말미", p.FooterTemplate ?? DefaultFooter)
        }

        )
        {
            if (name != "공지문 말미" && string.IsNullOrWhiteSpace(t))
                e.Add(name + " 문구가 비어 있습니다.");
            var rest = Token.Replace(t, "");
            if (rest.Contains("{{") || rest.Contains("}}"))
                e.Add(name + " 문구의 변수 표시가 잘못되었습니다.");
            foreach (Match m in Token.Matches(t))
                if (!allowed.Contains(m.Groups[1].Value))
                    e.Add("이 Preset에서 사용할 수 없는 변수입니다: " + m.Value);
        }

        return e;
    }

    static string N(double? n) => n.HasValue ? n.Value.ToString(n == Math.Round(n.Value) ? "0" : "0.00", CultureInfo.InvariantCulture) : "";
    static string Percent(double? s, double? m, string missing) => s.HasValue && m > 0 ? (s.Value / m.Value * 100).ToString("F2", CultureInfo.InvariantCulture) + "%" : missing;
    static string Stars(double? s, double? m, MessagePreset p, string missing) => s.HasValue && m > 0 ? (s / m >= p.HighThreshold ? "★★★" : s / m >= p.MiddleThreshold ? "★★☆" : "★☆☆") : missing;
    static string Line(string s) => string.IsNullOrWhiteSpace(s) ? "" : "\n- " + s.Trim() + "\n";
    public static List<string> ValidateInput(LessonInput i)
    {
        var e = new List<string>();
        if (i.Attendance.Trim()is not ("출석" or "동영상"))
            e.Add("출결은 출석 또는 동영상만 사용할 수 있습니다.");
        void Score(double? s, double? m)
        {
            if (s.HasValue && (!double.IsFinite(s.Value) || s < 0) || m.HasValue && (!double.IsFinite(m.Value) || m <= 0) || s.HasValue && (!m.HasValue || s > m))
                e.Add("점수는 0 이상 만점 이하이며 만점은 양수여야 합니다.");
        }

        Score(i.HomeworkScore, i.HomeworkMaximum);
        Score(i.TestScore, i.TestMaximum);
        foreach (var x in i.Exams)
        {
            Score(x.Score, x.Maximum);
            if (x.Average.HasValue && (!double.IsFinite(x.Average.Value) || x.Average < 0) || x.Highest.HasValue && (!double.IsFinite(x.Highest.Value) || x.Highest < 0) || x.Rank is <= 0 || x.Attendees is < 0 || x.Rank > x.Attendees)
                e.Add("모의고사 평균·최고점·등수·응시인원을 확인해주세요.");
        }

        return e;
    }

    public static string Render(MessagePreset p, LessonInput i)
    {
        var errors = Validate(p);
        errors.AddRange(ValidateInput(i));
        if (errors.Count > 0)
            throw new InvalidDataException(string.Join("\n", errors));
        const string hm = "과제를 제출하지 않았습니다.", tm = "시험 미응시로 점수 없습니다.";
        Dictionary<string, string> v = new()
        {
            ["academy"] = p.AcademyName,
            ["teacher"] = p.TeacherName,
            ["student_name"] = i.StudentName,
            ["nickname"] = i.Nickname,
            ["date"] = i.Date,
            ["attendance"] = i.Attendance.Trim(),
            ["notice"] = i.Notice,
            ["progress"] = i.Progress,
            ["assignment"] = i.Assignment,
            ["exam_unit"] = i.ExamUnit,
            ["mock_exam_name"] = i.ExamUnit,
            ["attitude_stars"] = i.Attitude.StartsWith('3') ? "★★★" : "★★☆",
            ["attitude_comment"] = i.Attitude is "3" or "3.0" ? "수업 집중력이 좋습니다." : i.Attitude == "2-핸드폰" ? "수업시간에 집중하지 않고 핸드폰을 사용했습니다." : "수업 시간에 집중이 흐트러진 모습이 있었습니다.",
            ["homework_stars"] = Stars(i.HomeworkScore, i.HomeworkMaximum, p, hm),
            ["test_stars"] = Stars(i.TestScore, i.TestMaximum, p, tm),
            ["homework_score"] = N(i.HomeworkScore),
            ["homework_max"] = N(i.HomeworkMaximum),
            ["homework_percent"] = Percent(i.HomeworkScore, i.HomeworkMaximum, hm),
            ["test_score"] = N(i.TestScore),
            ["test_max"] = N(i.TestMaximum),
            ["test_percent"] = Percent(i.TestScore, i.TestMaximum, tm),
            ["homework_comment"] = i.HomeworkComment,
            ["test_comment"] = i.TestComment,
            ["homework_comment_line"] = Line(i.HomeworkComment),
            ["test_comment_line"] = Line(i.TestComment)
        };
        for (int n = 1; n <= 3; n++)
        {
            var x = i.Exams.ElementAtOrDefault(n - 1) ?? new();
            var k = $"mock{n}_";
            v[k + "title"] = x.Title;
            v[k + "score"] = N(x.Score);
            v[k + "max"] = N(x.Maximum);
            v[k + "percent"] = Percent(x.Score, x.Maximum, "");
            v[k + "average"] = N(x.Average);
            v[k + "highest"] = N(x.Highest);
            v[k + "rank"] = x.Rank?.ToString() ?? "";
            v[k + "attendees"] = x.Attendees?.ToString() ?? "";
            v[k + "comment"] = x.Comment;
            v[k + "comment_line"] = Line(x.Comment);
        }

        string Sections(bool individual) => string.Join("\n\n", i.Exams.Take(Math.Clamp(p.MockExamCount ?? 3, 1, 3)).Select((x, n) =>
        {
            List<string> l = [$"{n + 3} {x.Title}"];
            if (x.Attendees.HasValue)
                l.Add($"응시인원 : {x.Attendees}명");
            if (x.Average.HasValue)
                l.Add("평균 : " + N(x.Average));
            if (x.Highest.HasValue)
                l.Add("최고점 : " + N(x.Highest));
            if (individual)
            {
                if (x.Score.HasValue)
                    l.Add("학생 점수 : " + N(x.Score) + "점");
                if (x.Rank.HasValue)
                    l.Add($"학생 등수 : {x.Rank}등");
                if (x.Comment.Length > 0)
                    l.Add("\n- " + x.Comment);
            }

            return string.Join("\n", l);
        }));
        v["exam_sections"] = Sections(true);
        v["exam_summary_sections"] = Sections(false);
        string Expand(string t) => Token.Replace(t, m => v[m.Groups[1].Value]).Trim();
        return string.Join("\n\n", new[] { Expand(i.Attendance.Trim() == "출석" ? p.PresentTemplate : p.VideoTemplate), Expand(p.FooterTemplate ?? DefaultFooter) }.Where(x => x.Length > 0));
    }
}
