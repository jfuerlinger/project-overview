# Projekt-Verwaltung

Persönliche Verwaltung mehrerer Projekte mit Arbeitspaketen und Meilensteinen, inklusive Abhängigkeiten zwischen diesen Elementen. Single-User.

## Language

**Projekt**:
Ein abgeschlossenes Vorhaben mit Arbeitspaketen und Meilensteinen. Wiederkehrende Aufgaben sind kein Projekt.
_Avoid_: Vorhaben, Bereich, Daueraufgabe

**Arbeitspaket**:
Arbeit mit Dauer und Status, die genau einem Projekt zugeordnet ist. Kann Unterarbeitspakete haben.
_Avoid_: Task, Aufgabe, Ticket

**Unterarbeitspaket**:
Ein Arbeitspaket, das einem übergeordneten Arbeitspaket zugeordnet ist und dessen Arbeit weiter untergliedert.
_Avoid_: Subtask, Teilaufgabe

**Meilenstein**:
Ein Ereignis ohne Dauer mit Zieldatum, das genau einem Projekt zugeordnet ist. Eigener Typ mit eigener Visualisierung, kein Arbeitspaket.
_Avoid_: Milestone, Termin, Deadline

**Abhängigkeit**:
Eine gerichtete Beziehung "Finish-to-Start": das abhängige Element kann erst beginnen, wenn das Vorgänger-Element abgeschlossen ist. Besteht zwischen Arbeitspaketen und Meilensteinen (auch projektübergreifend) sowie grob zwischen Projekten. Zyklen sind unzulässig.
_Avoid_: Verknüpfung, Beziehung, Blocker

**Projekt-Abhängigkeit**:
Eine Abhängigkeit zwischen zwei Projekten: kein Arbeitspaket des abhängigen Projekts ist startbar, solange das Vorgänger-Projekt nicht abgeschlossen ist.
_Avoid_: Programm-Abhängigkeit

## Status

**Status**:
Manuell gesetzter Zustand eines Arbeitspakets oder Projekts: `offen`, `in Arbeit`, `erledigt` oder `verworfen`. Bei Elternpaketen aus den Unterarbeitspaketen abgeleitet.
_Avoid_: Phase, Zustand

**Blockiert**:
Berechnete Eigenschaft eines Elements, das mindestens einen nicht erledigten Vorgänger hat. Wird nie manuell gesetzt.
_Avoid_: Wartend, gesperrt

**Erreicht**:
Berechneter Zustand eines Meilensteins, sobald alle seine Vorgänger erledigt sind. Wird nie manuell gesetzt.
_Avoid_: Abgehakt, fertig

**Verworfen**:
Ein Arbeitspaket oder Projekt, das nicht mehr verfolgt wird. Bleibt sichtbar und gibt seine Nachfolger frei, als wäre es erledigt.
_Avoid_: Abgebrochen, gelöscht
