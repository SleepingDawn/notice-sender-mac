import AppKit
import SwiftUI

struct PerformanceSpreadsheet: NSViewRepresentable {
    @Binding var rows: [PreparedNoticeRow]
    @Binding var homeworkMaximum: String
    @Binding var testMaximum: String
    @Binding var mockMaximums: [String]
    @Binding var selectedStudentRow: Int
    @Binding var selectedInputColumn: Int
    var layout: PresetCategory
    var mockExamCount: Int
    var onBeforeChange: () -> Void
    var onUndo: () -> Void
    var isInteractionEnabled = true

    nonisolated static func applying(_ changes: [SpreadsheetCanvas.CellChange], to rows: [PreparedNoticeRow], homeworkMaximum: String, testMaximum: String, mockMaximums: [String] = ["100", "100", "100"], layout: PresetCategory = .regular, mockExamCount: Int = 3) -> (rows: [PreparedNoticeRow], homeworkMaximum: String, testMaximum: String, mockMaximums: [String]) {
        var updatedRows = rows
        var updatedHomeworkMaximum = homeworkMaximum
        var updatedTestMaximum = testMaximum
        var updatedMockMaximums = mockMaximums
        for change in changes {
            if layout != .direct, change.row == 1 {
                if layout == .regular {
                    if change.column == 6 { updatedHomeworkMaximum = change.value }
                    if change.column == 7 { updatedTestMaximum = change.value }
                } else if layout == .mock, change.column >= 6, change.column < 6 + max(1, min(3, mockExamCount)) {
                    let examIndex = change.column - 6
                    while updatedMockMaximums.count <= examIndex { updatedMockMaximums.append("100") }
                    updatedMockMaximums[examIndex] = change.value
                }
                continue
            }
            let studentIndex = change.row - (layout == .direct ? 1 : 2)
            guard updatedRows.indices.contains(studentIndex) else { continue }
            if layout == .direct {
                if change.column == 1 { updatedRows[studentIndex].isIncluded = Self.checkboxValue(change.value) }
                if change.column == 3 { updatedRows[studentIndex].noticeMessage = change.value }
                continue
            }
            switch change.column {
            case 1: updatedRows[studentIndex].isIncluded = Self.checkboxValue(change.value)
            case 4: updatedRows[studentIndex].attendance = change.value
            case 5: updatedRows[studentIndex].attitude = change.value
            case 6...(6 + max(1, min(3, mockExamCount)) * 2) where layout == .mock:
                if change.column == 6 + max(1, min(3, mockExamCount)) * 2 { updatedRows[studentIndex].noticeMessage = change.value; break }
                let count = max(1, min(3, mockExamCount))
                let examIndex = change.column < 6 + count ? change.column - 6 : change.column - 6 - count
                while updatedRows[studentIndex].mockExams.count <= examIndex { updatedRows[studentIndex].mockExams.append(PreparedMockExam()) }
                if change.column < 6 + count { updatedRows[studentIndex].mockExams[examIndex].score = change.value }
                else { updatedRows[studentIndex].mockExams[examIndex].comment = change.value }
            case 6: updatedRows[studentIndex].homework = change.value
            case 7: updatedRows[studentIndex].test = change.value
            case 8: updatedRows[studentIndex].homeworkComment = change.value
            case 9: updatedRows[studentIndex].testComment = change.value
            case 10: updatedRows[studentIndex].noticeMessage = change.value
            default: break
            }
        }
        return (updatedRows, updatedHomeworkMaximum, updatedTestMaximum, updatedMockMaximums)
    }

    private nonisolated static func checkboxValue(_ value: String) -> Bool {
        ["true", "1", "yes", "y", "☑︎", "☑"].contains(value.lowercased())
    }

    func makeCoordinator() -> Coordinator { Coordinator(parent: self) }

    func makeNSView(context: Context) -> SpreadsheetCanvas {
        let view = SpreadsheetCanvas()
        view.onApply = { changes in context.coordinator.apply(changes) }
        view.onBeforeChange = onBeforeChange
        view.onUndo = onUndo
        view.isInteractionEnabled = isInteractionEnabled
        view.onSelectionChange = { cell in context.coordinator.updateSelection(cell) }
        view.update(rows: rows, homeworkMaximum: homeworkMaximum, testMaximum: testMaximum, mockMaximums: mockMaximums, layout: layout, mockExamCount: mockExamCount)
        return view
    }

