using BarkFluff.Client.WPF.UserControls;

using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;

namespace BarkFluff.Client.WPF.Pages
{
    public partial class MessengerPage
    {
        #region SearchBox

        private void SearchBoxFocus(object sender, RoutedEventArgs e)
        {

        }

        public void ChatListFadeIn()
        {
            var storyboard = new Storyboard();
            var opacityAnimation = new DoubleAnimation
            {
                From = 0.0,
                To = 1.0,
                Duration = new Duration(TimeSpan.FromMilliseconds(500)),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
            };
            Storyboard.SetTarget(opacityAnimation, ChatList);
            Storyboard.SetTargetProperty(opacityAnimation, new PropertyPath("Opacity"));
            storyboard.Children.Add(opacityAnimation);
            storyboard.Begin();
        }

        public void ChatListFadeOut()
        {
            var storyboard = new Storyboard();
            var opacityAnimation = new DoubleAnimation
            {
                From = 1.0,
                To = 0.0,
                Duration = new Duration(TimeSpan.FromMilliseconds(150)),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
            };
            Storyboard.SetTarget(opacityAnimation, ChatList);
            Storyboard.SetTargetProperty(opacityAnimation, new PropertyPath("Opacity"));
            storyboard.Children.Add(opacityAnimation);
            storyboard.Begin();
        }

        private void SearchTextBox_GotFocus(object sender, RoutedEventArgs e)
        {
            SearchTextBox.PlaceholderText = "Введите минимум 3 символа для поиска";
            SearchResultsHeader.Text = string.Empty;
            ExpandGrid();
        }

        private void ExpandGrid()
        {
            var expandAnimation = (Storyboard)FindResource("ExpandAnimation");
            expandAnimation.Begin();
            ChatListFadeOut();
        }

        private void CollapseGrid()
        {
            var collapseAnimation = (Storyboard)FindResource("CollapseAnimation");
            collapseAnimation.Begin();
            ChatListFadeIn();
        }

        public void ClearSearchAndHideResults()
        {
            SearchTextBox.Text = string.Empty;
            CollapseGrid();
        }

        private void SearchTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            SearchTextBox.PlaceholderText = "Поиск";

            // потом заменить на что то другое а то так хуева оставлять
            ClearSearchAndHideResults();
        }

        private async void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (SearchTextBox.Text.Length >= 3)
            {

                var response = await App.ServerCommunication.SearchUser(App.GParam, SearchTextBox.Text);
                SearchCollection.Children.Clear();
                foreach (var item in response.userList)
                {
                    var a = new SearchElement(item);
                    SearchCollection.Children.Add(a);
                }
                SearchResultsHeader.Text = "Найдено " + response.userList.Count + " результатов";
            }
            else if (SearchTextBox.Text.Length <= 2 && SearchTextBox.Text.Length >= 1)
            {
                SearchCollection.Children.Clear();
                SearchTextBox.PlaceholderText = string.Empty;
                SearchResultsHeader.Text = "Введите минимум 3 символа для поиска";
            }
            else if (SearchTextBox.Text.Length == 0)
            {
                SearchCollection.Children.Clear();
                SearchResultsHeader.Text = string.Empty;
            }
        }

        #endregion
    }
}
