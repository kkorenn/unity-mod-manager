import Foundation

final class InstallerService {
    private let fileManager = FileManager.default
    private let bundle: Bundle

    init(bundle: Bundle = .main) {
        self.bundle = bundle
    }

    func payloadRootURL() throws -> URL {
        if let resourceURL = bundle.resourceURL {
            let payload = resourceURL.appendingPathComponent("Payload", isDirectory: true)
            if fileManager.fileExists(atPath: payload.path) {
                return payload
            }
        }

        let cwdPayload = URL(fileURLWithPath: fileManager.currentDirectoryPath, isDirectory: true)
            .appendingPathComponent("Resources", isDirectory: true)
            .appendingPathComponent("Payload", isDirectory: true)
        if fileManager.fileExists(atPath: cwdPayload.path) {
            return cwdPayload
        }

        throw NativeUMMError.missingPayload("Payload/")
    }

    func install(for paths: ADOFAIPaths, applyRosetta: Bool, installMacQOL: Bool) throws -> InstallReport {
        let validatedPaths = try paths.validated()
        let payloadRoot = try payloadRootURL()
        let timestamp = DateFormatter.backupTimestamp.string(from: Date())

        var report = InstallReport()

        let payloadCore = payloadRoot
            .appendingPathComponent("Managed", isDirectory: true)
            .appendingPathComponent("UnityEngine.CoreModule.dll", isDirectory: false)
        let payloadUMMFolder = payloadRoot
            .appendingPathComponent("Managed", isDirectory: true)
            .appendingPathComponent("UnityModManager", isDirectory: true)
        let payloadMacQOLFolder = payloadRoot
            .appendingPathComponent("Mods", isDirectory: true)
            .appendingPathComponent("MacQOL", isDirectory: true)

        guard fileManager.fileExists(atPath: payloadCore.path) else {
            throw NativeUMMError.missingPayload("Managed/UnityEngine.CoreModule.dll")
        }
        guard fileManager.fileExists(atPath: payloadUMMFolder.path) else {
            throw NativeUMMError.missingPayload("Managed/UnityModManager")
        }

        try fileManager.createDirectory(at: validatedPaths.modsURL, withIntermediateDirectories: true)
        try fileManager.createDirectory(at: validatedPaths.unityModManagerURL, withIntermediateDirectories: true)

        // Install patched UnityEngine.CoreModule.dll used by UMM injection.
        let coreDestination = validatedPaths.managedURL.appendingPathComponent("UnityEngine.CoreModule.dll", isDirectory: false)
        if fileManager.fileExists(atPath: coreDestination.path) {
            let backup = coreDestination.deletingLastPathComponent()
                .appendingPathComponent("UnityEngine.CoreModule.dll.nativeumm_backup_\(timestamp)", isDirectory: false)
            try fileManager.copyItem(at: coreDestination, to: backup)
            report.backupPaths.append(backup.path)
        }
        try replaceItem(at: coreDestination, with: payloadCore)
        report.copiedPaths.append(coreDestination.path)

        // Install UMM managed runtime files.
        try copyDirectoryContentsReplacing(source: payloadUMMFolder, destination: validatedPaths.unityModManagerURL)
        report.copiedPaths.append(validatedPaths.unityModManagerURL.path)

        if installMacQOL {
            guard fileManager.fileExists(atPath: payloadMacQOLFolder.path) else {
                throw NativeUMMError.missingPayload("Mods/MacQOL")
            }
            let macQOLDestination = validatedPaths.modsURL.appendingPathComponent("MacQOL", isDirectory: true)
            try replaceDirectory(at: macQOLDestination, with: payloadMacQOLFolder)
            report.copiedPaths.append(macQOLDestination.path)
        }

        if applyRosetta {
            try applyRosettaWrapper(paths: validatedPaths, report: &report)
        }

        report.notes.append("Install completed for A Dance of Fire and Ice only.")
        return report
    }

    func loadMods(for paths: ADOFAIPaths) throws -> [ModRecord] {
        let validatedPaths = try paths.validated()
        guard fileManager.fileExists(atPath: validatedPaths.modsURL.path) else {
            return []
        }

        let folderContents = try fileManager.contentsOfDirectory(
            at: validatedPaths.modsURL,
            includingPropertiesForKeys: [.isDirectoryKey],
            options: [.skipsHiddenFiles]
        )

        var mods: [ModRecord] = []
        for entry in folderContents.sorted(by: { $0.lastPathComponent.lowercased() < $1.lastPathComponent.lowercased() }) {
            let infoURL = entry.appendingPathComponent("Info.json", isDirectory: false)
            guard fileManager.fileExists(atPath: infoURL.path) else { continue }

            let data = try Data(contentsOf: infoURL)
            guard
                let json = try JSONSerialization.jsonObject(with: data) as? [String: Any]
            else {
                continue
            }

            let id = (json["Id"] as? String) ?? entry.lastPathComponent
            let displayName = (json["DisplayName"] as? String) ?? id
            let version = (json["Version"] as? String) ?? "-"

            mods.append(ModRecord(id: id, displayName: displayName, version: version, folderName: entry.lastPathComponent))
        }

        return mods
    }