    func updateNSView(_ view: SpreadsheetCanvas, context: Context) {
        context.coordinator.parent = self
        view.onBeforeChange = onBeforeChange
        view.onUndo = onUndo
        view.isInteractionEnabled = isInteractionEnabled
        view.onSelectionChange = { cell in context.coordinator.updateSelection(cell) }
        view.update(rows: rows, homeworkMaximum: homeworkMaximum, testMaximum: testMaximum, mockMaximums: mockMaximums, layout: layout, mockExamCount: mockExamCount)
    }

    @MainActor final class Coordinator {
        var parent: PerformanceSpreadsheet
        init(parent: PerformanceSpreadsheet) { self.parent = parent }

        func apply(_ changes: [SpreadsheetCanvas.CellChange]) {
            guard !changes.isEmpty else { return }
            let result = PerformanceSpreadsheet.applying(changes, to: parent.rows, homeworkMaximum: parent.homeworkMaximum, testMaximum: parent.testMaximum, mockMaximums: parent.mockMaximums, layout: parent.layout, mockExamCount: parent.mockExamCount)
            parent.rows = result.rows
            parent.homeworkMaximum = result.homeworkMaximum
            parent.testMaximum = result.testMaximum
            parent.mockMaximums = result.mockMaximums
        }

        func updateSelection(_ cell: SpreadsheetCanvas.Cell) {
            let firstRow = parent.layout == .direct ? 1 : 2
            let firstColumn = parent.layout == .direct ? 3 : 4
            guard cell.row >= firstRow, cell.column >= firstColumn else { return }
            parent.selectedStudentRow = cell.row - firstRow
            parent.selectedInputColumn = cell.column - firstColumn
        }
    }
}

final class SpreadsheetCanvas: NSView, NSTextViewDelegate {
    struct Cell: Hashable { var row: Int; var column: Int }
    struct CellChange: Hashable { var row: Int; var column: Int; var value: String }

    static let fontSize: CGFloat = 12
    static let textHeight: CGFloat = ceil(NSFont.systemFont(ofSize: fontSize).boundingRectForFont.height)
    static let headerHeight: CGFloat = max(40, textHeight + 16)
    static let rowHeight: CGFloat = max(40, textHeight + 16)

    private var layout: PresetCategory = .regular
    private var mockExamCount = 3
    private var widths: [CGFloat] { Self.widths(layout: layout, mockExamCount: mockExamCount) }
    private var headers: [String] {
        switch layout {
        case .direct: ["번호", "발송", "성명", "메시지"]
        case .regular: ["번호", "발송", "성명", "호칭", "출석", "태도", "숙제", "테스트", "숙제 코멘트", "테스트 코멘트", "공지 멘트"]
        case .mock:
            ["번호", "발송", "성명", "호칭", "출석", "태도"]
                + (1...mockExamCount).map { "모의고사 \($0) 점수" }
                + (1...mockExamCount).map { "모의고사 \($0) 코멘트" }
                + ["공지 멘트"]
        }
    }
    private var rows: [PreparedNoticeRow] = []
    private var homeworkMaximum = ""
    private var testMaximum = ""
    private var mockMaximums = ["100", "100", "100"]
    private var anchor = Cell(row: 2, column: 4)
    private var cursor = Cell(row: 2, column: 4)
    private var editor: SpreadsheetTextView?
    private var editorCell: Cell?
    var onApply: (([CellChange]) -> Void)?
    var onBeforeChange: (() -> Void)?
    var onUndo: (() -> Void)?
    var onSelectionChange: ((Cell) -> Void)?
    var isInteractionEnabled = true {
        didSet {
            if !isInteractionEnabled, editor != nil { cancelEditing() }
        }
    }

    override var acceptsFirstResponder: Bool { true }
    override var isFlipped: Bool { true }
    override var intrinsicContentSize: NSSize {
        NSSize(width: widths.reduce(0, +), height: Self.headerHeight + CGFloat(rows.count + (layout == .direct ? 0 : 1)) * Self.rowHeight)
    }

    static func widths(layout: PresetCategory, mockExamCount: Int) -> [CGFloat] {
        switch layout {
        case .direct: [45, 56, 110, 520]
        case .regular: [45, 56, 90, 90, 90, 70, 85, 85, 180, 180, 360]
        case .mock: [45, 56, 90, 90, 90, 70] + Array(repeating: CGFloat(105), count: max(1, min(3, mockExamCount))) + Array(repeating: CGFloat(180), count: max(1, min(3, mockExamCount))) + [320]
        }
    }

