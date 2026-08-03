//
//  ContentView.swift
//  UnityModManagerMac
//
//  Layout mirrors the classic Windows UnityModManager Installer: Install / Mods /
//  Log tabs, with a status bar along the bottom.
//

import SwiftUI
import AppKit
import UniformTypeIdentifiers

struct ContentView: View {
    @StateObject private var service = InstallerService()
    @StateObject private var updater = UpdateService()
    @State private var tab = Tab.install
    @State private var confirm: ConfirmAction?

    enum Tab: Hashable { case install, mods, log, settings }

    var body: some View {
        VStack(spacing: 0) {
            TabView(selection: $tab) {
                InstallTab(service: service, updater: updater, confirm: $confirm)
                    .tabItem { Text("Install") }.tag(Tab.install)
                ModsTab(service: service)
                    .tabItem { Text("Mods") }.tag(Tab.mods)
                LogTab(service: service)
                    .tabItem { Text("Log") }.tag(Tab.log)
                SettingsTab(updater: updater)
                    .tabItem { Text("Settings") }.tag(Tab.settings)
            }
            .padding(12)

            statusBar
        }
        .frame(width: 540, height: 660)
        .overlay { if service.busy { busyOverlay } }
        .task { await service.refresh() }
        .task { await updater.autoCheck() }
        // .task(id:) (macOS 12+) stands in for the macOS-14-only two-param
        // onChange so one binary runs on Ventura (13) through Tahoe (26). Fires
        // on appear (tab == .install, no-op) and on every tab switch.
        .task(id: tab) {
            if tab == .mods { await service.listMods() }
            if tab == .log { await service.showLog() }
        }
        .confirmationDialog(
            confirm?.title ?? "",
            isPresented: Binding(get: { confirm != nil }, set: { if !$0 { confirm = nil } }),
            titleVisibility: .visible
        ) {
            if let confirm {
                Button(confirm.button, role: .destructive) {
                    let run = confirm.run
                    Task { await run() }
                }
            }
            Button("Cancel", role: .cancel) {}
        } message: { Text(confirm?.message ?? "") }
    }

    private var statusBar: some View {
        HStack(spacing: 6) {
            if service.busy { ProgressView().controlSize(.small) }
            Text(statusMessage)
                .font(.caption)
                .foregroundStyle(service.error != nil ? .red : .secondary)
                .lineLimit(1)
                .truncationMode(.middle)
            Spacer()
        }
        .padding(.horizontal, 12)
        .frame(height: 26)
        .frame(maxWidth: .infinity)
        .background(.bar)
        .overlay(Divider(), alignment: .top)
    }

    private var statusMessage: String {
        if service.busy { return "Working…" }
        if let error = service.error, !error.isEmpty { return error }
        if let last = service.log.last { return last.message }
        return service.detected ? "Ready." : "Game not detected."
    }

    private var busyOverlay: some View {
        ZStack {
            Color.black.opacity(0.12)
            ProgressView().controlSize(.large).padding(22)
                .background(.regularMaterial, in: RoundedRectangle(cornerRadius: 12))
        }
    }
}

// MARK: - Install tab

private struct InstallTab: View {
    @ObservedObject var service: InstallerService
    @ObservedObject var updater: UpdateService
    @Binding var confirm: ConfirmAction?
    @State private var recommended: [RecommendedMod] = []
    @State private var selected: Set<String> = []

    private var hasBackup: Bool { service.status?.hasBackup == true }
    private var installed: Bool { service.status?.hookInstalled == true }
    // Only report an in-game version when UMM is actually hooked/active; the
    // UnityModManager/ files can linger after an Uninstall (which only removes the hook).
    private var ingameVersion: String {
        service.status?.hookInstalled == true ? (service.status?.managerVersion ?? "-") : "-"
    }

