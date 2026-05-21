using System.Collections.Concurrent;

using Barkfluff.AdminPanel.Models;
using Barkfluff.AdminPanel.Models.Dtos;

using MailKit;
using MailKit.Net.Imap;
using MailKit.Net.Smtp;
using MailKit.Search;
using MailKit.Security;

using Microsoft.Extensions.Options;

using MimeKit;

namespace Barkfluff.AdminPanel.Services;

public class MailService : IAsyncDisposable
{
    private readonly MailSettings _settings;
    private readonly ILogger<MailService> _logger;
    private readonly ConcurrentDictionary<string, AccountState> _states = new(StringComparer.OrdinalIgnoreCase);

    public MailService(IOptions<MailSettings> settings, ILogger<MailService> logger)
    {
        _settings = settings.Value;
        _logger = logger;
    }

    public IReadOnlyList<MailAccountDto> GetAccounts()
    {
        return _settings.Accounts
            .Where(a => !string.IsNullOrWhiteSpace(a.Address))
            .Select(a => new MailAccountDto(a.Address, a.DisplayName))
            .ToList();
    }

    public async Task<MailMessageListResult> ListMessagesAsync(
        string address, string folder, int page, int size, CancellationToken ct)
    {
        var account = GetAccountOrThrow(address);
        var state = GetOrCreateState(account);

        await state.Lock.WaitAsync(ct);
        try
        {
            await EnsureConnectedAsync(state, ct);

            var f = string.Equals(folder, "INBOX", StringComparison.OrdinalIgnoreCase)
                ? state.Client!.Inbox
                : await state.Client!.GetFolderAsync(folder, ct);

            if (!f.IsOpen)
                await f.OpenAsync(FolderAccess.ReadWrite, ct);

            var allUids = await f.SearchAsync(SearchQuery.All, ct);
            var ordered = allUids.OrderByDescending(u => u.Id).ToList();
            var total = ordered.Count;

            var safePage = Math.Max(0, page);
            var safeSize = Math.Clamp(size, 1, 200);
            var skip = safePage * safeSize;
            var pageUids = ordered.Skip(skip).Take(safeSize).ToList();
            if (pageUids.Count == 0)
                return new MailMessageListResult(Array.Empty<MailMessageDto>(), total, safePage, safeSize);

            var summaries = await f.FetchAsync(pageUids,
                MessageSummaryItems.UniqueId
                | MessageSummaryItems.Envelope
                | MessageSummaryItems.Flags
                | MessageSummaryItems.BodyStructure
                | MessageSummaryItems.PreviewText,
                ct);

            var items = summaries
                .Select(MapSummary)
                .OrderByDescending(m => m.Date)
                .ToList();

            return new MailMessageListResult(items, total, safePage, safeSize);
        }
        finally
        {
            state.Lock.Release();
        }
    }

    public async Task<MailMessageDetailDto?> GetMessageAsync(
        string address, uint uid, CancellationToken ct)
    {
        var account = GetAccountOrThrow(address);
        var state = GetOrCreateState(account);

        await state.Lock.WaitAsync(ct);
        try
        {
            await EnsureConnectedAsync(state, ct);

            var inbox = state.Client!.Inbox;
            if (!inbox.IsOpen)
                await inbox.OpenAsync(FolderAccess.ReadWrite, ct);

            var uniqueId = new UniqueId(uid);
            var message = await inbox.GetMessageAsync(uniqueId, ct);
            if (message == null)
                return null;

            await inbox.AddFlagsAsync(uniqueId, MessageFlags.Seen, true, ct);

            var attachments = message.Attachments
                .OfType<MimePart>()
                .Select((p, i) => new MailAttachmentDto(
                    i,
                    string.IsNullOrEmpty(p.FileName) ? $"attachment_{i}" : p.FileName,
                    p.ContentType?.MimeType ?? "application/octet-stream",
                    GetAttachmentSize(p)))
                .ToList();

            var fromMailbox = message.From.Mailboxes.FirstOrDefault();
            return new MailMessageDetailDto(
                uid,
                fromMailbox != null ? new MailAddressDto(fromMailbox.Name, fromMailbox.Address) : null,
                message.To.Mailboxes.Select(m => new MailAddressDto(m.Name, m.Address)).ToList(),
                message.Cc.Mailboxes.Select(m => new MailAddressDto(m.Name, m.Address)).ToList(),
                message.Subject ?? string.Empty,
                message.Date,
                message.MessageId,
                message.InReplyTo,
                message.References?.ToList() ?? new List<string>(),
                message.HtmlBody,
                message.TextBody,
                attachments);
        }
        finally
        {
            state.Lock.Release();
        }
    }

