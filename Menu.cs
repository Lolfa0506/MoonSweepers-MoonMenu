using System;
using System.IO;
using System.Text;
using System.Threading;

namespace MoonMenu
{
    // Hotkeys (GetAsyncKeyState, nur wenn das Spiel im Vordergrund ist).
    // Bedient wird das Menü ingame mit der Maus (Overlay). Keine Enter-/Pfeiltasten,
    // weil das Spiel diese selbst nutzt (Chat, Platzieren).
    internal static class Menu
    {
        const int VK_SHIFT = 0x10, VK_INSERT = 0x2D, VK_F1 = 0x70;
        static readonly bool[] Down = new bool[256];

        public static volatile bool Open;

        static readonly float[] CleanSteps = { 1f, 2f, 5f, 10f, 25f, 50f, 100f };

        public static void Start()
        {
            var t = new Thread(Loop) { IsBackground = true, Name = "MoonMenu" };
            t.Start();
        }

        static void Loop()
        {
            int tick = 0;
            bool consoleShown = false;
            int playerSeenAt = -1;
            while (true)
            {
                try
                {
                    if (GameFocused()) HandleKeys();
                    else Array.Clear(Down, 0, Down.Length);

                    // Notlösung: Wenn 10 s nach Spielerfund nie ingame gezeichnet wurde -> Konsole
                    if (State.PlayerFound && playerSeenAt < 0) playerSeenAt = Environment.TickCount;
                    if (!consoleShown && !Overlay.EverDrawn && playerSeenAt >= 0 && Environment.TickCount - playerSeenAt > 10_000)
                    {
                        consoleShown = OpenConsole();
                        Log.Write("Kein OnGUI erreicht -> Konsolen-Anzeige.");
                    }
                    if (consoleShown && tick++ % 5 == 0) DrawConsole();
                }
                catch (Exception e) { Log.Write("Menü: " + e.Message); }
                Thread.Sleep(30);
            }
        }

        public static bool GameFocused()
        {
            var fg = Win.GetForegroundWindow();
            if (fg == IntPtr.Zero) return false;
            if (fg == Win.GetConsoleWindow()) return true;
            Win.GetWindowThreadProcessId(fg, out var pid);
            return pid == (uint)Environment.ProcessId;
        }

        static bool Pressed(int vk)
        {
            bool now = (Win.GetAsyncKeyState(vk) & 0x8000) != 0;
            bool was = Down[vk];
            Down[vk] = now;
            return now && !was;
        }

        public static float StepClean(float cur, int d)
        {
            int i = Array.IndexOf(CleanSteps, cur);
            if (i < 0) i = 0;
            return CleanSteps[Math.Clamp(i + d, 0, CleanSteps.Length - 1)];
        }

        static void HandleKeys()
        {
            bool shift = (Win.GetAsyncKeyState(VK_SHIFT) & 0x8000) != 0;

            if (Pressed(VK_F1)) Open = !Open;

            // Schnelltasten
            if (Pressed(VK_F1 + 1)) State.Fly = !State.Fly;
            if (Pressed(VK_F1 + 2)) State.FlySpeedMul = Math.Clamp(State.FlySpeedMul - 0.5f, 0.5f, 10f);
            if (Pressed(VK_F1 + 3)) State.FlySpeedMul = Math.Clamp(State.FlySpeedMul + 0.5f, 0.5f, 10f);
            if (Pressed(VK_F1 + 4)) State.WalkSpeedMul = Math.Clamp(State.WalkSpeedMul - 0.25f, 1f, 5f);
            if (Pressed(VK_F1 + 5)) State.WalkSpeedMul = Math.Clamp(State.WalkSpeedMul + 0.25f, 1f, 5f);
            if (Pressed(VK_F1 + 6)) { if (shift) State.WidthMul = Math.Clamp(State.WidthMul - 0.5f, 1f, 4f); else State.CleanMul = StepClean(State.CleanMul, -1); }
            if (Pressed(VK_F1 + 7)) { if (shift) State.WidthMul = Math.Clamp(State.WidthMul + 0.5f, 1f, 4f); else State.CleanMul = StepClean(State.CleanMul, +1); }
            if (Pressed(VK_F1 + 8)) State.QueueMoney(shift ? 1_000_000m : 10_000m);
            if (Pressed(VK_F1 + 9)) State.InfiniteBoosts = !State.InfiniteBoosts;
        }

        // ---------------- Konsolen-Notlösung ----------------

        static bool OpenConsole()
        {
            try
            {
                Win.AllocConsole();
                Win.SetConsoleTitleW("MoonMenu");
                Console.OutputEncoding = Encoding.UTF8;
                Console.SetOut(new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false)) { AutoFlush = true });
                try { Console.CursorVisible = false; } catch { }
                return true;
            }
            catch (Exception e) { Log.Write("Konsole: " + e.Message); return false; }
        }

        static void DrawConsole()
        {
            var sb = new StringBuilder();
            sb.AppendLine("  ===== MoonMenu (Konsole) =====");
            sb.AppendLine(State.PlayerFound ? $"  Geld: {State.Balance:N0} $" : "  Warte auf Spieler ...");
            sb.AppendLine($"  [F2] Fly {(State.Fly ? "AN" : "aus")}   [F3/F4] Fly-Speed {State.FlySpeedMul:0.0}x");
            sb.AppendLine($"  [F5/F6] Laufspeed {State.WalkSpeedMul:0.00}x   [F7/F8] Putzstärke {State.CleanMul:0}x");
            sb.AppendLine($"  [Shift+F7/F8] Putzbreite {State.WidthMul:0.0}x   [F9] +10.000 $   [F10] Boosts {(State.InfiniteBoosts ? "AN" : "aus")}");
            var outp = new StringBuilder();
            foreach (var l in sb.ToString().Split('\n')) outp.Append(l.TrimEnd('\r').PadRight(80)).Append('\n');
            try { Console.SetCursorPosition(0, 0); } catch { }
            Console.Write(outp.ToString());
        }
    }
}
