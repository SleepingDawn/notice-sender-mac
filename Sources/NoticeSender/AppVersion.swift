import Foundation

enum AppVersion {
    static var display: String {
        formatted(
            shortVersion: Bundle.main.object(forInfoDictionaryKey: "CFBundleShortVersionString") as? String,
            build: Bundle.main.object(forInfoDictionaryKey: "CFBundleVersion") as? String
        )
    }

    static func formatted(shortVersion: String?, build: String?) -> String {
        let version = shortVersion?.trimmingCharacters(in: .whitespacesAndNewlines) ?? ""
        let build = build?.trimmingCharacters(in: .whitespacesAndNewlines) ?? ""
        guard !version.isEmpty else { return "버전 정보 없음" }
        return build.isEmpty ? "v\(version)" : "v\(version) (빌드 \(build))"
    }
}
