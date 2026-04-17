import Foundation

struct ModRecord {
    let id: String
    let displayName: String
    let version: String
    let folderName: String
}

struct InstallReport {
    var copiedPaths: [String] = []
    var backupPaths: [String] = []
    var notes: [String] = []
}

enum NativeUMMError: LocalizedError {
    case unsupportedGamePath(URL)
    case missingPayload(String)
    case missingFile(URL)
    case processFailed(String)
    case invalidZip(URL)

    var errorDescription: String? {
        switch self {
        case .unsupportedGamePath(let url):
            return "Unsupported game path: \(url.path). This app currently supports only A Dance of Fire and Ice."
        case .missingPayload(let file):
            return "Missing bundled payload file: \(file). Run scripts/prepare_payload_from_game.sh first, then rebuild the app."
        case .missingFile(let url):
            return "Required file not found: \(url.path)"
        case .processFailed(let message):
            return message
        case .invalidZip(let url):
            return "Invalid mod zip: \(url.lastPathComponent)"
        }
    }
}
