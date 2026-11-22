using BarkFluff.Proto.Users;
using BarkFluff.Shared.Queue.Messages;

using Grpc.Core;

using MassTransit;

using Microsoft.AspNetCore.Http;

namespace Barkfluff.AdminPanel.Services;

/// <summary>
/// Тестовая отправка push-уведомления. Получатель — BarkFluff-пользователь, привязанный к записи
/// администратора панели; его устройства находит Users. Публикация идёт тем же событием, что и
/// рассылка, с TargetDeviceIds — пустой список означал бы рассылку на все устройства, поэтому
/// отправка без устройств отклоняется до публикации.
/// </summary>
public sealed class NotificationTestService
{
    private static readonly TimeSpan UsersCallTimeout = TimeSpan.FromSeconds(10);

    private readonly AdminService _adminService;
    private readonly UsersServerApi.UsersServerApiClient _usersClient;
    private readonly IPublishEndpoint _publishEndpoint;

    public NotificationTestService(
        AdminService adminService,
        UsersServerApi.UsersServerApiClient usersClient,
        IPublishEndpoint publishEndpoint)
    {
        _adminService = adminService;
        _usersClient = usersClient;
        _publishEndpoint = publishEndpoint;
    }

    /// <summary>Возвращает привязанного получателя с актуальными устройствами или null, если привязки нет.</summary>
    public async Task<TestRecipient?> GetRecipientAsync(long adminTelegramUserId, CancellationToken cancellationToken)
    {
        var barkFluffUserId = _adminService.GetRecord(adminTelegramUserId)?.BarkFluffUserId;
        if (barkFluffUserId is null)
            return null;

        return await LoadRecipientAsync(barkFluffUserId.Value, cancellationToken);
    }

    /// <summary>Проверяет, что пользователь существует, и сохраняет привязку текущему администратору.</summary>
    public async Task<TestRecipient> LinkRecipientAsync(
        long adminTelegramUserId,
        string updatedBy,
        long barkFluffUserId,
        CancellationToken cancellationToken)
    {
        if (barkFluffUserId <= 0)
            throw new TestNotificationException(StatusCodes.Status400BadRequest, "BarkFluff ID должен быть положительным числом.");

        var recipient = await LoadRecipientAsync(barkFluffUserId, cancellationToken);

        if (!_adminService.SetBarkFluffUserId(adminTelegramUserId, barkFluffUserId, updatedBy))
            throw new TestNotificationException(StatusCodes.Status404NotFound, "Запись администратора не найдена.");

        return recipient;
    }

    /// <summary>Снимает привязку тестового получателя.</summary>
    public void UnlinkRecipient(long adminTelegramUserId, string updatedBy)
    {
        if (!_adminService.SetBarkFluffUserId(adminTelegramUserId, null, updatedBy))
            throw new TestNotificationException(StatusCodes.Status404NotFound, "Запись администратора не найдена.");
    }

    /// <summary>Ставит тестовое уведомление в очередь на устройства привязанного получателя.</summary>
    public async Task<IReadOnlyList<TestRecipientDevice>> SendTestAsync(
        long adminTelegramUserId,
        string title,
        string body,
        string? imageUrl,
        CancellationToken cancellationToken)
    {
        var barkFluffUserId = _adminService.GetRecord(adminTelegramUserId)?.BarkFluffUserId
            ?? throw new TestNotificationException(
                StatusCodes.Status400BadRequest,
                "Тестовый получатель не привязан. Укажите BarkFluff ID своего пользователя.");

        var devices = await GetDevicesAsync(barkFluffUserId, cancellationToken);
        if (devices.Count == 0)
            throw new TestNotificationException(StatusCodes.Status400BadRequest, "У пользователя нет устройств.");

        await _publishEndpoint.Publish(new AdminBroadcastNotificationEvent
        {
            Title = title,
            Body = body,
            ImageUrl = imageUrl,
            TargetDeviceIds = devices.Select(d => d.DeviceId).ToList()
        }, cancellationToken);

        return devices;
    }

    private async Task<TestRecipient> LoadRecipientAsync(long barkFluffUserId, CancellationToken cancellationToken)
    {
        var userResponse = await CallUsersAsync(() => _usersClient.GetByIdAsync(
            new GetByIdRequest { UserId = barkFluffUserId },
            deadline: DateTime.UtcNow.Add(UsersCallTimeout),
            cancellationToken: cancellationToken));

        // Протобуф возвращает пустой User вместо null, поэтому существование проверяем по Id.
        var user = userResponse.User;
        if (user is null || user.Id != barkFluffUserId)
            throw new TestNotificationException(StatusCodes.Status404NotFound, "Пользователь с таким BarkFluff ID не найден.");

        var devices = await GetDevicesAsync(barkFluffUserId, cancellationToken);
        return new TestRecipient(barkFluffUserId, user.Username, user.FirstName, user.LastName, devices);
    }

    private async Task<IReadOnlyList<TestRecipientDevice>> GetDevicesAsync(long barkFluffUserId, CancellationToken cancellationToken)
    {
        var response = await CallUsersAsync(() => _usersClient.GetUserDevicesAsync(
            new GetUserDevicesRequest { UserId = barkFluffUserId },
            deadline: DateTime.UtcNow.Add(UsersCallTimeout),
            cancellationToken: cancellationToken));

        return response.Devices
            .Where(d => Guid.TryParse(d.DeviceId, out _))
            .Select(d => new TestRecipientDevice(
                Guid.Parse(d.DeviceId),
                FirstNonEmpty(d.CustomName, d.OriginalName, "Устройство"),
                d.OperationSystem,
                d.AppName,
                d.NotificationsEnabled))
            .ToList();
    }

    private static async Task<T> CallUsersAsync<T>(Func<AsyncUnaryCall<T>> call)
    {
        try
        {
            return await call();
        }
        catch (RpcException)
        {
            throw new TestNotificationException(
                StatusCodes.Status503ServiceUnavailable,
                "Сервис пользователей временно недоступен. Повторите позже.");
        }
    }

    private static string FirstNonEmpty(params string[] values)
    {
        return values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v)) ?? string.Empty;
    }
}

public sealed record TestRecipient(
    long UserId,
    string Username,
    string FirstName,
    string LastName,
    IReadOnlyList<TestRecipientDevice> Devices);

public sealed record TestRecipientDevice(
    Guid DeviceId,
    string Name,
    string OperationSystem,
    string AppName,
    bool NotificationsEnabled);

/// <summary>Ошибка, которую эндпоинт переводит в HTTP-ответ с понятным сообщением.</summary>
public sealed class TestNotificationException : Exception
{
    public TestNotificationException(int statusCode, string message) : base(message)
    {
        StatusCode = statusCode;
    }

    public int StatusCode { get; }
}
