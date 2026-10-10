# Blockiert und Erreicht werden berechnet, nie manuell gesetzt

Der manuelle Status kennt nur `offen`, `in Arbeit`, `erledigt` und `verworfen`. "Blockiert" (Element mit nicht erledigtem Vorgänger) und "Erreicht" (Meilenstein mit allen Vorgängern erledigt) werden ausschließlich aus dem Abhängigkeits-Graphen berechnet. Ein manuell gepflegter "blockiert"-Status würde mit dem Graphen auseinanderlaufen; die Wahrheit liegt in den Abhängigkeiten.

## Consequences

- Statusänderungen eines Vorgängers wirken sofort auf alle Nachfolger, ohne dass Daten nachgezogen werden müssen.
- Es gibt keine Möglichkeit, ein Element gegen den Graphen als "blockiert" zu markieren; Gründe außerhalb des Graphen (z. B. externe Wartezeit) müssen als Arbeitspaket oder Meilenstein modelliert werden.
