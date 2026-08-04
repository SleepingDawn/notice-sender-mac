import AppKit
import SwiftUI

enum AppSection: String, CaseIterable, Identifiable {
    case dashboard = "시작 및 진단"
    case students = "학생 DB"
    case classes = "반 관리"
    case presets = "Preset"
    case sending = "수업 및 발송"
    case logs = "기록"
    case settings = "학교 설정"
    var id: String { rawValue }
    var icon: String {
        switch self {
        case .dashboard: "checkmark.shield"
        case .students: "person.3"
        case .classes: "rectangle.3.group"
        case .presets: "text.badge.star"
        case .sending: "calendar.badge.checkmark"
        case .logs: "clock.arrow.circlepath"
        case .settings: "building.2"
        }
    }
}

struct RootView: View {
    @EnvironmentObject private var store: AppStore
    @EnvironmentObject private var kakao: KakaoAutomationService
    @Environment(\.scenePhase) private var scenePhase
    @State private var selection: AppSection? = .dashboard

    var body: some View {
        NavigationSplitView {
            List(AppSection.allCases, selection: $selection) { section in
                Label(section.rawValue, systemImage: section.icon).tag(section)
            }
            .navigationTitle("공지 발송")
        } detail: {
            let activeSection = selection ?? .dashboard
            ZStack {
                nonSendingDetail(for: activeSection)
                    .opacity(activeSection == .sending ? 0 : 1)
                    .allowsHitTesting(activeSection != .sending)
                    .accessibilityHidden(activeSection == .sending)

                // Keep this view mounted while navigating elsewhere so all lesson,
                // pasted-cell, preview, review and attachment state survives tab changes.
                UnifiedLessonSendingView()
                    .opacity(activeSection == .sending ? 1 : 0)
                    .allowsHitTesting(activeSection == .sending)
                    .accessibilityHidden(activeSection != .sending)
            }
            .safeAreaInset(edge: .bottom) {
                if let banner = store.banner {
                    HStack { Text(banner); Spacer(); Button("닫기") { store.banner = nil } }
                        .padding(10).background(.regularMaterial)
                }
            }
        }
        .onAppear { kakao.runDiagnostic() }
        .onChange(of: scenePhase) { _, phase in
            if phase == .active { kakao.runDiagnostic() }
        }
    }

    @ViewBuilder
    private func nonSendingDetail(for section: AppSection) -> some View {
        switch section {
        case .dashboard: DashboardView(onOpenStudents: { selection = .students })
        case .students: StudentsView()
        case .classes: ClassesView()
        case .presets: PresetsView()
        case .sending: Color.clear
        case .logs: LogsView()
        case .settings: SchoolSettingsView()
        }
    }
}

struct DashboardView: View {
    @EnvironmentObject private var store: AppStore
    @EnvironmentObject private var kakao: KakaoAutomationService
    @State private var duplicateReview: DuplicateReviewKind?
    let onOpenStudents: () -> Void

    var body: some View {
        ScrollView {
            VStack(alignment: .leading, spacing: 20) {
                HStack(alignment: .firstTextBaseline) {
                    Text("안전한 공지 발송 준비").font(.largeTitle.bold())
                    Spacer()
                    Text(AppVersion.display)
                        .font(.callout.monospacedDigit())
                        .foregroundStyle(.secondary)
                }
                Text("학생 DB와 카카오톡 상태를 먼저 확인합니다. 실제 전송은 수업 및 발송 화면에서 전체 검토 후에만 시작됩니다.").foregroundStyle(.secondary)
                HStack(spacing: 16) {
                    MetricCard(title: "학생", value: "\(store.database.students.count)명", color: .blue)
                    MetricCard(title: "반", value: "\(store.database.classes.count)개", color: .teal)
                    MetricCard(
                        title: "중복 후보",
                        value: "\(store.duplicateStudents().count)건",
                        color: store.duplicateStudents().isEmpty ? .green : .orange,
                        action: { duplicateReview = .students }
                    )
                    MetricCard(
                        title: "중복 톡방",
                        value: "\(store.duplicateChatRooms().count)건",
                        color: store.duplicateChatRooms().isEmpty ? .green : .red,
                        action: { duplicateReview = .chatRooms }
                    )
                }
                GroupBox("1. 기존 XLSX 가져오기") {
                    HStack {
                        Text("학생 DB와 한성25 같은 수업 시트의 명단을 가져옵니다. 기존 데이터는 덮어쓰지 않습니다.")
                        Spacer()
                        Button("XLSX 선택…") { chooseWorkbook() }.buttonStyle(.borderedProminent)
                    }.padding(8)
                }
                GroupBox("2. KakaoTalk 접근성 진단") {
                    VStack(alignment: .leading, spacing: 12) {
                        if let diagnostic = kakao.diagnostic {
                            LabeledContent("손쉬운 사용 권한", value: diagnostic.accessibilityTrusted ? "허용됨" : "필요")
                            LabeledContent("KakaoTalk", value: diagnostic.isRunning ? "실행 중 (\(diagnostic.appVersion))" : "실행 필요")
                            LabeledContent("전송 엔진", value: diagnostic.kmsgAvailable ? "kmsg \(diagnostic.kmsgVersion)" : "안전형 kmsg 없음")
                            LabeledContent("진단", value: diagnostic.summary)
                            if !diagnostic.accessibilityTrusted {
                                Text("아래 버튼을 누른 뒤 시스템 설정에서 공지발송 스위치만 한 번 켜면 됩니다.")
                                    .font(.caption)
                                    .foregroundStyle(.orange)
                            }
                        } else { Text("아직 진단하지 않았습니다.").foregroundStyle(.secondary) }
                        HStack {
                            Button {
                                kakao.repairAccessibilityPermission()
                            } label: {
                                if kakao.isRepairingAccessibility {
                                    HStack(spacing: 6) {
                                        ProgressView().controlSize(.small)
                                        Text("권한 확인 중")
                                    }
                                } else {
                                    Label("손쉬운 사용 한 번에 설정", systemImage: "hand.raised.fill")
                                }
                            }
                            .buttonStyle(.borderedProminent)
                            .disabled(kakao.isRepairingAccessibility)
                            Button("다시 진단") { kakao.runDiagnostic() }.buttonStyle(.borderedProminent)
                            Button("현재 앱을 Finder에서 보기") {
                                NSWorkspace.shared.activateFileViewerSelecting([Bundle.main.bundleURL])
                            }
                        }
                    }.padding(8)
                }
            }.padding(24)
        }
        .sheet(item: $duplicateReview) { kind in
            DuplicateReviewSheet(kind: kind) {
                duplicateReview = nil
                onOpenStudents()
            }
        }
    }

    private func chooseWorkbook() {
        let panel = NSOpenPanel()
        panel.allowedContentTypes = [.spreadsheet]
        panel.allowsMultipleSelection = false
        if panel.runModal() == .OK, let url = panel.url { Task { await store.importWorkbook(url: url) } }
    }
}

struct MetricCard: View {
    let title: String
    let value: String
    let color: Color
    var action: (() -> Void)? = nil

    var body: some View {
        if let action {
            Button(action: action) { content }
                .buttonStyle(.plain)
                .help("\(title) 상세 검토")
        } else {
            content
        }
    }

    private var content: some View {
        VStack(alignment: .leading, spacing: 8) { Text(title).foregroundStyle(.secondary); Text(value).font(.title2.bold()).foregroundStyle(color) }
            .frame(maxWidth: .infinity, alignment: .leading).padding().background(.quaternary.opacity(0.5), in: RoundedRectangle(cornerRadius: 12))
    }
}

private enum DuplicateReviewKind: String, Identifiable {
    case students = "중복 학생 후보"
    case chatRooms = "중복 톡방"
    var id: String { rawValue }
}

private struct DuplicateReviewRow: Identifiable {
    var id: UUID { student.id }
    var reason: String
    var student: Student
}

private struct DuplicateReviewSheet: View {
    @EnvironmentObject private var store: AppStore
    @State private var editing: Student?
    let kind: DuplicateReviewKind
    let onManage: () -> Void

    private var rows: [DuplicateReviewRow] {
        switch kind {
        case .students:
            return store.duplicateStudents()
                .sorted { $0.key < $1.key }
                .flatMap { key, students in
                    students.map { DuplicateReviewRow(reason: key, student: $0) }
                }
        case .chatRooms:
            let duplicateRooms = Set(store.duplicateChatRooms())
            return store.database.students
                .filter { $0.isActive && duplicateRooms.contains($0.chatRoomName) }
                .sorted {
                    $0.chatRoomName.localizedStandardCompare($1.chatRoomName) == .orderedAscending ||
                    ($0.chatRoomName == $1.chatRoomName && $0.name.localizedStandardCompare($1.name) == .orderedAscending)
                }
                .map { DuplicateReviewRow(reason: $0.chatRoomName, student: $0) }
        }
    }

    var body: some View {
        VStack(alignment: .leading, spacing: 14) {
            Text(kind.rawValue).font(.title.bold())
            Text(kind == .students
                 ? "같은 학교·학번·성명을 가진 행입니다. 실제 중복인지 확인해 학생 DB에서 수정하거나 비활성화하세요."
                 : "같은 톡방 이름을 공유하면서 고유한 chat_id로 구분되지 않는 학생입니다. 톡방 이름과 chat_id를 확인하세요.")
                .foregroundStyle(.secondary)
            if rows.isEmpty {
                ContentUnavailableView("검토할 항목이 없습니다", systemImage: "checkmark.circle")
                    .frame(maxWidth: .infinity, maxHeight: .infinity)
            } else {
                Table(rows) {
                    TableColumn("중복 기준", value: \.reason).width(min: 190)
                    TableColumn("이름") { Text($0.student.name) }.width(90)
                    TableColumn("학교") { Text($0.student.school) }.width(70)
                    TableColumn("학번") { Text(AdmissionYearPolicy.formatted($0.student.admissionYear)) }.width(55)
                    TableColumn("상태") { Text($0.student.isActive ? "활성" : "비활성") }.width(65)
                    TableColumn("톡방 이름") { Text($0.student.chatRoomName) }.width(min: 260)
                    TableColumn("chat_id") { Text($0.student.chatID ?? "—") }.width(min: 150)
                    TableColumn("관리") { row in
                        Button("수정") { editing = row.student }
                            .buttonStyle(.borderless)
                    }
                    .width(50)
                }
            }
            HStack {
                Spacer()
                Button("학생 DB에서 검토·관리") { onManage() }
                    .buttonStyle(.borderedProminent)
            }
        }
        .padding(20)
        .frame(minWidth: 900, minHeight: 520)
        .sheet(item: $editing) { student in
            StudentEditor(student: student) {
                store.updateStudent($0)
                editing = nil
            }
        }
    }
}

