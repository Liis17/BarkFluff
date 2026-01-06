using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

using BarkFluff.Client.WPF.Services.App.Caching;
using BarkFluff.WebApi.Core.MessengerData.NonSavedData;

namespace BarkFluff.Client.WPF.UserControls.MessageContent
{
    /// <summary>
    /// Control for displaying multiple images in an adaptive grid layout (Telegram-style)
    /// </summary>
    public partial class MultiImageGrid : UserControl
    {
        private const int IMAGE_MAX_WIDTH = 400;
        private const int IMAGE_MAX_HEIGHT = 300;
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
            if (attachments.Count > 10)
            {
                _attachments = attachments.Take(10).ToList();
            }
            else
            {
                _attachments = attachments;
            }

            BuildImageGrid();
        }

        /// <summary>
        /// Вычисляет размеры одной картинки для ряда с заданным количеством картинок
        /// Соотношение сторон всегда 16:9
        /// </summary>
        private (int width, int height) CalculateImageSize(int imagesInRow)
        {
            // Вычисляем доступную ширину на одну картинку с учетом spacing
            int totalSpacing = (imagesInRow - 1) * IMAGE_SPACING;
            int width = (IMAGE_MAX_WIDTH - totalSpacing) / imagesInRow;

            // Вычисляем высоту с соотношением 16:9
            int height = (int)(width * 9.0 / 16.0);

            return (width, height);
        }

        private void BuildImageGrid()
        {
            ImageGrid.Children.Clear();
            ImageGrid.RowDefinitions.Clear();
            ImageGrid.ColumnDefinitions.Clear();

            int count = _attachments.Count;

            if (count == 1)
            {
                // Single image - full width
                CreateSingleImageLayout();
            }
            else if (count == 2)
            {
                // Two images side by side
                CreateTwoImageLayout();
            }
            else if (count == 3)
            {
                // First image large on top, two smaller below
                CreateThreeImageLayout();
            }
            else
            {
                // 4+ images - 2xN grid
                CreateMultiImageLayout();
            }
        }

        private void CreateSingleImageLayout()
        {
            ImageGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            ImageGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            // Single image - round top corners only
            var (width, height) = CalculateImageSize(1);
            var cornerRadius = new CornerRadius(18, 18, 0, 0);
            var image = CreateImageBorder(_attachments[0], width, height, cornerRadius);
            Grid.SetRow(image, 0);
            Grid.SetColumn(image, 0);
            ImageGrid.Children.Add(image);
        }

        private void CreateTwoImageLayout()
        {
            ImageGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            ImageGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            ImageGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var (width, height) = CalculateImageSize(2);

            // Left image - round top-left corner only
            var cornerRadiusLeft = new CornerRadius(18, 0, 0, 0);
            var image1 = CreateImageBorder(_attachments[0], width, height, cornerRadiusLeft);
            Grid.SetRow(image1, 0);
            Grid.SetColumn(image1, 0);
            image1.Margin = new Thickness(0, 0, IMAGE_SPACING / 2, 0);
            ImageGrid.Children.Add(image1);

            // Right image - round top-right corner only
            var cornerRadiusRight = new CornerRadius(0, 18, 0, 0);
            var image2 = CreateImageBorder(_attachments[1], width, height, cornerRadiusRight);
            Grid.SetRow(image2, 0);
            Grid.SetColumn(image2, 1);
            image2.Margin = new Thickness(IMAGE_SPACING / 2, 0, 0, 0);
            ImageGrid.Children.Add(image2);
        }