    func update(rows: [PreparedNoticeRow], homeworkMaximum: String, testMaximum: String, mockMaximums: [String], layout: PresetCategory, mockExamCount: Int) {
        let layoutChanged = self.layout != layout
        self.rows = rows; self.homeworkMaximum = homeworkMaximum; self.testMaximum = testMaximum
        self.mockMaximums = mockMaximums; self.layout = layout; self.mockExamCount = max(1, min(3, mockExamCount))
        if layoutChanged {
            anchor = Cell(row: firstStudentRow, column: layout == .direct ? 3 : 4)
            cursor = anchor
        } else {
            anchor = normalized(anchor); cursor = normalized(cursor)
        }
        invalidateIntrinsicContentSize(); needsDisplay = true
    }

    override func draw(_ dirtyRect: NSRect) {
        super.draw(dirtyRect)
        NSColor.textBackgroundColor.setFill(); dirtyRect.fill()
        for row in 0..<(rows.count + (layout == .direct ? 1 : 2)) {
            for column in widths.indices {
                let rect = cellRect(row: row, column: column)
                if selected(row: row, column: column) { NSColor.controlAccentColor.withAlphaComponent(0.14).setFill(); rect.fill() }
                fillForCell(row: row, column: column)?.setFill(); if let _ = fillForCell(row: row, column: column) { rect.fill() }
                NSColor.separatorColor.setStroke(); NSBezierPath(rect: rect).stroke()
                drawText(value(row: row, column: column), in: rect, bold: row == 0 || (row == 1 && layout != .direct), centered: column < (layout == .direct ? 3 : 8))
            }
        }
        NSColor.controlAccentColor.setStroke()
        let selection = selectionRect()
        let path = NSBezierPath(rect: selection); path.lineWidth = 2; path.stroke()
    }

    override func mouseDown(with event: NSEvent) {
        guard isInteractionEnabled else { return }
        if editor != nil { commitEditing() }
        guard let cell = cell(at: convert(event.locationInWindow, from: nil)) else { return }
        anchor = cell; cursor = cell; window?.makeFirstResponder(self); needsDisplay = true
        notifySelection()
        if isSendCheckbox(cell) {
            toggleSendCheckbox(cell)
            return
        }
        if event.clickCount == 2, editable(cell) { beginEditing(cell) }
    }

    override func mouseDragged(with event: NSEvent) {
        guard isInteractionEnabled else { return }
        guard let cell = cell(at: convert(event.locationInWindow, from: nil)) else { return }
        cursor = cell; needsDisplay = true
        notifySelection()
    }

    override func performKeyEquivalent(with event: NSEvent) -> Bool {
        guard isInteractionEnabled else { return super.performKeyEquivalent(with: event) }
        guard event.modifierFlags.contains(.command) else { return super.performKeyEquivalent(with: event) }
        switch event.charactersIgnoringModifiers?.lowercased() {
        case "c": copySelection(); return true
        case "v": pasteSelection(); return true
        case "x": cutSelection(); return true
        case "z": onUndo?(); return true
        default: return super.performKeyEquivalent(with: event)
        }
    }

    @objc func copy(_ sender: Any?) { copySelection() }
    @objc func paste(_ sender: Any?) { pasteSelection() }
    @objc func cut(_ sender: Any?) { cutSelection() }
    @objc func undo(_ sender: Any?) { onUndo?() }

    override func keyDown(with event: NSEvent) {
        guard isInteractionEnabled else { super.keyDown(with: event); return }
        if event.modifierFlags.contains(.command) {
            switch event.charactersIgnoringModifiers?.lowercased() {
            case "c": copySelection(); return
            case "v": pasteSelection(); return
            case "x": cutSelection(); return
            case "z": onUndo?(); return
            default: break
            }
        }
        switch event.keyCode {
        case 36: if editable(cursor) { beginEditing(cursor) }
        case 49:
            if isSendCheckbox(cursor) { toggleSendCheckbox(cursor) }
            else if editable(cursor) { beginEditing(cursor, replacingWith: event.characters ?? " ") }
            else { super.keyDown(with: event) }
        case 51, 117: deleteSelection()
        case 123: moveCursor(dx: -1, dy: 0)
        case 124: moveCursor(dx: 1, dy: 0)
        case 125: moveCursor(dx: 0, dy: 1)
        case 126: moveCursor(dx: 0, dy: -1)
        default:
            if editable(cursor), let characters = event.characters, !characters.isEmpty, !event.modifierFlags.contains(.control) {
                beginEditing(cursor, replacingWith: characters)
            } else { super.keyDown(with: event) }
        }
    }