struct StudentsView: View {
    @EnvironmentObject private var store: AppStore
    @EnvironmentObject private var kakao: KakaoAutomationService
    @State private var search = ""
    @State private var school = "전체"
    @State private var editing: Student?
    @State private var adding = false
    @State private var selectedStudentIDs = Set<UUID>()
    @State private var showingDeleteConfirmation = false

    private var schools: [String] { ["전체"] + Set(store.database.students.map(\.school)).sorted() }
    private var filtered: [Student] {
        store.database.students.filter {
            (school == "전체" || $0.school == school) &&
            (search.isEmpty || $0.name.localizedCaseInsensitiveContains(search) || $0.chatRoomName.localizedCaseInsensitiveContains(search))
        }
    }

    var body: some View {
        VStack(alignment: .leading, spacing: 12) {
            HStack {
                Text("학생 DB").font(.largeTitle.bold())
                Text("학번 범위: 00~99")
                    .font(.caption)
                    .foregroundStyle(.secondary)
                Spacer()
                Button {
                    guard kakao.ensureAccessibilityForOperation() else {
                        store.banner = "시스템 설정에서 공지발송 스위치를 켠 뒤 같은 버튼을 다시 눌러주세요."
                        return
                    }
                    Task { await store.syncStudentsFromKakaoChats() }
                } label: {
                    if store.isSyncingStudentsFromKakao {
                        HStack(spacing: 6) {
                            ProgressView().controlSize(.small)
                            Text("카카오톡 확인 중")
                        }
                    } else {
                        Label("카카오톡에서 DB 업데이트", systemImage: "bubble.left.and.bubble.right")
                    }
                }
                .disabled(store.isSyncingStudentsFromKakao)
                Button("학생 추가") { adding = true }.buttonStyle(.borderedProminent)
                Button("XLSX/CSV 가져오기") { importStudentFile() }
                Button("CSV 내보내기") { exportStudentCSV() }
                Button("선택 학생 \(selectedStudentIDs.count)명 삭제", role: .destructive) {
                    showingDeleteConfirmation = true
                }
                .disabled(selectedStudentIDs.isEmpty)
                Button("백업") { exportBackup() }
                Button("복원") { restoreBackup() }
            }
            HStack { TextField("이름 또는 톡방 검색", text: $search).textFieldStyle(.roundedBorder); Picker("학교", selection: $school) { ForEach(schools, id: \.self) { Text($0) } }.frame(width: 160) }
            Table(filtered, selection: $selectedStudentIDs) {
                TableColumn("상태") { Text($0.isActive ? "활성" : "비활성").foregroundStyle($0.isActive ? .green : .secondary) }.width(60)
                TableColumn("이름", value: \.name).width(90)
                TableColumn("호칭", value: \.nickname).width(90)
                TableColumn("학교", value: \.school).width(70)
                TableColumn("학번") { Text(AdmissionYearPolicy.formatted($0.admissionYear)) }.width(55)
                TableColumn("정확한 톡방 이름", value: \.chatRoomName)
                TableColumn("chat_id") { Text($0.chatID ?? "—").foregroundStyle(.secondary) }.width(145)
                TableColumn("") { student in Button("수정") { editing = student }.buttonStyle(.borderless) }.width(45)
            }
        }.padding(20)
        .sheet(isPresented: $adding) { StudentEditor(student: nil) { store.addStudent($0); adding = false } }
        .sheet(item: $editing) { student in StudentEditor(student: student) { store.updateStudent($0); editing = nil } }
        .confirmationDialog(
            "선택한 학생 \(selectedStudentIDs.count)명을 삭제할까요?",
            isPresented: $showingDeleteConfirmation,
            titleVisibility: .visible
        ) {
            Button("학생 \(selectedStudentIDs.count)명 삭제", role: .destructive) {
                store.deleteStudents(ids: selectedStudentIDs)
                selectedStudentIDs.removeAll()
            }
            Button("취소", role: .cancel) { }
        } message: {
            Text("학생 DB와 모든 반 명단에서 제거됩니다. 기존 발송 기록은 유지됩니다.")
        }
        .onChange(of: search) { _, _ in selectedStudentIDs.removeAll() }
        .onChange(of: school) { _, _ in selectedStudentIDs.removeAll() }
    }

    private func exportBackup() {
        let panel = NSSavePanel(); panel.nameFieldStringValue = "notice-sender-backup.json"; panel.allowedContentTypes = [.json]
        if panel.runModal() == .OK, let url = panel.url { do { try store.exportBackup(to: url); store.banner = "백업을 저장했습니다." } catch { store.banner = error.localizedDescription } }
    }
    private func restoreBackup() {
        let panel = NSOpenPanel(); panel.allowedContentTypes = [.json]
        if panel.runModal() == .OK, let url = panel.url { do { try store.restoreBackup(from: url); store.banner = "백업을 복원했습니다." } catch { store.banner = error.localizedDescription } }
    }
    private func importStudentFile() {
        let panel = NSOpenPanel()
        panel.allowedContentTypes = [.spreadsheet, .commaSeparatedText, .tabSeparatedText]
        panel.allowsMultipleSelection = false
        if panel.runModal() == .OK, let url = panel.url { Task { await store.importStudentFile(url: url) } }
    }
    private func exportStudentCSV() {
        let panel = NSSavePanel()
        panel.nameFieldStringValue = "notice-sender-students.csv"
        panel.allowedContentTypes = [.commaSeparatedText]
        if panel.runModal() == .OK, let url = panel.url {
            do {
                try store.exportStudentCSV(to: url)
                store.banner = "학생 DB \(store.database.students.count)명을 CSV로 저장했습니다."
            } catch {
                store.banner = "학생 DB CSV 내보내기 실패: \(error.localizedDescription)"
            }
        }
    }
}

struct StudentEditor: View {
    @Environment(\.dismiss) private var dismiss
    @State private var value: Student
    @State private var admissionYearText: String
    @State private var nicknameWasManuallyEdited: Bool
    let onSave: (Student) -> Void
    init(student: Student?, onSave: @escaping (Student) -> Void) {
        var initial = student ?? Student(name: "", nickname: "", school: "", admissionYear: Calendar.current.component(.year, from: .now) % 100, chatRoomName: "")
        let generatedNickname = NicknameGenerator.generate(from: initial.name)
        initial.nickname = NicknameGenerator.resolved(name: initial.name, enteredNickname: initial.nickname)
        _value = State(initialValue: initial)
        _admissionYearText = State(initialValue: AdmissionYearPolicy.formatted(initial.admissionYear))
        _nicknameWasManuallyEdited = State(initialValue: student != nil && initial.nickname != generatedNickname)
        self.onSave = onSave
    }
    private var admissionYear: Int? { AdmissionYearPolicy.parseTwoDigit(admissionYearText) }
    private var nicknameBinding: Binding<String> {
        Binding(
            get: { value.nickname },
            set: {
                value.nickname = $0
                nicknameWasManuallyEdited = true
            }
        )
    }
    var body: some View {
        VStack(alignment: .leading, spacing: 14) {
            Text("학생 정보").font(.title2.bold())
            Form {
                TextField("이름", text: $value.name)
                HStack {
                    TextField("호칭", text: nicknameBinding)
                    Button("자동값 사용") {
                        value.nickname = NicknameGenerator.generate(from: value.name)
                        nicknameWasManuallyEdited = false
                    }
                }
                TextField("학교", text: $value.school)
                TextField("학번 (00~99)", text: $admissionYearText)
                TextField("정확한 톡방 이름", text: $value.chatRoomName)
                TextField("kmsg chat_id (선택)", text: Binding(
                    get: { value.chatID ?? "" },
                    set: {
                        let trimmed = $0.trimmingCharacters(in: .whitespacesAndNewlines)
                        value.chatID = trimmed.isEmpty ? nil : trimmed
                    }
                ))
                Toggle("활성", isOn: $value.isActive)
            }
            HStack {
                Spacer()
                Button("취소") { dismiss() }
                Button("저장") {
                    guard let admissionYear else { return }
                    value.admissionYear = admissionYear
                    value.nickname = NicknameGenerator.resolved(name: value.name, enteredNickname: value.nickname)
                    onSave(value)
                }
                .buttonStyle(.borderedProminent)
                .disabled(value.name.isEmpty || value.school.isEmpty || value.chatRoomName.isEmpty || admissionYear == nil)
            }
        }
        .padding(24)
        .frame(width: 480)
        .onChange(of: value.name) { _, newName in
            if !nicknameWasManuallyEdited {
                value.nickname = NicknameGenerator.generate(from: newName)
            }
        }
    }
}

enum ClassManagementSplitLayout {
    static let classListFraction = 0.3
    static let studentManagementFraction = 0.7

    static func classListWidth(totalWidth: CGFloat) -> CGFloat {
        totalWidth * classListFraction
    }

    static func studentManagementWidth(totalWidth: CGFloat) -> CGFloat {
        totalWidth * studentManagementFraction
    }
}

struct ClassesView: View {
    @EnvironmentObject private var store: AppStore
    @State private var selectedID: UUID?
    @State private var creating = false
    @State private var selectedStudents = Set<UUID>()
    @State private var studentSearch = ""
    @State private var deletionRequest: ClassDeletionRequest?
    @State private var showingRenameClass = false
    @State private var renameClassText = ""