        private void CreateThreeImageLayout()
        {
            // 2 ряда
            ImageGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            ImageGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // 2 колонки
            ImageGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            ImageGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            // Первый ряд (row 0): 2 картинки
            var (widthRow0, heightRow0) = CalculateImageSize(2);
            var cornerRadiusLeft = new CornerRadius(18, 0, 0, 0);
            var image1 = CreateImageBorder(_attachments[0], widthRow0, heightRow0, cornerRadiusLeft);
            Grid.SetRow(image1, 0);
            Grid.SetColumn(image1, 0);
            image1.Margin = new Thickness(0, 0, IMAGE_SPACING / 2, IMAGE_SPACING);
            ImageGrid.Children.Add(image1);

            var cornerRadiusRight = new CornerRadius(0, 18, 0, 0);
            var image2 = CreateImageBorder(_attachments[1], widthRow0, heightRow0, cornerRadiusRight);
            Grid.SetRow(image2, 0);
            Grid.SetColumn(image2, 1);
            image2.Margin = new Thickness(IMAGE_SPACING / 2, 0, 0, IMAGE_SPACING);
            ImageGrid.Children.Add(image2);

            // Второй ряд (row 1): 1 картинка на всю ширину
            var (widthRow1, heightRow1) = CalculateImageSize(1);
            var cornerRadiusNone = new CornerRadius(0);
            var image3 = CreateImageBorder(_attachments[2], widthRow1, heightRow1, cornerRadiusNone);
            Grid.SetRow(image3, 1);
            Grid.SetColumn(image3, 0);
            Grid.SetColumnSpan(image3, 2);
            ImageGrid.Children.Add(image3);
        }

        private void CreateMultiImageLayout()
        {
            int count = _attachments.Count;

            switch (count)
            {
                case 4:
                    CreateFourImageLayout();
                    break;
                case 5:
                    CreateFiveImageLayout();
                    break;
                case 6:
                    CreateSixImageLayout();
                    break;
                case 7:
                    CreateSevenImageLayout();
                    break;
                case 8:
                    CreateEightImageLayout();
                    break;
                case 9:
                    CreateNineImageLayout();
                    break;
                case 10:
                    CreateTenImageLayout();
                    break;
                default:
                    // Не должно происходить, так как ограничено 10 картинками в SetImages
                    break;
            }
        }

        private void CreateFourImageLayout()
        {
            // Сетка 2x2
            ImageGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            ImageGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            ImageGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            ImageGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var (width, height) = CalculateImageSize(2);

            // Первый ряд (row 0)
            var cornerRadiusTopLeft = new CornerRadius(18, 0, 0, 0);
            var image1 = CreateImageBorder(_attachments[0], width, height, cornerRadiusTopLeft);
            Grid.SetRow(image1, 0);
            Grid.SetColumn(image1, 0);
            image1.Margin = new Thickness(0, 0, IMAGE_SPACING / 2, IMAGE_SPACING);
            ImageGrid.Children.Add(image1);

            var cornerRadiusTopRight = new CornerRadius(0, 18, 0, 0);
            var image2 = CreateImageBorder(_attachments[1], width, height, cornerRadiusTopRight);
            Grid.SetRow(image2, 0);
            Grid.SetColumn(image2, 1);
            image2.Margin = new Thickness(IMAGE_SPACING / 2, 0, 0, IMAGE_SPACING);
            ImageGrid.Children.Add(image2);

            // Второй ряд (row 1)
            var cornerRadiusNone = new CornerRadius(0);
            var image3 = CreateImageBorder(_attachments[2], width, height, cornerRadiusNone);
            Grid.SetRow(image3, 1);
            Grid.SetColumn(image3, 0);
            image3.Margin = new Thickness(0, 0, IMAGE_SPACING / 2, 0);
            ImageGrid.Children.Add(image3);

            var image4 = CreateImageBorder(_attachments[3], width, height, cornerRadiusNone);
            Grid.SetRow(image4, 1);
            Grid.SetColumn(image4, 1);
            image4.Margin = new Thickness(IMAGE_SPACING / 2, 0, 0, 0);
            ImageGrid.Children.Add(image4);
        }

