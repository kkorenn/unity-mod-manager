import AppKit

@MainActor
final class MainWindowController: NSWindowController {
    private enum DefaultsKey {
        static let gamePath = "adofai.game.path"
        static let rosetta = "install.rosetta"
        static let macQOL = "install.macqol"
    }

    private let installer = InstallerService()
    private let modsTableController = ModsTableController()

    private let gamePathField = NSTextField(string: "")
    private let rosettaCheckbox = NSButton(checkboxWithTitle: "Apply Rosetta wrapper on install", target: nil, action: nil)
    private let macQOLCheckbox = NSButton(checkboxWithTitle: "Install Mac QOL (ADOFAI only)", target: nil, action: nil)

    private let installButton = NSButton(title: "Install / Repair", target: nil, action: nil)
    private let launchButton = NSButton(title: "Launch Game", target: nil, action: nil)
    private let refreshButton = NSButton(title: "Refresh Mods", target: nil, action: nil)
    private let openModsButton = NSButton(title: "Open Mods Folder", target: nil, action: nil)

    private let modsTableView = NSTableView(frame: .zero)
    private let dropZoneView = DropZoneView(frame: .zero)
    private let logTextView = NSTextView(frame: .zero)

    private var isBusy = false {
        didSet {
            installButton.isEnabled = !isBusy
            launchButton.isEnabled = !isBusy
            refreshButton.isEnabled = !isBusy
            openModsButton.isEnabled = !isBusy
        }
    }

    init() {
        let window = NSWindow(
            contentRect: NSRect(x: 0, y: 0, width: 1040, height: 760),
            styleMask: [.titled, .closable, .miniaturizable, .resizable],
            backing: .buffered,
            defer: false
        )
        window.title = "Unity Mod Manager"
        window.appearance = NSAppearance(named: .darkAqua)
        window.backgroundColor = NSColor(calibratedWhite: 0.12, alpha: 1)

        super.init(window: window)

        setupUI()
        loadDefaults()
        refreshMods()
        appendLog("Native Apple Silicon UMM ready. Supported game: A Dance of Fire and Ice.")
    }

    @available(*, unavailable)
    required init?(coder: NSCoder) {
        nil
    }

