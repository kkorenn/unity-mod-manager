import AppKit

final class DropZoneView: NSView {
    var onZipDrop: (([URL]) -> Void)?
    private var highlighted = false

    override init(frame frameRect: NSRect) {
        super.init(frame: frameRect)
        commonInit()
    }

    required init?(coder: NSCoder) {
        super.init(coder: coder)
        commonInit()
    }

    private func commonInit() {
        wantsLayer = true
        layer?.cornerRadius = 8
        layer?.borderWidth = 1
        layer?.borderColor = NSColor(calibratedWhite: 0.35, alpha: 1.0).cgColor
        registerForDraggedTypes([.fileURL])
    }

    override func draw(_ dirtyRect: NSRect) {
        super.draw(dirtyRect)

        let background = highlighted
            ? NSColor(calibratedRed: 0.22, green: 0.28, blue: 0.37, alpha: 1)
            : NSColor(calibratedWhite: 0.16, alpha: 1)
        background.setFill()
        dirtyRect.fill()

        let text = "Drop Mod ZIP Files Here"
        let attrs: [NSAttributedString.Key: Any] = [
            .font: NSFont.systemFont(ofSize: 13, weight: .semibold),
            .foregroundColor: NSColor(calibratedWhite: 0.88, alpha: 1),
        ]
        let textSize = text.size(withAttributes: attrs)
        let textRect = CGRect(
            x: (bounds.width - textSize.width) / 2,
            y: (bounds.height - textSize.height) / 2,
            width: textSize.width,
            height: textSize.height
        )
        text.draw(in: textRect, withAttributes: attrs)
    }

    override func draggingEntered(_ sender: NSDraggingInfo) -> NSDragOperation {
        let urls = extractURLs(from: sender)
        let canHandle = urls.contains { $0.pathExtension.lowercased() == "zip" }
        highlighted = canHandle
        needsDisplay = true
        return canHandle ? .copy : []
    }

    override func draggingExited(_ sender: NSDraggingInfo?) {
        highlighted = false
        needsDisplay = true
    }

    override func prepareForDragOperation(_ sender: NSDraggingInfo) -> Bool {
        true
    }

    override func performDragOperation(_ sender: NSDraggingInfo) -> Bool {
        defer {
            highlighted = false
            needsDisplay = true
        }

        let urls = extractURLs(from: sender).filter { $0.pathExtension.lowercased() == "zip" }
        guard !urls.isEmpty else { return false }
        onZipDrop?(urls)
        return true
    }

    private func extractURLs(from sender: NSDraggingInfo) -> [URL] {
        let pasteboard = sender.draggingPasteboard
        let options: [NSPasteboard.ReadingOptionKey: Any] = [.urlReadingFileURLsOnly: true]
        return pasteboard.readObjects(forClasses: [NSURL.self], options: options) as? [URL] ?? []
    }
}
