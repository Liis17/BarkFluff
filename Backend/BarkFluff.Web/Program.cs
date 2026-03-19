using BarkFluff.GrpcServer;
using BarkFluff.GrpcServer.Metrics;
using BarkFluff.Proto.Files;
using BarkFluff.Proto.Identity;
using BarkFluff.Proto.Messages;
using BarkFluff.Proto.Onliner;
using BarkFluff.Proto.Shared;
using BarkFluff.Proto.Updates;
using BarkFluff.Proto.Users;
using BarkFluff.Shared.Identity;

using Grpc.Core;

using Microsoft.AspNetCore.Server.Kestrel.Core;

using Serilog;

using System.Text;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);

builder.LoadConfiguration(ServiceId.Web);
builder.AddBarkFluffSerilog("BarkFluff.Web");

var port = int.TryParse(builder.Configuration["RunSettings:Port"], out var p) ? p : 7016;
builder.WebHost.ConfigureKestrel(options =>
{
    options.ListenAnyIP(port, listenOptions =>
    {
        listenOptions.Protocols = HttpProtocols.Http1AndHttp2;
    });
});

builder.Services.AddBarkFluffMetrics("BarkFluff.Web");

// --- gRPC-клиенты ---

builder.Services.AddGrpcClient<IdentityApi.IdentityApiClient>(o =>
{
    o.Address = new Uri(builder.Configuration["IdentityService:Host"] ?? "http://identity:7000");
});

builder.Services.AddGrpcClient<MessagesApi.MessagesApiClient>(o =>
{
    o.Address = new Uri(builder.Configuration["MessagesService:Host"] ?? "http://messages:7007");
});

builder.Services.AddGrpcClient<UsersApi.UsersApiClient>(o =>
{
    o.Address = new Uri(builder.Configuration["UsersService:Host"] ?? "http://users:7001");
});

builder.Services.AddGrpcClient<FilesApi.FilesApiClient>(o =>
{
    o.Address = new Uri(builder.Configuration["FilesService:Host"] ?? "http://files:7005");
});

builder.Services.AddGrpcClient<UpdatesApi.UpdatesApiClient>(o =>
{
    o.Address = new Uri(builder.Configuration["UpdatesService:Host"] ?? "http://updates:7015");
});

builder.Services.AddGrpcClient<OnlinerApi.OnlinerApiClient>(o =>
{
    o.Address = new Uri(builder.Configuration["OnlinerService:Host"] ?? "http://onliner:7009");
});

var app = builder.Build();

var JsonOpts = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

// Middleware для логирования и метрик HTTP-запросов
app.Use(async (ctx, next) =>
{
    var metrics = ctx.RequestServices.GetRequiredService<MetricsCollector>();
    metrics.Increment("http_requests_total");

    var logger = ctx.RequestServices.GetRequiredService<ILogger<Program>>();
    var ip = ctx.Request.Headers["X-Forwarded-For"].FirstOrDefault()
             ?? ctx.Request.Headers["X-Real-IP"].FirstOrDefault()
             ?? ctx.Connection.RemoteIpAddress?.ToString()
             ?? "unknown";

    logger.LogDebug("HTTP {Method} {Path} от {IP}", ctx.Request.Method, ctx.Request.Path, ip);

    try
    {
        await next();
    }
    catch (Exception ex)
    {
        metrics.Increment("http_requests_errors");
        logger.LogError(ex, "Ошибка при обработке {Method} {Path}", ctx.Request.Method, ctx.Request.Path);
        throw;
    }
});

app.UseStaticFiles();

// ========== AUTH ENDPOINTS ==========

