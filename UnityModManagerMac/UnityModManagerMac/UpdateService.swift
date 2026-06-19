//
//  UpdateService.swift
//  UnityModManagerMac
//
//  Self-update against the GitHub Releases of the kkorenn/unity-mod-manager
//  fork. On launch it asks the GitHub API for the latest release, compares the
//  tag to this app's CFBundleShortVersionString, and — if newer — offers a
//  one-click update: download the .dmg, mount it, and hand off to a tiny shell
//  helper that swaps the running .app once we quit, then relaunches.
//

import Foundation
import AppKit
import Combine

/// A release newer than the running app, ready to install.
struct AvailableUpdate: Equatable {
    let version: String          // normalized, e.g. "1.1.0"
    let tag: String              // raw tag, e.g. "v1.1.0"
    let dmgURL: URL              // .dmg asset download
    let pageURL: URL?            // release html_url (browser fallback)
    let notes: String            // release body
}

@MainActor
final class UpdateService: ObservableObject {
    /// Set once a strictly-newer release with a .dmg asset is found.
    @Published var available: AvailableUpdate?
    /// Drives the banner UI during an update.
    @Published var phase: Phase = .idle
    /// True while a check is in flight (drives the Settings "Check Now" spinner).
    @Published var checking = false
    /// When the last successful check completed; nil until the first one.
    @Published var lastCheckedAt: Date?

    init() {
        let t = UserDefaults.standard.double(forKey: SettingsKey.lastCheck)
        if t > 0 { lastCheckedAt = Date(timeIntervalSince1970: t) }
    }

    enum Phase: Equatable {
        case idle
        case downloading
        case installing
        case failed(String)
    }

    /// CFBundleShortVersionString of the running app (MARKETING_VERSION), e.g. "1.0".
    let currentVersion: String =
        (Bundle.main.infoDictionary?["CFBundleShortVersionString"] as? String) ?? "0"

    private static let latestReleaseURL =
        URL(string: "https://api.github.com/repos/kkorenn/unity-mod-manager/releases/latest")!

    /// Don't hit the network under the SwiftUI Preview / Playground JIT.
    private static let inPreview: Bool = {
        let env = ProcessInfo.processInfo.environment
        return env["XCODE_RUNNING_FOR_PREVIEWS"] == "1" || env["XCODE_RUNNING_FOR_PLAYGROUNDS"] == "1"
    }()

    // MARK: - Check

    /// Launch-time check that honors the user's settings: skips when auto-update
    /// is off or set to manual, and throttles by the chosen frequency.
    func autoCheck() async {
        let d = UserDefaults.standard
        let enabled = d.object(forKey: SettingsKey.autoUpdate) as? Bool ?? true
        guard enabled else { return }

        let freq = UpdateFrequency(rawValue: d.string(forKey: SettingsKey.frequency) ?? "") ?? .onLaunch
        guard let interval = freq.interval else { return } // .manual

        let last = d.double(forKey: SettingsKey.lastCheck)
        if interval > 0, last > 0, Date().timeIntervalSince1970 - last < interval { return }

        await check()
    }

    /// Queries the latest release and publishes `available` when it's newer.
    /// Always runs (manual "Check Now" path); auto-check gating lives in autoCheck().
    func check() async {
        guard !Self.inPreview, !checking else { return }
        checking = true
        defer { checking = false }
        do {
            var req = URLRequest(url: Self.latestReleaseURL)
            req.setValue("application/vnd.github+json", forHTTPHeaderField: "Accept")
            req.setValue("UnityModManagerMac", forHTTPHeaderField: "User-Agent") // GitHub requires a UA
            req.timeoutInterval = 15

            let (data, response) = try await URLSession.shared.data(for: req)
            guard let http = response as? HTTPURLResponse, http.statusCode == 200 else { return }

            // Record a successful contact so frequency throttling works.
            let now = Date()
            UserDefaults.standard.set(now.timeIntervalSince1970, forKey: SettingsKey.lastCheck)
            lastCheckedAt = now

            let release = try JSONDecoder().decode(GitHubRelease.self, from: data)
            guard !release.draft, !release.prerelease else { return }

            let remote = Self.normalize(release.tagName)
            guard Self.isNewer(remote, than: Self.normalize(currentVersion)) else { return }

            // Prefer the .dmg asset; nothing to offer without one.
            guard let asset = release.assets.first(where: { $0.name.lowercased().hasSuffix(".dmg") }),
                  let dmgURL = URL(string: asset.browserDownloadURL) else { return }

            available = AvailableUpdate(
                version: remote,
                tag: release.tagName,
                dmgURL: dmgURL,
                pageURL: release.htmlURL.flatMap(URL.init(string:)),
                notes: release.body ?? "")
        } catch {
            // Offline / rate-limited / parse error: stay silent, just don't offer an update.
        }
    }

