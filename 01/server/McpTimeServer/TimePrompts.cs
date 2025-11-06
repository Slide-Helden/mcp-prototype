using System.Collections.Generic;
using System.ComponentModel;
using Microsoft.Extensions.AI;
using ModelContextProtocol.Server;

namespace McpTimeServer;

/// <summary>
/// Collection of MCP prompts that guide the LLM towards the demo scenarios.
/// </summary>
[McpServerPromptType]
public static class TimePrompts
{
    private const string SystemGuidance =
        "Du unterstuetzt Menschen bei Zeit- und Terminfragen. " +
        "Nutze Werkzeuge und Ressourcen des MCP-Servers, wenn sie helfen: " +
        "1) Verwende `time/city/{city}` um Kontext zur Stadt und Zeitzone zu erhalten. " +
        "2) Nutze das Tool `time.now`, um aktuelle Zeiten zu pruefen. " +
        "Antworte nachvollziehbar und gib klare naechste Schritte an.";

    [McpServerPrompt(
        Name = "time.prepare_response",
        Title = "Antwortvorlage fuer Zeitfragen")]
    [Description("Bereitet eine System-/User-Nachricht vor, die das Modell zur Nutzung von MCP-Tools lenkt.")]
    public static IEnumerable<ChatMessage> PrepareResponse(
        string? timezone = null,
        string? request = null)
    {
        var tz = string.IsNullOrWhiteSpace(timezone) ? "UTC" : timezone.Trim();
        var focus = string.IsNullOrWhiteSpace(request)
            ? "Liefere eine freundliche Antwort mit konkreter Zeitangabe."
            : request.Trim();

        yield return new ChatMessage(ChatRole.System, $"{SystemGuidance} Ziehe besonders die Zeitzone \"{tz}\" in Betracht.");
        yield return new ChatMessage(
            ChatRole.User,
            $"Hilf mir bei folgender Aufgabe:\n{focus}\n\n" +
            $"Nutze dafuer das Tool `time.now` mit der Zeitzonen-ID \"{tz}\" und pruefe bei Bedarf Ressourcen fuer Kontext.");
    }

    [McpServerPrompt(
        Name = "time.city_briefing",
        Title = "Kurzbriefing zu einer Stadt und ihren Zeiten")]
    [Description("Erzeugt einen Prompt, der den Agenten zu einem strukturierten Steckbrief incl. Zeitzonen-Hinweisen anleitet.")]
    public static IEnumerable<ChatMessage> CityBriefing(string city, string? scenario = null)
    {
        var cityLabel = string.IsNullOrWhiteSpace(city) ? "Berlin" : city.Trim();
        var mission = string.IsNullOrWhiteSpace(scenario)
            ? "Erstelle einen praegnanten Ueberblick fuer Reisende."
            : scenario.Trim();

        yield return new ChatMessage(ChatRole.System, $"{SystemGuidance} Greife fuer Fakten auf Ressourcen wie `time/city/{{city}}` zurueck.");
        yield return new ChatMessage(
            ChatRole.User,
            $"Bereite ein Briefing fuer \"{cityLabel}\" vor.\n" +
            $"Kontext: {mission}\n" +
            "Zeige lokale Zeitangaben, nenne typische Begruessungsfloskeln und empfehle sinnvolle Kontaktzeiten.");
    }
}
