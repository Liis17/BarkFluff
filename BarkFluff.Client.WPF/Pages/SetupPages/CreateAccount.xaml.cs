using BarkFluff.Client.WPF.Pages.SetupPages.Registration;
using BarkFluff.Client.WPF.Services.App;
using BarkFluff.Client.WPF.UserControls;
using BarkFluff.Shared.Exceptions;
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

using Windows.Devices.Sensors;

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
            UpdateErrorMessageTextBlock("");
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
            UpdateErrorMessageTextBlock("");
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
                        UpdateErrorMessageTextBlock("");
                        LoginEnter.Focus();
                    }
                    else if (FirstNameEnter.Text.Length >= 41 && LastNameEnter.Text.Length >= 41)
                    {
                        UpdateErrorMessageTextBlock("Имя и фамилия слиииииииииииииииииииииии");
                    }
                    else if (FirstNameEnter.Text.Length >= 41)
                    {
                        UpdateErrorMessageTextBlock("Имя слииииииишком длинное");
                    }
                    else if (LastNameEnter.Text.Length >= 41)
                    {
                        UpdateErrorMessageTextBlock("Фамилия слииииииишком длинная");
                    }
                    else if (FirstNameEnter.Text.Replace(" ", "") == string.Empty)
                    {
                        UpdateErrorMessageTextBlock("Имя не может быть пустым");
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
                            var response = await App.ServerCommunication.UsersAC.CheckExistUsernameAsync(
                                new Proto.Users.CheckExistUsernameRequest { Username = LoginEnter.Text.ToLower() });

                            if (response.Exist == true)
                            {
                                UpdateErrorMessageTextBlock("Имя пользователя уже занято.");
                                return;
                            }
                            else
                            {
                                AnimateTransition(steps[currentStep], steps[currentStep + 1], SlideDirection.Forward);
                                currentStep++;
                                UpdateNavigationButtons();
                                UpdateErrorMessageTextBlock("");
                                EmailEnter.Focus();
                                return;
                            }
                        }
                        catch (Exception ex)
                        {
                            UpdateErrorMessageTextBlock("Ошибка подключения к серверу. Проверьте интернет-соединение.");
                            return;
                        }
                    }
                    else
                    {
                        UpdateErrorMessageTextBlock(error);
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

                            try
                            {
                                var response = await App.ServerCommunication.IdentityAC.CreateAccountAsync(new Proto.Identity.CreateAccountRequest
                                {
                                    FirstName = _firstName,
                                    LastName = _lastName,
                                    Email = _email,
                                    Username = _login
                                });
                                _codeId = response.CodeId;
                            }
                            catch (EmailExistException)
                            {
                                UpdateErrorMessageTextBlock("Эта почта уже использовалась при регистрации на этом сервере");
                                return;
                            }
                            catch (BaseGrpcException)
                            {
                                UpdateErrorMessageTextBlock("Неизвестная ошибка BaseGrpc");
                                return;
                            }
                            catch (Grpc.Core.RpcException rpcEx)
                            {
                                UpdateErrorMessageTextBlock("Неизвестная ошибка Rpc");
                                return;
                            }
                            catch (Exception ex)
                            {
                                UpdateErrorMessageTextBlock("Неизвестная ошибка");
                                return;
                            }

                            if (_codeId != string.Empty)
                            {
                                AnimateTransition(steps[currentStep], steps[currentStep + 1], SlideDirection.Forward);
                                currentStep++;
                                UpdateNavigationButtons();
                                UpdateErrorMessageTextBlock("");
                                EmailHelperText.Text = "● Код отправлен на " + EmailEnter.Text.ToLower();
                                VerificationCodeEnter.Focus();
                                return;
                            }

                        }
                        else
                        {
                            UpdateErrorMessageTextBlock("Введите корректный адрес почты");
                            return;
                        }
                    }
                    else
                    {
                        UpdateErrorMessageTextBlock("Поле ввода почты не может быть пустым");
                        return;
                    }
                }
                else if (currentStep == 3) // Четвертый шаг (код подтверждения почты)
                {
                    if (VerificationCodeEnter.Text != string.Empty)
                    {
                        try
                        {
                            var response = await App.ServerCommunication.IdentityAC.ConfirmAccountAsync(new Proto.Identity.ConfirmAccountRequest
                            {
                                CodeId = _codeId,
                                CodeValue = VerificationCodeEnter.Text
                            });

                            App.GParam.RefreshToken = response.RefreshToken;
                            await App.ServerCommunication.TokenUpdate(App.GParam, response.RefreshToken.Value);
                            GlobalParam.Save(App.GParam, App.GParam.AppPath + "GlobalParam.json", App.GParam.AppPass);
                            App.UpdateApiClient();
                            MainWindow.SaveSettings();
                        }
                        catch (ConfirmationCodeExpiredException)
                        {
                            UpdateErrorMessageTextBlock($"Код подтверждения больше недействителен");
                            return;
                        }
                        catch (ConfirmationCodeIncorrectException)
                        {
                            UpdateErrorMessageTextBlock("Неверный код подтверждения");
                            return;
                        }
                        catch (ConfirmationCodeNotFoundException)
                        {
                            UpdateErrorMessageTextBlock("Код подтверждения не найден?");
                            return;
                        }
                        catch (Grpc.Core.RpcException ex)
                        {
                            UpdateErrorMessageTextBlock($"Произошла ошибка при подтверждении:\n {ex.Status.Detail}");
                            return;
                        }

                        AnimateTransition(steps[currentStep], steps[currentStep + 1], SlideDirection.Forward);
                        currentStep++;
                        UpdateNavigationButtons();
                        UpdateErrorMessageTextBlock("");
                        PasswordEnter.Focus();
                    }
                    else
                    {
                        UpdateErrorMessageTextBlock("Введите код из сообщения на " + EmailEnter.Text.ToLower());
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
                                var response = await App.ServerCommunication.UsersAC.SetPasswordAsync(new Proto.Users.SetPasswordRequest
                                {
                                    Password = PasswordEnter.Password,
                                });
                                var a = response;
                                AnimateTransition(steps[currentStep], steps[currentStep + 1], SlideDirection.Forward);
                                currentStep++;
                                UpdateNavigationButtons();
                                UpdateErrorMessageTextBlock("");
                                ExpansionSpace();
                                return;
                            }
                            catch
                            {
                                UpdateErrorMessageTextBlock(@"Произошла неизвестная ошибка ¯\(°_o)/¯");
                            }

                        }
                        else if (PasswordEnter.Password != PasswordRepeatedEnter.Password)
                        {
                            UpdateErrorMessageTextBlock("Пароли не совпадают");
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
                    UpdateErrorMessageTextBlock("");

                    JpegBitmapEncoder encoder = new JpegBitmapEncoder();
                    encoder.QualityLevel = 60; // Качество 60%
                    encoder.Frames.Add(BitmapFrame.Create(AvatarHolder.Image));
                    using var memoryStream = new MemoryStream();
                    encoder.Save(memoryStream);
                    byte[] jpegBytes = memoryStream.ToArray();

                    await App.ServerCommunication.UploadUserAvatarAsync(jpegBytes);

                    var response = await App.ServerCommunication.GetUserDatas();
                    var fullName = $@"{response.FirstName} {response.LastName}";
                    PreviewUserElement.PreviewUser_Update(fullName, response.Username, response.ProfilePictureUrl);
                }
                else if (currentStep == 6) //Дополнительная информация о профиле
                {
                    AnimateTransition(steps[currentStep], steps[currentStep + 1], SlideDirection.Forward);
                    currentStep++;
                    UpdateNavigationButtons();
                    UpdateErrorMessageTextBlock("");

                    OtpSuggestion.Update();
                }
                else if (currentStep == 7) //Предложение включить 2fa
                {
                    AnimateTransition(steps[currentStep], steps[currentStep + 1], SlideDirection.Forward);
                    currentStep++;
                    UpdateNavigationButtons();
                    UpdateErrorMessageTextBlock("");
                }
                else if (currentStep == 8) //Последний шаг (завершение регистрации)
                {
                    AnimateTransition(steps[currentStep], steps[currentStep + 1], SlideDirection.Forward);
                    currentStep++;
                    UpdateNavigationButtons();
                    UpdateErrorMessageTextBlock("");
                    CompletionRegistrationElement.TimerStart();
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
        public bool IsValidPassword(string password)
        {
            if (string.IsNullOrEmpty(password))
            {
                UpdateErrorMessageTextBlock("Пароль не должен быть пустым.");
                return false;
            }

            if (password.Length < 8)
            {
                UpdateErrorMessageTextBlock("Пароль должен содержать не менее 8 символов.");
                return false;
            }

            if (password.Contains(" "))
            {
                UpdateErrorMessageTextBlock("Пароль не должен содержать пробелы.");
                return false;
            }

            UpdateErrorMessageTextBlock(""); // ошибок нет
            return true;
        }

        #endregion


        #endregion

        private void Enter_TextChanged(object sender, TextChangedEventArgs e)
        {
            UpdateErrorMessageTextBlock("");
        }



        private void UpdateErrorMessageTextBlock(string text)
        {
            ErrorMessageTextBlock.Text = text;
        }

        private void PasswordEnter_TextChanged(object sender, RoutedEventArgs e)
        {
            UpdateErrorMessageTextBlock("");
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