    var body: some View {
        InitialRatioSplitView(
            leadingFraction: ClassManagementSplitLayout.classListFraction,
            minimumLeadingWidth: 280,
            minimumTrailingWidth: 280
        ) {
                VStack(alignment: .leading) {
                HStack {
                    Text("반").font(.title.bold())
                    Spacer()
                    Menu {
                        Button("전체 반 JSON 내보내기") { exportClassArchive() }
                            .disabled(store.database.classes.isEmpty)
                        Button("반 JSON 가져오기") { importClassArchive() }
                    } label: {
                        Image(systemName: "square.and.arrow.up.on.square")
                    }
                    .help("반 파일 가져오기·내보내기")
                    Button { creating = true } label: { Image(systemName: "plus") }
                        .help("새 반 추가")
                }
                List(store.database.classes, selection: $selectedID) { group in
                    VStack(alignment: .leading) { Text(group.name); Text("\(group.members.count)명 · v\(group.version)").font(.caption).foregroundStyle(.secondary) }.tag(group.id)
                }
                HStack {
                    Button("이름 편집") {
                        guard let group = store.database.classes.first(where: { $0.id == selectedID }) else { return }
                        renameClassText = group.name
                        showingRenameClass = true
                    }
                    .disabled(selectedID == nil)
                    Button("선택 반 삭제", role: .destructive) {
                        guard let group = store.database.classes.first(where: { $0.id == selectedID }) else { return }
                        deletionRequest = .selected(id: group.id, name: group.name)
                    }
                    .disabled(selectedID == nil)
                    Spacer()
                    Button("전체 반 삭제", role: .destructive) {
                        deletionRequest = .all(count: store.database.classes.count)
                    }
                    .disabled(store.database.classes.isEmpty)
                }
                }
                .padding()
                .frame(maxWidth: .infinity, maxHeight: .infinity)
        } trailing: {
                if let group = store.database.classes.first(where: { $0.id == selectedID }) {
                    let candidates = availableStudents(for: group)
                    VStack(alignment: .leading, spacing: 12) {
                    HStack {
                        Text(group.name).font(.largeTitle.bold())
                        Spacer()
                        Text("현재 \(group.members.count)명").foregroundStyle(.secondary)
                    }
                    Text("현재 반 학생 명단").font(.headline)
                    List {
                        ForEach(Array(ClassMemberSorter.sorted(group.members, students: store.database.students).enumerated()), id: \.element.id) { index, member in
                            if let student = store.student(id: member.studentID) {
                                HStack {
                                    Text("\(index + 1)").frame(width: 35, alignment: .trailing).foregroundStyle(.secondary)
                                    Text(student.name).frame(width: 100, alignment: .leading)
                                    Text(member.nicknameOverride?.isEmpty == false ? member.nicknameOverride! : student.nickname).frame(width: 100, alignment: .leading)
                                    Text(student.chatRoomName).foregroundStyle(.secondary)
                                    Spacer()
                                    Button(role: .destructive) { remove(group, member.studentID) } label: { Image(systemName: "trash") }
                                        .help("반에서 학생 삭제")
                                }
                            }
                        }
                    }
                    .frame(minHeight: 260, maxHeight: .infinity)
                    Divider()
                    Text("학생 추가").font(.headline)
                    TextField("이름·학교·학번 검색", text: $studentSearch).textFieldStyle(.roundedBorder)
                    HStack {
                        Text("검색 결과 \(candidates.count)명 · 선택 \(selectedStudents.count)명")
                            .font(.caption).foregroundStyle(.secondary)
                        Spacer()
                        Button("전체 선택") {
                            selectedStudents.formUnion(candidates.map(\.id))
                        }
                        .disabled(candidates.isEmpty || candidates.allSatisfy { selectedStudents.contains($0.id) })
                        Button("선택 해제") { selectedStudents.removeAll() }
                            .disabled(selectedStudents.isEmpty)
                    }
                    List(candidates) { student in
                        Toggle(isOn: studentSelectionBinding(student.id, selection: $selectedStudents)) {
                            Text("\(student.school)\(AdmissionYearPolicy.formatted(student.admissionYear)) · \(student.name) · \(student.chatRoomName)")
                        }
                        .toggleStyle(.checkbox)
                    }
                    .frame(height: 190)
                    HStack {
                        Spacer()
                        Button("선택 학생 \(selectedStudents.count)명 추가") { addSelected(group) }
                            .buttonStyle(.borderedProminent)
                            .disabled(selectedStudents.isEmpty)
                    }
                    }
                    .padding(20)
                    .frame(maxWidth: .infinity, maxHeight: .infinity)
                } else {
                    ContentUnavailableView("반을 선택하세요", systemImage: "rectangle.3.group")
                        .frame(maxWidth: .infinity, maxHeight: .infinity)
                }
        }
        .frame(maxWidth: .infinity, maxHeight: .infinity)
        .onChange(of: selectedID) { _, _ in selectedStudents.removeAll() }
        .sheet(isPresented: $creating) { ClassCreator { name, school, year, ids in store.createClass(name: name, school: school, year: year, studentIDs: ids); creating = false } }
        .alert("반 이름 편집", isPresented: $showingRenameClass) {
            TextField("반 이름", text: $renameClassText)
            Button("취소", role: .cancel) { }
            Button("저장") {
                guard let selectedID else { return }
                if !store.renameClass(id: selectedID, to: renameClassText) {
                    NSSound.beep()
                }
            }
            .disabled(renameClassText.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty)
        } message: {
            Text("반 ID, 학생 명단과 기본 Preset은 그대로 유지됩니다.")
        }
        .confirmationDialog(
            deletionRequest?.title ?? "반 삭제",
            isPresented: Binding(
                get: { deletionRequest != nil },
                set: { if !$0 { deletionRequest = nil } }
            ),
            titleVisibility: .visible
        ) {
            Button(deletionRequest?.actionTitle ?? "삭제", role: .destructive) { performDeletion() }
            Button("취소", role: .cancel) { deletionRequest = nil }
        } message: {
            Text(deletionRequest?.message ?? "")
        }
    }

    private func performDeletion() {
        guard let deletionRequest else { return }
        switch deletionRequest {
        case .selected(let id, _):
            store.deleteClass(id: id)
            if selectedID == id { selectedID = nil }
        case .all:
            store.deleteAllClasses()
            selectedID = nil
        }
        selectedStudents.removeAll()
        self.deletionRequest = nil
    }

    private func exportClassArchive() {
        let panel = NSSavePanel()
        panel.nameFieldStringValue = "notice-sender-classes.json"
        panel.allowedContentTypes = [.json]
        if panel.runModal() == .OK, let url = panel.url {
            do {
                try store.exportClassArchive(to: url)
                store.banner = "반 \(store.database.classes.count)개와 연결 명단을 JSON으로 저장했습니다."
            } catch {
                store.banner = "반 파일 내보내기 실패: \(error.localizedDescription)"
            }
        }
    }

    private func importClassArchive() {
        let panel = NSOpenPanel()
        panel.allowedContentTypes = [.json]
        panel.allowsMultipleSelection = false
        if panel.runModal() == .OK, let url = panel.url {
            do {
                let summary = try store.importClassArchive(from: url)
                selectedID = selectedID.flatMap { id in store.database.classes.contains(where: { $0.id == id }) ? id : nil }
                    ?? store.database.classes.first?.id
                selectedStudents.removeAll()
                store.banner = summary.message
            } catch {
                store.banner = "반 파일 가져오기 실패: \(error.localizedDescription)"
            }
        }
    }

    private func availableStudents(for group: ClassGroup) -> [Student] {
        let memberIDs = Set(group.members.map(\.studentID))
        let query = studentSearch.trimmingCharacters(in: .whitespacesAndNewlines)
        return store.database.students
            .filter {
                $0.isActive && !memberIDs.contains($0.id) &&
                (query.isEmpty ||
                 $0.name.localizedCaseInsensitiveContains(query) ||
                 $0.school.localizedCaseInsensitiveContains(query) ||
                 AdmissionYearPolicy.formatted($0.admissionYear).contains(query))
            }
            .sorted {
                $0.school.localizedStandardCompare($1.school) == .orderedAscending ||
                ($0.school == $1.school && $0.admissionYear != $1.admissionYear && $0.admissionYear < $1.admissionYear) ||
                ($0.school == $1.school && $0.admissionYear == $1.admissionYear && $0.name.localizedStandardCompare($1.name) == .orderedAscending)
            }
    }

    private func addSelected(_ group: ClassGroup) {
        var copy = group
        let orderedIDs = store.database.students
            .filter { selectedStudents.contains($0.id) }
            .sorted { $0.name.localizedStandardCompare($1.name) == .orderedAscending }
            .map(\.id)
        copy.members.append(contentsOf: orderedIDs.map { ClassMember(studentID: $0) })
        store.updateClass(copy)
        selectedStudents.removeAll()
    }
    private func remove(_ group: ClassGroup, _ id: UUID) { var copy = group; copy.members.removeAll { $0.studentID == id }; store.updateClass(copy) }
}

private enum ClassDeletionRequest {
    case selected(id: UUID, name: String)
    case all(count: Int)

    var title: String {
        switch self {
        case .selected(_, let name): "‘\(name)’ 반을 삭제할까요?"
        case .all: "모든 반을 삭제할까요?"
        }
    }

    var message: String {
        switch self {
        case .selected: "반 명단만 삭제됩니다. 학생 DB와 저장된 수업 기록은 유지됩니다."
        case .all(let count): "등록된 반 \(count)개를 모두 삭제합니다. 학생 DB와 저장된 수업 기록은 유지됩니다."
        }
    }

    var actionTitle: String {
        switch self {
        case .selected: "선택 반 삭제"
        case .all: "전체 반 삭제"
        }
    }
}

struct ClassCreator: View {
    @EnvironmentObject private var store: AppStore
    @Environment(\.dismiss) private var dismiss
    @State private var name = ""
    @State private var school = ""
    @State private var year: Int?
    @State private var selected = Set<UUID>()
    let onSave: (String, String, Int, [UUID]) -> Void

    private var schools: [String] {
        Set(store.database.students.filter(\.isActive).map { $0.school.trimmingCharacters(in: .whitespacesAndNewlines) })
            .filter { !$0.isEmpty }
            .sorted { $0.localizedStandardCompare($1) == .orderedAscending }
    }

    private var years: [Int] {
        guard !school.isEmpty else { return [] }
        let studentYears = Set(store.database.students.filter {
            $0.isActive &&
            $0.school.trimmingCharacters(in: .whitespacesAndNewlines).localizedCaseInsensitiveCompare(school) == .orderedSame
        }.map(\.admissionYear))
        return studentYears.filter(AdmissionYearPolicy.isValid).sorted()
    }

