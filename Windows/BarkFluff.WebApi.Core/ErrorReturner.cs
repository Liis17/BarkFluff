namespace BarkFluff.WebApi.Core
{
    public class ErrorReturner
    {
        public const int HistoryAnchorNotFound = 2;
        public const int PermanentOutgoingFailure = 3;

        public bool IsSuccess { get; set; }
        public string? ErrorMessage { get; set; }
        public string? ErrorResourceKey { get; set; }
        /// <summary>
        /// 1 - получаемый список сообщений в чате пустой, 2 - якорь истории недоступен
        /// </summary>
        public int ErrorCode { get; set; } = 0;
        public ErrorReturner(bool isSuccess, string? errorMessage = null, int errorCode = 0, string? errorResourceKey = null)
        {
            IsSuccess = isSuccess;
            ErrorMessage = errorMessage;
            ErrorCode = errorCode;
            ErrorResourceKey = errorResourceKey;
        }
    }
}
