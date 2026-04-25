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
                [Console]::OutputEncoding = [System.Text.Encoding]::UTF8
                $OutputEncoding = [System.Text.Encoding]::UTF8

                $FallbackUrl = '%%DOWNLOAD_URL%%'
                $TempZip     = Join-Path $env:TEMP "BarkFluff_update.zip"
                $InstallPath = '%%INSTALL_PATH%%'
                $ExePath     = Join-Path $InstallPath "Barkfluff.exe"
                $IsInstalled = $%%IS_INSTALLED%%

                Write-Host "Waiting for BarkFluff to exit..."
                Start-Sleep -Seconds 2

                # Kill running BarkFluff processes
                Get-Process -Name "Barkfluff" -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
                Start-Sleep -Seconds 1

                Write-Host "Downloading BarkFluff update..."

                if (Test-Path $TempZip) {
                    Remove-Item $TempZip -Force
                }

                $Meta        = $null
                $DownloadUrl = $FallbackUrl
                $Downloaded  = $false

                # Fetch metadata: URL (server cache), checksum, version
                try {
                    $Meta = Invoke-RestMethod -Uri ($FallbackUrl + '/bitsurl') -Method Get
                    if ($Meta.version)  { Write-Host "Version: $($Meta.version)" }
                    if ($Meta.fileSize) { Write-Host "Size: $([math]::Round($Meta.fileSize / 1MB, 2)) MB" }
                    if ($Meta.url)      { $DownloadUrl = $Meta.url }
                }
                catch {
                    Write-Warning "Failed to fetch metadata, checksum will be skipped."
                }

                # 1. BITS — URL from /bitsurl (server-side cache, supports Range)
                try {
                    Write-Host "Downloading via BITS..."
                    Start-BitsTransfer -Source $DownloadUrl -Destination $TempZip
                    $Downloaded = $true
                    Write-Host "Downloaded via BITS."
                }
                catch {
                    Write-Warning "BITS failed, falling back to WebClient..."
                }

                # 2. WebClient fallback
                if (-not $Downloaded) {
                    $wc = New-Object System.Net.WebClient
                    $wc.DownloadFile($DownloadUrl, $TempZip)
                    Write-Host "Downloaded via WebClient."
                }

                # 3. Checksum verification
                if ($Meta -ne $null -and $Meta.checksum) {
                    Write-Host "Verifying checksum..."
                    $Hash = (Get-FileHash -Path $TempZip -Algorithm SHA256).Hash
                    if ($Hash -ne $Meta.checksum.ToUpper()) {
                        Remove-Item $TempZip -Force -ErrorAction SilentlyContinue
                        throw "Checksum mismatch: expected $($Meta.checksum.ToUpper()), got $Hash"
                    }
                    Write-Host "Checksum OK."
                }

                # 4. Extract
                Write-Host "Extracting to: $InstallPath"
                if (-not (Test-Path $InstallPath)) {
                    New-Item -ItemType Directory -Path $InstallPath | Out-Null
                }
                Expand-Archive -Path $TempZip -DestinationPath $InstallPath -Force
                Remove-Item $TempZip -Force -ErrorAction SilentlyContinue
                Write-Host "Extraction complete."

                if ($IsInstalled) {
                    # 5. Start Menu shortcut
                    $StartMenuPrograms = Join-Path ([Environment]::GetFolderPath('StartMenu')) "Programs"
                    $ShortcutPath = Join-Path $StartMenuPrograms "BarkFluff.lnk"

                    if (-not (Test-Path $ShortcutPath)) {
                        Write-Host "Creating Start Menu shortcut..."
                        $wshell = New-Object -ComObject WScript.Shell
                        $shortcut = $wshell.CreateShortcut($ShortcutPath)
                        $shortcut.TargetPath = $ExePath
                        $shortcut.WorkingDirectory = $InstallPath
                        $shortcut.Description = "BarkFluff Messenger"
                        $shortcut.Save()
                        Write-Host "Shortcut created: $ShortcutPath"
                    }
                    else {
                        Write-Host "Start Menu shortcut already exists."
                    }

                    # 6. Register bf:// protocol (requires admin)
                    $ProtocolRegistered = Test-Path "Registry::HKEY_CLASSES_ROOT\bf"

                    if (-not $ProtocolRegistered) {
                        Write-Host "Registering bf:// protocol (requires Administrator)..."

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
                        Write-Host "Protocol registration complete."
                    }
                    else {
                        Write-Host "Protocol bf:// already registered."
                    }
                }

                # 7. Launch BarkFluff
                if (Test-Path $ExePath) {
                    Write-Host "Launching BarkFluff..."
                    Start-Process -FilePath $ExePath -WorkingDirectory $InstallPath -ArgumentList "--successfulupdate"
                }
                else {
                    Write-Warning "Barkfluff.exe not found at: $ExePath"
                    Read-Host "Press Enter to exit"
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
