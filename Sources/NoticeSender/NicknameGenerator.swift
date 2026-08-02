import Foundation

enum NicknameGenerator {
    /// Removes ASCII alphabetic suffixes, drops the family-name character,
    /// and appends "이" when the last Hangul syllable has a final consonant.
    static func generate(from rawName: String) -> String {
        let withoutAlphabet = String(rawName.filter { character in
            !(character.isASCII && character.isLetter)
        }).trimmingCharacters(in: .whitespacesAndNewlines)
        guard !withoutAlphabet.isEmpty else { return "" }
        let remainder = String(withoutAlphabet.dropFirst()).trimmingCharacters(in: .whitespacesAndNewlines)
        let base = remainder.isEmpty ? withoutAlphabet : remainder
        guard let scalar = base.unicodeScalars.last else { return base }
        let value = scalar.value
        let hasFinalConsonant = (0xAC00...0xD7A3).contains(value) && ((value - 0xAC00) % 28 != 0)
        return hasFinalConsonant ? base + "이" : base
    }

    /// Keeps a user-entered nickname and falls back to the generated default only when blank.
    static func resolved(name: String, enteredNickname: String) -> String {
        let entered = enteredNickname.trimmingCharacters(in: .whitespacesAndNewlines)
        return entered.isEmpty ? generate(from: name) : entered
    }
}
