import AppKit

final class ModsTableController: NSObject, NSTableViewDataSource, NSTableViewDelegate {
    enum Column: String {
        case name
        case version
        case id
        case folder
    }

    var mods: [ModRecord] = []

    func numberOfRows(in tableView: NSTableView) -> Int {
        mods.count
    }

    func tableView(_ tableView: NSTableView, viewFor tableColumn: NSTableColumn?, row: Int) -> NSView? {
        guard row >= 0 && row < mods.count else { return nil }
        let mod = mods[row]
        let column = Column(rawValue: tableColumn?.identifier.rawValue ?? "")

        let text: String
        switch column {
        case .name:
            text = mod.displayName
        case .version:
            text = mod.version
        case .id:
            text = mod.id
        case .folder:
            text = mod.folderName
        case .none:
            text = ""
        }

        let identifier = NSUserInterfaceItemIdentifier("Cell_\(tableColumn?.identifier.rawValue ?? "Default")")
        let cell = (tableView.makeView(withIdentifier: identifier, owner: nil) as? NSTableCellView) ?? NSTableCellView()

        cell.identifier = identifier
        if cell.textField == nil {
            let textField = NSTextField(labelWithString: "")
            textField.translatesAutoresizingMaskIntoConstraints = false
            textField.textColor = NSColor(calibratedWhite: 0.9, alpha: 1)
            textField.font = NSFont.systemFont(ofSize: 12)
            cell.addSubview(textField)
            cell.textField = textField

            NSLayoutConstraint.activate([
                textField.leadingAnchor.constraint(equalTo: cell.leadingAnchor, constant: 8),
                textField.trailingAnchor.constraint(equalTo: cell.trailingAnchor, constant: -8),
                textField.centerYAnchor.constraint(equalTo: cell.centerYAnchor),
            ])
        }

        cell.textField?.stringValue = text
        return cell
    }
}
