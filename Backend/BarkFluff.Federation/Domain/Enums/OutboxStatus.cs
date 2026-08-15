namespace BarkFluff.Federation.Domain.Enums;

// Статус записи в FederationOutbox. Pending → Processing (claim инстанса) → Delivered/DeadLetter
// либо обратно в Pending (retry/reclaim после крэша инстанса по истечении lease).
// Хранится как int в БД (не HasConversion<string>, как KnownServerStatus — компактнее и
// джойны по индексу (Status, NextAttemptAt) дешевле).
public enum OutboxStatus
{
    Pending = 0,
    Delivered = 1,
    DeadLetter = 2,
    Processing = 3,
}
