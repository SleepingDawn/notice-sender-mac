import Foundation
import KmsgSafeCore

struct KakaoStudentDBSyncReport: Sendable {
    var scannedChats: Int
    var recognizedStudentRooms: Int
    var addedStudents: Int
    var updatedStudents: Int
    var unchangedStudents: Int
    var skippedIdentities: Int
    var multipleRoomSelections: Int

    var summary: String {
        "카카오톡 \(scannedChats)개 방 확인 · 학생방 \(recognizedStudentRooms)개 인식 · 신규 \(addedStudents)명 · 기존 \(updatedStudents)명 갱신 · 최신 방 선택 \(multipleRoomSelections)명 · 변경 없음 \(unchangedStudents)명 · 검토 필요 \(skippedIdentities)명"
    }
}

struct KakaoStudentDBSyncOutput: Sendable {
    var students: [Student]
    var report: KakaoStudentDBSyncReport
}

struct KakaoStudentRoomCandidate: Sendable {
    var name: String
    var school: String
    var admissionYear: Int
    var title: String
    var chatID: String?
    var lastMessage: String?
    var listIndex: Int
    var hasStandardSuffix: Bool

    var identityKey: String { "\(school)|\(admissionYear)|\(name)" }
}

enum KakaoStudentRoomParser {
    private static let aliases = [
        "인천영": "인천",
        "경기": "경기",
        "대구": "대구",
        "대전": "대전",
        "세종": "세종",
        "인천": "인천",
        "한성": "한성",
    ]

    static func parse(_ chat: KmsgEmbeddedChat, knownSchools: Set<String>) -> KakaoStudentRoomCandidate? {
        let exactTitle = chat.title.trimmingCharacters(in: .whitespacesAndNewlines)
        let title = normalizeTitle(exactTitle)
        guard title.contains("화학") else { return nil }

        var schoolMap = aliases
        for school in knownSchools {
            let normalized = school.trimmingCharacters(in: .whitespacesAndNewlines)
            if !normalized.isEmpty { schoolMap[normalized] = normalized }
        }
        let schoolPattern = schoolMap.keys
            .sorted { $0.count > $1.count }
            .map(NSRegularExpression.escapedPattern(for:))
            .joined(separator: "|")
        guard !schoolPattern.isEmpty else { return nil }

        let patterns = [
            "^(?<school>\(schoolPattern))(?:과학고|과고)?\\s*[._-]?\\s*(?<year>\\d{2})\\s+(?<name>[가-힣]{2,4}[A-Za-z]?)",
            "^(?<year>\\d{2})\\s*[._-]?\\s*(?<school>\(schoolPattern))(?:과학고|과고)?\\s+(?<name>[가-힣]{2,4}[A-Za-z]?)",
            "^(?<school>\(schoolPattern))(?:과학고|과고)?\\s*(?<year>\\d{2})\\s*(?<name>[가-힣]{2,4}[A-Za-z]?)",
            "^(?<year>\\d{2})\\s*[._-]?\\s*(?<school>\(schoolPattern))(?:과학고|과고)?\\s*(?<name>[가-힣]{2,4}[A-Za-z]?)",
        ]

        let fullRange = NSRange(title.startIndex..<title.endIndex, in: title)
        for pattern in patterns {
            let regex = try! NSRegularExpression(pattern: pattern, options: [.caseInsensitive])
            guard let match = regex.firstMatch(in: title, range: fullRange),
                  let schoolToken = capture("school", match: match, in: title),
                  let yearText = capture("year", match: match, in: title),
                  let year = Int(yearText),
                  let name = capture("name", match: match, in: title)
            else { continue }

            let suffixStart = match.range.location + match.range.length
            let suffixRange = NSRange(location: suffixStart, length: max(0, (title as NSString).length - suffixStart))
            let suffix = (title as NSString).substring(with: suffixRange).trimmingCharacters(in: .whitespacesAndNewlines)
            return KakaoStudentRoomCandidate(
                name: name,
                school: schoolMap[schoolToken] ?? schoolToken,
                admissionYear: year,
                title: exactTitle,
                chatID: chat.chatID,
                lastMessage: chat.lastMessage,
                listIndex: chat.listIndex,
                hasStandardSuffix: isStandardSuffix(suffix)
            )
        }
        return nil
    }

    private static func capture(_ name: String, match: NSTextCheckingResult, in text: String) -> String? {
        let range = match.range(withName: name)
        guard range.location != NSNotFound, let swiftRange = Range(range, in: text) else { return nil }
        return String(text[swiftRange])
    }