    private func copySelection() {
        let range = selectedRange()
        let text = (range.rows).map { row in range.columns.map { value(row: row, column: $0) }.joined(separator: "\t") }.joined(separator: "\n")
        NSPasteboard.general.clearContents(); NSPasteboard.general.setString(text, forType: .string)
    }

    private func cutSelection() { copySelection(); deleteSelection() }

    private func deleteSelection() {
        let changes = Self.deletionChanges(anchor: anchor, cursor: cursor, studentCount: rows.count, layout: layout, mockExamCount: mockExamCount) { [weak self] row, column in
            self?.value(row: row, column: column) ?? ""
        }
        guard !changes.isEmpty else { NSSound.beep(); return }
        onBeforeChange?(); onApply?(changes); needsDisplay = true
    }

    private func pasteSelection() {
        guard let text = NSPasteboard.general.string(forType: .string) else { return }
        let source = BatchParser.parseTSV(text)
        var changes: [CellChange] = []
        let start = Cell(row: min(anchor.row, cursor.row), column: min(anchor.column, cursor.column))
        for (rowOffset, sourceRow) in source.enumerated() {
            for (columnOffset, value) in sourceRow.enumerated() {
                let target = Cell(row: start.row + rowOffset, column: start.column + columnOffset)
                if editable(target) { changes.append(CellChange(row: target.row, column: target.column, value: value)) }
            }
        }
        guard !changes.isEmpty else { NSSound.beep(); return }
        onBeforeChange?(); onApply?(changes)
        cursor = Cell(row: min(lastRow, start.row + source.count - 1), column: min(widths.count - 1, start.column + (source.map(\.count).max() ?? 1) - 1))
        needsDisplay = true
    }

    private func beginEditing(_ cell: Cell, replacingWith replacement: String? = nil) {
        guard editable(cell) else { return }
        if editor != nil { commitEditing() }
        let textView = SpreadsheetTextView(frame: cellRect(row: cell.row, column: cell.column).insetBy(dx: 2, dy: 3))
        textView.string = replacement ?? value(row: cell.row, column: cell.column)
        textView.delegate = self
        textView.font = .systemFont(ofSize: Self.fontSize)
        textView.textColor = .labelColor
        textView.backgroundColor = .textBackgroundColor
        textView.drawsBackground = true
        textView.isRichText = false
        textView.importsGraphics = false
        textView.allowsUndo = true
        textView.isHorizontallyResizable = false
        textView.isVerticallyResizable = false
        textView.textContainerInset = NSSize(width: 3, height: 5)
        textView.wantsLayer = true
        textView.layer?.borderColor = NSColor.controlAccentColor.cgColor
        textView.layer?.borderWidth = 2
        textView.layer?.cornerRadius = 2
        textView.onCommit = { [weak self] in self?.commitEditing() }
        textView.onCancel = { [weak self] in self?.cancelEditing() }
        addSubview(textView)
        editor = textView
        editorCell = cell
        window?.makeFirstResponder(textView)
        if replacement == nil {
            textView.selectAll(nil)
        } else {
            textView.setSelectedRange(NSRange(location: textView.string.utf16.count, length: 0))
        }
    }

    func textDidEndEditing(_ notification: Notification) {
        commitEditing()
    }

    private func commitEditing() {
        guard let textView = editor, let cell = editorCell else { return }
        let updatedValue = textView.string
        textView.delegate = nil
        editor = nil
        editorCell = nil
        textView.removeFromSuperview()
        window?.makeFirstResponder(self)
        if updatedValue != value(row: cell.row, column: cell.column) {
            onBeforeChange?()
            onApply?([CellChange(row: cell.row, column: cell.column, value: updatedValue)])
        }
        needsDisplay = true
    }

