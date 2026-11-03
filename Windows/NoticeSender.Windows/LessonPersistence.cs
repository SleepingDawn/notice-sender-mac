using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Controls;
using NoticeSender.Core;

namespace NoticeSender.Windows;

public sealed partial class MainWindow
{
    private readonly ObservableCollection<PortableLesson> savedLessons = new();
    private readonly ComboBox savedLessonBox = new() { DisplayMemberPath = "Label", MinWidth = 290, Margin = new System.Windows.Thickness(0, 0, 10, 8) };
    private Dictionary<Guid, LessonDraft> savedDrafts = new();
    private Guid? currentLessonPlanId;
    private bool draftReadable = true;
    private void ResetLessonState()
    {
        rows.Clear(); sheetRows.Clear(); undo.Clear(); preparedIds.Clear(); preparedIdentityHistory.Clear(); deliverySessionId = Guid.NewGuid(); loadedClassId = null; currentLessonPlanId = null; draftReadable = true;
        progressBox.Clear(); assignmentBox.Clear(); examUnitBox.Clear(); noticeBox.Clear();
        foreach (var common in commonBoxes) common.Clear();
        for (int i = 0; i < 3; i++) examTitles[i].Text = "모의고사 " + (i + 1);
        lessonDate.SelectedDate = DateTime.Today; folderBox.Text = database.AttachmentRootPath ?? "";
        totals = new LessonRow { IsTotal = true, Included = false, Input = new LessonInput { StudentName = "만점 →", HomeworkScore = 100, TestScore = 100, Exams = new List<ExamInput> { new() { Score = 100 }, new() { Score = 100 }, new() { Score = 100 } } } };
        System.Windows.Data.BindingOperations.ClearBinding(selectedMessageEditor, TextBox.TextProperty); selectedMessageEditor.Clear(); selectedMessageEditor.IsEnabled = false;
        selectedStudentLabel.Text = "학생을 선택하면 최종 문구를 여러 줄로 편집할 수 있습니다."; selectedExamStats.Text = "";
        resultGrid.ItemsSource = null; sendProgress.Text = "발송 대기"; progressBar.Value = 0;
        attachmentSummary.Text = "첨부 폴더를 선택하면 최종 미리보기에서 학생별 매칭 결과를 확인할 수 있습니다.";
        RebuildLessonColumns();
    }
    private void InitializeLessonStorage()
    {
        savedLessons.Clear(); savedDrafts.Clear(); savedLessonBox.ItemsSource = savedLessons;
        preparedIdentityHistory.Clear();
        if (database.Extra?.TryGetValue("windowsDeliverySessionId", out var storedSession) == true)
        {
            if (storedSession.ValueKind != JsonValueKind.String || !storedSession.TryGetGuid(out var sessionId) || sessionId == Guid.Empty) throw new InvalidDataException("발송 회차 식별자가 잘못되었습니다.");
            deliverySessionId = sessionId;
        }
        if (database.Extra?.TryGetValue("windowsPreparedIdentityHistory", out var history) == true)
        {
            var restoredHistory = JsonSerializer.Deserialize<Dictionary<string, Guid>>(history.GetRawText(), DatabaseJson.Options) ?? throw new InvalidDataException("발송 보호 이력 형식이 잘못되었습니다.");
            foreach (var pair in restoredHistory) preparedIdentityHistory[ScopePreparedContent(pair.Key)] = pair.Value;
        }
        if (database.Extra?.TryGetValue("lessonPlans", out var plans) == true && plans.ValueKind == JsonValueKind.Array)
        {
            foreach (var p in JsonSerializer.Deserialize<List<PortableLesson>>(plans.GetRawText(), DatabaseJson.Options) ?? new()) { p.ClassName = classes.FirstOrDefault(c => c.Id == p.ClassId)?.Name ?? "삭제된 반"; savedLessons.Add(p); }
        }
        if (database.Extra?.TryGetValue("windowsLessonDrafts", out var library) == true)
        {
            try { savedDrafts = JsonSerializer.Deserialize<Dictionary<Guid, LessonDraft>>(library.GetRawText(), DatabaseJson.Options) ?? new(); }
            catch (JsonException) { Notice("저장된 Windows 수업 상세를 읽지 못했습니다. 원본은 백업에 보존됩니다."); }
        }
        if (savedLessons.Count > 0) savedLessonBox.SelectedIndex = 0;
    }
    private LessonDraft CaptureDraft() => new()
    {
        ClassId = loadedClassId, PresetId = CurrentPreset?.Id, PlanId = currentLessonPlanId, DeliverySessionId = deliverySessionId,
        Date = lessonDate.SelectedDate ?? DateTime.Today, Progress = progressBox.Text, Assignment = assignmentBox.Text, ExamUnit = examUnitBox.Text, Notice = noticeBox.Text,
        Rows = Clone(rows.ToList()), Totals = Clone(totals), PreparedIds = new Dictionary<string, Guid>(preparedIds, StringComparer.Ordinal), CommonMessages = commonBoxes.Select(b => b.Text).ToList(), ExamTitles = examTitles.Select(b => b.Text).ToList(), AttachmentRoot = folderBox.Text
    };
    private void SaveCurrentDraft()
    {
        database.Extra ??= new Dictionary<string, JsonElement>();
        database.Extra["windowsDeliverySessionId"] = JsonSerializer.SerializeToElement(deliverySessionId, DatabaseJson.Options);
        database.Extra["windowsPreparedIdentityHistory"] = JsonSerializer.SerializeToElement(preparedIdentityHistory, DatabaseJson.Options);
        if (!draftReadable) return;
        var draft = CaptureDraft();
        database.Extra["windowsLessonDraft"] = JsonSerializer.SerializeToElement(draft, DatabaseJson.Options);
        if (currentLessonPlanId is Guid planId && savedDrafts.ContainsKey(planId))
        {
            savedDrafts[planId] = draft;
            database.Extra["windowsLessonDrafts"] = JsonSerializer.SerializeToElement(savedDrafts, DatabaseJson.Options);
        }
        database.AttachmentRootPath = string.IsNullOrWhiteSpace(folderBox.Text) ? null : folderBox.Text;
    }
    private void RestoreCurrentDraft()
    {
        if (database.Extra?.TryGetValue("windowsLessonDraft", out var element) != true) return;
        try
        {
            var draft = JsonSerializer.Deserialize<LessonDraft>(element.GetRawText(), DatabaseJson.Options) ?? throw new JsonException("수업 초안이 비어 있습니다.");
            ApplyDraft(draft); Notice("마지막 Windows 수업 입력을 복원했습니다.");
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException or InvalidDataException)
        {
            draftReadable = false; Notice("Windows 수업 초안을 복원하지 못했습니다. 원본은 유지됩니다: " + ex.Message);
        }
    }
    private void ApplyDraft(LessonDraft draft)
    {
        if (draft.Rows == null || draft.Totals == null || draft.CommonMessages == null || draft.ExamTitles == null || draft.Rows.Any(r => r == null || r.Input == null || r.Input.Exams == null) || draft.Totals.Input == null || draft.Totals.Input.Exams == null) throw new InvalidDataException("수업 초안의 표 형식이 잘못되었습니다.");
        currentLessonPlanId = draft.PlanId; loadedClassId = draft.ClassId;
        // Legacy drafts inherit the current stable scope, never a random per-load scope.
        if (draft.DeliverySessionId is Guid restoredSession && restoredSession != Guid.Empty) deliverySessionId = restoredSession;
        preparedIds.Clear(); foreach (var pair in draft.PreparedIds ?? new()) preparedIds[ScopePreparedContent(pair.Key)] = pair.Value;
        foreach (var row in draft.Rows) if (row.PreparedContent != null) { row.PreparedContent = ScopePreparedContent(row.PreparedContent); preparedIds.TryAdd(row.PreparedContent, row.PreparedId); }
        lessonClass.SelectedItem = classes.FirstOrDefault(c => c.Id == draft.ClassId);
        lessonPreset.SelectedItem = presets.FirstOrDefault(p => p.Id == draft.PresetId) ?? presets.FirstOrDefault();
        lessonDate.SelectedDate = draft.Date; progressBox.Text = draft.Progress; assignmentBox.Text = draft.Assignment; examUnitBox.Text = draft.ExamUnit; noticeBox.Text = draft.Notice; folderBox.Text = draft.AttachmentRoot;
        rows.Clear(); foreach (var row in draft.Rows) { while (row.Input.Exams.Count < 3) row.Input.Exams.Add(new ExamInput()); rows.Add(row); }
        totals = draft.Totals; while (totals.Input.Exams.Count < 3) totals.Input.Exams.Add(new ExamInput());
        for (int i = 0; i < 5; i++) commonBoxes[i].Text = i < draft.CommonMessages.Count ? draft.CommonMessages[i] : "";
        for (int i = 0; i < 3; i++) examTitles[i].Text = i < draft.ExamTitles.Count ? draft.ExamTitles[i] : "모의고사 " + (i + 1);
        RebuildLessonColumns();
    }
    private void SaveLesson()
    {
        SaveDatabase();
        if (loadedClassId is not Guid classId || CurrentPreset is not MessagePreset preset) throw new InvalidOperationException("반 명단과 Preset을 선택한 수업을 먼저 준비하세요.");
        var id = currentLessonPlanId ?? Guid.NewGuid(); currentLessonPlanId = id;
        var plan = savedLessons.FirstOrDefault(p => p.Id == id);
        if (plan == null) { plan = new PortableLesson { Id = id }; savedLessons.Add(plan); }
        plan.ClassId = classId; plan.PresetId = preset.Id; plan.Date = new DateTimeOffset(lessonDate.SelectedDate ?? DateTime.Today); plan.UpdatedAt = DateTimeOffset.Now;
        plan.Progress = progressBox.Text; plan.Assignment = assignmentBox.Text; plan.ExamUnit = examUnitBox.Text; plan.Notice = noticeBox.Text; plan.ClassName = classes.FirstOrDefault(c => c.Id == classId)?.Name ?? "반";
        savedDrafts[id] = CaptureDraft(); WriteLessonLibrary(); SaveDatabase(); savedLessonBox.Items.Refresh(); savedLessonBox.SelectedItem = plan; Notice("수업 입력과 학생별 문구를 저장했습니다.");
    }
    private void LoadSavedLesson()
    {
        if (savedLessonBox.SelectedItem is not PortableLesson plan) throw new InvalidOperationException("저장된 수업을 선택하세요.");
        if (rows.Count > 0 && !Ui.Confirm(this, "현재 입력을 저장된 수업으로 바꿀까요? 입력 뒤로가기로 학생별 표를 되돌릴 수 있습니다.")) return;
        PushUndo();
        if (savedDrafts.TryGetValue(plan.Id, out var draft)) ApplyDraft(Clone(draft));
        else
        {
            var group = classes.FirstOrDefault(c => c.Id == plan.ClassId) ?? throw new InvalidOperationException("이 수업의 반이 학생 DB에 없습니다.");
            lessonClass.SelectedItem = group; lessonPreset.SelectedItem = presets.FirstOrDefault(p => p.Id == plan.PresetId) ?? presets.FirstOrDefault(); LoadLesson();
            lessonDate.SelectedDate = plan.Date.LocalDateTime.Date; progressBox.Text = plan.Progress; assignmentBox.Text = plan.Assignment; examUnitBox.Text = plan.ExamUnit; noticeBox.Text = plan.Notice;
        }
        currentLessonPlanId = plan.Id; Notice("저장된 수업을 불러왔습니다. 발송 전 최종 문구를 확인하세요.");
    }
    private void DeleteSavedLesson()
    {
        if (savedLessonBox.SelectedItem is not PortableLesson plan || !Ui.Confirm(this, $"‘{plan.Label}’ 저장 수업을 삭제할까요? 발송 기록은 유지됩니다.")) return;
        savedLessons.Remove(plan); savedDrafts.Remove(plan.Id); if (currentLessonPlanId == plan.Id) currentLessonPlanId = null; WriteLessonLibrary(); SaveDatabase();
    }
    private void WriteLessonLibrary()
    {
        database.Extra ??= new Dictionary<string, JsonElement>();
        database.Extra["lessonPlans"] = JsonSerializer.SerializeToElement(savedLessons.ToList(), DatabaseJson.Options);
        database.Extra["windowsLessonDrafts"] = JsonSerializer.SerializeToElement(savedDrafts, DatabaseJson.Options);
    }
    public sealed class PortableLesson : ExtensibleModel
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        [JsonPropertyName("classID")] public Guid ClassId { get; set; }
        [JsonPropertyName("presetID")] public Guid PresetId { get; set; }
        public DateTimeOffset Date { get; set; } = DateTimeOffset.Now;
        public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.Now;
        public string Progress { get; set; } = "";
        public string Assignment { get; set; } = "";
        public string ExamUnit { get; set; } = "";
        public string Notice { get; set; } = "";
        public string PreviewAttendance { get; set; } = "출석";
        [JsonIgnore] public string ClassName { get; set; } = "";
        [JsonIgnore] public string Label => $"{Date:yyyy-MM-dd} · {ClassName} · {Progress}";
    }
    public sealed class LessonDraft
    {
        public Guid? ClassId { get; set; }
        public Guid? PresetId { get; set; }
        public Guid? PlanId { get; set; }
        public Guid? DeliverySessionId { get; set; }
        public DateTime Date { get; set; } = DateTime.Today;
        public string Progress { get; set; } = "";
        public string Assignment { get; set; } = "";
        public string ExamUnit { get; set; } = "";
        public string Notice { get; set; } = "";
        public string AttachmentRoot { get; set; } = "";
        public Dictionary<string, Guid> PreparedIds { get; set; } = new(StringComparer.Ordinal);
        public List<LessonRow> Rows { get; set; } = new();
        public LessonRow Totals { get; set; } = new();
        public List<string> CommonMessages { get; set; } = new();
        public List<string> ExamTitles { get; set; } = new();
    }
}