    private static func normalizeTitle(_ value: String) -> String {
        value.precomposedStringWithCanonicalMapping
            .split(whereSeparator: \.isWhitespace)
            .joined(separator: " ")
            .trimmingCharacters(in: .whitespacesAndNewlines)
    }

    private static func isStandardSuffix(_ suffix: String) -> Bool {
        let pattern = #"^(?:화학\s*(?:단톡방|단체방|개별방|개인방|개발방|방)|섭씨화학\s*(?:단톡방|톡방)|김요섭T?\s*화학방|\([^)]{1,12}\)\s*(?:화학\s*(?:단톡방|단체방|개별방|개인방)|김요섭T?\s*화학방)|AP\s*화학\s*단톡방)$"#
        return suffix.range(of: pattern, options: [.regularExpression, .caseInsensitive]) != nil
    }
}

enum KakaoStudentDBSynchronizer {
    static func synchronize(
        chats: [KmsgEmbeddedChat],
        currentStudents: [Student],
        allowedAdmissionYears: Set<Int>? = nil,
        currentYear: Int = Calendar.current.component(.year, from: .now) % 100
    ) -> KakaoStudentDBSyncOutput {
        var students = currentStudents
        let knownSchools = Set(currentStudents.map(\.school))
        let parsed = chats.compactMap { KakaoStudentRoomParser.parse($0, knownSchools: knownSchools) }
        let allowedYears = allowedAdmissionYears ?? Set((0...2).map { (currentYear - $0 + 100) % 100 })
        let groups = Dictionary(grouping: parsed, by: \.identityKey)
        var added = 0
        var updated = 0
        var unchanged = 0
        var skipped = 0
        var multipleSelections = 0

        for key in groups.keys.sorted() {
            guard let allRows = groups[key] else { continue }
            let rows = allRows.filter { $0.hasStandardSuffix && allowedYears.contains($0.admissionYear) }
            guard !rows.isEmpty else {
                skipped += 1
                continue
            }
            let ordered = rows.sorted {
                let lhsHasMessage = !($0.lastMessage ?? "").trimmingCharacters(in: .whitespacesAndNewlines).isEmpty
                let rhsHasMessage = !($1.lastMessage ?? "").trimmingCharacters(in: .whitespacesAndNewlines).isEmpty
                if lhsHasMessage != rhsHasMessage { return lhsHasMessage && !rhsHasMessage }
                return $0.listIndex < $1.listIndex
            }
            if ordered.count > 1,
               !ordered.contains(where: { !($0.lastMessage ?? "").trimmingCharacters(in: .whitespacesAndNewlines).isEmpty }) {
                skipped += 1
                continue
            }
            guard let selected = ordered.first,
                  let selectedChatID = selected.chatID?.trimmingCharacters(in: .whitespacesAndNewlines),
                  !selectedChatID.isEmpty
            else {
                skipped += 1
                continue
            }
            if ordered.count > 1 { multipleSelections += 1 }

            let matches = students.indices.filter {
                students[$0].name == selected.name &&
                students[$0].school == selected.school &&
                students[$0].admissionYear == selected.admissionYear
            }
            let chatIDIsUsedElsewhere: (Int?) -> Bool = { excludedIndex in
                students.indices.contains { index in
                    if index == excludedIndex { return false }
                    return students[index].chatID?.trimmingCharacters(in: .whitespacesAndNewlines) == selectedChatID
                }
            }

            if matches.isEmpty {
                guard !chatIDIsUsedElsewhere(nil) else {
                    skipped += 1
                    continue
                }
                students.append(Student(
                    name: selected.name,
                    nickname: NicknameGenerator.generate(from: selected.name),
                    school: selected.school,
                    admissionYear: selected.admissionYear,
                    chatRoomName: selected.title,
                    chatID: selectedChatID
                ))
                added += 1
            } else if matches.count == 1, let index = matches.first {
                guard !chatIDIsUsedElsewhere(index) else {
                    skipped += 1
                    continue
                }
                if students[index].chatRoomName != selected.title || students[index].chatID != selectedChatID {
                    students[index].chatRoomName = selected.title
                    students[index].chatID = selectedChatID
                    updated += 1
                } else {
                    unchanged += 1
                }
            } else {
                skipped += 1
            }
        }

        return KakaoStudentDBSyncOutput(
            students: students,
            report: KakaoStudentDBSyncReport(
                scannedChats: chats.count,
                recognizedStudentRooms: parsed.count,
                addedStudents: added,
                updatedStudents: updated,
                unchangedStudents: unchanged,
                skippedIdentities: skipped,
                multipleRoomSelections: multipleSelections
            )
        )
    }
}