    // MARK: - Update

    /// Downloads the release .dmg, mounts it, and launches the swap helper. On
    /// success this terminates the app; the helper relaunches the new build.
    func update() async {
        guard let upd = available, phase == .idle else { return }
        phase = .downloading
        do {
            let dmg = try await download(upd.dmgURL)
            phase = .installing
            let (mountPoint, device) = try Self.attach(dmg)

            guard let newApp = Self.firstApp(in: mountPoint) else {
                Self.detach(device)
                throw UpdateError("No .app found inside the downloaded disk image.")
            }
            let dest = Bundle.main.bundleURL
            try Self.launchSwapHelper(newApp: newApp, dest: dest, device: device, dmg: dmg)

            // Hand off to the helper, which waits for us to exit before swapping.
            NSApp.terminate(nil)
        } catch {
            phase = .failed((error as? UpdateError)?.message ?? error.localizedDescription)
        }
    }

    /// Opens the release page so the user can update by hand if a swap fails.
    func openReleasePage() {
        if let url = available?.pageURL { NSWorkspace.shared.open(url) }
    }

    private func download(_ url: URL) async throws -> URL {
        var req = URLRequest(url: url)
        req.setValue("UnityModManagerMac", forHTTPHeaderField: "User-Agent")
        req.timeoutInterval = 300
        let (tmp, response) = try await URLSession.shared.download(for: req)
        guard let http = response as? HTTPURLResponse, (200..<300).contains(http.statusCode) else {
            throw UpdateError("Download failed (HTTP \((response as? HTTPURLResponse)?.statusCode ?? -1)).")
        }
        // Move to a stable .dmg path; the URLSession temp file is unsuffixed and short-lived.
        let dest = FileManager.default.temporaryDirectory
            .appendingPathComponent("UnityModManagerMac-update-\(UUID().uuidString).dmg")
        try? FileManager.default.removeItem(at: dest)
        try FileManager.default.moveItem(at: tmp, to: dest)
        return dest
    }

    // MARK: - Version compare

    /// Strips a leading v/V and keeps the leading dotted-numeric core ("v1.2.0-beta" -> "1.2.0").
    static func normalize(_ raw: String) -> String {
        var s = raw.trimmingCharacters(in: .whitespacesAndNewlines)
        if let f = s.first, f == "v" || f == "V" { s.removeFirst() }
        let core = s.prefix { $0.isNumber || $0 == "." }
        return core.isEmpty ? "0" : String(core)
    }

    /// True when dotted-numeric `remote` is strictly greater than `local`
    /// (missing components treated as 0, so "1.1" > "1.0.0").
    static func isNewer(_ remote: String, than local: String) -> Bool {
        let r = remote.split(separator: ".").map { Int($0) ?? 0 }
        let l = local.split(separator: ".").map { Int($0) ?? 0 }
        for i in 0..<max(r.count, l.count) {
            let rv = i < r.count ? r[i] : 0
            let lv = i < l.count ? l[i] : 0
            if rv != lv { return rv > lv }
        }
        return false
    }

    // MARK: - DMG helpers