    public async Task<(string FileName, string ContentType, byte[] Bytes)?> GetAttachmentAsync(
        string address, uint uid, int idx, CancellationToken ct)
    {
        var account = GetAccountOrThrow(address);
        var state = GetOrCreateState(account);

        await state.Lock.WaitAsync(ct);
        try
        {
            await EnsureConnectedAsync(state, ct);

            var inbox = state.Client!.Inbox;
            if (!inbox.IsOpen)
                await inbox.OpenAsync(FolderAccess.ReadWrite, ct);

            var uniqueId = new UniqueId(uid);
            var message = await inbox.GetMessageAsync(uniqueId, ct);
            if (message == null) return null;

            var attachments = message.Attachments.OfType<MimePart>().ToList();
            if (idx < 0 || idx >= attachments.Count) return null;

            var att = attachments[idx];
            using var ms = new MemoryStream();
            await att.Content.DecodeToAsync(ms, ct);
            return (
                string.IsNullOrEmpty(att.FileName) ? $"attachment_{idx}" : att.FileName,
                att.ContentType?.MimeType ?? "application/octet-stream",
                ms.ToArray());
        }
        finally
        {
            state.Lock.Release();
        }
    }

    public async Task MarkAsReadAsync(string address, uint uid, CancellationToken ct)
    {
        var account = GetAccountOrThrow(address);
        var state = GetOrCreateState(account);

        await state.Lock.WaitAsync(ct);
        try
        {
            await EnsureConnectedAsync(state, ct);
            var inbox = state.Client!.Inbox;
            if (!inbox.IsOpen)
                await inbox.OpenAsync(FolderAccess.ReadWrite, ct);
            await inbox.AddFlagsAsync(new UniqueId(uid), MessageFlags.Seen, true, ct);
        }
        finally
        {
            state.Lock.Release();
        }
    }

    public async Task SendAsync(
        string fromAddress,
        SendMailRequest req,
        IReadOnlyList<IFormFile> files,
        CancellationToken ct)
    {
        var account = GetAccountOrThrow(fromAddress);

        if (string.IsNullOrWhiteSpace(_settings.SmtpHost))
            throw new InvalidOperationException("SMTP host is not configured");

        if (req.To == null || req.To.Count(s => !string.IsNullOrWhiteSpace(s)) == 0)
            throw new ArgumentException("Не указан ни один получатель");

        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(account.DisplayName ?? account.Address, account.Address));

        foreach (var to in req.To.Where(s => !string.IsNullOrWhiteSpace(s)))
            message.To.Add(MailboxAddress.Parse(to.Trim()));
        if (req.Cc != null)
            foreach (var cc in req.Cc.Where(s => !string.IsNullOrWhiteSpace(s)))
                message.Cc.Add(MailboxAddress.Parse(cc.Trim()));

        message.Subject = req.Subject ?? string.Empty;

        if (!string.IsNullOrEmpty(req.InReplyTo))
            message.InReplyTo = req.InReplyTo;
        if (req.References != null)
            foreach (var r in req.References.Where(s => !string.IsNullOrWhiteSpace(s)))
                message.References.Add(r);

        var builder = new BodyBuilder();
        if (req.IsHtml)
            builder.HtmlBody = req.Body ?? string.Empty;
        else
            builder.TextBody = req.Body ?? string.Empty;

        if (files != null)
        {
            foreach (var f in files)
            {
                if (f == null || f.Length == 0) continue;
                using var stream = f.OpenReadStream();
                using var ms = new MemoryStream();
                await stream.CopyToAsync(ms, ct);
                var contentTypeStr = string.IsNullOrWhiteSpace(f.ContentType) ? "application/octet-stream" : f.ContentType;
                ContentType contentType;
                try { contentType = ContentType.Parse(contentTypeStr); }
                catch { contentType = ContentType.Parse("application/octet-stream"); }
                builder.Attachments.Add(f.FileName ?? "attachment", ms.ToArray(), contentType);
            }
        }

        message.Body = builder.ToMessageBody();