    var body: some View {
        ScrollView {
            VStack(spacing: 12) {
                if updater.available != nil { updateBanner }

                Button { Task { await service.install(force: false) } } label: {
                    Text(installed ? "Reinstall / Repair" : "Install")
                        .font(.title3).bold()
                        .frame(maxWidth: .infinity, minHeight: 36)
                }
                .buttonStyle(.borderedProminent)
                .disabled(!service.detected)

                Button { askUninstall() } label: {
                    Text("Uninstall").frame(maxWidth: .infinity, minHeight: 24)
                }
                .disabled(!installed)

                Button { askRestore() } label: {
                    Text("Restore original files").frame(maxWidth: .infinity, minHeight: 24)
                }
                .disabled(!hasBackup)

                folderVersionRow
                recommendedBox
            }
            .padding(.vertical, 2)
        }
        .onAppear {
            if recommended.isEmpty {
                recommended = RecommendedMods.load()
                selected = Set(recommended.map(\.id)) // default: all ticked
            }
        }
    }

    private var folderVersionRow: some View {
        HStack(alignment: .top, spacing: 12) {
            gameFolderBox
                .frame(maxWidth: .infinity, alignment: .leading)
                .layoutPriority(1)

            versionHomeRow
                .frame(width: 168, alignment: .top)
        }
        .frame(maxWidth: .infinity, alignment: .leading)
    }

    private var versionHomeRow: some View {
        VStack(alignment: .leading, spacing: 8) {
            VStack(alignment: .leading, spacing: 2) {
                Text("App Version: \(updater.currentVersion)")
                Text("UMM Version: \(service.bundledVersion ?? "—")")
                Text("Ingame Version: \(ingameVersion)")
            }
            .font(.caption)
            .foregroundStyle(.secondary)
            .lineLimit(1)
            .minimumScaleFactor(0.9)
            .frame(maxWidth: .infinity, alignment: .leading)

            Button { openHomePage() } label: {
                Text("Home Page")
                    .frame(maxWidth: .infinity)
                    .foregroundStyle(.white)
            }
            .buttonStyle(.borderedProminent)
            .tint(.green)
        }
    }

    /// Shows the detected/selected game location and lets the user override it.
    private var gameFolderBox: some View {
        GroupBox("Game folder") {
            VStack(alignment: .leading, spacing: 6) {
                HStack(spacing: 6) {
                    Image(systemName: service.detected ? "checkmark.circle.fill" : "exclamationmark.triangle.fill")
                        .foregroundStyle(service.detected ? .green : .orange)
                    Text(folderStatus)
                        .font(.caption).foregroundStyle(.secondary)
                    Spacer()
                }
                Text(gamePathDisplay)
                    .font(.system(.caption2, design: .monospaced))
                    .foregroundStyle(.secondary)
                    .lineLimit(1).truncationMode(.middle)
                    .textSelection(.enabled)
                    .frame(maxWidth: .infinity, alignment: .leading)
                HStack {
                    Button("Choose…") { chooseGameFolder() }
                    Button("Auto-detect") { Task { await service.setGamePath(nil) } }
                        .disabled(service.gamePath == nil)
                    Spacer()
                }
            }
            .frame(maxWidth: .infinity, alignment: .leading)
        }
    }

    private var folderStatus: String {
        if !service.detected { return "Not detected — choose the game .app below" }
        return service.gamePath != nil ? "Set manually" : "Auto-detected"
    }

    private var gamePathDisplay: String {
        service.gamePath ?? service.status?.appPath ?? service.status?.gameRoot ?? "—"
    }

    private func chooseGameFolder() {
        let panel = NSOpenPanel()
        panel.canChooseFiles = true            // the game .app (a package)
        panel.canChooseDirectories = true      // …or its containing folder
        panel.allowsMultipleSelection = false
        panel.treatsFilePackagesAsDirectories = false // select the .app as one item
        panel.allowedContentTypes = [.application]
        panel.prompt = "Select"
        panel.message = "Select 'A Dance of Fire and Ice.app' or the folder that contains it."
        if panel.runModal() == .OK, let url = panel.url {
            Task { await service.setGamePath(url.path) }
        }
    }

