import AppKit
import SwiftUI

struct PerformanceSpreadsheet: NSViewRepresentable {
    @Binding var rows: [PreparedNoticeRow]
    @Binding var homeworkMaximum: String
    @Binding var testMaximum: String
    @Binding var selectedStudentRow: Int
    @Binding var selectedInputColumn: Int
    var onBeforeChange: () -> Void
    var onUndo: () -> Void
    var isInteractionEnabled = true

    nonisolated static func applying(_ changes: [SpreadsheetCanvas.CellChange], to rows: [PreparedNoticeRow], homeworkMaximum: String, testMaximum: String) -> (rows: [PreparedNoticeRow], homeworkMaximum: String, testMaximum: String) {
        var updatedRows = rows
        var updatedHomeworkMaximum = homeworkMaximum
        var updatedTestMaximum = testMaximum
        for change in changes {
            if change.row == 1 {
                if change.column == 6 { updatedHomeworkMaximum = change.value }
                if change.column == 7 { updatedTestMaximum = change.value }
                continue
            }
            let studentIndex = change.row - 2
            guard updatedRows.indices.contains(studentIndex) else { continue }
            switch change.column {
            case 1: updatedRows[studentIndex].isIncluded = ["true", "1", "yes", "y", "☑︎", "☑"].contains(change.value.lowercased())
            case 4: updatedRows[studentIndex].attendance = change.value
            case 5: updatedRows[studentIndex].attitude = change.value
            case 6: updatedRows[studentIndex].homework = change.value
            case 7: updatedRows[studentIndex].test = change.value
            case 8: updatedRows[studentIndex].homeworkComment = change.value
            case 9: updatedRows[studentIndex].testComment = change.value
            case 10: updatedRows[studentIndex].noticeMessage = change.value
            default: break
            }
        }
        return (updatedRows, updatedHomeworkMaximum, updatedTestMaximum)
    }

    func makeCoordinator() -> Coordinator { Coordinator(parent: self) }

    func makeNSView(context: Context) -> SpreadsheetCanvas {
        let view = SpreadsheetCanvas()
        view.onApply = { changes in context.coordinator.apply(changes) }
        view.onBeforeChange = onBeforeChange
        view.onUndo = onUndo
        view.isInteractionEnabled = isInteractionEnabled
        view.onSelectionChange = { cell in context.coordinator.updateSelection(cell) }
        view.update(rows: rows, homeworkMaximum: homeworkMaximum, testMaximum: testMaximum)
        return view
    }

    func updateNSView(_ view: SpreadsheetCanvas, context: Context) {
        context.coordinator.parent = self
        view.onBeforeChange = onBeforeChange
        view.onUndo = onUndo
        view.isInteractionEnabled = isInteractionEnabled
        view.onSelectionChange = { cell in context.coordinator.updateSelection(cell) }
        view.update(rows: rows, homeworkMaximum: homeworkMaximum, testMaximum: testMaximum)
    }

    @MainActor final class Coordinator {
        var parent: PerformanceSpreadsheet
        init(parent: PerformanceSpreadsheet) { self.parent = parent }

        func apply(_ changes: [SpreadsheetCanvas.CellChange]) {
            guard !changes.isEmpty else { return }
            let result = PerformanceSpreadsheet.applying(changes, to: parent.rows, homeworkMaximum: parent.homeworkMaximum, testMaximum: parent.testMaximum)
            parent.rows = result.rows
            parent.homeworkMaximum = result.homeworkMaximum
            parent.testMaximum = result.testMaximum
        }

        func updateSelection(_ cell: SpreadsheetCanvas.Cell) {
            guard cell.row >= 2, cell.column >= 4 else { return }
            parent.selectedStudentRow = cell.row - 2
            parent.selectedInputColumn = cell.column - 4
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

    private let widths: [CGFloat] = [45, 56, 90, 90, 90, 70, 85, 85, 180, 180, 360]
    private let headers = ["번호", "발송", "성명", "호칭", "출석", "태도", "숙제", "테스트", "숙제 코멘트", "테스트 코멘트", "공지 멘트"]
    private var rows: [PreparedNoticeRow] = []
    private var homeworkMaximum = ""
    private var testMaximum = ""
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
        NSSize(width: widths.reduce(0, +), height: Self.headerHeight + CGFloat(rows.count + 1) * Self.rowHeight)
    }

    func update(rows: [PreparedNoticeRow], homeworkMaximum: String, testMaximum: String) {
        self.rows = rows; self.homeworkMaximum = homeworkMaximum; self.testMaximum = testMaximum
        invalidateIntrinsicContentSize(); needsDisplay = true
    }

