namespace BarkFluff.WebApi.Core
{
    public class ErrorReturner
    {
        public bool IsSuccess { get; set; }
        public string? ErrorMessage { get; set; }
        public string? ErrorResourceKey { get; set; }
        /// <summary>
        /// 1 - получаемый список сообщений в чате пустой
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