    /// Shown only when GitHub has a release newer than this app.
    private var updateBanner: some View {
        let upd = updater.available
        return HStack(spacing: 10) {
            Image(systemName: "arrow.down.circle.fill")
                .foregroundStyle(.tint)
            VStack(alignment: .leading, spacing: 1) {
                Text("Update available: \(upd?.tag ?? "")").bold()
                switch updater.phase {
                case .downloading:
                    Text("Downloading…").font(.caption).foregroundStyle(.secondary)
                case .installing:
                    Text("Installing — the app will relaunch.").font(.caption).foregroundStyle(.secondary)
                case .failed(let why):
                    Text(why).font(.caption).foregroundStyle(.red).lineLimit(2)
                case .idle:
                    Text("Version \(upd?.version ?? "") is newer than \(updater.currentVersion).")
                        .font(.caption).foregroundStyle(.secondary)
                }
            }
            Spacer()
            switch updater.phase {
            case .downloading, .installing:
                ProgressView().controlSize(.small)
            case .failed:
                Button("Open Page") { updater.openReleasePage() }
                    .buttonStyle(.bordered).controlSize(.small)
            case .idle:
                Button("Update Now") { Task { await updater.update() } }
                    .buttonStyle(.borderedProminent).controlSize(.small)
            }
        }
        .padding(10)
        .background(.tint.opacity(0.12), in: RoundedRectangle(cornerRadius: 8))
    }

    private var recommendedBox: some View {
        GroupBox("Recommended mods") {
            VStack(alignment: .leading, spacing: 6) {
                if recommended.isEmpty {
                    Text("None listed.").font(.caption).foregroundStyle(.secondary)
                        .frame(maxWidth: .infinity, alignment: .leading)
                } else {
                    ForEach(recommended) { mod in
                        let installed = isInstalled(mod)
                        Toggle(isOn: toggle(mod.id, installed: installed)) {
                            VStack(alignment: .leading, spacing: 1) {
                                Text(installed ? "\(mod.name)  (Installed)" : mod.name)
                                    .foregroundStyle(installed ? .secondary : .primary)
                                if let desc = mod.description, !desc.isEmpty {
                                    Text(desc).font(.caption2).foregroundStyle(.secondary)
                                }
                            }
                        }
                        .disabled(installed)
                    }
                }
                Button { installSelected() } label: {
                    Text("Install Mods").frame(maxWidth: .infinity, minHeight: 24)
                }
                .buttonStyle(.bordered)
                .disabled(!service.detected || installableSelected.isEmpty)
                .padding(.top, 2)
            }
            .frame(maxWidth: .infinity, alignment: .leading)
        }
    }

    private func toggle(_ id: String, installed: Bool) -> Binding<Bool> {
        Binding(get: { !installed && selected.contains(id) },
                set: { on in if on { selected.insert(id) } else { selected.remove(id) } })
    }

    /// A recommended mod counts as installed if an installed mod shares its id or name.
    private func isInstalled(_ rec: RecommendedMod) -> Bool {
        service.mods.contains { m in
            m.isInstalled &&
            (m.modId.caseInsensitiveCompare(rec.id) == .orderedSame ||
             m.name.caseInsensitiveCompare(rec.name) == .orderedSame)
        }
    }

    private var installableSelected: [RecommendedMod] {
        recommended.filter { selected.contains($0.id) && !isInstalled($0) }
    }

    private func installSelected() {
        let urls = installableSelected.map(\.url)
        guard !urls.isEmpty else { return }
        Task { await service.installMods(urls: urls) }
    }

    private func askUninstall() {
        confirm = ConfirmAction(
            title: "Uninstall UMM?",
            message: "Removes the UMM hook from the game assembly. Mods and UMM files stay in place.",
            button: "Uninstall") { await service.uninstall() }
    }

    private func askRestore() {
        confirm = ConfirmAction(
            title: "Restore original files?",
            message: "Restores UnityEngine.CoreModule.dll from the .original_ backup.",
            button: "Restore") { await service.restoreOriginal() }
    }

    private func openHomePage() {
        if let url = URL(string: "https://github.com/newman55/unity-mod-manager") {
            NSWorkspace.shared.open(url)
        }
    }
}

// MARK: - Mods tab

private struct ModsTab: View {
    @ObservedObject var service: InstallerService
    @State private var selection: UmmMod.ID?
    @State private var dropTargeted = false
    @State private var confirmRemove: UmmMod?

