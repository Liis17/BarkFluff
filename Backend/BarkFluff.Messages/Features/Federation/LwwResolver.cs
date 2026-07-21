namespace BarkFluff.Messages.Features.Federation;

// LWW-разрешение конфликтов входящих fed-событий (docs/rearch/05-chat-replication.md,
// «Метка последнего изменения»; docs/rearch/phase-2/step-2.4-edit-delete-read-lww.md, Изменение 2).
public static class LwwResolver
{
    /// <summary>
    /// Применять ли входящую правку/удаление к сообщению. Удаление терминально: если сообщение уже
    /// удалено локально, любое последующее событие (правка или повторное удаление) игнорируется
    /// независимо от меток. Иначе — новее побеждает; при равенстве меток — лексикографический
    /// tie-break по (origin_server, event_id), детерминированный на обеих нодах.
    /// </summary>
    public static bool ShouldApplyMessageChange(
        bool currentIsDeleted,
        DateTime currentLastChangeAt,
        string currentOriginServer,
        Guid currentEventId,
        DateTime incomingOriginTs,
        string incomingOriginServer,
        Guid incomingEventId)
    {
        if (currentIsDeleted)
            return false;

        var tsCmp = incomingOriginTs.CompareTo(currentLastChangeAt);
        if (tsCmp != 0)
            return tsCmp > 0;

        var serverCmp = string.CompareOrdinal(incomingOriginServer, currentOriginServer);
        if (serverCmp != 0)
            return serverCmp > 0;

        return string.CompareOrdinal(incomingEventId.ToString(), currentEventId.ToString()) > 0;
    }

    /// <summary>
    /// Применять ли входящую отметку "прочитано". Read-события идемпотентны по природе (не критичны
    /// к tie-break) — монотонное правило "более старое не откатывает более новое" достаточно.
    /// </summary>
    public static bool ShouldApplyRead(DateTime? currentReadAt, DateTime incomingOriginTs)
        => currentReadAt is null || incomingOriginTs > currentReadAt.Value;
}
