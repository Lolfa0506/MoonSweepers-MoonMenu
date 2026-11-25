using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace MoonMenu
{
    // Ingame-Menü über Unity-IMGUI (im Build sind nur GUI.Label + abgerundetes GUI.DrawTexture erhalten).
    // Aufruf aus gehookten OnGUI-Methoden des Spiels -> Hauptthread. Bedienung mit der Maus (Win32-Cursor).
    //
    // Design: kompaktes Werkzeug-Panel. Neutrales Dunkelgrau, eine Akzentfarbe, dichte Zeilen,
    // kleine Radien, keine Verläufe/Glow, keine Fülltexte.
    internal static unsafe class Overlay
    {
        [StructLayout(LayoutKind.Sequential)] struct Rect { public float X, Y, W, H; public Rect(float x, float y, float w, float h) { X = x; Y = y; W = w; H = h; } }
        [StructLayout(LayoutKind.Sequential)]
        struct Color
        {
            public float R, G, B, A;
            public Color(float r, float g, float b, float a = 1f) { R = r; G = g; B = b; A = a; }
            public Color WithA(float a) => new Color(R, G, B, a);
        }

        const float UiScale = 1.4f;   // Gesamtgröße des Menüs (1.0 = Basis bei 1080p)
        const int EventRepaint = 7;
        const int MiddleLeft = 3, MiddleCenter = 4, MiddleRight = 5;

        // ---------------- Farben ----------------
        static readonly Color CBg = new Color(0.074f, 0.077f, 0.084f, 0.97f);
        static readonly Color CHead = new Color(0.102f, 0.106f, 0.116f, 1f);
        static readonly Color CEdge = new Color(1f, 1f, 1f, 0.09f);
        static readonly Color CHover = new Color(1f, 1f, 1f, 0.035f);
        static readonly Color CTrack = new Color(1f, 1f, 1f, 0.13f);
        static readonly Color CText = new Color(0.87f, 0.88f, 0.90f);
        static readonly Color CDim = new Color(0.50f, 0.52f, 0.56f);
        static readonly Color CFaint = new Color(0.34f, 0.35f, 0.39f);
        static readonly Color CAccent = new Color(1.00f, 0.76f, 0.22f);
        static readonly Color COk = new Color(0.45f, 0.82f, 0.50f);

        // ---------------- il2cpp-Methoden ----------------
        static bool _ready, _failed;
        static IntPtr mLabel, mDrawTexR, mSetContentColor, mGetSkin, mSkinLabel, mStyleCtor, mSetFontSize, mSetAlign, mSetFontStyle;
        static IntPtr mEventCurrent, mEventType, mWhiteTex, mScreenW, mScreenH, mFrameCount, kStyle, _baseLabel, _white;
        static int _lastFrame = -1;
        public static volatile bool EverDrawn;

        static bool Resolve()
        {
            const string R = "UnityEngine.Rect", Str = "System.String", Sty = "UnityEngine.GUIStyle";
            var gui = Il2Cpp.FindClass("UnityEngine.IMGUIModule.dll", "UnityEngine", "GUI");
            var skin = Il2Cpp.FindClass("UnityEngine.IMGUIModule.dll", "UnityEngine", "GUISkin");
            kStyle = Il2Cpp.FindClass("UnityEngine.IMGUIModule.dll", "UnityEngine", "GUIStyle");
            var ev = Il2Cpp.FindClass("UnityEngine.IMGUIModule.dll", "UnityEngine", "Event");
            var tex2d = Il2Cpp.FindClass("UnityEngine.CoreModule.dll", "UnityEngine", "Texture2D");
            var screen = Il2Cpp.FindClass("UnityEngine.CoreModule.dll", "UnityEngine", "Screen");
            var time = Il2Cpp.FindClass("UnityEngine.CoreModule.dll", "UnityEngine", "Time");

            mLabel = Il2Cpp.FindMethod(gui, "Label", R, Str, Sty);
            mDrawTexR = Il2Cpp.FindMethod(gui, "DrawTexture", R, "UnityEngine.Texture", "UnityEngine.ScaleMode", "System.Boolean", "System.Single", "UnityEngine.Color", "System.Single", "System.Single");
            mSetContentColor = Il2Cpp.FindMethod(gui, "set_contentColor", "UnityEngine.Color");
            mGetSkin = Il2Cpp.FindMethod(gui, "get_skin");
            mSkinLabel = Il2Cpp.FindMethod(skin, "get_label");
            mStyleCtor = Il2Cpp.FindMethod(kStyle, ".ctor", Sty);
            mSetFontSize = Il2Cpp.FindMethod(kStyle, "set_fontSize", "System.Int32");
            mSetAlign = Il2Cpp.FindMethod(kStyle, "set_alignment", "UnityEngine.TextAnchor");
            mSetFontStyle = Il2Cpp.FindMethod(kStyle, "set_fontStyle", "UnityEngine.FontStyle");
            mEventCurrent = Il2Cpp.FindMethod(ev, "get_current");
            mEventType = Il2Cpp.FindMethod(ev, "get_type");
            mWhiteTex = Il2Cpp.FindMethod(tex2d, "get_whiteTexture");
            mScreenW = Il2Cpp.FindMethod(screen, "get_width");
            mScreenH = Il2Cpp.FindMethod(screen, "get_height");
            mFrameCount = Il2Cpp.FindMethod(time, "get_frameCount");

            bool ok = mLabel != IntPtr.Zero && mDrawTexR != IntPtr.Zero && mGetSkin != IntPtr.Zero && mSkinLabel != IntPtr.Zero
                      && mStyleCtor != IntPtr.Zero && mEventCurrent != IntPtr.Zero && mEventType != IntPtr.Zero
                      && mWhiteTex != IntPtr.Zero && mScreenW != IntPtr.Zero && mScreenH != IntPtr.Zero;
            Log.Write("Overlay-API " + (ok ? "komplett" : "unvollständig"));
            return ok;
        }

        static int UnboxInt(IntPtr boxed) => boxed == IntPtr.Zero ? 0 : *(int*)Il2Cpp.il2cpp_object_unbox(boxed);

        // ---------------- Zeichen-Primitive ----------------

        static readonly Dictionary<int, IntPtr> Styles = new Dictionary<int, IntPtr>();

        static IntPtr Style(int px, bool bold, int anchor)
        {
            int key = px * 100 + (bold ? 10 : 0) + anchor;
            if (Styles.TryGetValue(key, out var st)) return st;
            st = Il2Cpp.il2cpp_object_new(kStyle);
            IntPtr* a = stackalloc IntPtr[1];
            a[0] = _baseLabel;
            Il2Cpp.Invoke(mStyleCtor, st, a, "GUIStyle.ctor");
            Il2Cpp.il2cpp_gchandle_new(st, false);
            int v = px; a[0] = (IntPtr)(&v);
            if (mSetFontSize != IntPtr.Zero) Il2Cpp.Invoke(mSetFontSize, st, a, "fontSize");
            int al = anchor; a[0] = (IntPtr)(&al);
            if (mSetAlign != IntPtr.Zero) Il2Cpp.Invoke(mSetAlign, st, a, "alignment");
            int fs = bold ? 1 : 0; a[0] = (IntPtr)(&fs);
            if (mSetFontStyle != IntPtr.Zero) Il2Cpp.Invoke(mSetFontStyle, st, a, "fontStyle");
            Styles[key] = st;
            return st;
        }

        static float _s = 1f, _ox, _oy;  // Skalierung + Panel-Ursprung in Pixeln

        static Rect P(float x, float y, float w, float h) => new Rect(_ox + x * _s, _oy + y * _s, w * _s, h * _s);

        static void Box(float x, float y, float w, float h, Color c, float radius = 0f, float border = 0f)
        {
            var r = P(x, y, w, h);
            int scale = 0; byte alpha = 1; float aspect = 0f, bw = border * _s, br = radius * _s;
            IntPtr* a = stackalloc IntPtr[8];
            a[0] = (IntPtr)(&r); a[1] = _white; a[2] = (IntPtr)(&scale); a[3] = (IntPtr)(&alpha);
            a[4] = (IntPtr)(&aspect); a[5] = (IntPtr)(&c); a[6] = (IntPtr)(&bw); a[7] = (IntPtr)(&br);
            Il2Cpp.Invoke(mDrawTexR, IntPtr.Zero, a, "DrawTexture");
        }

        static void Text(float x, float y, float w, float h, string text, int size, Color c, bool bold = false, int anchor = MiddleLeft)
        {
            if (string.IsNullOrEmpty(text)) return;
            var r = P(x, y, w, h);
            IntPtr* a = stackalloc IntPtr[3];
            if (mSetContentColor != IntPtr.Zero) { a[0] = (IntPtr)(&c); Il2Cpp.Invoke(mSetContentColor, IntPtr.Zero, a, "contentColor"); }
            a[0] = (IntPtr)(&r);
            a[1] = Il2Cpp.il2cpp_string_new(text);
            a[2] = Style(Math.Max(8, (int)Math.Round(size * _s)), bold, anchor);
            Il2Cpp.Invoke(mLabel, IntPtr.Zero, a, "Label");
        }

        // ---------------- Maus ----------------

        static float _mx = -9999, _my = -9999;      // in Panel-Einheiten
        static float _mxPx, _myPx;                  // in Pixeln
        static bool _mDown, _mPressed, _mReleased;
        static int _hot = -1;                        // aktiv gezogener Regler
        static bool _dragging;
        static float _dragDX, _dragDY;
        static float _panelX = float.NaN, _panelY;   // in Pixeln

        static void PollMouse(int screenW, int screenH)
        {
            bool down = false;
            float px = -9999, py = -9999;
            if (Menu.GameFocused())
            {
                var hwnd = Win.GetForegroundWindow();
                if (Win.GetCursorPos(out var p) && Win.ScreenToClient(hwnd, ref p) && Win.GetClientRect(hwnd, out var cr))
                {
                    float cw = Math.Max(1, cr.Right - cr.Left), ch = Math.Max(1, cr.Bottom - cr.Top);
                    px = p.X * screenW / cw;
                    py = p.Y * screenH / ch;
                }
                down = (Win.GetAsyncKeyState(0x01) & 0x8000) != 0;
            }
            _mPressed = down && !_mDown;
            _mReleased = !down && _mDown;
            _mDown = down;
            _mxPx = px; _myPx = py;
        }

        static bool Hover(float x, float y, float w, float h) => _mx >= x && _mx < x + w && _my >= y && _my < y + h;
        static bool Clicked(float x, float y, float w, float h) => _mPressed && _hot < 0 && !_dragging && Hover(x, y, w, h);

        // ---------------- Einstieg ----------------

        public static void OnGUI()
        {
            if (_failed) return;
            if (!_ready)
            {
                if (!Resolve()) { _failed = true; return; }
                _ready = true;
            }

            var ev = Il2Cpp.Invoke(mEventCurrent, IntPtr.Zero, null, "Event.current");
            if (ev == IntPtr.Zero || UnboxInt(Il2Cpp.Invoke(mEventType, ev, null, "Event.type")) != EventRepaint) return;

            // Mehrere gehookte OnGUI-Komponenten -> nur einmal pro Frame zeichnen
            if (mFrameCount != IntPtr.Zero)
            {
                int f = UnboxInt(Il2Cpp.Invoke(mFrameCount, IntPtr.Zero, null, "frameCount"));
                if (f == _lastFrame) return;
                _lastFrame = f;
            }

            EverDrawn = true;
            if (!Menu.Open) { _dragging = false; _hot = -1; return; }

            if (_white == IntPtr.Zero)
            {
                _white = Il2Cpp.Invoke(mWhiteTex, IntPtr.Zero, null, "whiteTexture");
                if (_white == IntPtr.Zero) return;
                Il2Cpp.il2cpp_gchandle_new(_white, false);
            }
            if (_baseLabel == IntPtr.Zero)
            {
                var skin = Il2Cpp.Invoke(mGetSkin, IntPtr.Zero, null, "GUI.skin");
                _baseLabel = skin == IntPtr.Zero ? IntPtr.Zero : Il2Cpp.Invoke(mSkinLabel, skin, null, "skin.label");
                if (_baseLabel == IntPtr.Zero) return;
                Il2Cpp.il2cpp_gchandle_new(_baseLabel, false);
                Log.Write("Overlay bereit.");
            }

            int sw = UnboxInt(Il2Cpp.Invoke(mScreenW, IntPtr.Zero, null, "Screen.width"));
            int sh = UnboxInt(Il2Cpp.Invoke(mScreenH, IntPtr.Zero, null, "Screen.height"));
            if (sw <= 0 || sh <= 0) return;
            _s = Math.Max(0.8f, sh / 1080f * UiScale);

            PollMouse(sw, sh);
            DrawMenu(sw, sh);

            var white = new Color(1, 1, 1, 1);
            IntPtr* a = stackalloc IntPtr[1];
            if (mSetContentColor != IntPtr.Zero) { a[0] = (IntPtr)(&white); Il2Cpp.Invoke(mSetContentColor, IntPtr.Zero, a, "contentColor"); }
        }

        // ---------------- Seiten als Zeilen-Listen ----------------

        abstract class Row { public virtual float Height => RowH; public abstract void Draw(float y, int id); }

        const float W = 480, Pad = 14, RowH = 30, HeadH = 34, TabH = 30, FootH = 30;
        const float LabelW = 120;                     // Spalte für Beschriftung
        const float ValueW = 58;                      // Spalte für Zahlenwert rechts

        sealed class Group : Row
        {
            readonly string _t;
            public Group(string t) { _t = t; }
            public override float Height => 26;
            public override void Draw(float y, int id)
            {
                Text(Pad, y + 8, W - Pad * 2, 16, _t, 10, CFaint, true);
            }
        }

        sealed class Toggle : Row
        {
            readonly string _l, _key; readonly Func<bool> _g; readonly Action<bool> _s;
            public Toggle(string l, string key, Func<bool> g, Action<bool> s) { _l = l; _key = key; _g = g; _s = s; }
            public override void Draw(float y, int id)
            {
                bool v = _g(), hov = Hover(0, y, W, RowH);
                if (hov) Box(0, y, W, RowH, CHover);
                Text(Pad, y, LabelW + 80, RowH, _l, 13, CText);
                if (_key != null) Text(W - Pad - 70, y, 40, RowH, _key, 10, CFaint, false, MiddleRight);
                float bx = W - Pad - 14, by = y + 8;
                if (v) Box(bx, by, 14, 14, CAccent, 2);
                else Box(bx, by, 14, 14, CDim, 2, 1.2f);
                if (v) Box(bx + 4, by + 4, 6, 6, CBg, 1);
                if (Clicked(0, y, W, RowH)) _s(!v);
            }
        }

        sealed class Slider : Row
        {
            readonly string _l; readonly Func<float> _g; readonly Action<float> _set; readonly float _min, _max, _step; readonly Func<float, string> _fmt;
            public Slider(string l, Func<float> g, Action<float> s, float min, float max, float step, Func<float, string> fmt)
            { _l = l; _g = g; _set = s; _min = min; _max = max; _step = step; _fmt = fmt; }
            public override void Draw(float y, int id)
            {
                float v = _g();
                bool hov = Hover(0, y, W, RowH) || _hot == id;
                if (hov) Box(0, y, W, RowH, CHover);
                Text(Pad, y, LabelW, RowH, _l, 13, CText);

                float tx = Pad + LabelW, tw = W - Pad * 2 - LabelW - ValueW - 8, ty = y + RowH / 2;
                float t = Math.Clamp((v - _min) / (_max - _min), 0f, 1f);
                Box(tx, ty - 1, tw, 2, CTrack);
                Box(tx, ty - 1, tw * t, 2, CAccent);
                Box(tx + tw * t - 4, ty - 6, 8, 12, _hot == id ? CText : CAccent, 1.5f);

                Text(W - Pad - ValueW, y, ValueW, RowH, _fmt(v), 12, _hot == id ? CAccent : CDim, false, MiddleRight);

                if (_mPressed && _hot < 0 && !_dragging && Hover(tx - 6, y, tw + 12, RowH)) _hot = id;
                if (_hot == id && _mDown)
                {
                    float nt = Math.Clamp((_mx - tx) / tw, 0f, 1f);
                    float nv = Math.Clamp(_min + (float)Math.Round(nt * (_max - _min) / _step) * _step, _min, _max);
                    if (nv != v) _set(nv);
                }
            }
        }

        sealed class ValueRow : Row
        {
            readonly string _l; readonly Func<string> _v; readonly bool _strong;
            public ValueRow(string l, Func<string> v, bool strong = false) { _l = l; _v = v; _strong = strong; }
            public override void Draw(float y, int id)
            {
                Text(Pad, y, LabelW, RowH, _l, 13, CDim);
                Text(Pad + LabelW, y, W - Pad * 2 - LabelW, RowH, _v(), _strong ? 15 : 13, _strong ? CText : CDim, _strong, MiddleRight);
            }
        }

        static bool FlatButton(float x, float y, float w, float h, string label, bool enabled = true)
        {
            bool hov = enabled && Hover(x, y, w, h);
            if (hov) Box(x, y, w, h, new Color(1, 1, 1, 0.07f), 3);
            Box(x, y, w, h, enabled ? (hov ? CAccent.WithA(0.7f) : CEdge) : CEdge.WithA(0.04f), 3, 1f);
            Text(x, y, w, h, label, 12, enabled ? (hov ? CText : CDim) : CFaint, false, MiddleCenter);
            return enabled && Clicked(x, y, w, h);
        }

        sealed class Buttons : Row
        {
            readonly (string label, Action act)[] _b;
            public Buttons(params (string, Action)[] b) { _b = b; }
            public override float Height => 36;
            public override void Draw(float y, int id)
            {
                float gap = 6, bw = (W - Pad * 2 - gap * (_b.Length - 1)) / _b.Length;
                for (int i = 0; i < _b.Length; i++)
                    if (FlatButton(Pad + i * (bw + gap), y + 4, bw, 26, _b[i].label)) _b[i].act();
            }
        }

        // Auswahl aus mehreren Optionen (z. B. Aus | Tempomat | Bahnen)
        sealed class Segmented : Row
        {
            readonly string _l; readonly string[] _opts; readonly Func<int> _g; readonly Action<int> _s;
            public Segmented(string l, string[] opts, Func<int> g, Action<int> s) { _l = l; _opts = opts; _g = g; _s = s; }
            public override void Draw(float y, int id)
            {
                Text(Pad, y, LabelW, RowH, _l, 13, CText);
                float x0 = Pad + LabelW, w = W - Pad - x0, bw = w / _opts.Length;
                int cur = _g();
                Box(x0, y + 4, w, RowH - 8, CEdge, 3, 1f);
                for (int i = 0; i < _opts.Length; i++)
                {
                    float bx = x0 + i * bw;
                    bool sel = i == cur, hov = Hover(bx, y + 4, bw, RowH - 8);
                    if (sel) Box(bx + 2, y + 6, bw - 4, RowH - 12, CAccent, 2);
                    else if (hov) Box(bx + 2, y + 6, bw - 4, RowH - 12, new Color(1, 1, 1, 0.06f), 2);
                    Text(bx, y + 4, bw, RowH - 8, _opts[i], 12, sel ? CBg : (hov ? CText : CDim), sel, MiddleCenter);
                    if (Clicked(bx, y + 4, bw, RowH - 8)) _s(i);
                }
            }
        }

        sealed class Note : Row
        {
            readonly Func<string> _t;
            public Note(Func<string> t) { _t = t; }
            public override float Height => 24;
            public override void Draw(float y, int id) => Text(Pad, y, W - Pad * 2, 24, _t(), 11, CFaint);
        }

        sealed class SlotRow : Row
        {
            readonly int _i;
            public SlotRow(int i) { _i = i; }
            public override void Draw(float y, int id)
            {
                var slot = State.Slots[_i];
                Text(Pad, y, 60, RowH, $"Slot {_i + 1}", 13, CText);
                Text(Pad + 56, y, 130, RowH, slot.HasValue ? slot.Value.ToString() : "leer", 11, slot.HasValue ? CDim : CFaint);
                float bx = W - Pad - 64 * 2 - 6;
                if (FlatButton(bx, y + 4, 64, 22, "Setzen")) State.SaveSlotRequest = _i;
                if (FlatButton(bx + 70, y + 4, 64, 22, "Springen", slot.HasValue)) State.LoadSlotRequest = _i;
            }
        }

        // Fahrzeuge, die gerade selbst fahren (auch ohne Fahrer), je mit Stopp-Knopf
        sealed class BotListRow : Row
        {
            List<Vehicle.BotInfo> _list = new List<Vehicle.BotInfo>();
            public override float Height
            {
                get { _list = Vehicle.BotList(); return RowH * Math.Max(1, _list.Count) + (_list.Count > 1 ? 36 : 0); }
            }
            public override void Draw(float y, int id)
            {
                if (_list.Count == 0) { Text(Pad, y, W - Pad * 2, RowH, "keins", 12, CFaint); return; }
                foreach (var b in _list)
                {
                    Text(Pad, y, 44, RowH, b.Name, 13, b.IsMine ? CAccent : CText, true);
                    string st = b.IsMine ? "du · " + b.Status : b.Status;
                    if (st.Length > 46) st = st.Substring(0, 45) + "…";
                    Text(Pad + 40, y, W - Pad * 2 - 40 - 70, RowH, st, 11, CDim);
                    if (FlatButton(W - Pad - 64, y + 4, 64, 22, "Stopp")) Vehicle.RequestStop(b.Id, "Menü");
                    y += RowH;
                }
                if (_list.Count > 1 && FlatButton(Pad, y + 4, W - Pad * 2, 26, "Alle stoppen")) Vehicle.StopAll("Menü");
            }
        }

        static readonly float[] CleanSteps = { 1f, 2f, 5f, 10f, 25f, 50f, 100f };
        static string X1(float v) => $"{v:0.0}x";
        static string X2(float v) => $"{v:0.00}x";

        static readonly string[] Tabs = { "Spieler", "Geld", "Putzen", "Fahrzeug", "Autodrive", "Teleport", "Welt" };

        static string AutoStatus()
        {
            if (!Vehicle.InVehicle) return Vehicle.Running > 0 ? $"nicht im Fahrzeug · {Vehicle.Running} fahren allein" : "nicht im Fahrzeug";
            if (Vehicle.AutoMode == 0) return string.IsNullOrEmpty(Vehicle.FieldText) ? "bereit" : Vehicle.FieldText;
            if (Vehicle.AutoMode == 1) return "Tempomat aktiv";
            return Vehicle.Phase;
        }

        static string WidthText()
        {
            if (!Vehicle.InVehicle) return "–";
            return Vehicle.CleanWidth > 0.2f ? $"Bürste {Vehicle.CleanWidth:0.00} m  →  Abstand {Vehicle.EffectiveWidth:0.00} m" : "Bürste unbekannt";
        }
        static readonly Row[][] Pages =
        {
            new Row[]
            {
                new Toggle("Fliegen", "F2", () => State.Fly, v => State.Fly = v),
                new Slider("Fluggeschw.", () => State.FlySpeedMul, v => State.FlySpeedMul = v, 0.5f, 10f, 0.5f, X1),
                new Slider("Laufgeschw.", () => State.WalkSpeedMul, v => State.WalkSpeedMul = v, 1f, 5f, 0.25f, X2),
                new Toggle("Unendlich Boosts", "F10", () => State.InfiniteBoosts, v => State.InfiniteBoosts = v),
            },
            new Row[]
            {
                new ValueRow("Kontostand", () => State.PlayerFound ? $"{State.Balance:N0} $" : "–", true),
                new Group("HINZUFÜGEN"),
                new Buttons(("+10 Tsd.", () => State.QueueMoney(10_000m)), ("+100 Tsd.", () => State.QueueMoney(100_000m))),
                new Buttons(("+1 Mio.", () => State.QueueMoney(1_000_000m)), ("+10 Mio.", () => State.QueueMoney(10_000_000m))),
            },
            new Row[]
            {
                new Slider("Stärke", () => Math.Max(0, Array.IndexOf(CleanSteps, State.CleanMul)), v => State.CleanMul = CleanSteps[(int)v], 0, 6, 1, v => $"{CleanSteps[(int)v]:0}x"),
                new Slider("Breite", () => State.WidthMul, v => State.WidthMul = v, 1f, 4f, 0.5f, X1),
            },
            new Row[]
            {
                new Group("ABLADEN"),
                new ValueRow("Tank", () => !Vehicle.InVehicle ? "–" : $"{Vehicle.DustKg:0} / {Vehicle.DustCap:0} kg"),
                new Toggle("Automatisch abladen", null, () => Vehicle.AutoUnload, v => Vehicle.AutoUnload = v),
                new Segmented("Abladen", new[] { "Hinfahren", "Aus der Ferne" }, () => Vehicle.UnloadMode, v => Vehicle.UnloadMode = v),
                new Slider("Ab Füllstand", () => Vehicle.UnloadAt, v => Vehicle.UnloadAt = v, 0.5f, 1f, 0.05f, v => $"{v * 100:0} %"),
                new Buttons(("Jetzt abladen", () => Vehicle.UnloadNow = true)),
                new Note(() => Vehicle.TripText),
                new Group("TUNING"),
                new Slider("Tempo-Faktor", () => Vehicle.SpeedMul, v => Vehicle.SpeedMul = v, 1f, 3f, 0.1f, X1),
                new Toggle("Tempomat-Upgrade", null, () => Vehicle.CruiseUnlock, v => Vehicle.CruiseUnlock = v),
            },
            new Row[]
            {
                new Segmented("Modus", new[] { "Aus", "Tempomat", "Bahnen" }, () => Vehicle.AutoMode, v => Vehicle.AutoMode = v),
                new Slider("Gas", () => Vehicle.Throttle, v => Vehicle.Throttle = v, 0.2f, 1f, 0.05f, v => $"{v * 100:0} %"),
                new ValueRow("Status", AutoStatus),
                new Toggle("Ohne Fahrer weiterfahren", null, () => Vehicle.KeepDriving, v => Vehicle.KeepDriving = v),
                new Note(() => Vehicle.KeepDriving ? "Aussteigen: fährt allein weiter. S im Fahrzeug stoppt es." : "S drücken oder Aussteigen beendet Autodrive."),
                new Group("FAHREN GERADE"),
                new BotListRow(),
                new Group("FELD"),
                new Slider("Bahnlänge", () => Vehicle.LaneLength, v => Vehicle.LaneLength = v, 10f, 300f, 5f, v => $"{v:0} m"),
                new Slider("Anzahl Bahnen", () => Vehicle.LaneCount, v => Vehicle.LaneCount = v, 0f, 200f, 1f, v => v < 0.5f ? "endlos" : $"{v:0}"),
                new Segmented("Versatz nach", new[] { "Links", "Keiner", "Rechts" }, () => Vehicle.Shift, v => Vehicle.Shift = v),
                new Toggle("Abstand = Bürste", null, () => Vehicle.AutoWidth, v => Vehicle.AutoWidth = v),
                new Slider("Überlappung", () => Vehicle.Overlap, v => Vehicle.Overlap = v, 0f, 0.4f, 0.05f, v => $"{v * 100:0} %"),
                new Slider("Bahnabstand", () => Vehicle.LaneWidth, v => Vehicle.LaneWidth = v, 0.5f, 50f, 0.5f, v => Vehicle.AutoWidth ? "auto" : $"{v:0.0} m"),
                new ValueRow("Breite", WidthText),
                new Slider("Anlauf", () => Vehicle.Overrun, v => Vehicle.Overrun = v, 0f, 20f, 1f, v => $"{v:0} m"),
                new Toggle("Nur Dreck fahren", null, () => Vehicle.DirtOnly, v => Vehicle.DirtOnly = v),
                new ValueRow("Diese Bahn", () => Vehicle.AutoMode == 2 && Vehicle.PlanText.Length > 0 ? Vehicle.PlanText : "–"),
                new Toggle("Nachputzen", null, () => Vehicle.FinishPass, v => Vehicle.FinishPass = v),
                new Buttons(("Jetzt nachputzen", () => Vehicle.FinishNow = true)),
                new Group("HINDERNISSE"),
                new Toggle("Ausweichen", null, () => Vehicle.AvoidObstacles, v => Vehicle.AvoidObstacles = v),
                new Slider("Sichtweite", () => Vehicle.SightRange, v => Vehicle.SightRange = v, 3f, 20f, 1f, v => $"{v:0} m"),
                new ValueRow("Zuletzt", () => Vehicle.ObstacleText),
            },
            new Row[]
            {
                new ValueRow("Position", () => State.PlayerFound ? State.Position.ToString() : "–"),
                new Group("SLOTS"),
                new SlotRow(0), new SlotRow(1), new SlotRow(2),
            },
            new Row[]
            {
                new Slider("Spieltempo", () => State.TimeScale, v => State.TimeScale = v, 0.25f, 5f, 0.25f, X2),
            },
        };

        static int _tab;

        static void DrawMenu(int sw, int sh)
        {
            var rows = Pages[_tab];
            float bodyH = 6;
            foreach (var r in rows) bodyH += r.Height;
            bodyH += 6;
            float H = HeadH + TabH + bodyH + FootH;

            if (float.IsNaN(_panelX)) { _panelX = 28 * _s; _panelY = 28 * _s; }
            if (_dragging)
            {
                if (_mDown) { _panelX = _mxPx - _dragDX; _panelY = _myPx - _dragDY; }
                else _dragging = false;
            }
            _panelX = Math.Clamp(_panelX, 0, sw - W * _s);
            _panelY = Math.Clamp(_panelY, 0, sh - HeadH * _s);
            _ox = _panelX; _oy = _panelY;
            _mx = (_mxPx - _ox) / _s; _my = (_myPx - _oy) / _s;
            if (_mReleased) _hot = -1;

            // Grund
            Box(0, 0, W, H, CBg, 5);
            Box(0, 0, W, HeadH, CHead, 5);
            Box(0, HeadH - 5, W, 5, CHead);
            Box(0, 0, W, H, CEdge, 5, 1f);

            // Kopf
            Box(Pad, HeadH / 2 - 4, 3, 8, CAccent);
            Text(Pad + 10, 0, 200, HeadH, "MoonMenu", 13, CText, true);
            bool hk = Hover(W - Pad - 30, 7, 30, 20);
            Box(W - Pad - 30, 7, 30, 20, hk ? CAccent : CEdge, 3, 1f);
            Text(W - Pad - 30, 7, 30, 20, "F1", 10, hk ? CAccent : CDim, true, MiddleCenter);
            if (Clicked(W - Pad - 30, 7, 30, 20)) { Menu.Open = false; return; }
            if (_mPressed && _hot < 0 && !_dragging && Hover(0, 0, W - Pad - 36, HeadH))
            {
                _dragging = true;
                _dragDX = _mxPx - _panelX; _dragDY = _myPx - _panelY;
            }

            // Tabs
            float tx = Pad;
            for (int i = 0; i < Tabs.Length; i++)
            {
                float tw = 12 + Tabs[i].Length * 7.2f;
                bool sel = i == _tab, hov = Hover(tx - 4, HeadH, tw + 8, TabH);
                Text(tx, HeadH, tw, TabH, Tabs[i], 12, sel ? CText : (hov ? CText.WithA(0.75f) : CDim), sel, MiddleCenter);
                if (sel) Box(tx + 4, HeadH + TabH - 2, tw - 8, 2, CAccent);
                if (Clicked(tx - 4, HeadH, tw + 8, TabH)) _tab = i;
                tx += tw + 4;
            }
            Box(0, HeadH + TabH - 1, W, 1, CEdge);

            // Inhalt
            float y = HeadH + TabH + 6;
            for (int i = 0; i < rows.Length; i++)
            {
                rows[i].Draw(y, _tab * 100 + i + 1);
                y += rows[i].Height;
            }

            // Fuß
            float fy = H - FootH;
            Box(0, fy, W, 1, CEdge);
            bool ok = State.PlayerFound;
            Box(Pad, fy + FootH / 2 - 3, 6, 6, ok ? COk : CFaint, 3);
            Text(Pad + 12, fy, 200, FootH, ok ? "verbunden" : "kein Spielstand geladen", 11, CDim);
            bool hr = Hover(W - Pad - 70, fy, 70, FootH);
            Text(W - Pad - 70, fy, 70, FootH, "zurücksetzen", 11, hr ? CAccent : CFaint, false, MiddleRight);
            if (Clicked(W - Pad - 70, fy, 70, FootH)) State.ResetAll();
        }
    }
}
