using ModelContextProtocol.Server;
using System.ComponentModel;

namespace McpTimeServer
{
    [McpServerToolType]
    public static class TimeTools
    {
        [McpServerTool(Name = "time.now")]
        [Description("Gibt aktuelles Datum/Uhrzeit zurück. Optional: 'timezone' (IANA/Windows-ID).")]
        public static TimeNowResult Now(string? timezone = null)
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] [Tool] time.now aufgerufen (timezone={timezone ?? "default"})");
            var utc = DateTimeOffset.UtcNow;

            DateTimeOffset local;
            string tzId;

            try
            {
                if (!string.IsNullOrWhiteSpace(timezone))
                {
                    var tz = TimeZoneInfo.FindSystemTimeZoneById(timezone);
                    local = TimeZoneInfo.ConvertTime(utc, tz);
                    tzId = tz.Id;
                }
                else
                {
                    local = DateTimeOffset.Now;
                    tzId = TimeZoneInfo.Local.Id;
                }
            }
            catch
            {
                // Fallback auf lokale Zeitzone bei ungültiger ID
                local = DateTimeOffset.Now;
                tzId = TimeZoneInfo.Local.Id;
            }

            return new TimeNowResult(
                IsoUtc: utc.ToString("o"),
                IsoLocal: local.ToString("o"),
                LocalFormatted: local.ToString("yyyy-MM-dd HH:mm:ss"),
                TimeZone: tzId,
                EpochSecondsUtc: utc.ToUnixTimeSeconds()
            );
        }
    }
}