// POST /api/auth/login — проксирует авторизацию в Identity gRPC
app.MapPost("/api/auth/login", async (HttpContext httpCtx, IdentityApi.IdentityApiClient identityClient, MetricsCollector metrics) =>
{
    metrics.Increment("auth_login_attempts");

    var body = await httpCtx.Request.ReadFromJsonAsync<LoginRequest>();
    if (body == null || string.IsNullOrWhiteSpace(body.Login) || string.IsNullOrWhiteSpace(body.Password))
        return Results.BadRequest(new { error = "missing_fields" });

    var authRequest = new AuthRequest { Password = body.Password };

    if (body.Login.Contains('@'))
        authRequest.Email = body.Login;
    else
        authRequest.Username = body.Login;

    if (!string.IsNullOrEmpty(body.OtpCode))
        authRequest.OtpCode = body.OtpCode;

    var metadata = BuildMetadata(httpCtx, body.DeviceId, body.BrowserName, body.OsName);

    try
    {
        var response = await identityClient.AuthAsync(authRequest, new CallOptions(headers: metadata));

        metrics.Increment("auth_login_success");

        return Results.Ok(new TokenResponse
        {
            AccessToken = response.AccessToken.Value,
            AccessTokenExpiration = response.AccessToken.ExpirationDate.ToDateTimeOffset().ToUnixTimeMilliseconds(),
            RefreshToken = response.RefreshToken.Value,
            RefreshTokenExpiration = response.RefreshToken.ExpirationDate.ToDateTimeOffset().ToUnixTimeMilliseconds()
        });
    }
    catch (RpcException ex)
    {
        var logger = httpCtx.RequestServices.GetRequiredService<ILogger<Program>>();
        var errorCode = ex.Trailers.Get("x-error-code")?.Value;

        logger.LogWarning("gRPC Auth ошибка: StatusCode={StatusCode}, ErrorCode={ErrorCode}, Detail={Detail}",
            ex.StatusCode, errorCode ?? "none", ex.Status.Detail);

        if (errorCode == "C1576884-12D8-4722-A7EE-9F9789AD1265")
        {
            metrics.Increment("auth_otp_required");
            return Results.Json(new { error = "otp_required" }, statusCode: 200);
        }

        metrics.Increment("auth_login_failed");

        return errorCode switch
        {
            "803B632C-4457-4B05-9435-9C3DD0F41E00" =>
                Results.Json(new { error = "invalid_otp" }, statusCode: 401),
            "21BFB9B5-C377-45D1-9B15-6B7F3432B397" =>
                Results.Json(new { error = "invalid_credentials" }, statusCode: 401),
            _ =>
                Results.Json(new { error = "server_error", detail = ex.Status.Detail }, statusCode: 500)
        };
    }
});

// POST /api/auth/refresh — обновление access токена по refresh токену
app.MapPost("/api/auth/refresh", async (HttpContext httpCtx, IdentityApi.IdentityApiClient identityClient, MetricsCollector metrics) =>
{
    metrics.Increment("auth_refresh_attempts");

    var body = await httpCtx.Request.ReadFromJsonAsync<RefreshRequest>();
    if (body == null || string.IsNullOrWhiteSpace(body.RefreshToken))
        return Results.BadRequest(new { error = "missing_refresh_token" });

    try
    {
        var response = await identityClient.CreateTokenAsync(
            new CreateTokenRequest { RefreshToken = body.RefreshToken });

        metrics.Increment("auth_refresh_success");

        return Results.Ok(new
        {
            accessToken = response.AccessToken.Value,
            expiration = response.AccessToken.ExpirationDate.ToDateTimeOffset().ToUnixTimeMilliseconds()
        });
    }
    catch (RpcException)
    {
        metrics.Increment("auth_refresh_failed");
        return Results.Json(new { error = "invalid_refresh_token" }, statusCode: 401);
    }
});

// ========== CHATS ENDPOINTS ==========

// GET /api/chats?offset=0&size=50
app.MapGet("/api/chats", async (HttpContext httpCtx, MessagesApi.MessagesApiClient messagesClient, int offset = 0, int size = 50) =>
{
    var metadata = BuildAuthMetadata(httpCtx);
    if (metadata == null) return Results.Json(new { error = "unauthorized" }, statusCode: 401);

    try
    {
        var response = await messagesClient.ListChatsAsync(
            new ListChatsRequest { Pagination = new PageRequest { Offset = offset, Size = Math.Min(size, 50) } },
            new CallOptions(headers: metadata));

        return Results.Ok(new
        {
            chats = response.Chats.Select(c => MapChat(c)),
            totalCount = response.TotalCount
        });
    }
    catch (RpcException ex) { return HandleGrpcError(ex); }
});

