using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;

using BarkFluff.WebApi.Core.MessengerData.NonSavedData;

namespace BarkFluff.Client.WPF.UserControls.MessageContent
{
    /// <summary>
    /// Control for displaying multiple documents as a vertical list
    /// </summary>
    public partial class MultiDocumentList : UserControl
    {
        public MultiDocumentList()
        {
            InitializeComponent();
        }

        public void SetAttachments(List<AttachmentsModel> attachments)
        {
            if (attachments == null || attachments.Count == 0)
                return;

            DocumentStack.Children.Clear();

            foreach (var attachment in attachments)
            {
                // Передаем тип вложения для правильной иконки
                var documentItem = new DocumentMessageContent(
                    attachment.FileId,
                    attachment.PreviewUrl,
                    attachment.Size,
                    attachment.Type);  // ← НОВЫЙ параметр

                documentItem.Margin = new Thickness(0, 0, 0, 4);
                DocumentStack.Children.Add(documentItem);
            }

            // Убрать отступ у последнего элемента
            if (DocumentStack.Children.Count > 0)
            {
                var lastItem = DocumentStack.Children[DocumentStack.Children.Count - 1] as FrameworkElement;
                if (lastItem != null)
                {
                    lastItem.Margin = new Thickness(0);
                }
            }
        }

        [Obsolete("Use SetAttachments instead")]
        public void SetDocuments(List<AttachmentsModel> attachments)
        {
            SetAttachments(attachments);
        }
    }
}
