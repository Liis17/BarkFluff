using BarkFluff.WebApi.Core.MessengerData.NonSavedData;

using System.Windows;
using System.Windows.Controls;

namespace BarkFluff.Client.WPF.UserControls.MessageContent
{
    /// <summary>
    /// Orchestrator control for displaying multiple images in an adaptive grid layout
    /// Delegates row rendering to ImageRow components
    /// </summary>
    public partial class MultiImageGrid : UserControl
    {
        private const int IMAGE_MAX_WIDTH = 400;
        private const int IMAGE_SPACING = 2;

        private List<AttachmentsModel> _attachments = new List<AttachmentsModel>();

        public MultiImageGrid()
        {
            InitializeComponent();
        }

        public void SetImages(List<AttachmentsModel> attachments)
        {
            if (attachments == null || attachments.Count == 0)
                return;

            // Ограничить до 10 картинок максимум
            _attachments = attachments.Count > 10
                ? attachments.Take(10).ToList()
                : attachments;

            BuildImageGrid();
        }

        private void BuildImageGrid()
        {
            ImageStack.Children.Clear();
            this.MinHeight = 0;

            var rowsDistribution = DetermineLayout(_attachments.Count);
            int imageIndex = 0;

            for (int rowIndex = 0; rowIndex < rowsDistribution.Count; rowIndex++)
            {
                int imagesInRow = rowsDistribution[rowIndex];
                var rowImages = _attachments.Skip(imageIndex).Take(imagesInRow).ToList();

                var imageRow = new ImageRow();
                imageRow.SetImages(rowImages, isFirstRow: rowIndex == 0, allAttachments: _attachments, globalOffset: imageIndex);

                // Добавить отступ между рядами (2px)
                if (rowIndex > 0)
                {
                    imageRow.Margin = new Thickness(0, IMAGE_SPACING, 0, 0);
                }

                ImageStack.Children.Add(imageRow);
                imageIndex += imagesInRow;
            }
        }

        /// <summary>
        /// Определяет распределение картинок по рядам
        /// </summary>
        /// <param name="totalImages">Общее количество картинок (1-10)</param>
        /// <returns>Список количества картинок в каждом ряду</returns>
        private List<int> DetermineLayout(int totalImages)
        {
            return totalImages switch
            {
                1 => new List<int> { 1 },              // [1]
                2 => new List<int> { 2 },              // [2]
                3 => new List<int> { 2, 1 },           // [2, 1]
                4 => new List<int> { 2, 2 },           // [2, 2]
                5 => new List<int> { 3, 2 },           // [3, 2]
                6 => new List<int> { 3, 3 },           // [3, 3]
                7 => new List<int> { 3, 2, 2 },        // [3, 2, 2]
                8 => new List<int> { 3, 3, 2 },        // [3, 3, 2]
                9 => new List<int> { 3, 3, 3 },        // [3, 3, 3]
                10 => new List<int> { 3, 2, 2, 3 },    // [3, 2, 2, 3]
                _ => new List<int> { 1 }               // Fallback
            };
        }
    }
}
