using BarkFluff.WebApi.Core.MessengerData;
using BarkFluff.WebApi.Core.MessengerData.NonSavedData;

namespace BarkFluff.WebApi.Core.Managers
{
    /// <summary>
    /// Менеджер для поиска пользователей.
    /// </summary>
    internal class WebApiSearchManager : WebApiBase
    {
        private const int DefaultPageSize = 50;
        private readonly WebApi _webApi;

        public WebApiSearchManager(WebApi webApi) : base(webApi)
        {
            _webApi = webApi;
        }

        public async Task<(ErrorReturner error, List<UserData>? userList)> SearchUser(GlobalParam globalParam, string userNameSearched)
        {
            try
            {
                return await _webApi.TokenManager.SafeCallAsync(async () =>
                {
                    var response = await UsersAC!.SearchUsersAsync(new Proto.Users.SearchUsersRequest
                    {
                        Pagination = new Proto.Shared.PageRequest
                        {
                            Offset = 0,
                            Size = DefaultPageSize
                        },
                        Query = userNameSearched
                    });

                    var userDataList = response.Users
                        .Select(item => new UserData
                        {
                            FirstName = item.FirstName,
                            LastName = item.LastName,
                            Email = "Почта скрыта",
                            Username = item.Username,
                            RegistrationDate = item.RegistrationDate.ToDateTime(),
                            Id = item.Id,
                            Badges = item.Badges.ToString(),
                            ProfilePictureUrl = item.ProfilePicture,
                            ProfilePicturePreviewUrl = item.ProfilePicturePreview,
                        })
                        .ToList();

                    return (new ErrorReturner(true), userDataList);
                }, globalParam);
            }
            catch (BarkFluff.Shared.Exceptions.Identity.InvalidRefreshTokenException)
            {
                return (new ErrorReturner(false, "Неверный токен обновления."), null);
            }
            catch (Exception)
            {
                return (new ErrorReturner(false, "Ошибка поиска пользователей"), null);
            }
        }
    }
}
