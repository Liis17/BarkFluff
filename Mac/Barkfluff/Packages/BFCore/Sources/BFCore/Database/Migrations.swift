//
//  Migrations.swift
//  BFCore
//

import Foundation
import GRDB

enum Migrations {

    static var migrator: DatabaseMigrator {
        var migrator = DatabaseMigrator()

        migrator.registerMigration("v1") { db in
            try db.create(table: "cached_file") { t in
                t.column("file_id", .text).primaryKey()
                t.column("type", .text).notNull()
                t.column("size_bytes", .integer).notNull()
                t.column("mime_type", .text)
                t.column("original_name", .text)
                t.column("added_at", .integer).notNull()
            }
            try db.create(indexOn: "cached_file", columns: ["type"])

            try db.create(table: "cached_chat") { t in
                t.column("id", .text).primaryKey()
                t.column("title", .text).notNull()
                t.column("picture_url", .text)
                t.column("picture_file_id", .text)
                t.column("is_group", .integer).notNull()
                t.column("last_message_id", .integer)
                t.column("unread_count", .integer).notNull().defaults(to: 0)
                t.column("members_json", .blob)
                t.column("updated_at", .integer).notNull()
            }

            try db.create(table: "cached_message") { t in
                t.column("id", .integer).primaryKey()
                t.column("chat_id", .text).notNull()
                t.column("sender_id", .integer).notNull()
                t.column("sender_name", .text)
                t.column("text", .text).notNull().defaults(to: "")
                t.column("attachments_json", .blob)
                t.column("sent_at", .integer).notNull()
                t.column("read_by_json", .blob)
                t.column("is_system", .integer).notNull().defaults(to: 0)
            }
            try db.create(
                indexOn: "cached_message",
                columns: ["chat_id", "sent_at"]
            )
        }

        return migrator
    }
}
