import Foundation

struct ParsedBatchResult: Sendable {
    var batch: SendBatch?
    var issues: [ValidationIssue]
}

enum BatchParser {
    static func parse(text: String, database: AppDatabase, selectedClassID: UUID?) -> ParsedBatchResult {
        let rows = parseTSV(text)
        guard !rows.isEmpty else {
            return ParsedBatchResult(batch: nil, issues: [ValidationIssue(severity: .error, message: "붙여넣은 데이터가 비어 있습니다.")])
        }
        if rows.contains(where: { $0.contains("NOTICE_SENDER_SAFE") }) {
            return parseSafe(rows: rows, database: database)
        }
        return parseLegacy(rows: rows, database: database, selectedClassID: selectedClassID)
    }

    static func validate(batch: SendBatch, database: AppDatabase) -> [ValidationIssue] {
        var issues: [ValidationIssue] = []
        if batch.metadata.date.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty || batch.metadata.date == "날짜 입력" {
            issues.append(ValidationIssue(severity: .error, message: "수업 날짜를 입력해야 합니다."))
        }
        let preset = database.presets.first { $0.id == batch.metadata.presetID }
        if !batch.metadata.isLegacy {
            if preset == nil { issues.append(ValidationIssue(severity: .error, message: "Preset ID를 로컬 DB에서 찾을 수 없습니다.")) }
            else if preset?.version != batch.metadata.presetVersion { issues.append(ValidationIssue(severity: .error, message: "Sheet와 앱의 Preset 버전이 다릅니다.")) }
        }
        let itemIDs = batch.items.map(\.studentID)
        for id in Set(itemIDs) where itemIDs.filter({ $0 == id }).count > 1 {
            issues.append(ValidationIssue(severity: .error, message: "같은 학생 UUID가 발송 목록에 중복되었습니다: \(id.uuidString)"))
        }
        let endpointGroups = Dictionary(grouping: batch.items) { item in
            if let chatID = item.chatID?.nilIfEmpty { return "chat_id:\(chatID)" }
            return "room:\(item.chatRoomName)"
        }
        for (endpoint, items) in endpointGroups where !endpoint.hasSuffix(":") && items.count > 1 {
            issues.append(ValidationIssue(severity: .error, message: "동일한 카카오톡 발송 대상이 여러 학생에게 연결되었습니다: \(endpoint)"))
        }
        for (index, item) in batch.items.enumerated() {
            if item.chatRoomName.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty {
                issues.append(ValidationIssue(severity: .error, row: index + 1, message: "톡방 이름이 비어 있습니다."))
            }
            if item.allMessages.isEmpty {
                issues.append(ValidationIssue(severity: .error, row: index + 1, message: "발송할 메시지가 비어 있습니다."))
            }
            if item.allMessages.count > 5 {
                issues.append(ValidationIssue(severity: .error, row: index + 1, message: "학생당 메시지는 최대 5개까지 전송할 수 있습니다."))
            }
            if item.allMessages.contains(where: containsFormulaError) {
                issues.append(ValidationIssue(severity: .error, row: index + 1, message: "공지 멘트에 Sheet 수식 오류가 포함되어 있습니다."))
            }
            for path in item.attachmentPaths ?? [] {
                var isDirectory: ObjCBool = false
                if !FileManager.default.fileExists(atPath: path, isDirectory: &isDirectory) || isDirectory.boolValue {
                    issues.append(ValidationIssue(severity: .error, row: index + 1, message: "첨부파일을 찾을 수 없습니다: \(path)"))
                }
            }
            guard let student = database.students.first(where: { $0.id == item.studentID }) else {
                issues.append(ValidationIssue(severity: .error, row: index + 1, message: "로컬 DB에 없는 학생 UUID입니다.")); continue
            }
            if student.name != item.studentName {
                issues.append(ValidationIssue(severity: .error, row: index + 1, message: "학생 UUID와 이름이 일치하지 않습니다."))
            }
            if student.chatID?.nilIfEmpty != item.chatID?.nilIfEmpty {
                issues.append(ValidationIssue(severity: .error, row: index + 1, message: "학생 UUID와 chat_id가 일치하지 않습니다."))
            }
            if !student.isActive {
                issues.append(ValidationIssue(severity: .error, row: index + 1, message: "비활성 학생입니다."))
            }
            let sameKey = database.students.filter { $0.isActive && $0.duplicateKey == student.duplicateKey }
            if sameKey.count > 1 {
                issues.append(ValidationIssue(severity: .error, row: index + 1, message: "동일 학교·학번·이름의 중복 후보가 해결되지 않았습니다."))
            }
            let sameRoom = database.students.filter { $0.isActive && $0.chatRoomName == student.chatRoomName }
            if student.chatID?.nilIfEmpty == nil && sameRoom.count > 1 {
                issues.append(ValidationIssue(severity: .error, row: index + 1, message: "로컬 DB에서 동일 톡방 이름이 중복되었습니다."))
            }
            if let chatID = student.chatID?.nilIfEmpty {
                let sameChatID = database.students.filter { $0.isActive && $0.chatID?.nilIfEmpty == chatID }
                if sameChatID.count > 1 {
                    issues.append(ValidationIssue(severity: .error, row: index + 1, message: "로컬 DB에서 동일 chat_id가 중복되었습니다."))
                }
            }
        }
        return issues
    }

