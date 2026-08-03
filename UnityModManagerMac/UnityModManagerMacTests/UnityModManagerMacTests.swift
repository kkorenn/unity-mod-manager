//
//  UnityModManagerMacTests.swift
//  UnityModManagerMacTests
//
//  Created by Koren on 5/29/26.
//

import Foundation
import Testing
@testable import UnityModManagerMac

struct UnityModManagerMacTests {

    /// The mods payload the NativeUmm core writes (see Exports.cs), including the
    /// resolved requirement states.
    private static let modsJSON = """
    {
      "ok": true,
      "detected": true,
      "mods": [
        {
          "id": "ModA", "name": "Mod A", "version": "1.0.0", "path": "/Mods/ModA",
          "status": "OK", "installed": true,
          "requirements": [
            { "id": "ModB", "version": "1.2.0", "state": "Outdated" },
            { "id": "ModC", "state": "Inactive" },
            { "id": "ModD", "state": "Missing" },
            { "id": "ModE", "version": "1.0.0", "state": "OK" }
          ]
        },
        {
          "id": "ModB", "name": "Mod B", "version": "1.0.0", "path": "/Mods/ModB",
          "status": "OK", "installed": true
        }
      ]
    }
    """

    private func decodeMods() throws -> [UmmMod] {
        let response = try JSONDecoder().decode(UmmResponse.self, from: Data(Self.modsJSON.utf8))
        return try #require(response.mods)
    }

    @Test func decodesResolvedRequirementStates() throws {
        let mods = try decodeMods()
        let modA = try #require(mods.first { $0.modId == "ModA" })

        #expect(modA.requirementList.map(\.reqId) == ["ModB", "ModC", "ModD", "ModE"])
        #expect(modA.requirementList.map(\.state) == ["Outdated", "Inactive", "Missing", "OK"])
        #expect(modA.requirementList.map(\.version) == ["1.2.0", nil, nil, "1.0.0"])
    }

    /// Only unsatisfied requirements carry a tag, matching the in-game manager.
    @Test func labelsTagOnlyUnsatisfiedRequirements() throws {
        let mods = try decodeMods()
        let modA = try #require(mods.first { $0.modId == "ModA" })

        #expect(modA.requirementList.map(\.label) ==
                ["ModB (Outdated)", "ModC (Inactive)", "ModD (Missing)", "ModE"])
        #expect(modA.requirementList.map(\.isSatisfied) == [false, false, false, true])
        #expect(modA.hasUnmetRequirements)
    }

    /// A mod whose Info.json has no Requirements block omits the key entirely.
    @Test func missingRequirementsKeyDecodesAsEmpty() throws {
        let mods = try decodeMods()
        let modB = try #require(mods.first { $0.modId == "ModB" })

        #expect(modB.requirementList.isEmpty)
        #expect(!modB.hasUnmetRequirements)
    }
}
