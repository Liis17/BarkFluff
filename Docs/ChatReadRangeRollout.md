# Chat read range rollout

`Messages:ChatReadRangeEnabled` gates the additive `MessagesApi.MarkChatAsReadUpTo` RPC and routes the existing exact `MarkAsRead` operation through the transactional read outbox. The setting is false when absent. When false, the new RPC returns `UNIMPLEMENTED`; the old exact RPC keeps its direct-publish behavior. `MessageOutboxDispatcher` drains both NewMessage and ReadBy rows regardless of the gate.

## Deployment order

1. Apply the prepared `20261024002540_AddReadByMessageOutbox` Messages migration through the normal database release process. This migration is additive: existing rows receive `EventKind=0` (NewMessage), the `EventId` unique index remains, and `MessageId` uniqueness applies only to kind 0. This implementation task prepared the migration but did not apply it.
2. Deploy the version containing both `MessageOutboxDispatcher` support for `EventKind=1` and the gated RPC to every Messages replica. Keep the gate absent or false during the rolling deployment. Verify that no old dispatcher replica remains before continuing.
3. Roll out Android clients. With the gate still off, they receive `UNIMPLEMENTED` and use the bounded legacy pagination path. Other errors do not trigger fallback.
4. Enable `Messages:ChatReadRangeEnabled=true` through the deployment configuration only after every Messages replica is running the new dispatcher. The setting is not added to checked-in runtime configuration by this change.
5. Monitor `message_outbox_pending_read_by`, `message_outbox_read_by_delivered`, `message_outbox_read_by_reclaimed`, `message_outbox_read_by_delivery_errors`, and `message_outbox_read_by_deadletter`, alongside the matching `new_message` metrics. Confirm NewMessage delivery remains healthy while read traffic is present.

## Rollback order

1. Set `Messages:ChatReadRangeEnabled=false` on all replicas. The dispatcher continues to drain already-created ReadBy rows while new range calls return `UNIMPLEMENTED`.
2. Wait for all `EventKind=1` pending/processing rows to drain and inspect the per-kind dead-letter and error metrics. Before binary rollback, inspect ReadBy rows grouped by status:

   ```sql
   SELECT "Status", count(*)
   FROM "MessageOutbox"
   WHERE "EventKind" = 1
   GROUP BY "Status";
   ```

   Pending (`Status=0`) and leased/processing (`Status=3`) rows must reach a terminal state. Resolve or explicitly disposition every dead letter (`Status=2`) before rollback. Delivered rows (`Status=1`) are terminal and can remain for normal retention; the older dispatcher does not claim them. An older binary does not understand pending ReadBy payloads.
3. Only after no pending/processing ReadBy rows remain and all read dead letters have a disposition may the Messages binary be rolled back. The added column/index are backward-compatible with the previous binary and can remain until a separately reviewed schema cleanup.

The Android fallback is deliberately limited to five pages per worker activation and stores its `(SentAt, Id)` cursor. Large unread histories on older servers therefore complete over multiple worker runs. A deleted cursor is reset to latest and retried; it does not mark the range complete.
