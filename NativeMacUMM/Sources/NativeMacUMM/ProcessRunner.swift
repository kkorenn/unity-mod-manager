import Foundation

struct ProcessRunner {
    @discardableResult
    static func run(_ launchPath: String, arguments: [String]) throws -> String {
        let process = Process()
        process.executableURL = URL(fileURLWithPath: launchPath)
        process.arguments = arguments

        let outPipe = Pipe()
        let errPipe = Pipe()
        process.standardOutput = outPipe
        process.standardError = errPipe

        try process.run()
        process.waitUntilExit()

        let outData = outPipe.fileHandleForReading.readDataToEndOfFile()
        let errData = errPipe.fileHandleForReading.readDataToEndOfFile()

        let out = String(data: outData, encoding: .utf8) ?? ""
        let err = String(data: errData, encoding: .utf8) ?? ""

        guard process.terminationStatus == 0 else {
            throw NativeUMMError.processFailed(
                "Process failed (\(process.terminationStatus)): \(launchPath) \(arguments.joined(separator: " "))\n\(err)"
            )
        }

        return out.trimmingCharacters(in: .whitespacesAndNewlines)
    }
}
