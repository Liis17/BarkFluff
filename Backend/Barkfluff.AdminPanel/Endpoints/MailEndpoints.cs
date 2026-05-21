using System.Text.Json;

using Barkfluff.AdminPanel.Models;
using Barkfluff.AdminPanel.Models.Dtos;
using Barkfluff.AdminPanel.Services;

namespace Barkfluff.AdminPanel.Endpoints;

public static class MailEndpoints
{
    public static void MapMailEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/mail")
            .WithTags("Mail");

        // GET /api/mail/accounts
        group.MapGet("/accounts", (MailService mail, HttpContext context) =>
        {
            if (context.Items["AuthToken"] is not AuthToken)
                return Results.Unauthorized();

            return Results.Ok(mail.GetAccounts());
        })
        .WithName("ListMailAccounts");

        // GET /api/mail/{address}/messages?folder=INBOX&page=0&size=50
        group.MapGet("/{address}/messages", async (
            string address,
            string? folder,
            int? page,
            int? size,
            MailService mail,
            HttpContext context,
            CancellationToken ct) =>
        {
            if (context.Items["AuthToken"] is not AuthToken)
                return Results.Unauthorized();

            try
            {
                var result = await mail.ListMessagesAsync(
                    address,
                    string.IsNullOrWhiteSpace(folder) ? "INBOX" : folder,
                    page ?? 0,
                    size ?? 50,
                    ct);
                return Results.Ok(result);
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
            catch (Exception ex)
            {
                return Results.Problem(detail: ex.Message, statusCode: 500);
            }
        })
        .WithName("ListMailMessages");

        // GET /api/mail/{address}/messages/{uid}
        group.MapGet("/{address}/messages/{uid:long}", async (
            string address,
            long uid,
            MailService mail,
            HttpContext context,
            CancellationToken ct) =>
        {
            if (context.Items["AuthToken"] is not AuthToken)
                return Results.Unauthorized();

            try
            {
                var detail = await mail.GetMessageAsync(address, (uint)uid, ct);
                return detail == null
                    ? Results.NotFound()
                    : Results.Ok(detail);
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
            catch (Exception ex)
            {
                return Results.Problem(detail: ex.Message, statusCode: 500);
            }
        })
        .WithName("GetMailMessage");

        // GET /api/mail/{address}/messages/{uid}/attachments/{idx}
        group.MapGet("/{address}/messages/{uid:long}/attachments/{idx:int}", async (
            string address,
            long uid,
            int idx,
            MailService mail,
            HttpContext context,
            CancellationToken ct) =>
        {
            if (context.Items["AuthToken"] is not AuthToken)
                return Results.Unauthorized();

            try
            {
                var att = await mail.GetAttachmentAsync(address, (uint)uid, idx, ct);
                if (att == null) return Results.NotFound();
                return Results.File(att.Value.Bytes, att.Value.ContentType, att.Value.FileName);
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
            catch (Exception ex)
            {
                return Results.Problem(detail: ex.Message, statusCode: 500);
            }
        })
        .WithName("DownloadMailAttachment");

        // GET /api/mail/{address}/messages/{uid}/inline/{cid} — inline attachment по Content-ID (для <img src="cid:...">)
        group.MapGet("/{address}/messages/{uid:long}/inline/{cid}", async (
            string address,
            long uid,
            string cid,
            MailService mail,
            HttpContext context,
            CancellationToken ct) =>
        {
            if (context.Items["AuthToken"] is not AuthToken)
                return Results.Unauthorized();

            try
            {
                var att = await mail.GetInlineAttachmentAsync(address, (uint)uid, cid, ct);
                if (att == null) return Results.NotFound();
                return Results.File(att.Value.Bytes, att.Value.ContentType);
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
            catch (Exception ex)
            {
                return Results.Problem(detail: ex.Message, statusCode: 500);
            }
        })
        .WithName("DownloadMailInlineAttachment");

        // POST /api/mail/{address}/messages/{uid}/read
        group.MapPost("/{address}/messages/{uid:long}/read", async (
            string address,
            long uid,
            MailService mail,
            HttpContext context,
            CancellationToken ct) =>
        {
            if (context.Items["AuthToken"] is not AuthToken)
                return Results.Unauthorized();

            try
            {
                await mail.MarkAsReadAsync(address, (uint)uid, ct);
                return Results.Ok(new { ok = true });
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
            catch (Exception ex)
            {
                return Results.Problem(detail: ex.Message, statusCode: 500);
            }
        })
        .WithName("MarkMailMessageRead");

        // POST /api/mail/{address}/send (multipart/form-data: payload + files[])
        group.MapPost("/{address}/send", async (
            string address,
            HttpRequest request,
            MailService mail,
            HttpContext context,
            CancellationToken ct) =>
        {
            if (context.Items["AuthToken"] is not AuthToken)
                return Results.Unauthorized();

            if (!request.HasFormContentType)
                return Results.BadRequest(new { error = "Ожидается multipart/form-data" });

            var form = await request.ReadFormAsync(ct);
            var payload = form["payload"].ToString();
            if (string.IsNullOrWhiteSpace(payload))
                return Results.BadRequest(new { error = "Поле 'payload' пусто" });

            SendMailRequest? req;
            try
            {
                req = JsonSerializer.Deserialize<SendMailRequest>(payload, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
            }
            catch (JsonException ex)
            {
                return Results.BadRequest(new { error = $"Невалидный JSON: {ex.Message}" });
            }

            if (req == null)
                return Results.BadRequest(new { error = "Не удалось распарсить payload" });

            try
            {
                await mail.SendAsync(address, req, form.Files.ToList(), ct);
                return Results.Ok(new { ok = true });
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
            catch (Exception ex)
            {
                return Results.Problem(detail: ex.Message, statusCode: 500);
            }
        })
        .DisableAntiforgery()
        .WithName("SendMail");
    }
}
