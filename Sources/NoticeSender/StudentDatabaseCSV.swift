import Foundation

enum StudentDatabaseCSV {
    static let headers = ["student_id", "이름", "호칭", "학교", "학번", "톡방 이름", "chat_id", "상태"]

    static func data(students: [Student]) -> Data {
        let sorted = students.sorted {
            let schoolOrder = $0.school.localizedStandardCompare($1.school)
            if schoolOrder != .orderedSame { return schoolOrder == .orderedAscending }
            if $0.admissionYear != $1.admissionYear { return $0.admissionYear < $1.admissionYear }
            let nameOrder = $0.name.localizedStandardCompare($1.name)
            if nameOrder != .orderedSame { return nameOrder == .orderedAscending }
            return $0.id.uuidString < $1.id.uuidString
        }
        let rows = [headers] + sorted.map { student in
            [
                student.id.uuidString.lowercased(),
                student.name,
                student.nickname,
                student.school,
                AdmissionYearPolicy.formatted(student.admissionYear),
                student.chatRoomName,
                student.chatID ?? "",
                student.isActive ? "활성" : "비활성",
            ]
        }
        let text = rows.map { $0.map(escaped).joined(separator: ",") }.joined(separator: "\r\n") + "\r\n"
        var data = Data([0xEF, 0xBB, 0xBF])
        data.append(contentsOf: text.utf8)
        return data
    }

    static func write(students: [Student], to url: URL) throws {
        try data(students: students).write(to: url, options: .atomic)
    }

    private static func escaped(_ value: String) -> String {
        "\"\(value.replacingOccurrences(of: "\"", with: "\"\""))\""
    }
}