    override func draw(_ dirtyRect: NSRect) {
        super.draw(dirtyRect)
        NSColor.textBackgroundColor.setFill(); dirtyRect.fill()
        for row in 0..<(rows.count + 2) {
            for column in widths.indices {
                let rect = cellRect(row: row, column: column)
                if selected(row: row, column: column) { NSColor.controlAccentColor.withAlphaComponent(0.14).setFill(); rect.fill() }
                fillForCell(row: row, column: column)?.setFill(); if let _ = fillForCell(row: row, column: column) { rect.fill() }
                NSColor.separatorColor.setStroke(); NSBezierPath(rect: rect).stroke()
                drawText(value(row: row, column: column), in: rect, bold: row == 0 || row == 1, centered: column < 8)
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
        let changes = Self.deletionChanges(anchor: anchor, cursor: cursor, studentCount: rows.count) { [weak self] row, column in
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
        cursor = Cell(row: min(rows.count + 1, start.row + source.count - 1), column: min(widths.count - 1, start.column + (source.map(\.count).max() ?? 1) - 1))
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
        cursor = Cell(row: min(max(0, cursor.row + dy), rows.count + 1), column: min(max(0, cursor.column + dx), widths.count - 1)); anchor = cursor; needsDisplay = true; notifySelection()
    }
    private func notifySelection() {
        let topLeft = Cell(row: min(anchor.row, cursor.row), column: min(anchor.column, cursor.column))
        onSelectionChange?(topLeft)
    }
    private func isSendCheckbox(_ cell: Cell) -> Bool {
        cell.column == 1 && cell.row >= 2 && cell.row < rows.count + 2
    }
    private func toggleSendCheckbox(_ cell: Cell) {
        guard isSendCheckbox(cell), rows.indices.contains(cell.row - 2) else { return }
        onBeforeChange?()
        onApply?([CellChange(row: cell.row, column: cell.column, value: rows[cell.row - 2].isIncluded ? "false" : "true")])
        needsDisplay = true
    }
    private func editable(_ cell: Cell) -> Bool { (cell.row == 1 && [6, 7].contains(cell.column)) || (cell.row >= 2 && cell.row < rows.count + 2 && cell.column >= 4) }
    nonisolated static func deletionChanges(anchor: Cell, cursor: Cell, studentCount: Int, value: (Int, Int) -> String) -> [CellChange] {
        let rowRange = min(anchor.row, cursor.row)...max(anchor.row, cursor.row)
        let columnRange = min(anchor.column, cursor.column)...max(anchor.column, cursor.column)
        var changes: [CellChange] = []
        for row in rowRange {
            for column in columnRange {
                let isEditable = (row == 1 && [6, 7].contains(column)) || (row >= 2 && row < studentCount + 2 && column >= 4 && column <= 10)
                if isEditable, !value(row, column).isEmpty { changes.append(CellChange(row: row, column: column, value: "")) }
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
        guard row <= rows.count + 1 else { return nil }
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
        if row == 1 { if column == 2 { return "총 개수" }; if column == 6 { return homeworkMaximum }; if column == 7 { return testMaximum }; return "" }
        guard rows.indices.contains(row - 2) else { return "" }; let item = rows[row - 2]
        switch column {
        case 0: return String(item.number)
        case 1: return item.isIncluded ? "☑︎" : "☐"
        case 2: return item.name
        case 3: return item.nickname
        case 4: return item.attendance
        case 5: return item.attitude
        case 6: return item.homework
        case 7: return item.test
        case 8: return item.homeworkComment
        case 9: return item.testComment
        default: return item.noticeMessage
        }
    }
    private func fillForCell(row: Int, column: Int) -> NSColor? {
        guard row <= 1 else { return nil }
        if row == 1 && column == 6 { return .systemBlue.withAlphaComponent(0.12) }
        if row == 1 && column == 7 { return .systemPink.withAlphaComponent(0.12) }
        return [.windowBackgroundColor, .systemGreen.withAlphaComponent(0.14), .lightGray, .lightGray, .systemYellow.withAlphaComponent(0.2), .systemRed.withAlphaComponent(0.15), .systemBlue.withAlphaComponent(0.15), .systemPink.withAlphaComponent(0.15), .systemPurple.withAlphaComponent(0.12), .systemPurple.withAlphaComponent(0.12), .systemYellow.withAlphaComponent(0.22)][column]
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
