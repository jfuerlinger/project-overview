# Projekt-Abhängigkeit ist eine eigene harte Regel

Eine Abhängigkeit zwischen Projekten ist keine Zusammenfassung der Paket-Abhängigkeiten, sondern eine eigenständige Regel: Kein Arbeitspaket des abhängigen Projekts ist startbar, solange das Vorgänger-Projekt nicht abgeschlossen ist. Paket-Abhängigkeiten verfeinern das nur innerhalb dieses Rahmens. Gewählt wurde das, weil sich so grobe Reihenfolgen zwischen Projekten ausdrücken lassen, ohne jedes Paket einzeln zu verknüpfen.

## Considered Options

- **Aus Paket-Abhängigkeiten abgeleitete Sicht**: verworfen, weil damit keine Reihenfolge zwischen Projekten festgelegt werden kann, bevor Pakete existieren.

## Consequences

- Die Zyklusprüfung muss Projekt- und Paketebene gemeinsam betrachten.
- Die Blockiert-Berechnung eines Pakets berücksichtigt zusätzlich den Status des Vorgänger-Projekts.
