using BarkFluff.Client.WPF.Pages.SetupPages.Registration;
using BarkFluff.Client.WPF.Services.App;
using BarkFluff.Client.WPF.UserControls;
using BarkFluff.Shared.Exceptions.Identity;
using BarkFluff.WebApi.Core.MessengerData;

using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;

namespace BarkFluff.Client.WPF.Pages.SetupPages
{
    /// <summary>
    /// Логика взаимодействия для CreateAccount.xaml
    /// </summary>
    public partial class CreateAccount : UserControl
    {
        private int currentStep = 0;
        private List<StackPanel>? steps;
        private string _codeId = string.Empty;
        private BitmapSource _picture;
        public AvatarImageHolder AvatarHolder { get; set; } = new AvatarImageHolder();

        public enum SlideDirection
        {
            Forward,
            Backward
        }
        public CreateAccount()
        {
            InitializeComponent();
            Loaded += Register_Loaded;
        }
        private void Register_Loaded(object sender, RoutedEventArgs e)
        {
            steps = new List<StackPanel> { Step1, Step2, Step3, Step4, Step5, Step6, Step7, Step8, Step9 };

            foreach (var item in steps)
            {
                item.Visibility = Visibility.Collapsed;
            }
            steps[0].Visibility = Visibility.Visible;
            UpdateNavigationButtons();
            FirstNameEnter.Focus();
            CreateButton.Visibility = Visibility.Collapsed;
        }
        private void TextBox_KeyDown(object sender, KeyEventArgs e)
        {
            NextButton.IsEnabled = true;
            if (e.Key == Key.Enter)
            {
                if (sender is TextBox textBox)
                {
                    object tag = textBox.Tag;

                    if (tag != null && tag.ToString() == "Names")
                    {
                        LastNameEnter.Focus();
                    }
                    else
                    {
                        NextButton_Click(sender, e);
                    }
                }
            }
        }
        private void Registration(object sender, RoutedEventArgs e)
        {

        }
        private void TextBlock_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is TextBlock textBlock)
            {
                textBlock.Text = App.GParam.ServerName;
            }
        }

        #region анимация и переход между шагами
        public static void AnimateTransition(UIElement fromPanel, UIElement toPanel, SlideDirection direction, double durationMs = 300)
        {
            if (fromPanel == null || toPanel == null)
                return;

            double fromOffset = direction == SlideDirection.Forward ? -500 : 500;
            double toOffset = direction == SlideDirection.Forward ? 500 : -500;

            var easing = new CubicEase { EasingMode = EasingMode.EaseInOut };

            // Подготовка новых трансформаций
            var fromTransform = new TranslateTransform();
            var toTransform = new TranslateTransform();
            fromPanel.RenderTransform = fromTransform;
            toPanel.RenderTransform = toTransform;

            // Начальные значения
            toPanel.Visibility = Visibility.Visible;
            toPanel.Opacity = 0;
            toTransform.X = toOffset;

            // Анимации сдвига и прозрачности
            var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(durationMs)) { EasingFunction = easing };
            var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(durationMs)) { EasingFunction = easing };

            var slideOut = new DoubleAnimation(0, fromOffset, TimeSpan.FromMilliseconds(durationMs)) { EasingFunction = easing };
            var slideIn = new DoubleAnimation(toOffset, 0, TimeSpan.FromMilliseconds(durationMs)) { EasingFunction = easing };

            fadeOut.Completed += (s, e) =>
            {
                fromPanel.Visibility = Visibility.Collapsed;
                fromPanel.Opacity = 1;
                fromTransform.X = 0;
            };

            // Запуск анимаций
            fromPanel.BeginAnimation(UIElement.OpacityProperty, fadeOut);
            fromTransform.BeginAnimation(TranslateTransform.XProperty, slideOut);

            toPanel.BeginAnimation(UIElement.OpacityProperty, fadeIn);
            toTransform.BeginAnimation(TranslateTransform.XProperty, slideIn);
        }

        private void UpdateNavigationButtons()
        {
            //BackButton.Visibility = currentStep > 0 ? Visibility.Visible : Visibility.Collapsed;
            NextButton.Visibility = currentStep < steps.Count - 1 ? Visibility.Visible : Visibility.Collapsed;
            CreateButton.Visibility = currentStep == steps.Count - 1 ? Visibility.Visible : Visibility.Collapsed;
        }

        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            if (currentStep > 0)
            {
                AnimateTransition(steps[currentStep], steps[currentStep - 1], SlideDirection.Backward);
                currentStep--;
                UpdateNavigationButtons();
            }
        }
        private bool IsValidLogin(string login, out string errorMessage)
        {
            errorMessage = "";

            if (login.Length < 3 || login.Length > 30)
            {
                errorMessage = "Логин должен быть от 3 до 30 символов.";
                return false;
            }

            if (Regex.IsMatch(login, @"^[0-9_-]"))
            {
                errorMessage = "Логин не может начинаться с цифры, тире или нижнего подчеркивания.";
                return false;
            }

            if (login.IndexOf("bot", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                errorMessage = "Логин не должен содержать 'bot'.";
                return false;
            }

            string pattern = @"^[a-zA-Z0-9_-]+$";
            Regex regex = new Regex(pattern);
            if (!regex.IsMatch(login))
            {
                errorMessage = "Логин содержит недопустимые символы.";
                return false;
            }

            return true;
        }
        private async void NextButton_Click(object sender, RoutedEventArgs e)
        {
            NextButton.IsEnabled = false; // Отключаем кнопку, чтобы предотвратить повторные нажатия
            if (currentStep < steps.Count)
            {
                if (currentStep == 0) // Первый шаг (имя фамилия)
                {
                    if (FirstNameEnter.Text.Replace(" ", "") != string.Empty && FirstNameEnter.Text.Length <= 40 && LastNameEnter.Text.Length <= 40)
                    {
                        AnimateTransition(steps[currentStep], steps[currentStep + 1], SlideDirection.Forward);
                        currentStep++;
                        UpdateNavigationButtons();
                        LoginEnter.Focus();
                    }
                    else if (FirstNameEnter.Text.Length >= 41 && LastNameEnter.Text.Length >= 41)
                    {
                        App.ErideMessage.AddMessage("Имя и фамилия слиииииииииииииииииииииии", new Services.Erida.MessageType { Type = Services.Erida.MessageType.MessageTypeEnum.Warning });
                    }
                    else if (FirstNameEnter.Text.Length >= 41)
                    {
                        App.ErideMessage.AddMessage("Имя слииииииишком длинное", new Services.Erida.MessageType { Type = Services.Erida.MessageType.MessageTypeEnum.Warning });
                    }
                    else if (LastNameEnter.Text.Length >= 41)
                    {
                        App.ErideMessage.AddMessage("Фамилия слииииииишком длинная", new Services.Erida.MessageType { Type = Services.Erida.MessageType.MessageTypeEnum.Warning });
                    }
                    else if (FirstNameEnter.Text.Replace(" ", "") == string.Empty)
                    {
                        App.ErideMessage.AddMessage("Имя не может быть пустым", new Services.Erida.MessageType { Type = Services.Erida.MessageType.MessageTypeEnum.Warning });
                    }
                    return;
                }
                else if (currentStep == 1) // Второй шаг (логин)
                {
                    var error = "";
                    if (IsValidLogin(LoginEnter.Text, out error))
                    {
                        try
                        {
                            var response = await App.ServerCommunication.CheckUsername(LoginEnter.Text, App.GParam);
                            if (!response.error.IsSuccess)
                            {
                                App.ErideMessage.AddMessage(response.error.ErrorMessage, new Services.Erida.MessageType { Type = Services.Erida.MessageType.MessageTypeEnum.Error });
                                return;
                            }
                            if (response.exists)
                            {
                                App.ErideMessage.AddMessage("Имя пользователя уже занято.", new Services.Erida.MessageType { Type = Services.Erida.MessageType.MessageTypeEnum.Warning });
                                return;
                            }
                            else
                            {
                                AnimateTransition(steps[currentStep], steps[currentStep + 1], SlideDirection.Forward);
                                currentStep++;
                                UpdateNavigationButtons();
                                EmailEnter.Focus();
                                return;
                            }
                        }
                        catch (Exception ex)
                        {
                            App.ErideMessage.AddMessage("Ошибка подключения к серверу. Проверьте интернет-соединение.", new Services.Erida.MessageType { Type = Services.Erida.MessageType.MessageTypeEnum.Error });
                            return;
                        }
                    }
                    else
                    {
                        App.ErideMessage.AddMessage(error, new Services.Erida.MessageType { Type = Services.Erida.MessageType.MessageTypeEnum.Error });
                        return;
                    }
                }
                else if (currentStep == 2) // Третий шаг (почта)
                {
                    if (EmailEnter.Text != string.Empty)
                    {
                        if (ContainsEmail(EmailEnter.Text))
                        {
                            var _firstName = FirstNameEnter.Text;
                            var _lastName = LastNameEnter.Text;
                            var _email = EmailEnter.Text.ToLower();
                            var _login = LoginEnter.Text.ToLower();

                            var response = await App.ServerCommunication.CreateAccount(_firstName, _lastName, _email, _login, App.GParam);
                            if (!response.error.IsSuccess)
                            {
                                App.ErideMessage.AddMessage("Ошибка при создании аккаунта", new Services.Erida.MessageType { Type = Services.Erida.MessageType.MessageTypeEnum.Error });
                                return;
                            }
                            _codeId = response.Item2;

                            if (_codeId != string.Empty)
                            {
                                AnimateTransition(steps[currentStep], steps[currentStep + 1], SlideDirection.Forward);
                                currentStep++;
                                UpdateNavigationButtons();
                                EmailHelperText.Text = "● Код отправлен на " + EmailEnter.Text.ToLower();
                                VerificationCodeEnter.Focus();
                                return;
                            }

                        }
                        else
                        {
                            App.ErideMessage.AddMessage("Введите корректный адрес почты", new Services.Erida.MessageType { Type = Services.Erida.MessageType.MessageTypeEnum.Error });
                            return;
                        }
                    }
                    else
                    {
                        App.ErideMessage.AddMessage("Поле ввода почты не может быть пустым", new Services.Erida.MessageType { Type = Services.Erida.MessageType.MessageTypeEnum.Error });
                        return;
                    }
                }
                else if (currentStep == 3) // Четвертый шаг (код подтверждения почты)
                {
                    if (VerificationCodeEnter.Text != string.Empty)
                    {
                        try
                        {
                            var response = await App.ServerCommunication.ConfirmAccount(_codeId, VerificationCodeEnter.Text, App.GParam);
                            if (!response.error.IsSuccess)
                            {
                                App.ErideMessage.AddMessage("Ошибка подтверждения аккаунта", new Services.Erida.MessageType { Type = Services.Erida.MessageType.MessageTypeEnum.Warning });
                                return;
                            }
                            App.GParam.RefreshToken = response.RefreshToken;
                            await App.ServerCommunication.TokenUpdate(App.GParam);
                            MainWindow.SaveSettings();
                            App.UpdateApiClient();
                            MainWindow.SaveSettings();
                        }
                        catch (ConfirmationCodeExpiredException)
                        {
                            App.ErideMessage.AddMessage($"Код подтверждения больше недействителен", new Services.Erida.MessageType { Type = Services.Erida.MessageType.MessageTypeEnum.Warning });
                            return;
                        }
                        catch (ConfirmationCodeIncorrectException)
                        {
                            App.ErideMessage.AddMessage("Неверный код подтверждения", new Services.Erida.MessageType { Type = Services.Erida.MessageType.MessageTypeEnum.Warning });
                            return;
                        }
                        catch (ConfirmationCodeNotFoundException)
                        {
                            App.ErideMessage.AddMessage("Код подтверждения не найден?", new Services.Erida.MessageType { Type = Services.Erida.MessageType.MessageTypeEnum.Warning });
                            return;
                        }
                        catch (Grpc.Core.RpcException ex)
                        {
                            App.ErideMessage.AddMessage($"Произошла ошибка при подтверждении:\n {ex.Status.Detail}", new Services.Erida.MessageType { Type = Services.Erida.MessageType.MessageTypeEnum.Error });
                            return;
                        }

                        AnimateTransition(steps[currentStep], steps[currentStep + 1], SlideDirection.Forward);
                        currentStep++;
                        UpdateNavigationButtons();
                        PasswordEnter.Focus();
                    }
                    else
                    {
                        App.ErideMessage.AddMessage("Введите код из сообщения на " + EmailEnter.Text.ToLower(), new Services.Erida.MessageType { Type = Services.Erida.MessageType.MessageTypeEnum.Info });
                    }
                }
                else if (currentStep == 4) //пятый шаг (придумать пароль)
                {
                    if (PasswordEnter.Password != string.Empty)
                    {
                        if (IsValidPassword(PasswordEnter.Password) && Shared.SecurityUtilities.SecurityUtilities.EvaluatePasswordStrength(PasswordEnter.Password) >= 60 && PasswordEnter.Password == PasswordRepeatedEnter.Password)
                        {
                            try
                            {
                                var response = await App.ServerCommunication.SetPassword(PasswordEnter.Password, App.GParam);
                                if (!response.IsSuccess)
                                {
                                    App.ErideMessage.AddMessage("Ошибка при установке пароля", new Services.Erida.MessageType { Type = Services.Erida.MessageType.MessageTypeEnum.Error });
                                    return;
                                }
                                AnimateTransition(steps[currentStep], steps[currentStep + 1], SlideDirection.Forward);
                                currentStep++;
                                UpdateNavigationButtons();
                                ExpansionSpace();
                                return;
                            }
                            catch
                            {
                                App.ErideMessage.AddMessage(@"Произошла неизвестная ошибка ¯\(°_o)/¯", new Services.Erida.MessageType { Type = Services.Erida.MessageType.MessageTypeEnum.Error });
                            }

                        }
                        else if (PasswordEnter.Password != PasswordRepeatedEnter.Password)
                        {
                            App.ErideMessage.AddMessage("Пароли не совпадают", new Services.Erida.MessageType { Type = Services.Erida.MessageType.MessageTypeEnum.Warning });
                        }
                        else
                        {
                            PasswordDifficultyIndicator.Text = "Пароль слишком простой\nТребуется более сложный пароль!";
                        }

                    }
                    else
                    {

                    }
                }
                else if (currentStep == 5) //шестой шаг (обрезка аватара)
                {
                    AnimateTransition(steps[currentStep], steps[currentStep + 1], SlideDirection.Forward);
                    currentStep++;
                    UpdateNavigationButtons();

                    JpegBitmapEncoder encoder = new JpegBitmapEncoder();
                    encoder.QualityLevel = 60; // Качество 60%
                    encoder.Frames.Add(BitmapFrame.Create(AvatarHolder.Image));
                    using var memoryStream = new MemoryStream();
                    encoder.Save(memoryStream);
                    byte[] jpegBytes = memoryStream.ToArray();

                    await App.ServerCommunication.UploadUserAvatarAsync(App.GParam, jpegBytes);

                    var response = await App.ServerCommunication.GetUserData(App.GParam);
                    if (!response.Error.IsSuccess)
                    {
                        App.ErideMessage.AddMessage(response.Error.ErrorMessage, new Services.Erida.MessageType { Type = Services.Erida.MessageType.MessageTypeEnum.Error});
                        return;
                    }
                    var fullName = $@"{response.Data.FirstName} {response.Data.LastName}";
                    PreviewUserElement.PreviewUser_Update(fullName, response.Data.Username, response.Data.ProfilePictureUrl);

                    App.GParam.UserId = response.Data.Id;
                    App.GParam.UserName = response.Data.Username;
                    App.GParam.FirstName = response.Data.FirstName;
                    App.GParam.LastName = response.Data.LastName;
                    App.GParam.Description = response.Data.Description;

                    MainWindow.SaveSettings();
                }
                else if (currentStep == 6) //Дополнительная информация о профиле
                {
                    AnimateTransition(steps[currentStep], steps[currentStep + 1], SlideDirection.Forward);
                    currentStep++;
                    UpdateNavigationButtons();

                    OtpSuggestion.Update();
                }
                else if (currentStep == 7) //Предложение включить 2fa
                {
                    AnimateTransition(steps[currentStep], steps[currentStep + 1], SlideDirection.Forward);
                    currentStep++;
                    UpdateNavigationButtons();
                    CompletionRegistrationElement.TimerStart();
                }
                else if (currentStep == 8) //Последний шаг (завершение регистрации)
                {
                    AnimateTransition(steps[currentStep], steps[currentStep + 1], SlideDirection.Forward);
                    currentStep++;
                    UpdateNavigationButtons();

                }
            }
        }

        private void ExpansionSpace()
        {
            ButtonBottomBar.Margin = new Thickness(left: 0, top: 0, right: 0, bottom: -620);
            TitleServerNameBar.Margin = new Thickness(left: 25, top: -650, right: 25, bottom: 0);

            MainGrid.MinWidth = 500;
            MainGrid.MinHeight = 600;

            MainGrid.MaxWidth = 500;
            MainGrid.MaxHeight = 600;
        }

        #region Проверки
        private bool ContainsEmail(string input)
        {
            string pattern = @"[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}";
            return Regex.IsMatch(input, pattern);
        }
        private bool IsValidPassword(string password)
        {
            if (string.IsNullOrEmpty(password))
            {
                App.ErideMessage.AddMessage("Пароль не должен быть пустым.", new Services.Erida.MessageType { Type = Services.Erida.MessageType.MessageTypeEnum.Error });
                return false;
            }

            if (password.Length < 8)
            {
                App.ErideMessage.AddMessage("Пароль должен содержать не менее 8 символов.", new Services.Erida.MessageType { Type = Services.Erida.MessageType.MessageTypeEnum.Error });
                return false;
            }

            if (password.Contains(" "))
            {
                App.ErideMessage.AddMessage("Пароль не должен содержать пробелы.", new Services.Erida.MessageType { Type = Services.Erida.MessageType.MessageTypeEnum.Error });
                return false;
            }

            App.ErideMessage.AddMessage("", new Services.Erida.MessageType { Type = Services.Erida.MessageType.MessageTypeEnum.Info }); // ошибок нет
            return true;
        }

        #endregion


        #endregion

        private void Enter_TextChanged(object sender, TextChangedEventArgs e)
        {
            
        }

        private void PasswordEnter_TextChanged(object sender, RoutedEventArgs e)
        {
            var a = 0;
            PasswordStrengthBar.Value = a = BarkFluff.Shared.SecurityUtilities.SecurityUtilities.EvaluatePasswordStrength(PasswordEnter.Password);
            var colors = BarkFluff.Shared.SecurityUtilities.SecurityUtilities.GetPasswordStrengthMessage(a);
            PasswordDifficultyIndicator.Text = colors.message;
            //PasswordDifficultyIndicator.Foreground = (Brush)new BrushConverter().ConvertFromString(colors.colorHex); отключим, так выгляди лучше, наверное хз
            PasswordStrengthBar.Foreground = (Brush)new BrushConverter().ConvertFromString(colors.colorHex);
        }

        private void MainContainer_Loaded(object sender, RoutedEventArgs e)
        {
            MoveChildren(MainContainer, MainGrid);
        }

        private void MoveChildren(StackPanel stackPanel, Grid grid)
        {
            if (stackPanel == null || grid == null)
                return;
            var children = new UIElement[stackPanel.Children.Count];
            stackPanel.Children.CopyTo(children, 0);

            stackPanel.Children.Clear();

            foreach (var child in children)
            {
                grid.Children.Add(child);
            }
        }

        private void CropperLoaded(object sender, RoutedEventArgs e)
        {
            if (sender is CropImage cropImage)
            {
                cropImage.AvatarHolder = AvatarHolder;
                cropImage.Pattern = this;
            }
        }

        public void NextStep()
        {
            NextButton_Click(null, null);
        }

        #region потом удалить их
        private void EmailEnter_Loaded(object sender, RoutedEventArgs e)
        {
#if (DEBUG)
            if (sender is TextBox serverIp)
            {
                serverIp.Text = "me@liis17.ru";
            }
#endif
        }
        #endregion
        private void PreviewUserElement_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is PreviewUser previewUser)
            {
                previewUser.Pattern = this;
            }
        }

        private void OtpSuggestion_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is TwoFA previewUser)
            {
                previewUser.Pattern = this;
            }

        }
    }

}