        private void CreateFiveImageLayout()
        {
            // Первый ряд: 3 картинки, второй ряд: 2 картинки
            ImageGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            ImageGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            ImageGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            ImageGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            ImageGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            // Первый ряд (row 0): 3 картинки
            var (widthRow0, heightRow0) = CalculateImageSize(3);
            var cornerRadiusTopLeft = new CornerRadius(18, 0, 0, 0);
            var image1 = CreateImageBorder(_attachments[0], widthRow0, heightRow0, cornerRadiusTopLeft);
            Grid.SetRow(image1, 0);
            Grid.SetColumn(image1, 0);
            image1.Margin = new Thickness(0, 0, IMAGE_SPACING / 2, IMAGE_SPACING);
            ImageGrid.Children.Add(image1);

            var cornerRadiusNone = new CornerRadius(0);
            var image2 = CreateImageBorder(_attachments[1], widthRow0, heightRow0, cornerRadiusNone);
            Grid.SetRow(image2, 0);
            Grid.SetColumn(image2, 1);
            image2.Margin = new Thickness(IMAGE_SPACING / 2, 0, IMAGE_SPACING / 2, IMAGE_SPACING);
            ImageGrid.Children.Add(image2);

            var cornerRadiusTopRight = new CornerRadius(0, 18, 0, 0);
            var image3 = CreateImageBorder(_attachments[2], widthRow0, heightRow0, cornerRadiusTopRight);
            Grid.SetRow(image3, 0);
            Grid.SetColumn(image3, 2);
            image3.Margin = new Thickness(IMAGE_SPACING / 2, 0, 0, IMAGE_SPACING);
            ImageGrid.Children.Add(image3);

            // Второй ряд (row 1): 2 картинки
            var (widthRow1, heightRow1) = CalculateImageSize(2);
            var image4 = CreateImageBorder(_attachments[3], widthRow1, heightRow1, cornerRadiusNone);
            Grid.SetRow(image4, 1);
            Grid.SetColumn(image4, 0);
            Grid.SetColumnSpan(image4, 2);
            image4.Margin = new Thickness(0, 0, IMAGE_SPACING / 2, 0);
            ImageGrid.Children.Add(image4);

            var image5 = CreateImageBorder(_attachments[4], widthRow1, heightRow1, cornerRadiusNone);
            Grid.SetRow(image5, 1);
            Grid.SetColumn(image5, 2);
            image5.Margin = new Thickness(IMAGE_SPACING / 2, 0, 0, 0);
            ImageGrid.Children.Add(image5);
        }

        private void CreateSixImageLayout()
        {
            // Сетка 3x2
            ImageGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            ImageGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            ImageGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            ImageGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            ImageGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var (width, height) = CalculateImageSize(3);

            // Первый ряд (row 0): 3 картинки
            var cornerRadiusTopLeft = new CornerRadius(18, 0, 0, 0);
            var image1 = CreateImageBorder(_attachments[0], width, height, cornerRadiusTopLeft);
            Grid.SetRow(image1, 0);
            Grid.SetColumn(image1, 0);
            image1.Margin = new Thickness(0, 0, IMAGE_SPACING / 2, IMAGE_SPACING);
            ImageGrid.Children.Add(image1);

            var cornerRadiusNone = new CornerRadius(0);
            var image2 = CreateImageBorder(_attachments[1], width, height, cornerRadiusNone);
            Grid.SetRow(image2, 0);
            Grid.SetColumn(image2, 1);
            image2.Margin = new Thickness(IMAGE_SPACING / 2, 0, IMAGE_SPACING / 2, IMAGE_SPACING);
            ImageGrid.Children.Add(image2);

            var cornerRadiusTopRight = new CornerRadius(0, 18, 0, 0);
            var image3 = CreateImageBorder(_attachments[2], width, height, cornerRadiusTopRight);
            Grid.SetRow(image3, 0);
            Grid.SetColumn(image3, 2);
            image3.Margin = new Thickness(IMAGE_SPACING / 2, 0, 0, IMAGE_SPACING);
            ImageGrid.Children.Add(image3);

            // Второй ряд (row 1): 3 картинки
            var image4 = CreateImageBorder(_attachments[3], width, height, cornerRadiusNone);
            Grid.SetRow(image4, 1);
            Grid.SetColumn(image4, 0);
            image4.Margin = new Thickness(0, 0, IMAGE_SPACING / 2, 0);
            ImageGrid.Children.Add(image4);

            var image5 = CreateImageBorder(_attachments[4], width, height, cornerRadiusNone);
            Grid.SetRow(image5, 1);
            Grid.SetColumn(image5, 1);
            image5.Margin = new Thickness(IMAGE_SPACING / 2, 0, IMAGE_SPACING / 2, 0);
            ImageGrid.Children.Add(image5);

            var image6 = CreateImageBorder(_attachments[5], width, height, cornerRadiusNone);
            Grid.SetRow(image6, 1);
            Grid.SetColumn(image6, 2);
            image6.Margin = new Thickness(IMAGE_SPACING / 2, 0, 0, 0);
            ImageGrid.Children.Add(image6);
        }

