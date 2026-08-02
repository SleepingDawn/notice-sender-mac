import AppKit
import CryptoKit
import Foundation
import UniformTypeIdentifiers

@MainActor
final class AppStore: ObservableObject {
    @Published var database: AppDatabase
    @Published var selectedClassID: UUID?
    @Published var currentBatch: SendBatch?
    @Published var validationIssues: [ValidationIssue] = []
    @Published var banner: String?
    @Published var isSyncingStudentsFromKakao = false

    private let databaseURL: URL
    private let encoder: JSONEncoder
    private let decoder: JSONDecoder

    init(databaseURL: URL? = nil) {
        let base = databaseURL ?? Self.defaultDatabaseURL()
        self.databaseURL = base
        encoder = JSONEncoder()
        encoder.outputFormatting = [.prettyPrinted, .sortedKeys]
        encoder.dateEncodingStrategy = .iso8601
        decoder = JSONDecoder()
        decoder.dateDecodingStrategy = .iso8601
        if let data = try? Data(contentsOf: base), let decoded = try? decoder.decode(AppDatabase.self, from: data) {
            database = decoded
        } else {
            database = AppDatabase()
        }
        var migratedDatabase = false
        if database.schemaVersion < 2 {
            for index in database.students.indices {
                database.students[index].nickname = NicknameGenerator.generate(from: database.students[index].name)
            }
            for classIndex in database.classes.indices {
                for memberIndex in database.classes[classIndex].members.indices {
                    database.classes[classIndex].members[memberIndex].nicknameOverride = nil
                }
            }
            database.schemaVersion = 2
            migratedDatabase = true
        }
        if database.schemaVersion < 4 {
            for defaultPreset in DefaultPresets.all where !database.presets.contains(where: { $0.kind == defaultPreset.kind && $0.name == defaultPreset.name }) {
                database.presets.append(defaultPreset)
            }
            for index in database.presets.indices where database.presets[index].kind == .mock && database.presets[index].mockExamCount == nil {
                database.presets[index].mockExamCount = 3
            }
            database.schemaVersion = 4
            migratedDatabase = true
        }
        if database.schemaVersion < 5 {
            if !database.presets.contains(where: { $0.kind == .direct }) {
                database.presets.insert(DefaultPresets.direct, at: 0)
            }
            if let directPresetID = database.presets.first(where: { $0.kind == .direct })?.id {
                for index in database.classes.indices {
                    database.classes[index].defaultPresetID = directPresetID
                }
            }
            database.schemaVersion = 5
            migratedDatabase = true
        }
        if database.schemaVersion < 6 {
            database.operatingAdmissionYears = OperatingAdmissionYearPolicy.defaultYears()
            database.schemaVersion = 6
            migratedDatabase = true
        } else if let configuredYears = database.operatingAdmissionYears {
            let normalizedYears = OperatingAdmissionYearPolicy.normalized(configuredYears)
            if configuredYears != normalizedYears {
                database.operatingAdmissionYears = normalizedYears
                migratedDatabase = true
            }
        } else {
            database.operatingAdmissionYears = OperatingAdmissionYearPolicy.defaultYears()
            migratedDatabase = true
        }
        for index in database.classes.indices {
            let sortedMembers = ClassMemberSorter.sorted(database.classes[index].members, students: database.students)
            if database.classes[index].members != sortedMembers {
                database.classes[index].members = sortedMembers
                migratedDatabase = true
            }
        }
        if migratedDatabase { save() }
    }