    private var filteredStudents: [Student] {
        ClassStudentFilter.students(in: store.database.students, school: school, admissionYear: year)
    }

    var body: some View {
        VStack(alignment: .leading, spacing: 12) {
            Text("새 반 만들기").font(.title2.bold())
            TextField("반 이름", text: $name)
            Picker("학교", selection: $school) {
                Text("학교 선택").tag("")
                ForEach(schools, id: \.self) { Text($0).tag($0) }
            }
            Picker("학번", selection: $year) {
                Text("학번 선택").tag(Int?.none)
                ForEach(years, id: \.self) { Text(AdmissionYearPolicy.formatted($0)).tag(Optional($0)) }
            }
            .disabled(school.isEmpty)

            if school.isEmpty || year == nil {
                ContentUnavailableView("학교와 학번을 선택하세요", systemImage: "person.3")
                    .frame(maxWidth: .infinity, minHeight: 280)
            } else if filteredStudents.isEmpty {
                ContentUnavailableView("일치하는 학생이 없습니다", systemImage: "person.crop.circle.badge.questionmark")
                    .frame(maxWidth: .infinity, minHeight: 280)
            } else {
                HStack {
                    Text("학생 DB 일치 \(filteredStudents.count)명 · 선택 \(selected.count)명")
                        .font(.caption).foregroundStyle(.secondary)
                    Spacer()
                    Button("전체 선택") { selected = Set(filteredStudents.map(\.id)) }
                        .disabled(filteredStudents.allSatisfy { selected.contains($0.id) })
                    Button("선택 해제") { selected.removeAll() }
                        .disabled(selected.isEmpty)
                }
                List(filteredStudents) { student in
                    Toggle(isOn: studentSelectionBinding(student.id, selection: $selected)) {
                        Text("\(student.name) · \(student.chatRoomName)")
                    }
                    .toggleStyle(.checkbox)
                }
                .frame(height: 280)
            }
            HStack {
                Spacer()
                Button("취소") { dismiss() }
                Button("생성") {
                    guard let year else { return }
                    let orderedIDs = filteredStudents.filter { selected.contains($0.id) }.map(\.id)
                    onSave(name.trimmingCharacters(in: .whitespacesAndNewlines), school, year, orderedIDs)
                }
                .buttonStyle(.borderedProminent)
                .disabled(name.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty || school.isEmpty || year == nil || selected.isEmpty)
            }
        }.padding(24).frame(width: 500)
        .onChange(of: school) { _, _ in
            year = nil
            selected.removeAll()
        }
        .onChange(of: year) { _, _ in selected.removeAll() }
    }
}

private func studentSelectionBinding(_ studentID: UUID, selection: Binding<Set<UUID>>) -> Binding<Bool> {
    Binding(
        get: { selection.wrappedValue.contains(studentID) },
        set: { isSelected in
            if isSelected { selection.wrappedValue.insert(studentID) }
            else { selection.wrappedValue.remove(studentID) }
        }
    )
}

struct PresetsView: View {
    @EnvironmentObject private var store: AppStore
    @State private var selectedID: UUID?
    @State private var draft: MessagePreset = DefaultPresets.first
    @State private var preview = ""

    var body: some View {
        InitialRatioSplitView(
            leadingFraction: ClassManagementSplitLayout.classListFraction,
            minimumLeadingWidth: 280,
            minimumTrailingWidth: 420
        ) {
                List(store.database.presets.sorted { ($0.kind.rawValue, -$0.version) < ($1.kind.rawValue, -$1.version) }, selection: $selectedID) { preset in
                    VStack(alignment: .leading) { Text(preset.name); Text("\(preset.kind.rawValue) · v\(preset.version)").font(.caption).foregroundStyle(.secondary) }.tag(preset.id)
                }
                .frame(maxWidth: .infinity, maxHeight: .infinity)
                .onChange(of: selectedID) { _, id in
                    guard let id, let selected = store.database.presets.first(where: { $0.id == id }) else { return }
                    draft = selected
                    updatePreview()
                }
        } trailing: {
                if selectedID != nil {
                    ScrollView {
                        VStack(alignment: .leading, spacing: 14) {
                            Text("Preset 편집").font(.largeTitle.bold())
                            HStack { TextField("이름", text: $draft.name); TextField("학원명", text: $draft.academyName); TextField("선생님", text: $draft.teacherName) }
                            HStack { Text("중간 별점"); Slider(value: $draft.middleThreshold, in: 0...0.95); Text(draft.middleThreshold, format: .percent); Text("높은 별점"); Slider(value: $draft.highThreshold, in: 0.05...1); Text(draft.highThreshold, format: .percent) }
                            if draft.kind == .mock {
                                Stepper("모의고사 개수: \(draft.mockExamCount ?? 3)개", value: Binding(
                                    get: { draft.mockExamCount ?? 3 },
                                    set: { draft.mockExamCount = max(1, min(3, $0)) }
                                ), in: 1...3)
                            }
                            PresetAppleIntelligenceEditorView(
                                draft: $draft,
                                onDraftChanged: updatePreview
                            )
                            .id(draft.id)
                            TemplateEditor(title: "출석 문구", text: $draft.presentTemplate)
                            TemplateEditor(title: "동영상 문구", text: $draft.videoTemplate)
                            TemplateEditor(title: "결석 문구", text: $draft.absentTemplate)
                            HStack { Button("예시 새로고침") { updatePreview() }; Button("새 버전으로 저장") { savePreset() }.buttonStyle(.borderedProminent) }
                            GroupBox("고점 출석 예시") { ScrollView { Text(preview).textSelection(.enabled).frame(maxWidth: .infinity, alignment: .leading).padding(8) } }.frame(minHeight: 260)
                        }
                        .padding(20)
                    }
                    .frame(maxWidth: .infinity, maxHeight: .infinity)
                } else {
                    ContentUnavailableView("Preset을 선택하세요", systemImage: "text.badge.star")
                        .frame(maxWidth: .infinity, maxHeight: .infinity)
                }
        }
        .frame(maxWidth: .infinity, maxHeight: .infinity)
    }
    private func updatePreview() {
        let input = LessonInput(studentID: UUID(), studentName: "홍길동", nickname: "길동이", date: "7월 12일", attendance: "출석", attitude: "3", homeworkScore: 18, homeworkMaximum: 20, testScore: 9, testMaximum: 10, homeworkComment: "숙제를 성실하게 했습니다.", testComment: "틀린 문제를 복습해주세요.", progress: "화학 결합과 분자 구조", assignment: "교재 20~35번", examUnit: "화학 결합", notice: "복습과 숙제를 꼼꼼히 해주세요.", exams: [ExamInput(title: "모의고사 1회", score: 92, maximum: 100, average: 76.5, highest: 98, rank: 2, attendees: 10, comment: "계산 과정을 꼼꼼히 쓰세요.")])
        do { preview = try TemplateEngine.render(preset: draft, input: input) } catch { preview = "오류: \(error.localizedDescription)" }
    }
    private func savePreset() { do { try TemplateEngine.validate(draft); store.addPresetVersion(from: draft); store.banner = "새 preset 버전을 저장했습니다." } catch { store.banner = error.localizedDescription } }
}

struct TemplateEditor: View {
    let title: String
    @Binding var text: String
    var body: some View {
        GroupBox(title) {
            writingToolsEditor
                .font(.system(.body, design: .monospaced))
                .frame(minHeight: 180)
                .padding(4)
        }
    }

    @ViewBuilder
    private var writingToolsEditor: some View {
        if #available(macOS 15.0, *) {
            TextEditor(text: $text)
                .writingToolsBehavior(.complete)
        } else {
            TextEditor(text: $text)
        }
    }
}

private struct LessonPreviewRow: Identifiable {
    var id: UUID { studentID }
    var studentID: UUID
    var studentName: String
    var nickname: String
    var chatRoomName: String
    var message: String
    var error: String?
}

private struct LessonInputSnapshot: Hashable {
    var draft: LessonPlan
    var rows: [PreparedNoticeRow]
    var homeworkMaximum: String
    var testMaximum: String
}

struct UnifiedLessonSendingView: View {
    var body: some View {
        ScrollView {
            LessonManagementView().frame(minHeight: 1200)
        }
    }
}

struct LessonManagementView: View {
    @EnvironmentObject private var store: AppStore
    @EnvironmentObject private var kakao: KakaoAutomationService
    @State private var draft = LessonPlan(classID: UUID(), presetID: UUID())
    @State private var selectedPreviewStudentID: UUID?
    @State private var performanceRows: [PreparedNoticeRow] = []
    @State private var homeworkMaximum = ""
    @State private var testMaximum = ""
    @State private var selectedPasteRow = 0
    @State private var selectedPasteColumn = 0
    @State private var inputHistory: [LessonInputSnapshot] = []
    @State private var isPreparingSend = false
    @State private var lessonDryRun = true
    @State private var lessonSelectionRevision = 0
    @State private var isCommonMessageEnabled = false
    @State private var commonMessage = ""
    @State private var showingMissingNicknameWarning = false
    @State private var missingNicknameIssues: [DirectNoticeNicknameIssue] = []
    @State private var lessonAttachmentPaths: [UUID: [String]] = [:]
    @State private var lessonAttachmentScannedFileCount = 0
    @State private var isScanningLessonAttachments = false
    @State private var lessonAttachmentRefreshRevision = 0
    @State private var lessonAttachmentScanError: String?
    @State private var pendingLessonBatch: SendBatch?
    @State private var showingLessonAttachmentConfirmation = false

    private var group: ClassGroup? { store.database.classes.first { $0.id == draft.classID } }
    private var preset: MessagePreset? { store.database.presets.first { $0.id == draft.presetID } }
    private var canPreview: Bool {
        guard group != nil, let preset else { return false }
        let includedRows = PreparedNoticeSelection.includedRows(performanceRows)
        guard !includedRows.isEmpty else { return false }
        if preset.kind == .direct {
            return effectiveCommonMessage != nil
                || PreparedNoticeSelection.directMessagesAreReady(in: performanceRows, allowEmptyMessages: lessonDryRun)
        }
        guard !draft.progress.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty else { return false }
        switch preset.kind {
        case .direct: return true
        case .first: return true
        case .testOnly, .mock: return (number(testMaximum) ?? 0) > 0
        case .homeworkOnly: return (number(homeworkMaximum) ?? 0) > 0
        case .regular: return (number(homeworkMaximum) ?? 0) > 0 && (number(testMaximum) ?? 0) > 0
        }
    }

