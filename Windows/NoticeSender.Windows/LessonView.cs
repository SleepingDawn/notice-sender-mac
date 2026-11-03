using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Data;
using NoticeSender.Automation;
using NoticeSender.Core;

namespace NoticeSender.Windows;

public sealed partial class MainWindow
{
    private readonly ObservableCollection<LessonRow> rows = new();
    private readonly ObservableCollection<LessonRow> sheetRows = new();
    private readonly DataGrid lessonGrid = Ui.Table();
    private readonly DataGrid resultGrid = Ui.Table();
    private readonly DatePicker lessonDate = new() { SelectedDate = DateTime.Today, MinWidth = 150, Margin = new Thickness(0, 0, 10, 8) };
    private readonly TextBox progressBox = Ui.Box();
    private readonly TextBox assignmentBox = Ui.Box();
    private readonly TextBox examUnitBox = Ui.Box();
    private readonly TextBox noticeBox = Ui.Box("", true);
    private readonly TextBox folderBox = Ui.Box();
    private readonly TextBox[] commonBoxes = Enumerable.Range(0, 5).Select(_ => Ui.Box("", true)).ToArray();
    private readonly TextBox[] examTitles = Enumerable.Range(1, 3).Select(i => Ui.Box("모의고사 " + i)).ToArray();
    private readonly StackPanel examTitlePanel = new();
    private readonly TextBox selectedMessageEditor = Ui.Box("", true);
    private readonly TextBlock selectedStudentLabel = Ui.Text("학생을 선택하면 최종 문구를 여러 줄로 편집할 수 있습니다.", 13);
    private readonly TextBlock selectedExamStats = Ui.Text("", 12);
    private readonly TextBlock layoutHint = Ui.Text("");
    private readonly TextBlock attachmentSummary = Ui.Text("첨부 폴더를 선택하면 최종 미리보기에서 학생별 매칭 결과를 확인할 수 있습니다.", 12);
    private readonly TextBlock sendProgress = Ui.Text("발송 대기", 14);
    private readonly ProgressBar progressBar = new() { Height = 7, Minimum = 0, Maximum = 1, Margin = new Thickness(0, 6, 0, 12) };
    private readonly Stack<UndoFrame> undo = new();
    private Guid deliverySessionId = Guid.NewGuid();
    private readonly Dictionary<string, Guid> preparedIds = new(StringComparer.Ordinal);
    // This history belongs to the database, not the editable lesson/undo stack.
    // A roster reload or old saved draft must not erase a possible delivery.
    private readonly Dictionary<string, Guid> preparedIdentityHistory = new(StringComparer.Ordinal);
    private string ScopePreparedContent(string content) => content.StartsWith("{", StringComparison.Ordinal) ? deliverySessionId.ToString("D") + "\n" + content : content;
    private bool HasProtectedSend(Guid batchId) => database.Logs.Any(l => l.BatchId == batchId && l.Result is "전송 요청 완료" or "확인 필요" or "전송 중");
    private LessonRow totals = new() { IsTotal = true, Included = false, Input = new LessonInput { StudentName = "만점 →", HomeworkScore = 100, TestScore = 100, Exams = new List<ExamInput> { new() { Score = 100 }, new() { Score = 100 }, new() { Score = 100 } } } };
    private CancellationTokenSource? sendCancellation;
    private Guid? loadedClassId;
    private StackPanel? lessonInputPanel;
    private MessagePreset? CurrentPreset => lessonPreset.SelectedItem as MessagePreset;
    private string Category => CurrentPreset?.Category ?? "일반 수업";
    private int ExamCount => Math.Clamp(CurrentPreset?.MockExamCount ?? 3, 1, 3);