    private static func parseSafe(rows: [[String]], database: AppDatabase) -> ParsedBatchResult {
        var issues: [ValidationIssue] = []
        var metadataMap: [String: String] = [:]
        let metadataKeys: Set<String> = ["NOTICE_SENDER_SAFE", "class_id", "session_id", "date", "preset_id", "preset_version"]
        for row in rows {
            for index in row.indices.dropLast() where metadataKeys.contains(row[index]) {
                metadataMap[row[index]] = row[index + 1]
            }
        }
        guard let classID = UUID(uuidString: metadataMap["class_id"] ?? ""),
              let sessionID = UUID(uuidString: metadataMap["session_id"] ?? ""),
              let presetID = UUID(uuidString: metadataMap["preset_id"] ?? ""),
              let presetVersion = Int(metadataMap["preset_version"] ?? ""),
              let schemaVersion = Int(metadataMap["NOTICE_SENDER_SAFE"] ?? "") else {
            return ParsedBatchResult(batch: nil, issues: [ValidationIssue(severity: .error, message: "안전 형식 메타데이터가 잘못되었습니다.")])
        }
        guard let headerIndex = rows.firstIndex(where: { $0.contains("student_id") && ($0.contains("notice_message") || $0.contains("공지 멘트")) }) else {
            return ParsedBatchResult(batch: nil, issues: [ValidationIssue(severity: .error, message: "student_id/notice_message 헤더를 찾을 수 없습니다.")])
        }
        let headers = rows[headerIndex]
        guard let idColumn = headers.firstIndex(of: "student_id"),
              let nameColumn = headers.firstIndex(where: { ["student_name", "이름"].contains($0) }),
              let nicknameColumn = headers.firstIndex(where: { ["nickname", "호칭"].contains($0) }),
              let messageColumn = headers.firstIndex(where: { ["notice_message", "공지 멘트"].contains($0) }) else {
            return ParsedBatchResult(batch: nil, issues: [ValidationIssue(severity: .error, message: "필수 열이 없습니다.")])
        }
        var items: [BatchItem] = []
        var foundStudentRow = false
        for (offset, row) in rows.dropFirst(headerIndex + 1).enumerated() {
            guard row.indices.contains(idColumn), !row[idColumn].isEmpty else {
                if foundStudentRow { break }
                continue
            }
            guard let id = UUID(uuidString: row[idColumn]) else {
                issues.append(ValidationIssue(severity: .error, row: offset + 1, message: "학생 UUID 형식이 잘못되었습니다.")); continue
            }
            foundStudentRow = true
            let name = value(row, nameColumn)
            let nickname = value(row, nicknameColumn)
            let message = value(row, messageColumn)
            guard let student = database.students.first(where: { $0.id == id }) else {
                issues.append(ValidationIssue(severity: .error, row: offset + 1, message: "로컬 DB에 없는 학생 UUID입니다.")); continue
            }
            items.append(BatchItem(studentID: id, studentName: name, nickname: nickname, chatRoomName: student.chatRoomName, message: message, chatID: student.chatID))
        }
        let metadata = BatchMetadata(schemaVersion: schemaVersion, classID: classID, sessionID: sessionID, date: metadataMap["date"] ?? "", presetID: presetID, presetVersion: presetVersion)
        let batch = SendBatch(metadata: metadata, items: items)
        issues.append(contentsOf: validate(batch: batch, database: database))
        return ParsedBatchResult(batch: batch, issues: issues)
    }