    private var effectiveCommonMessage: String? {
        CommonMessagePolicy.effectiveMessage(isEnabled: isCommonMessageEnabled, text: commonMessage)
    }

    private var previewRows: [LessonPreviewRow] {
        guard let group, var resolvedPreset = preset else { return [] }
        if resolvedPreset.kind == .direct {
            return PreparedNoticeSelection.includedRows(performanceRows).compactMap { performance in
                guard let student = store.student(id: performance.id) else { return nil }
                let message = DirectNoticeMessage.normalized(performance.noticeMessage)
                let error = message.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty
                    && effectiveCommonMessage == nil
                    && !lessonDryRun
                    ? "공지 멘트가 비어 있습니다."
                    : nil
                return LessonPreviewRow(
                    studentID: student.id,
                    studentName: student.name,
                    nickname: performance.nickname,
                    chatRoomName: student.chatRoomName,
                    message: error == nil ? message : "",
                    error: error
                )
            }
        }
        resolvedPreset.academyName = store.academyName(for: group.school, fallback: resolvedPreset.academyName)
        let dateText = Self.koreanDateFormatter.string(from: draft.date)
        return PreparedNoticeSelection.includedRows(performanceRows).compactMap { performance in
            guard let student = store.student(id: performance.id) else { return nil }
            let nickname = performance.nickname
            let examCount = max(1, min(3, resolvedPreset.mockExamCount ?? 3))
            let exams = resolvedPreset.kind == .mock
                ? (1...examCount).map { index in
                    ExamInput(title: draft.examUnit.isEmpty ? "모의고사 \(index)" : "\(draft.examUnit) \(index)", score: index == 1 ? number(performance.test) : nil, maximum: number(testMaximum) ?? 100, average: nil, highest: nil, rank: nil, attendees: nil, comment: index == 1 ? performance.testComment : "")
                }
                : []
            let input = LessonInput(
                studentID: student.id,
                studentName: student.name,
                nickname: nickname,
                date: dateText,
                attendance: performance.attendance,
                attitude: performance.attitude,
                homeworkScore: number(performance.homework),
                homeworkMaximum: number(homeworkMaximum),
                testScore: number(performance.test),
                testMaximum: number(testMaximum),
                homeworkComment: performance.homeworkComment,
                testComment: performance.testComment,
                progress: draft.progress,
                assignment: draft.assignment,
                examUnit: draft.examUnit,
                notice: draft.notice,
                exams: exams
            )
            do {
                return LessonPreviewRow(studentID: student.id, studentName: student.name, nickname: nickname, chatRoomName: student.chatRoomName, message: try TemplateEngine.render(preset: resolvedPreset, input: input), error: nil)
            } catch {
                return LessonPreviewRow(studentID: student.id, studentName: student.name, nickname: nickname, chatRoomName: student.chatRoomName, message: "", error: error.localizedDescription)
            }
        }
    }

    private var lessonAttachmentScanKey: String {
        let root = (store.database.attachmentRootPath ?? "").trimmingCharacters(in: .whitespacesAndNewlines)
        return ([root, String(lessonAttachmentRefreshRevision)] + previewRows.map { $0.studentID.uuidString }).joined(separator: "|")
    }

    private var lessonAttachmentCount: Int {
        previewRows.reduce(0) { $0 + (lessonAttachmentPaths[$1.studentID]?.count ?? 0) }
    }

    private var lessonAttachmentStudentCount: Int {
        previewRows.filter { !(lessonAttachmentPaths[$0.studentID] ?? []).isEmpty }.count
    }

    private var includedPerformanceRowCount: Int {
        PreparedNoticeSelection.includedRows(performanceRows).count
    }

    var body: some View {
        VStack(alignment: .leading, spacing: 14) {
            HStack {
                Text("수업 내용과 학생별 결과").font(.largeTitle.bold())
                Spacer()
                Button { undoLastInput() } label: { Label("입력 뒤로가기", systemImage: "arrow.uturn.backward") }
                    .keyboardShortcut("z", modifiers: .command)
                    .disabled(inputHistory.isEmpty || kakao.isBusy || isPreparingSend)
                if kakao.isBusy {
                    Button("즉시 중지", role: .destructive) { kakao.stop() }
                }
                Toggle("드라이런", isOn: $lessonDryRun)
                    .toggleStyle(.switch)
                    .disabled(kakao.isBusy || isPreparingSend)
                    .help("켜면 각 학생의 정확한 채팅방과 입력창만 확인하며 메시지·첨부파일은 전송하지 않습니다.")
                Button {
                    requestLessonSend()
                } label: {
                    Label(
                        isPreparingSend ? "준비 중" : (lessonDryRun ? "공지 드라이런" : "공지 바로 발송"),
                        systemImage: lessonDryRun ? "checkmark.shield.fill" : "paperplane.fill"
                    )
                }
                .buttonStyle(.borderedProminent)
                .tint(lessonDryRun ? .blue : .red)
                .disabled(!canPreview || previewRows.isEmpty || kakao.isBusy || isPreparingSend)
            }
            HStack(spacing: 14) {
                Picker("반", selection: $draft.classID) { ForEach(store.database.classes) { Text($0.name).tag($0.id) } }.frame(width: 190)
                Picker("Preset", selection: $draft.presetID) { ForEach(store.database.presets.sorted { $0.updatedAt > $1.updatedAt }) { Text("\($0.name) v\($0.version)").tag($0.id) } }.frame(width: 260)
                DatePicker("날짜", selection: $draft.date, displayedComponents: .date)
                Spacer()
                Text(kakao.statusText).font(.caption).foregroundStyle(.secondary)
            }
            KakaoPreparationGuide()
            if let summary = kakao.lastRunSummary {
                KakaoRunSummaryCard(summary: summary, onDismiss: kakao.clearLastRunSummary)
            }
            if preset?.kind == .direct {
                GroupBox("직접입력 안내") {
                    Text("아래 학생별 입력표의 ‘공지 멘트’를 각 학생에게 그대로 발송합니다. 문구의 첫 문자와 마지막 문자가 큰따옴표(\")이면 그 한 쌍만 자동으로 제거합니다.")
                        .frame(maxWidth: .infinity, alignment: .leading)
                        .padding(6)
                }
            } else {
                VStack(alignment: .leading, spacing: 8) {
                    Text("선생님 수업 입력").font(.headline)
                    LessonTextField(title: "진도", text: lessonTextBinding(\.progress), onBeginEditing: recordUndoSnapshot).frame(height: 82)
                    LessonTextField(title: "숙제", text: lessonTextBinding(\.assignment), onBeginEditing: recordUndoSnapshot).frame(height: 82)
                    LessonTextField(title: "시험단원", text: lessonTextBinding(\.examUnit), onBeginEditing: recordUndoSnapshot).frame(height: 82)
                    LessonTextField(title: "공지", text: lessonTextBinding(\.notice), onBeginEditing: recordUndoSnapshot).frame(height: 82)
                }.frame(maxWidth: .infinity)
            }
            commonMessageSection
            lessonAttachmentFolderSection
            GroupBox("학생별 입력표 · 성명 오름차순") {
                VStack(alignment: .leading, spacing: 8) {
                    HStack {
                        Text("마우스로 셀 범위를 드래그 선택합니다. ⌘C/⌘V로 Excel·Google Sheet처럼 복사·붙여넣고, 더블클릭하면 한 셀을 편집합니다.").font(.caption).foregroundStyle(.secondary)
                        Spacer()
                        Text("발송 \(includedPerformanceRowCount)/\(performanceRows.count)명")
                            .font(.caption).foregroundStyle(.secondary)
                        Button("전체 선택") { setAllPerformanceRowsIncluded(true) }
                            .disabled(performanceRows.allSatisfy(\.isIncluded) || kakao.isBusy || isPreparingSend)
                        Button("전체 해제") { setAllPerformanceRowsIncluded(false) }
                            .disabled(performanceRows.allSatisfy { !$0.isIncluded } || kakao.isBusy || isPreparingSend)
                        Text(selectedPasteDescription).font(.caption.monospaced()).foregroundStyle(.blue)
                        Button("선택 칸부터 범위 붙여넣기") { pastePerformanceRange() }
                            .buttonStyle(.borderedProminent)
                            .disabled(kakao.isBusy || isPreparingSend)
                    }
                    performanceGrid
                }.padding(6)
            }
            lessonPreviewSection
        }.padding(20)
        .onAppear { ensureDraft(); rebuildPerformanceRows() }
        .onChange(of: performanceRows.map(\.isIncluded)) { _, _ in
            invalidateLessonBatchForSelectionChange()
        }
        .onChange(of: isCommonMessageEnabled) { _, _ in
            invalidateLessonBatchForSelectionChange()
        }
        .onChange(of: commonMessage) { _, _ in
            invalidateLessonBatchForSelectionChange()
        }
        .onChange(of: draft.classID) { _, newClassID in
            handleLessonClassChange(newClassID)
        }
        .onChange(of: group?.version) { oldVersion, newVersion in
            guard oldVersion != newVersion else { return }
            rebuildPerformanceRows()
        }
        .task(id: lessonAttachmentScanKey) {
            await scanLessonAttachmentsForPreview()
        }
        .alert("공지 문구에 학생 호칭이 없습니다", isPresented: $showingMissingNicknameWarning) {
            Button("돌아가서 직접 확인", role: .cancel) { }
            Button("무시하고 직접 보내기", role: .destructive) { sendLessonNow() }
        } message: {
            Text("아래 학생은 자신의 호칭이 공지 문구에 포함되어 있지 않습니다. Google Sheet의 행과 문구가 맞는지 확인하세요.\n\n\(missingNicknameWarningText)")
        }
        .alert("첨부파일 발송 확인", isPresented: $showingLessonAttachmentConfirmation) {
            Button("뒤로가기", role: .cancel) { pendingLessonBatch = nil }
            Button("보내기", role: .destructive) {
                guard let batch = pendingLessonBatch else { return }
                pendingLessonBatch = nil
                Task { await kakao.send(batch: batch, dryRun: false, store: store) }
            }
        } message: {
            Text(AttachmentDeliveryNotice.confirmation(in: pendingLessonBatch?.items ?? []) ?? "")
        }
    }