// GET /api/chats/{chatId}/messages?from=0&before=30&after=0
app.MapGet("/api/chats/{chatId}/messages", async (HttpContext httpCtx, MessagesApi.MessagesApiClient messagesClient,
    string chatId, long from = 0, int before = 30, int after = 0) =>
{
    var metadata = BuildAuthMetadata(httpCtx);
    if (metadata == null) return Results.Json(new { error = "unauthorized" }, statusCode: 401);

    try
    {
        var response = await messagesClient.ListMessagesAsync(
            new ListMessagesRequest
            {
                ChatId = chatId,
                FromMessageId = from,
                OffsetBefore = Math.Min(before, 50),
                OffsetAfter = Math.Min(after, 50)
            },
            new CallOptions(headers: metadata));

        return Results.Ok(new { messages = response.Messages.Select(m => MapMessage(m)) });
    }
    catch (RpcException ex) { return HandleGrpcError(ex); }
});

// GET /api/chats/{chatId}/info
app.MapGet("/api/chats/{chatId}/info", async (HttpContext httpCtx, MessagesApi.MessagesApiClient messagesClient, string chatId) =>
{
    var metadata = BuildAuthMetadata(httpCtx);
    if (metadata == null) return Results.Json(new { error = "unauthorized" }, statusCode: 401);

    try
    {
        var response = await messagesClient.GetChatInfoAsync(
            new GetChatInfoRequest { ChatId = chatId },
            new CallOptions(headers: metadata));

        return Results.Ok(new
        {
            lastMessageId = response.LastMessageId,
            firstUnreadMessageId = response.FirstUnreadMessageId,
            title = response.Title,
            picture = response.Picture,
            isGroupChat = response.IsGroupChat,
            countUnread = response.CountUnread,
            membersId = response.MembersId.ToList()
        });
    }
    catch (RpcException ex) { return HandleGrpcError(ex); }
});

// GET /api/chats/personal/{userId}
app.MapGet("/api/chats/personal/{userId:long}", async (HttpContext httpCtx, MessagesApi.MessagesApiClient messagesClient, long userId) =>
{
    var metadata = BuildAuthMetadata(httpCtx);
    if (metadata == null) return Results.Json(new { error = "unauthorized" }, statusCode: 401);

    try
    {
        var response = await messagesClient.GetPersonChatIdAsync(
            new GetPersonChatIdRequest { UserId = userId },
            new CallOptions(headers: metadata));

        return Results.Ok(new { chatId = response.ChatId });
    }
    catch (RpcException ex) { return HandleGrpcError(ex); }
});

// POST /api/chats/send
app.MapPost("/api/chats/send", async (HttpContext httpCtx, MessagesApi.MessagesApiClient messagesClient) =>
{
    var metadata = BuildAuthMetadata(httpCtx);
    if (metadata == null) return Results.Json(new { error = "unauthorized" }, statusCode: 401);

    var body = await httpCtx.Request.ReadFromJsonAsync<SendMessageRequest>(JsonOpts);
    if (body == null) return Results.BadRequest(new { error = "invalid_body" });

    var request = new BarkFluff.Proto.Messages.SendMessageRequest
    {
        Message = new OutgoingMessage { Text = body.Text ?? "" }
    };

    if (!string.IsNullOrEmpty(body.ChatId))
        request.ChatId = body.ChatId;
    else if (body.UserId.HasValue)
        request.UserId = body.UserId.Value;
    else
        return Results.BadRequest(new { error = "chatId_or_userId_required" });

    if (body.FileIds is { Count: > 0 })
        request.Message.FilesIds.AddRange(body.FileIds);

    try
    {
        var response = await messagesClient.SendMessageAsync(request, new CallOptions(headers: metadata));
        return Results.Ok(new { message = MapMessage(response.Message) });
    }
    catch (RpcException ex) { return HandleGrpcError(ex); }
});

