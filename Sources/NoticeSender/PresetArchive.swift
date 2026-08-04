import Foundation

struct PresetArchive: Codable, Sendable {
    static let currentSchemaVersion = 1

    var schemaVersion: Int = currentSchemaVersion
    var exportedAt: Date = .now
    var preset: MessagePreset
}

enum PresetArchiveError: LocalizedError {
    case unsupportedSchema(Int)
    case invalidPreset(String)
    case presetNotFound

    var errorDescription: String? {
        switch self {
        case .unsupportedSchema(let version):
            "지원하지 않는 Preset 파일 버전입니다: \(version)"
        case .invalidPreset(let message):
            "Preset 파일의 문구가 올바르지 않습니다: \(message)"
        case .presetNotFound:
            "내보낼 Preset을 찾을 수 없습니다."
        }
    }
}

enum PresetManagementError: LocalizedError {
    case emptyName
    case presetNotFound
    case cannotDeleteLastPreset

    var errorDescription: String? {
        switch self {
        case .emptyName: "Preset 이름을 입력해주세요."
        case .presetNotFound: "Preset을 찾을 수 없습니다."
        case .cannotDeleteLastPreset: "마지막 Preset은 삭제할 수 없습니다."
        }
    }
}