        private void CreateSevenImageLayout()
        {
            // Первый ряд: 3 картинки, второй ряд: 2 картинки, третий ряд: 2 картинки
            ImageGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            ImageGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            ImageGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            ImageGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            ImageGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            ImageGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            // Первый ряд (row 0): 3 картинки
            var (widthRow0, heightRow0) = CalculateImageSize(3);
            var cornerRadiusTopLeft = new CornerRadius(18, 0, 0, 0);
            var image1 = CreateImageBorder(_attachments[0], widthRow0, heightRow0, cornerRadiusTopLeft);
            Grid.SetRow(image1, 0);
            Grid.SetColumn(image1, 0);
            image1.Margin = new Thickness(0, 0, IMAGE_SPACING / 2, IMAGE_SPACING);
            ImageGrid.Children.Add(image1);

            var cornerRadiusNone = new CornerRadius(0);
            var image2 = CreateImageBorder(_attachments[1], widthRow0, heightRow0, cornerRadiusNone);
            Grid.SetRow(image2, 0);
            Grid.SetColumn(image2, 1);
            image2.Margin = new Thickness(IMAGE_SPACING / 2, 0, IMAGE_SPACING / 2, IMAGE_SPACING);
            ImageGrid.Children.Add(image2);

            var cornerRadiusTopRight = new CornerRadius(0, 18, 0, 0);
            var image3 = CreateImageBorder(_attachments[2], widthRow0, heightRow0, cornerRadiusTopRight);
            Grid.SetRow(image3, 0);
            Grid.SetColumn(image3, 2);
            image3.Margin = new Thickness(IMAGE_SPACING / 2, 0, 0, IMAGE_SPACING);
            ImageGrid.Children.Add(image3);

            // Второй ряд (row 1): 2 картинки
            var (widthRow1, heightRow1) = CalculateImageSize(2);
            var image4 = CreateImageBorder(_attachments[3], widthRow1, heightRow1, cornerRadiusNone);
            Grid.SetRow(image4, 1);
            Grid.SetColumn(image4, 0);
            Grid.SetColumnSpan(image4, 2);
            image4.Margin = new Thickness(0, 0, IMAGE_SPACING / 2, IMAGE_SPACING);
            ImageGrid.Children.Add(image4);

            var image5 = CreateImageBorder(_attachments[4], widthRow1, heightRow1, cornerRadiusNone);
            Grid.SetRow(image5, 1);
            Grid.SetColumn(image5, 2);
            image5.Margin = new Thickness(IMAGE_SPACING / 2, 0, 0, IMAGE_SPACING);
            ImageGrid.Children.Add(image5);

            // Третий ряд (row 2): 2 картинки
            var (widthRow2, heightRow2) = CalculateImageSize(2);
            var image6 = CreateImageBorder(_attachments[5], widthRow2, heightRow2, cornerRadiusNone);
            Grid.SetRow(image6, 2);
            Grid.SetColumn(image6, 0);
            Grid.SetColumnSpan(image6, 2);
            image6.Margin = new Thickness(0, 0, IMAGE_SPACING / 2, 0);
            ImageGrid.Children.Add(image6);

            var image7 = CreateImageBorder(_attachments[6], widthRow2, heightRow2, cornerRadiusNone);
            Grid.SetRow(image7, 2);
            Grid.SetColumn(image7, 2);
            image7.Margin = new Thickness(IMAGE_SPACING / 2, 0, 0, 0);
            ImageGrid.Children.Add(image7);
        }

