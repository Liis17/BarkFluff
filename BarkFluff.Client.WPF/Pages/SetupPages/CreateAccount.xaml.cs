using BarkFluff.Client.WPF.Pages.SetupPages.Registration;
using BarkFluff.Client.WPF.Services.App;
using BarkFluff.Client.WPF.Services.Registration;
using BarkFluff.Client.WPF.UserControls;
using BarkFluff.Client.WPF.Validators;
using BarkFluff.Shared.Exceptions.Identity;

using System.IO;
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
    public partial class CreateAccount : UserControl, IDisposable
    {
        private int currentStep = 0;
        private List<FrameworkElement>? steps;
        private readonly RegistrationStateService _registrationState = new();
        public AvatarImageHolder AvatarHolder { get; set; } = new AvatarImageHolder();

        // Colors for validation feedback
        private static readonly SolidColorBrush ValidColor = new(Color.FromRgb(0x4C, 0xAF, 0x50));
        private static readonly SolidColorBrush InvalidColor = new(Color.FromRgb(0xFF, 0x46, 0x46));
        private static readonly SolidColorBrush NeutralColor = new(Color.FromRgb(0x88, 0x88, 0x88));

        public enum SlideDirection
        {
            Forward,
            Backward
        }
        public CreateAccount()
        {
            InitializeComponent();
            Loaded += Register_Loaded;
            Unloaded += CreateAccount_Unloaded;
        }

        private void CreateAccount_Unloaded(object sender, RoutedEventArgs e)
        {
            Dispose();
        }

        public void Dispose()
        {
            _registrationState.Dispose();
            GC.SuppressFinalize(this);
        }

        private void Register_Loaded(object sender, RoutedEventArgs e)
        {
            steps = new List<FrameworkElement> { Step1, Step2, Step3, Step4, Step5, Step6, Step7, Step8, Step9 };

            foreach (var item in steps)
            {
                item.Visibility = Visibility.Collapsed;
            }
            steps[0].Visibility = Visibility.Visible;
            UpdateNavigationButtons();
            UpdateProgressIndicator();
            FirstNameEnter.Focus();
            CreateButton.Visibility = Visibility.Collapsed;
        }

        private void UpdateProgressIndicator()
        {
            StepIndicatorText.Text = $"Шаг {currentStep + 1} из 9";
            StepProgressBar.Value = currentStep + 1;
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

        private void TextBlock_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is TextBlock textBlock)
            {
                textBlock.Text = App.GParam.ServerName;
            }
        }

        #region Real-time validation handlers

        private void FirstNameEnter_TextChanged(object sender, TextChangedEventArgs e)
        {
            var firstName = FirstNameEnter.Text;
            if (string.IsNullOrEmpty(firstName))
            {
                FirstNameValidationText.Text = "";
                FirstNameValidationText.Foreground = NeutralColor;
                return;
            }

            if (PersonalInfoValidator.ValidateFirstName(firstName, out var errorMessage))
            {
                FirstNameValidationText.Text = "✓ Имя корректно";
                FirstNameValidationText.Foreground = ValidColor;
            }
            else
            {
                FirstNameValidationText.Text = errorMessage;
                FirstNameValidationText.Foreground = InvalidColor;
            }
        }

        private void LastNameEnter_TextChanged(object sender, TextChangedEventArgs e)
        {
            var lastName = LastNameEnter.Text;
            if (string.IsNullOrEmpty(lastName))
            {
                LastNameValidationText.Text = "";
                LastNameValidationText.Foreground = NeutralColor;
                return;
            }

            if (PersonalInfoValidator.ValidateLastName(lastName, out var errorMessage))
            {
                LastNameValidationText.Text = "✓ Фамилия корректна";
                LastNameValidationText.Foreground = ValidColor;
            }
            else
            {
                LastNameValidationText.Text = errorMessage;
                LastNameValidationText.Foreground = InvalidColor;
            }
        }

        private void LoginEnter_TextChanged(object sender, TextChangedEventArgs e)
        {
            var login = LoginEnter.Text;
            if (string.IsNullOrEmpty(login))
            {
                LoginValidationText.Text = "";
                LoginValidationText.Foreground = NeutralColor;
                return;
            }

            var result = UsernameValidator.ValidateRealTime(login);
            LoginValidationText.Text = result.Message;
            LoginValidationText.Foreground = result.IsValid ? ValidColor : InvalidColor;
        }

        private void EmailEnter_TextChanged(object sender, TextChangedEventArgs e)
        {
            var email = EmailEnter.Text;
            if (string.IsNullOrEmpty(email))
            {
                EmailValidationText.Text = "";
                EmailValidationText.Foreground = NeutralColor;
                return;
            }

            var result = EmailValidator.ValidateRealTime(email);
            EmailValidationText.Text = result.Message;
            EmailValidationText.Foreground = result.IsValid ? ValidColor : InvalidColor;
        }

        private void VerificationCodeEnter_TextChanged(object sender, TextChangedEventArgs e)
        {
            var code = VerificationCodeEnter.Text;
            if (string.IsNullOrEmpty(code))
            {
                VerificationCodeValidationText.Text = "";
                VerificationCodeValidationText.Foreground = NeutralColor;
                return;
            }

            var result = VerificationCodeValidator.ValidateRealTime(code);
            VerificationCodeValidationText.Text = result.Message;
            VerificationCodeValidationText.Foreground = result.IsValid ? ValidColor : InvalidColor;
        }

        private void PasswordEnter_TextChanged(object sender, RoutedEventArgs e)
        {
            var password = PasswordEnter.Password;
            var strength = BarkFluff.Shared.SecurityUtilities.SecurityUtilities.EvaluatePasswordStrength(password);
            PasswordStrengthBar.Value = strength;
            var colors = BarkFluff.Shared.SecurityUtilities.SecurityUtilities.GetPasswordStrengthMessage(strength);
            PasswordDifficultyIndicator.Text = colors.message;
            PasswordStrengthBar.Foreground = (Brush)new BrushConverter().ConvertFromString(colors.colorHex)!;

            // Update requirements checklist
            var requirements = PasswordValidator.GetRequirementsStatus(password);
            UpdateRequirementText(ReqMinLength, requirements.HasMinLength, "Минимум 8 символов");
            UpdateRequirementText(ReqUpperCase, requirements.HasUpperCase, "Заглавные буквы");
            UpdateRequirementText(ReqLowerCase, requirements.HasLowerCase, "Строчные буквы");
            UpdateRequirementText(ReqDigit, requirements.HasDigit, "Цифры");
            UpdateRequirementText(ReqSpecialChar, requirements.HasSpecialChar, "Специальные символы");

            // Update password match if confirm field has text
            if (!string.IsNullOrEmpty(PasswordRepeatedEnter.Password))
            {
                UpdatePasswordMatchIndicator();
            }
        }

        private void PasswordRepeatedEnter_TextChanged(object sender, RoutedEventArgs e)
        {
            UpdatePasswordMatchIndicator();
        }

        private void UpdatePasswordMatchIndicator()
        {
            if (string.IsNullOrEmpty(PasswordRepeatedEnter.Password))
            {
                PasswordMatchText.Text = "";
                PasswordMatchText.Foreground = NeutralColor;
                return;
            }

            if (PasswordEnter.Password == PasswordRepeatedEnter.Password)
            {
                PasswordMatchText.Text = "✓ Пароли совпадают";
                PasswordMatchText.Foreground = ValidColor;
            }
            else
            {
                PasswordMatchText.Text = "✗ Пароли не совпадают";
                PasswordMatchText.Foreground = InvalidColor;
            }
        }

        private static void UpdateRequirementText(TextBlock textBlock, bool isMet, string text)
        {
            textBlock.Text = isMet ? $"✓ {text}" : $"○ {text}";
            textBlock.Foreground = isMet ? ValidColor : NeutralColor;
        }

        #endregion

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
            if (steps == null) return;
            NextButton.Visibility = currentStep < steps.Count - 1 ? Visibility.Visible : Visibility.Collapsed;
            CreateButton.Visibility = currentStep == steps.Count - 1 ? Visibility.Visible : Visibility.Collapsed;
        }

        private async void NextButton_Click(object? sender, RoutedEventArgs? e)
        {
            NextButton.IsEnabled = false; // Отключаем кнопку, чтобы предотвратить повторные нажатия

            try
            {
                if (steps == null || currentStep >= steps.Count) return;

                if (currentStep == 0) // Первый шаг (имя фамилия)
                {
                    await ProcessStep1();
                }
                else if (currentStep == 1) // Второй шаг (логин)
                {
                    await ProcessStep2();
                }
                else if (currentStep == 2) // Третий шаг (почта)
                {
                    await ProcessStep3();
                }
                else if (currentStep == 3) // Четвертый шаг (код подтверждения почты)
                {
                    await ProcessStep4();
                }
                else if (currentStep == 4) // Пятый шаг (придумать пароль)
                {
                    await ProcessStep5();
                }
                else if (currentStep == 5) // Шестой шаг (обрезка аватара)
                {
                    await ProcessStep6();
                }
                else if (currentStep == 6) // Дополнительная информация о профиле
                {
                    ProcessStep7();
                }
                else if (currentStep == 7) // Предложение включить 2fa
                {
                    ProcessStep8();
                }
                else if (currentStep == 8) // Последний шаг (завершение регистрации)
                {
                    ProcessStep9();
                }
            }
            finally
            {
                NextButton.IsEnabled = true;
            }
        }

        private async Task ProcessStep1()
        {
            var firstName = PersonalInfoValidator.GetTrimmedFirstName(FirstNameEnter.Text);
            var lastName = PersonalInfoValidator.GetTrimmedLastName(LastNameEnter.Text);

            if (!PersonalInfoValidator.ValidateFirstName(firstName, out var firstNameError))
            {
                App.ErideMessage.AddMessage(firstNameError, new Services.Erida.MessageType { Type = Services.Erida.MessageType.MessageTypeEnum.Warning });
                return;
            }

            if (!PersonalInfoValidator.ValidateLastName(lastName, out var lastNameError))
            {
                App.ErideMessage.AddMessage(lastNameError, new Services.Erida.MessageType { Type = Services.Erida.MessageType.MessageTypeEnum.Warning });
                return;
            }

            // Store state
            _registrationState.FirstName = firstName;
            _registrationState.LastName = lastName;

            GoToNextStep();
            LoginEnter.Focus();
            await Task.CompletedTask;
        }

        private async Task ProcessStep2()
        {
            if (!UsernameValidator.Validate(LoginEnter.Text, out var error))
            {
                App.ErideMessage.AddMessage(error, new Services.Erida.MessageType { Type = Services.Erida.MessageType.MessageTypeEnum.Error });
                return;
            }

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

                _registrationState.Username = LoginEnter.Text.ToLower();
                GoToNextStep();
                EmailEnter.Focus();
            }
            catch (Exception)
            {
                App.ErideMessage.AddMessage("Ошибка подключения к серверу. Проверьте интернет-соединение.", new Services.Erida.MessageType { Type = Services.Erida.MessageType.MessageTypeEnum.Error });
            }
        }

        private async Task ProcessStep3()
        {
            if (!EmailValidator.Validate(EmailEnter.Text, out var error))
            {
                App.ErideMessage.AddMessage(error, new Services.Erida.MessageType { Type = Services.Erida.MessageType.MessageTypeEnum.Error });
                return;
            }

            var email = EmailValidator.Normalize(EmailEnter.Text);
            _registrationState.Email = email;

            try
            {
                var response = await App.ServerCommunication.CreateAccount(
                    _registrationState.FirstName,
                    _registrationState.LastName,
                    email,
                    _registrationState.Username,
                    App.GParam);

                if (!response.error.IsSuccess)
                {
                    App.ErideMessage.AddMessage("Ошибка при создании аккаунта", new Services.Erida.MessageType { Type = Services.Erida.MessageType.MessageTypeEnum.Error });
                    return;
                }

                _registrationState.CodeId = response.Item2;

                if (!string.IsNullOrEmpty(_registrationState.CodeId))
                {
                    GoToNextStep();
                    EmailHelperText.Text = "● Код отправлен на " + email;
                    VerificationCodeEnter.Focus();
                }
            }
            catch (Exception)
            {
                App.ErideMessage.AddMessage("Ошибка подключения к серверу.", new Services.Erida.MessageType { Type = Services.Erida.MessageType.MessageTypeEnum.Error });
            }
        }

        private async Task ProcessStep4()
        {
            // Validate code format before sending to server
            if (!VerificationCodeValidator.Validate(VerificationCodeEnter.Text, out var formatError))
            {
                App.ErideMessage.AddMessage(formatError, new Services.Erida.MessageType { Type = Services.Erida.MessageType.MessageTypeEnum.Warning });
                return;
            }

            // Check rate limiting
            if (!_registrationState.CanAttemptVerification(out var rateLimitError))
            {
                App.ErideMessage.AddMessage(rateLimitError, new Services.Erida.MessageType { Type = Services.Erida.MessageType.MessageTypeEnum.Warning });
                return;
            }

            var code = VerificationCodeValidator.Normalize(VerificationCodeEnter.Text);

            try
            {
                var response = await App.ServerCommunication.ConfirmAccount(_registrationState.CodeId, code, App.GParam);
                if (!response.error.IsSuccess)
                {
                    _registrationState.RecordVerificationAttempt(false);
                    UpdateRemainingAttemptsDisplay();
                    App.ErideMessage.AddMessage("Ошибка подтверждения аккаунта", new Services.Erida.MessageType { Type = Services.Erida.MessageType.MessageTypeEnum.Warning });
                    return;
                }

                _registrationState.RecordVerificationAttempt(true);
                RemainingAttemptsText.Visibility = Visibility.Collapsed;

                App.GParam.RefreshToken = response.RefreshToken;
                await App.ServerCommunication.TokenUpdate(App.GParam);
                MainWindow.SaveSettings();
                App.UpdateApiClient();
                MainWindow.SaveSettings();

                GoToNextStep();
                PasswordEnter.Focus();
            }
            catch (ConfirmationCodeExpiredException)
            {
                _registrationState.RecordVerificationAttempt(false);
                UpdateRemainingAttemptsDisplay();
                App.ErideMessage.AddMessage("Код подтверждения больше недействителен", new Services.Erida.MessageType { Type = Services.Erida.MessageType.MessageTypeEnum.Warning });
            }
            catch (ConfirmationCodeIncorrectException)
            {
                _registrationState.RecordVerificationAttempt(false);
                UpdateRemainingAttemptsDisplay();
                App.ErideMessage.AddMessage("Неверный код подтверждения", new Services.Erida.MessageType { Type = Services.Erida.MessageType.MessageTypeEnum.Warning });
            }
            catch (ConfirmationCodeNotFoundException)
            {
                _registrationState.RecordVerificationAttempt(false);
                UpdateRemainingAttemptsDisplay();
                App.ErideMessage.AddMessage("Код подтверждения не найден", new Services.Erida.MessageType { Type = Services.Erida.MessageType.MessageTypeEnum.Warning });
            }
            catch (Grpc.Core.RpcException ex)
            {
                _registrationState.RecordVerificationAttempt(false);
                UpdateRemainingAttemptsDisplay();
                App.ErideMessage.AddMessage($"Произошла ошибка при подтверждении:\n {ex.Status.Detail}", new Services.Erida.MessageType { Type = Services.Erida.MessageType.MessageTypeEnum.Error });
            }
        }

        private void UpdateRemainingAttemptsDisplay()
        {
            var remaining = _registrationState.RemainingVerificationAttempts;
            if (remaining > 0 && remaining < 5)
            {
                RemainingAttemptsText.Text = $"• Осталось попыток: {remaining}";
                RemainingAttemptsText.Visibility = Visibility.Visible;
            }
            else if (remaining == 0)
            {
                RemainingAttemptsText.Text = "• Превышен лимит попыток. Подождите.";
                RemainingAttemptsText.Visibility = Visibility.Visible;
            }
        }

        private async Task ProcessStep5()
        {
            var password = PasswordEnter.Password;
            var confirmPassword = PasswordRepeatedEnter.Password;

            if (string.IsNullOrEmpty(password))
            {
                App.ErideMessage.AddMessage("Введите пароль", new Services.Erida.MessageType { Type = Services.Erida.MessageType.MessageTypeEnum.Warning });
                return;
            }

            if (!PasswordValidator.Validate(password, out var passwordError))
            {
                App.ErideMessage.AddMessage(passwordError, new Services.Erida.MessageType { Type = Services.Erida.MessageType.MessageTypeEnum.Warning });
                return;
            }

            if (!PasswordValidator.ValidateMatch(password, confirmPassword, out var matchError))
            {
                App.ErideMessage.AddMessage(matchError, new Services.Erida.MessageType { Type = Services.Erida.MessageType.MessageTypeEnum.Warning });
                return;
            }

            try
            {
                var response = await App.ServerCommunication.SetPassword(password, App.GParam);
                if (!response.IsSuccess)
                {
                    App.ErideMessage.AddMessage("Ошибка при установке пароля", new Services.Erida.MessageType { Type = Services.Erida.MessageType.MessageTypeEnum.Error });
                    return;
                }

                // Clear passwords from UI after successful submission
                PasswordEnter.Password = string.Empty;
                PasswordRepeatedEnter.Password = string.Empty;

                GoToNextStep();
                ExpansionSpace();
            }
            catch (Exception)
            {
                App.ErideMessage.AddMessage(@"Произошла неизвестная ошибка ¯\(°_o)/¯", new Services.Erida.MessageType { Type = Services.Erida.MessageType.MessageTypeEnum.Error });
            }
        }

        private async Task ProcessStep6()
        {
            GoToNextStep();

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
                App.ErideMessage.AddMessage(response.Error.ErrorMessage, new Services.Erida.MessageType { Type = Services.Erida.MessageType.MessageTypeEnum.Error });
                return;
            }
            var fullName = $@"{response.Data.FirstName} {response.Data.LastName}";
            PreviewUserElement.PreviewUser_Update(fullName, response.Data.Username, response.Data.ProfilePictureUrl);

            App.GParam.UserId = response.Data.Id;
            App.GParam.UserName = response.Data.Username;
            App.GParam.FirstName = response.Data.FirstName;
            App.GParam.LastName = response.Data.LastName;
            App.GParam.Description = response.Data.Description;
            App.GParam.RegistrationDate = response.Data.RegistrationDate;
            App.GParam.Email = response.Data.Email;

            MainWindow.SaveSettings();
        }

        private void ProcessStep7()
        {
            GoToNextStep();
            OtpSuggestion.Update();
        }

        private void ProcessStep8()
        {
            GoToNextStep();
            CompletionRegistrationElement.TimerStart();
        }

        private void ProcessStep9()
        {
            // This is the last step - handled by CompletionRegistration
        }

        private void GoToNextStep()
        {
            if (steps == null || currentStep >= steps.Count - 1) return;

            AnimateTransition(steps[currentStep], steps[currentStep + 1], SlideDirection.Forward);
            currentStep++;
            _registrationState.CurrentStep = currentStep;
            UpdateNavigationButtons();
            UpdateProgressIndicator();
        }

        private void ExpansionSpace()
        {
            ButtonBottomBar.Margin = new Thickness(left: 0, top: 0, right: 0, bottom: -620);
            TitleServerNameBar.Margin = new Thickness(left: 25, top: -650, right: 25, bottom: 0);
            ProgressIndicatorPanel.Visibility = Visibility.Collapsed;

            MainGrid.MinWidth = 500;
            MainGrid.MinHeight = 600;

            MainGrid.MaxWidth = 500;
            MainGrid.MaxHeight = 600;
        }

        #endregion

        private void MainContainer_Loaded(object sender, RoutedEventArgs e)
        {
            MoveChildren(MainContainer, MainGrid);
        }

        private static void MoveChildren(StackPanel stackPanel, Grid grid)
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