// POST /api/chats/mark-read
app.MapPost("/api/chats/mark-read", async (HttpContext httpCtx, MessagesApi.MessagesApiClient messagesClient) =>
{
    var metadata = BuildAuthMetadata(httpCtx);
    if (metadata == null) return Results.Json(new { error = "unauthorized" }, statusCode: 401);

    var body = await httpCtx.Request.ReadFromJsonAsync<MarkAsReadRequest>(JsonOpts);
    if (body?.MessageIds == null || body.MessageIds.Count == 0)
        return Results.BadRequest(new { error = "missing_message_ids" });

    var request = new BarkFluff.Proto.Messages.MarkAsReadRequest();
    request.MessageIds.AddRange(body.MessageIds);

    try
    {
        await messagesClient.MarkAsReadAsync(request, new CallOptions(headers: metadata));
        return Results.Ok(new { ok = true });
    }
    catch (RpcException ex) { return HandleGrpcError(ex); }
});

// ========== USERS ENDPOINTS ==========

// GET /api/users/{userId}
app.MapGet("/api/users/{userId:long}", async (HttpContext httpCtx, UsersApi.UsersApiClient usersClient, long userId) =>
{
    var metadata = BuildAuthMetadata(httpCtx);
    if (metadata == null) return Results.Json(new { error = "unauthorized" }, statusCode: 401);

    try
    {
        var response = await usersClient.GetUserAsync(
            new GetUserRequest { UserId = userId },
            new CallOptions(headers: metadata));

        return Results.Ok(new { user = MapUser(response.User) });
    }
    catch (RpcException ex) { return HandleGrpcError(ex); }
});

// GET /api/users/search?query=X&offset=0&size=20
app.MapGet("/api/users/search", async (HttpContext httpCtx, UsersApi.UsersApiClient usersClient,
    string query = "", int offset = 0, int size = 20) =>
{
    var metadata = BuildAuthMetadata(httpCtx);
    if (metadata == null) return Results.Json(new { error = "unauthorized" }, statusCode: 401);

    try
    {
        var response = await usersClient.SearchUsersAsync(
            new SearchUsersRequest
            {
                Query = query,
                Pagination = new PageRequest { Offset = offset, Size = Math.Min(size, 50) }
            },
            new CallOptions(headers: metadata));

        return Results.Ok(new
        {
            users = response.Users.Select(u => MapUser(u)),
            totalCount = response.TotalCount
        });
    }
    catch (RpcException ex) { return HandleGrpcError(ex); }
});

// ========== FILES ENDPOINTS ==========

// GET /api/files/download-urls?fileIds=X,Y
app.MapGet("/api/files/download-urls", async (HttpContext httpCtx, FilesApi.FilesApiClient filesClient, string fileIds = "") =>
{
    var metadata = BuildAuthMetadata(httpCtx);
    if (metadata == null) return Results.Json(new { error = "unauthorized" }, statusCode: 401);

    var ids = fileIds.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    if (ids.Length == 0) return Results.BadRequest(new { error = "missing_file_ids" });

    var request = new GetTempDownloadUrlRequest();
    request.FileIds.AddRange(ids);

    try
    {
        var response = await filesClient.GetTempDownloadUrlAsync(request, new CallOptions(headers: metadata));

        return Results.Ok(new
        {
            files = response.FileUrls.Select(f => new
            {
                fileId = f.FileId,
                url = f.Url,
                previewUrl = f.PreviewUrl
            })
        });
    }
    catch (RpcException ex) { return HandleGrpcError(ex); }
});