        private void CreateEightImageLayout()
        {
            // Первый ряд: 3 картинки, второй ряд: 3 картинки, третий ряд: 2 картинки
            ImageGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            ImageGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            ImageGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            ImageGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            ImageGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            ImageGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var (widthRow03, heightRow03) = CalculateImageSize(3);
            var (widthRow12, heightRow12) = CalculateImageSize(2);

            // Первый ряд (row 0): 3 картинки
            var cornerRadiusTopLeft = new CornerRadius(18, 0, 0, 0);
            var image1 = CreateImageBorder(_attachments[0], widthRow03, heightRow03, cornerRadiusTopLeft);
            Grid.SetRow(image1, 0);
            Grid.SetColumn(image1, 0);
            image1.Margin = new Thickness(0, 0, IMAGE_SPACING / 2, IMAGE_SPACING);
            ImageGrid.Children.Add(image1);

            var cornerRadiusNone = new CornerRadius(0);
            var image2 = CreateImageBorder(_attachments[1], widthRow03, heightRow03, cornerRadiusNone);
            Grid.SetRow(image2, 0);
            Grid.SetColumn(image2, 1);
            image2.Margin = new Thickness(IMAGE_SPACING / 2, 0, IMAGE_SPACING / 2, IMAGE_SPACING);
            ImageGrid.Children.Add(image2);

            var cornerRadiusTopRight = new CornerRadius(0, 18, 0, 0);
            var image3 = CreateImageBorder(_attachments[2], widthRow03, heightRow03, cornerRadiusTopRight);
            Grid.SetRow(image3, 0);
            Grid.SetColumn(image3, 2);
            image3.Margin = new Thickness(IMAGE_SPACING / 2, 0, 0, IMAGE_SPACING);
            ImageGrid.Children.Add(image3);

            // Второй ряд (row 1): 3 картинки
            var image4 = CreateImageBorder(_attachments[3], widthRow03, heightRow03, cornerRadiusNone);
            Grid.SetRow(image4, 1);
            Grid.SetColumn(image4, 0);
            image4.Margin = new Thickness(0, 0, IMAGE_SPACING / 2, IMAGE_SPACING);
            ImageGrid.Children.Add(image4);

            var image5 = CreateImageBorder(_attachments[4], widthRow03, heightRow03, cornerRadiusNone);
            Grid.SetRow(image5, 1);
            Grid.SetColumn(image5, 1);
            image5.Margin = new Thickness(IMAGE_SPACING / 2, 0, IMAGE_SPACING / 2, IMAGE_SPACING);
            ImageGrid.Children.Add(image5);

            var image6 = CreateImageBorder(_attachments[5], widthRow03, heightRow03, cornerRadiusNone);
            Grid.SetRow(image6, 1);
            Grid.SetColumn(image6, 2);
            image6.Margin = new Thickness(IMAGE_SPACING / 2, 0, 0, IMAGE_SPACING);
            ImageGrid.Children.Add(image6);

            // Третий ряд (row 2): 2 картинки
            var image7 = CreateImageBorder(_attachments[6], widthRow12, heightRow12, cornerRadiusNone);
            Grid.SetRow(image7, 2);
            Grid.SetColumn(image7, 0);
            Grid.SetColumnSpan(image7, 2);
            image7.Margin = new Thickness(0, 0, IMAGE_SPACING / 2, 0);
            ImageGrid.Children.Add(image7);

            var image8 = CreateImageBorder(_attachments[7], widthRow12, heightRow12, cornerRadiusNone);
            Grid.SetRow(image8, 2);
            Grid.SetColumn(image8, 2);
            image8.Margin = new Thickness(IMAGE_SPACING / 2, 0, 0, 0);
            ImageGrid.Children.Add(image8);
        }

