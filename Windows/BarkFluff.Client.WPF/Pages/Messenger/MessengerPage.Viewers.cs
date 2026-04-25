using BarkFluff.Client.WPF.UserControls;
using BarkFluff.WebApi.Core.MessengerData.NonSavedData;

using System.ComponentModel;
using System.Diagnostics;
using System.IO;
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
                    var message = "Доступно новое обновление Barkfluff!";
                    App.ErideMessage.AddMessage(message, new Erida { Type = MType.Warning });
                    LaunchUpdater();
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
        /// Генерирует PS1-скрипт обновления и запускает его отдельно от приложения
        /// </summary>
        private void LaunchUpdater()
        {
            try
            {
                var downloadUrl = App.UpdateDownloadUrl;
                if (string.IsNullOrEmpty(downloadUrl))
                {
                    App.ErideMessage.AddMessage("URL для обновления не найден", new Erida { Type = MType.Error });
                    return;
                }

                string currentDirectory = AppDomain.CurrentDomain.BaseDirectory;
                string appDataPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BarkFluff");
                bool isInstalledInAppData = currentDirectory.TrimEnd('\\', '/').Equals(appDataPath.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase);

                string scriptContent = GenerateUpdateScript(downloadUrl, currentDirectory, isInstalledInAppData);
                string scriptPath = Path.Combine(Path.GetTempPath(), "BarkFluff_update.ps1");
                File.WriteAllText(scriptPath, scriptContent, System.Text.Encoding.UTF8);

                var processInfo = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{scriptPath}\"",
                    UseShellExecute = true,
                    WorkingDirectory = Path.GetTempPath()
                };

                Process.Start(processInfo);
                App.ErideMessage.AddMessage("Запущено обновление BarkFluff", new Erida { Type = MType.Debug });
                Application.Current.Shutdown();
            }
            catch (Exception ex)
            {
                App.ErideMessage.AddMessage($"Ошибка при запуске обновления: {ex.Message}", new Erida { Type = MType.Error });
                MessageBox.Show($"Не удалось запустить обновление: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private static string GenerateUpdateScript(string downloadUrl, string installPath, bool isInstalledInAppData)
        {
            string escapedInstallPath = installPath.TrimEnd('\\', '/').Replace("'", "''");
            string escapedDownloadUrl = downloadUrl.TrimEnd('/').Replace("'", "''");

            string script = """
                $ErrorActionPreference = "Stop"

                $FallbackUrl = '%%DOWNLOAD_URL%%'
                $TempZip     = Join-Path $env:TEMP "BarkFluff_update.zip"
                $InstallPath = '%%INSTALL_PATH%%'
                $ExePath     = Join-Path $InstallPath "Barkfluff.exe"
                $IsInstalled = $%%IS_INSTALLED%%

                Write-Host "Ожидание завершения BarkFluff..."
                Start-Sleep -Seconds 2

                # Завершить запущенные процессы BarkFluff
                Get-Process -Name "Barkfluff" -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
                Start-Sleep -Seconds 1

                Write-Host "Скачивание обновления BarkFluff..."

                if (Test-Path $TempZip) {
                    Remove-Item $TempZip -Force
                }

                $BitsInfo   = $null
                $Downloaded = $false

                # Получение presigned URL и метаданных через /bitsurl
                try {
                    $BitsInfo = Invoke-RestMethod -Uri ($FallbackUrl + '/bitsurl') -Method Get
                    if ($BitsInfo.version)  { Write-Host "Версия: $($BitsInfo.version)" }
                    if ($BitsInfo.fileSize) { Write-Host "Размер: $([math]::Round($BitsInfo.fileSize / 1MB, 2)) MB" }
                }
                catch {
                    Write-Warning "Не удалось получить BITS-информацию, будет использован прямой URL."
                }

                # 1. BITS — напрямую к S3 через presigned URL
                if ($BitsInfo -ne $null) {
                    try {
                        Write-Host "Загрузка через BITS..."
                        Start-BitsTransfer -Source $BitsInfo.url -Destination $TempZip
                        $Downloaded = $true
                        Write-Host "Загружено через BITS."
                    }
                    catch {
                        Write-Warning "BITS не удался, переход на WebClient..."
                    }
                }

                # 2. WebClient fallback
                if (-not $Downloaded) {
                    $wc = New-Object System.Net.WebClient
                    $wc.DownloadFile($FallbackUrl, $TempZip)
                    Write-Host "Загружено через WebClient."
                }

                # 3. Проверка контрольной суммы
                if ($BitsInfo -ne $null -and $BitsInfo.checksum) {
                    Write-Host "Проверка контрольной суммы..."
                    $Hash = (Get-FileHash -Path $TempZip -Algorithm SHA256).Hash
                    if ($Hash -ne $BitsInfo.checksum.ToUpper()) {
                        Remove-Item $TempZip -Force -ErrorAction SilentlyContinue
                        throw "Ошибка контрольной суммы: ожидалось $($BitsInfo.checksum.ToUpper()), получено $Hash"
                    }
                    Write-Host "Контрольная сумма верна."
                }

                # 4. Распаковка
                Write-Host "Распаковка в: $InstallPath"
                if (-not (Test-Path $InstallPath)) {
                    New-Item -ItemType Directory -Path $InstallPath | Out-Null
                }
                Expand-Archive -Path $TempZip -DestinationPath $InstallPath -Force
                Remove-Item $TempZip -Force -ErrorAction SilentlyContinue
                Write-Host "Распаковка завершена."

                if ($IsInstalled) {
                    # 5. Ярлык в меню Пуск
                    $StartMenuPrograms = Join-Path ([Environment]::GetFolderPath('StartMenu')) "Programs"
                    $ShortcutPath = Join-Path $StartMenuPrograms "BarkFluff.lnk"

                    if (-not (Test-Path $ShortcutPath)) {
                        Write-Host "Создание ярлыка в меню Пуск..."
                        $wshell = New-Object -ComObject WScript.Shell
                        $shortcut = $wshell.CreateShortcut($ShortcutPath)
                        $shortcut.TargetPath = $ExePath
                        $shortcut.WorkingDirectory = $InstallPath
                        $shortcut.Description = "BarkFluff Messenger"
                        $shortcut.Save()
                        Write-Host "Ярлык создан: $ShortcutPath"
                    }
                    else {
                        Write-Host "Ярлык в меню Пуск уже существует."
                    }

                    # 6. Регистрация протокола bf:// (требует права администратора)
                    $ProtocolRegistered = Test-Path "Registry::HKEY_CLASSES_ROOT\bf"

                    if (-not $ProtocolRegistered) {
                        Write-Host "Регистрация протокола bf:// (требуются права администратора)..."

                        $RegScript = @"
                `$exePath = '$($ExePath -replace "'", "''")'
                New-PSDrive -Name HKCR -PSProvider Registry -Root HKEY_CLASSES_ROOT -ErrorAction SilentlyContinue | Out-Null
                New-Item -Path 'HKCR:\bf' -Force | Out-Null
                Set-ItemProperty -Path 'HKCR:\bf' -Name '(Default)' -Value 'URL:BarkFluff Messenger Protocol'
                Set-ItemProperty -Path 'HKCR:\bf' -Name 'URL Protocol' -Value ''
                New-Item -Path 'HKCR:\bf\DefaultIcon' -Force | Out-Null
                Set-ItemProperty -Path 'HKCR:\bf\DefaultIcon' -Name '(Default)' -Value ('"' + `$exePath + '",0')
                New-Item -Path 'HKCR:\bf\shell\open\command' -Force | Out-Null
                Set-ItemProperty -Path 'HKCR:\bf\shell\open\command' -Name '(Default)' -Value ('"' + `$exePath + '" "%1"')
                "@

                        $Bytes   = [System.Text.Encoding]::Unicode.GetBytes($RegScript)
                        $Encoded = [Convert]::ToBase64String($Bytes)

                        Start-Process powershell -Verb RunAs -ArgumentList "-NoProfile -NonInteractive -EncodedCommand $Encoded" -Wait
                        Write-Host "Регистрация протокола завершена."
                    }
                    else {
                        Write-Host "Протокол bf:// уже зарегистрирован."
                    }
                }

                # 7. Запуск BarkFluff
                if (Test-Path $ExePath) {
                    Write-Host "Запуск BarkFluff..."
                    Start-Process -FilePath $ExePath -WorkingDirectory $InstallPath -ArgumentList "--successfulupdate"
                }
                else {
                    Write-Warning "Barkfluff.exe не найден: $ExePath"
                    Read-Host "Нажмите Enter для выхода"
                }
                """;
            script = script.Replace("%%DOWNLOAD_URL%%", escapedDownloadUrl);
            script = script.Replace("%%INSTALL_PATH%%", escapedInstallPath);
            script = script.Replace("%%IS_INSTALLED%%", isInstalledInAppData ? "true" : "false");
            return script;
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
