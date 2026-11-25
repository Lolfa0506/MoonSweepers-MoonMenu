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
- **Mehrere Fahrzeuge:** Autodrive starten, aussteigen. Das Fahrzeug fährt allein weiter. Im nächsten
  Fahrzeug das Gleiche. Unter „FAHREN GERADE" stehen alle mit Status und Stopp-Knopf. Wieder einsteigen geht
  jederzeit. **S** im Fahrzeug stoppt dessen Autodrive.

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
- **BepInEx 6 Bleeding Edge, IL2CPP x64, Build #793** – nur als Loader-Paket: Es liefert Unity Doorstop
  (`version.dll`), die .NET-Runtime (`dotnet\`) und `dobby.dll`. Der BepInEx-Preloader selbst wird nicht
  benutzt, weil Il2CppInterop mit diesem Spiel nicht läuft.

## Installation

1. BepInEx 6 Bleeding Edge #793 (IL2CPP, win-x64) herunterladen:
   [BepInEx-Unity.IL2CPP-win-x64-6.0.0-be.793+dbc82f0.zip](https://builds.bepinex.dev/projects/bepinex_be/793/BepInEx-Unity.IL2CPP-win-x64-6.0.0-be.793%2Bdbc82f0.zip)
   (Übersicht: <https://builds.bepinex.dev/projects/bepinex_be>)
2. Den Inhalt in den Spielordner entpacken (dort, wo `MoonSweepers.exe` liegt):
   Steam → Moon Sweepers → Rechtsklick → Verwalten → Lokale Dateien durchsuchen.
3. `MoonMenu-v1.0.0.zip` aus den [Releases](../../releases) **in denselben Ordner** entpacken und die
   vorhandene `doorstop_config.ini` überschreiben.
4. Spiel über Steam starten, Spielstand laden, **F1** drücken.

Danach sieht der Spielordner so aus:

```
Moon Sweepers\
  MoonSweepers.exe
  version.dll            (Doorstop, aus BepInEx)
  doorstop_config.ini    (aus MoonMenu)
  dotnet\                (aus BepInEx)
  BepInEx\core\dobby.dll (aus BepInEx)
  MoonMenu\MoonMenu.dll  (aus MoonMenu)
```

## Deinstallieren

`version.dll` und den Ordner `MoonMenu` löschen. Optional auch `BepInEx\`, `dotnet\` und `doorstop_config.ini`.
Oder in `doorstop_config.ini` `enabled = false` setzen.

## Probleme

- Log: `Moon Sweepers\MoonMenu\MoonMenu.log`. Hier stehen Start, Hooks, Autodrive-Ereignisse
  („Hindernis …", „Festgefahren …", „Abladefahrt …").
- Kein Menü bei F1: Steht im Log `---- MoonMenu startet ----`? Wenn nicht, prüfe, ob `doorstop_config.ini`
  auf `MoonMenu\MoonMenu.dll` zeigt und `version.dll` und `dotnet\` vorhanden sind.
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
- [BepInEx](https://github.com/BepInEx/BepInEx) – Loader-Paket mit Doorstop und .NET-Runtime
- [Dobby](https://github.com/jmpews/Dobby) – Funktions-Hooks
- Analyse-Werkzeuge: [Cpp2IL](https://github.com/SamboyCoding/Cpp2IL), [AsmResolver](https://github.com/Washi1337/AsmResolver), [Iced](https://github.com/icedland/iced)

**KI-Hinweis:** Code und Doku wurden mit [Claude Code](https://claude.com/claude-code) (Anthropic,
Claude Opus 5.5) erstellt. Getestet wurde im Spiel.

## Lizenz

[MIT](LICENSE)