        private void CreateNineImageLayout()
        {
            // Сетка 3x3
            ImageGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            ImageGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            ImageGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            ImageGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            ImageGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            ImageGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var (width, height) = CalculateImageSize(3);

            // Первый ряд (row 0): 3 картинки
            var cornerRadiusTopLeft = new CornerRadius(18, 0, 0, 0);
            var image1 = CreateImageBorder(_attachments[0], width, height, cornerRadiusTopLeft);
            Grid.SetRow(image1, 0);
            Grid.SetColumn(image1, 0);
            image1.Margin = new Thickness(0, 0, IMAGE_SPACING / 2, IMAGE_SPACING);
            ImageGrid.Children.Add(image1);

            var cornerRadiusNone = new CornerRadius(0);
            var image2 = CreateImageBorder(_attachments[1], width, height, cornerRadiusNone);
            Grid.SetRow(image2, 0);
            Grid.SetColumn(image2, 1);
            image2.Margin = new Thickness(IMAGE_SPACING / 2, 0, IMAGE_SPACING / 2, IMAGE_SPACING);
            ImageGrid.Children.Add(image2);

            var cornerRadiusTopRight = new CornerRadius(0, 18, 0, 0);
            var image3 = CreateImageBorder(_attachments[2], width, height, cornerRadiusTopRight);
            Grid.SetRow(image3, 0);
            Grid.SetColumn(image3, 2);
            image3.Margin = new Thickness(IMAGE_SPACING / 2, 0, 0, IMAGE_SPACING);
            ImageGrid.Children.Add(image3);

            // Второй ряд (row 1): 3 картинки
            var image4 = CreateImageBorder(_attachments[3], width, height, cornerRadiusNone);
            Grid.SetRow(image4, 1);
            Grid.SetColumn(image4, 0);
            image4.Margin = new Thickness(0, 0, IMAGE_SPACING / 2, IMAGE_SPACING);
            ImageGrid.Children.Add(image4);

            var image5 = CreateImageBorder(_attachments[4], width, height, cornerRadiusNone);
            Grid.SetRow(image5, 1);
            Grid.SetColumn(image5, 1);
            image5.Margin = new Thickness(IMAGE_SPACING / 2, 0, IMAGE_SPACING / 2, IMAGE_SPACING);
            ImageGrid.Children.Add(image5);

            var image6 = CreateImageBorder(_attachments[5], width, height, cornerRadiusNone);
            Grid.SetRow(image6, 1);
            Grid.SetColumn(image6, 2);
            image6.Margin = new Thickness(IMAGE_SPACING / 2, 0, 0, IMAGE_SPACING);
            ImageGrid.Children.Add(image6);

            // Третий ряд (row 2): 3 картинки
            var image7 = CreateImageBorder(_attachments[6], width, height, cornerRadiusNone);
            Grid.SetRow(image7, 2);
            Grid.SetColumn(image7, 0);
            image7.Margin = new Thickness(0, 0, IMAGE_SPACING / 2, 0);
            ImageGrid.Children.Add(image7);

            var image8 = CreateImageBorder(_attachments[7], width, height, cornerRadiusNone);
            Grid.SetRow(image8, 2);
            Grid.SetColumn(image8, 1);
            image8.Margin = new Thickness(IMAGE_SPACING / 2, 0, IMAGE_SPACING / 2, 0);
            ImageGrid.Children.Add(image8);

            var image9 = CreateImageBorder(_attachments[8], width, height, cornerRadiusNone);
            Grid.SetRow(image9, 2);
            Grid.SetColumn(image9, 2);
            image9.Margin = new Thickness(IMAGE_SPACING / 2, 0, 0, 0);
            ImageGrid.Children.Add(image9);
        }