    private func setupUI() {
        guard let contentView = window?.contentView else { return }

        let root = NSStackView()
        root.orientation = .vertical
        root.spacing = 10
        root.translatesAutoresizingMaskIntoConstraints = false
        contentView.addSubview(root)

        NSLayoutConstraint.activate([
            root.leadingAnchor.constraint(equalTo: contentView.leadingAnchor, constant: 14),
            root.trailingAnchor.constraint(equalTo: contentView.trailingAnchor, constant: -14),
            root.topAnchor.constraint(equalTo: contentView.topAnchor, constant: 14),
            root.bottomAnchor.constraint(equalTo: contentView.bottomAnchor, constant: -14),
        ])

        let titleLabel = NSTextField(labelWithString: "Unity Mod Manager")
        titleLabel.font = NSFont.systemFont(ofSize: 24, weight: .bold)
        titleLabel.textColor = NSColor(calibratedWhite: 0.95, alpha: 1)
        root.addArrangedSubview(titleLabel)

        let subtitle = NSTextField(labelWithString: "Native macOS build (no Mono runtime). ADOFAI support only.")
        subtitle.font = NSFont.systemFont(ofSize: 12, weight: .regular)
        subtitle.textColor = NSColor(calibratedWhite: 0.75, alpha: 1)
        root.addArrangedSubview(subtitle)

        root.addArrangedSubview(makeGamePathRow())
        root.addArrangedSubview(makeActionRow())
        root.addArrangedSubview(makeOptionsRow())

        let modsBox = NSBox()
        modsBox.title = "Installed Mods"
        modsBox.titleFont = NSFont.systemFont(ofSize: 12, weight: .semibold)
        modsBox.boxType = .custom
        modsBox.borderColor = NSColor(calibratedWhite: 0.30, alpha: 1)
        modsBox.contentViewMargins = NSSize(width: 6, height: 6)
        modsBox.translatesAutoresizingMaskIntoConstraints = false

        let tableScroll = NSScrollView()
        tableScroll.hasVerticalScroller = true
        tableScroll.drawsBackground = true
        tableScroll.backgroundColor = NSColor(calibratedWhite: 0.10, alpha: 1)
        tableScroll.borderType = .noBorder
        tableScroll.documentView = modsTableView

        modsTableView.usesAlternatingRowBackgroundColors = false
        modsTableView.backgroundColor = NSColor(calibratedWhite: 0.10, alpha: 1)
        modsTableView.gridStyleMask = [.solidHorizontalGridLineMask]
        modsTableView.gridColor = NSColor(calibratedWhite: 0.22, alpha: 1)
        modsTableView.headerView = NSTableHeaderView(frame: NSRect(x: 0, y: 0, width: 900, height: 20))
        modsTableView.rowHeight = 24
        modsTableView.delegate = modsTableController
        modsTableView.dataSource = modsTableController
        modsTableView.translatesAutoresizingMaskIntoConstraints = false

        addColumn(id: .name, title: "Name", width: 280)
        addColumn(id: .version, title: "Version", width: 120)
        addColumn(id: .id, title: "Id", width: 220)
        addColumn(id: .folder, title: "Folder", width: 280)

        modsBox.contentView?.addSubview(tableScroll)
        tableScroll.translatesAutoresizingMaskIntoConstraints = false
        if let boxContent = modsBox.contentView {
            NSLayoutConstraint.activate([
                tableScroll.leadingAnchor.constraint(equalTo: boxContent.leadingAnchor),
                tableScroll.trailingAnchor.constraint(equalTo: boxContent.trailingAnchor),
                tableScroll.topAnchor.constraint(equalTo: boxContent.topAnchor),
                tableScroll.bottomAnchor.constraint(equalTo: boxContent.bottomAnchor),
            ])
        }

        root.addArrangedSubview(modsBox)
        modsBox.heightAnchor.constraint(greaterThanOrEqualToConstant: 320).isActive = true

        dropZoneView.translatesAutoresizingMaskIntoConstraints = false
        dropZoneView.heightAnchor.constraint(equalToConstant: 64).isActive = true
        dropZoneView.onZipDrop = { [weak self] urls in
            self?.importZipMods(urls)
        }
        root.addArrangedSubview(dropZoneView)

        let logBox = NSBox()
        logBox.title = "Log"
        logBox.titleFont = NSFont.systemFont(ofSize: 12, weight: .semibold)
        logBox.boxType = .custom
        logBox.borderColor = NSColor(calibratedWhite: 0.30, alpha: 1)
        logBox.contentViewMargins = NSSize(width: 6, height: 6)
        root.addArrangedSubview(logBox)
        logBox.heightAnchor.constraint(equalToConstant: 160).isActive = true

        let logScroll = NSScrollView()
        logScroll.hasVerticalScroller = true
        logScroll.drawsBackground = true
        logScroll.backgroundColor = NSColor(calibratedWhite: 0.08, alpha: 1)

        logTextView.isEditable = false
        logTextView.isSelectable = true
        logTextView.drawsBackground = true
        logTextView.backgroundColor = NSColor(calibratedWhite: 0.08, alpha: 1)
        logTextView.textColor = NSColor(calibratedWhite: 0.88, alpha: 1)
        logTextView.font = NSFont.monospacedSystemFont(ofSize: 11, weight: .regular)

        logScroll.documentView = logTextView
        logBox.contentView?.addSubview(logScroll)
        logScroll.translatesAutoresizingMaskIntoConstraints = false
        if let boxContent = logBox.contentView {
            NSLayoutConstraint.activate([
                logScroll.leadingAnchor.constraint(equalTo: boxContent.leadingAnchor),
                logScroll.trailingAnchor.constraint(equalTo: boxContent.trailingAnchor),
                logScroll.topAnchor.constraint(equalTo: boxContent.topAnchor),
                logScroll.bottomAnchor.constraint(equalTo: boxContent.bottomAnchor),
            ])
        }
    }