// GET /api/files/upload-url?fileType=2
app.MapGet("/api/files/upload-url", async (HttpContext httpCtx, FilesApi.FilesApiClient filesClient, int fileType = 0) =>
{
    var metadata = BuildAuthMetadata(httpCtx);
    if (metadata == null) return Results.Json(new { error = "unauthorized" }, statusCode: 401);

    try
    {
        var response = await filesClient.GetUploadUrlAsync(
            new GetUploadUrlRequest { FileType = (UploadFileType)fileType },
            new CallOptions(headers: metadata));

        return Results.Ok(new { url = response.Url, fileId = response.FileId });
    }
    catch (RpcException ex) { return HandleGrpcError(ex); }
});

// ========== ONLINE ENDPOINTS ==========

// POST /api/online/ping
app.MapPost("/api/online/ping", async (HttpContext httpCtx, OnlinerApi.OnlinerApiClient onlinerClient) =>
{
    var metadata = BuildAuthMetadata(httpCtx);
    if (metadata == null) return Results.Json(new { error = "unauthorized" }, statusCode: 401);

    try
    {
        await onlinerClient.SetOnlineStatusAsync(new SetOnlineStatusRequest(), new CallOptions(headers: metadata));
        return Results.Ok(new { ok = true });
    }
    catch (RpcException ex) { return HandleGrpcError(ex); }
});

// GET /api/online/status?userIds=1,2
app.MapGet("/api/online/status", async (HttpContext httpCtx, OnlinerApi.OnlinerApiClient onlinerClient, string userIds = "") =>
{
    var metadata = BuildAuthMetadata(httpCtx);
    if (metadata == null) return Results.Json(new { error = "unauthorized" }, statusCode: 401);

    var ids = userIds.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    var request = new GetOnlineStatusRequest();
    foreach (var id in ids)
        if (long.TryParse(id, out var uid))
            request.UserIds.Add(uid);

    try
    {
        var response = await onlinerClient.GetOnlineStatusAsync(request, new CallOptions(headers: metadata));
        return Results.Ok(new
        {
            statuses = response.UsersStatuses.Select(s => new
            {
                userId = s.UserId,
                status = s.Status.ToString(),
                lastSeen = s.LastSeen?.ToDateTimeOffset().ToUnixTimeMilliseconds()
            })
        });
    }
    catch (RpcException ex) { return HandleGrpcError(ex); }
});

// ========== SSE ENDPOINTS ==========

// GET /api/sse/updates?token=X
app.MapGet("/api/sse/updates", async (HttpContext httpCtx, UpdatesApi.UpdatesApiClient updatesClient, string token = "") =>
{
    if (string.IsNullOrEmpty(token))
    {
        httpCtx.Response.StatusCode = 401;
        return;
    }

    httpCtx.Response.ContentType = "text/event-stream";
    httpCtx.Response.Headers.CacheControl = "no-cache";
    httpCtx.Response.Headers.Connection = "keep-alive";

    var metadata = BuildAuthMetadataFromToken(httpCtx, token);
    var ct = httpCtx.RequestAborted;
    var semaphore = new SemaphoreSlim(1, 1);

    async Task WriteSseEvent(string eventType, object data)
    {
        var json = JsonSerializer.Serialize(data, JsonOpts);
        await semaphore.WaitAsync(ct);
        try
        {
            await httpCtx.Response.WriteAsync($"event: {eventType}\ndata: {json}\n\n", ct);
            await httpCtx.Response.Body.FlushAsync(ct);
        }
        finally { semaphore.Release(); }
    }

    var task1 = Task.Run(async () =>
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var stream = updatesClient.SubscribeNewMessages(
                    new SubscribeNewMessagesRequest(), new CallOptions(headers: metadata, cancellationToken: ct));
                await foreach (var evt in stream.ResponseStream.ReadAllAsync(ct))
                {
                    await WriteSseEvent("new_message", new
                    {
                        chatId = evt.ChatId,
                        message = MapMessage(evt.Message)
                    });
                }
            }
            catch (OperationCanceledException) { break; }
            catch (RpcException ex) when (ex.StatusCode == StatusCode.Cancelled) { break; }
            catch { await Task.Delay(3000, ct); }
        }
    }, ct);

    var task2 = Task.Run(async () =>
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var stream = updatesClient.SubscribeMessagesRead(
                    new SubscribeMessagesReadRequest(), new CallOptions(headers: metadata, cancellationToken: ct));
                await foreach (var evt in stream.ResponseStream.ReadAllAsync(ct))
                {
                    await WriteSseEvent("message_read", new
                    {
                        chatId = evt.ChatId,
                        messageId = evt.MessageId,
                        readBy = evt.NewReadBy.ToList()
                    });
                }
            }
            catch (OperationCanceledException) { break; }
            catch (RpcException ex) when (ex.StatusCode == StatusCode.Cancelled) { break; }
            catch { await Task.Delay(3000, ct); }
        }
    }, ct);

    await Task.WhenAll(task1, task2);
});

