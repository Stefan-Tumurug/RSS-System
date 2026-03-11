namespace RssSite.Components.Authentication
{
    public class AuthenticationResult(bool success, string? errorMessage = null)
    {
        public bool Success { get; } = success;
        public string? ErrorMessage { get; } = errorMessage;
    }
}
