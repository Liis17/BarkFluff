using BarkFluff.WebApi.Core.MessengerData.NonSavedData;

using System.Windows;
using System.Windows.Controls;

namespace BarkFluff.Client.WPF.UserControls.MessageContent
{
    /// <summary>
    /// Orchestrator control for displaying multiple videos in an adaptive grid layout
    /// Delegates row rendering to VideoRow components
    /// </summary>
    public partial class MultiVideoGrid : UserControl
    {
        private const int VIDEO_MAX_WIDTH = 400;
        private const int VIDEO_SPACING = 2;

        private List<AttachmentsModel> _attachments = new List<AttachmentsModel>();

        public MultiVideoGrid()
        {
            InitializeComponent();
        }

        public void SetVideos(List<AttachmentsModel> attachments)
        {
            if (attachments == null || attachments.Count == 0)
                return;

            // Ограничить до 10 видео максимум
            _attachments = attachments.Count > 10
                ? attachments.Take(10).ToList()
                : attachments;

            BuildVideoGrid();
        }

        private void BuildVideoGrid()
        {
            VideoStack.Children.Clear();

            var rowsDistribution = DetermineLayout(_attachments.Count);
            int videoIndex = 0;

            for (int rowIndex = 0; rowIndex < rowsDistribution.Count; rowIndex++)
            {
                int videosInRow = rowsDistribution[rowIndex];
                var rowVideos = _attachments.Skip(videoIndex).Take(videosInRow).ToList();

                var videoRow = new VideoRow();
                videoRow.SetVideos(rowVideos, isFirstRow: rowIndex == 0);

                // Добавить отступ между рядами (2px)
                if (rowIndex > 0)
                {
                    videoRow.Margin = new Thickness(0, VIDEO_SPACING, 0, 0);
                }

                VideoStack.Children.Add(videoRow);
                videoIndex += videosInRow;
            }
        }

        /// <summary>
        /// Определяет распределение видео по рядам
        /// ТОЧНО ТАКИЕ ЖЕ layouts как у MultiImageGrid
        /// </summary>
        /// <param name="totalVideos">Общее количество видео (1-10)</param>
        /// <returns>Список количества видео в каждом ряду</returns>
        private List<int> DetermineLayout(int totalVideos)
        {
            return totalVideos switch
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
