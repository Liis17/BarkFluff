using BarkFluff.Client.WPF.Services.App.Caching;
using BarkFluff.Client.WPF.UserControls;
using BarkFluff.WebApi.Core.MessengerData;
using BarkFluff.WebApi.Core.MessengerData.NonSavedData;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

using Erida = BarkFluff.Client.WPF.Services.Erida.MessageType;
using MessageAttachmentType = BarkFluff.Proto.Shared.MessageAttachmentType;
using MType = BarkFluff.Client.WPF.Services.Erida.MessageType.MessageTypeEnum;

namespace BarkFluff.Client.WPF.Pages.Messenger.Controllers
{
    /// <summary>
    /// Отвечает за выбор, preview, загрузку и отправку вложений,
    /// обработку drag&amp;drop и paste изображений/файлов.
    /// </summary>
    public sealed class AttachmentController
    {
        private readonly MessengerPage _page;
        private readonly AttachmentPreviewOverlay _attachmentPreview;
        private readonly FrameworkElement _attachmentOverlay;
        private readonly FrameworkElement _dragDropOverlay;
        private readonly TextBox _textForMessage;
        private readonly ChatHistoryController _history;
        private readonly ChatListController _chatListCtrl;

        private CommandBinding? _pasteBinding;

        public AttachmentController(
            MessengerPage page,
            AttachmentPreviewOverlay attachmentPreview,
            FrameworkElement attachmentOverlay,
            FrameworkElement dragDropOverlay,
            TextBox textForMessage,
            ChatHistoryController history,
            ChatListController chatListCtrl)
        {
            _page = page;
            _attachmentPreview = attachmentPreview;
            _attachmentOverlay = attachmentOverlay;
            _dragDropOverlay = dragDropOverlay;
            _textForMessage = textForMessage;
            _history = history;
            _chatListCtrl = chatListCtrl;
        }

        /// <summary>
        /// Подписывает события preview overlay и биндинг команды вставки на TextForMessage.
        /// Должен вызываться единожды из конструктора <see cref="MessengerPage"/>.
        /// </summary>
        public void Attach()
        {
            _attachmentPreview.OnCancel += OnPreviewCancel;
            _attachmentPreview.OnSend += OnPreviewSend;

            _pasteBinding = new CommandBinding(ApplicationCommands.Paste);
            _pasteBinding.Executed += OnPasteCommand;
            _textForMessage.CommandBindings.Add(_pasteBinding);
        }

        public void Detach()
        {
            _attachmentPreview.OnCancel -= OnPreviewCancel;
            _attachmentPreview.OnSend -= OnPreviewSend;

            if (_pasteBinding != null)
            {
                _textForMessage.CommandBindings.Remove(_pasteBinding);
                _pasteBinding.Executed -= OnPasteCommand;
                _pasteBinding = null;
            }
        }

        public void OnAttachFileButtonClick()
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Multiselect = true,
                Filter = "Все файлы (*.*)|*.*",
                Title = "Выберите файл"
            };

