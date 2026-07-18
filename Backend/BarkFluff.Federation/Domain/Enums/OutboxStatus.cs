namespace BarkFluff.Federation.Domain.Enums;

// Статус записи в FederationOutbox. Pending → Delivered/DeadLetter.
// Хранится как int в БД (не HasConversion<string>, как KnownServerStatus — компактнее и
// джойны по индексу (Status, NextAttemptAt) дешевле).
public enum OutboxStatus
{
    Pending = 0,
    Delivered = 1,
    DeadLetter = 2,
}
