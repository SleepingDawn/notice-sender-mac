import Foundation

enum SendLogCSV {
    static let headers = [
        "log_id",
        "batch_id",
        "student_id",
        "발송 시각",
        "결과",
        "학생",
        "톡방",
        "메시지 SHA-256",
        "공지 본문 1",
        "공지 본문 2",
        "공지 본문 3",
        "공지 본문 4",
        "공지 본문 5",
        "상세",
    ]

    static func data(logs: [SendLog]) -> Data {
        let formatter = ISO8601DateFormatter()
        formatter.formatOptions = [.withInternetDateTime, .withFractionalSeconds]

        let rows = [headers] + logs.map { log in
            let messages = Array((log.messages ?? []).prefix(5))
            let paddedMessages = messages + Array(repeating: "", count: 5 - messages.count)
            return [
                log.id.uuidString.lowercased(),
                log.batchID.uuidString.lowercased(),
                log.studentID.uuidString.lowercased(),
                formatter.string(from: log.sentAt),
                log.result.rawValue,
                log.studentName,
                log.chatRoomName,
                log.messageSHA256,
            ] + paddedMessages + [
                log.detail ?? "",
            ]
        }
        let text = rows.map { $0.map(escaped).joined(separator: ",") }
            .joined(separator: "\r\n") + "\r\n"

        // Excel detects UTF-8 Korean text reliably when the CSV begins with a BOM.
        var data = Data([0xEF, 0xBB, 0xBF])
        data.append(contentsOf: text.utf8)
        return data
    }

    static func write(logs: [SendLog], to url: URL) throws {
        try data(logs: logs).write(to: url, options: .atomic)
    }

    private static func escaped(_ value: String) -> String {
        "\"\(value.replacingOccurrences(of: "\"", with: "\"\""))\""
    }
}