    private static func parseLegacy(rows: [[String]], database: AppDatabase, selectedClassID: UUID?) -> ParsedBatchResult {
        guard let selectedClassID, let group = database.classes.first(where: { $0.id == selectedClassID }) else {
            return ParsedBatchResult(batch: nil, issues: [ValidationIssue(severity: .error, message: "기존 7열 형식은 먼저 정확한 반을 선택해야 합니다.")])
        }
        guard rows.contains(where: { $0.contains("공지 멘트") }) else {
            return ParsedBatchResult(batch: nil, issues: [ValidationIssue(severity: .error, message: "지원하는 안전 형식 또는 기존 7열 수업 블록이 아닙니다.")])
        }
        var issues: [ValidationIssue] = []
        var items: [BatchItem] = []
        let memberStudents = group.members.compactMap { member in database.students.first { $0.id == member.studentID } }
        let dataRows = rows.dropFirst(3).prefix { row in
            let attendance = value(row, 0)
            return ["출석", "동영상", "결석"].contains(attendance)
        }
        for (index, row) in dataRows.enumerated() {
            guard index < memberStudents.count else {
                issues.append(ValidationIssue(severity: .error, row: index + 1, message: "붙여넣은 학생 행이 반 명단보다 많습니다.")); break
            }
            let student = memberStudents[index]
            let nickname = group.members[index].nicknameOverride?.nilIfEmpty ?? student.nickname
            let message = value(row, 6)
            if !message.contains("\(nickname)의") {
                issues.append(ValidationIssue(severity: .error, row: index + 1, message: "공지 멘트의 호칭과 반 명단 순서가 일치하지 않습니다: \(student.name)"))
            }
            items.append(BatchItem(studentID: student.id, studentName: student.name, nickname: nickname, chatRoomName: student.chatRoomName, message: message, chatID: student.chatID))
        }
        if items.count != memberStudents.count {
            issues.append(ValidationIssue(severity: .error, message: "기존 블록 행 수(\(items.count))와 반 명단 인원(\(memberStudents.count))이 다릅니다."))
        }
        let date = rows.first.flatMap { $0.last } ?? ""
        let metadata = BatchMetadata(schemaVersion: 0, classID: group.id, sessionID: UUID(), date: date, presetID: UUID(), presetVersion: 0, isLegacy: true)
        let batch = SendBatch(metadata: metadata, items: items)
        issues.append(contentsOf: validate(batch: batch, database: database))
        issues.append(ValidationIssue(severity: .warning, message: "기존 7열 형식입니다. 모든 학생·톡방·멘트를 직접 검토해야 합니다."))
        return ParsedBatchResult(batch: batch, issues: issues)
    }

    static func parseTSV(_ text: String) -> [[String]] {
        var rows: [[String]] = []
        var row: [String] = []
        var field = ""
        var inQuotes = false
        var index = text.startIndex
        while index < text.endIndex {
            let character = text[index]
            let next = text.index(after: index)
            if character == "\"" {
                if inQuotes, next < text.endIndex, text[next] == "\"" {
                    field.append("\"")
                    index = text.index(after: next)
                    continue
                }
                inQuotes.toggle()
            } else if character == "\t" && !inQuotes {
                row.append(field); field = ""
            } else if (character == "\n" || character == "\r") && !inQuotes {
                if character == "\r", next < text.endIndex, text[next] == "\n" { index = next }
                row.append(field); rows.append(row); row = []; field = ""
            } else {
                field.append(character)
            }
            index = text.index(after: index)
        }
        row.append(field)
        if !row.allSatisfy(\.isEmpty) || rows.isEmpty { rows.append(row) }
        return rows
    }

    private static func value(_ row: [String], _ index: Int) -> String { row.indices.contains(index) ? row[index] : "" }
    private static func containsFormulaError(_ text: String) -> Bool {
        ["#REF!", "#DIV/0!", "#VALUE!", "#NAME?", "#N/A"].contains { text.contains($0) }
    }
}
