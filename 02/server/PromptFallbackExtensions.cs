using System.Collections.Generic;
using Microsoft.Extensions.AI;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;

namespace DocServer;

public static class PromptFallbackExtensions
{
    public static GetPromptResult CreatePromptResult(params ChatMessage[] messages)
    {
        var promptMessages = new List<PromptMessage>();
        foreach (var message in messages)
        {
            promptMessages.AddRange(AIContentExtensions.ToPromptMessages(message));
        }

        return new GetPromptResult
        {
            Messages = promptMessages
        };
    }
}
