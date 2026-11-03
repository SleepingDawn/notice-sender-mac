using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using NoticeSender.Core;

namespace NoticeSender.Windows;

public sealed partial class MainWindow
{
    private readonly ComboBox lessonClass = new() { MinWidth = 190, DisplayMemberPath = "Name", Margin = new Thickness(0, 0, 10, 8) };
    private readonly ComboBox lessonPreset = new() { MinWidth = 230, DisplayMemberPath = "Name", Margin = new Thickness(0, 0, 10, 8) };
    private readonly ListBox classList = new() { DisplayMemberPath = "Name", MinWidth = 190 };
    private readonly ListBox presetList = new() { DisplayMemberPath = "Name", MinWidth = 190 };
    private readonly DataGrid logsGrid = Ui.Table();
    private readonly ObservableCollection<MemberRow> memberRows = new();

    private void ImportWholeWorkbook()
    {
        var path = Ui.Open("Excel 통합 문서|*.xlsx"); if (path == null) return;
        SaveDatabase();
        var candidate = Clone(database);
        string result = StudentFiles.ImportWorkbook(path, candidate);
        DatabaseJson.Validate(candidate);
        if (!demo) store!.Save(candidate);
        ReplaceDatabase(candidate);
        Notice(result); System.Windows.MessageBox.Show(this, result, "통합 문서 가져오기 완료");
    }
    private UIElement BuildStudents()
    {
        var search = Ui.Box(); search.Width = 290;
        var view = new ListCollectionView(students);
        search.TextChanged += (_, _) => { string q = search.Text.Trim(); view.Filter = o => o is Student s && (s.Name + " " + s.Nickname + " " + s.School + " " + s.ChatRoomName).Contains(q, StringComparison.OrdinalIgnoreCase); };
        var grid = Ui.Table(view); tables.Add(grid);
        Ui.Col(grid, "이름", "Name", 155); Ui.Col(grid, "호칭", "Nickname", 100); Ui.Col(grid, "학교", "School", 120); Ui.Col(grid, "학번 (00~99)", "AdmissionYear", 110); Ui.Col(grid, "채팅방 이름 (정확히)", "ChatRoomName", 230); Ui.Col(grid, "채팅 ID (선택)", "ChatId", 160); Ui.CheckCol(grid, "활성", "IsActive");
        var top = Ui.Stack(Ui.Text("학생 DB", 25), Ui.Text("이름·호칭·학교·학번과 카카오톡의 정확한 채팅방 이름을 관리합니다. 셀을 두 번 눌러 수정하고 저장하세요."),
            Ui.Actions(Ui.Button("학생 추가", () => Guard(() => { var s = new Student { Id = Guid.NewGuid(), Name = "새 학생", AdmissionYear = DateTime.Today.Year % 100, IsActive = true }; students.Add(s); grid.SelectedItem = s; grid.ScrollIntoView(s); }), true),
            Ui.Button("선택 학생 삭제", () => Guard(() => {
                var selected = grid.SelectedCells.Select(c => c.Item).OfType<Student>().Distinct().ToList();
                if (selected.Count == 0 || !Ui.Confirm(this, $"선택한 학생 {selected.Count}명을 학생 DB와 모든 반 명단에서 삭제할까요? 발송 기록은 유지됩니다.")) return;
                foreach (var s in selected) { students.Remove(s); foreach (var c in classes) c.Members.RemoveAll(m => m.StudentId == s.Id); }
                SaveDatabase();
            })),
            Ui.Button("XLSX / CSV / TSV 가져오기", () => Guard(() => {
                string? path = Ui.Open("학생 명단|*.xlsx;*.csv;*.tsv|모든 파일|*.*"); if (path == null) return;
                SaveDatabase(); int count = StudentFiles.Merge(database, StudentFiles.Read(path)); RefreshCollections(); RefreshSources(); SaveDatabase(); Notice($"학생 {count}명 가져오기를 반영했습니다.");
            })), Ui.Button("CSV 내보내기", () => Guard(() => { SaveDatabase(); var path = Ui.Save("CSV|*.csv", "학생명단.csv"); if (path != null) StudentFiles.WriteCsv(path, students); })),
            Ui.Button("저장", () => Guard(SaveDatabase)), Ui.Button("전체 백업", () => Guard(ExportBackup)), Ui.Button("복원", () => Guard(RestoreBackup))), Ui.Field("학생 검색", search));
        var panel = new DockPanel(); DockPanel.SetDock(top, Dock.Top); panel.Children.Add(top); panel.Children.Add(grid); return panel;
    }
    private UIElement BuildClasses()
    {
        classList.ItemsSource = classes;
        var members = Ui.Table(memberRows); tables.Add(members);
        Ui.CheckCol(members, "소속", "Included"); Ui.Col(members, "이름", "Student.Name", 145, true); Ui.Col(members, "학교", "Student.School", 100, true); Ui.Col(members, "학번", "Student.AdmissionYear", 70, true); Ui.Col(members, "반 내 호칭 (선택)", "NicknameOverride", 170);
        var editor = new StackPanel();
        classList.SelectionChanged += (_, _) => Guard(() => {
            editor.Children.Clear(); memberRows.Clear();
            if (classList.SelectedItem is not ClassGroup group) return;
            var name = BoundBox(group, "Name"); var school = BoundBox(group, "School"); var year = BoundBox(group, "AdmissionYear");
            var preset = new ComboBox { ItemsSource = presets, DisplayMemberPath = "Name", SelectedValuePath = "Id", SelectedValue = group.DefaultPresetId, MinHeight = 34 };
            editor.Children.Add(Ui.Text("반 정보", 20)); editor.Children.Add(Ui.Field("이름", name)); editor.Children.Add(Ui.Field("학교", school)); editor.Children.Add(Ui.Field("학번 (00~99)", year)); editor.Children.Add(Ui.Field("기본 Preset", preset));
            foreach (var s in students) { var m = group.Members.FirstOrDefault(m => m.StudentId == s.Id); memberRows.Add(new MemberRow { Student = s, Included = m != null, NicknameOverride = m?.NicknameOverride }); }
            editor.Children.Add(Ui.Actions(Ui.Button("전체 학생 선택", () => { foreach (var m in memberRows) m.Included = true; members.Items.Refresh(); }), Ui.Button("전체 해제", () => { foreach (var m in memberRows) m.Included = false; members.Items.Refresh(); }), Ui.Button("반 정보 / 명단 저장", () => Guard(() => {
                CommitTables(); group.DefaultPresetId = preset.SelectedValue is Guid id ? id : null;
                group.Members = memberRows.Where(m => m.Included).Select(m => new ClassMember { StudentId = m.Student.Id, NicknameOverride = string.IsNullOrWhiteSpace(m.NicknameOverride) ? null : m.NicknameOverride }).ToList();
                if (string.IsNullOrWhiteSpace(group.Name)) throw new InvalidOperationException("반 이름을 입력하세요.");
                SaveDatabase(); classList.Items.Refresh(); lessonClass.Items.Refresh();
            }), true)));
        });
        var left = Ui.Stack(classList, Ui.Actions(Ui.Button("새 반", () => {
            var c = new ClassGroup { Id = Guid.NewGuid(), Name = "새 반", AdmissionYear = DateTime.Today.Year % 100, DefaultPresetId = presets.FirstOrDefault()?.Id }; classes.Add(c); classList.SelectedItem = c;
        }, true), Ui.Button("삭제", () => Guard(() => {
            if (classList.SelectedItem is ClassGroup c && Ui.Confirm(this, $"‘{c.Name}’ 반을 삭제할까요? 학생 DB는 유지됩니다.")) { classes.Remove(c); SaveDatabase(); }
        }))));
        classList.Height = 380;
        var right = new DockPanel { Margin = new Thickness(20, 0, 0, 0) }; DockPanel.SetDock(editor, Dock.Top); right.Children.Add(editor); right.Children.Add(members);
        var columns = new Grid(); columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220) }); columns.ColumnDefinitions.Add(new ColumnDefinition()); columns.Children.Add(left); Grid.SetColumn(right, 1); columns.Children.Add(right);
        var top = Ui.Stack(Ui.Text("반 관리", 25), Ui.Text("학생 소속과 반별 호칭을 편집한 뒤 ‘반 정보 / 명단 저장’을 누르세요."), Ui.Actions(
            Ui.Button("전체 반 JSON 내보내기", () => Guard(() => { SaveDatabase(); var p = Ui.Save("반 JSON|*.json", "공지발송-반.json"); if (p != null) ArchiveService.ExportClasses(p, database); })),
            Ui.Button("반 JSON 가져오기", () => Guard(() => { var p = Ui.Open("반 JSON|*.json"); if (p == null) return; SaveDatabase(); string result = ArchiveService.ImportClasses(p, database); RefreshCollections(); RefreshSources(); SaveDatabase(); Notice(result); }))));
        var panel = new DockPanel(); DockPanel.SetDock(top, Dock.Top); panel.Children.Add(top); panel.Children.Add(columns); if (classes.Count > 0) classList.SelectedIndex = 0; return panel;
    }
    private UIElement BuildPresets()
    {
        presetList.ItemsSource = presets; presetList.Height = 460;
        var editor = new StackPanel();
        presetList.SelectionChanged += (_, _) => {
            editor.Children.Clear(); if (presetList.SelectedItem is not MessagePreset p) return;
            editor.Children.Add(Ui.Text("Preset 문구 편집", 22)); editor.Children.Add(Ui.Text("Windows에서는 템플릿을 직접 편집합니다. {{nickname}}, {{date}}, {{progress}} 등의 변수는 생성할 때 치환됩니다. 지원하지 않는 변수는 저장 검증에서 표시합니다."));
            foreach (var field in new[] { ("이름", "Name"), ("종류 (직접입력 / 일반 수업 / 모의고사)", "Kind"), ("버전", "Version"), ("선생님", "TeacherName"), ("학원", "AcademyName"), ("높은 별점 기준 (0~1)", "HighThreshold"), ("중간 별점 기준 (0~1)", "MiddleThreshold"), ("모의고사 회차 수 (1~3)", "MockExamCount") }) editor.Children.Add(Ui.Field(field.Item1, BoundBox(p, field.Item2)));
            foreach (var field in new[] { ("출석 문구", "PresentTemplate"), ("동영상 문구", "VideoTemplate"), ("결석 문구", "AbsentTemplate"), ("말미 공통 문구", "FooterTemplate") }) editor.Children.Add(Ui.Field(field.Item1, BoundBox(p, field.Item2, true)));
            editor.Children.Add(Ui.Actions(Ui.Button("검증 후 저장", () => Guard(() => { KeyboardFocus(); var errors = TemplateEngine.Validate(p); if (errors.Count > 0) throw new InvalidOperationException(string.Join("\n", errors)); SaveDatabase(); presetList.Items.Refresh(); lessonPreset.Items.Refresh(); RebuildLessonColumns(); }), true),
                Ui.Button("새 버전 복제", () => Guard(() => { KeyboardFocus(); var clone = Clone(p); clone.Id = Guid.NewGuid(); clone.Version++; clone.Name = p.Name + " v" + clone.Version; presets.Add(clone); presetList.SelectedItem = clone; SaveDatabase(); }))));
        };
        var actions = Ui.Actions(Ui.Button("새 Preset", () => { var p = Clone(presets.FirstOrDefault() ?? DefaultPresets.Create().First()); p.Id = Guid.NewGuid(); p.Name = "새 Preset"; p.Version = 1; presets.Add(p); presetList.SelectedItem = p; }, true),
            Ui.Button("삭제", () => Guard(() => {
                if (presetList.SelectedItem is not MessagePreset p || !Ui.Confirm(this, $"‘{p.Name}’ Preset을 삭제할까요?")) return;
                if (presets.Count <= 1) throw new InvalidOperationException("Preset은 하나 이상 유지하세요."); presets.Remove(p); foreach (var c in classes.Where(c => c.DefaultPresetId == p.Id)) c.DefaultPresetId = presets[0].Id; SaveDatabase();
            })));
        var left = Ui.Stack(presetList, actions);
        var columns = new Grid(); columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(230) }); columns.ColumnDefinitions.Add(new ColumnDefinition()); columns.Children.Add(left);
        var scroll = Ui.Scroll(editor); scroll.Margin = new Thickness(22, 0, 0, 0); Grid.SetColumn(scroll, 1); columns.Children.Add(scroll);
        var top = Ui.Stack(Ui.Text("문구 Preset", 25), Ui.Actions(Ui.Button("선택 Preset JSON 내보내기", () => Guard(() => { if (presetList.SelectedItem is MessagePreset p) { KeyboardFocus(); var path = Ui.Save("Preset JSON|*.json", p.Name + ".json"); if (path != null) ArchiveService.ExportPreset(path, p); } })),
            Ui.Button("Preset JSON 가져오기", () => Guard(() => { var path = Ui.Open("Preset JSON|*.json"); if (path == null) return; var p = ArchiveService.ImportPreset(path); if (presets.Any(x => x.Id == p.Id)) p.Id = Guid.NewGuid(); presets.Add(p); presetList.SelectedItem = p; SaveDatabase(); }))));
        var panel = new DockPanel(); DockPanel.SetDock(top, Dock.Top); panel.Children.Add(top); panel.Children.Add(columns); if (presets.Count > 0) presetList.SelectedIndex = 0; return panel;
    }
    private static TextBox BoundBox(object source, string property, bool multiline = false)
    {
        var box = Ui.Box("", multiline); box.SetBinding(TextBox.TextProperty, new Binding(property) { Source = source, Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.LostFocus, ValidatesOnExceptions = true }); return box;
    }
    private void KeyboardFocus() { System.Windows.Input.Keyboard.ClearFocus(); Focus(); }
    private void RefreshSources() { classList.Items.Refresh(); presetList.Items.Refresh(); lessonClass.Items.Refresh(); lessonPreset.Items.Refresh(); }
    private UIElement BuildLogs()
    {
        logsGrid.IsReadOnly = true;
        Ui.Col(logsGrid, "일시", "SentAt", 190, true); Ui.Col(logsGrid, "학생", "StudentName", 130, true); Ui.Col(logsGrid, "채팅방", "ChatRoomName", 190, true); Ui.Col(logsGrid, "결과", "Result", 140, true); Ui.Col(logsGrid, "상세", "Detail", 320, true);
        var detail = Ui.Box("발송 기록을 선택하면 당시 메시지와 상세 정보를 확인합니다.", true); detail.IsReadOnly = true; detail.Height = 180;
        logsGrid.SelectionChanged += (_, _) => { if (logsGrid.SelectedItem is SendLog l) detail.Text = $"{l.SentAt:yyyy-MM-dd HH:mm:ss} · {l.StudentName} · {l.Result}\n방: {l.ChatRoomName}\n배치: {l.BatchId}\nSHA256: {l.MessageSHA256}\n{l.Detail}\n\n" + string.Join("\n\n────\n\n", l.Messages ?? new List<string>()); };
        var top = Ui.Stack(Ui.Text("발송 기록", 25), Ui.Text("‘전송 요청 완료’는 상대방 수신 확인과 다릅니다. ‘확인 필요’ 항목은 채팅방을 직접 확인한 뒤 처리하고 자동으로 재전송하지 마세요."), Ui.Actions(Ui.Button("새로고침", RefreshLogs), Ui.Button("CSV 내보내기", () => Guard(() => {
            var path = Ui.Save("발송 기록 CSV|*.csv", "발송기록-" + DateTime.Now.ToString("yyyyMMdd") + ".csv"); if (path == null) return;
            static string Q(string? v) => "\"" + (v ?? "").Replace("\"", "\"\"") + "\"";
            var csv = new StringBuilder("일시,학생,채팅방,결과,상세,메시지SHA256,메시지\r\n"); foreach (var l in database.Logs) csv.AppendLine(string.Join(",", new[] { l.SentAt.ToString("O"), l.StudentName, l.ChatRoomName, l.Result, l.Detail, l.MessageSHA256, string.Join("\n\n", l.Messages ?? new List<string>()) }.Select(Q))); File.WriteAllText(path, csv.ToString(), new UTF8Encoding(true)); Notice("발송 기록 CSV를 내보냈습니다.");
        }))));
        var panel = new DockPanel(); DockPanel.SetDock(top, Dock.Top); panel.Children.Add(top); DockPanel.SetDock(detail, Dock.Bottom); panel.Children.Add(detail); panel.Children.Add(logsGrid); RefreshLogs(); return panel;
    }
    private void RefreshLogs() => logsGrid.ItemsSource = database.Logs.OrderByDescending(l => l.SentAt).ToList();
    private sealed class MemberRow
    {
        public Student Student { get; set; } = new();
        public bool Included { get; set; }
        public string? NicknameOverride { get; set; }
    }
}
