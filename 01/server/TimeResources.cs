using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace McpTimeServer;

/// <summary>
/// Sample MCP resources that provide contextual data for the demo.
/// Includes direct resources and URI-templated variants.
/// </summary>
[McpServerResourceType]
public static class TimeResources
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private static readonly IDictionary<string, string[]> CityTimeZoneMap =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["berlin"] = ["Europe/Berlin", "W. Europe Standard Time"],
            ["new-york"] = ["America/New_York", "Eastern Standard Time"],
            ["sydney"] = ["Australia/Sydney", "AUS Eastern Standard Time"],
            ["seoul"] = ["Asia/Seoul", "Korea Standard Time"],
            ["sao-paulo"] = ["America/Sao_Paulo", "E. South America Standard Time"]
        };

    [McpServerResource(
        Name = "time.about",
        Title = "Ueber diesen MCP-Demo-Server",
        MimeType = "text/markdown",
        UriTemplate = "time/about")]
    [Description("Liefert eine kurze Markdown-Beschreibung, wie Tools, Prompts und Ressourcen zusammenspielen.")]
    public static string About()
    {
        return
        """
        # MCP Zeit-Demo

        Dieser Server zeigt:

        - **Tools** (`time.now`) liefern Live-Daten.
        - **Prompts** geben dem Modell gut strukturierte Startnachrichten.
        - **Resources** ergaenzen Fakten wie Zeitzonen oder Staedteprofile.

        Kombiniere alle drei Bausteine, um Antworten konsistent und ueberpruefbar zu machen.
        """;
    }

    [McpServerResource(
        Name = "time.cities",
        Title = "Beispielstaedte & Zeitzonen",
        MimeType = "text/markdown",
        UriTemplate = "time/cities")]
    [Description("Listet Staedte auf, die zusammen mit dem Resource-Template `time/city/{city}` genutzt werden koennen.")]
    public static string Cities()
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Demo-Staedte");
        builder.AppendLine();

        foreach (var (cityKey, tzIds) in CityTimeZoneMap.OrderBy(kv => kv.Key))
        {
            var displayName = ToTitleCase(cityKey.Replace('-', ' '));
            builder.Append("- ");
            builder.Append(displayName);
            builder.Append(" -> ");
            builder.AppendLine(string.Join(", ", tzIds));
        }

        builder.AppendLine();
        builder.AppendLine("Verwende `read_resource time/city/{city}` fuer Details in JSON.");
        return builder.ToString();
    }

    [McpServerResource(
        Name = "time.city",
        Title = "Staedte-Steckbrief mit Zeitzone",
        MimeType = "application/json",
        UriTemplate = "time/city/{city}")]
    [Description("Liefert JSON-Daten zur Stadt inklusive aufgeloester Zeitzone, lokaler Zeit und Beispielaufrufen.")]
    public static string City(string city)
    {
        if (string.IsNullOrWhiteSpace(city))
        {
            return JsonSerializer.Serialize(new
            {
                error = "city parameter missing",
                hint = "Rufe z. B. /time/city/berlin auf."
            }, JsonOptions);
        }

        var key = city.Trim();
        if (!CityTimeZoneMap.TryGetValue(key, out var candidates))
        {
            return JsonSerializer.Serialize(new
            {
                city = ToTitleCase(key.Replace('-', ' ')),
                error = "unknown city",
                knownCities = CityTimeZoneMap.Keys.OrderBy(c => c)
            }, JsonOptions);
        }

        var tzInfo = ResolveTimeZone(candidates);
        var utcNow = DateTimeOffset.UtcNow;

        if (tzInfo is null)
        {
            return JsonSerializer.Serialize(new
            {
                city = ToTitleCase(key.Replace('-', ' ')),
                error = "timezone not available on this platform",
                attemptedIds = candidates
            }, JsonOptions);
        }

        var local = TimeZoneInfo.ConvertTime(utcNow, tzInfo);
        var offset = tzInfo.GetUtcOffset(utcNow);

        return JsonSerializer.Serialize(new
        {
            city = ToTitleCase(key.Replace('-', ' ')),
            timezoneId = tzInfo.Id,
            utcNow = utcNow.ToString("o", CultureInfo.InvariantCulture),
            localTime = local.ToString("o", CultureInfo.InvariantCulture),
            offsetHours = Math.Round(offset.TotalHours, 2),
            observesDaylightSaving = tzInfo.SupportsDaylightSavingTime,
            recommendedToolCall = $"time.now timezone=\"{tzInfo.Id}\""
        }, JsonOptions);
    }

    private static TimeZoneInfo? ResolveTimeZone(IEnumerable<string> ids)
    {
        foreach (var id in ids)
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(id);
            }
            catch (TimeZoneNotFoundException)
            {
                // Continue with next candidate.
            }
            catch (InvalidTimeZoneException)
            {
                // Continue with next candidate.
            }
        }

        return null;
    }

    private static string ToTitleCase(string value)
    {
        var textInfo = CultureInfo.InvariantCulture.TextInfo;
        return textInfo.ToTitleCase(value);
    }
}