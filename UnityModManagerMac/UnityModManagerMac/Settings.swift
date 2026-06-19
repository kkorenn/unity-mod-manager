//
//  Settings.swift
//  UnityModManagerMac
//
//  User-facing preferences (Settings tab) and their persistence keys, plus the
//  macOS "launch at login" wrapper around SMAppService.
//

import Foundation
import ServiceManagement

/// UserDefaults keys shared by the Settings tab (@AppStorage) and UpdateService.
enum SettingsKey {
    static let autoUpdate = "autoUpdateEnabled"     // Bool, default true
    static let frequency = "updateCheckFrequency"   // UpdateFrequency.rawValue
    static let lastCheck = "lastUpdateCheck"         // Double, epoch seconds
}

/// How often the app auto-checks GitHub for a newer release.
enum UpdateFrequency: String, CaseIterable, Identifiable {
    case onLaunch, daily, weekly, manual

    var id: String { rawValue }

    var label: String {
        switch self {
        case .onLaunch: return "Every launch"
        case .daily:    return "Once a day"
        case .weekly:   return "Once a week"
        case .manual:   return "Only when I click"
        }
    }

    /// Minimum seconds between auto-checks; nil means never auto-check.
    var interval: TimeInterval? {
        switch self {
        case .onLaunch: return 0
        case .daily:    return 86_400
        case .weekly:   return 604_800
        case .manual:   return nil
        }
    }
}

/// Registers/unregisters the app as a macOS login item via ServiceManagement.
enum LoginItem {
    static var isEnabled: Bool {
        SMAppService.mainApp.status == .enabled
    }

    static func set(_ on: Bool) throws {
        if on {
            if SMAppService.mainApp.status != .enabled { try SMAppService.mainApp.register() }
        } else {
            if SMAppService.mainApp.status == .enabled { try SMAppService.mainApp.unregister() }
        }
    }
}
