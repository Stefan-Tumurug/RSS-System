namespace RSSAPI.Models
{
    public class Logs
    {
        public int ID { get; set; }
        public string MacAddress { get; set; } = string.Empty;
        public string Action { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; }
    }
}