        private void CreateTenImageLayout()
        {
            // Первый ряд: 3 картинки, второй ряд: 2 картинки, третий ряд: 2 картинки, четвертый ряд: 3 картинки
            ImageGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            ImageGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            ImageGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            ImageGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            ImageGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            ImageGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            ImageGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var (width3, height3) = CalculateImageSize(3);
            var (width2, height2) = CalculateImageSize(2);

            // Первый ряд (row 0): 3 картинки
            var cornerRadiusTopLeft = new CornerRadius(18, 0, 0, 0);
            var image1 = CreateImageBorder(_attachments[0], width3, height3, cornerRadiusTopLeft);
            Grid.SetRow(image1, 0);
            Grid.SetColumn(image1, 0);
            image1.Margin = new Thickness(0, 0, IMAGE_SPACING / 2, IMAGE_SPACING);
            ImageGrid.Children.Add(image1);

            var cornerRadiusNone = new CornerRadius(0);
            var image2 = CreateImageBorder(_attachments[1], width3, height3, cornerRadiusNone);
            Grid.SetRow(image2, 0);
            Grid.SetColumn(image2, 1);
            image2.Margin = new Thickness(IMAGE_SPACING / 2, 0, IMAGE_SPACING / 2, IMAGE_SPACING);
            ImageGrid.Children.Add(image2);

            var cornerRadiusTopRight = new CornerRadius(0, 18, 0, 0);
            var image3 = CreateImageBorder(_attachments[2], width3, height3, cornerRadiusTopRight);
            Grid.SetRow(image3, 0);
            Grid.SetColumn(image3, 2);
            image3.Margin = new Thickness(IMAGE_SPACING / 2, 0, 0, IMAGE_SPACING);
            ImageGrid.Children.Add(image3);

            // Второй ряд (row 1): 2 картинки
            var image4 = CreateImageBorder(_attachments[3], width2, height2, cornerRadiusNone);
            Grid.SetRow(image4, 1);
            Grid.SetColumn(image4, 0);
            Grid.SetColumnSpan(image4, 2);
            image4.Margin = new Thickness(0, 0, IMAGE_SPACING / 2, IMAGE_SPACING);
            ImageGrid.Children.Add(image4);

            var image5 = CreateImageBorder(_attachments[4], width2, height2, cornerRadiusNone);
            Grid.SetRow(image5, 1);
            Grid.SetColumn(image5, 2);
            image5.Margin = new Thickness(IMAGE_SPACING / 2, 0, 0, IMAGE_SPACING);
            ImageGrid.Children.Add(image5);

            // Третий ряд (row 2): 2 картинки
            var image6 = CreateImageBorder(_attachments[5], width2, height2, cornerRadiusNone);
            Grid.SetRow(image6, 2);
            Grid.SetColumn(image6, 0);
            Grid.SetColumnSpan(image6, 2);
            image6.Margin = new Thickness(0, 0, IMAGE_SPACING / 2, IMAGE_SPACING);
            ImageGrid.Children.Add(image6);

            var image7 = CreateImageBorder(_attachments[6], width2, height2, cornerRadiusNone);
            Grid.SetRow(image7, 2);
            Grid.SetColumn(image7, 2);
            image7.Margin = new Thickness(IMAGE_SPACING / 2, 0, 0, IMAGE_SPACING);
            ImageGrid.Children.Add(image7);

            // Четвертый ряд (row 3): 3 картинки
            var image8 = CreateImageBorder(_attachments[7], width3, height3, cornerRadiusNone);
            Grid.SetRow(image8, 3);
            Grid.SetColumn(image8, 0);
            image8.Margin = new Thickness(0, 0, IMAGE_SPACING / 2, 0);
            ImageGrid.Children.Add(image8);

            var image9 = CreateImageBorder(_attachments[8], width3, height3, cornerRadiusNone);
            Grid.SetRow(image9, 3);
            Grid.SetColumn(image9, 1);
            image9.Margin = new Thickness(IMAGE_SPACING / 2, 0, IMAGE_SPACING / 2, 0);
            ImageGrid.Children.Add(image9);

            var image10 = CreateImageBorder(_attachments[9], width3, height3, cornerRadiusNone);
            Grid.SetRow(image10, 3);
            Grid.SetColumn(image10, 2);
            image10.Margin = new Thickness(IMAGE_SPACING / 2, 0, 0, 0);
            ImageGrid.Children.Add(image10);
        }

