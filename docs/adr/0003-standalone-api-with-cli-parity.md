# Eigenständige API; Blazor-UI und CLI sind gleichrangige Clients

Die Fachlogik (Projekte, Arbeitspakete, Meilensteine, Abhängigkeiten, Zyklusprüfung, berechneter Status) liegt in einer eigenständigen API. Die Blazor-Oberfläche (Interactive Server) greift nicht direkt auf die Datenbank zu, sondern ist einer von mehreren Clients dieser API; Aspire orchestriert API und UI. Gewählt wurde das, damit die gleiche Funktionalität ohne Umweg für Menschen (UI) und für Agents (CLI) verfügbar ist.

**Verbindliche Anforderung:** Jede Funktionalität, die ein API-Endpunkt bietet, muss auch über eine CLI aufrufbar sein (Funktionsparität). Neue Endpunkte gelten erst als fertig, wenn es den passenden CLI-Befehl gibt.

**Agent-Skill:** Zur CLI gehört ein eigener Skill, der Agents beschreibt, wie sie mit der CLI arbeiten (Befehle, Ausgabeformat, typische Abläufe). Der Skill wird zusammen mit der CLI gepflegt.

## Considered Options

- **UI greift direkt auf die Datenbank zu, keine API**: einfacher, aber die CLI und damit Agent-Zugriff müssten die Fachlogik duplizieren oder an der UI vorbei die Datenbank beschreiben.

## Consequences

- Eine zusätzliche Projektebene (API) in V1, im Gegenzug genau eine Stelle für Validierung und Zyklusprüfung.
- Die CLI ist ein reiner API-Client; sie enthält keine eigene Fachlogik und keinen eigenen Datenbankzugriff.
- Der API-Vertrag (z. B. OpenAPI) ist die Quelle der Wahrheit für UI, CLI und Skill.
