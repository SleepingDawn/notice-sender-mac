import CoreXLSX
import Foundation

struct MigrationResult: Sendable {
    var students: [Student]
    var classes: [ClassGroup]
}

enum XLSXMigrationError: LocalizedError {
    case cannotOpen
    case missingStudentDatabase

    var errorDescription: String? {
        switch self {
        case .cannotOpen: "XLSX 파일을 열 수 없습니다."
        case .missingStudentDatabase: "‘학생 DB’ 시트를 찾을 수 없습니다."
        }
    }
}

enum XLSXMigrationImporter {
    static func importStudents(at url: URL) throws -> [Student] {
        guard let file = XLSXFile(filepath: url.path) else { throw XLSXMigrationError.cannotOpen }
        let sharedStrings = try file.parseSharedStrings()
        var allRows: [[String]] = []
        for workbook in try file.parseWorkbooks() {
            for (_, path) in try file.parseWorksheetPathsAndNames(workbook: workbook) {
                let worksheet = try file.parseWorksheet(at: path)
                let grid = gridValues(worksheet, sharedStrings: sharedStrings)
                let maxColumn = grid.values.keys.map(\.column).max() ?? 0
                guard maxColumn > 0 else { continue }
                let rows = (1...grid.maxRow).map { row in (1...maxColumn).map { grid.value(row: row, column: $0) } }
                if StudentColumnMap.find(in: rows) != nil { allRows = rows; break }
            }
            if !allRows.isEmpty { break }
        }
        guard !allRows.isEmpty else { throw StudentFileImportError.missingHeaders }
        return try StudentFileImporter.students(from: allRows)
    }

    static func importWorkbook(at url: URL) throws -> MigrationResult {
        guard let file = XLSXFile(filepath: url.path) else { throw XLSXMigrationError.cannotOpen }
        let sharedStrings = try file.parseSharedStrings()
        var sheets: [(String, Worksheet)] = []
        for workbook in try file.parseWorkbooks() {
            for (name, path) in try file.parseWorksheetPathsAndNames(workbook: workbook) {
                guard let name else { continue }
                sheets.append((name, try file.parseWorksheet(at: path)))
            }
        }
        guard let dbSheet = sheets.first(where: { $0.0.trimmingCharacters(in: .whitespaces) == "학생 DB" })?.1 else {
            throw XLSXMigrationError.missingStudentDatabase
        }

        let grid = gridValues(dbSheet, sharedStrings: sharedStrings)
        var students: [Student] = []
        for row in 5...max(5, grid.maxRow) {
            let name = grid.value(row: row, column: 2).trimmed
            let school = grid.value(row: row, column: 3).trimmed
            let year = AdmissionYearPolicy.parseImported(grid.value(row: row, column: 4))
            let room = grid.value(row: row, column: 5).trimmed
            guard !name.isEmpty, !school.isEmpty, let year, !room.isEmpty else { continue }
            students.append(Student(name: name, nickname: NicknameGenerator.generate(from: name), school: school, admissionYear: year, chatRoomName: room))
        }

        var classes: [ClassGroup] = []
        for (sheetName, sheet) in sheets where sheetName != "학생 DB" && classIdentity(from: sheetName) != nil {
            guard let (school, year) = classIdentity(from: sheetName) else { continue }
            let rosterGrid = gridValues(sheet, sharedStrings: sharedStrings)
            var members: [ClassMember] = []
            for row in 4...max(4, rosterGrid.maxRow) {
                let fullName = rosterGrid.value(row: row, column: 2).trimmed
                guard !fullName.isEmpty else { continue }
                let candidates = students.filter { $0.name == fullName && $0.school == school && $0.admissionYear == year }
                guard candidates.count == 1, let student = candidates.first else { continue }
                if !members.contains(where: { $0.studentID == student.id }) {
                    members.append(ClassMember(studentID: student.id, nicknameOverride: nil))
                }
            }
            if !members.isEmpty {
                classes.append(ClassGroup(name: sheetName, school: school, admissionYear: year, members: members))
            }
        }
        return MigrationResult(students: students, classes: classes)
    }

    private static func classIdentity(from sheetName: String) -> (String, Int)? {
        let schools = ["한성", "세종", "인천", "경기", "대구"]
        guard let school = schools.first(where: { sheetName.contains($0) }) else { return nil }
        let digits = sheetName.filter(\.isNumber)
        guard digits.count >= 2, let year = Int(digits.prefix(2)) else { return nil }
        return (school, year)
    }

    private static func gridValues(_ worksheet: Worksheet, sharedStrings: SharedStrings?) -> CellGrid {
        var values: [CellCoordinate: String] = [:]
        var maxRow = 0
        for row in worksheet.data?.rows ?? [] {
            maxRow = max(maxRow, Int(row.reference))
            for cell in row.cells {
                let coordinate = CellCoordinate(reference: cell.reference.description)
                let text = sharedStrings.flatMap { cell.stringValue($0) } ?? cell.value ?? ""
                values[coordinate] = text
            }
        }
        return CellGrid(values: values, maxRow: maxRow)
    }
}

private struct CellCoordinate: Hashable {
    var row: Int
    var column: Int

    init(row: Int, column: Int) {
        self.row = row
        self.column = column
    }

    init(reference: String) {
        let letters = reference.prefix { $0.isLetter }
        let digits = reference.drop { $0.isLetter }
        var col = 0
        for scalar in letters.uppercased().unicodeScalars {
            col = col * 26 + Int(scalar.value - 64)
        }
        row = Int(digits) ?? 0
        column = col
    }
}

private struct CellGrid {
    var values: [CellCoordinate: String]
    var maxRow: Int
    func value(row: Int, column: Int) -> String { values[CellCoordinate(row: row, column: column)] ?? "" }
}

private extension String {
    var trimmed: String { trimmingCharacters(in: .whitespacesAndNewlines) }
}
