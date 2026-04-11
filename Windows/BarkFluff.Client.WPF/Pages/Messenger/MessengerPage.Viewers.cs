using BarkFluff.Client.WPF.UserControls;
using BarkFluff.WebApi.Core.MessengerData;
using BarkFluff.WebApi.Core.MessengerData.NonSavedData;

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;

using Erida = BarkFluff.Client.WPF.Services.Erida.MessageType;
using MessageAttachmentType = BarkFluff.Proto.Shared.MessageAttachmentType;
using MType = BarkFluff.Client.WPF.Services.Erida.MessageType.MessageTypeEnum;

namespace BarkFluff.Client.WPF.Pages
{
    public partial class MessengerPage
    {
        #region Обработка задач из протокола

        private async void MessagerTask_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            var task = App.MessagerTask.Value;
            if (!string.IsNullOrEmpty(task))
            {
                App.MessagerTask.Value = string.Empty;
                OpenCommandViaProtocol(task);
            }
            else
            {
                return;
            }
        }

        private async void OpenCommandViaProtocol(string task)
        {
            try
            {
                if (task.StartsWith("user-username"))
                {
                    var command = task.Split("=")[0];
                    var arg = task.Split("=")[1];
                    if (command == "user-username")
                    {
                        var result = await App.ServerCommunication.CheckUsername(arg, App.GParam);
                        if (!result.error.IsSuccess)
                        {
                            App.ErideMessage.AddMessage($"Что-то пошло не так {result.error.ErrorMessage}", new Erida { Type = MType.Warning });
                            return;
                        }
                        if (result.exists)
                        {
                            var responseUserId = await App.ServerCommunication.SearchUser(App.GParam, arg);
                            if (!responseUserId.error.IsSuccess)
                            {
                                App.ErideMessage.AddMessage($"Что-то пошло не так {responseUserId.error.ErrorMessage}", new Erida { Type = MType.Warning });
                                return;
                            }
                            var responseChatId = await App.ServerCommunication.GetPersonChatId(App.GParam, responseUserId.userList[0].Id);
                            if (!responseChatId.error.IsSuccess)
                            {
                                App.ErideMessage.AddMessage($"Что-то пошло не так {responseChatId.error.ErrorMessage}", new Erida { Type = MType.Warning });
                                return;
                            }

                            OpenChatFromSearch(responseUserId.userList[0].Id);

                            App.ErideMessage.AddMessage($"Открытие чата с {arg}", new Erida { Type = MType.Info });
                        }
                        else
                        {
                            App.ErideMessage.AddMessage($"Пользователь {arg} не найден", new Erida { Type = MType.Warning });
                        }
                    }
                }
                if (task.StartsWith("successfulupdate"))
                {
                    OpenCenterPanel();
                    CenterPanel.Child = new UserControls.PostUpdateMessage();
                }

                if (task.StartsWith("launch-updater"))
                {
                    // Показать сообщение об обновлении
                    var message = "Доступно новое обновление Barkfluff!";
                    App.ErideMessage.AddMessage(message, new Erida { Type = MType.Warning });

                    // Запустить Barkfluff.Updater.CLI.exe
                    try
                    {
                        string appDirectory = AppDomain.CurrentDomain.BaseDirectory;
                        string updaterPath = System.IO.Path.Combine(appDirectory, "Barkfluff.Updater.CLI.exe");

                        if (System.IO.File.Exists(updaterPath))
                        {
                            // Запустить обновление в фоновом режиме
                            Process.Start(new ProcessStartInfo
                            {
                                FileName = updaterPath,
                                Arguments = "--noseamless", // Принудительное обновление без бесшовного режима
                                CreateNoWindow = true, // Не показывать окно консоли
                                WindowStyle = ProcessWindowStyle.Hidden,
                                UseShellExecute = true // Использовать оболочку для запуска
                            });

                            App.ErideMessage.AddMessage("Запущено обновление Barkfluff", new Erida { Type = MType.Debug });
                        }
                        else
                        {
                            App.ErideMessage.AddMessage("Обновление не найдено", new Erida { Type = MType.Debug });
                        }
                    }
                    catch (Exception ex)
                    {
                        App.ErideMessage.AddMessage($"Ошибка при запуске обновления: {ex.Message}", new Erida { Type = MType.Debug });
                    }
                }

            }
            catch (Exception ex)
            {
                var a = ex.Message;
                App.ErideMessage.AddMessage($"Ошибка при выполении задачи из протокола: {ex.Message}", new Erida { Type = MType.Error });
            }
        }

