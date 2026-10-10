# Blazor (Interactive Server) und Aspire auf .NET

Die Anwendung wird durchgängig in .NET gebaut: Blazor mit Interactive Server als UI, eine eigenständige API mit SQLite als Speicher und eine .NET-CLI (siehe ADR-0003). Aspire orchestriert die Bestandteile lokal (API, UI, Telemetrie-Dashboard). Gewählt wurde das, weil es das Ökosystem ist, das der Betreiber am besten beherrscht; eine einzige Sprache und ein gemeinsamer Vertrag für UI, API und CLI halten den Aufwand für ein persönliches Projekt klein.

Interactive Server statt WebAssembly: die App ist lokal und Single-User, ein WASM-Download und zusätzliche Client-seitige Komplexität bringen keinen Nutzen.

## Considered Options

- **TypeScript-Stack (SvelteKit/Next.js mit Drizzle)**: verworfen, weil es nicht dem Ökosystem des Betreibers entspricht.
- **Blazor WebAssembly oder Auto**: verworfen, kein Nutzen bei lokalem Single-User-Betrieb.

## Consequences

- API-Vertrag (OpenAPI) und generierter Client lassen sich zwischen Blazor-UI und CLI teilen.
- Der Graph (Cytoscape.js) wird über JS-Interop angebunden und bleibt in einer Komponente gekapselt.
- Aspire ist Voraussetzung für den Entwicklungsbetrieb; ein Betrieb ohne Aspire (z. B. reine `dotnet run`-Starts) muss bewusst eingerichtet werden.