        using var smtp = new SmtpClient();
        if (_settings.AcceptInvalidCertificates)
            smtp.ServerCertificateValidationCallback = (s, c, h, e) => true;
        await smtp.ConnectAsync(_settings.SmtpHost, _settings.SmtpPort, GetSmtpSecurity(), ct);
        await smtp.AuthenticateAsync(account.Address, account.Password, ct);
        await smtp.SendAsync(message, ct);
        await smtp.DisconnectAsync(true, ct);
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var state in _states.Values)
        {
            await state.Lock.WaitAsync();
            try
            {
                if (state.Client != null)
                {
                    try
                    {
                        if (state.Client.IsConnected)
                            await state.Client.DisconnectAsync(true);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to disconnect IMAP client for {Address}", state.Address);
                    }
                    state.Client.Dispose();
                    state.Client = null;
                }
            }
            finally
            {
                state.Lock.Release();
            }
            state.Lock.Dispose();
        }
        _states.Clear();
        GC.SuppressFinalize(this);
    }

    // --- private helpers ---

    private MailAccountSettings GetAccountOrThrow(string address)
    {
        var acc = _settings.Accounts.FirstOrDefault(a =>
            string.Equals(a.Address, address, StringComparison.OrdinalIgnoreCase));
        if (acc == null)
            throw new InvalidOperationException($"Mail account '{address}' is not configured");
        return acc;
    }

    private AccountState GetOrCreateState(MailAccountSettings account)
        => _states.GetOrAdd(account.Address, _ => new AccountState(account));

    private async Task EnsureConnectedAsync(AccountState state, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_settings.ImapHost))
            throw new InvalidOperationException("IMAP host is not configured");

        if (state.Client == null || !state.Client.IsConnected)
        {
            state.Client?.Dispose();
            state.Client = new ImapClient();
            if (_settings.AcceptInvalidCertificates)
                state.Client.ServerCertificateValidationCallback = (s, c, h, e) => true;
            await state.Client.ConnectAsync(
                _settings.ImapHost,
                _settings.ImapPort,
                ParseSecurity(_settings.ImapSecurity, SecureSocketOptions.SslOnConnect),
                ct);
        }

        if (!state.Client.IsAuthenticated)
        {
            await state.Client.AuthenticateAsync(state.Address, state.Password, ct);
        }
    }

    private SecureSocketOptions GetSmtpSecurity()
    {
        if (!string.IsNullOrWhiteSpace(_settings.SmtpSecurity) &&
            !string.Equals(_settings.SmtpSecurity, "Auto", StringComparison.OrdinalIgnoreCase))
        {
            return ParseSecurity(_settings.SmtpSecurity, SecureSocketOptions.Auto);
        }
        return _settings.SmtpPort switch
        {
            465 => SecureSocketOptions.SslOnConnect,
            587 => SecureSocketOptions.StartTls,
            25 => SecureSocketOptions.None,
            _ => SecureSocketOptions.Auto
        };
    }

    private static SecureSocketOptions ParseSecurity(string? value, SecureSocketOptions fallback) =>
        (value ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "sslonconnect" => SecureSocketOptions.SslOnConnect,
            "starttls" => SecureSocketOptions.StartTls,
            "starttlswhenavailable" => SecureSocketOptions.StartTlsWhenAvailable,
            "none" => SecureSocketOptions.None,
            "auto" => SecureSocketOptions.Auto,
            _ => fallback
        };

    private static MailMessageDto MapSummary(IMessageSummary s)
    {
        var env = s.Envelope;
        var fromMailbox = env?.From?.Mailboxes?.FirstOrDefault();
        var toList = env?.To?.Mailboxes?
            .Select(m => new MailAddressDto(m.Name, m.Address))
            .ToList() ?? new List<MailAddressDto>();

        var isRead = s.Flags?.HasFlag(MessageFlags.Seen) ?? false;
        var hasAttachments = s.Attachments != null && s.Attachments.Any();
        var preview = s.PreviewText ?? string.Empty;
        if (preview.Length > 150) preview = preview.Substring(0, 150);

        return new MailMessageDto(
            s.UniqueId.Id,
            fromMailbox != null ? new MailAddressDto(fromMailbox.Name, fromMailbox.Address) : null,
            toList,
            env?.Subject ?? string.Empty,
            env?.Date ?? DateTimeOffset.MinValue,
            isRead,
            hasAttachments,
            preview);
    }

    private static long GetAttachmentSize(MimePart part)
    {
        if (part.ContentDisposition?.Size is long sz && sz > 0) return sz;
        try
        {
            return part.Content?.Stream?.Length ?? 0;
        }
        catch
        {
            return 0;
        }
    }

    private sealed class AccountState
    {
        public string Address { get; }
        public string Password { get; }
        public SemaphoreSlim Lock { get; } = new(1, 1);
        public ImapClient? Client { get; set; }

        public AccountState(MailAccountSettings account)
        {
            Address = account.Address;
            Password = account.Password;
        }
    }
}