    private var commonMessageSection: some View {
        GroupBox("공통 메시지") {
            VStack(alignment: .leading, spacing: 8) {
                Toggle("공통 메시지 사용", isOn: $isCommonMessageEnabled)
                    .toggleStyle(.checkbox)
                    .disabled(kakao.isBusy || isPreparingSend)
                TextEditor(text: $commonMessage)
                    .frame(height: 82)
                    .disabled(!isCommonMessageEnabled || kakao.isBusy || isPreparingSend)
                    .overlay(RoundedRectangle(cornerRadius: 5).stroke(.quaternary))
                Text(effectiveCommonMessage == nil
                     ? "체크가 꺼져 있거나 공백·줄바꿈만 입력된 경우 사용하지 않습니다."
                     : "발송 체크된 학생마다 학생별 공지 다음에 한 번씩 전송됩니다.")
                    .font(.caption)
                    .foregroundStyle(effectiveCommonMessage == nil ? Color.secondary : Color.blue)
            }.padding(6)
        }
    }

    private var lessonAttachmentFolderSection: some View {
        GroupBox("학생별 첨부파일 폴더") {
            VStack(alignment: .leading, spacing: 8) {
                HStack {
                    TextField("저장매체의 루트 폴더 주소", text: Binding(
                        get: { store.database.attachmentRootPath ?? "" },
                        set: { store.setAttachmentRootPath($0) }
                    ))
                    .textFieldStyle(.roundedBorder)
                    .disabled(kakao.isBusy || isPreparingSend)
                    Button("폴더 선택…") { chooseLessonAttachmentFolder() }
                        .disabled(kakao.isBusy || isPreparingSend)
                    Button(isScanningLessonAttachments ? "스캔 중…" : "최대 3단계 파일 스캔") {
                        lessonAttachmentRefreshRevision &+= 1
                    }
                    .disabled(previewRows.isEmpty || isScanningLessonAttachments || kakao.isBusy || isPreparingSend)
                }
                Text("선택 폴더를 깊이 0으로 보고 최대 깊이 3까지 확인합니다. 파일명에 학교, 학번(예: 25 또는 2025), 정확한 성명이 모두 포함된 모든 일반 파일을 학생별로 연결합니다.")
                    .font(.caption)
                    .foregroundStyle(.secondary)
            }.padding(6)
        }
    }

    private var lessonPreviewSection: some View {
        VStack(alignment: .leading, spacing: 14) {
            HStack {
                Text("학생별 최종 문구 · \(previewRows.count)명").font(.headline)
                Spacer()
                lessonAttachmentStatus
                if let group, let preset {
                    Text(preset.kind == .direct ? "\(group.school) · 학생별 공지 멘트 사용" : "\(group.school) → \(store.academyName(for: group.school, fallback: preset.academyName))")
                        .foregroundStyle(.secondary)
                }
            }
            Table(previewRows, selection: $selectedPreviewStudentID) {
                TableColumn("학생", value: \.studentName).width(75)
                TableColumn("호칭", value: \.nickname).width(75)
                TableColumn("발송 예정 문구") { row in
                    let display = row.error ?? lessonPreviewMessage(for: row)
                    Text(display)
                        .lineLimit(3)
                        .foregroundStyle(row.error == nil ? Color.primary : Color.red)
                        .help(display)
                }.width(min: 410)
                TableColumn("첨부 안내") { row in
                    let paths = lessonAttachmentPaths[row.studentID] ?? []
                    Text(AttachmentDeliveryNotice.preview(studentName: row.studentName, paths: paths) ?? "없음")
                        .foregroundStyle(paths.isEmpty ? Color.secondary : Color.blue)
                        .help(paths.map { URL(fileURLWithPath: $0).lastPathComponent }.joined(separator: "\n"))
                }.width(min: 190)
            }.frame(height: 210)
            selectedLessonMessage
        }
    }

    @ViewBuilder
    private var lessonAttachmentStatus: some View {
        if isScanningLessonAttachments {
            ProgressView().controlSize(.small)
            Text("첨부파일 확인 중").foregroundStyle(.secondary)
        } else if let lessonAttachmentScanError {
            Text("첨부 확인 실패: \(lessonAttachmentScanError)").foregroundStyle(.red).lineLimit(1)
        } else if !(store.database.attachmentRootPath ?? "").trimmingCharacters(in: .whitespacesAndNewlines).isEmpty {
            Text("첨부파일 \(lessonAttachmentCount)개 · \(lessonAttachmentStudentCount)명")
                .foregroundStyle(lessonAttachmentCount == 0 ? Color.secondary : Color.blue)
                .help("루트 포함 최대 깊이 \(AttachmentScanner.maximumDepth)까지 일반 파일 \(lessonAttachmentScannedFileCount)개를 확인했습니다.")
        }
    }

    private var selectedLessonMessage: some View {
        GroupBox("선택 학생 전체 메시지") {
            if let id = selectedPreviewStudentID, let row = previewRows.first(where: { $0.studentID == id }) {
                ScrollView {
                    VStack(alignment: .leading, spacing: 10) {
                        Text(row.error ?? lessonPreviewMessage(for: row)).textSelection(.enabled)
                        let paths = lessonAttachmentPaths[id] ?? []
                        if let notice = AttachmentDeliveryNotice.preview(studentName: row.studentName, paths: paths) {
                            Divider()
                            Text(notice).font(.headline).foregroundStyle(.blue)
                            ForEach(paths, id: \.self) { path in
                                Text("• \(URL(fileURLWithPath: path).lastPathComponent)").textSelection(.enabled)
                            }
                        }
                    }.frame(maxWidth: .infinity, alignment: .leading).padding(8)
                }
            } else {
                Text("학생 행을 클릭하면 잘리지 않은 전체 메시지가 표시됩니다.")
                    .foregroundStyle(.secondary)
                    .frame(maxWidth: .infinity, alignment: .leading)
                    .padding(8)
            }
        }.frame(height: 190)
    }

    private func lessonPreviewMessage(for row: LessonPreviewRow) -> String {
        let messages = CommonMessagePolicy.orderedMessages(
            individualNotice: row.message,
            commonMessage: effectiveCommonMessage
        )
        return messages.enumerated().map { index, message in
            let label = index == 0 && !row.message.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty
                ? "학생별 공지"
                : "공통 메시지"
            return "[\(label)]\n\(message)"
        }.joined(separator: "\n\n")
    }

    private func handleLessonClassChange(_ newClassID: UUID) {
        let defaultID = store.database.classes.first { $0.id == newClassID }?.defaultPresetID
        if let defaultID, store.database.presets.contains(where: { $0.id == defaultID }) {
            draft.presetID = defaultID
        } else if let directID = store.database.presets.first(where: { $0.kind == .direct })?.id {
            draft.presetID = directID
        }
        rebuildPerformanceRows()
    }

    private func chooseLessonAttachmentFolder() {
        let panel = NSOpenPanel()
        panel.canChooseFiles = false
        panel.canChooseDirectories = true
        panel.allowsMultipleSelection = false
        if panel.runModal() == .OK, let path = panel.url?.path {
            store.setAttachmentRootPath(path)
        }
    }

    private var performanceGrid: some View {
        ScrollView([.horizontal, .vertical]) {
            PerformanceSpreadsheet(
                rows: $performanceRows,
                homeworkMaximum: $homeworkMaximum,
                testMaximum: $testMaximum,
                selectedStudentRow: $selectedPasteRow,
                selectedInputColumn: $selectedPasteColumn,
                onBeforeChange: recordUndoSnapshot,
                onUndo: undoLastInput,
                isInteractionEnabled: !kakao.isBusy && !isPreparingSend
            )
            .frame(
                width: 1331,
                height: max(
                    SpreadsheetCanvas.headerHeight + SpreadsheetCanvas.rowHeight * 2,
                    SpreadsheetCanvas.headerHeight + CGFloat(performanceRows.count + 1) * SpreadsheetCanvas.rowHeight
                )
            )
        }
        .frame(height: 330)
        .overlay(RoundedRectangle(cornerRadius: 5).stroke(.quaternary))
    }

    private var selectedPasteDescription: String {
        let columns = ["출석", "태도", "숙제", "테스트", "숙제 코멘트", "테스트 코멘트", "공지 멘트"]
        let student = performanceRows.indices.contains(selectedPasteRow) ? performanceRows[selectedPasteRow].name : "-"
        let column = columns.indices.contains(selectedPasteColumn) ? columns[selectedPasteColumn] : "-"
        return "선택 시작: \(student) · \(column)"
    }

    private func rebuildPerformanceRows() {
        invalidateLessonBatchForSelectionChange()
        guard let group = store.group(id: draft.classID) else { performanceRows = []; return }
        performanceRows = group.members.compactMap { member -> PreparedNoticeRow? in
            guard let student = store.student(id: member.studentID) else { return nil }
            return PreparedNoticeRow(id: student.id, number: 0, name: student.name, nickname: member.nicknameOverride?.nilIfEmpty ?? student.nickname)
        }.sorted { $0.name.localizedStandardCompare($1.name) == .orderedAscending }
        for index in performanceRows.indices { performanceRows[index].number = index + 1 }
        selectedPasteRow = 0; selectedPasteColumn = 0
    }

    private func setAllPerformanceRowsIncluded(_ isIncluded: Bool) {
        guard performanceRows.contains(where: { $0.isIncluded != isIncluded }) else { return }
        recordUndoSnapshot()
        for index in performanceRows.indices { performanceRows[index].isIncluded = isIncluded }
        if !isIncluded { selectedPreviewStudentID = nil }
    }

    private func invalidateLessonBatchForSelectionChange() {
        lessonSelectionRevision &+= 1
        store.currentBatch = nil
        store.validationIssues = []
        if let selectedPreviewStudentID,
           !performanceRows.contains(where: { $0.id == selectedPreviewStudentID && $0.isIncluded }) {
            self.selectedPreviewStudentID = nil
        }
    }

