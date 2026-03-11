namespace RssSite.Components.Models
{
    public class ApiResponse<T>
    {
        public bool Success { get; set; }
        public string ErrorMessage { get; set; } = string.Empty;
        public T? Item { get; set; }
        public List<T>? Items { get; set; }
    }
}