    var body: some View {
        VStack(spacing: 10) {
            Table(service.mods, selection: $selection) {
                TableColumn("Name") { mod in
                    Text(mod.name).foregroundStyle(mod.isInstalled ? .primary : .secondary)
                }
                TableColumn("Version") { mod in
                    Text(mod.version).foregroundStyle(mod.isInstalled ? .primary : .secondary)
                }.width(60)
                TableColumn("Manager") { mod in
                    Text(mod.managerVersion ?? "—").foregroundStyle(.secondary)
                }.width(70)
                TableColumn("Requirements") { mod in
                    RequirementsCell(mod: mod)
                }.width(min: 120, ideal: 160)
                TableColumn("Status") { mod in
                    Text(mod.status).foregroundStyle(statusColor(mod))
                }.width(90)
            }
            .frame(minHeight: 280)
            .overlay {
                if service.mods.isEmpty {
                    Text("No mods installed.").foregroundStyle(.secondary)
                }
            }
            .contextMenu(forSelectionType: UmmMod.ID.self) { ids in
                if let id = ids.first, let mod = service.mods.first(where: { $0.id == id }) {
                    Button("Open Folder") { openFolder(mod) }
                        .disabled(mod.path == nil)
                    if let home = mod.homePage, !home.isEmpty {
                        Button("Home Page") { open(home) }
                    }
                    Divider()
                    if mod.isInstalled {
                        Button("Uninstall") {
                            if let p = mod.path { Task { await service.uninstallMod(p) } }
                        }
                    } else {
                        Button("Install") {
                            if let p = mod.path { Task { await service.restoreMod(p) } }
                        }
                    }
                    Button("Remove", role: .destructive) { confirmRemove = mod }
                }
            }

            Button { chooseMod() } label: {
                Text("Install Mod").frame(maxWidth: .infinity, minHeight: 28)
            }
            .buttonStyle(.bordered)

            dropZone
        }
        .confirmationDialog(
            "Permanently remove \(confirmRemove?.name ?? "")?",
            isPresented: Binding(get: { confirmRemove != nil }, set: { if !$0 { confirmRemove = nil } }),
            titleVisibility: .visible
        ) {
            if let mod = confirmRemove, let p = mod.path {
                Button("Remove", role: .destructive) {
                    Task { await service.removeMod(p) }
                }
            }
            Button("Cancel", role: .cancel) {}
        } message: { Text("Deletes the mod files from disk — cannot be undone. Use Uninstall to keep a restorable copy.") }
    }

    private func statusColor(_ mod: UmmMod) -> Color {
        if !mod.isInstalled { return .secondary }
        return mod.status == "OK" ? .green : .orange
    }

    /// Comma-joined requirement ids, with unmet ones tagged and reddened the way
    /// the in-game manager marks them (Missing / Inactive / Outdated).
    private struct RequirementsCell: View {
        let mod: UmmMod

        var body: some View {
            if mod.requirementList.isEmpty {
                Text("—").foregroundStyle(.secondary)
            } else {
                Text(styled)
                    .lineLimit(1)
                    .help(mod.requirementList.map(\.label).joined(separator: ", "))
            }
        }

        private var styled: AttributedString {
            var line = AttributedString()
            for (index, req) in mod.requirementList.enumerated() {
                if index > 0 { line += AttributedString(", ") }
                var piece = AttributedString(req.label)
                if !req.isSatisfied { piece.foregroundColor = .red }
                line += piece
            }
            return line
        }
    }

    private var dropZone: some View {
        RoundedRectangle(cornerRadius: 8)
            .strokeBorder(style: StrokeStyle(lineWidth: 2, dash: [6]))
            .foregroundStyle(dropTargeted ? Color.accentColor : .secondary.opacity(0.5))
            .frame(height: 90)
            .overlay {
                Text("Drop zip files here")
                    .font(.title3).bold()
                    .foregroundStyle(dropTargeted ? Color.accentColor : .secondary)
            }
            .onDrop(of: [.fileURL], isTargeted: $dropTargeted) { providers in
                handleDrop(providers)
            }
    }

    private func handleDrop(_ providers: [NSItemProvider]) -> Bool {
        var handled = false
        for provider in providers where provider.canLoadObject(ofClass: URL.self) {
            handled = true
            _ = provider.loadObject(ofClass: URL.self) { url, _ in
                guard let url, url.pathExtension.lowercased() == "zip" else { return }
                Task { @MainActor in await service.installMod(url.path) }
            }
        }
        return handled
    }