    static func defaultDatabaseURL() -> URL {
        if let override = ProcessInfo.processInfo.environment["NOTICE_SENDER_DATA_DIR"] {
            return URL(fileURLWithPath: override).appendingPathComponent("database.json")
        }
        let root = FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask)[0]
        return root.appendingPathComponent("NoticeSender", isDirectory: true).appendingPathComponent("database.json")
    }

    func save() {
        do {
            try FileManager.default.createDirectory(at: databaseURL.deletingLastPathComponent(), withIntermediateDirectories: true)
            try encoder.encode(database).write(to: databaseURL, options: .atomic)
        } catch {
            banner = "저장 실패: \(error.localizedDescription)"
        }
    }

    func addStudent(_ student: Student) {
        var normalized = student
        normalized.nickname = NicknameGenerator.generate(from: normalized.name)
        database.students.append(normalized)
        save()
    }

    func updateStudent(_ student: Student) {
        guard let index = database.students.firstIndex(where: { $0.id == student.id }) else { return }
        var normalized = student
        normalized.nickname = NicknameGenerator.generate(from: normalized.name)
        database.students[index] = normalized
        for classIndex in database.classes.indices {
            database.classes[classIndex].members = ClassMemberSorter.sorted(database.classes[classIndex].members, students: database.students)
        }
        save()
    }

    func deleteStudents(at offsets: IndexSet, from filtered: [Student]) {
        let ids = Set(offsets.map { filtered[$0].id })
        deleteStudents(ids: ids)
    }

    func deleteStudents(ids: Set<UUID>) {
        guard !ids.isEmpty else { return }
        database.students.removeAll { ids.contains($0.id) }
        for index in database.classes.indices {
            let previousCount = database.classes[index].members.count
            database.classes[index].members.removeAll { ids.contains($0.studentID) }
            if database.classes[index].members.count != previousCount {
                database.classes[index].version += 1
            }
        }
        if currentBatch?.items.contains(where: { ids.contains($0.studentID) }) == true {
            currentBatch = nil
            validationIssues = []
        }
        save()
    }

    func createClass(name: String, school: String, year: Int, studentIDs: [UUID]) {
        let directPresetID = database.presets.first(where: { $0.kind == .direct })?.id
        let members = ClassMemberSorter.sorted(studentIDs.map { ClassMember(studentID: $0) }, students: database.students)
        database.classes.append(ClassGroup(name: name, school: school, admissionYear: year, members: members, defaultPresetID: directPresetID))
        save()
    }

    func updateClass(_ group: ClassGroup) {
        guard let index = database.classes.firstIndex(where: { $0.id == group.id }) else { return }
        var changed = group
        changed.members = ClassMemberSorter.sorted(changed.members, students: database.students)
        changed.version = database.classes[index].version + 1
        database.classes[index] = changed
        save()
    }

    func deleteClass(id: UUID) {
        database.classes.removeAll { $0.id == id }
        if selectedClassID == id { selectedClassID = nil }
        if currentBatch?.metadata.classID == id {
            currentBatch = nil
            validationIssues = []
        }
        save()
    }

    func deleteAllClasses() {
        database.classes.removeAll()
        selectedClassID = nil
        currentBatch = nil
        validationIssues = []
        save()
    }

    func exportClassArchive(to url: URL) throws {
        let archive = try ClassArchiveBuilder.make(database: database)
        try encoder.encode(archive).write(to: url, options: .atomic)
    }

    @discardableResult
    func importClassArchive(from url: URL) throws -> ClassArchiveImportSummary {
        let archive = try decoder.decode(ClassArchive.self, from: Data(contentsOf: url))
        guard archive.schemaVersion == ClassArchive.currentSchemaVersion else {
            throw ClassArchiveError.unsupportedSchema(archive.schemaVersion)
        }
        guard !archive.classes.isEmpty else { throw ClassArchiveError.noClasses }

        var addedStudents = 0
        var reusedStudents = 0
        var addedClasses = 0
        var updatedClasses = 0
        var skippedMembers = 0
        var localStudentIDByArchivedID: [UUID: UUID] = [:]

        for archivedStudent in archive.students {
            if let existing = database.students.first(where: { $0.id == archivedStudent.id }) {
                localStudentIDByArchivedID[archivedStudent.id] = existing.id
                reusedStudents += 1
                continue
            }
            let identityMatches = database.students.filter { $0.duplicateKey == archivedStudent.duplicateKey }
            if identityMatches.count == 1, let existing = identityMatches.first {
                localStudentIDByArchivedID[archivedStudent.id] = existing.id
                reusedStudents += 1
            } else if identityMatches.isEmpty {
                database.students.append(archivedStudent)
                localStudentIDByArchivedID[archivedStudent.id] = archivedStudent.id
                addedStudents += 1
            }
        }

        var localPresetIDByArchivedID: [UUID: UUID] = [:]
        for archivedPreset in archive.presets {
            if let existing = database.presets.first(where: { $0.id == archivedPreset.id })
                ?? database.presets.first(where: { $0.kind == archivedPreset.kind && $0.name == archivedPreset.name }) {
                localPresetIDByArchivedID[archivedPreset.id] = existing.id
            } else {
                database.presets.append(archivedPreset)
                localPresetIDByArchivedID[archivedPreset.id] = archivedPreset.id
            }
        }
        let directPresetID = database.presets.first(where: { $0.kind == .direct })?.id

        for archivedClass in archive.classes {
            var mapped = archivedClass
            mapped.members = archivedClass.members.compactMap { member in
                guard let localStudentID = localStudentIDByArchivedID[member.studentID] else {
                    skippedMembers += 1
                    return nil
                }
                return ClassMember(studentID: localStudentID, nicknameOverride: member.nicknameOverride)
            }
            mapped.members = ClassMemberSorter.sorted(mapped.members, students: database.students)
            mapped.defaultPresetID = archivedClass.defaultPresetID.flatMap { localPresetIDByArchivedID[$0] } ?? directPresetID

            let identityMatches = database.classes.indices.filter {
                database.classes[$0].name == archivedClass.name
                    && database.classes[$0].school == archivedClass.school
                    && database.classes[$0].admissionYear == archivedClass.admissionYear
            }
            let targetIndex = database.classes.firstIndex(where: { $0.id == archivedClass.id })
                ?? (identityMatches.count == 1 ? identityMatches.first : nil)
            if let targetIndex {
                mapped.id = database.classes[targetIndex].id
                mapped.version = max(database.classes[targetIndex].version, archivedClass.version) + 1
                database.classes[targetIndex] = mapped
                updatedClasses += 1
            } else {
                mapped.version = max(1, archivedClass.version)
                database.classes.append(mapped)
                addedClasses += 1
            }
        }

        currentBatch = nil
        validationIssues = []
        save()
        return ClassArchiveImportSummary(
            addedStudents: addedStudents,
            reusedStudents: reusedStudents,
            addedClasses: addedClasses,
            updatedClasses: updatedClasses,
            skippedMembers: skippedMembers
        )
    }

    func addPresetVersion(from preset: MessagePreset) {
        var next = preset
        next.id = UUID()
        next.version = (database.presets.filter { $0.kind == preset.kind }.map(\.version).max() ?? 0) + 1
        next.updatedAt = .now
        database.presets.append(next)
        save()
    }

    func saveLessonPlan(_ plan: LessonPlan) {
        var plans = database.lessonPlans ?? []
        var updated = plan
        updated.updatedAt = .now
        if let index = plans.firstIndex(where: { $0.id == plan.id }) { plans[index] = updated }
        else { plans.insert(updated, at: 0) }
        database.lessonPlans = plans
        save()
    }

    func deleteLessonPlan(id: UUID) {
        database.lessonPlans?.removeAll { $0.id == id }
        save()
    }

    func student(id: UUID) -> Student? { database.students.first { $0.id == id } }
    func group(id: UUID?) -> ClassGroup? { database.classes.first { $0.id == id } }

    func duplicateStudents() -> [String: [Student]] {
        Dictionary(grouping: database.students, by: \.duplicateKey).filter { $0.value.count > 1 }
    }

    func duplicateChatRooms() -> [String] {
        Dictionary(grouping: database.students.filter(\.isActive), by: \.chatRoomName)
            .filter { room, students in
                guard !room.isEmpty, students.count > 1 else { return false }
                let ids = students.compactMap { $0.chatID?.nilIfEmpty }
                return ids.count != students.count || Set(ids).count != ids.count
            }
            .map(\.key).sorted()
    }

    var operatingAdmissionYears: [Int] {
        database.operatingAdmissionYears ?? OperatingAdmissionYearPolicy.defaultYears()
    }

    func setOperatingAdmissionYears(_ years: [Int]) {
        database.operatingAdmissionYears = OperatingAdmissionYearPolicy.normalized(years)
        database.schemaVersion = max(database.schemaVersion, 6)
        save()
    }

    func academyName(for school: String, fallback: String = "") -> String {
        let configured = database.schoolAcademies?[school]?.trimmingCharacters(in: .whitespacesAndNewlines) ?? ""
        return configured.isEmpty ? fallback : configured
    }

    func setAcademyName(_ value: String, for school: String) {
        var settings = database.schoolAcademies ?? [:]
        settings[school] = value
        database.schoolAcademies = settings
        save()
    }

    func setAttachmentRootPath(_ value: String) {
        database.attachmentRootPath = value
        save()
    }

    func importWorkbook(url: URL) async {
        do {
            let result = try XLSXMigrationImporter.importWorkbook(at: url)
            func identity(_ student: Student) -> String { "\(student.name)|\(student.school)|\(student.admissionYear)|\(student.chatRoomName)" }
            var localIDByImportedID: [UUID: UUID] = [:]
            for imported in result.students {
                if let existing = database.students.first(where: { identity($0) == identity(imported) }) {
                    localIDByImportedID[imported.id] = existing.id
                } else {
                    database.students.append(imported)
                    localIDByImportedID[imported.id] = imported.id
                }
            }
            for importedClass in result.classes where !database.classes.contains(where: { $0.name == importedClass.name }) {
                var mapped = importedClass
                mapped.members = importedClass.members.compactMap { member in
                    guard let localID = localIDByImportedID[member.studentID] else { return nil }
                    return ClassMember(studentID: localID, nicknameOverride: member.nicknameOverride)
                }
                mapped.members = ClassMemberSorter.sorted(mapped.members, students: database.students)
                if !mapped.members.isEmpty { database.classes.append(mapped) }
            }
            save()
            banner = "학생 \(result.students.count)명과 반 \(result.classes.count)개를 읽었습니다. 중복 후보는 발송 전에 확인하세요."
        } catch {
            banner = "가져오기 실패: \(error.localizedDescription)"
        }
    }

    func importStudentFile(url: URL) async {
        do {
            let imported = try StudentFileImporter.importRecords(at: url)
            func studentIdentity(_ student: Student) -> String { "\(student.name)|\(student.school)|\(student.admissionYear)" }
            var additions = 0
            var updates = 0
            var skipped = 0
            for record in imported {
                let student = record.student
                let idMatch = record.sourceID.flatMap { sourceID in
                    database.students.firstIndex(where: { $0.id == sourceID })
                }
                let identityMatches = database.students.indices.filter { studentIdentity(database.students[$0]) == studentIdentity(student) }
                let targetIndex = idMatch ?? (identityMatches.count == 1 ? identityMatches.first : nil)
                let sourceIDIsAvailable = record.sourceID.map { sourceID in
                    !database.students.contains(where: { $0.id == sourceID })
                } ?? true
                if let index = targetIndex {
                    var merged = student
                    merged.id = database.students[index].id
                    if !record.nicknameProvided { merged.nickname = database.students[index].nickname }
                    if !record.chatIDProvided { merged.chatID = database.students[index].chatID }
                    if !record.statusProvided { merged.isActive = database.students[index].isActive }
                    database.students[index] = merged
                    updates += 1
                } else if identityMatches.isEmpty, sourceIDIsAvailable {
                    database.students.append(student)
                    additions += 1
                } else {
                    skipped += 1
                }
            }
            for classIndex in database.classes.indices {
                database.classes[classIndex].members = ClassMemberSorter.sorted(database.classes[classIndex].members, students: database.students)
            }
            currentBatch = nil
            validationIssues = []
            save()
            banner = "학생 \(additions)명을 등록하고 기존 학생 \(updates)명을 갱신했습니다. 중복·충돌 후보 \(skipped)건은 건너뛰었습니다."
        } catch {
            banner = "학생 DB 가져오기 실패: \(error.localizedDescription)"
        }
    }

    func exportStudentCSV(to url: URL) throws {
        try StudentDatabaseCSV.write(students: database.students, to: url)
    }

    func syncStudentsFromKakaoChats() async {
        guard !isSyncingStudentsFromKakao else { return }
        let allowedYears = Set(operatingAdmissionYears)
        guard !allowedYears.isEmpty else {
            banner = "학생 DB에서 운영 학번을 한 개 이상 설정해주세요."
            return
        }
        isSyncingStudentsFromKakao = true
        let yearText = allowedYears.sorted().map { String(format: "%02d", $0) }.joined(separator: ", ")
        banner = "운영 학번 \(yearText)의 카카오톡 최근 채팅방을 읽고 있습니다. 최대 1,000개까지 확인합니다."
        defer { isSyncingStudentsFromKakao = false }
        do {
            let chats = try await KmsgSafeAdapter().listChats(limit: 1_000)
            let output = KakaoStudentDBSynchronizer.synchronize(
                chats: chats,
                currentStudents: database.students,
                allowedAdmissionYears: allowedYears
            )
            database.students = output.students
            save()
            banner = output.report.summary
        } catch {
            banner = "카카오톡 학생 DB 업데이트 실패: \(error.localizedDescription)"
        }
    }

    func exportBackup(to url: URL) throws {
        try encoder.encode(database).write(to: url, options: .atomic)
    }

    func restoreBackup(from url: URL) throws {
        database = try decoder.decode(AppDatabase.self, from: Data(contentsOf: url))
        if database.schemaVersion < 6 || database.operatingAdmissionYears == nil {
            database.operatingAdmissionYears = OperatingAdmissionYearPolicy.defaultYears()
            database.schemaVersion = max(database.schemaVersion, 6)
        } else {
            database.operatingAdmissionYears = OperatingAdmissionYearPolicy.normalized(database.operatingAdmissionYears ?? [])
        }
        currentBatch = nil
        validationIssues = []
        save()
    }

    func log(item: BatchItem, batchID: UUID, result: BatchItemStatus, detail: String?) {
        let combinedMessages = item.allMessages.joined(separator: "\u{001E}")
        let digest = SHA256.hash(data: Data(combinedMessages.utf8)).map { String(format: "%02x", $0) }.joined()
        database.logs.insert(SendLog(
            batchID: batchID,
            studentID: item.studentID,
            studentName: item.studentName,
            chatRoomName: item.chatRoomName,
            sentAt: .now,
            result: result,
            messageSHA256: digest,
            detail: detail
        ), at: 0)
        save()
    }
}