    /// `hdiutil attach` the image; returns (mountPoint, devEntry) for the data volume.
    private static func attach(_ dmg: URL) throws -> (mount: String, device: String) {
        let out = try runData("/usr/bin/hdiutil",
                              ["attach", dmg.path, "-nobrowse", "-noverify", "-plist"])
        guard
            let plist = try? PropertyListSerialization.propertyList(from: out, format: nil) as? [String: Any],
            let entities = plist["system-entities"] as? [[String: Any]]
        else { throw UpdateError("Could not read the disk image.") }

        var device = ""
        var mount = ""
        for e in entities {
            if let dev = e["dev-entry"] as? String, device.isEmpty || (e["mount-point"] != nil) {
                device = dev
            }
            if let mp = e["mount-point"] as? String, !mp.isEmpty { mount = mp }
        }
        guard !mount.isEmpty, !device.isEmpty else { throw UpdateError("Disk image did not mount.") }
        return (mount, device)
    }

    private static func detach(_ device: String) {
        _ = try? runData("/usr/bin/hdiutil", ["detach", device, "-force"])
    }

    private static func firstApp(in mountPoint: String) -> URL? {
        let dir = URL(fileURLWithPath: mountPoint)
        let items = (try? FileManager.default.contentsOfDirectory(
            at: dir, includingPropertiesForKeys: nil)) ?? []
        return items.first { $0.pathExtension == "app" }
    }

    /// Writes and launches a detached shell helper that waits for this process to
    /// exit, replaces the app bundle, detaches the image, and relaunches.
    private static func launchSwapHelper(newApp: URL, dest: URL, device: String, dmg: URL) throws {
        let pid = ProcessInfo.processInfo.processIdentifier
        let script = """
        #!/bin/bash
        # Self-update swap helper for UnityModManagerMac. Args set below.
        PID="\(pid)"
        SRC="\(newApp.path)"
        DEST="\(dest.path)"
        DEVICE="\(device)"
        DMG="\(dmg.path)"

        # Wait for the app to quit so the bundle isn't busy.
        while kill -0 "$PID" 2>/dev/null; do sleep 0.3; done

        /bin/rm -rf "$DEST"
        if /bin/cp -R "$SRC" "$DEST"; then
            /usr/bin/xattr -dr com.apple.quarantine "$DEST" 2>/dev/null || true
            /usr/bin/hdiutil detach "$DEVICE" -force >/dev/null 2>&1 || true
            /bin/rm -f "$DMG" 2>/dev/null || true
            /usr/bin/open "$DEST"
        else
            # Swap failed (e.g. permissions): leave the image mounted so the
            # user can drag it across manually, and reopen the old app.
            /usr/bin/open "$DEST" 2>/dev/null || true
        fi
        """
        let scriptURL = FileManager.default.temporaryDirectory
            .appendingPathComponent("umm-update-\(UUID().uuidString).sh")
        try script.write(to: scriptURL, atomically: true, encoding: .utf8)

        let task = Process()
        task.executableURL = URL(fileURLWithPath: "/bin/bash")
        task.arguments = [scriptURL.path]
        // Detach from our process group so it survives our termination.
        try task.run()
    }

    @discardableResult
    private static func runData(_ launchPath: String, _ args: [String]) throws -> Data {
        let task = Process()
        task.executableURL = URL(fileURLWithPath: launchPath)
        task.arguments = args
        let pipe = Pipe()
        task.standardOutput = pipe
        task.standardError = Pipe()
        try task.run()
        let data = pipe.fileHandleForReading.readDataToEndOfFile()
        task.waitUntilExit()
        guard task.terminationStatus == 0 else {
            throw UpdateError("\(launchPath) exited \(task.terminationStatus).")
        }
        return data
    }
}

struct UpdateError: Error { let message: String; init(_ m: String) { message = m } }

// MARK: - GitHub API model

private struct GitHubRelease: Decodable {
    let tagName: String
    let htmlURL: String?
    let body: String?
    let draft: Bool
    let prerelease: Bool
    let assets: [Asset]

    struct Asset: Decodable {
        let name: String
        let browserDownloadURL: String
        enum CodingKeys: String, CodingKey {
            case name
            case browserDownloadURL = "browser_download_url"
        }
    }

    enum CodingKeys: String, CodingKey {
        case tagName = "tag_name"
        case htmlURL = "html_url"
        case body, draft, prerelease, assets
    }
}
