# MCP Demo-Architektur

## Grundlegendes Kommunikationsmodell

Alle Demos basieren auf dem gleichen Architekturprinzip:

```
┌──────────┐      MCP/SSE       ┌──────────┐      HTTP/JSON     ┌─────┐
│  CLIENT  │◄──────────────────►│  SERVER  │◄──────────────────►│ LLM │
└──────────┘                    └──────────┘                    └─────┘
     ▲                                                              ▲
     │ Eingabe                                          Tool-Calls  │
     ▼                                                              ▼
┌──────────┐
│   USER   │
└──────────┘
```

## MCP-Bausteine

| Baustein | Beschreibung | Beispiel |
|----------|--------------|----------|
| **Tools** | Ausführbare Funktionen | `time.now`, `docs.search` |
| **Resources** | Daten/Kontext (URI-basiert) | `time/about`, `docs/catalog` |
| **Prompts** | Vordefinierte Workflows | `time.prepare_response` |

## Zwei Steuerungsmodelle

### LLM-first (Demo 01, 02, 04)
Das LLM entscheidet autonom, welche Tools/Resources es aufruft:
```
USER -> Frage -> LLM entscheidet -> Tool-Call -> MCP-Server -> Antwort
```

### Orchestrator-first (Demo 03, 12)
Der Operator/Code steuert jeden Schritt bewusst:
```
USER -> Menü-Auswahl -> Client ruft MCP -> Server -> Ergebnis
           (optional am Ende: LLM-Zusammenfassung)
```

## Technologie-Stack

- **Runtime:** .NET 10
- **MCP SDK:** ModelContextProtocol 0.4.1-preview.1
- **Transport:** HTTP/SSE (Server-Sent Events)
- **LLM-Integration:** Microsoft.Extensions.AI + OpenAI-kompatible API

## Protokoll-Details

MCP nutzt JSON-RPC 2.0 über HTTP:

```json
// Request
{"jsonrpc":"2.0","id":1,"method":"tools/list","params":{}}

// Response
{"jsonrpc":"2.0","id":1,"result":{"tools":[...]}}
```

Typische MCP-Methoden:
- `initialize` - Handshake
- `tools/list` - Verfügbare Tools abfragen
- `tools/call` - Tool ausführen
- `resources/list` - Verfügbare Resources abfragen
- `resources/read` - Resource lesen
- `prompts/list` - Verfügbare Prompts abfragen
- `prompts/get` - Prompt abrufen

## Demo-Übersicht (Roter Faden)

| Demo   |           Fokus                        |        Ausbaustufe           |
|--------|----------------------------------------|------------------------------|
| **01** | Einstieg: LLM-first mit Zeit-Tools     | Basis                        |
| **02** | LLM-first mit Dokumenten + Quellen     | +Dokumente, +Suche           |
| **03** | Orchestrator-first (Kontrast zu 01/02) | Steuerungsmodell-Wechsel     |
| **04** | LLM-first + Live-Trace                 | +Transparenz, +Debugging     |
| **12** | MCP ohne KI (reiner Operator-Flow)     | MCP als Standard-Connector   |
| **13** | Trace-Demo (Protokoll-Inspektion)      | +Protokoll-Details           |
| **14** | Testplan-Katalog (nur lesen)           |  +Use-Case: Testing          |
| **15** | Multi-Server-Orchestrierung            | +Skalierung                  |
