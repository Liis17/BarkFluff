using BarkFluff.Client.WPF.Services.Erida;

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows.Controls;

using Erida = BarkFluff.Client.WPF.Services.Erida.MessageType;
using MType = BarkFluff.Client.WPF.Services.Erida.MessageType.MessageTypeEnum;

namespace BarkFluff.Client.WPF.Pages.Messenger.Controllers
{
    /// <summary>
    /// Отвечает за отображение онлайн-статуса собеседника в заголовке открытого чата.
    /// Подписывается на OnlineStatusService.OnlineStatusChanged и запросы немедленного статуса через gRPC.
    /// </summary>
    public sealed class OnlineStatusController
    {
        private readonly TextBlock _userOnlineTime;

        // Отслеживаемый в текущий момент чат
        private long _currentChatUserId;
        private bool _isCurrentChatGroup;

        public OnlineStatusController(TextBlock userOnlineTime)
        {
            _userOnlineTime = userOnlineTime;
        }

        public void Attach()
        {
            App.OnlineStatusService.OnlineStatusChanged += OnOnlineStatusChanged;
        }

        public void Detach()
        {
            App.OnlineStatusService.OnlineStatusChanged -= OnOnlineStatusChanged;
        }

        /// <summary>
        /// Устанавливает текущий отслеживаемый чат. Для личного чата — подписывается на realtime-обновления
        /// онлайн-статуса и сразу показывает кешированное значение (если есть), иначе очищает поле.
        /// Для группового — просто очищает поле.
        /// </summary>
        public void SetCurrentChat(long userId, bool isGroup)
        {
            _currentChatUserId = userId;
            _isCurrentChatGroup = isGroup;

            if (isGroup)
            {
                Clear();
                return;
            }

            if (userId <= 0)
            {
                Clear();
                return;
            }

            App.OnlineStatusService.TrackUser(userId);

            var cachedStatus = App.OnlineStatusService.GetCachedStatus(userId);
            if (cachedStatus != null)
            {
                UpdateUserOnlineTime(cachedStatus);
            }
            else
            {
                Clear();
            }
        }

        /// <summary>
        /// Очищает текст онлайн-статуса.
        /// </summary>
        public void Clear()
        {
            if (_userOnlineTime == null) return;

            if (!_userOnlineTime.Dispatcher.CheckAccess())
            {
                _userOnlineTime.Dispatcher.BeginInvoke(new Action(Clear));
                return;
            }

            _userOnlineTime.Text = string.Empty;
        }

        /// <summary>
        /// Делает немедленный запрос онлайн-статуса для пользователя (не через stream)
        /// и обновляет UI. Использует кеш через <c>OnlineStatusService.UpdateCachedStatus</c>.
        /// </summary>
        public async Task FetchAndUpdateOnlineStatusAsync(long userId)
        {
            try
            {
                var userIds = new List<long> { userId };
                var (error, response) = await App.ServerCommunication.GetOnlineStatus(userIds, App.GParam);

                if (error.IsSuccess && response != null && response.UsersStatuses.Count > 0)
                {
                    var status = response.UsersStatuses[0];

                    // Обновляем кеш в OnlineStatusService для консистентности с stream обновлениями
                    App.OnlineStatusService.UpdateCachedStatus(status);

                    // Обновляем UI
                    _userOnlineTime.Dispatcher.Invoke(() =>
                    {
                        UpdateUserOnlineTime(status);
                    });

                    App.ErideMessage.AddMessage(
                        $"Получен статус для пользователя {userId}: {(status.Status == BarkFluff.Proto.Onliner.StatusTypeId.StatusOnline ? "онлайн" : "оффлайн")}",
                        new Erida { Type = MType.Debug });
                }
                else
                {
                    // Если не удалось получить статус - показываем пустое
                    _userOnlineTime.Dispatcher.Invoke(Clear);
                }
            }
            catch (Exception ex)
            {
                App.ErideMessage.AddMessage(
                    $"Ошибка получения статуса для пользователя {userId}: {ex.Message}",
                    new Erida { Type = MType.Error });
            }
        }

        private void OnOnlineStatusChanged(BarkFluff.Proto.Onliner.UserOnlineStatus status)
        {
            // Обновляем только если это пользователь текущего открытого чата
            if (!_isCurrentChatGroup && status.UserId == _currentChatUserId)
            {
                UpdateUserOnlineTime(status);
            }
        }

        private void UpdateUserOnlineTime(BarkFluff.Proto.Onliner.UserOnlineStatus status)
        {
            // События приходят в UI потоке, но подстраховываемся
            if (!_userOnlineTime.Dispatcher.CheckAccess())
            {
                _userOnlineTime.Dispatcher.BeginInvoke(new Action(() => UpdateUserOnlineTime(status)));
                return;
            }

            if (_userOnlineTime == null) return;

            if (status.Status == BarkFluff.Proto.Onliner.StatusTypeId.StatusOnline)
            {
                _userOnlineTime.Text = "В сети";
                _userOnlineTime.Foreground = (System.Windows.Media.Brush)_userOnlineTime.FindResource("UserOnlineTime");
            }
            else if (status.LastSeen != null)
            {
                var lastSeen = status.LastSeen.ToDateTime();
                _userOnlineTime.Text = FormatLastSeenForMessenger(lastSeen);
                _userOnlineTime.Foreground = (System.Windows.Media.Brush)_userOnlineTime.FindResource("UserOfflineTime");
            }
            else
            {
                _userOnlineTime.Text = string.Empty;
            }
        }

        private static string FormatLastSeenForMessenger(DateTime lastSeen)
        {
            // Проверяем если время Unknown (Unix epoch или очень старая дата)
            if (lastSeen.Year <= 1970 || lastSeen == DateTime.MinValue)
            {
                return "Был(а) давно";
            }

            var localTime = lastSeen.ToLocalTime();
            var now = DateTime.Now;

            // Если это сегодня - показываем только время
            if (localTime.Date == now.Date)
            {
                return $"Был(а) в {localTime:HH:mm}";
            }
            // Если это вчера
            else if (localTime.Date == now.Date.AddDays(-1))
            {
                return $"Был(а) вчера в {localTime:HH:mm}";
            }
            // Если это в пределах недели
            else if ((now - localTime).TotalDays < 7)
            {
                string dayName = localTime.ToString("dddd", System.Globalization.CultureInfo.CurrentCulture);
                return $"Был(а) в {dayName} в {localTime:HH:mm}";
            }
            // Старше недели
            else
            {
                return $"Был(а) {localTime:dd.MM.yyyy} в {localTime:HH:mm}";
            }
        }
    }
}
