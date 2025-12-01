# Testplan: Google News erreichbar?

Ziel: Der Host/LLM soll automatisiert pruefen, ob die Google-News-Startseite erreichbar ist und Inhalte wie erwartet zurueckliefert.

## Schritte
- **resolve-target:** Ziel-URL bestimmen. Standard: `https://news.google.com` oder Umgebungsvariable `TESTPLAN_TARGET_URL`.
- **http-get:** HTTP GET senden, Redirects folgen, User-Agent `mcp-testplan/1.0` setzen.
- **status-check:** Statuscode 2xx oder 3xx akzeptieren.
- **content-fetch:** Body lesen (max. 12 KB).
- **content-check:** Schlagwortsuche nach `Google News` oder `News` im HTML.

## Hinweise fuer LLM-Hosts
- Nutze `tools/call` mit `tests.run` und `plan=google-news`.
- Lies das Plan-Dokument vorher via `resources/read` (`tests/plan/google-news`), um dem LLM Kontext und Akzeptanzkriterien zu geben.
- Passe das Ziel per Env `TESTPLAN_TARGET_URL` an (z. B. Spiegel-Frontpage), der Plan bleibt identisch.
- Werte die Schritt-Details aus (`status`, `durationMs`, `summary`), nicht nur den Gesamtstatus.