    private UIElement BuildLesson()
    {
        lessonClass.ItemsSource = classes; lessonPreset.ItemsSource = presets;
        if (classes.Count > 0) lessonClass.SelectedIndex = 0;
        if (presets.Count > 0) lessonPreset.SelectedIndex = 0;
        lessonClass.SelectionChanged += (_, _) => { if (lessonClass.SelectedItem is ClassGroup c && c.DefaultPresetId is Guid id) lessonPreset.SelectedItem = presets.FirstOrDefault(p => p.Id == id) ?? presets.FirstOrDefault(); };
        lessonPreset.SelectionChanged += (_, _) => { if (lessonGrid.Columns.Count > 0) Guard(() => { CommitTables(); RebuildLessonColumns(); }); };
        lessonGrid.ItemsSource = sheetRows; lessonGrid.Height = 360; tables.Add(lessonGrid);
        lessonGrid.BeginningEdit += (_, _) => PushUndo();
        lessonGrid.PreviewKeyDown += (_, e) => {
            if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.V) { e.Handled = true; Guard(PasteLesson); }
            else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.Z) { e.Handled = true; Guard(UndoLesson); }
        };
        selectedMessageEditor.Height = 135; selectedMessageEditor.IsEnabled = false;
        selectedMessageEditor.GotKeyboardFocus += (_, _) => PushUndo();
        lessonGrid.SelectionChanged += (_, _) => UpdateSelectedMessage();
        resultGrid.Height = 200; resultGrid.IsReadOnly = true;
        Ui.Col(resultGrid, "학생", "StudentName", 150, true); Ui.Col(resultGrid, "채팅방", "ChatRoomName", 210, true); Ui.Col(resultGrid, "진행 상태", "Status", 180, true); Ui.Col(resultGrid, "개별 문구", "Message", 450, true);
        folderBox.Text = database.AttachmentRootPath ?? "";
        folderBox.MinWidth = 530;
        var folderActions = Ui.Actions(Ui.Button("첨부 폴더 선택", () => Guard(() => {
            var dialog = new Microsoft.Win32.OpenFolderDialog { Title = "학생별 첨부파일 폴더" }; if (dialog.ShowDialog(this) == true) folderBox.Text = dialog.FolderName;
        })), Ui.Button("파일 스캔", () => Guard(ScanFiles)), Ui.Button("주소 지우기", () => { folderBox.Clear(); attachmentSummary.Text = "첨부파일을 사용하지 않습니다."; }));
        var common = new StackPanel();
        for (int i = 0; i < 5; i++) common.Children.Add(Ui.Field("공통 메시지 " + (i + 1), commonBoxes[i]));
        var commonExpander = new Expander { Header = "공통 메시지 · 최대 5개 (개별 공지 뒤에 순서대로 발송)", Content = common, Margin = new Thickness(0, 10, 0, 15) };
        for (int i = 0; i < 3; i++) examTitlePanel.Children.Add(Ui.Field("모의고사 " + (i + 1) + " 제목", examTitles[i]));
        var shared = new Grid(); shared.ColumnDefinitions.Add(new ColumnDefinition()); shared.ColumnDefinitions.Add(new ColumnDefinition()); shared.ColumnDefinitions.Add(new ColumnDefinition());
        var f1 = Ui.Field("진도", progressBox); var f2 = Ui.Field("과제", assignmentBox); var f3 = Ui.Field("시험 범위 / 시험명", examUnitBox);
        foreach (var f in new[] { f1, f2, f3 }) { ((FrameworkElement)f).Margin = new Thickness(0, 0, 12, 0); shared.Children.Add(f); }
        Grid.SetColumn(f2, 1); Grid.SetColumn(f3, 2);
        lessonInputPanel = Ui.Stack(Ui.Text("수업 내용과 학생별 결과", 25), Ui.Actions(Ui.Field("반", lessonClass), Ui.Field("Preset", lessonPreset), Ui.Field("수업일", lessonDate), Ui.Button("명단 불러오기", () => Guard(() => {
            if (rows.Count > 0 && !Ui.Confirm(this, "현재 학생별 입력을 선택한 반 명단으로 새로 불러올까요? 입력 뒤로가기로 되돌릴 수 있습니다.")) return; LoadLesson();
        }), true)),
            Ui.Actions(Ui.Field("저장된 수업", savedLessonBox), Ui.Button("수업 저장", () => Guard(SaveLesson)), Ui.Button("저장된 수업 불러오기", () => Guard(LoadSavedLesson)), Ui.Button("저장된 수업 삭제", () => Guard(DeleteSavedLesson))),
            shared, Ui.Field("공지사항 (문구 변수 {{notice}})", noticeBox), examTitlePanel, layoutHint,
            Ui.Actions(Ui.Button("전체 발송 선택", () => { PushUndo(); foreach (var r in rows) r.Included = true; lessonGrid.Items.Refresh(); }), Ui.Button("전체 해제", () => { PushUndo(); foreach (var r in rows) r.Included = false; lessonGrid.Items.Refresh(); }),
                Ui.Button("선택 칸부터 붙여넣기 (Ctrl+V)", () => Guard(PasteLesson)), Ui.Button("입력 뒤로가기 (Ctrl+Z)", () => Guard(UndoLesson)), Ui.Button("문구 생성 / 갱신", () => Guard(GenerateMessages), true), Ui.Button("새 발송 회차", () => Guard(StartNewDeliveryRound))),
            lessonGrid, selectedStudentLabel, selectedMessageEditor, selectedExamStats, Ui.Actions(Ui.Button("문구 전부 지우기", () => { if (!Ui.Confirm(this, "학생별 문구와 공통 메시지를 지울까요? 성적과 출결은 유지됩니다.")) return; PushUndo(); foreach (var r in rows) r.Message = ""; foreach (var b in commonBoxes) b.Clear(); noticeBox.Clear(); lessonGrid.Items.Refresh(); })),
            commonExpander, Ui.Text("학생별 첨부파일", 19), folderBox, folderActions, attachmentSummary,
            Ui.Text("폴더 깊이 3까지 학교·학번·학생 이름이 모두 일치하는 파일을 연결합니다. 미리보기에서 정확한 파일 경로를 확인하세요.", 12));
        var actions = Ui.Actions(Ui.Button("최종 미리보기", () => Guard(() => Review(false, false))), Ui.Button("드라이런 · 대상만 확인", () => Guard(() => Review(true, true)), true), Ui.Button("검토 후 실제 발송", () => Guard(() => Review(false, true)), true));
        var cancel = Ui.Button("즉시 중지", () => { sendCancellation?.Cancel(); Notice("중지를 요청했습니다. 일부 발송 가능성이 있으면 ‘확인 필요’로 기록합니다."); });
        var panel = Ui.Stack(lessonInputPanel, actions, Ui.Actions(cancel), sendProgress, progressBar, resultGrid);
        RebuildLessonColumns(); return Ui.Scroll(panel);
    }
    private void StartNewDeliveryRound()
    {
        if (busy) throw new InvalidOperationException("진행 중인 발송이 끝난 뒤 새 회차를 시작하세요.");
        if (!Ui.Confirm(this, "새 발송 회차를 시작할까요? 이전 회차와 동일한 문구·첨부파일도 다시 발송할 수 있게 됩니다. 실제 발송은 최종 검토와 확인 후에만 진행됩니다.", "새 발송 회차 확인")) return;
        BeginNewDeliverySession(); SaveDatabase(); Notice("새 발송 회차를 준비했습니다. 수신자와 문구를 다시 검토하세요.");
    }
    private void BeginNewDeliverySession()
    {
        CommitTables(); PushUndo(); deliverySessionId = Guid.NewGuid(); currentLessonPlanId = null; preparedIds.Clear();
    }
    private void LoadLesson()
    {
        CommitTables(); PushUndo();
        if (lessonClass.SelectedItem is not ClassGroup group) throw new InvalidOperationException("반을 먼저 선택하세요.");
        rows.Clear(); preparedIds.Clear(); loadedClassId = group.Id; currentLessonPlanId = null;
        foreach (var member in group.Members)
        {
            var s = students.FirstOrDefault(s => s.Id == member.StudentId); if (s == null || !s.IsActive) continue;
            rows.Add(new LessonRow { Included = true, Input = new LessonInput { StudentId = s.Id, StudentName = s.Name, Nickname = member.NicknameOverride ?? s.Nickname, Date = DateTime.Today.ToString("yyyy-MM-dd"), Attendance = "출석", Attitude = "3", Exams = new List<ExamInput> { new(), new(), new() } } });
        }
        RebuildLessonColumns(); Notice($"‘{group.Name}’ 학생 {rows.Count}명을 불러왔습니다.");
    }
    private void RebuildLessonColumns()
    {
        if (lessonGrid == null) return;
        lessonGrid.Columns.Clear();
        Ui.CheckCol(lessonGrid, "A 발송", "Included", 68); Ui.Col(lessonGrid, "B 성명", "Input.StudentName", 145, true);
        if (Category == "직접입력")
        {
            Ui.Col(lessonGrid, "메시지", "Message", 700);
            layoutHint.Text = "직접입력: 학생별 메시지를 그대로 사용합니다. 첫 글자와 끝 글자가 큰따옴표이면 한 쌍만 제거합니다.";
        }
        else
        {
            Ui.Col(lessonGrid, "C 호칭", "Input.Nickname", 100); Ui.Col(lessonGrid, "D 출석", "Input.Attendance", 95); Ui.Col(lessonGrid, "E 태도", "Input.Attitude", 90);
            if (Category == "모의고사")
            {
                for (int i = 1; i <= ExamCount; i++) Ui.Col(lessonGrid, $"모의 {i} 점수", $"Exam{i}Score", 100);
                for (int i = 1; i <= ExamCount; i++) Ui.Col(lessonGrid, $"모의 {i} 코멘트", $"Exam{i}Comment", 190);
                Ui.Col(lessonGrid, "개별 최종 문구", "Message", 450);
                layoutHint.Text = "모의고사: 첫 행에 회차별 만점을 입력하세요. 평균·최고점·공동 석차·응시자 수는 해당 반의 입력 점수 전체에서 문구 생성 시 계산합니다.";
            }
            else
            {
                Ui.Col(lessonGrid, "F 숙제", "Input.HomeworkScore", 95); Ui.Col(lessonGrid, "G 테스트", "Input.TestScore", 95); Ui.Col(lessonGrid, "H 숙제 코멘트", "Input.HomeworkComment", 190); Ui.Col(lessonGrid, "I 테스트 코멘트", "Input.TestComment", 190); Ui.Col(lessonGrid, "J 개별 최종 문구", "Message", 450);
                layoutHint.Text = "일반 수업 A:J: 발송 · 성명 · 호칭 · 출석 · 태도 · 숙제 · 테스트 · 숙제 코멘트 · 테스트 코멘트 · 문구. 첫 행 F/G는 만점입니다. Excel 표를 첫 셀부터 범위 붙여넣기할 수 있습니다.";
            }
        }
        examTitlePanel.Visibility = Category == "모의고사" ? Visibility.Visible : Visibility.Collapsed;
        for (int i = 0; i < 3; i++) ((FrameworkElement)examTitlePanel.Children[i]).Visibility = i < ExamCount ? Visibility.Visible : Visibility.Collapsed;
        sheetRows.Clear(); if (Category != "직접입력") sheetRows.Add(totals); foreach (var r in rows) sheetRows.Add(r);
    }
    private void PasteLesson() { PushUndo(); Ui.PasteRectangle(lessonGrid); Notice("선택한 칸부터 범위를 붙여넣었습니다. 최종 문구를 갱신하고 검토하세요."); }
    private void PushUndo()
    {
        undo.Push(new UndoFrame(Clone(rows.ToList()), Clone(totals), loadedClassId, currentLessonPlanId, deliverySessionId));
        if (undo.Count > 100) { var recent = undo.Take(100).Reverse().ToArray(); undo.Clear(); foreach (var f in recent) undo.Push(f); }
    }
    private void ResetUndo() => undo.Clear();
    private void UndoLesson()
    {
        if (undo.Count == 0) { Notice("되돌릴 입력이 없습니다."); return; }
        lessonGrid.CancelEdit(DataGridEditingUnit.Cell); lessonGrid.CancelEdit(DataGridEditingUnit.Row);
        var frame = undo.Pop(); rows.Clear(); foreach (var r in frame.Rows) rows.Add(r); totals = frame.Totals; loadedClassId = frame.ClassId; currentLessonPlanId = frame.PlanId; deliverySessionId = frame.DeliverySessionId; RebuildLessonColumns(); Notice("학생별 입력을 되돌렸습니다.");
    }
    private void GenerateMessages()
    {
        CommitTables(); if (CurrentPreset is not MessagePreset preset) throw new InvalidOperationException("Preset을 선택하세요.");
        if (rows.Count == 0) throw new InvalidOperationException("반 명단을 먼저 불러오세요.");
        PushUndo();
        if (preset.Category == "직접입력") { Notice("직접입력 문구를 유지했습니다. 미리보기에서 바깥 큰따옴표 한 쌍만 해제합니다."); return; }
        var errors = TemplateEngine.Validate(preset); if (errors.Count > 0) throw new InvalidOperationException(string.Join("\n", errors));
        var inputs = Clone(rows.Select(r => r.Input).ToList());
        foreach (var input in inputs)
        {
            input.Date = (lessonDate.SelectedDate ?? DateTime.Today).ToString("yyyy-MM-dd"); input.Progress = progressBox.Text; input.Assignment = assignmentBox.Text; input.ExamUnit = examUnitBox.Text; input.Notice = noticeBox.Text;
            input.HomeworkMaximum = totals.Input.HomeworkScore; input.TestMaximum = totals.Input.TestScore;
            if (preset.Category != "모의고사") { ValidateScore(input.HomeworkScore, input.HomeworkMaximum, input.StudentName + " 숙제"); ValidateScore(input.TestScore, input.TestMaximum, input.StudentName + " 테스트"); }
        }
        if (preset.Category == "모의고사")
        {
            for (int i = 0; i < ExamCount; i++)
            {
                double? maximum = totals.Input.Exams[i].Score;
                var scored = inputs.Where(l => l.Exams[i].Score.HasValue).Select(l => l.Exams[i].Score!.Value).ToList();
                foreach (var input in inputs)
                {
                    var e = input.Exams[i]; e.Title = examTitles[i].Text; e.Maximum = maximum;
                    ValidateScore(e.Score, maximum, input.StudentName + " 모의고사 " + (i + 1));
                    e.Average = scored.Count == 0 ? null : scored.Average(); e.Highest = scored.Count == 0 ? null : scored.Max(); e.Attendees = scored.Count; e.Rank = e.Score.HasValue ? 1 + scored.Count(s => s > e.Score.Value) : null;
                }
            }
        }
        var messages = inputs.Select(i => TemplateEngine.Render(preset, i)).ToList();
        for (int i = 0; i < rows.Count; i++) { rows[i].Input = inputs[i]; rows[i].Message = messages[i]; }
        lessonGrid.Items.Refresh(); UpdateSelectedMessage(); Notice("학생별 최종 문구를 생성했습니다. 수정 후에는 다시 미리보기에서 확인하세요.");
    }
    private void UpdateSelectedMessage()
    {
        if (lessonGrid.SelectedItem is not LessonRow row || row.IsTotal)
        {
            selectedMessageEditor.IsEnabled = false; selectedStudentLabel.Text = "학생을 선택하면 최종 문구를 여러 줄로 편집할 수 있습니다."; selectedExamStats.Text = ""; return;
        }
        selectedMessageEditor.IsEnabled = true; selectedStudentLabel.Text = row.Input.StudentName + " · 최종 문구 상세 편집";
        selectedMessageEditor.SetBinding(TextBox.TextProperty, new Binding("Message") { Source = row, Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
        selectedExamStats.Text = Category == "모의고사" ? string.Join("\n", row.Input.Exams.Take(ExamCount).Select(e => $"{e.Title}: {e.Score?.ToString("0.##") ?? "미응시"}/{e.Maximum?.ToString("0.##") ?? "—"} · 평균 {e.Average?.ToString("0.##") ?? "—"} · 최고 {e.Highest?.ToString("0.##") ?? "—"} · 석차 {e.Rank?.ToString() ?? "—"}/{e.Attendees?.ToString() ?? "—"}")) : "";
    }
    private static void ValidateScore(double? score, double? maximum, string title)
    {
        if (maximum is <= 0 || maximum.HasValue && !double.IsFinite(maximum.Value)) throw new InvalidOperationException(title + ": 만점은 0보다 큰 유한한 숫자로 입력하세요.");
        if (score.HasValue && (!double.IsFinite(score.Value) || score < 0 || maximum.HasValue && score > maximum)) throw new InvalidOperationException(title + ": 점수는 0~만점 범위로 입력하세요.");
    }
    private static string Unquote(string text)
    {
        return text.Length >= 2 && text[0] == '"' && text[^1] == '"' ? text[1..^1] : text;
    }
    private Dictionary<Guid, List<string>> MatchFiles()
    {
        string root = folderBox.Text.Trim(); if (root.Length == 0) return new Dictionary<Guid, List<string>>();
        return AttachmentScanner.Scan(root, rows.Where(r => r.Included).Select(r => students.FirstOrDefault(s => s.Id == r.Input.StudentId) ?? throw new InvalidOperationException("학생 DB에서 수신자를 찾을 수 없습니다.")));
    }
    private void ScanFiles()
    {
        var files = MatchFiles(); attachmentSummary.Text = $"매칭 파일 {files.Values.Sum(f => f.Count)}개 · 학생 {files.Count(kv => kv.Value.Count > 0)}명. 미리보기에서 전체 경로를 확인하세요.";
    }
    internal List<BatchItem> BuildSnapshot(bool allowEmptyMessages = false)
    {
        CommitTables();
        var selected = rows.Where(r => r.Included).ToList(); if (selected.Count == 0) throw new InvalidOperationException("발송할 학생을 선택하세요.");
        var common = commonBoxes.Select(b => b.Text).Where(s => !string.IsNullOrWhiteSpace(s)).ToList();
        var files = MatchFiles();
        var batch = new List<BatchItem>();
        foreach (var row in selected)
        {
            var student = students.SingleOrDefault(s => s.Id == row.Input.StudentId) ?? throw new InvalidOperationException(row.Input.StudentName + ": 학생 DB에서 찾을 수 없습니다. 명단을 다시 불러오세요.");
            var item = new BatchItem { StudentId = student.Id, StudentName = student.Name, Nickname = row.Input.Nickname, ChatRoomName = student.ChatRoomName, ChatId = student.ChatId, Message = Category == "직접입력" ? Unquote(row.Message) : row.Message, CommonMessages = new List<string>(common), AttachmentPaths = files.GetValueOrDefault(student.Id, new List<string>()).ToList(), Status = "준비", PreserveMessageWhitespace = true };
            if (!allowEmptyMessages && item.AllMessages.Count == 0 && item.AttachmentPaths.Count == 0) throw new InvalidOperationException(student.Name + ": 메시지 또는 첨부파일을 입력하세요.");
            string content = ScopePreparedContent(JsonSerializer.Serialize(new { item.StudentId, item.StudentName, item.ChatRoomName, item.ChatId, item.Nickname, item.Message, item.CommonMessages, item.AttachmentPaths }));
            Guid preparedId;
            if (preparedIdentityHistory.TryGetValue(content, out Guid historicalId) && HasProtectedSend(historicalId)) preparedId = historicalId;
            else if (!preparedIds.TryGetValue(content, out preparedId)) preparedId = row.PreparedContent == content ? row.PreparedId : Guid.NewGuid();
            preparedIds[content] = preparedId;
            preparedIdentityHistory[content] = preparedId;
            row.PreparedId = preparedId; row.PreparedContent = content; item.Id = preparedId;
            var previous = database.Logs.Where(l => l.BatchId == item.Id).ToList();
            item.Status = previous.LastOrDefault(l => l.Result is "전송 요청 완료" or "확인 필요" or "전송 중")?.Result ?? previous.LastOrDefault()?.Result ?? "준비";
            batch.Add(item);
        }
        return Clone(batch);
    }
    private void Review(bool dryRun, bool execute)
    {
        if (busy) throw new InvalidOperationException("진행 중인 작업이 있습니다.");
        if (demo && !dryRun && execute) throw new InvalidOperationException("데모 모드에서는 실제 발송이 차단됩니다. 드라이런은 데모 연결 확인으로 실행할 수 있습니다.");
        SaveDatabase();
        var snapshot = BuildSnapshot(dryRun);
        var validation = BatchValidator.Validate(database, snapshot, dryRun);
        if (validation.Count > 0) throw new InvalidOperationException(string.Join("\n", validation));
        // The modal owns a detached snapshot. No prior preview is ever considered approval.
        var review = new ReviewWindow(snapshot, dryRun, execute, demo) { Owner = this };
        if (review.ShowDialog() == true && execute) RunBatch(snapshot, dryRun);
    }
    private async void RunBatch(List<BatchItem> snapshot, bool dryRun)
    {
        SaveCurrentDraft();
        if (!demo) store!.Save(database);
        busy = true; sendCancellation = new CancellationTokenSource();
        // Keep the stop button responsive while all editable inputs are locked.
        foreach (TabItem tab in tabs.Items) if (tab != tabs.SelectedItem) tab.IsEnabled = false;
        if (lessonInputPanel != null) lessonInputPanel.IsEnabled = false;
        resultGrid.ItemsSource = snapshot; progressBar.Value = 0; progressBar.Maximum = snapshot.Count;
        sendProgress.Text = demo ? "데모 드라이런 진행 중 · 실제 카카오톡 접근 없음" : dryRun ? "드라이런 진행 중 · 메시지 입력 및 전송 없음" : "발송 진행 중";
        Notice("진행 중에는 입력을 수정할 수 없습니다. ‘즉시 중지’로 중단할 수 있습니다.");
        try
        {
            IChatClient client = demo ? new DemoClient() : new WindowsKakaoClient();
            var coordinator = new SendCoordinator(client);
            await coordinator.RunAsync(database, snapshot, dryRun, item => Dispatcher.Invoke(() => {
                resultGrid.Items.Refresh(); progressBar.Value = snapshot.Count(b => b.Status != "준비" && b.Status != "전송 중");
                sendProgress.Text = $"{item.StudentName} · {item.Status}"; resultGrid.ScrollIntoView(item);
            }), () => { if (!demo) store!.Save(database); }, sendCancellation.Token);
            int failed = snapshot.Count(b => b.Status is "실패" or "확인 필요");
            sendProgress.Text = $"{(demo ? "데모 " : "")}{(dryRun ? "드라이런" : "발송 작업")} 종료 · {string.Join(" / ", snapshot.GroupBy(b => b.Status).Select(g => g.Key + " " + g.Count() + "명"))}";
            Notice(failed > 0 ? "실패 또는 확인 필요 항목을 발송 기록에서 확인하세요. 자동으로 다시 보내지 않습니다." : "작업 결과를 확인하세요.");
        }
        catch (OperationCanceledException) { sendProgress.Text = "중지되었습니다. 일부 전송 가능성이 있는 항목은 직접 확인하세요."; }
        catch (Exception ex) { sendProgress.Text = "작업 중단: " + ex.Message; MessageBox.Show(this, ex.Message, "발송 작업 중단", MessageBoxButton.OK, MessageBoxImage.Warning); }
        finally
        {
            busy = false; sendCancellation.Dispose(); sendCancellation = null;
            foreach (TabItem tab in tabs.Items) tab.IsEnabled = true;
            if (lessonInputPanel != null) lessonInputPanel.IsEnabled = true;
            resultGrid.Items.Refresh(); RefreshLogs(); UpdateMetrics();
        }
    }
    private sealed record UndoFrame(List<LessonRow> Rows, LessonRow Totals, Guid? ClassId, Guid? PlanId, Guid DeliverySessionId);
    public sealed class LessonRow
    {
        public Guid PreparedId { get; set; } = Guid.NewGuid();
        public string? PreparedContent { get; set; }
        public bool IsTotal { get; set; }
        public bool Included { get; set; } = true;
        public LessonInput Input { get; set; } = new() { Exams = new List<ExamInput> { new(), new(), new() } };
        public string Message { get; set; } = "";
        [JsonIgnore] public double? Exam1Score { get => Input.Exams[0].Score; set => Input.Exams[0].Score = value; }
        [JsonIgnore] public double? Exam2Score { get => Input.Exams[1].Score; set => Input.Exams[1].Score = value; }
        [JsonIgnore] public double? Exam3Score { get => Input.Exams[2].Score; set => Input.Exams[2].Score = value; }
        [JsonIgnore] public string Exam1Comment { get => Input.Exams[0].Comment; set => Input.Exams[0].Comment = value; }
        [JsonIgnore] public string Exam2Comment { get => Input.Exams[1].Comment; set => Input.Exams[1].Comment = value; }
        [JsonIgnore] public string Exam3Comment { get => Input.Exams[2].Comment; set => Input.Exams[2].Comment = value; }
    }
    private sealed class DemoClient : IChatClient
    {
        public Task<ChatTarget> ResolveAsync(string room, CancellationToken token) { token.ThrowIfCancellationRequested(); return Task.FromResult(new ChatTarget(0, 0, room, 0)); }
        public Task SendTextAsync(ChatTarget target, string text, CancellationToken token) => throw new InvalidOperationException("데모 전송이 차단되었습니다.");
        public Task SendFilesAsync(ChatTarget target, IReadOnlyList<string> paths, CancellationToken token) => throw new InvalidOperationException("데모 파일 전송이 차단되었습니다.");
        public Task CompleteAsync(ChatTarget target, CancellationToken token) => Task.CompletedTask;
    }
}
