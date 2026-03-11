namespace RssSite.Components.Models
{
    public class LogModel
    {
        public required string MacAddress { get; set; }
        public DateTime Timestamp { get; set; }
        public required string Action { get; set; }
        public string FormattedTimestamp => Timestamp.ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture);
    }
}