// GET /api/sse/online?userIds=1,2&token=X
app.MapGet("/api/sse/online", async (HttpContext httpCtx, OnlinerApi.OnlinerApiClient onlinerClient, string token = "", string userIds = "") =>
{
    if (string.IsNullOrEmpty(token))
    {
        httpCtx.Response.StatusCode = 401;
        return;
    }

    httpCtx.Response.ContentType = "text/event-stream";
    httpCtx.Response.Headers.CacheControl = "no-cache";
    httpCtx.Response.Headers.Connection = "keep-alive";

    var metadata = BuildAuthMetadataFromToken(httpCtx, token);
    var ct = httpCtx.RequestAborted;

    var request = new SubscribeToOnlineStatusRequest();
    foreach (var id in userIds.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        if (long.TryParse(id, out var uid))
            request.UserIds.Add(uid);

    var stream = onlinerClient.SubscribeToOnlineStatus(request, new CallOptions(headers: metadata, cancellationToken: ct));

    try
    {
        await foreach (var evt in stream.ResponseStream.ReadAllAsync(ct))
        {
            var json = JsonSerializer.Serialize(new
            {
                userId = evt.UserId,
                status = evt.Status.ToString(),
                lastSeen = evt.LastSeen?.ToDateTimeOffset().ToUnixTimeMilliseconds()
            }, JsonOpts);

            await httpCtx.Response.WriteAsync($"event: online_status\ndata: {json}\n\n", ct);
            await httpCtx.Response.Body.FlushAsync(ct);
        }
    }
    catch (OperationCanceledException) { }
    catch (RpcException ex) when (ex.StatusCode == StatusCode.Cancelled) { }
});

app.MapGet("/messenger", (IWebHostEnvironment env) =>
    Results.File(Path.Combine(env.WebRootPath, "messenger.html"), "text/html"));

app.MapFallbackToFile("index.html");

app.Lifetime.ApplicationStopped.Register(Log.CloseAndFlush);
app.Run();

// --- Утилиты ---

static Metadata BuildMetadata(HttpContext ctx, string? deviceId, string? browserName, string? osName)
{
    var ip = ctx.Request.Headers["X-Forwarded-For"].FirstOrDefault()
             ?? ctx.Request.Headers["X-Real-IP"].FirstOrDefault()
             ?? ctx.Connection.RemoteIpAddress?.ToString()
             ?? "unknown";

    return new Metadata
    {
        { "x-device-id", ToBase64(deviceId ?? Guid.NewGuid().ToString()) },
        { "x-device-name", ToBase64(browserName ?? "Неизвестно") },
        { "x-os-name", ToBase64(osName ?? "Неизвестно") },
        { "x-app-name", ToBase64("BarkFluff Web") },
        { "x-app-version", ToBase64("1.0.0") },
        { "x-ip-address", ToBase64(ip) },
    };
}

static Metadata? BuildAuthMetadata(HttpContext ctx)
{
    var authHeader = ctx.Request.Headers.Authorization.FirstOrDefault();
    if (string.IsNullOrEmpty(authHeader) || !authHeader.StartsWith("Bearer "))
        return null;

    var token = authHeader["Bearer ".Length..];
    return BuildAuthMetadataFromToken(ctx, token);
}

static Metadata BuildAuthMetadataFromToken(HttpContext ctx, string token)
{
    var ip = ctx.Request.Headers["X-Forwarded-For"].FirstOrDefault()
             ?? ctx.Request.Headers["X-Real-IP"].FirstOrDefault()
             ?? ctx.Connection.RemoteIpAddress?.ToString()
             ?? "unknown";

    var deviceId = ctx.Request.Headers["X-Device-Id"].FirstOrDefault() ?? Guid.NewGuid().ToString();
    var browserName = ctx.Request.Headers["X-Browser-Name"].FirstOrDefault() ?? "Неизвестно";
    var osName = ctx.Request.Headers["X-Os-Name"].FirstOrDefault() ?? "Неизвестно";

    return new Metadata
    {
        { "x-auth-token", token },
        { "x-device-id", ToBase64(deviceId) },
        { "x-device-name", ToBase64(browserName) },
        { "x-os-name", ToBase64(osName) },
        { "x-app-name", ToBase64("BarkFluff Web") },
        { "x-app-version", ToBase64("1.0.0") },
        { "x-ip-address", ToBase64(ip) },
    };
}

static IResult HandleGrpcError(RpcException ex)
{
    return ex.StatusCode switch
    {
        StatusCode.Unauthenticated => Results.Json(new { error = "unauthorized" }, statusCode: 401),
        StatusCode.PermissionDenied => Results.Json(new { error = "forbidden" }, statusCode: 403),
        StatusCode.NotFound => Results.Json(new { error = "not_found" }, statusCode: 404),
        _ => Results.Json(new { error = "server_error", detail = ex.Status.Detail }, statusCode: 500)
    };
}

static string ToBase64(string value) =>
    Convert.ToBase64String(Encoding.UTF8.GetBytes(value));

// --- Маппинг gRPC → JSON ---

static object MapChat(Chat c) => new
{
    id = c.Id,
    title = c.Title,
    picture = c.Picture,
    isGroupChat = c.IsGroupChat,
    lastMessage = c.LastMessage != null ? MapMessage(c.LastMessage) : null,
    members = c.Members.Select(m => new { userId = m.UserId }),
    countUnread = c.CountUnread,
    firstUnreadMessageId = c.FirstUnreadMessageId
};

static object MapMessage(Message m) => new
{
    id = m.Id,
    senderId = m.SenderId,
    readBy = m.ReadBy.ToList(),
    sentAt = m.SentAt?.ToDateTimeOffset().ToUnixTimeMilliseconds(),
    type = m.Type.ToString(),
    content = new
    {
        text = m.Content?.Text ?? "",
        attachments = m.Content?.Attachments.Select(a => new
        {
            id = a.Id,
            type = a.Type.ToString(),
            fileId = a.FileId,
            previewUrl = a.PreviewUrl,
            attachmentSize = a.AttachmentSize,
            previewFileId = a.PreviewFileId,
            fileName = a.FileName
        }) ?? Enumerable.Empty<object>()
    }
};

static object MapUser(User u) => new
{
    id = u.Id,
    firstName = u.FirstName,
    lastName = u.LastName,
    username = u.Username,
    profilePicture = u.ProfilePicture,
    profilePicturePreview = u.ProfilePicturePreview,
    bio = u.Bio,
    badges = u.Badges.Select(b => new
    {
        name = b.Badge?.Name,
        imageUrl = b.Badge?.ImageUrl,
        priority = b.Priority
    })
};

// --- DTO ---

record LoginRequest(string? Login, string? Password, string? OtpCode, string? DeviceId, string? BrowserName, string? OsName);
record RefreshRequest(string? RefreshToken);
record SendMessageRequest(string? ChatId, long? UserId, string? Text, List<string>? FileIds);
record MarkAsReadRequest(List<long>? MessageIds);

class TokenResponse
{
    public string AccessToken { get; init; } = "";
    public long AccessTokenExpiration { get; init; }
    public string RefreshToken { get; init; } = "";
    public long RefreshTokenExpiration { get; init; }
}
