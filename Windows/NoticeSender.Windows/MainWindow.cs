using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using NoticeSender.Core;

namespace NoticeSender.Windows;

public sealed partial class MainWindow : Window
{
    private readonly bool demo;
    private readonly DatabaseStore? store;
    private AppDatabase database;
    private readonly ObservableCollection<Student> students = new();
    private readonly ObservableCollection<ClassGroup> classes = new();
    private readonly ObservableCollection<MessagePreset> presets = new();
    private readonly List<DataGrid> tables = new();
    private readonly TextBlock status = Ui.Text("준비되었습니다.", 13);
    private readonly TextBlock metrics = Ui.Text("", 20);
    private readonly TextBox diagnostic = Ui.Box("카카오톡 상태는 아직 진단하지 않았습니다.", true);
    private readonly TabControl tabs = new() { TabStripPlacement = Dock.Left, Background = Brushes.White, BorderThickness = new Thickness(0) };
    private bool busy;

    public MainWindow(bool demoMode)
    {
        demo = demoMode;
        Title = demo ? "공지발송 · 데모 (실제 전송 차단)" : "공지발송 · Windows";
        Width = 1440; Height = 920; MinWidth = 1050; MinHeight = 720;
        FontFamily = new FontFamily("Malgun Gothic"); FontSize = 14;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = new SolidColorBrush(Color.FromRgb(243, 247, 250));
        if (demo) database = CreateDemo();
        else { store = new DatabaseStore(); database = store.Load(); }
        if (database.Presets.Count == 0) database.Presets = DefaultPresets.Create();
        RefreshCollections();
        InitializeLessonStorage();
        var root = new DockPanel { Margin = new Thickness(20) };
        var heading = Ui.Stack(Ui.Text("공지발송", 28), Ui.Text(demo ? "안전한 데모 공간 · 합성 학생 데이터 · 실제 카카오톡 발송 차단" : "학생 관리부터 수업 공지 검토와 발송까지", 13));
        heading.Margin = new Thickness(5, 0, 0, 12); DockPanel.SetDock(heading, Dock.Top); root.Children.Add(heading);
        status.Margin = new Thickness(8, 12, 0, 0); DockPanel.SetDock(status, Dock.Bottom); root.Children.Add(status);
        root.Children.Add(tabs); Content = root;
        AddTab("시작 / 진단", BuildDashboard());
        AddTab("학생 DB", BuildStudents());
        AddTab("반 관리", BuildClasses());
        AddTab("수업 / 발송", BuildLesson());
        AddTab("문구 Preset", BuildPresets());
        AddTab("발송 기록", BuildLogs());
        Closing += OnClosing;
        RestoreCurrentDraft();
        UpdateMetrics();
    }
    private void AddTab(string title, UIElement content)
    {
        tabs.Items.Add(new TabItem { Header = new TextBlock { Text = title, Width = 130, Margin = new Thickness(10, 15, 10, 15), FontWeight = FontWeights.SemiBold }, Content = new Border { Background = Brushes.White, Padding = new Thickness(25, 20, 18, 15), Child = content } });
    }
    private void RefreshCollections()
    {
        students.Clear(); foreach (var s in database.Students) students.Add(s);
        classes.Clear(); foreach (var c in database.Classes) classes.Add(c);
        presets.Clear(); foreach (var p in database.Presets) presets.Add(p);
    }
    private void UpdateMetrics() => metrics.Text = $"학생 {students.Count}명     반 {classes.Count}개     문구 {presets.Count}개     기록 {database.Logs.Count}건";
    private void Notice(string text) => status.Text = text;
    private void Guard(Action action)
    {
        try { action(); }
        catch (Exception ex) { Notice("작업을 완료하지 못했습니다."); MessageBox.Show(this, ex.Message, "확인해 주세요", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }
    private void CommitTables()
    {
        KeyboardFocus();
        foreach (var grid in tables) if (!Ui.Commit(grid)) throw new InvalidOperationException("입력값이 올바르지 않은 셀이 있습니다. 숫자와 필수 항목을 확인하세요.");
    }
    private void SaveDatabase()
    {
        CommitTables();
        Ui.CommitBindings(this);
        foreach (var student in students)
        {
            if (string.IsNullOrWhiteSpace(student.Name)) throw new InvalidOperationException("학생 이름을 입력하세요.");
            if (student.AdmissionYear < 0 || student.AdmissionYear > 99) throw new InvalidOperationException("학번은 00~99로 입력하세요.");
        }
        database.Students = students.ToList(); database.Classes = classes.ToList(); database.Presets = presets.ToList();
        SaveCurrentDraft();
        if (!demo) store!.Save(database);
        UpdateMetrics(); Notice(demo ? "데모 메모리에 저장했습니다. 실제 DB는 사용하지 않습니다." : "저장했습니다.");
    }
    private UIElement BuildDashboard()
    {
        diagnostic.IsReadOnly = true; diagnostic.MinHeight = 230;
        var diagnose = Ui.Button("카카오톡 읽기 전용 진단", () => Diagnose(), true);
        return Ui.Scroll(Ui.Stack(Ui.Text("안전한 공지 발송 준비", 25), Ui.Text("학생 DB와 반을 준비한 뒤, 수업 결과를 입력하고 최종 문구를 검토하세요. 드라이런은 입력·전송 없이 대상만 확인합니다."), metrics,
            new Separator { Margin = new Thickness(0, 15, 0, 15) }, Ui.Text("데이터 보관", 19),
            Ui.Text(demo ? "현재 데모 데이터는 메모리에만 있습니다. 앱을 닫으면 초기화됩니다." : "데이터 위치: " + store!.Path),
            Ui.Actions(Ui.Button("기존 XLSX 전체 가져오기", () => Guard(ImportWholeWorkbook), true)),
            Ui.Text("기존 통합 문서의 학생 DB와 반별 수업 시트에서 학생과 반 명단을 함께 가져옵니다.", 12),
            Ui.Actions(Ui.Button("전체 JSON 백업", () => Guard(ExportBackup)), Ui.Button("JSON 복원", () => Guard(RestoreBackup))),
            Ui.Text("카카오톡 연결 상태", 19), Ui.Text("카카오톡을 실행하고 발송할 채팅방을 열어 두세요. 같은 제목의 창이 여러 개이거나 입력창을 안전하게 식별하지 못하면 발송을 중단합니다."), Ui.Actions(diagnose), diagnostic));
    }
    private async void Diagnose()
    {
        diagnostic.Text = "읽기 전용 진단 중…";
        try { diagnostic.Text = demo ? "데모 모드입니다. 카카오톡에 접근하거나 실제 메시지를 발송하지 않습니다." : await new NoticeSender.Automation.WindowsKakaoClient().DiagnoseAsync(System.Threading.CancellationToken.None); }
        catch (Exception ex) { diagnostic.Text = "진단 실패: " + ex.Message; }
    }
    private void ExportBackup()
    {
        SaveDatabase(); string? path = Ui.Save("JSON 백업|*.json", "공지발송-백업-" + DateTime.Now.ToString("yyyyMMdd") + ".json");
        if (path == null) return;
        (store ?? new DatabaseStore(Path.Combine(Path.GetTempPath(), "NoticeSender-demo-unused.json"))).ExportBackup(path, database);
        Notice("전체 DB를 백업했습니다.");
    }
    private void RestoreBackup()
    {
        string? path = Ui.Open("JSON 백업|*.json"); if (path == null) return;
        // Parse the complete candidate before replacing the in-memory database.
        var candidate = (store ?? new DatabaseStore(Path.Combine(Path.GetTempPath(), "NoticeSender-demo-unused.json"))).ImportBackup(path);
        if (!Ui.Confirm(this, $"학생 {candidate.Students.Count}명, 반 {candidate.Classes.Count}개, Preset {candidate.Presets.Count}개로 전체 데이터를 복원합니다. 현재 데이터는 자동 백업 후 교체합니다.", "DB 복원")) return;
        if (!demo)
        {
            SaveDatabase();
            store!.ExportBackup(store.Path + ".before-restore-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".json", database);
            store.Save(candidate);
        }
        ReplaceDatabase(candidate); Notice("백업을 복원했습니다. 수업 명단과 복원된 초안을 확인하세요.");
    }
    private void ReplaceDatabase(AppDatabase candidate)
    {
        database = candidate;
        ResetLessonState(); RefreshCollections(); InitializeLessonStorage(); RefreshSources();
        lessonClass.SelectedItem = classes.FirstOrDefault();
        lessonPreset.SelectedItem = presets.FirstOrDefault(p => p.Id == (lessonClass.SelectedItem as ClassGroup)?.DefaultPresetId) ?? presets.FirstOrDefault();
        RestoreCurrentDraft(); RefreshLogs(); UpdateMetrics();
    }
    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (busy) { e.Cancel = true; MessageBox.Show(this, "진행 중인 발송을 먼저 중지하고 결과를 확인하세요.", "발송 진행 중"); return; }
        try { SaveDatabase(); }
        catch (Exception ex) { e.Cancel = !Ui.Confirm(this, "저장하지 못했습니다: " + ex.Message + "\n\n저장하지 않고 종료할까요?", "저장 확인"); }
    }
    private static readonly JsonSerializerOptions CloneOptions = new() { PropertyNameCaseInsensitive = true };
    internal static T Clone<T>(T value) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value, CloneOptions), CloneOptions)!;
    private static AppDatabase CreateDemo()
    {
        var db = new AppDatabase { SchemaVersion = 8, Presets = DefaultPresets.Create() };
        db.Students.AddRange(new[] {
            new Student { Id = Guid.NewGuid(), Name = "가상학생 김하늘", Nickname = "하늘", School = "데모고", AdmissionYear = 26, ChatRoomName = "[데모] 김하늘", IsActive = true },
            new Student { Id = Guid.NewGuid(), Name = "가상학생 이바다", Nickname = "바다", School = "데모고", AdmissionYear = 26, ChatRoomName = "[데모] 이바다", IsActive = true },
            new Student { Id = Guid.NewGuid(), Name = "가상학생 박나무", Nickname = "나무", School = "데모고", AdmissionYear = 26, ChatRoomName = "[데모] 박나무", IsActive = true }
        });
        db.Classes.Add(new ClassGroup { Id = Guid.NewGuid(), Name = "데모 수학반", School = "데모고", AdmissionYear = 26, DefaultPresetId = db.Presets.FirstOrDefault()?.Id, Members = db.Students.Select(s => new ClassMember { StudentId = s.Id }).ToList() });
        return db;
    }
    internal void VerifySmokeTest()
    {
        if (!demo || store != null || tabs.Items.Count != 6 || students.Count != 3) throw new InvalidOperationException("격리 데모 초기화 실패");
        lessonClass.SelectedIndex = 0; LoadLesson();
        if (rows.Count != 3) throw new InvalidOperationException("데모 수업 명단 실패");
        foreach (var row in rows) row.Message = row.Input.Nickname + " 학생의 데모 공지입니다.";
        PushUndo();
        var beforeFirstPreview = CaptureDraft();
        var snapshot = BuildSnapshot();
        if (snapshot.Count != 3 || snapshot.Any(b => b.AllMessages.Count == 0)) throw new InvalidOperationException("미리보기 생성 실패");
        string original = snapshot[0].Message; rows[0].Message = "수정 후";
        if (snapshot[0].Message != original) throw new InvalidOperationException("발송 스냅샷 분리 실패");
        if (database.Logs.Count != 0) throw new InvalidOperationException("스모크 테스트 중 발송 기록 발생");
        rows[0].Message = original;
        if (BuildSnapshot()[0].Id != snapshot[0].Id) throw new InvalidOperationException("동일 발송 사본의 식별자 재사용 실패");
        database.Logs.Add(new SendLog { BatchId = snapshot[0].Id, StudentId = snapshot[0].StudentId, Result = "확인 필요" });
        if (BuildSnapshot()[0].Status != "확인 필요") throw new InvalidOperationException("미확인 발송 재시도 차단 실패");
        LoadLesson(); UndoLesson(); UndoLesson();
        if (BuildSnapshot()[0].Id != snapshot[0].Id || BuildSnapshot()[0].Status != "확인 필요") throw new InvalidOperationException("입력 뒤로가기로 발송 식별자 / 재시도 보호 초기화됨");
        ApplyDraft(beforeFirstPreview);
        if (BuildSnapshot()[0].Id != snapshot[0].Id || BuildSnapshot()[0].Status != "확인 필요") throw new InvalidOperationException("이전 수업 초안 로드로 발송 보호 초기화됨");
        SaveCurrentDraft(); preparedIdentityHistory.Clear(); InitializeLessonStorage(); ApplyDraft(beforeFirstPreview);
        if (BuildSnapshot()[0].Id != snapshot[0].Id || BuildSnapshot()[0].Status != "확인 필요") throw new InvalidOperationException("DB 발송 보호 이력 재시작 복원 실패");
        var previouslySentDraft = CaptureDraft();
        BeginNewDeliverySession();
        var nextRound = BuildSnapshot();
        if (nextRound[0].Id == snapshot[0].Id || nextRound[0].Status != "준비") throw new InvalidOperationException("명시적인 새 발송 회차에서 동일 문구 발송 준비 실패");
        var nextRoundDraft = CaptureDraft();
        UndoLesson();
        if (deliverySessionId != previouslySentDraft.DeliverySessionId || BuildSnapshot()[0].Id != snapshot[0].Id || BuildSnapshot()[0].Status != "확인 필요") throw new InvalidOperationException("새 회차 시작 뒤 입력 되돌리기에서 이전 회차 보호 손실");
        ApplyDraft(nextRoundDraft);
        if (deliverySessionId != nextRoundDraft.DeliverySessionId || BuildSnapshot()[0].Id != nextRound[0].Id || BuildSnapshot()[0].Status != "준비") throw new InvalidOperationException("새 발송 회차 초안의 회차 / 식별자 복원 실패");
        ApplyDraft(previouslySentDraft);
        if (BuildSnapshot()[0].Id != snapshot[0].Id || BuildSnapshot()[0].Status != "확인 필요") throw new InvalidOperationException("이전 회차 초안 복원 시 재시도 보호 손실");
        var legacyDraft = Clone(previouslySentDraft); legacyDraft.DeliverySessionId = null;
        Guid legacyScope = deliverySessionId; ApplyDraft(legacyDraft); ApplyDraft(legacyDraft);
        if (deliverySessionId != legacyScope || BuildSnapshot()[0].Id != snapshot[0].Id || BuildSnapshot()[0].Status != "확인 필요") throw new InvalidOperationException("회차 없는 구버전 초안의 안정적인 회차 상속 실패");
        database.Logs.Clear();
        foreach (var row in rows) row.Message = "";
        if (BuildSnapshot(true).Any(b => b.AllMessages.Count != 0)) throw new InvalidOperationException("빈 문구 드라이런 생성 실패");
        commonBoxes[0].Text = "  공통 메시지만 발송\n\n";
        if (BuildSnapshot()[0].AllMessages.Single() != commonBoxes[0].Text) throw new InvalidOperationException("공통 메시지 단독 / 공백 보존 실패");
        commonBoxes[0].Clear();
        lessonPreset.SelectedItem = presets.First(p => p.Category == "직접입력");
        foreach (var row in rows) row.Message = "  \"직접 문구\"  ";
        if (BuildSnapshot()[0].Message != "  \"직접 문구\"  ") throw new InvalidOperationException("직접 문구 외곽 공백 보존 실패");
        foreach (var row in rows) row.Message = "\"  직접 문구\n\n \"";
        if (BuildSnapshot()[0].AllMessages[0] != "  직접 문구\n\n ") throw new InvalidOperationException("직접 문구 한 쌍 따옴표 제거 실패");
        foreach (var row in rows) row.Message = "\"\"hello\"\"";
        GenerateMessages(); GenerateMessages();
        if (BuildSnapshot()[0].Message != "\"hello\"") throw new InvalidOperationException("반복 문구 생성 시 큰따옴표 한 쌍 보존 실패");
        lessonPreset.SelectedItem = presets.First(p => p.Category == "일반 수업");
        lessonGrid.CurrentCell = new DataGridCellInfo(totals, lessonGrid.Columns[5]);
        Ui.PasteRectangle(lessonGrid, "120\t70");
        if (totals.Input.HomeworkScore != 120 || totals.Input.TestScore != 70) throw new InvalidOperationException("F/G 만점 범위 붙여넣기 실패");
        PushUndo(); rows[0].Input.HomeworkScore = 65; UndoLesson();
        if (rows[0].Input.HomeworkScore != null) throw new InvalidOperationException("학생별 입력 뒤로가기 실패");
        lessonPreset.SelectedItem = presets.First(p => p.Category == "모의고사");
        rows[0].Exam1Score = 80; rows[1].Exam1Score = 80; rows[2].Exam1Score = 60;
        GenerateMessages();
        if (rows[0].Input.Exams[0].Rank != 1 || rows[1].Input.Exams[0].Rank != 1 || rows[2].Input.Exams[0].Rank != 3 || rows[0].Input.Exams[0].Attendees != 3 || rows[0].Input.Exams[0].Highest != 80 || Math.Abs(rows[0].Input.Exams[0].Average!.Value - 220.0 / 3) > 0.0001) throw new InvalidOperationException("모의고사 평균 / 최고점 / 공동 석차 집계 실패");
        commonBoxes[0].Text = "재시작 후 공통 메시지";
        var captured = CaptureDraft();
        var restored = Clone(captured);
        string savedMessage = rows[0].Message;
        rows[0].Message = "임시 변경"; commonBoxes[0].Clear();
        ApplyDraft(restored);
        if (rows[0].Message != savedMessage || commonBoxes[0].Text != "재시작 후 공통 메시지" || rows[0].Input.Exams[0].Rank != 1) throw new InvalidOperationException("수업 초안 JSON 복원 실패");
        SaveCurrentDraft();
        if (database.Extra?.ContainsKey("windowsLessonDraft") != true) throw new InvalidOperationException("수업 초안 DB 확장 저장 실패");
        using (var firstInstance = new SingleInstanceGuard(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "NoticeSender-smoke-" + Guid.NewGuid() + ".json")))
        {
            using var duplicate = new SingleInstanceGuard(firstInstance.DatabasePath);
            if (!firstInstance.Acquired || duplicate.Acquired) throw new InvalidOperationException("단일 DB 실행 잠금 실패");
        }
        commonBoxes[0].Text = "이전 DB의 문구"; folderBox.Text = "C:\\old-database"; progressBox.Text = "이전 DB의 진도"; assignmentBox.Text = "이전 DB의 과제"; noticeBox.Text = "이전 DB의 공지";
        totals.Input.HomeworkScore = 777; loadedClassId = Guid.NewGuid(); currentLessonPlanId = Guid.NewGuid();
        ReplaceDatabase(CreateDemo());
        if (commonBoxes.Any(b => b.Text.Length != 0) || folderBox.Text.Length != 0 || progressBox.Text.Length != 0 || assignmentBox.Text.Length != 0 || noticeBox.Text.Length != 0 || rows.Count != 0 || loadedClassId != null || currentLessonPlanId != null || totals.Input.HomeworkScore != 100) throw new InvalidOperationException("다른 DB 복원 시 이전 수업 데이터 초기화 실패");
        foreach (TabItem tab in tabs.Items) { tabs.SelectedItem = tab; UpdateLayout(); }
    }
}
