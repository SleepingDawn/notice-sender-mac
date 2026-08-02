import Foundation
import KmsgSafeCore

struct KmsgSafeResult: Decodable, Sendable {
    let ok: Bool
    let action: String
    let roomTitle: String
    let exact: Bool
    let unique: Bool
    let forcedTyping: Bool

    enum CodingKeys: String, CodingKey {
        case ok
        case action
        case roomTitle = "room_title"
        case exact
        case unique
        case forcedTyping = "forced_typing"
    }
}

enum KmsgSafeAdapterError: LocalizedError {
    case helperUnavailable
    case helperFailed(Int32, String)
    case missingSafeResult
    case unsafeResult
    case wrongRoom(expected: String, actual: String)

    var errorDescription: String? {
        switch self {
        case .helperUnavailable:
            "앱에 포함된 안전형 kmsg 실행 파일을 찾지 못했습니다. 앱을 다시 빌드해주세요."
        case .helperFailed(let code, let output):
            "kmsg 안전 검증이 실패했습니다(종료 코드 \(code)). \(output)"
        case .missingSafeResult:
            "kmsg가 안전 검증 결과를 반환하지 않았습니다. 전송하지 않았습니다."
        case .unsafeResult:
            "kmsg 결과에 정확 일치·유일 결과·강제 타이핑 금지 확인이 없습니다. 전송하지 않았습니다."
        case .wrongRoom(let expected, let actual):
            "열린 방 제목이 다릅니다. 예상: \(expected), 실제: \(actual)"
        }
    }
}

struct KmsgSafeAdapter: Sendable {
    var isAvailable: Bool { true }

    func version() async -> String? {
        "\(KmsgEmbeddedEngine.upstreamVersion) · embedded"
    }

    func listChats(limit: Int = 1_000) async throws -> [KmsgEmbeddedChat] {
        try await Task.detached(priority: .userInitiated) {
            try KmsgEmbeddedEngine.listChats(limit: limit)
        }.value
    }

    func verify(
        roomName: String,
        chatID: String? = nil,
        cancellationToken: KmsgCancellationToken? = nil
    ) async throws -> KmsgSafeResult {
        let result = try await Task.detached(priority: .userInitiated) {
            try KmsgEmbeddedEngine.verify(
                roomName: roomName,
                chatID: chatID,
                cancellationToken: cancellationToken
            )
        }.value
        return try validate(result: result, roomName: roomName, action: "verify")
    }

    /// Performs a fresh exact-title search even when the student DB has a chat_id.
    /// This is the only verification path used by user-visible dry runs.
    func verifyByExactRoomSearch(
        roomName: String,
        cancellationToken: KmsgCancellationToken? = nil
    ) async throws -> KmsgSafeResult {
        let result = try await Task.detached(priority: .userInitiated) {
            try KmsgEmbeddedEngine.verify(
                roomName: roomName,
                chatID: nil,
                cancellationToken: cancellationToken
            )
        }.value
        return try validate(result: result, roomName: roomName, action: "verify")
    }

    func send(
        roomName: String,
        chatID: String? = nil,
        message: String,
        cancellationToken: KmsgCancellationToken? = nil
    ) async throws -> KmsgSafeResult {
        let result = try await Task.detached(priority: .userInitiated) {
            try KmsgEmbeddedEngine.send(
                roomName: roomName,
                chatID: chatID,
                message: message,
                cancellationToken: cancellationToken
            )
        }.value
        return try validate(result: result, roomName: roomName, action: "send")
    }

    func send(
        roomName: String,
        chatID: String? = nil,
        messages: [String],
        attachmentPaths: [String] = [],
        cancellationToken: KmsgCancellationToken? = nil
    ) async throws -> KmsgSafeResult {
        let result = try await Task.detached(priority: .userInitiated) {
            try KmsgEmbeddedEngine.send(
                roomName: roomName,
                chatID: chatID,
                messages: messages,
                attachmentPaths: attachmentPaths,
                cancellationToken: cancellationToken
            )
        }.value
        return try validate(result: result, roomName: roomName, action: "send")
    }

    static func safeArguments(roomName: String, message: String, verifyOnly: Bool) -> [String] {
        var arguments = [
            "send", roomName, message,
            "--exact",
            "--require-unique",
            "--no-forced-typing",
            "--keep-window",
            "--no-cache",
            "--json",
        ]
        if verifyOnly {
            arguments.append("--verify-only")
        }
        return arguments
    }

    static func normalizeRoomName(_ value: String) -> String {
        KmsgEmbeddedEngine.normalizeRoomName(value)
    }

    private func validate(result: KmsgEmbeddedResult, roomName: String, action: String) throws -> KmsgSafeResult {
        guard Self.normalizeRoomName(result.roomTitle) == Self.normalizeRoomName(roomName) else {
            throw KmsgSafeAdapterError.wrongRoom(expected: roomName, actual: result.roomTitle)
        }
        if action == "send", !result.didSend {
            throw KmsgSafeAdapterError.unsafeResult
        }
        return KmsgSafeResult(
            ok: true,
            action: action,
            roomTitle: result.roomTitle,
            exact: true,
            unique: true,
            forcedTyping: false
        )
    }
}
