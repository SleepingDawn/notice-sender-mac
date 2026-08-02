import Foundation

struct ClassArchive: Codable, Sendable {
    static let currentSchemaVersion = 1

    var schemaVersion: Int = currentSchemaVersion
    var exportedAt: Date = .now
    var students: [Student]
    var classes: [ClassGroup]
    var presets: [MessagePreset]
}

struct ClassArchiveImportSummary: Sendable {
    var addedStudents: Int
    var reusedStudents: Int
    var addedClasses: Int
    var updatedClasses: Int
    var skippedMembers: Int

    var message: String {
        "반 파일을 읽어 학생 \(addedStudents)명 추가·\(reusedStudents)명 연결, 반 \(addedClasses)개 추가·\(updatedClasses)개 갱신했습니다. 연결하지 못한 명단 \(skippedMembers)건은 제외했습니다."
    }
}

enum ClassArchiveError: LocalizedError {
    case unsupportedSchema(Int)
    case noClasses

    var errorDescription: String? {
        switch self {
        case .unsupportedSchema(let version): "지원하지 않는 반 파일 버전입니다: \(version)"
        case .noClasses: "반 파일에 저장된 반이 없습니다."
        }
    }
}

enum ClassArchiveBuilder {
    static func make(database: AppDatabase) throws -> ClassArchive {
        guard !database.classes.isEmpty else { throw ClassArchiveError.noClasses }
        let studentIDs = Set(database.classes.flatMap { $0.members.map(\.studentID) })
        let presetIDs = Set(database.classes.compactMap(\.defaultPresetID))
        return ClassArchive(
            students: database.students.filter { studentIDs.contains($0.id) },
            classes: database.classes,
            presets: database.presets.filter { presetIDs.contains($0.id) }
        )
    }
}

struct DirectNoticeNicknameIssue: Identifiable, Hashable, Sendable {
    var id: UUID { studentID }
    var studentID: UUID
    var studentName: String
    var nickname: String
}

enum DirectNoticeNicknameValidator {
    static func issuesIfWarningRequired(
        isDryRun: Bool,
        presetKind: PresetKind,
        items: [(studentID: UUID, studentName: String, nickname: String, message: String)]
    ) -> [DirectNoticeNicknameIssue] {
        guard !isDryRun, presetKind == .direct else { return [] }
        return missingNicknameIssues(in: items)
    }

    static func missingNicknameIssues(
        in items: [(studentID: UUID, studentName: String, nickname: String, message: String)]
    ) -> [DirectNoticeNicknameIssue] {
        items.compactMap { item in
            let nickname = item.nickname.trimmingCharacters(in: .whitespacesAndNewlines)
            let normalizedNickname = nickname.precomposedStringWithCanonicalMapping
            let normalizedMessage = item.message.precomposedStringWithCanonicalMapping
            guard normalizedNickname.isEmpty || !normalizedMessage.contains(normalizedNickname) else { return nil }
            return DirectNoticeNicknameIssue(
                studentID: item.studentID,
                studentName: item.studentName,
                nickname: nickname
            )
        }
    }
}