        #endregion

        #region Управление иконкой обновления

        /// <summary>
        /// Показывает иконку обновления в заголовке приложения
        /// </summary>
        public void ShowUpdateIcon()
        {
            UpdateIconBorder.Visibility = Visibility.Visible;
        }

        /// <summary>
        /// Скрывает иконку обновления в заголовке приложения
        /// </summary>
        public void HideUpdateIcon()
        {
            UpdateIconBorder.Visibility = Visibility.Collapsed;
        }

        #endregion

        #region Updater Launch

        /// <summary>
        /// Запускает программу обновления Barkfluff.Updater.CLI.exe
        /// </summary>
        private void LaunchUpdater()
        {
            try
            {
                // Получаем путь к директории текущей программы
                string currentDirectory = AppDomain.CurrentDomain.BaseDirectory;
                string updaterPath = Path.Combine(currentDirectory, "Barkfluff.Updater.CLI.exe");

                // Проверяем, существует ли файл
                if (!File.Exists(updaterPath))
                {
                    App.ErideMessage.AddMessage($"Файл обновления не найден: {updaterPath}", new Erida { Type = MType.Error });
                    MessageBox.Show("Программа обновления не найдена.", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                // Создаем процесс для запуска обновления
                var processInfo = new ProcessStartInfo
                {
                    FileName = updaterPath,
                    UseShellExecute = true,
                    WorkingDirectory = currentDirectory
                };

                Process.Start(processInfo);
                App.ErideMessage.AddMessage("Программа обновления запущена", new Erida { Type = MType.Debug });

                // Закрываем текущее приложение для применения обновления
                Application.Current.Shutdown();
            }
            catch (Exception ex)
            {
                App.ErideMessage.AddMessage($"Ошибка при запуске программы обновления: {ex.Message}", new Erida { Type = MType.Error });
                MessageBox.Show($"Не удалось запустить программу обновления: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        #endregion

        #region Image / Video viewers, QR

        public void OpenQRModal()
        {
            OpenCenterPanel();
            CenterPanel.Child = new UserControls.ProfileShare(App.GParam.UserName);
        }

        public void OpenImageViewer(List<AttachmentsModel> attachments, int currentIndex)
        {
            // Фильтровать только изображения и GIF
            var imageAttachments = attachments
                .Where(a => a.Type == MessageAttachmentType.Image ||
                            a.Type == MessageAttachmentType.Gif)
                .ToList();

            if (imageAttachments.Count == 0) return;

            var adjustedIndex = Math.Min(currentIndex, imageAttachments.Count - 1);

            ImageViewer.Attachments = imageAttachments;
            ImageViewer.CurrentIndex = adjustedIndex;
            ImageViewer.IsOpen = true;
            ImageViewerOverlay.Visibility = Visibility.Visible;
            ImageViewer.Focus(); // Для обработки клавиатуры
        }

        private void OnImageViewerClosed(object sender, EventArgs e)
        {
            ImageViewerOverlay.Visibility = Visibility.Collapsed;
            ImageViewer.IsOpen = false;
        }

        public void OpenVideoPlayer(List<AttachmentsModel> attachments, int currentIndex)
        {
            // Фильтровать только видео
            var videoAttachments = attachments
                .Where(a => a.Type == MessageAttachmentType.Video)
                .ToList();

            if (videoAttachments.Count == 0) return;

            var adjustedIndex = Math.Min(currentIndex, videoAttachments.Count - 1);

            VideoPlayer.Attachments = videoAttachments;
            VideoPlayer.CurrentIndex = adjustedIndex;
            VideoPlayer.IsOpen = true;
            VideoPlayerOverlay.Visibility = Visibility.Visible;
            VideoPlayer.Focus();
        }

        private void OnVideoPlayerClosed(object sender, EventArgs e)
        {
            VideoPlayerOverlay.Visibility = Visibility.Collapsed;
            VideoPlayer.IsOpen = false;
            VideoPlayer.StopAndCleanup();
        }

        #endregion
    }
}
