using BarkFluff.Federation.Services;

namespace BarkFluff.Federation.Tests.Infrastructure;

// По умолчанию всегда пропускает (квота не достигнута) — большинство тестов не о квоте.
// AlwaysReject переключает поведение для тестов самой квоты.
public class FakeChatCreatedQuotaLimiter : IChatCreatedQuotaLimiter
{
    public bool AlwaysReject { get; set; }

    public Task<bool> TryConsumeAsync(string origin) => Task.FromResult(!AlwaysReject);
}
