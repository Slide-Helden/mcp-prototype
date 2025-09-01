namespace McpTimeServer
{
    public record TimeNowResult(
        string IsoUtc,
        string IsoLocal,
        string LocalFormatted,
        string TimeZone,
        long EpochSecondsUtc
    );
}