    private func pastePerformanceRange() {
        let text = NSPasteboard.general.string(forType: .string) ?? ""
        let source = BatchParser.parseTSV(text)
        guard !source.isEmpty else { store.banner = "클립보드에 붙여넣을 표 범위가 없습니다."; return }
        recordUndoSnapshot()
        if let headerIndex = source.firstIndex(where: { $0.contains("출석") && ($0.contains("숙제") || $0.contains(where: { $0.contains("숙제") })) }) {
            let header = source[headerIndex]
            let attendanceColumn = header.firstIndex(of: "출석") ?? 3
            let nameColumn = header.firstIndex(where: { $0 == "이름" || $0.contains("성명") })
            let messageColumn = header.firstIndex(where: { $0 == "공지 멘트" || $0 == "notice_message" })
            if source.indices.contains(headerIndex + 1) {
                let totals = source[headerIndex + 1]
                if totals.indices.contains(attendanceColumn + 2), !totals[attendanceColumn + 2].isEmpty { homeworkMaximum = totals[attendanceColumn + 2] }
                if totals.indices.contains(attendanceColumn + 3), !totals[attendanceColumn + 3].isEmpty { testMaximum = totals[attendanceColumn + 3] }
            }
            var sequentialIndex = 0
            for row in source.dropFirst(headerIndex + 1) {
                let targetIndex: Int?
                if let nameColumn, row.indices.contains(nameColumn), !row[nameColumn].trimmingCharacters(in: .whitespacesAndNewlines).isEmpty {
                    targetIndex = performanceRows.firstIndex { $0.name == row[nameColumn].trimmingCharacters(in: .whitespacesAndNewlines) }
                } else if row.indices.contains(attendanceColumn), ["출석", "동영상", "결석"].contains(row[attendanceColumn]), sequentialIndex < performanceRows.count {
                    targetIndex = sequentialIndex; sequentialIndex += 1
                } else { targetIndex = nil }
                guard let targetIndex else { continue }
                for column in 0..<6 where row.indices.contains(attendanceColumn + column) { setPerformanceValue(row[attendanceColumn + column], row: targetIndex, column: column) }
                if let messageColumn, row.indices.contains(messageColumn) { setPerformanceValue(row[messageColumn], row: targetIndex, column: 6) }
            }
            store.banner = "Google Sheet 표와 총 개수를 성명 기준으로 붙여넣었습니다."
            return
        }
        for (rowOffset, sourceRow) in source.enumerated() {
            let targetRow = selectedPasteRow + rowOffset
            guard performanceRows.indices.contains(targetRow) else { break }
            for (columnOffset, value) in sourceRow.enumerated() {
                let targetColumn = selectedPasteColumn + columnOffset
                guard targetColumn < 7 else { break }
                setPerformanceValue(value, row: targetRow, column: targetColumn)
            }
        }
        store.banner = "선택한 시작 칸부터 \(source.count)행 범위를 붙여넣었습니다."
    }

    private func setPerformanceValue(_ value: String, row: Int, column: Int) {
        guard performanceRows.indices.contains(row) else { return }
        switch column {
        case 0: performanceRows[row].attendance = value
        case 1: performanceRows[row].attitude = value
        case 2: performanceRows[row].homework = value
        case 3: performanceRows[row].test = value
        case 4: performanceRows[row].homeworkComment = value
        case 5: performanceRows[row].testComment = value
        default: performanceRows[row].noticeMessage = value
        }
    }

    private func number(_ text: String) -> Double? {
        Double(text.trimmingCharacters(in: .whitespacesAndNewlines).replacingOccurrences(of: ",", with: ""))
    }

    private func lessonTextBinding(_ keyPath: WritableKeyPath<LessonPlan, String>) -> Binding<String> {
        Binding(get: { draft[keyPath: keyPath] }, set: { value in
            guard draft[keyPath: keyPath] != value else { return }
            draft[keyPath: keyPath] = value
        })
    }

    private func recordUndoSnapshot() {
        let snapshot = LessonInputSnapshot(draft: draft, rows: performanceRows, homeworkMaximum: homeworkMaximum, testMaximum: testMaximum)
        guard inputHistory.last != snapshot else { return }
        inputHistory.append(snapshot)
        if inputHistory.count > 100 { inputHistory.removeFirst(inputHistory.count - 100) }
    }

    private func undoLastInput() {
        guard let snapshot = inputHistory.popLast() else { NSSound.beep(); return }
        draft = snapshot.draft
        performanceRows = snapshot.rows
        homeworkMaximum = snapshot.homeworkMaximum
        testMaximum = snapshot.testMaximum
        store.banner = "마지막 입력 작업을 되돌렸습니다."
    }

    private func ensureDraft() {
        guard !store.database.classes.isEmpty, !store.database.presets.isEmpty else { return }
        if !store.database.classes.contains(where: { $0.id == draft.classID }) { draft.classID = store.database.classes[0].id }
        if !store.database.presets.contains(where: { $0.id == draft.presetID }) {
            let classDefaultID = store.group(id: draft.classID)?.defaultPresetID
            draft.presetID = store.database.presets.first(where: { $0.id == classDefaultID })?.id
                ?? store.database.presets.first(where: { $0.kind == .direct })?.id
                ?? store.database.presets[0].id
        }
    }

    private func scanLessonAttachmentsForPreview() async {
        let root = (store.database.attachmentRootPath ?? "").trimmingCharacters(in: .whitespacesAndNewlines)
        guard !root.isEmpty else {
            lessonAttachmentPaths = [:]
            lessonAttachmentScannedFileCount = 0
            lessonAttachmentScanError = nil
            isScanningLessonAttachments = false
            return
        }
        let students = previewRows.compactMap { store.student(id: $0.studentID) }
        guard !students.isEmpty else {
            lessonAttachmentPaths = [:]
            lessonAttachmentScannedFileCount = 0
            lessonAttachmentScanError = nil
            isScanningLessonAttachments = false
            return
        }
        isScanningLessonAttachments = true
        lessonAttachmentScanError = nil
        do {
            let result = try await Task.detached {
                try AttachmentScanner.scan(rootPath: root, students: students)
            }.value
            guard !Task.isCancelled else { return }
            lessonAttachmentPaths = result.filesByStudentID.mapValues { $0.map(\.path) }
            lessonAttachmentScannedFileCount = result.scannedFileCount
        } catch {
            guard !Task.isCancelled else { return }
            lessonAttachmentPaths = [:]
            lessonAttachmentScannedFileCount = 0
            lessonAttachmentScanError = error.localizedDescription
        }
        if !Task.isCancelled { isScanningLessonAttachments = false }
    }

    private func makeLessonBatch(allowEmptyMessages: Bool) -> (SendBatch, [ValidationIssue])? {
        guard let group, let preset else { return nil }
        var issues: [ValidationIssue] = []
        if performanceRows.count != group.members.count {
            issues.append(ValidationIssue(severity: .error, message: "로컬 입력표 행 수와 반 명단 인원이 다릅니다."))
        }
        let includedCount = PreparedNoticeSelection.includedRows(performanceRows).count
        if includedCount == 0 {
            issues.append(ValidationIssue(severity: .error, message: "발송 대상으로 선택한 학생이 없습니다."))
        }
        let items = previewRows.compactMap { row -> BatchItem? in
            if let error = row.error {
                issues.append(ValidationIssue(severity: .error, message: "\(row.studentName): \(error)"))
                return nil
            }
            let messages = CommonMessagePolicy.orderedMessages(
                individualNotice: row.message,
                commonMessage: effectiveCommonMessage
            )
            guard let firstMessage = messages.first else { return nil }
            return BatchItem(
                studentID: row.studentID,
                studentName: row.studentName,
                nickname: row.nickname,
                chatRoomName: row.chatRoomName,
                message: firstMessage,
                chatID: store.student(id: row.studentID)?.chatID,
                additionalMessages: Array(messages.dropFirst()),
                preserveMessageWhitespace: preset.kind == .direct || effectiveCommonMessage != nil
            )
        }
        if items.count != includedCount {
            issues.append(ValidationIssue(severity: .error, message: "선택 학생 \(includedCount)명과 발송 문구 \(items.count)건이 다릅니다."))
        }
        let metadata = BatchMetadata(
            schemaVersion: 1,
            classID: group.id,
            sessionID: draft.id,
            date: Self.koreanDateFormatter.string(from: draft.date),
            presetID: preset.id,
            presetVersion: preset.version
        )
        let batch = SendBatch(metadata: metadata, items: items)
        issues.append(contentsOf: BatchParser.validate(batch: batch, database: store.database, allowEmptyMessages: allowEmptyMessages))
        return (batch, issues)
    }

    private func sendLessonNow() {
        let operationDryRun = lessonDryRun
        let operationSelectionRevision = lessonSelectionRevision
        guard kakao.ensureAccessibilityForOperation() else {
            store.banner = "시스템 설정에서 공지발송 스위치를 켠 뒤 다시 시도해주세요."
            return
        }
        guard let (initialBatch, initialIssues) = makeLessonBatch(allowEmptyMessages: operationDryRun) else {
            store.banner = "반과 Preset을 선택하고 수업 내용을 입력하세요."
            return
        }
        store.currentBatch = initialBatch
        store.validationIssues = initialIssues
        guard !initialIssues.contains(where: { $0.severity == .error }) else {
            store.banner = "발송 검증 오류가 있습니다. 학생별 입력과 최종 문구를 확인하세요."
            return
        }

        isPreparingSend = true
        Task {
            var batch = initialBatch
            do {
                let root = (store.database.attachmentRootPath ?? "").trimmingCharacters(in: .whitespacesAndNewlines)
                if !root.isEmpty {
                    let students = batch.items.compactMap { store.student(id: $0.studentID) }
                    let result = try await Task.detached {
                        try AttachmentScanner.scan(rootPath: root, students: students)
                    }.value
                    for index in batch.items.indices {
                        batch.items[index].attachmentPaths = result.filesByStudentID[batch.items[index].studentID]?.map(\.path) ?? []
                    }
                    lessonAttachmentPaths = result.filesByStudentID.mapValues { $0.map(\.path) }
                    lessonAttachmentScannedFileCount = result.scannedFileCount
                    lessonAttachmentScanError = nil
                } else {
                    for index in batch.items.indices { batch.items[index].attachmentPaths = [] }
                }
                guard operationSelectionRevision == lessonSelectionRevision else {
                    store.currentBatch = nil
                    store.validationIssues = []
                    store.banner = "발송 준비 중 학생 선택이 바뀌어 작업을 취소했습니다. 다시 확인해 주세요."
                    isPreparingSend = false
                    return
                }
                let issues = BatchParser.validate(batch: batch, database: store.database, allowEmptyMessages: operationDryRun)
                store.currentBatch = batch
                store.validationIssues = issues
                guard !issues.contains(where: { $0.severity == .error }) else {
                    store.banner = "첨부파일을 포함한 최종 검증에서 오류가 발생해 발송하지 않았습니다."
                    isPreparingSend = false
                    return
                }
                isPreparingSend = false
                if !operationDryRun, AttachmentDeliveryNotice.confirmation(in: batch.items) != nil {
                    pendingLessonBatch = batch
                    showingLessonAttachmentConfirmation = true
                    return
                }
                await kakao.send(batch: batch, dryRun: operationDryRun, store: store)
            } catch {
                isPreparingSend = false
                store.banner = "발송 준비 실패: \(error.localizedDescription)"
            }
        }
    }

