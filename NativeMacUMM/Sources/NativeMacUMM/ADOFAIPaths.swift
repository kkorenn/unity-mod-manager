import Foundation

struct ADOFAIPaths {
    static let gameFolderName = "A Dance of Fire and Ice"
    static let steamGameID = "977950"

    let rootURL: URL

    var appBundleURL: URL {
        rootURL.appendingPathComponent("ADanceOfFireAndIce.app", isDirectory: true)
    }

    var appBinaryURL: URL {
        appBundleURL
            .appendingPathComponent("Contents", isDirectory: true)
            .appendingPathComponent("MacOS", isDirectory: true)
            .appendingPathComponent("ADanceOfFireAndIce", isDirectory: false)
    }

    var appBinaryRealURL: URL {
        appBundleURL
            .appendingPathComponent("Contents", isDirectory: true)
            .appendingPathComponent("MacOS", isDirectory: true)
            .appendingPathComponent("ADanceOfFireAndIce.real", isDirectory: false)
    }

    var managedURL: URL {
        appBundleURL
            .appendingPathComponent("Contents", isDirectory: true)
            .appendingPathComponent("Resources", isDirectory: true)
            .appendingPathComponent("Data", isDirectory: true)
            .appendingPathComponent("Managed", isDirectory: true)
    }

    var unityModManagerURL: URL {
        managedURL.appendingPathComponent("UnityModManager", isDirectory: true)
    }

    var modsURL: URL {
        rootURL.appendingPathComponent("Mods", isDirectory: true)
    }

    static func defaultInstallURL() -> URL {
        URL(fileURLWithPath: NSHomeDirectory(), isDirectory: true)
            .appendingPathComponent("Library", isDirectory: true)
            .appendingPathComponent("Application Support", isDirectory: true)
            .appendingPathComponent("Steam", isDirectory: true)
            .appendingPathComponent("steamapps", isDirectory: true)
            .appendingPathComponent("common", isDirectory: true)
            .appendingPathComponent(gameFolderName, isDirectory: true)
    }

    func validated() throws -> ADOFAIPaths {
        let fm = FileManager.default

        var isDir: ObjCBool = false
        guard fm.fileExists(atPath: rootURL.path, isDirectory: &isDir), isDir.boolValue else {
            throw NativeUMMError.unsupportedGamePath(rootURL)
        }

        guard fm.fileExists(atPath: appBundleURL.path) else {
            throw NativeUMMError.unsupportedGamePath(rootURL)
        }

        guard fm.fileExists(atPath: managedURL.path) else {
            throw NativeUMMError.unsupportedGamePath(rootURL)
        }

        return self
    }
}