    private func openFolder(_ mod: UmmMod) {
        guard let path = mod.path else { return }
        NSWorkspace.shared.activateFileViewerSelecting([URL(fileURLWithPath: path)])
    }

    private func open(_ urlString: String) {
        let normalized = urlString.contains("://") ? urlString : "https://\(urlString)"
        if let url = URL(string: normalized) { NSWorkspace.shared.open(url) }
    }

    private func chooseMod() {
        let panel = NSOpenPanel()
        panel.allowedContentTypes = [.zip]
        panel.allowsMultipleSelection = false
        panel.prompt = "Install"
        if panel.runModal() == .OK, let url = panel.url {
            Task { await service.installMod(url.path) }
        }
    }
}

// MARK: - Log tab

private struct LogTab: View {
    @ObservedObject var service: InstallerService

    var body: some View {
        VStack(spacing: 8) {
            HStack {
                Spacer()
                Button { Task { await service.showLog() } } label: {
                    Label("Refresh", systemImage: "arrow.clockwise")
                }
                .controlSize(.small)
            }
            ScrollView {
                Text(service.logText ?? "No log.")
                    .font(.system(.caption, design: .monospaced))
                    .textSelection(.enabled)
                    .frame(maxWidth: .infinity, alignment: .leading)
            }
            .background(.black.opacity(0.04), in: RoundedRectangle(cornerRadius: 8))
        }
    }
}

// MARK: - Settings tab

private struct SettingsTab: View {
    @ObservedObject var updater: UpdateService
    @AppStorage(SettingsKey.autoUpdate) private var autoUpdate = true
    @AppStorage(SettingsKey.frequency) private var frequencyRaw = UpdateFrequency.onLaunch.rawValue
    @State private var launchAtLogin = LoginItem.isEnabled
    @State private var loginError: String?

    var body: some View {
        Form {
            Section("Updates") {
                Toggle("Check for updates automatically", isOn: $autoUpdate)

                Picker("Check", selection: $frequencyRaw) {
                    ForEach(UpdateFrequency.allCases) { freq in
                        Text(freq.label).tag(freq.rawValue)
                    }
                }
                .disabled(!autoUpdate)

                HStack(spacing: 8) {
                    Button("Check Now") { Task { await updater.check() } }
                        .disabled(updater.checking)
                    if updater.checking { ProgressView().controlSize(.small) }
                    Spacer()
                    Text(checkStatus).font(.caption).foregroundStyle(.secondary)
                }
            }

            Section("General") {
                Toggle("Launch at login", isOn: $launchAtLogin)
                if let loginError {
                    Text(loginError).font(.caption).foregroundStyle(.red)
                }
            }

            Section {
                LabeledContent("App Version", value: updater.currentVersion)
            }
        }
        .formStyle(.grouped)
        // .task(id:) (macOS 12+) instead of the macOS-14-only two-param onChange,
        // matching the rest of the app. Fires on appear (no-op: guard matches the
        // live status) and whenever the toggle flips.
        .task(id: launchAtLogin) { applyLoginItem(launchAtLogin) }
    }

    private var checkStatus: String {
        if updater.checking { return "Checking…" }
        if updater.available != nil { return "Update available" }
        if let date = updater.lastCheckedAt {
            return "Up to date · checked \(date.formatted(.relative(presentation: .named)))"
        }
        return ""
    }

    private func applyLoginItem(_ on: Bool) {
        guard on != LoginItem.isEnabled else { return } // skip the on-appear no-op
        do {
            try LoginItem.set(on)
            loginError = nil
        } catch {
            loginError = "Couldn't \(on ? "enable" : "disable") launch at login: \(error.localizedDescription)"
            launchAtLogin = LoginItem.isEnabled // snap the toggle back to reality
        }
    }
}

// MARK: - Shared

private struct ConfirmAction: Identifiable {
    let id = UUID()
    let title: String
    let message: String
    let button: String
    let run: () async -> Void
}

#Preview {
    ContentView()
}