    func launchGame(for paths: ADOFAIPaths) throws {
        _ = try paths.validated()

        // Preferred Steam launch path.
        do {
            _ = try ProcessRunner.run("/usr/bin/open", arguments: ["steam://rungameid/\(ADOFAIPaths.steamGameID)"])
            return
        } catch {
            // Fallback to app bundle open.
        }

        _ = try ProcessRunner.run("/usr/bin/open", arguments: [paths.appBundleURL.path])
    }

    func importModZips(_ zipURLs: [URL], for paths: ADOFAIPaths) throws -> [String] {
        let validatedPaths = try paths.validated()
        try fileManager.createDirectory(at: validatedPaths.modsURL, withIntermediateDirectories: true)

        var importedFolders: [String] = []

        for zipURL in zipURLs {
            guard zipURL.pathExtension.lowercased() == "zip" else {
                continue
            }

            let tempRoot = fileManager.temporaryDirectory
                .appendingPathComponent("nativeumm_\(UUID().uuidString)", isDirectory: true)
            try fileManager.createDirectory(at: tempRoot, withIntermediateDirectories: true)
            defer { try? fileManager.removeItem(at: tempRoot) }

            _ = try ProcessRunner.run(
                "/usr/bin/unzip",
                arguments: ["-qq", "-o", zipURL.path, "-d", tempRoot.path]
            )

            let infoRoots = findModRoots(in: tempRoot)
            guard !infoRoots.isEmpty else {
                throw NativeUMMError.invalidZip(zipURL)
            }

            for root in infoRoots {
                let destination = validatedPaths.modsURL.appendingPathComponent(root.lastPathComponent, isDirectory: true)
                try replaceDirectory(at: destination, with: root)
                importedFolders.append(root.lastPathComponent)
            }
        }

        return importedFolders
    }

    private func findModRoots(in directory: URL) -> [URL] {
        guard let enumerator = fileManager.enumerator(
            at: directory,
            includingPropertiesForKeys: [.isRegularFileKey],
            options: [.skipsHiddenFiles]
        ) else {
            return []
        }

        var roots: [URL] = []
        var seen = Set<String>()

        for case let fileURL as URL in enumerator {
            guard fileURL.lastPathComponent.lowercased() == "info.json" else { continue }
            let root = fileURL.deletingLastPathComponent()
            let key = root.path
            guard !seen.contains(key) else { continue }
            seen.insert(key)
            roots.append(root)
        }

        return roots.sorted { $0.lastPathComponent.lowercased() < $1.lastPathComponent.lowercased() }
    }

    private func applyRosettaWrapper(paths: ADOFAIPaths, report: inout InstallReport) throws {
        let binaryURL = paths.appBinaryURL
        let realBinaryURL = paths.appBinaryRealURL

        guard fileManager.fileExists(atPath: binaryURL.path) else {
            throw NativeUMMError.missingFile(binaryURL)
        }

        if !fileManager.fileExists(atPath: realBinaryURL.path) {
            try fileManager.moveItem(at: binaryURL, to: realBinaryURL)
            report.notes.append("Moved original executable to ADanceOfFireAndIce.real")
        }

        let wrapper = """
#!/bin/zsh
DIR=\"$(cd \"$(dirname \"$0\")\" && pwd)\"
exec /usr/bin/arch -x86_64 \"$DIR/ADanceOfFireAndIce.real\" \"$@\"
"""

        try wrapper.write(to: binaryURL, atomically: true, encoding: .utf8)
        try ProcessRunner.run("/bin/chmod", arguments: ["755", binaryURL.path])

        report.copiedPaths.append(binaryURL.path)
        report.notes.append("Applied Rosetta wrapper for Apple Silicon compatibility")
    }

    private func copyDirectoryContentsReplacing(source: URL, destination: URL) throws {
        try fileManager.createDirectory(at: destination, withIntermediateDirectories: true)

        for entry in try fileManager.contentsOfDirectory(at: source, includingPropertiesForKeys: nil, options: [.skipsHiddenFiles]) {
            let target = destination.appendingPathComponent(entry.lastPathComponent, isDirectory: false)
            if fileManager.fileExists(atPath: target.path) {
                try fileManager.removeItem(at: target)
            }
            try fileManager.copyItem(at: entry, to: target)
        }
    }

    private func replaceDirectory(at destination: URL, with source: URL) throws {
        if fileManager.fileExists(atPath: destination.path) {
            try fileManager.removeItem(at: destination)
        }
        try fileManager.copyItem(at: source, to: destination)
    }

    private func replaceItem(at destination: URL, with source: URL) throws {
        if fileManager.fileExists(atPath: destination.path) {
            try fileManager.removeItem(at: destination)
        }
        try fileManager.copyItem(at: source, to: destination)
    }
}

private extension DateFormatter {
    static let backupTimestamp: DateFormatter = {
        let formatter = DateFormatter()
        formatter.dateFormat = "yyyyMMdd_HHmmss"
        formatter.locale = Locale(identifier: "en_US_POSIX")
        return formatter
    }()
}
