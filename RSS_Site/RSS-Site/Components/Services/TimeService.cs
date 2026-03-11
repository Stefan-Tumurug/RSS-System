namespace RssSite.Components.Services
{
    public class TimeService(ILogger<TimeService> logger)
    {
        private readonly ILogger<TimeService> _logger = logger;

        public int GetTimezoneOffsetInMinutes()
        {
            try
            {
                int timezoneOffset = (int)TimeZoneInfo.Local.GetUtcOffset(DateTime.UtcNow).TotalMinutes * -1;
                _logger.LogDebug("[TIME SERVICE] Current timezone offset: {TimezoneOffset} minutes", timezoneOffset);
                return timezoneOffset;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[TIME SERVICE] Error getting timezone offset.");
                return 0;
            }
        }

        public DateTime AdjustToLocalTime(DateTime utcDateTime)
        {
            try
            {
                if (utcDateTime.Kind == DateTimeKind.Unspecified)
                {
                    utcDateTime = DateTime.SpecifyKind(utcDateTime, DateTimeKind.Utc);
                }

                DateTime localTime = utcDateTime.ToLocalTime();
                _logger.LogDebug("[TIME SERVICE] Converted UTC {UtcDateTime} to local {LocalTime}", utcDateTime, localTime);
                return localTime;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[TIME SERVICE] Error converting UTC time to local.");
                return utcDateTime;
            }
        }

        public string FormatDateTime(DateTime? dateTime, bool includeTime = true)
        {
            if (dateTime == null)
            {
                return "Never";
            }

            DateTime adjustedTime = AdjustToLocalTime(dateTime.Value);
            string formattedTime = includeTime
                ? adjustedTime.ToString("yyyy-MM-dd HH:mm:ss")
                : adjustedTime.ToString("yyyy-MM-dd");

            _logger.LogDebug("[TIME SERVICE] Formatted DateTime: {FormattedTime}", formattedTime);
            return formattedTime;
        }

        public DateTime ConvertToUtc(DateTime localDateTime)
        {
            try
            {
                if (localDateTime.Kind == DateTimeKind.Unspecified)
                {
                    localDateTime = DateTime.SpecifyKind(localDateTime, DateTimeKind.Local);
                }

                DateTime utcTime = localDateTime.ToUniversalTime();
                _logger.LogDebug("[TIME SERVICE] Converted local {LocalDateTime} to UTC {UtcTime}", localDateTime, utcTime);
                return utcTime;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[TIME SERVICE] Error converting local time to UTC.");
                return localDateTime;
            }
        }
    }
}
