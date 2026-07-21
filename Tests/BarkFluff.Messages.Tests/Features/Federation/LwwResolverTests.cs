using BarkFluff.Messages.Features.Federation;

using FluentAssertions;

using Xunit;

namespace BarkFluff.Messages.Tests.Features.Federation;

public class LwwResolverTests
{
    private static readonly DateTime Base = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void ShouldApplyMessageChange_NewerTimestamp_Applies()
    {
        var result = LwwResolver.ShouldApplyMessageChange(
            currentIsDeleted: false,
            currentLastChangeAt: Base,
            currentOriginServer: "a.test",
            currentEventId: Guid.NewGuid(),
            incomingOriginTs: Base.AddSeconds(1),
            incomingOriginServer: "a.test",
            incomingEventId: Guid.NewGuid());

        result.Should().BeTrue();
    }

    [Fact]
    public void ShouldApplyMessageChange_OlderTimestamp_Ignored()
    {
        var result = LwwResolver.ShouldApplyMessageChange(
            currentIsDeleted: false,
            currentLastChangeAt: Base,
            currentOriginServer: "a.test",
            currentEventId: Guid.NewGuid(),
            incomingOriginTs: Base.AddSeconds(-1),
            incomingOriginServer: "a.test",
            incomingEventId: Guid.NewGuid());

        result.Should().BeFalse();
    }

    [Fact]
    public void ShouldApplyMessageChange_EqualTimestamp_TieBreaksByEventId_BothOrders()
    {
        // docs/rearch/05: обе ноды должны сойтись на одном победителе независимо от порядка обработки.
        var eventA = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var eventB = Guid.Parse("00000000-0000-0000-0000-000000000002");

        // Нода 1: применила A первой, затем пришло B с той же меткой → B побеждает (больше по ordinal).
        var node1AppliesBAfterA = LwwResolver.ShouldApplyMessageChange(
            currentIsDeleted: false,
            currentLastChangeAt: Base,
            currentOriginServer: "a.test",
            currentEventId: eventA,
            incomingOriginTs: Base,
            incomingOriginServer: "a.test",
            incomingEventId: eventB);

        // Нода 2: применила B первой, затем пришло A с той же меткой → A НЕ должно победить B.
        var node2AppliesAAfterB = LwwResolver.ShouldApplyMessageChange(
            currentIsDeleted: false,
            currentLastChangeAt: Base,
            currentOriginServer: "a.test",
            currentEventId: eventB,
            incomingOriginTs: Base,
            incomingOriginServer: "a.test",
            incomingEventId: eventA);

        node1AppliesBAfterA.Should().BeTrue("B лексикографически больше A — обе ноды должны выбрать B");
        node2AppliesAAfterB.Should().BeFalse("A лексикографически меньше уже применённого B");
    }

    [Fact]
    public void ShouldApplyMessageChange_EqualTimestampDifferentServer_TieBreaksByServer()
    {
        var result = LwwResolver.ShouldApplyMessageChange(
            currentIsDeleted: false,
            currentLastChangeAt: Base,
            currentOriginServer: "a.test",
            currentEventId: Guid.Empty,
            incomingOriginTs: Base,
            incomingOriginServer: "z.test",
            incomingEventId: Guid.Empty);

        result.Should().BeTrue("\"z.test\" лексикографически больше \"a.test\"");
    }

    [Fact]
    public void ShouldApplyMessageChange_EditAfterDelete_TerminallyIgnored()
    {
        // Удаление терминально: даже более новая метка правки не воскрешает сообщение.
        var result = LwwResolver.ShouldApplyMessageChange(
            currentIsDeleted: true,
            currentLastChangeAt: Base,
            currentOriginServer: "a.test",
            currentEventId: Guid.NewGuid(),
            incomingOriginTs: Base.AddDays(1),
            incomingOriginServer: "a.test",
            incomingEventId: Guid.NewGuid());

        result.Should().BeFalse();
    }

    [Fact]
    public void ShouldApplyMessageChange_DeleteAfterEdit_NewerWins()
    {
        // Не терминальный случай: сообщение ещё не удалено — обычное сравнение меток.
        var result = LwwResolver.ShouldApplyMessageChange(
            currentIsDeleted: false,
            currentLastChangeAt: Base,
            currentOriginServer: "a.test",
            currentEventId: Guid.NewGuid(),
            incomingOriginTs: Base.AddSeconds(1),
            incomingOriginServer: "a.test",
            incomingEventId: Guid.NewGuid());

        result.Should().BeTrue();
    }

    [Fact]
    public void ShouldApplyMessageChange_StaleDeleteAfterNewerEdit_Ignored()
    {
        // Удаление со старой меткой, пришедшее после уже применённой более новой правки — устарело.
        var result = LwwResolver.ShouldApplyMessageChange(
            currentIsDeleted: false,
            currentLastChangeAt: Base.AddSeconds(10),
            currentOriginServer: "a.test",
            currentEventId: Guid.NewGuid(),
            incomingOriginTs: Base,
            incomingOriginServer: "a.test",
            incomingEventId: Guid.NewGuid());

        result.Should().BeFalse();
    }

    [Fact]
    public void ShouldApplyRead_NoExistingState_Applies()
    {
        LwwResolver.ShouldApplyRead(null, Base).Should().BeTrue();
    }

    [Fact]
    public void ShouldApplyRead_Newer_Applies()
    {
        LwwResolver.ShouldApplyRead(Base, Base.AddSeconds(1)).Should().BeTrue();
    }

    [Fact]
    public void ShouldApplyRead_OlderOrEqual_Ignored()
    {
        LwwResolver.ShouldApplyRead(Base, Base).Should().BeFalse();
        LwwResolver.ShouldApplyRead(Base, Base.AddSeconds(-1)).Should().BeFalse();
    }
}
