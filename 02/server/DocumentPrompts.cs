using System.Collections.Generic;
using System.ComponentModel;
using Microsoft.Extensions.AI;
using ModelContextProtocol.Server;

namespace DocServer;

[McpServerPromptType]
public static class DocumentPrompts
{
    private const string SystemRule =
        "Du arbeitest als Dokumenten-Concierge. " +
        "Nutze MCP Ressourcen, um Inhalte nachzuschlagen, bevor du antwortest. " +
        "Werkzeuge stehen dir zur Suche nach Stichwoertern zur Verfuegung. " +
        "Beziehe dich in Antworten auf die Quelle, indem du auf docs/catalog oder docs/document/{id} verweist.";

    [McpServerPrompt(
        Name = "docs.find_humor",
        Title = "Witzsuche mit Recherche")]
    [Description("Fuehrt das Modell durch eine strukturierte Recherche nach passenden Witzen.")]
    public static IEnumerable<ChatMessage> FindHumor(string? topic = null)
    {
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] [Prompt] docs.find_humor aufgerufen (topic={topic ?? "null"})");
        var focus = string.IsNullOrWhiteSpace(topic)
            ? "Finde einen passenden Witz, der gute Laune verspricht."
            : $"Finde einen passenden Witz zum Thema \"{topic.Trim()}\".";

        yield return new ChatMessage(ChatRole.System, SystemRule);
        yield return new ChatMessage(
            ChatRole.User,
            $"{focus}\n" +
            "1. Liste relevante Dokumente ueber docs.search oder docs.catalog auf.\n" +
            "2. Lies das beste Dokument mit read_resource docs/document/{id}.\n" +
            "3. Fasse den Witz kurz zusammen und gib den Quellpfad an.");
    }

    [McpServerPrompt(
        Name = "docs.summarize_document",
        Title = "Dokument zusammenfassen")]
    [Description("Leitet das Modell dazu an, ein Dokument zu lesen und strukturiert zusammenzufassen.")]
    public static IEnumerable<ChatMessage> Summarize(string documentId)
    {
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] [Prompt] docs.summarize_document aufgerufen (documentId={documentId ?? "null"})");
        yield return new ChatMessage(ChatRole.System, SystemRule);
        yield return new ChatMessage(
            ChatRole.User,
            "Lies das Dokument ueber read_resource docs/document/{documentId} und erstelle danach eine Zusammenfassung in drei Stichpunkten. " +
            "Pruefe vorab ueber docs.summary/{documentId}, ob das Dokument existiert. " +
            $"Ziel-Dokument: {documentId}");
    }
}