            if (dialog.ShowDialog() == true)
            {
                ShowAttachmentPreview(dialog.FileNames.ToList());
            }
        }

        public void ShowAttachmentPreview(List<string> filePaths)
        {
            ShowAttachmentPreviewWithText(() =>
            {
                _attachmentPreview.AddAttachments(filePaths);
            });
        }

        /// <summary>
        /// Показывает AttachmentPreviewOverlay и переносит текст из TextForMessage в MessageTextBox overlay
        /// </summary>
        private void ShowAttachmentPreviewWithText(Action attachmentAction)
        {
            // Захватываем текущий текст до любых операций
            string currentText = _textForMessage.Text ?? string.Empty;

            // Выполняем действие добавления вложений
            attachmentAction();

            // Переносим текст в overlay
            _attachmentPreview.SetMessageText(currentText);

            // Очищаем исходное текстовое поле
            _textForMessage.Text = string.Empty;

            // Показываем overlay
            _attachmentOverlay.Visibility = Visibility.Visible;
        }

        private void OnPreviewCancel(object? sender, EventArgs e)
        {
            _attachmentOverlay.Visibility = Visibility.Collapsed;
            _attachmentPreview.Clear();
        }

        private async void OnPreviewSend(object? sender, SendAttachmentsEventArgs e)
        {
            _attachmentOverlay.Visibility = Visibility.Collapsed;

            if (e.SendSeparately)
            {
                // Отправить каждый файл как отдельное сообщение
                // Отправлять текст только с первым вложением, чтобы избежать дубликатов
                for (int i = 0; i < e.Attachments.Count; i++)
                {
                    var textToSend = i == 0 ? e.MessageText : string.Empty;
                    await SendMessageWithAttachments(textToSend, new List<AttachmentPreviewItem> { e.Attachments[i] });
                }
            }
            else
            {
                // Отправить все файлы в одном сообщении
                await SendMessageWithAttachments(e.MessageText, e.Attachments);
            }

            _attachmentPreview.Clear();
        }

        private async Task SendMessageWithAttachments(string text, List<AttachmentPreviewItem> attachments)
        {
            try
            {
                // Определяем получателя
                string recipientId;
                bool isUserId;
                if (_page.IsOpenChatEmpty)
                {
                    recipientId = _page.ChatIdbyUserId.Value.ToString();
                    isUserId = true;
                }
                else
                {
                    recipientId = _page.ChatId.Value;
                    isUserId = false;
                }

                // Create pending message model for UI
                var pendingMessage = new MessageModel
                {
                    Text = text,
                    ChatId = _page.ChatId.Value,
                    SenderId = App.GParam.UserId,
                    SentAt = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTime(DateTime.UtcNow),
                    Attachments = new List<AttachmentsModel>()
                };

                // Создаём временные модели вложений для превью
                foreach (var attachment in attachments)
                {
                    pendingMessage.Attachments.Add(new AttachmentsModel
                    {
                        Type = DetermineAttachmentType(attachment.FileType),
                        FileId = string.Empty, // Will be filled after upload
                        PreviewUrl = attachment.FilePath, // Use local path as preview
                        Size = new FileInfo(attachment.FilePath).Length
                    });
                }

                // Определяем тип сообщения по первому вложению
                var messageType = attachments.Count > 0 ? GetMessageTypeFromAttachment(attachments[0].FileType) : MessageBubble.MessageType.Text;

                // Добавляем разделитель даты при необходимости (ПЕРЕД добавлением сообщения)
                _history.AddDateSeparatorIfNeeded(DateTime.Now);

                // Создаём пузырь сообщения в состоянии ожидания
                var messageControl = new MessageBubble(MessageBubble.MessageOwner.Me, messageType, pendingMessage, _page.IsGroup);

                // Настраиваем элементы загружаемых вложений для отображения индивидуального прогресса
                var localFilePaths = attachments.Select(a => a.FilePath).ToList();
                messageControl.SetupUploadingAttachments(localFilePaths);

                // Немедленно добавляем в UI (показывает загружаемые файлы с прогрессом)
                _history.AddMessage(messageControl);

                // Загружаем файлы и получаем их ID
                var fileIds = new List<string>();
                for (int i = 0; i < attachments.Count; i++)
                {
                    var attachment = attachments[i];

                    // Создаём прогресс-репортер для конкретного вложения
                    var progress = new Progress<double>(percent =>
                    {
                        // Обновляем прогресс конкретного вложения
                        messageControl.UpdateAttachmentProgress(i, percent);
                    });

                    var (error, fileId) = await App.ServerCommunication.UploadFileAsync(
                        App.GParam,
                        attachment.FilePath,
                        attachment.FileType,
                        progress);

                    if (!error.IsSuccess || string.IsNullOrEmpty(fileId))
                    {
                        messageControl.MarkAttachmentFailed(i, error.ErrorMessage ?? "Неизвестная ошибка");
                        App.ErideMessage.AddMessage(
                            $"Ошибка загрузки файла {attachment.FileName}: {error.ErrorMessage}",
                            new Erida { Type = MType.Error });
                        continue;
                    }

                    fileIds.Add(fileId);
                    messageControl.MarkAttachmentUploaded(i, fileId);

                    // Обновляем вложение реальным fileId
                    if (i < pendingMessage.Attachments.Count)
                    {
                        pendingMessage.Attachments[i].FileId = fileId;
                    }

                    // Clean up temp file if from clipboard
                    if (attachment.IsFromClipboard)
                    {
                        try
                        {
                            if (File.Exists(attachment.FilePath))
                                File.Delete(attachment.FilePath);
                        }
                        catch
                        {
                            // Ignore errors deleting temp files
                        }
                    }
                }

                if (fileIds.Count == 0)
                {
                    App.ErideMessage.AddMessage("Не удалось загрузить ни один файл", new Erida { Type = MType.Error });
                    return;
                }

                // Отправляем сообщение с загруженными ID файлов
                (bool, string) type = new(isUserId, recipientId);
                var letter = new ForwardingLetter { Text = text, FilesId = fileIds };
                var response = await App.ServerCommunication.SendMessage(App.GParam, type, letter);

                if (!response.error.IsSuccess)
                {
                    App.ErideMessage.AddMessage(
                        $"Ошибка отправки сообщения: {response.error.ErrorMessage}",
                        new Erida { Type = MType.Error });
                }
                else if (response.message != null)
                {
                    // Обновляем контрол сообщения реальным ID и отмечаем как отправленное
                    messageControl.MessageId = response.message.MessageId.ToString();

                    // Заменяем панель загрузки реальным содержимым
                    messageControl.ReplaceUploadingWithContent(response.message, messageType);

                    // Отмечаем как отправленное (меняет иконку часов на галочку)
                    messageControl.MarkAsSent();

                    // Сохраняем в кеш
                    App.CacheManager.SaveMessage(
                        response.message.ChatId,
                        _page.TitleChat,
                        response.message,
                        MessageOperation.Added);

                    // Обновляем список чатов
                    _chatListCtrl.UpdateChatWithMessage(response.message);
                }
            }
            catch (Exception ex)
            {
                App.ErideMessage.AddMessage($"Ошибка отправки сообщения с вложениями: {ex.Message}", new Erida { Type = MType.Error });
            }
        }

        private static MessageAttachmentType DetermineAttachmentType(Proto.Files.UploadFileType fileType)
        {
            return fileType switch
            {
                Proto.Files.UploadFileType.MessageAttachmentImage => MessageAttachmentType.Image,
                Proto.Files.UploadFileType.MessageAttachmentVideo => MessageAttachmentType.Video,
                Proto.Files.UploadFileType.MessageAttachmentGif => MessageAttachmentType.Gif,
                Proto.Files.UploadFileType.MessageAttachmentDocument => MessageAttachmentType.Document,
                Proto.Files.UploadFileType.MessageAttachmentAudio => MessageAttachmentType.Audio,
                Proto.Files.UploadFileType.MessageAttachmentSticker => MessageAttachmentType.Sticker,
                _ => MessageAttachmentType.Document
            };
        }

        private static MessageBubble.MessageType GetMessageTypeFromAttachment(Proto.Files.UploadFileType fileType)
        {
            return fileType switch
            {
                Proto.Files.UploadFileType.MessageAttachmentImage => MessageBubble.MessageType.Image,
                Proto.Files.UploadFileType.MessageAttachmentVideo => MessageBubble.MessageType.Video,
                Proto.Files.UploadFileType.MessageAttachmentGif => MessageBubble.MessageType.Gif,
                Proto.Files.UploadFileType.MessageAttachmentDocument => MessageBubble.MessageType.Document,
                Proto.Files.UploadFileType.MessageAttachmentAudio => MessageBubble.MessageType.Audio,
                Proto.Files.UploadFileType.MessageAttachmentSticker => MessageBubble.MessageType.Sticker,
                _ => MessageBubble.MessageType.Document
            };
        }

        private void OnPasteCommand(object sender, ExecutedRoutedEventArgs e)
        {
            System.Diagnostics.Debug.WriteLine("=== OnPasteCommand вызван ===");

            // Получаем данные из буфера обмена напрямую
            var clipboard = Clipboard.GetDataObject();
            if (clipboard == null)
            {
                System.Diagnostics.Debug.WriteLine("Буфер обмена пуст");
                return;
            }

            System.Diagnostics.Debug.WriteLine($"FileDrop present: {clipboard.GetDataPresent(DataFormats.FileDrop)}");
            System.Diagnostics.Debug.WriteLine($"Bitmap present: {clipboard.GetDataPresent(DataFormats.Bitmap)}");

            // Выводим все доступные форматы
            var formats = clipboard.GetFormats();
            System.Diagnostics.Debug.WriteLine($"Доступные форматы: {string.Join(", ", formats)}");

            if (clipboard.GetDataPresent(DataFormats.FileDrop))
            {
                // Файлы, вставленные из Проводника
                System.Diagnostics.Debug.WriteLine("Обработка FileDrop");
                e.Handled = true; // Предотвращаем стандартную обработку
                var files = (string[])clipboard.GetData(DataFormats.FileDrop);
                if (files != null && files.Length > 0)
                {
                    System.Diagnostics.Debug.WriteLine($"Найдено файлов: {files.Length}");
                    ShowAttachmentPreview(files.ToList());
                }
            }
            else if (clipboard.GetDataPresent(DataFormats.Bitmap))
            {
                // Изображение, вставленное из буфера обмена (скриншот)
                System.Diagnostics.Debug.WriteLine("Обработка Bitmap");
                e.Handled = true; // Предотвращаем стандартную обработку
                var image = Clipboard.GetImage();
                if (image != null)
                {
                    System.Diagnostics.Debug.WriteLine("Изображение получено");
                    ShowAttachmentPreviewWithText(() =>
                    {
                        _attachmentPreview.AddImageFromClipboard(image);
                    });
                }
            }
            else if (clipboard.GetDataPresent(DataFormats.Text) || clipboard.GetDataPresent(DataFormats.UnicodeText))
            {
                // Обычный текст - выполняем вставку вручную
                System.Diagnostics.Debug.WriteLine("Обработка обычного текста - ручная вставка");
                var text = Clipboard.GetText();
                if (!string.IsNullOrEmpty(text))
                {
                    // Получаем текущую позицию каретки и выделение
                    var textBox = _textForMessage;
                    int selectionStart = textBox.SelectionStart;
                    int selectionLength = textBox.SelectionLength;
                    string currentText = textBox.Text ?? string.Empty;

                    // Вставляем текст с заменой выделенного фрагмента
                    string newText = currentText.Substring(0, selectionStart) + text + currentText.Substring(selectionStart + selectionLength);
                    textBox.Text = newText;

                    // Устанавливаем каретку после вставленного текста
                    textBox.SelectionStart = selectionStart + text.Length;
                    textBox.SelectionLength = 0;
                }
                e.Handled = true;
            }
        }

        public void OnDragEnter(DragEventArgs e)
        {
            System.Diagnostics.Debug.WriteLine("=== DragEnter ===");

            // Проверяем, содержит ли перетаскиваемый объект файлы
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                System.Diagnostics.Debug.WriteLine("FileDrop detected in drag");
                e.Effects = DragDropEffects.Copy;
                _dragDropOverlay.Visibility = Visibility.Visible;
            }
            else
            {
                e.Effects = DragDropEffects.None;
            }

            e.Handled = true;
        }

        public void OnDragOver(DragEventArgs e)
        {
            // Проверяем, содержит ли перетаскиваемый объект файлы
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effects = DragDropEffects.Copy;
            }
            else
            {
                e.Effects = DragDropEffects.None;
            }

            e.Handled = true;
        }

        public void OnDragLeave(DragEventArgs e)
        {
            System.Diagnostics.Debug.WriteLine("=== DragLeave ===");
            _dragDropOverlay.Visibility = Visibility.Collapsed;
            e.Handled = true;
        }

        public void OnDrop(DragEventArgs e)
        {
            System.Diagnostics.Debug.WriteLine("=== Drop ===");

            // Скрываем визуальный индикатор
            _dragDropOverlay.Visibility = Visibility.Collapsed;

            // Проверяем, содержит ли перетаскиваемый объект файлы
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                var files = (string[])e.Data.GetData(DataFormats.FileDrop);
                if (files != null && files.Length > 0)
                {
                    System.Diagnostics.Debug.WriteLine($"Dropped {files.Length} файлов");
                    ShowAttachmentPreview(files.ToList());
                }
            }

            e.Handled = true;
        }
    }
}
