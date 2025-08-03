using System;
using System.Collections.Generic;
using System.Text;

namespace BarkFluff.WebApi.Core
{
    public class ErrorReturner
    {
        public bool IsSuccess { get; set; }
        public string? ErrorMessage { get; set; }
        public ErrorReturner(bool isSuccess, string? errorMessage = null)
        {
            IsSuccess = isSuccess;
            ErrorMessage = errorMessage;
        }
    }
}
