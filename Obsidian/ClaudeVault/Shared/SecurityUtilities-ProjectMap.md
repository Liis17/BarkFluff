# BarkFluff.Shared.SecurityUtilities — карта проекта

Исходный каталог: `Shared/BarkFluff.Shared.SecurityUtilities/` (`net10.0`, без внешних NuGet-зависимостей).

- `SecurityUtilities.cs` — два статических метода для оценки сложности пароля и локализованного статуса; контракт и scoring приведены в [[Shared/SecurityUtilities]].
- Проектная ссылка используется WPF-клиентом; прямые вызовы находятся в `Windows/BarkFluff.Client.WPF/Validators/PasswordValidator.cs` и формах регистрации/сброса пароля.