        private Border CreateImageBorder(AttachmentsModel attachment, int width, int height, CornerRadius cornerRadius)
        {
            var border = new Border
            {
                Width = width,
                Height = height,
                ClipToBounds = true,
                Cursor = Cursors.Hand,
                Background = Brushes.Transparent
            };

            // Determine file type and file ID
            var fileType = attachment.Type == Proto.Shared.MessageAttachmentType.Gif ? FileType.Gif : FileType.Image;
            var fileId = !string.IsNullOrEmpty(attachment.PreviewFileId) ? attachment.PreviewFileId : attachment.FileId;

            // Create CachedImage control
            var cachedImage = new CachedImage
            {
                FileId = fileId,
                FileUrl = attachment.PreviewUrl,
                FileType = fileType,
                Stretch = Stretch.UniformToFill,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                DecodePixelWidth = width
            };

            border.Child = cachedImage;

            // Применяем закругление углов через Clip после загрузки
            border.Loaded += (s, e) =>
            {
                if (cornerRadius.TopLeft > 0 || cornerRadius.TopRight > 0 ||
                    cornerRadius.BottomRight > 0 || cornerRadius.BottomLeft > 0)
                {
                    border.Clip = CreateRoundedRectangleGeometry(width, height, cornerRadius);
                }
            };

            // Add click handler
            border.MouseLeftButtonDown += (sender, e) =>
            {
                OnImageClicked(fileId);
                e.Handled = true;
            };

            return border;
        }

        private Geometry CreateRoundedRectangleGeometry(double width, double height, CornerRadius cornerRadius)
        {
            var geometry = new StreamGeometry();
            using (var context = geometry.Open())
            {
                double topLeft = cornerRadius.TopLeft;
                double topRight = cornerRadius.TopRight;
                double bottomRight = cornerRadius.BottomRight;
                double bottomLeft = cornerRadius.BottomLeft;

                // Начинаем рисовать с точки после верхнего левого скругления
                context.BeginFigure(new Point(topLeft, 0), true, true);

                // Верхняя сторона до верхнего правого угла
                context.LineTo(new Point(width - topRight, 0), true, false);

                // Верхний правый угол
                if (topRight > 0)
                    context.ArcTo(new Point(width, topRight), new Size(topRight, topRight), 0, false, SweepDirection.Clockwise, true, false);

                // Правая сторона
                context.LineTo(new Point(width, height - bottomRight), true, false);

                // Нижний правый угол
                if (bottomRight > 0)
                    context.ArcTo(new Point(width - bottomRight, height), new Size(bottomRight, bottomRight), 0, false, SweepDirection.Clockwise, true, false);

                // Нижняя сторона
                context.LineTo(new Point(bottomLeft, height), true, false);

                // Нижний левый угол
                if (bottomLeft > 0)
                    context.ArcTo(new Point(0, height - bottomLeft), new Size(bottomLeft, bottomLeft), 0, false, SweepDirection.Clockwise, true, false);

                // Левая сторона
                context.LineTo(new Point(0, topLeft), true, false);

                // Верхний левый угол
                if (topLeft > 0)
                    context.ArcTo(new Point(topLeft, 0), new Size(topLeft, topLeft), 0, false, SweepDirection.Clockwise, true, false);
            }

            geometry.Freeze();
            return geometry;
        }

        private void OnImageClicked(string fileId)
        {
            var msgType = new Services.Erida.MessageType
            {
                Type = Services.Erida.MessageType.MessageTypeEnum.Info
            };
            App.ErideMessage.AddMessage($"Image clicked: {fileId}", msgType);
        }
    }
}
