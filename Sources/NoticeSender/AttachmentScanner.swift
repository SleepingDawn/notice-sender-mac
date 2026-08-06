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
    /// The selected root is depth 0. Files below it are inspected through depth 3.
    static let maximumDepth = 3

    static func scan(rootPath: String, students: [Student]) throws -> AttachmentScanResult {
        let expanded = NSString(string: rootPath.trimmingCharacters(in: .whitespacesAndNewlines)).expandingTildeInPath
        guard !expanded.isEmpty else { throw AttachmentScannerError.emptyPath }
        var isDirectory: ObjCBool = false
        guard FileManager.default.fileExists(atPath: expanded, isDirectory: &isDirectory), isDirectory.boolValue else {
            throw AttachmentScannerError.notDirectory
        }
        let root = URL(fileURLWithPath: expanded, isDirectory: true)
        let keys: [URLResourceKey] = [.isRegularFileKey, .isDirectoryKey, .isHiddenKey, .isSymbolicLinkKey]
        guard let enumerator = FileManager.default.enumerator(
            at: root,
            includingPropertiesForKeys: keys,
            options: [.skipsHiddenFiles, .skipsPackageDescendants],
            errorHandler: { _, _ in true }
        ) else { throw AttachmentScannerError.cannotEnumerate }

        var files: [URL] = []
        for case let url as URL in enumerator {
            let values = try? url.resourceValues(forKeys: Set(keys))
            if values?.isSymbolicLink == true {
                enumerator.skipDescendants()
                continue
            }
            if enumerator.level >= maximumDepth, values?.isDirectory == true {
                enumerator.skipDescendants()
            }
            guard enumerator.level <= maximumDepth,
                  values?.isRegularFile == true,
                  values?.isHidden != true
            else { continue }
            files.append(url)
        }

        let identities = students.map { student in
            (
                id: student.id,
                school: normalized(student.school),
                // Attachments must always use the student's full DB name.
                // Nicknames are intentionally excluded: "홍길동" must not
                // match a file that only contains the nickname "길동이".
                fullName: normalized(student.name),
                shortYear: AdmissionYearPolicy.formatted(student.admissionYear)
            )
        }
        var matches = Dictionary(uniqueKeysWithValues: students.map { ($0.id, [URL]()) })
        for file in files {
            let filename = normalized(file.deletingPathExtension().lastPathComponent)
            let candidates = identities.filter { identity in
                guard !identity.school.isEmpty, !identity.fullName.isEmpty else { return false }
                let longYear = "20\(identity.shortYear)"
                return filename.contains(identity.school)
                    && filename.contains(identity.fullName)
                    && (filename.contains(identity.shortYear) || filename.contains(longYear))
            }

            // If both `윤서진` and `윤서진A` exist, a file containing 윤서진A
            // belongs only to the explicitly suffixed identity. Unrelated names
            // can still share a file when every required identity token is present.
            let resolved = candidates.filter { candidate in
                !candidates.contains { other in
                    other.id != candidate.id
                        && other.fullName.count > candidate.fullName.count
                        && other.fullName.hasPrefix(candidate.fullName)
                }
            }
            for candidate in resolved {
                matches[candidate.id, default: []].append(file)
            }
        }
        for studentID in Array(matches.keys) {
            matches[studentID]?.sort {
                $0.lastPathComponent.localizedStandardCompare($1.lastPathComponent) == .orderedAscending
            }
        }
        return AttachmentScanResult(filesByStudentID: matches, scannedFileCount: files.count)
    }

    private static func normalized(_ value: String) -> String {
        value.precomposedStringWithCanonicalMapping.lowercased().filter { !$0.isWhitespace }
    }
}

enum AttachmentDeliveryNotice {
    static func preview(studentName: String, paths: [String]) -> String? {
        guard !paths.isEmpty else { return nil }
        return "\(studentName) 학생에게 \(paths.count)개 파일이 전송됩니다."
    }

    static func matchedStudentNames(in items: [BatchItem]) -> [String] {
        var seen: Set<UUID> = []
        return items.compactMap { item in
            guard !(item.attachmentPaths ?? []).isEmpty, seen.insert(item.studentID).inserted else { return nil }
            return item.studentName
        }
    }

    static func confirmation(in items: [BatchItem]) -> String? {
        let names = matchedStudentNames(in: items)
        guard !names.isEmpty else { return nil }
        return "[\(names.joined(separator: ", "))]에게 파일이 함께 발송됩니다."
    }
}
