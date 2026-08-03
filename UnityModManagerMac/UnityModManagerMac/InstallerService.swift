//
//  InstallerService.swift
//  UnityModManagerMac
//
//  Bridges to the NativeAOT C# installer core (libNativeUmm.a) via the
//  umm_run / umm_free C entry points. One JSON request in, one JSON response out.
//

import Foundation
import Combine

struct UmmStatus: Decodable {
    var gameRoot: String?
    var appPath: String?
    var managedPath: String?
    var modsPath: String?
    var processArch: String?
    var gameArch: String?
    var hookInstalled: Bool?
    var managerInstalled: Bool?
    var managerVersion: String?
    var hasBackup: Bool?
    var backupPath: String?
    var warning: String?
}

struct RecommendedMod: Decodable, Identifiable {
    var id: String
    var name: String
    var description: String?
    var url: String
}

/// Recommended mods catalog, bundled as RecommendedMods.json. Add entries there.
enum RecommendedMods {
    static func load() -> [RecommendedMod] {
        guard let url = Bundle.main.url(forResource: "RecommendedMods", withExtension: "json"),
              let data = try? Data(contentsOf: url) else { return [] }
        struct Wrapper: Decodable { var mods: [RecommendedMod] }
        return (try? JSONDecoder().decode(Wrapper.self, from: data))?.mods ?? []
    }
}

struct UmmLogLine: Decodable {
    var level: String
    var message: String
}

/// One entry of a mod's Info.json "Requirements", already resolved by the core
/// against what's actually in Mods/.
struct UmmRequirement: Decodable, Identifiable, Hashable {
    let reqId: String
    let version: String?
    /// "OK" | "Missing" | "Inactive" | "Outdated" — mirrors the in-game tags.
    let state: String

    var id: String { reqId }
    var isSatisfied: Bool { state == "OK" }

    /// "SomeMod (Missing)" when unsatisfied, bare id when it's fine.
    var label: String { isSatisfied ? reqId : "\(reqId) (\(state))" }

    enum CodingKeys: String, CodingKey {
        case reqId = "id", version, state
    }
}

struct UmmMod: Decodable, Identifiable {
    let modId: String
    let name: String
    let version: String
    let status: String
    let managerVersion: String?
    let homePage: String?
    let path: String?
    let installed: Bool?
    let requirements: [UmmRequirement]?

    /// Unique per row (a mod can appear once installed, once removed).
    var id: String { path ?? modId }
    var isInstalled: Bool { installed ?? true }
    var requirementList: [UmmRequirement] { requirements ?? [] }
    var hasUnmetRequirements: Bool { requirementList.contains { !$0.isSatisfied } }

    enum CodingKeys: String, CodingKey {
        case modId = "id", name, version, status, managerVersion, homePage, path, installed, requirements
    }
}

struct UmmResponse: Decodable {
    var ok: Bool
    var detected: Bool
    var error: String?
    var bundledVersion: String?
    var status: UmmStatus?
    var mods: [UmmMod]?
    var log: [UmmLogLine]?
    var logText: String?
}

@MainActor
final class InstallerService: ObservableObject {
    @Published var status: UmmStatus?
    @Published var mods: [UmmMod] = []
    @Published var bundledVersion: String?
    @Published var log: [UmmLogLine] = []
    @Published var logText: String?
    @Published var error: String?
    @Published var detected = false
    @Published var busy = false

    /// User-selected game .app path; nil lets the core auto-detect Steam installs.
    /// Persisted so a manual choice survives relaunch.
    @Published var gamePath: String?

    private static let gamePathKey = "selectedGamePath"

    init() {
        gamePath = UserDefaults.standard.string(forKey: Self.gamePathKey)
    }

    /// True inside Xcode's SwiftUI Preview / Playground JIT environment, which
    /// can't host the NativeAOT runtime — native calls are skipped there.
    static let inPreview: Bool = {
        let env = ProcessInfo.processInfo.environment
        return env["XCODE_RUNNING_FOR_PREVIEWS"] == "1" || env["XCODE_RUNNING_FOR_PLAYGROUNDS"] == "1"
    }()

    func refresh() async { await run("status") }

    /// Set (or clear, with nil) the manual game path, persist it, and re-detect.
    func setGamePath(_ path: String?) async {
        gamePath = path
        let defaults = UserDefaults.standard
        if let path { defaults.set(path, forKey: Self.gamePathKey) }
        else { defaults.removeObject(forKey: Self.gamePathKey) }
        await refresh()
    }
    func install(force: Bool) async { await run("install", force: force) }
    func uninstall() async { await run("remove") }
    func restoreOriginal() async { await run("restore") }
    func listMods() async { await run("mods") }
    func installMod(_ zipPath: String) async { await run("installmod", zipPath: zipPath) }
    func installMods(urls: [String]) async { await run("installmods", urls: urls) }
    func uninstallMod(_ path: String) async { await run("uninstallmod", path: path) }
    func restoreMod(_ path: String) async { await run("restoremod", path: path) }
    func removeMod(_ path: String) async { await run("removemod", path: path) }
    func showLog() async { await run("logtail") }

    private func run(_ action: String, force: Bool = false, zipPath: String? = nil, urls: [String]? = nil, path: String? = nil) async {
        guard !Self.inPreview else { return } // don't call native under the Previews JIT
        guard !busy else { return }
        busy = true
        error = nil

        var request: [String: Any] = ["action": action, "forcePayload": force]
        if let gamePath { request["gamePath"] = gamePath }
        if let zipPath { request["zipPath"] = zipPath }
        if let urls { request["urls"] = urls }
        if let path { request["path"] = path }
        if let payload = Self.bundledPayloadDir() { request["payloadDir"] = payload }

        let requestData = (try? JSONSerialization.data(withJSONObject: request)) ?? Data("{}".utf8)
        let requestJSON = String(decoding: requestData, as: UTF8.self)

        let response = await Task.detached { Self.callNative(requestJSON) }.value

        busy = false

        guard let response else {
            error = "Native installer returned no response."
            return
        }
        detected = response.detected
        error = response.error
        if let version = response.bundledVersion { bundledVersion = version }
        if let s = response.status { status = s }
        if let m = response.mods { mods = m }
        if let lines = response.log { log = lines }
        if let text = response.logText { logText = text }
    }

    /// Path to the UMM DLLs bundled in the .app (Contents/Resources/Payload).
    private static func bundledPayloadDir() -> String? {
        Bundle.main.resourceURL?.appendingPathComponent("Payload").path
    }

    /// Calls the C# core. Runs off the main actor (blocking: may download/patch).
    private nonisolated static func callNative(_ requestJSON: String) -> UmmResponse? {
        let data: Data = requestJSON.withCString { cstr in
            guard let out = umm_run(cstr) else { return Data() }
            defer { umm_free(out) }
            return Data(String(cString: out).utf8)
        }
        guard !data.isEmpty else { return nil }
        return try? JSONDecoder().decode(UmmResponse.self, from: data)
    }
}