    private func makeGamePathRow() -> NSView {
        let row = NSStackView()
        row.orientation = .horizontal
        row.spacing = 8

        let label = NSTextField(labelWithString: "Game Folder")
        label.font = NSFont.systemFont(ofSize: 12, weight: .semibold)
        label.textColor = NSColor(calibratedWhite: 0.85, alpha: 1)
        label.setContentHuggingPriority(.required, for: .horizontal)

        gamePathField.font = NSFont.monospacedSystemFont(ofSize: 12, weight: .regular)
        gamePathField.placeholderString = ADOFAIPaths.defaultInstallURL().path

        let browseButton = NSButton(title: "Browse", target: self, action: #selector(browseForGamePath))
        browseButton.bezelStyle = .rounded

        let detectButton = NSButton(title: "Detect Default", target: self, action: #selector(useDefaultGamePath))
        detectButton.bezelStyle = .rounded

        row.addArrangedSubview(label)
        row.addArrangedSubview(gamePathField)
        row.addArrangedSubview(browseButton)
        row.addArrangedSubview(detectButton)

        return row
    }

    private func makeActionRow() -> NSView {
        let row = NSStackView()
        row.orientation = .horizontal
        row.spacing = 8

        installButton.target = self
        installButton.action = #selector(installOrRepair)
        installButton.bezelStyle = .texturedRounded
        installButton.keyEquivalent = "i"

        launchButton.target = self
        launchButton.action = #selector(launchGame)
        launchButton.bezelStyle = .texturedRounded

        refreshButton.target = self
        refreshButton.action = #selector(refreshModsAction)
        refreshButton.bezelStyle = .texturedRounded

        openModsButton.target = self
        openModsButton.action = #selector(openModsFolder)
        openModsButton.bezelStyle = .texturedRounded

        row.addArrangedSubview(installButton)
        row.addArrangedSubview(launchButton)
        row.addArrangedSubview(refreshButton)
        row.addArrangedSubview(openModsButton)
        row.addArrangedSubview(NSView())

        return row
    }

    private func makeOptionsRow() -> NSView {
        let row = NSStackView()
        row.orientation = .horizontal
        row.spacing = 16

        rosettaCheckbox.target = self
        rosettaCheckbox.action = #selector(toggleOption)
        macQOLCheckbox.target = self
        macQOLCheckbox.action = #selector(toggleOption)

        row.addArrangedSubview(rosettaCheckbox)
        row.addArrangedSubview(macQOLCheckbox)
        row.addArrangedSubview(NSView())

        return row
    }

    private func addColumn(id: ModsTableController.Column, title: String, width: CGFloat) {
        let column = NSTableColumn(identifier: NSUserInterfaceItemIdentifier(id.rawValue))
        column.title = title
        column.width = width
        modsTableView.addTableColumn(column)
    }

    private func currentPaths() throws -> ADOFAIPaths {
        let raw = gamePathField.stringValue.trimmingCharacters(in: .whitespacesAndNewlines)
        let path = raw.isEmpty ? ADOFAIPaths.defaultInstallURL().path : raw
        return try ADOFAIPaths(rootURL: URL(fileURLWithPath: path, isDirectory: true)).validated()
    }

    private func loadDefaults() {
        let defaults = UserDefaults.standard

        let path = defaults.string(forKey: DefaultsKey.gamePath) ?? ADOFAIPaths.defaultInstallURL().path
        gamePathField.stringValue = path

        let rosettaEnabled = defaults.object(forKey: DefaultsKey.rosetta) as? Bool ?? true
        rosettaCheckbox.state = rosettaEnabled ? .on : .off

        let macQOLEnabled = defaults.object(forKey: DefaultsKey.macQOL) as? Bool ?? true
        macQOLCheckbox.state = macQOLEnabled ? .on : .off
    }

    private func saveDefaults() {
        let defaults = UserDefaults.standard
        defaults.set(gamePathField.stringValue, forKey: DefaultsKey.gamePath)
        defaults.set(rosettaCheckbox.state == .on, forKey: DefaultsKey.rosetta)
        defaults.set(macQOLCheckbox.state == .on, forKey: DefaultsKey.macQOL)
    }

    private func appendLog(_ line: String) {
        let formatter = DateFormatter()
        formatter.dateFormat = "HH:mm:ss"
        formatter.locale = Locale(identifier: "en_US_POSIX")
        let timestamp = formatter.string(from: Date())

        let message = "[\(timestamp)] \(line)\n"
        logTextView.textStorage?.append(NSAttributedString(string: message))
        logTextView.scrollToEndOfDocument(nil)
    }

    private func refreshMods() {
        do {
            let paths = try currentPaths()
            let mods = try installer.loadMods(for: paths)
            modsTableController.mods = mods
            modsTableView.reloadData()
            appendLog("Loaded \(mods.count) mod(s).")
            saveDefaults()
        } catch {
            modsTableController.mods = []
            modsTableView.reloadData()
            appendLog("Refresh failed: \(error.localizedDescription)")
        }
    }

    private func performTask(refreshModsAfter: Bool = false, _ task: () throws -> String) {
        isBusy = true
        defer { isBusy = false }

        do {
            let message = try task()
            appendLog(message)
            if refreshModsAfter {
                refreshMods()
            }
        } catch {
            appendLog("Error: \(error.localizedDescription)")
        }
    }

    private func importZipMods(_ urls: [URL]) {
        performTask(refreshModsAfter: true) {
            let paths = try currentPaths()
            let imported = try installer.importModZips(urls, for: paths)
            saveDefaults()
            return "Imported \(imported.count) mod folder(s): \(imported.joined(separator: ", "))"
        }
    }

    @objc private func browseForGamePath() {
        let panel = NSOpenPanel()
        panel.canChooseFiles = false
        panel.canChooseDirectories = true
        panel.canCreateDirectories = false
        panel.allowsMultipleSelection = false
        panel.prompt = "Select"
        panel.message = "Select your A Dance of Fire and Ice game folder."

        if panel.runModal() == .OK, let url = panel.url {
            gamePathField.stringValue = url.path
            saveDefaults()
            refreshMods()
        }
    }

    @objc private func useDefaultGamePath() {
        gamePathField.stringValue = ADOFAIPaths.defaultInstallURL().path
        saveDefaults()
        refreshMods()
    }

    @objc private func toggleOption() {
        saveDefaults()
    }

    @objc private func installOrRepair() {
        performTask(refreshModsAfter: true) {
            let paths = try currentPaths()
            let report = try installer.install(
                for: paths,
                applyRosetta: rosettaCheckbox.state == .on,
                installMacQOL: macQOLCheckbox.state == .on
            )
            saveDefaults()

            var lines: [String] = []
            lines.append("Install/repair completed.")
            lines.append("Copied: \(report.copiedPaths.count) path(s)")
            if !report.backupPaths.isEmpty {
                lines.append("Backups: \(report.backupPaths.count) path(s)")
            }
            for note in report.notes {
                lines.append(note)
            }
            return lines.joined(separator: " | ")
        }
    }

    @objc private func launchGame() {
        performTask {
            let paths = try currentPaths()
            try installer.launchGame(for: paths)
            return "Launch command sent."
        }
    }

    @objc private func refreshModsAction() {
        refreshMods()
    }

    @objc private func openModsFolder() {
        do {
            let paths = try currentPaths()
            NSWorkspace.shared.open(paths.modsURL)
        } catch {
            appendLog("Cannot open Mods folder: \(error.localizedDescription)")
        }
    }
}
