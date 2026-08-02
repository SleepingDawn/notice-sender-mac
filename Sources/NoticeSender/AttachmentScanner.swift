import Foundation

struct AttachmentScanResult: Sendable {
    var filesByStudentID: [UUID: [URL]]
    var scannedFileCount: Int
}

enum AttachmentScannerError: LocalizedError {
    case emptyPath
    case notDirectory
    case cannotEnumerate

    var errorDescription: String? {
        switch self {
        case .emptyPath: "첨부파일 루트 폴더 주소를 입력해주세요."
        case .notDirectory: "입력한 주소가 접근 가능한 폴더가 아닙니다."
        case .cannotEnumerate: "폴더와 하위 폴더를 읽을 수 없습니다."
        }
    }
}

enum AttachmentScanner {
    static func scan(rootPath: String, students: [Student]) throws -> AttachmentScanResult {
        let expanded = NSString(string: rootPath.trimmingCharacters(in: .whitespacesAndNewlines)).expandingTildeInPath
        guard !expanded.isEmpty else { throw AttachmentScannerError.emptyPath }
        var isDirectory: ObjCBool = false
        guard FileManager.default.fileExists(atPath: expanded, isDirectory: &isDirectory), isDirectory.boolValue else {
            throw AttachmentScannerError.notDirectory
        }
        let root = URL(fileURLWithPath: expanded, isDirectory: true)
        let keys: [URLResourceKey] = [.isRegularFileKey, .isHiddenKey, .isSymbolicLinkKey]
        guard let enumerator = FileManager.default.enumerator(
            at: root,
            includingPropertiesForKeys: keys,
            options: [.skipsHiddenFiles, .skipsPackageDescendants],
            errorHandler: { _, _ in true }
        ) else { throw AttachmentScannerError.cannotEnumerate }

        var files: [URL] = []
        for case let url as URL in enumerator {
            let values = try? url.resourceValues(forKeys: Set(keys))
            guard values?.isRegularFile == true, values?.isHidden != true, values?.isSymbolicLink != true else { continue }
            files.append(url)
        }
        var matches: [UUID: [URL]] = [:]
        for student in students {
            let school = normalized(student.school)
            let name = normalized(student.name)
            let shortYear = String(format: "%02d", student.admissionYear)
            let longYear = "20\(shortYear)"
            matches[student.id] = files.filter { url in
                let filename = normalized(url.deletingPathExtension().lastPathComponent)
                return filename.contains(school) && filename.contains(name) && (filename.contains(shortYear) || filename.contains(longYear))
            }.sorted { $0.lastPathComponent.localizedStandardCompare($1.lastPathComponent) == .orderedAscending }
        }
        return AttachmentScanResult(filesByStudentID: matches, scannedFileCount: files.count)
    }

    private static func normalized(_ value: String) -> String {
        value.precomposedStringWithCanonicalMapping.lowercased().filter { !$0.isWhitespace }
    }
}