    private func cancelEditing() {
        guard let textView = editor else { return }
        textView.delegate = nil
        editor = nil
        editorCell = nil
        textView.removeFromSuperview()
        window?.makeFirstResponder(self)
        needsDisplay = true
    }
    private func moveCursor(dx: Int, dy: Int) {
        cursor = Cell(row: min(max(0, cursor.row + dy), lastRow), column: min(max(0, cursor.column + dx), widths.count - 1)); anchor = cursor; needsDisplay = true; notifySelection()
    }
    private func notifySelection() {
        let topLeft = Cell(row: min(anchor.row, cursor.row), column: min(anchor.column, cursor.column))
        onSelectionChange?(topLeft)
    }
    private func isSendCheckbox(_ cell: Cell) -> Bool {
        cell.column == 1 && cell.row >= firstStudentRow && cell.row <= lastRow
    }
    private func toggleSendCheckbox(_ cell: Cell) {
        let studentIndex = cell.row - firstStudentRow
        guard isSendCheckbox(cell), rows.indices.contains(studentIndex) else { return }
        onBeforeChange?()
        onApply?([CellChange(row: cell.row, column: cell.column, value: rows[studentIndex].isIncluded ? "false" : "true")])
        needsDisplay = true
    }
    private func editable(_ cell: Cell) -> Bool {
        Self.isEditable(cell, studentCount: rows.count, layout: layout, mockExamCount: mockExamCount)
    }
    nonisolated static func isEditable(_ cell: Cell, studentCount: Int, layout: PresetCategory = .regular, mockExamCount: Int = 3) -> Bool {
        let firstStudentRow = layout == .direct ? 1 : 2
        let lastStudentRow = firstStudentRow + studentCount - 1
        if layout == .direct {
            return cell.row >= firstStudentRow && cell.row <= lastStudentRow && cell.column == 3
        }
        let totalEditable = cell.row == 1 && (layout == .regular ? [6, 7].contains(cell.column) : (cell.column >= 6 && cell.column < 6 + max(1, min(3, mockExamCount))))
        return totalEditable || (cell.row >= firstStudentRow && cell.row <= lastStudentRow && cell.column >= 4)
    }
    nonisolated static func deletionChanges(anchor: Cell, cursor: Cell, studentCount: Int, layout: PresetCategory = .regular, mockExamCount: Int = 3, value: (Int, Int) -> String) -> [CellChange] {
        let rowRange = min(anchor.row, cursor.row)...max(anchor.row, cursor.row)
        let columnRange = min(anchor.column, cursor.column)...max(anchor.column, cursor.column)
        var changes: [CellChange] = []
        for row in rowRange {
            for column in columnRange {
                let cell = Cell(row: row, column: column)
                if isEditable(cell, studentCount: studentCount, layout: layout, mockExamCount: mockExamCount), !value(row, column).isEmpty {
                    changes.append(CellChange(row: row, column: column, value: ""))
                }
            }
        }
        return changes
    }
    private func selected(row: Int, column: Int) -> Bool { let range = selectedRange(); return range.rows.contains(row) && range.columns.contains(column) }
    private func selectedRange() -> (rows: ClosedRange<Int>, columns: ClosedRange<Int>) {
        let selectedRows = min(anchor.row, cursor.row)...max(anchor.row, cursor.row)
        let selectedColumns = min(anchor.column, cursor.column)...max(anchor.column, cursor.column)
        return (selectedRows, selectedColumns)
    }
    private func selectionRect() -> NSRect {
        let range = selectedRange(); let first = cellRect(row: range.rows.lowerBound, column: range.columns.lowerBound); let last = cellRect(row: range.rows.upperBound, column: range.columns.upperBound); return first.union(last)
    }
    private func cell(at point: NSPoint) -> Cell? {
        let row = point.y < Self.headerHeight ? 0 : Int((point.y - Self.headerHeight) / Self.rowHeight) + 1
        guard row <= lastRow else { return nil }
        var x: CGFloat = 0
        for (column, width) in widths.enumerated() { if point.x >= x && point.x < x + width { return Cell(row: row, column: column) }; x += width }
        return nil
    }
    private func cellRect(row: Int, column: Int) -> NSRect {
        NSRect(
            x: widths.prefix(column).reduce(0, +),
            y: row == 0 ? 0 : Self.headerHeight + CGFloat(row - 1) * Self.rowHeight,
            width: widths[column],
            height: row == 0 ? Self.headerHeight : Self.rowHeight
        )
    }
    private func value(row: Int, column: Int) -> String {
        if row == 0 { return headers[column] }
        if row == 1 && layout != .direct {
            if column == 2 { return layout == .mock ? "만점" : "총 개수" }
            if layout == .regular {
                if column == 6 { return homeworkMaximum }
                if column == 7 { return testMaximum }
            } else if column >= 6, column < 6 + mockExamCount {
                let index = column - 6
                return mockMaximums.indices.contains(index) ? mockMaximums[index] : "100"
            }
            return ""
        }
        guard rows.indices.contains(row - firstStudentRow) else { return "" }; let item = rows[row - firstStudentRow]
        switch column {
        case 0: return String(item.number)
        case 1: return item.isIncluded ? "☑︎" : "☐"
        case 2: return item.name
        case 3 where layout == .direct: return item.noticeMessage
        case 3: return item.nickname
        case 4: return item.attendance
        case 5: return item.attitude
        case 6...(headers.count - 1) where layout == .mock:
            if column == headers.count - 1 { return item.noticeMessage }
            let examIndex = column < 6 + mockExamCount ? column - 6 : column - 6 - mockExamCount
            guard item.mockExams.indices.contains(examIndex) else { return "" }
            return column < 6 + mockExamCount ? item.mockExams[examIndex].score : item.mockExams[examIndex].comment
        case 6: return item.homework
        case 7: return item.test
        case 8: return item.homeworkComment
        case 9: return item.testComment
        default: return item.noticeMessage
        }
    }
    private func fillForCell(row: Int, column: Int) -> NSColor? {
        if layout == .direct {
            guard row == 0 || (row >= firstStudentRow && row <= lastRow) else { return nil }
            return [.windowBackgroundColor, .systemGreen.withAlphaComponent(0.14), .lightGray, .systemYellow.withAlphaComponent(0.22)][column]
        }
        guard row <= 1 else { return nil }
        if row == 1, column >= 6 { return column < 6 + mockExamCount ? .systemBlue.withAlphaComponent(0.12) : .windowBackgroundColor }
        if layout == .mock {
            if column < 6 { return [.windowBackgroundColor, .systemGreen.withAlphaComponent(0.14), .lightGray, .lightGray, .systemYellow.withAlphaComponent(0.2), .systemRed.withAlphaComponent(0.15)][column] }
            return column == headers.count - 1 ? .systemYellow.withAlphaComponent(0.22) : (column < 6 + mockExamCount ? .systemBlue.withAlphaComponent(0.15) : .systemPurple.withAlphaComponent(0.12))
        }
        return [.windowBackgroundColor, .systemGreen.withAlphaComponent(0.14), .lightGray, .lightGray, .systemYellow.withAlphaComponent(0.2), .systemRed.withAlphaComponent(0.15), .systemBlue.withAlphaComponent(0.15), .systemPink.withAlphaComponent(0.15), .systemPurple.withAlphaComponent(0.12), .systemPurple.withAlphaComponent(0.12), .systemYellow.withAlphaComponent(0.22)][column]
    }

