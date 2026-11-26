# Changelog

## 1.0.2 – 2026-11-25

- Autodrive rechnet mit Spielzeit statt Systemzeit. Freifahren, Schonzeiten und Wartezeiten passen jetzt auch
  bei Spieltempo 0,25x bis 5x. Bei Pause verschwinden Fahrzeuge nicht mehr nach 5 s aus der Liste.
- Tempo-Faktor wirkt auch, wenn du selbst fährst, nicht nur mit Autodrive. Beim Aussteigen gilt wieder der
  Originalwert.
- Tempomat endet beim Aussteigen. Er lenkt nicht und würde ohne Fahrer geradeaus weiterfahren.
- Abladen aus der Ferne bremst dein Fahrzeug nicht mehr, während du selbst fährst.
- README: Bei einem Update von 1.0.0 die `winhttp.dll` von BepInEx löschen.

## 1.0.1 – 2026-11-25

- Release-Zip enthält jetzt alles zum Starten: Unity Doorstop (`version.dll`), .NET-6-Runtime und `dobby.dll`.
  BepInEx muss nicht mehr extra installiert werden.
- `dobby.dll` wird aus dem Ordner `MoonMenu` geladen. Ältere Installationen mit `BepInEx\core\dobby.dll`
  funktionieren weiter.
- Lizenztexte der mitgelieferten Komponenten liegen unter `MoonMenu\licenses`.

## 1.0.0 – 2026-11-25

Erste Veröffentlichung. Getestet mit Moon Sweepers Steam-Build 26682829.

- Ingame-Menü (F1) mit den Tabs Spieler, Geld, Putzen, Fahrzeug, Autodrive, Teleport und Welt
- Autodrive: Tempomat und Bahnen-Modus mit automatischem Bahnabstand aus der Bürstenbreite
- Nur Dreck fahren: Bahnen werden vorher abgetastet, saubere Bahnen und Abschnitte übersprungen
- Nachputzen der Reststellen nach dem Feld
- Ausweichen vor Hindernissen, Freifahren, Aufrichten nach Umkippen
- Abladen: automatisch zur Garage und zurück oder aus der Ferne; Warten bei belegter Garage
- Mehrere Fahrzeuge fahren gleichzeitig ohne Fahrer; Liste mit Stopp-Knöpfen; wieder Einsteigen möglich
