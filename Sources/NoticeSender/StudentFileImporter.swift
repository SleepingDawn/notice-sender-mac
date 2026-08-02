import CoreFoundation
import Foundation

enum StudentFileImportError: LocalizedError {
    case unsupportedFormat
    case unreadableText
    case missingHeaders
    case noStudents

    var errorDescription: String? {
        switch self {
        case .unsupportedFormat: "지원 형식은 .xlsx, .csv, .tsv입니다."
        case .unreadableText: "CSV/TSV 문자 인코딩을 읽을 수 없습니다. UTF-8 또는 EUC-KR로 저장해주세요."
        case .missingHeaders: "이름·학교·학번·톡방 이름 열을 찾을 수 없습니다."
        case .noStudents: "필수값이 모두 채워진 학생 행이 없습니다."
        }
    }
}

enum StudentFileImporter {
    static func importStudents(at url: URL) throws -> [Student] {
        switch url.pathExtension.lowercased() {
        case "xlsx": return try XLSXMigrationImporter.importStudents(at: url)
        case "csv", "tsv": return try importDelimited(at: url)
        default: throw StudentFileImportError.unsupportedFormat
        }
    }

    private static func importDelimited(at url: URL) throws -> [Student] {
        let data = try Data(contentsOf: url)
        let eucKR = String.Encoding(rawValue: CFStringConvertEncodingToNSStringEncoding(CFStringEncoding(CFStringEncodings.EUC_KR.rawValue)))
        let encodings: [String.Encoding] = [.utf8, eucKR, .utf16, .unicode]
        guard var text = encodings.lazy.compactMap({ String(data: data, encoding: $0) }).first else { throw StudentFileImportError.unreadableText }
        if text.first == "\u{feff}" { text.removeFirst() }
        let delimiter: Character = url.pathExtension.lowercased() == "tsv" ? "\t" : detectDelimiter(text)
        let rows = parseDelimited(text, delimiter: delimiter)
        return try students(from: rows)
    }

    static func students(from rows: [[String]]) throws -> [Student] {
        guard let (headerIndex, columns) = StudentColumnMap.find(in: rows) else { throw StudentFileImportError.missingHeaders }
        var students: [Student] = []
        for row in rows.dropFirst(headerIndex + 1) {
            let name = columns.value(.name, in: row).trimmed
            let school = columns.value(.school, in: row).trimmed
            let room = columns.value(.chatRoom, in: row).trimmed
            guard let year = normalizedYear(columns.value(.year, in: row)), !name.isEmpty, !school.isEmpty, !room.isEmpty else { continue }
            students.append(Student(
                name: name,
                nickname: NicknameGenerator.generate(from: name),
                school: school,
                admissionYear: year,
                chatRoomName: room,
                chatID: columns.value(.chatID, in: row).trimmed.nilIfEmpty
            ))
        }
        let unique = Dictionary(grouping: students, by: { "\($0.name)|\($0.school)|\($0.admissionYear)|\($0.chatRoomName)" }).compactMap(\.value.first)
        guard !unique.isEmpty else { throw StudentFileImportError.noStudents }
        return unique
    }

    private static func detectDelimiter(_ text: String) -> Character {
        let firstLines = text.split(whereSeparator: \.isNewline).prefix(5)
        let commas = firstLines.reduce(0) { $0 + $1.filter { $0 == "," }.count }
        let tabs = firstLines.reduce(0) { $0 + $1.filter { $0 == "\t" }.count }
        return tabs > commas ? "\t" : ","
    }

    static func parseDelimited(_ text: String, delimiter: Character) -> [[String]] {
        var rows: [[String]] = []
        var row: [String] = []
        var field = ""
        var quoted = false
        var index = text.startIndex
        while index < text.endIndex {
            let character = text[index]
            let next = text.index(after: index)
            if character == "\"" {
                if quoted, next < text.endIndex, text[next] == "\"" {
                    field.append("\""); index = text.index(after: next); continue
                }
                quoted.toggle()
            } else if character == delimiter && !quoted {
                row.append(field); field = ""
            } else if (character == "\n" || character == "\r") && !quoted {
                if character == "\r", next < text.endIndex, text[next] == "\n" { index = next }
                row.append(field); rows.append(row); row = []; field = ""
            } else { field.append(character) }
            index = text.index(after: index)
        }
        row.append(field)
        if !row.allSatisfy(\.isEmpty) || rows.isEmpty { rows.append(row) }
        return rows
    }

    private static func normalizedYear(_ value: String) -> Int? {
        let trimmed = value.trimmingCharacters(in: .whitespacesAndNewlines).replacingOccurrences(of: ",", with: "")
        var year: Int
        if let numeric = Double(trimmed), numeric.rounded() == numeric {
            year = Int(numeric)
        } else {
            let digits = trimmed.filter(\.isNumber)
            guard let parsed = Int(digits) else { return nil }
            year = parsed
        }
        if year >= 2000 { year %= 100 }
        return (0...99).contains(year) ? year : nil
    }

}

private enum StudentField { case name, nickname, school, year, chatRoom, chatID }

struct StudentColumnMap {
    var indexes: [String: Int]

    static func find(in rows: [[String]]) -> (Int, StudentColumnMap)? {
        for (index, row) in rows.prefix(30).enumerated() {
            var found: [String: Int] = [:]
            for (column, rawHeader) in row.enumerated() {
                let header = normalize(rawHeader)
                if nameAliases.contains(header) { found["name"] = column }
                if nicknameAliases.contains(header) { found["nickname"] = column }
                if schoolAliases.contains(header) { found["school"] = column }
                if yearAliases.contains(header) { found["year"] = column }
                if chatAliases.contains(header) { found["chat"] = column }
                if chatIDAliases.contains(header) { found["chatID"] = column }
            }
            if found["name"] != nil, found["school"] != nil, found["year"] != nil, found["chat"] != nil {
                return (index, StudentColumnMap(indexes: found))
            }
        }
        return nil
    }

    fileprivate func value(_ field: StudentField, in row: [String]) -> String {
        let key: String
        switch field { case .name: key = "name"; case .nickname: key = "nickname"; case .school: key = "school"; case .year: key = "year"; case .chatRoom: key = "chat"; case .chatID: key = "chatID" }
        guard let index = indexes[key], row.indices.contains(index) else { return "" }
        return row[index]
    }

    private static func normalize(_ value: String) -> String {
        value.lowercased().filter { $0.isLetter || $0.isNumber }
    }
    private static let nameAliases: Set<String> = ["이름", "성명", "성명홍길동", "name", "studentname", "학생이름"]
    private static let nicknameAliases: Set<String> = ["호칭", "별칭", "부르는이름", "성명길동이", "nickname", "학생호칭"]
    private static let schoolAliases: Set<String> = ["학교", "school", "학교명"]
    private static let yearAliases: Set<String> = ["학번", "입학년도", "입학연도", "기수", "year", "admissionyear"]
    private static let chatAliases: Set<String> = ["톡방이름", "카카오톡", "카카오톡채팅방", "채팅방", "채팅방이름", "톡방", "chatroom", "chatroomname", "kakaotalk"]
    private static let chatIDAliases: Set<String> = ["chatid", "톡방id", "채팅방id", "kmsgchatid"]
}

private extension String {
    var trimmed: String { trimmingCharacters(in: .whitespacesAndNewlines) }
}