    private var firstStudentRow: Int { layout == .direct ? 1 : 2 }
    private var lastRow: Int { firstStudentRow + rows.count - 1 }
    private func normalized(_ cell: Cell) -> Cell {
        Cell(row: min(max(0, cell.row), max(0, lastRow)), column: min(max(0, cell.column), max(0, widths.count - 1)))
    }
    private func drawText(_ text: String, in rect: NSRect, bold: Bool, centered: Bool) {
        let paragraph = NSMutableParagraphStyle(); paragraph.alignment = centered ? .center : .left; paragraph.lineBreakMode = .byTruncatingTail
        let font = bold ? NSFont.boldSystemFont(ofSize: Self.fontSize) : NSFont.systemFont(ofSize: Self.fontSize)
        let attributes: [NSAttributedString.Key: Any] = [.font: font, .foregroundColor: NSColor.labelColor, .paragraphStyle: paragraph]
        let measuredHeight = ceil(font.boundingRectForFont.height)
        let textRect = NSRect(
            x: rect.minX + 5,
            y: rect.midY - measuredHeight / 2,
            width: max(0, rect.width - 10),
            height: measuredHeight
        )
        NSString(string: text).draw(in: textRect, withAttributes: attributes)
    }
}

final class SpreadsheetTextView: NSTextView {
    var onCommit: (() -> Void)?
    var onCancel: (() -> Void)?

    override func keyDown(with event: NSEvent) {
        switch event.keyCode {
        case 36 where !event.modifierFlags.contains(.option),
             76 where !event.modifierFlags.contains(.option):
            onCommit?()
        case 53:
            onCancel?()
        default:
            super.keyDown(with: event)
        }
    }
}