    private func requestLessonSend() {
        guard let preset else {
            sendLessonNow()
            return
        }
        missingNicknameIssues = DirectNoticeNicknameValidator.issuesIfWarningRequired(
            isDryRun: lessonDryRun,
            presetKind: preset.kind,
            items: previewRows.compactMap {
                guard !$0.message.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty else { return nil }
                return (studentID: $0.studentID, studentName: $0.studentName, nickname: $0.nickname, message: $0.message)
            }
        )
        guard !missingNicknameIssues.isEmpty else {
            sendLessonNow()
            return
        }
        showingMissingNicknameWarning = true
    }

    private var missingNicknameWarningText: String {
        missingNicknameIssues.map { issue in
            let nickname = issue.nickname.isEmpty ? "호칭 비어 있음" : "호칭: \(issue.nickname)"
            return "• \(issue.studentName) (\(nickname))"
        }.joined(separator: "\n")
    }

    private static let koreanDateFormatter: DateFormatter = {
        let formatter = DateFormatter(); formatter.locale = Locale(identifier: "ko_KR"); formatter.dateFormat = "M월 d일"; return formatter
    }()
}

private struct LessonTextField: View {
    let title: String
    @Binding var text: String
    let onBeginEditing: () -> Void
    @FocusState private var isFocused: Bool

    var body: some View {
        GroupBox(title) {
            TextEditor(text: $text)
                .focused($isFocused)
                .padding(4)
        }
        .frame(maxWidth: .infinity)
        .onChange(of: isFocused) { wasFocused, isFocused in
            if isFocused && !wasFocused {
                onBeginEditing()
            }
        }
    }
}

private struct KakaoPreparationGuide: View {
    var body: some View {
        GroupBox("발송 전 KakaoTalk 준비") {
            HStack(alignment: .top, spacing: 10) {
                Image(systemName: "bubble.left.and.bubble.right.fill")
                    .foregroundStyle(.yellow)
                VStack(alignment: .leading, spacing: 4) {
                    Text("KakaoTalk을 실행하고 로그인한 뒤 메인 창을 열어두세요. 최소화하거나 창을 닫아두면 안 됩니다.")
                    Text("친구·채팅 중 어느 탭에 있어도 됩니다. 발송을 시작하면 앱이 자동으로 채팅 탭 → 정확한 톡방 이름 검색 → 결과 1개 확인 → 방 열기를 수행합니다.")
                    Text("작업 중에는 마우스와 키보드를 건드리지 마세요. 드라이런은 입력·Enter·첨부 전송을 하지 않습니다.")
                        .foregroundStyle(.secondary)
                }
                .font(.caption)
                Spacer()
            }
            .padding(6)
        }
    }
}

private struct KakaoRunSummaryCard: View {
    let summary: KakaoRunSummary
    let onDismiss: () -> Void
    @State private var showsDetails = false

    private var color: Color {
        if summary.failedCount > 0 { return .red }
        if summary.wasStopped { return .orange }
        return .green
    }

    var body: some View {
        GroupBox("최근 작업 결과") {
            HStack(alignment: .top, spacing: 10) {
                Image(systemName: summary.succeeded ? "checkmark.circle.fill" : "exclamationmark.triangle.fill")
                    .foregroundStyle(color)
                VStack(alignment: .leading, spacing: 4) {
                    Text("\(summary.dryRun ? "드라이런" : "실제 발송") · \(summary.finishedAt.formatted(date: .omitted, time: .standard))")
                        .font(.headline)
                    Text(summary.detail)
                    HStack(spacing: 14) {
                        Text("이번 실행 완료 \(summary.completedThisRun)/\(summary.totalCount)")
                        if summary.alreadySentCount > 0 { Text("기존 발송 완료 \(summary.alreadySentCount)") }
                        if summary.failedCount > 0 { Text("실패 \(summary.failedCount)").foregroundStyle(.red) }
                    }
                    .font(.caption)
                    .foregroundStyle(.secondary)
                }
                Spacer()
                Button("상세 확인") { showsDetails = true }
                    .buttonStyle(.borderedProminent)
                Button("결과 닫기", action: onDismiss)
            }
            .padding(6)
        }
        .sheet(isPresented: $showsDetails) {
            KakaoRunDetailView(summary: summary)
        }
    }
}

private struct KakaoRunDetailView: View {
    let summary: KakaoRunSummary
    @Environment(\.dismiss) private var dismiss

    var body: some View {
        VStack(alignment: .leading, spacing: 12) {
            HStack {
                VStack(alignment: .leading, spacing: 4) {
                    Text(summary.dryRun ? "드라이런 결과" : "실제 발송 결과").font(.title.bold())
                    Text(summary.detail).foregroundStyle(.secondary)
                }
                Spacer()
                Button("닫기") { dismiss() }
                    .keyboardShortcut(.cancelAction)
            }
            Table(summary.items) {
                TableColumn("상태") { Text($0.status.rawValue) }.width(110)
                TableColumn("학생", value: \.studentName).width(90)
                TableColumn("톡방", value: \.chatRoomName).width(min: 210)
                TableColumn("메시지") { item in
                    let text = item.allMessages.joined(separator: "\n\n")
                    Text(text).lineLimit(3).help(text)
                }.width(min: 300)
                TableColumn("첨부") { item in
                    Text("\(item.attachmentPaths?.count ?? 0)개")
                }.width(60)
                TableColumn("오류") { Text($0.error ?? "").foregroundStyle(.red) }.width(min: 160)
            }
        }
        .padding(20)
        .frame(minWidth: 980, minHeight: 540)
    }
}

struct PreparedNoticeRow: Identifiable, Hashable {
    let id: UUID
    var number: Int
    var isIncluded = true
    var name: String
    var nickname: String
    var attendance = "출석"
    var attitude = "3"
    var homework = ""
    var test = ""
    var homeworkComment = ""
    var testComment = ""
    var noticeMessage = ""
}

enum PreparedNoticeSelection {
    static func includedRows(_ rows: [PreparedNoticeRow]) -> [PreparedNoticeRow] {
        rows.filter(\.isIncluded)
    }

    static func directMessagesAreReady(in rows: [PreparedNoticeRow], allowEmptyMessages: Bool = false) -> Bool {
        let included = includedRows(rows)
        return !included.isEmpty && (allowEmptyMessages || included.allSatisfy {
            !DirectNoticeMessage.normalized($0.noticeMessage)
                .trimmingCharacters(in: .whitespacesAndNewlines)
                .isEmpty
        })
    }
}

struct LogsView: View {
    @EnvironmentObject private var store: AppStore
    var body: some View {
        VStack(alignment: .leading, spacing: 12) {
            Text("발송 기록").font(.largeTitle.bold())
            Text("공지 본문은 저장하지 않습니다. SHA-256 해시는 동일한 메시지였는지 확인하기 위한 값입니다.").foregroundStyle(.secondary)
            Table(store.database.logs) {
                TableColumn("시각") { Text($0.sentAt, format: .dateTime.year().month().day().hour().minute().second()) }.width(160)
                TableColumn("결과") { Text($0.result.rawValue) }.width(100)
                TableColumn("학생", value: \.studentName).width(90)
                TableColumn("톡방", value: \.chatRoomName).width(min: 200)
                TableColumn("메시지 해시") { Text($0.messageSHA256).font(.system(.caption, design: .monospaced)).lineLimit(1) }.width(min: 260)
                TableColumn("상세") { Text($0.detail ?? "") }.width(min: 180)
            }
        }.padding(20)
    }
}

struct SchoolSettingsView: View {
    @EnvironmentObject private var store: AppStore
    @State private var newSchool = ""

    private var schools: [String] {
        Set(store.database.students.map(\.school) + store.database.classes.map(\.school) + Array((store.database.schoolAcademies ?? [:]).keys)).filter { !$0.isEmpty }.sorted()
    }

    var body: some View {
        VStack(alignment: .leading, spacing: 16) {
            Text("학교별 Academy 설정").font(.largeTitle.bold())
            Text("반의 학교와 연결된 academy가 Google Sheets 공지 공식에 자동으로 사용됩니다. 설정이 비어 있으면 선택한 Preset의 기본 학원명을 사용합니다.").foregroundStyle(.secondary)
            GroupBox("학교별 학원명") {
                VStack(spacing: 12) {
                    ForEach(schools, id: \.self) { school in
                        HStack {
                            Text(school).font(.headline).frame(width: 100, alignment: .leading)
                            TextField("예: SNT 한성과고", text: Binding(
                                get: { store.database.schoolAcademies?[school] ?? "" },
                                set: { store.setAcademyName($0, for: school) }
                            )).textFieldStyle(.roundedBorder)
                        }
                    }
                    if schools.isEmpty { Text("학생 DB를 가져오거나 아래에서 학교를 추가하세요.").foregroundStyle(.secondary) }
                }.padding(8)
            }
            HStack {
                TextField("새 학교 이름", text: $newSchool).textFieldStyle(.roundedBorder).frame(width: 220)
                Button("학교 추가") {
                    let school = newSchool.trimmingCharacters(in: .whitespacesAndNewlines)
                    guard !school.isEmpty else { return }
                    store.setAcademyName("", for: school); newSchool = ""
                }.disabled(newSchool.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty)
            }
            Spacer()
        }.padding(20)
    }
}
