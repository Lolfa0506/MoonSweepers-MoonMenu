# MoonMenu – Mod-Menü & Autodrive für Moon Sweepers

Ingame-Mod-Menü für **Moon Sweepers** (Steam) mit Autodrive für die Putzfahrzeuge. Mehrere Fahrzeuge können
gleichzeitig ohne Fahrer ein Feld abfahren, Hindernissen ausweichen, nur dreckige Stellen putzen und selbst
zum Abladen fahren.

*English: in-game mod menu for Moon Sweepers with multi-vehicle autodrive (lane cleaning, dirt detection,
obstacle avoidance, automatic unloading). The UI is in German. Install steps are below.*

> Inoffizielle Fan-Mod, nicht verbunden mit DragDrop Games. Nur für die Steam-Version.

## Funktionen

Menü öffnen/schließen mit **F1**, Bedienung mit der Maus. Das Fenster lässt sich an der Kopfzeile verschieben.

| Tab | Inhalt |
|---|---|
| **Spieler** | Fliegen, Fluggeschwindigkeit, Laufgeschwindigkeit, unendlich Boosts |
| **Geld** | Kontostand, +10 Tsd. / +100 Tsd. / +1 Mio. / +10 Mio. |
| **Putzen** | Putzstärke 1–100x, Bürstenbreite 1–4x |
| **Fahrzeug** | Tank-Anzeige, automatisch abladen (hinfahren oder aus der Ferne), Tempo-Faktor, Tempomat-Upgrade |
| **Autodrive** | Tempomat oder Bahnen-Modus, Feld-Einstellungen, Ausweichen, Liste aller fahrenden Fahrzeuge |
| **Teleport** | 3 Positions-Slots |
| **Welt** | Spieltempo |

### Autodrive (Bahnen-Modus)

- Fährt ein Feld in parallelen Bahnen ab (Länge, Anzahl, Versatz links/rechts/keiner).
- **Bahnabstand automatisch** aus der Bürstenbreite minus Überlappung, oder fest ab 0,5 m.
- **Nur Dreck fahren:** Jede Bahn wird vorher abgetastet. Saubere Bahnen werden übersprungen, gefahren wird
  nur vom ersten bis zum letzten Dreck. Über saubere Lücken fährt es schneller.
- **Nachputzen:** Am Ende wird das Feld gerastert und Reststellen werden einzeln angefahren (bis 3 Runden).
- **Hindernisse:** erkennt Felsen, Gebäude und andere Fahrzeuge mit Kugel-Sensoren und weicht aus. Wenn
  es festsitzt, setzt es zurück. Eine blockierte Bahn wird ausgelassen. Ein umgekipptes Fahrzeug richtet es auf.
- **Abladen:** Bei vollem Tank fährt es zur nächsten Garage, lädt ab und fährt zurück. Ist die Garage
  belegt, wartet es davor.
- **Mehrere Fahrzeuge:** Bahnen-Modus starten, aussteigen. Das Fahrzeug fährt allein weiter. Im nächsten
  Fahrzeug das Gleiche. Unter „FAHREN GERADE“ stehen alle mit Status und Stopp-Knopf. Wieder einsteigen geht
  jederzeit. **S** im Fahrzeug stoppt dessen Autodrive. Der Tempomat lenkt nicht und endet deshalb beim
  Aussteigen.
- Alle Zeiten (Freifahren, Wartezeiten) laufen in Spielzeit und passen sich dem Spieltempo an.

### Schnelltasten

| Taste | Funktion |
|---|---|
| F1 | Menü |
| F2 | Fliegen an/aus |
| F3 / F4 | Fluggeschwindigkeit − / + |
| F5 / F6 | Laufgeschwindigkeit − / + |
| F7 / F8 | Putzstärke − / +  (mit Shift: Bürstenbreite) |
| F9 | +10.000 $  (mit Shift: +1 Mio. $) |
| F10 | Unendlich Boosts |
| S | Autodrive des eigenen Fahrzeugs stoppen |

## Voraussetzungen

- Moon Sweepers (Steam), getestet mit Build 26682829 (Unity 6, IL2CPP)
- Windows x64
- Sonst nichts: Das Release-Zip enthält alles zum Starten, also Loader (Unity Doorstop), .NET-Runtime
  und Hook-Bibliothek (Dobby). Siehe [THIRD-PARTY-NOTICES](THIRD-PARTY-NOTICES.md).

## Installation

