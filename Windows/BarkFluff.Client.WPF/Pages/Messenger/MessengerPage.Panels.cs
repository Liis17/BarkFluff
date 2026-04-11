using BarkFluff.Client.WPF.UserControls;

using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Animation;

namespace BarkFluff.Client.WPF.Pages
{
    public partial class MessengerPage
    {
        #region Боковая панель

        private bool isOpenPanel = false;
        private SideBar? _sideBar;
        private readonly CubicEase easingPanel = new CubicEase { EasingMode = EasingMode.EaseInOut };

        private void SidePanel_Loaded(object sender, RoutedEventArgs e)
        {
            // SideBar создаётся лениво при первом открытии панели,
            // чтобы данные пользователя (аватар, имя) уже были загружены
        }

        private void OpenPanelClick(object sender, RoutedEventArgs e)
        {
            if (!isOpenPanel)
                OpenPanel();
            else
                ClosePanel();
        }

        private void OverlayPanel_MouseDown(object sender, MouseButtonEventArgs e)
        {
            ClosePanel();
        }

        /// <summary>
        /// открыть Sidebar
        /// </summary>
        public void OpenPanel()
        {
            // Создаём SideBar лениво при первом открытии
            if (_sideBar == null)
            {
                _sideBar = new SideBar();
                SidePanel.Children.Clear();
                SidePanel.Children.Add(_sideBar);
            }
            else
            {
                // Обновляем данные при каждом открытии (аватар мог загрузиться)
                _sideBar.RefreshUserData();
            }

            var anim = new ThicknessAnimation
            {
                From = new Thickness(-350, 0, 0, 0),
                To = new Thickness(0, 0, 0, 0),
                Duration = TimeSpan.FromSeconds(0.2),
                EasingFunction = easingPanel
            };
            SidePanel.BeginAnimation(MarginProperty, anim);
            OverlayPanel.Visibility = Visibility.Visible;
            isOpenPanel = true;
        }

        /// <summary>
        /// Закрыть Sidebar
        /// </summary>
        public void ClosePanel()
        {
            var anim = new ThicknessAnimation
            {
                From = new Thickness(0, 0, 0, 0),
                To = new Thickness(-350, 0, 0, 0),
                Duration = TimeSpan.FromSeconds(0.2),
                EasingFunction = easingPanel
            };
            SidePanel.BeginAnimation(MarginProperty, anim);
            OverlayPanel.Visibility = Visibility.Collapsed;
            isOpenPanel = false;
        }

        #endregion

        #region Центральный блок контента

        private bool isOpenCenter = false;
        private readonly CubicEase easingCenter = new CubicEase { EasingMode = EasingMode.EaseInOut };
        private Profile? _currentProfile = null;

        private void OpenCenterBlock(object sender, MouseButtonEventArgs e)
        {
            var senderElement = sender as FrameworkElement;

            if (senderElement != null && !string.IsNullOrEmpty(senderElement.Tag?.ToString()))
            {
                var tag = senderElement.Tag.ToString();

                if (tag == "UserProfile")
                {
                    // Определяем, чей профиль открывать
                    if (senderElement.Name == "AvatarTitleWindowButton")
                    {
                        // Клик на аватар в заголовке - открываем свой профиль
                        ShowUserProfile(isCurrentUser: true);
                    }
                    else if (senderElement.Name == "ChatAvatarButton")
                    {
                        // Клик на аватар в чате - открываем профиль собеседника
                        if (ChatIdbyUserId.Value > 0)
                        {
                            ShowUserProfile(userId: ChatIdbyUserId.Value);
                        }
                    }

                    if (!isOpenCenter)
                    {
                        OpenCenterPanel();
                    }
                }
                else if (tag == "UpdateBlock")
                {
                    LaunchUpdater();
                }
            }
            else
            {
                if (!isOpenCenter)
                {
                    OpenCenterPanel();
                }
                else
                {
                    CloseCenterPanel();
                }
            }
        }

        /// <summary>
        /// Показывает профиль пользователя в центральной панели
        /// </summary>
        /// <param name="isCurrentUser">Если true, загружает профиль текущего пользователя</param>
        /// <param name="userId">ID пользователя для загрузки (если isCurrentUser = false)</param>
        private void ShowUserProfile(bool isCurrentUser = false, long userId = 0)
        {
            // Очищаем предыдущий контент
            CenterPanel.Child = null;

            // Создаем новый Profile контрол
            _currentProfile = new Profile();
            CenterPanel.Child = _currentProfile;

            if (isCurrentUser)
            {
                _currentProfile.LoadCurrentUserProfile();
            }
            else if (userId > 0)
            {
                _currentProfile.LoadUserProfile(userId);
            }
        }

        public void OpenSettings()
        {
            CenterPanel.Child = null;

            CenterPanel.Child = new BarkFluff.Client.WPF.UserControls.Settings();

            OpenCenterPanel();
        }

        public void OpenDebugMenu()
        {
            CenterPanel.Child = null;

            CenterPanel.Child = new BarkFluff.Client.WPF.UserControls.DevTools.Menu();

            OpenCenterPanel();
        }

        public void OpenVideoEditor(string videoPath)
        {
            CenterPanel.Child = null;

            var editor = new BarkFluff.Client.WPF.UserControls.VideoEditor(videoPath);
            CenterPanel.Child = editor;

            OpenCenterPanel();
        }

        private void OpenCenterPanel()
        {
            CenterPanel.Visibility = Visibility.Visible;
            OverlayCenter.Visibility = Visibility.Visible;

            var anim = new DoubleAnimation
            {
                From = 0,
                To = 1,
                Duration = TimeSpan.FromSeconds(0.2),
                EasingFunction = easingCenter
            };
            CenterPanel.BeginAnimation(OpacityProperty, anim);
            isOpenCenter = true;
        }

        private void CloseCenterPanel()
        {
            var anim = new DoubleAnimation
            {
                From = 1,
                To = 0,
                Duration = TimeSpan.FromSeconds(0.2),
                EasingFunction = easingCenter
            };
            anim.Completed += (s, e) =>
            {
                CenterPanel.Visibility = Visibility.Collapsed;
                OverlayCenter.Visibility = Visibility.Collapsed;
                // Очищаем контент при закрытии
                CenterPanel.Child = null;
                _currentProfile = null;
            };
            CenterPanel.BeginAnimation(OpacityProperty, anim);
            isOpenCenter = false;
        }

        private void OverlayCenter_MouseDown(object sender, MouseButtonEventArgs e)
        {
            CloseCenterPanel();
        }

        #endregion
    }
}