1. Das neueste `MoonMenu-v….zip` aus den [Releases](../../releases/latest) herunterladen.
2. Den Spielordner öffnen: Steam → Moon Sweepers → Rechtsklick → Verwalten → Lokale Dateien durchsuchen
   (dort liegt `MoonSweepers.exe`).
3. Den **Inhalt** des Zips direkt in diesen Ordner entpacken. Wenn Windows fragt, Dateien ersetzen.
4. Spiel über Steam starten, Spielstand laden, **F1** drücken.

Danach sieht der Spielordner so aus:

```
Moon Sweepers\
  MoonSweepers.exe
  version.dll            Loader (Unity Doorstop)
  .doorstop_version
  doorstop_config.ini    sagt dem Loader, dass er MoonMenu starten soll
  dotnet\                .NET-Runtime
  MoonMenu\
    MoonMenu.dll         die Mod
    dobby.dll            Hook-Bibliothek
    licenses\            Lizenztexte
```

**Update von 1.0.0 oder BepInEx installiert:** `winhttp.dll` im Spielordner löschen. Das ist der Loader von
BepInEx. Bleibt er liegen, laufen zwei Loader gleichzeitig. Das Zip überschreibt `doorstop_config.ini`, und
Doorstop startet dann MoonMenu statt BepInEx. Andere BepInEx-Plugins laufen dann nicht mehr. Der Ordner
`BepInEx` stört nicht.

## Deinstallieren

`version.dll`, `.doorstop_version`, `doorstop_config.ini`, den Ordner `dotnet` und den Ordner `MoonMenu`
löschen. Oder nur ausschalten: in `doorstop_config.ini` `enabled = false` setzen.

## Probleme

- Log: `Moon Sweepers\MoonMenu\MoonMenu.log`. Hier stehen Start, Hooks, Autodrive-Ereignisse
  („Hindernis …", „Festgefahren …", „Abladefahrt …").
- Kein Menü bei F1: Steht im Log `---- MoonMenu startet ----`? Wenn nicht, prüfe, ob `version.dll`,
  `doorstop_config.ini` und `dotnet\` direkt neben `MoonSweepers.exe` liegen und nicht in einem Unterordner
  (das passiert, wenn man den Zip-Ordner selbst statt seines Inhalts entpackt).
- Ein Virenscanner meldet `version.dll`: Das ist der Doorstop-Loader, der sich ins Spiel lädt. Das Verhalten
  ist typisch für Mod-Loader. Quelle und Lizenz stehen in [THIRD-PARTY-NOTICES](THIRD-PARTY-NOTICES.md).
- Nach einem Spiel-Update kann die Mod brechen. Bitte Log anhängen und ein Issue öffnen.

## Mehrspieler

Nur allein oder in privaten Lobbys mit Freunden benutzen, die einverstanden sind. Geld landet in der
Team-Kasse. Nicht in fremden Lobbys verwenden.

## Selbst bauen

```powershell
dotnet build -c Release
```

Benötigt das .NET SDK 6 oder neuer. Existiert das Spiel im Steam-Standardpfad, wird die DLL direkt nach
`<Spiel>\MoonMenu` kopiert. Einen anderen Pfad gibst du mit `-p:GameDir="D:\…\Moon Sweepers"` an.

So funktioniert es: Doorstop startet die .NET-Runtime und ruft `Doorstop.Entrypoint.Start()` auf. Die Mod
spricht das Spiel nur über die exportierte il2cpp-C-API von `GameAssembly.dll` an. Klassen, Felder und
Methoden findet sie zur Laufzeit über ihren Namen. Hooks setzt sie mit Dobby. Das Menü zeichnet sie mit Unity-IMGUI
aus gehookten `OnGUI`-Methoden. Es enthält keine Spieldateien und keinen dekompilierten Spielcode.

## Credits

- [Unity Doorstop](https://github.com/NeighTools/UnityDoorstop) (NeighTools)
- [BepInEx](https://github.com/BepInEx/BepInEx) – aus dessen Build #793 stammen Doorstop und .NET-Runtime im Zip
- [.NET Runtime](https://github.com/dotnet/runtime) (.NET Foundation)
- [Dobby](https://github.com/jmpews/Dobby) – Funktions-Hooks
- Analyse-Werkzeuge: [Cpp2IL](https://github.com/SamboyCoding/Cpp2IL), [AsmResolver](https://github.com/Washi1337/AsmResolver), [Iced](https://github.com/icedland/iced)

**KI-Hinweis:** Code und Doku wurden mit [Claude Code](https://claude.com/claude-code) (Anthropic,
Claude Opus 5.5) erstellt. Getestet wurde im Spiel.

## Lizenz

[MIT](LICENSE)
