using System;
using System.Collections.Generic;

namespace MoonMenu
{
    // Autodrive + Abladen für MoonPrototypeVehicle, für beliebig viele Fahrzeuge gleichzeitig.
    //
    // Jedes Fahrzeug mit laufendem Autodrive bekommt einen eigenen Bot (Zustand: Feld, Bahn, Ausweichen,
    // Feststecken, Abladefahrt). Der Bot läuft im gehookten FixedUpdate seines Fahrzeugs, auch wenn
    // niemand darin sitzt.
    //
    // Fahren: über die spieleigene Test-Methode TestDrive(throttle, steer, bool, bool), die nur die
    // Eingabefelder setzt und die Bremse löst. Aufruf direkt vor FixedUpdate.
    // Ohne Fahrer friert das Spiel ein Fahrzeug ein (kinematisch), solange IsDriven aus ist. Der Bot setzt
    // IsDriven deshalb vor jedem Physik-Schritt, solange er fährt, und gibt es beim Beenden wieder frei.
    //
    // Bahnen-Modus = Spurführung: Bahn i ist eine feste Linie im Abstand i * Bahnabstand von der Startlinie.
    //   Vor jeder Bahn wird ihr Streifen per DustUnder() abgetastet; nur Abschnitte mit Dreck werden gefahren.
    //   Nach der letzten Bahn: Feld rastern und Reststellen einzeln anfahren (Nachputzen, max. 3 Runden).
    //
    // Hindernisse: Kugel-Sweeps (Physics.SphereCastNonAlloc) in einem Fächer um die Wunschrichtung.
    //   Treffer am eigenen Rigidbody, Boden (Normale zeigt nach oben) und Startüberlappungen werden ignoriert.
    //   Andere Fahrzeuge sind normale Hindernisse.
    //
    // Abladen:
    //   "Hinfahren": Tank voll -> Position merken, zur nächsten Garage fahren, dort abladen, zurück, weiter.
    //   "Aus der Ferne": sofort abladen, ohne hinzufahren.
    //   Garage.Collect() prüft UnderHood(); während unseres Abladens meldet ein Hook "steht unter der Haube".
    internal static unsafe class Vehicle
    {
        public enum Mode { Off = 0, Cruise = 1, Lanes = 2 }
        enum Trip { None, ToGarage, WaitGarage, Unloading, Back }
        enum Sweep { Lanes, Scan, Fix, Done }

        // ---- Einstellungen (vom Menü; gelten für alle Fahrzeuge, Feldgröße wird beim Start übernommen) ----
        public static float Throttle = 0.6f;          // 0.2 .. 1
        public static float LaneLength = 60f;         // m
        public static float LaneWidth = 8f;           // m (nur ohne Auto-Abstand)
        public static float LaneCount = 20f;          // 0 = endlos
        public static volatile bool AutoWidth = true;
        public static float Overlap = 0.15f;          // Anteil der Bürstenbreite
        public static float Overrun = 4f;             // m Anlauf vor jeder Bahn
        public static volatile bool FinishPass = true;
        public static volatile bool DirtOnly = true;  // nur Abschnitte mit Dreck fahren
        public static volatile int Shift = 2;         // Versatz: 0 = links, 1 = keiner, 2 = rechts
        public static volatile bool KeepDriving = true;   // nach dem Aussteigen weiterfahren
        public static float SpeedMul = 1f;
        public static volatile bool CruiseUnlock;

        public static volatile bool AvoidObstacles = true;
        public static float SightRange = 8f;          // m

        public static volatile bool AutoUnload;
        public static volatile int UnloadMode;        // 0 = Hinfahren, 1 = aus der Ferne
        public static float UnloadAt = 0.95f;

        // Befehle für das Fahrzeug, in dem der Spieler sitzt (werden in dessen Physik-Schritt ausgeführt)
        public static volatile bool UnloadNow;
        public static volatile bool FinishNow;
        static volatile int _requestMode = -1;

        // ---- Anzeige für das eigene Fahrzeug ----
        public static IntPtr Mine;                    // Fahrzeug des lokalen Spielers (aus Walker-Hook)
        public static volatile bool InVehicle;
        public static double DustKg, DustCap;
        public static float CleanWidth, Speed;

        public static float EffectiveWidth => WidthFor(CleanWidth);
        static float WidthFor(float cw) =>
            AutoWidth && cw > 0.2f && cw < 40f ? Math.Max(0.3f, cw * (1f - Overlap)) : LaneWidth;

        static readonly Dictionary<IntPtr, Bot> Bots = new Dictionary<IntPtr, Bot>();
        static readonly Dictionary<IntPtr, string> LastResult = new Dictionary<IntPtr, string>();
        static int _botCounter;

        static Bot Cur => Mine != IntPtr.Zero && Bots.TryGetValue(Mine, out var b) ? b : null;

        public static int AutoMode
        {
            get => _requestMode >= 0 ? _requestMode : Cur?.Mode ?? 0;
            set => _requestMode = value;
        }

        public static string Phase => Cur?.Phase ?? "";
        public static string FieldText => Cur != null && Cur.Mode != 0 ? Cur.FieldText : (Mine != IntPtr.Zero && LastResult.TryGetValue(Mine, out var t) ? t : "");
        public static string PlanText => Cur?.PlanText ?? "";
        public static string ObstacleText => Cur?.ObstacleText ?? "–";
        public static string TripText => Cur?.TripText ?? _unloadInfo;
        static string _unloadInfo = "";

        public static int Running { get { int n = 0; foreach (var b in Bots.Values) if (b.Busy) n++; return n; } }

        public struct BotInfo { public IntPtr Id; public string Name, Status; public bool IsMine; }

        // Spielzeit in ms: läuft mit dem Spieltempo und steht bei Pause. Alle Bot-Zeiten nutzen sie,
        // damit Freifahren, Schonzeiten usw. auch bei 0,25x oder 5x Spieltempo gleich lang dauern.
        static long _now;
        static long GameMs()
        {
            if (mTime == IntPtr.Zero) return Environment.TickCount64;
            var boxed = Il2Cpp.Invoke(mTime, IntPtr.Zero, null, "Time.time");
            return boxed == IntPtr.Zero ? _now : (long)(*(float*)Il2Cpp.il2cpp_object_unbox(boxed) * 1000.0);
        }

        // Für das Menü (Hauptthread). Fahrzeuge, die 5 s Spielzeit keinen Physik-Schritt hatten
        // (entladen/zerstört), fliegen raus; ihre Zeiger werden nie mehr angefasst.
        public static List<BotInfo> BotList()
        {
            long now = GameMs();
            var dead = new List<IntPtr>();
            var r = new List<BotInfo>();
            foreach (var kv in Bots)
            {
                if (now - kv.Value.LastSeen > 5000) { dead.Add(kv.Key); continue; }
                if (!kv.Value.Busy) continue;
                r.Add(new BotInfo { Id = kv.Key, Name = kv.Value.Name, Status = kv.Value.Summary, IsMine = kv.Key == Mine });
            }
            foreach (var d in dead) { Log.Write($"{Bots[d].Name}: Fahrzeug nicht mehr da, aus der Liste entfernt."); Bots.Remove(d); }
            return r;
        }

        public static void RequestStop(IntPtr id, string why)
        {
            if (Bots.TryGetValue(id, out var b)) { b.StopReq = true; b.StopWhy = why; }
        }

        public static void StopAll(string why)
        {
            foreach (var b in Bots.Values) { b.StopReq = true; b.StopWhy = why; }
            _requestMode = -1;
        }

        // Vom Walker-Hook: Fahrzeug des Spielers hat gewechselt
        public static void SetMine(IntPtr mounted)
        {
            var old = Mine;
            Mine = mounted;
            InVehicle = mounted != IntPtr.Zero;
            if (old == mounted) return;
            _requestMode = -1;
            if (old != IntPtr.Zero && Bots.TryGetValue(old, out var b) && b.Busy)
            {
                if (KeepDriving) b.DriverLeft();
                else { b.StopReq = true; b.StopWhy = "ausgestiegen"; }
            }
        }

        // Tempo-Faktor für das eigene Fahrzeug (auch von Hand gefahren) und für jedes Fahrzeug mit Bot.
        // Basis neu übernehmen, wenn das Spiel selbst ändert (Upgrade); sonst Originalwert zurückschreiben.
        struct Spd { public float Base, Wrote; }
        static readonly Dictionary<IntPtr, Spd> Speeds = new Dictionary<IntPtr, Spd>();

        static void ApplySpeed(IntPtr self, bool on)
        {
            if (oSpeedFactor < 0) return;
            float cur = *(float*)(self + oSpeedFactor);
            bool has = Speeds.TryGetValue(self, out var s);
            if (!on || SpeedMul == 1f)
            {
                if (has) { if (cur == s.Wrote) *(float*)(self + oSpeedFactor) = s.Base; Speeds.Remove(self); }
                return;
            }
            if (!has || cur != s.Wrote) s.Base = cur;
            s.Wrote = s.Base * SpeedMul;
            *(float*)(self + oSpeedFactor) = s.Wrote;
            Speeds[self] = s;
        }

        // ---- il2cpp ----
        static int oSteer = -1, oBrake = -1, oSpeedFactor = -1, oCruiseFitted = -1, oUnloadPoint = -1;
        static int oBody = -1, oLastClean = -1, oHasClean = -1, oFlipped = -1, oIsDriven = -1, oCleaning = -1;
        static IntPtr mForwardSpeed, mFlipUpright, mClean, mTime;
        static int oNextWork = -1;
        static float CleanInterval = 0.1f;
        static IntPtr mTestDrive, mGetTransform, mGetPosition, mGetForward, mGetUp;
        static IntPtr mDustKg, mDustCap, fGarageAll, mCollect, mCleaningWidth, mDustUnder, mGetName;
        static IntPtr mSphereCast, mRaycastNA, mAttachedRb;
        static delegate* unmanaged<IntPtr, Vec3*, IntPtr, byte> _dustUnder;
        static delegate* unmanaged<IntPtr, IntPtr, IntPtr> _hitCollider;
        static bool _colliderThunk;
        static IntPtr _hits;
        static int _hitSize = 0x30;
        const int HitCap = 24;
        const int ObstacleMask = ~((1 << 2) | (1 << 5));   // ohne "Ignore Raycast" und UI
        static delegate* unmanaged<IntPtr, IntPtr, void> _origFixed;
        static delegate* unmanaged<IntPtr, IntPtr, float*, IntPtr, byte> _origUnderHood;

        public static void Init(IntPtr img)
        {
            var veh = Il2Cpp.il2cpp_class_from_name(img, "MoonCleaners", "MoonPrototypeVehicle");
            if (veh == IntPtr.Zero) { Log.Write("Fahrzeug-Klasse fehlt."); return; }
            oSteer = Il2Cpp.Offset(veh, "steer");
            oBrake = Il2Cpp.Offset(veh, "brake");
            oSpeedFactor = Il2Cpp.Offset(veh, "<SpeedFactor>k__BackingField");
            oCruiseFitted = Il2Cpp.Offset(veh, "<CruiseFitted>k__BackingField");
            oBody = Il2Cpp.Offset(veh, "body");
            oLastClean = Il2Cpp.Offset(veh, "lastCleanPoint");
            oHasClean = Il2Cpp.Offset(veh, "hasCleanPoint");
            oFlipped = Il2Cpp.Offset(veh, "<Flipped>k__BackingField");
            oIsDriven = Il2Cpp.Offset(veh, "<IsDriven>k__BackingField");
            oCleaning = Il2Cpp.Offset(veh, "<Cleaning>k__BackingField");
            mForwardSpeed = Il2Cpp.FindMethod(veh, "get_ForwardSpeed");
            mFlipUpright = Il2Cpp.FindMethod(veh, "FlipUpright");
            mClean = Il2Cpp.FindMethod(veh, "Clean");
            oNextWork = Il2Cpp.Offset(veh, "nextWork");
            mTime = Il2Cpp.FindMethod(Il2Cpp.FindClass("UnityEngine.CoreModule.dll", "UnityEngine", "Time"), "get_time");
            // Takt des Spiels: Konstante, die Update() auf nextWork addiert (RVA 0x1FEFB04 in diesem Build)
            try
            {
                var ga = Win.GetModuleHandleW("GameAssembly.dll");
                if (ga != IntPtr.Zero)
                {
                    float c = *(float*)(ga + 0x1FEFB04);
                    if (c >= 0.01f && c <= 1f) CleanInterval = c;
                }
            }
            catch { }
            mTestDrive = Il2Cpp.FindMethod(veh, "TestDrive", "System.Single", "System.Single", "System.Boolean", "System.Boolean");
            mDustKg = Il2Cpp.FindMethod(veh, "get_DustKilograms");
            mDustCap = Il2Cpp.FindMethod(veh, "get_DustCapacityKg");
            mCleaningWidth = Il2Cpp.FindMethod(veh, "get_CleaningWidth");
            mDustUnder = Il2Cpp.FindMethod(veh, "DustUnder", "UnityEngine.Vector3");
            var du = Il2Cpp.MethodPointer(mDustUnder);
            if (du != IntPtr.Zero) _dustUnder = (delegate* unmanaged<IntPtr, Vec3*, IntPtr, byte>)du;

            var comp = Il2Cpp.FindClass("UnityEngine.CoreModule.dll", "UnityEngine", "Component");
            var tr = Il2Cpp.FindClass("UnityEngine.CoreModule.dll", "UnityEngine", "Transform");
            var uobj = Il2Cpp.FindClass("UnityEngine.CoreModule.dll", "UnityEngine", "Object");
            mGetTransform = Il2Cpp.FindMethod(comp, "get_transform");
            mGetPosition = Il2Cpp.FindMethod(tr, "get_position");
            mGetForward = Il2Cpp.FindMethod(tr, "get_forward");
            mGetUp = Il2Cpp.FindMethod(tr, "get_up");
            mGetName = Il2Cpp.FindMethod(uobj, "get_name");

            InitPhysics();

            var garage = Il2Cpp.il2cpp_class_from_name(img, "MoonCleaners", "MoonGarage");
            if (garage != IntPtr.Zero)
            {
                fGarageAll = Il2Cpp.il2cpp_class_get_field_from_name(garage, "all");
                oUnloadPoint = Il2Cpp.Offset(garage, "unloadPoint");
                mCollect = Il2Cpp.FindMethod(garage, "Collect", "MoonCleaners.MoonPrototypeVehicle", "System.Single", "System.String&", "System.Boolean&");
                var hood = Il2Cpp.MethodPointer(Il2Cpp.FindMethod(garage, "UnderHood", "MoonCleaners.MoonPrototypeVehicle", "System.Single&"));
                if (hood != IntPtr.Zero)
                    _origUnderHood = (delegate* unmanaged<IntPtr, IntPtr, float*, IntPtr, byte>)Dobby.Hook(hood, (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, float*, IntPtr, byte>)&UnderHoodHook, "MoonGarage.UnderHood");
            }
            Log.Write($"Abladen-API: kg={mDustKg != IntPtr.Zero} cap={mDustCap != IntPtr.Zero} all={fGarageAll != IntPtr.Zero} collect={mCollect != IntPtr.Zero} unloadPoint=0x{oUnloadPoint:X} driven=0x{oIsDriven:X} cleaning=0x{oCleaning:X}");

            var target = Il2Cpp.MethodPointer(Il2Cpp.Method(veh, "FixedUpdate", 0));
            if (target != IntPtr.Zero)
                _origFixed = (delegate* unmanaged<IntPtr, IntPtr, void>)Dobby.Hook(target, (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, void>)&FixedHook, "MoonPrototypeVehicle.FixedUpdate");
        }

        static void InitPhysics()
        {
            const string pm = "UnityEngine.PhysicsModule.dll";
            var phys = Il2Cpp.FindClass(pm, "UnityEngine", "Physics");
            var hitK = Il2Cpp.FindClass(pm, "UnityEngine", "RaycastHit");
            var colK = Il2Cpp.FindClass(pm, "UnityEngine", "Collider");
            if (phys == IntPtr.Zero || hitK == IntPtr.Zero || colK == IntPtr.Zero) return;
            mSphereCast = Il2Cpp.FindMethod(phys, "SphereCastNonAlloc", "UnityEngine.Vector3", "System.Single", "UnityEngine.Vector3", "UnityEngine.RaycastHit[]", "System.Single", "System.Int32", "UnityEngine.QueryTriggerInteraction");
            mRaycastNA = Il2Cpp.FindMethod(phys, "RaycastNonAlloc", "UnityEngine.Vector3", "UnityEngine.Vector3", "UnityEngine.RaycastHit[]", "System.Single", "System.Int32", "UnityEngine.QueryTriggerInteraction");
            mAttachedRb = Il2Cpp.FindMethod(colK, "get_attachedRigidbody");

            int vs = Il2Cpp.il2cpp_class_value_size(hitK, out _);
            if (vs >= 0x2C && vs <= 0x60) _hitSize = vs;
            _hits = Il2Cpp.il2cpp_array_new(hitK, (UIntPtr)HitCap);
            if (_hits != IntPtr.Zero) Il2Cpp.il2cpp_gchandle_new(_hits, false);

            // RaycastHit.get_collider ist eine Struct-Methode: entweder direkt (this = RaycastHit*) oder als
            // Adjustor-Thunk (this = geboxtes Objekt, Thunk addiert 0x10). Am Maschinencode erkennen.
            var gc = Il2Cpp.MethodPointer(Il2Cpp.FindMethod(hitK, "get_collider"));
            string bytes = "";
            if (gc != IntPtr.Zero)
            {
                byte* b = (byte*)gc;
                _colliderThunk = (b[0] == 0x48 && b[1] == 0x83 && b[2] == 0xC1 && b[3] == 0x10)    // add rcx,10h
                              || (b[0] == 0x48 && b[1] == 0x8D && b[2] == 0x49 && b[3] == 0x10);   // lea rcx,[rcx+10h]
                for (int i = 0; i < 8; i++) bytes += b[i].ToString("X2");
                _hitCollider = (delegate* unmanaged<IntPtr, IntPtr, IntPtr>)gc;
            }
            Log.Write($"Physik: sphere={mSphereCast != IntPtr.Zero} ray={mRaycastNA != IntPtr.Zero} rb={mAttachedRb != IntPtr.Zero} hit=0x{_hitSize:X} arr={_hits != IntPtr.Zero} collider={bytes} thunk={_colliderThunk} dust={_dustUnder != null} body=0x{oBody:X}");
        }

        // ---------------- Hooks ----------------

        static bool _err;
        static IntPtr _forceHoodFor;

        [System.Runtime.InteropServices.UnmanagedCallersOnly]
        static void FixedHook(IntPtr self, IntPtr method)
        {
            bool unblock = false, forced = false;
            try
            {
                if (self != IntPtr.Zero)
                {
                    bool mine = self == Mine, busy = false;
                    if (mine || Bots.ContainsKey(self)) _now = GameMs();
                    if (mine) MineFrame(self);
                    if (Bots.TryGetValue(self, out var bot))
                    {
                        bot.Step(self, mine);
                        if (!bot.Busy) { bot.Release(self, mine); Bots.Remove(self); }
                        else
                        {
                            busy = true;
                            // Offenes Menü sperrt die Steuerung (UIBlocked) -> das Spiel nullt Lenkung/Gas.
                            // Während der Bot fährt, die Sperre nur für diesen Physik-Schritt lösen.
                            unblock = Core.MenuBlocking;
                            // Ohne Fahrer friert FixedUpdate das Fahrzeug ein, solange IsDriven aus ist.
                            // IsDriven nur für diesen Schritt setzen: im Frame-Update bleibt es aus, sonst
                            // liest das Fahrzeug die Tasten des Spielers (E = Aussteigen) und Enter() lehnt ab.
                            if (!mine && oIsDriven >= 0 && *(byte*)(self + oIsDriven) == 0)
                            {
                                *(byte*)(self + oIsDriven) = 1;
                                forced = true;
                            }
                        }
                    }
                    ApplySpeed(self, mine || busy);
                    if (unblock) Core.SetLocalBlocked(false);
                }
            }
            catch (Exception e) { if (!_err) { _err = true; Log.Write("Fahrzeug-Hook: " + e); } }
            if (_origFixed != null) _origFixed(self, method);
            if (unblock) { try { Core.SetLocalBlocked(true); } catch { } }
            if (forced) { try { if (Core.MountedNow != self) *(byte*)(self + oIsDriven) = 0; } catch { } }
        }

        // Eigenes Fahrzeug: Anzeige, Tuning, Befehle aus dem Menü
        static void MineFrame(IntPtr self)
        {
            DustKg = GetDouble(mDustKg, self);
            DustCap = GetDouble(mDustCap, self);
            CleanWidth = GetFloat(mCleaningWidth, self);
            Speed = GetFloat(mForwardSpeed, self);
            if (CruiseUnlock && oCruiseFitted >= 0) *(byte*)(self + oCruiseFitted) = 1;

            Bots.TryGetValue(self, out var bot);

            int req = _requestMode;
            if (req >= 0)
            {
                _requestMode = -1;
                if (req == 0) { if (bot != null) { bot.StopReq = true; bot.StopWhy = "Menü"; } }
                else
                {
                    if (bot == null) bot = NewBot(self);
                    bot.Start(req);
                }
            }

            bool full = DustCap > 0 && DustKg >= DustCap * UnloadAt;
            if (bot == null && ((UnloadNow && DustKg > 0.01) || (AutoUnload && full && _now >= _mineRetryAt)))
            {
                if (!UnloadNow) _mineRetryAt = _now + 2000;
                bot = NewBot(self);          // Bot nur fürs Abladen (Modus aus)
                if (UnloadNow) bot._retryAt = 0;
            }
            if (bot != null && UnloadNow) bot.UnloadReq = true;
            UnloadNow = false;

            if (bot != null && FinishNow) bot.FinishReq = true;
            FinishNow = false;

            if (bot != null && bot.Busy && Menu.GameFocused() && (Win.GetAsyncKeyState(0x53) & 0x8000) != 0)
            {
                bot.StopReq = true; bot.StopWhy = "S gedrückt";
            }
        }
        static long _mineRetryAt;

        static Bot NewBot(IntPtr self)
        {
            var b = new Bot(self, $"F{++_botCounter}");
            if (self == Mine) b._retryAt = _mineRetryAt;
            Bots[self] = b;
            return b;
        }

        [System.Runtime.InteropServices.UnmanagedCallersOnly]
        static byte UnderHoodHook(IntPtr self, IntPtr vehicle, float* outValue, IntPtr method)
        {
            if (_forceHoodFor != IntPtr.Zero && vehicle == _forceHoodFor)
            {
                if (outValue != null) *outValue = 0f;
                return 1;
            }
            return _origUnderHood != null ? _origUnderHood(self, vehicle, outValue, method) : (byte)0;
        }

        // ---------------- Vektor-Hilfen ----------------
        static Vec3 Add(Vec3 a, Vec3 b) => new Vec3 { X = a.X + b.X, Y = a.Y + b.Y, Z = a.Z + b.Z };
        static Vec3 Sub(Vec3 a, Vec3 b) => new Vec3 { X = a.X - b.X, Y = a.Y - b.Y, Z = a.Z - b.Z };
        static Vec3 Mul(Vec3 a, float s) => new Vec3 { X = a.X * s, Y = a.Y * s, Z = a.Z * s };
        static float Dot(Vec3 a, Vec3 b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;
        static Vec3 Cross(Vec3 a, Vec3 b) => new Vec3 { X = a.Y * b.Z - a.Z * b.Y, Y = a.Z * b.X - a.X * b.Z, Z = a.X * b.Y - a.Y * b.X };
        static float Len(Vec3 a) => MathF.Sqrt(Dot(a, a));
        static Vec3 Norm(Vec3 a) { float l = Len(a); return l < 1e-5f ? a : Mul(a, 1f / l); }
        static Vec3 Flat(Vec3 v, Vec3 up) => Norm(Sub(v, Mul(up, Dot(v, up))));
        static float FlatLen(Vec3 v, Vec3 up) => Len(Sub(v, Mul(up, Dot(v, up))));
        // Vorzeichenbehafteter Winkel (Grad) von a nach b um up; positiv = rechts (Unity, linkshändig)
        static float SignedAngle(Vec3 a, Vec3 b, Vec3 up) => MathF.Atan2(Dot(up, Cross(a, b)), Dot(a, b)) * 57.29578f;
        // Richtung um up drehen (Grad, positiv = rechts)
        static Vec3 Rot(Vec3 dir, Vec3 up, float deg)
        {
            float r = deg / 57.29578f;
            var right = Norm(Cross(up, dir));
            return Norm(Add(Mul(dir, MathF.Cos(r)), Mul(right, MathF.Sin(r))));
        }

        static bool Vec(IntPtr method, IntPtr obj, out Vec3 v)
        {
            v = default;
            if (method == IntPtr.Zero || obj == IntPtr.Zero) return false;
            var boxed = Il2Cpp.Invoke(method, obj, null, "Vector3");
            if (boxed == IntPtr.Zero) return false;
            v = *(Vec3*)Il2Cpp.il2cpp_object_unbox(boxed);
            return true;
        }

        static bool Position(IntPtr component, out Vec3 pos)
        {
            pos = default;
            var t = Il2Cpp.Invoke(mGetTransform, component, null, "transform");
            return Vec(mGetPosition, t, out pos);
        }

        static double GetDouble(IntPtr method, IntPtr obj)
        {
            if (method == IntPtr.Zero) return 0;
            var boxed = Il2Cpp.Invoke(method, obj, null, "double");
            return boxed == IntPtr.Zero ? 0 : *(double*)Il2Cpp.il2cpp_object_unbox(boxed);
        }

        static float GetFloat(IntPtr method, IntPtr obj)
        {
            if (method == IntPtr.Zero) return 0;
            var boxed = Il2Cpp.Invoke(method, obj, null, "float");
            return boxed == IntPtr.Zero ? 0 : *(float*)Il2Cpp.il2cpp_object_unbox(boxed);
        }

        static string NameOf(IntPtr obj)
        {
            if (obj == IntPtr.Zero || mGetName == IntPtr.Zero) return "?";
            return Il2Cpp.ReadString(Il2Cpp.Invoke(mGetName, obj, null, "name")) ?? "?";
        }

        static bool DustAt(IntPtr self, Vec3 p) => _dustUnder != null && _dustUnder(self, &p, mDustUnder) != 0;

        // ---------------- Physik ----------------

        static int CastAll(bool sphere, Vec3 origin, Vec3 dir, float range, float radius)
        {
            var m = sphere ? mSphereCast : mRaycastNA;
            if (_hits == IntPtr.Zero || m == IntPtr.Zero) return 0;
            int mask = ObstacleMask, qti = 1;          // QueryTriggerInteraction.Ignore
            float r = radius, dist = range;
            IntPtr* a = stackalloc IntPtr[7];
            int i = 0;
            a[i++] = (IntPtr)(&origin);
            if (sphere) a[i++] = (IntPtr)(&r);
            a[i++] = (IntPtr)(&dir);
            a[i++] = _hits;
            a[i++] = (IntPtr)(&dist);
            a[i++] = (IntPtr)(&mask);
            a[i++] = (IntPtr)(&qti);
            var boxed = Il2Cpp.Invoke(m, IntPtr.Zero, a, sphere ? "SphereCast" : "Raycast");
            if (boxed == IntPtr.Zero) return 0;
            return Math.Clamp(*(int*)Il2Cpp.il2cpp_object_unbox(boxed), 0, HitCap);
        }

        static byte* HitAt(int i) => (byte*)(_hits + 0x20 + i * _hitSize);
        static float HitDist(byte* h) => *(float*)(h + 0x1C);
        static Vec3 HitNormal(byte* h) => *(Vec3*)(h + 0x0C);

        static IntPtr HitCollider(byte* h)
        {
            if (_hitCollider == null) return IntPtr.Zero;
            return _hitCollider((IntPtr)(_colliderThunk ? h - 0x10 : h), IntPtr.Zero);
        }

        static bool IsOwn(IntPtr col, IntPtr body)
        {
            if (col == IntPtr.Zero) return true;
            if (body == IntPtr.Zero || mAttachedRb == IntPtr.Zero) return false;
            return Il2Cpp.Invoke(mAttachedRb, col, null, "attachedRigidbody") == body;
        }

        const float SensorRadius = 0.7f, SensorLift = 0.3f, GroundNormal = 0.55f;

        // Freie Strecke in Richtung dir (bis range)
        static float Clear(Vec3 origin, Vec3 dir, float range, Vec3 up, IntPtr body, out IntPtr what)
        {
            what = IntPtr.Zero;
            int n = CastAll(true, origin, dir, range, SensorRadius);
            float best = range;
            for (int i = 0; i < n; i++)
            {
                var h = HitAt(i);
                float d = HitDist(h);
                if (d <= 0.05f || d >= best) continue;               // Startüberlappung / weiter weg
                if (Dot(HitNormal(h), up) > GroundNormal) continue;  // Boden oder befahrbarer Hang
                var col = HitCollider(h);
                if (IsOwn(col, body)) continue;
                best = d; what = col;
            }
            return best;
        }

        static readonly float[] AvoidMags = { 15f, 30f, 45f, 60f, 80f, 100f, 120f };

        // ---------------- Fahren ----------------

        static void Drive(IntPtr self, float throttle, float steer)
        {
            if (mTestDrive == IntPtr.Zero) return;
            float t = Math.Clamp(throttle, -1f, 1f), s = Math.Clamp(steer, -1f, 1f);
            byte b1 = 0, b2 = 0;
            IntPtr* a = stackalloc IntPtr[4];
            a[0] = (IntPtr)(&t); a[1] = (IntPtr)(&s); a[2] = (IntPtr)(&b1); a[3] = (IntPtr)(&b2);
            Il2Cpp.Invoke(mTestDrive, self, a, "TestDrive");
        }

        static void Hold(IntPtr self)
        {
            Drive(self, 0f, 0f);
            if (oBrake >= 0) *(byte*)(self + oBrake) = 1;
        }

        // ---------------- Garage / Abladen ----------------

        static IntPtr[] AllGarages()
        {
            if (fGarageAll == IntPtr.Zero) return Array.Empty<IntPtr>();
            IntPtr list = IntPtr.Zero;
            Il2Cpp.il2cpp_field_static_get_value(fGarageAll, &list);
            if (list == IntPtr.Zero) return Array.Empty<IntPtr>();
            var items = *(IntPtr*)(list + 0x10);            // List<T>._items
            int size = *(int*)(list + 0x18);                // List<T>._size
            if (items == IntPtr.Zero || size <= 0 || size > 64) return Array.Empty<IntPtr>();
            var r = new IntPtr[size];
            for (int i = 0; i < size; i++) r[i] = *(IntPtr*)(items + 0x20 + i * 8);
            return r;
        }

        static bool UnloadPoint(IntPtr garage, out Vec3 p)
        {
            p = default;
            if (garage == IntPtr.Zero) return false;
            if (oUnloadPoint >= 0)
            {
                var t = *(IntPtr*)(garage + oUnloadPoint);
                if (t != IntPtr.Zero && Vec(mGetPosition, t, out p)) return true;
            }
            return Position(garage, out p);
        }

        static IntPtr NearestGarage(Vec3 pos, out Vec3 point)
        {
            point = default;
            IntPtr best = IntPtr.Zero; float bestD = float.MaxValue;
            foreach (var g in AllGarages())
            {
                if (!UnloadPoint(g, out var p)) continue;
                float d = Len(Sub(p, pos));
                if (d < bestD) { bestD = d; best = g; point = p; }
            }
            return best;
        }

        static bool ReallyUnderHood(IntPtr garage, IntPtr self)
        {
            if (_origUnderHood == null || garage == IntPtr.Zero) return false;
            float dummy;
            return _origUnderHood(garage, self, &dummy, IntPtr.Zero) != 0;
        }

        // Steht schon ein anderer Bot an dieser Garage?
        static bool GarageTaken(IntPtr garage, Bot me)
        {
            foreach (var b in Bots.Values)
                if (b != me && b.UsesGarage(garage)) return true;
            return false;
        }

        static double Collect(IntPtr garage, IntPtr self, float amount, out string msg)
        {
            msg = null;
            if (mCollect == IntPtr.Zero || garage == IntPtr.Zero) return 0;
            IntPtr msgPtr = IntPtr.Zero; byte flag = 0; float amt = amount;
            IntPtr* a = stackalloc IntPtr[4];
            a[0] = self; a[1] = (IntPtr)(&amt); a[2] = (IntPtr)(&msgPtr); a[3] = (IntPtr)(&flag);
            _forceHoodFor = self;
            IntPtr boxed;
            try { boxed = Il2Cpp.Invoke(mCollect, garage, a, "Garage.Collect"); }
            finally { _forceHoodFor = IntPtr.Zero; }
            msg = Il2Cpp.ReadString(msgPtr);
            return boxed == IntPtr.Zero ? 0 : *(double*)Il2Cpp.il2cpp_object_unbox(boxed);
        }

        // =====================================================================================
        // Ein Fahrzeug mit Autodrive
        // =====================================================================================
        sealed class Bot
        {
            public readonly IntPtr V;
            public readonly string Name;
            public int Mode;
            public long LastSeen = _now;
            public bool StopReq, UnloadReq, FinishReq;
            public string StopWhy = "";

            public string Phase = "", FieldText = "", ObstacleText = "–", PlanText = "", UnloadInfo = "";
            public int Lane, StuckCount;
            public float Travelled, CrossError, TripDistance;
            public bool Avoiding;
            double _kg, _cap;
            float _speed;
            bool _wantClean;

            public Bot(IntPtr v, string name) { V = v; Name = name; }

            void L(string msg) => Log.Write($"{Name}: {msg}");

            public bool Busy => Mode != 0 || _trip != Trip.None || _remoteActive || UnloadReq;
            public bool UsesGarage(IntPtr g) => _tripGarage == g && (_trip == Trip.Unloading || (_trip == Trip.ToGarage && TripDistance < 25f));

            public string TripText => _trip switch
            {
                Trip.ToGarage => $"fährt zur Garage · {TripDistance:0} m",
                Trip.WaitGarage => "wartet, Garage besetzt",
                Trip.Unloading => "lädt ab ...",
                Trip.Back => $"fährt zurück · {TripDistance:0} m",
                _ => _remoteActive ? "lädt ab ..." : UnloadInfo,
            };

            public string Summary
            {
                get
                {
                    string tank = _cap > 0 ? $"  ·  {_kg / _cap * 100:0} %" : "";
                    if (_trip != Trip.None) return TripText + tank;
                    if (Mode == (int)Vehicle.Mode.Cruise) return "Tempomat" + tank;
                    return (Phase.Length > 0 ? Phase : "startet") + tank;
                }
            }

            // ---- Feld (beim Start übernommen) ----
            float _L, _w; int _count, _shift;
            float ShiftMul => _shift == 1 ? 0f : 1f;

            public void Start(int mode)
            {
                Mode = mode;
                _activeMode = 0;      // Neustart im nächsten Schritt
                StopReq = false;
                LastResult.Remove(V);
            }

            // Putzen ohne Fahrer: Das Spiel ruft Clean() im Frame-Update im Takt von nextWork auf.
            // Läuft das ohne Fahrer nicht (nextWork bleibt stehen), übernimmt der Bot den Takt.
            float _myCleanAt;
            bool _cleanTakeover;
            void CleanFallback(IntPtr self)
            {
                if (mClean == IntPtr.Zero || mTime == IntPtr.Zero || oNextWork < 0) return;
                float now = GetFloat(mTime, IntPtr.Zero);
                float next = *(float*)(self + oNextWork);
                bool stale = now > next + CleanInterval * 3f + 0.3f;
                if (!stale) { if (_cleanTakeover) { _cleanTakeover = false; L("Spiel putzt wieder selbst."); } return; }
                if (!_cleanTakeover) { _cleanTakeover = true; L("Spiel putzt ohne Fahrer nicht, Bot übernimmt das Putzen."); }
                if (now < _myCleanAt) return;
                _myCleanAt = now + CleanInterval;
                Il2Cpp.Invoke(mClean, self, null, "Clean");
            }

            // Spieler ist ausgestiegen, Bot fährt weiter. Der Tempomat lenkt nicht und weicht nicht aus:
            // ohne Fahrer würde er stur geradeaus fahren, deshalb endet er (nach einer laufenden Abladefahrt).
            public void DriverLeft()
            {
                if (Mode != (int)Vehicle.Mode.Cruise) { L("ausgestiegen, fährt ohne Fahrer weiter."); return; }
                if (_trip != Trip.None) { _resumeMode = 0; L("ausgestiegen: Tempomat endet nach der Abladefahrt."); }
                else { StopReq = true; StopWhy = "ausgestiegen, Tempomat braucht einen Fahrer"; }
            }

            // Am Ende: Bremse (nicht, wenn der Spieler selbst fährt und der Bot nur abgeladen hat)
            public void Release(IntPtr self, bool mine)
            {
                if (!mine || _hadControl) Hold(self);
                if (!mine && oIsDriven >= 0 && Core.MountedNow != self) *(byte*)(self + oIsDriven) = 0;
                if (FieldText.Length > 0) LastResult[V] = FieldText;
                // Garage hat nichts genommen: Sperrzeit merken, sonst startet sofort die nächste Abladefahrt
                if (mine && _retryAt > _mineRetryAt) _mineRetryAt = _retryAt;
            }

            void StopNow(string why)
            {
                if (Mode != 0 || _trip != Trip.None) L("Autodrive aus: " + why);
                if (_trip != Trip.None) L("Abladefahrt abgebrochen: " + why);
                Mode = 0; _activeMode = 0; Avoiding = false;
                _trip = Trip.None; _remoteActive = false; _unloadFrames = 0; UnloadReq = false;
            }

            // ---------------- Pro Physik-Frame ----------------
            public void Step(IntPtr self, bool mine)
            {
                LastSeen = _now;
                if (StopReq) { StopReq = false; StopNow(StopWhy); return; }

                // Ohne Fahrer: Bürste anlassen (IsDriven setzt der Hook nur für den Physik-Schritt)
                if (mine) _wantClean = oCleaning >= 0 && *(byte*)(self + oCleaning) != 0;
                else if (_wantClean && oCleaning >= 0)
                {
                    *(byte*)(self + oCleaning) = 1;
                    CleanFallback(self);
                }

                _kg = GetDouble(mDustKg, self);
                _cap = GetDouble(mDustCap, self);
                _speed = GetFloat(mForwardSpeed, self);
                CheckFlip(self);

                var t = Il2Cpp.Invoke(mGetTransform, self, null, "transform");
                if (!Vec(mGetPosition, t, out var pos) || !Vec(mGetForward, t, out var fwdRaw) || !Vec(mGetUp, t, out var up)) return;
                up = Norm(up);
                var fwd = Flat(fwdRaw, up);

                // Lage der Bürste lernen (lastCleanPoint = letzter geputzter Punkt)
                if (oLastClean >= 0 && oHasClean >= 0 && *(byte*)(self + oHasClean) != 0)
                {
                    var lc = *(Vec3*)(self + oLastClean);
                    float off = Dot(Sub(lc, pos), fwd);
                    if (MathF.Abs(off) < 10f) _brushOff = _brushOff * 0.95f + off * 0.05f;
                }

                // ---- Abladen auslösen ----
                if (_trip == Trip.None && !_remoteActive)
                {
                    bool full = _cap > 0 && _kg >= _cap * UnloadAt;
                    bool trigger = (AutoUnload && full && _now >= _retryAt) || (UnloadReq && _kg > 0.01);
                    UnloadReq = false;
                    if (trigger)
                    {
                        if (UnloadMode == 1) { _remoteActive = true; L($"Abladen (Ferne): {_kg:0} / {_cap:0} kg"); }
                        else
                        {
                            _tripGarage = NearestGarage(pos, out _);
                            if (_tripGarage == IntPtr.Zero) { UnloadInfo = "keine Garage gefunden"; _retryAt = _now + 10_000; }
                            else
                            {
                                _resumePos = pos; _resumeMode = Mode;
                                _trip = Trip.ToGarage; ResetStuck(pos);
                                L($"Abladefahrt startet: {_kg:0} / {_cap:0} kg");
                            }
                        }
                    }
                }

                if (_remoteActive && RemoteUnloadStep(self)) _remoteActive = false;   // Autodrive läuft dabei weiter

                // ---- Abladefahrt ----
                if (_trip != Trip.None) { _hadControl = true; TripStep(self, pos, fwd, up); return; }

                // ---- Autodrive ----
                int mode = Mode;
                if (mode == 0)
                {
                    // nur Abladen aus der Ferne: Fährt der Spieler selbst, nicht bremsen
                    _activeMode = 0; Phase = ""; Avoiding = false;
                    if (!mine) Hold(self);
                    return;
                }
                _hadControl = true;

                if (mode == (int)Vehicle.Mode.Cruise)
                {
                    _activeMode = mode;
                    float steer = mine && oSteer >= 0 ? *(float*)(self + oSteer) : 0f;   // Lenkung bleibt beim Spieler
                    Drive(self, Throttle, steer);
                    Phase = "Tempomat";
                    return;
                }

                // ---- Bahnen ----
                if (_activeMode != mode)
                {
                    _activeMode = mode;
                    _origin = pos; _ref = fwd;
                    _L = LaneLength; _count = (int)LaneCount; _shift = Shift;
                    _w = WidthFor(GetFloat(mCleaningWidth, self));
                    ResetLanes();
                    _sweep = Sweep.Lanes;
                    _round = 0; _skipped = 0; _fixHas = false;
                    _dirty.Clear(); _skipList.Clear();
                    FieldText = "";
                    ResetStuck(pos);
                    L($"Bahnen-Modus gestartet: {_count} Bahnen x {_L:0} m, Abstand {_w:0.00} m, Bürstenlage {_brushOff:+0.0;-0.0} m");
                }

                _ref = Flat(_ref, up);                                          // an Mondoberfläche anpassen
                var side = LaneSide(up);                                        // Versatz-Richtung

                if (FinishReq)
                {
                    FinishReq = false;
                    if (_sweep == Sweep.Lanes) StartScan(Lane + 1, up);
                }

                switch (_sweep)
                {
                    case Sweep.Lanes: LanesStep(self, pos, fwd, up, side); break;
                    case Sweep.Scan: ScanStep(self); break;
                    case Sweep.Fix: FixStep(self, pos, fwd, up); break;
                    default: Hold(self); break;
                }
            }

            // ---------------- Abladefahrt ----------------
            Trip _trip;
            bool _remoteActive;
            IntPtr _tripGarage, _remoteGarage;
            Vec3 _resumePos;
            int _resumeMode, _unloadFrames;
            internal long _retryAt;
            double _unloadStartKg;

            void TripStep(IntPtr self, Vec3 pos, Vec3 fwd, Vec3 up)
            {
                Phase = "Abladefahrt";
                switch (_trip)
                {
                    case Trip.ToGarage:
                    case Trip.WaitGarage:
                    {
                        UnloadPoint(_tripGarage, out var gp);
                        float dist = FlatLen(Sub(gp, pos), up);
                        if (dist < 18f && GarageTaken(_tripGarage, this))
                        {
                            // anderer Bot lädt gerade ab: davor warten
                            _trip = Trip.WaitGarage; TripDistance = dist; Hold(self); ResetStuck(pos);
                            break;
                        }
                        _trip = Trip.ToGarage;
                        TripDistance = NavigateTo(self, gp, pos, fwd, up, 20f, 7f);
                        if (ReallyUnderHood(_tripGarage, self) || TripDistance < 7f)
                        {
                            _trip = Trip.Unloading; _unloadFrames = 0;
                            L($"An der Garage ({TripDistance:0.0} m).");
                        }
                        else if (StuckCount >= 4)
                        {
                            L("Komme nicht zur Garage durch, lade hier ab.");
                            _trip = Trip.Unloading; _unloadFrames = 0;
                        }
                        break;
                    }
                    case Trip.Unloading:
                        Hold(self);
                        if (UnloadStep(_tripGarage, self)) { _trip = Trip.Back; ResetStuck(pos); }
                        break;
                    case Trip.Back:
                        TripDistance = NavigateTo(self, _resumePos, pos, fwd, up, 10f, 4f);
                        if (TripDistance < 4f || StuckCount >= 4)
                        {
                            bool blocked = StuckCount >= 4;
                            _trip = Trip.None;
                            Mode = _resumeMode;
                            _lanePlanned = false;      // Bahn neu abtasten: nur der Rest mit Dreck wird gefahren
                            ResetStuck(pos);
                            if (_resumeMode == 0) Hold(self);
                            L(blocked ? "Rückweg blockiert, mache hier weiter." : "Zurück, Autodrive geht weiter.");
                        }
                        break;
                }
            }

            // Ein Abladeschritt pro Physik-Frame. true = fertig.
            bool UnloadStep(IntPtr garage, IntPtr self)
            {
                if (_unloadFrames++ == 0) _unloadStartKg = _kg;
                double taken = Collect(garage, self, 1000f, out var msg);
                if (_unloadFrames <= 2) L($"Collect: {taken:0.00} kg  {msg}");
                double now = GetDouble(mDustKg, self);
                if (now < 0.01 || taken <= 0 || _unloadFrames > 600)
                {
                    UnloadInfo = now < 0.01 ? $"abgeladen ({_unloadStartKg - now:0} kg)" : $"gestoppt: {msg ?? "nichts angenommen"}";
                    // Garage nimmt nichts (z. B. Auslaufband voll): nicht sofort wieder hinfahren
                    if (now >= 0.01) _retryAt = _now + 60_000;
                    L("Abladen fertig: " + UnloadInfo);
                    _unloadFrames = 0;
                    if (V == Mine) _unloadInfo = UnloadInfo;
                    return true;
                }
                return false;
            }

            // Aus der Ferne: alle Garagen probieren, bis eine annimmt.
            bool RemoteUnloadStep(IntPtr self)
            {
                if (_remoteGarage == IntPtr.Zero)
                {
                    foreach (var g in AllGarages())
                        if (Collect(g, self, 50f, out var m) > 0) { _remoteGarage = g; break; }
                        else if (m != null) UnloadInfo = m;
                    if (_remoteGarage == IntPtr.Zero) { _retryAt = _now + 10_000; return true; }
                }
                if (UnloadStep(_remoteGarage, self)) { _remoteGarage = IntPtr.Zero; return true; }
                return false;
            }

            // ---------------- Ausweichen ----------------
            int _avoidSide = 1, _avoidTick;
            long _sideUntil, _obstacleLogAt;
            float _avoidAngle, _frontClear = 99f, _bestClear = 99f;
            IntPtr _lastObstacle;

            // Winkel (Grad), um den die Wunschrichtung gedreht werden muss, um frei zu fahren
            float AvoidAngle(Vec3 origin, Vec3 desired, Vec3 up, float need, IntPtr body)
            {
                if (_avoidTick++ % 2 != 0) return _avoidAngle;    // jeden 2. Physik-Frame neu
                float d0 = Clear(origin, desired, need, up, body, out var what);
                _frontClear = d0;
                if (d0 >= need - 0.01f) { _avoidAngle = 0f; _bestClear = d0; Avoiding = false; return 0f; }

                long now = _now;
                if (now > _sideUntil)
                {
                    float l = Clear(origin, Rot(desired, up, -35f), need, up, body, out _);
                    float r = Clear(origin, Rot(desired, up, 35f), need, up, body, out _);
                    _avoidSide = r >= l ? 1 : -1;
                }

                float bestA = 0f, bestD = d0;
                bool found = false;
                for (int pass = 0; pass < 2 && !found; pass++)
                {
                    int side = pass == 0 ? _avoidSide : -_avoidSide;
                    foreach (var m in AvoidMags)
                    {
                        float a = side * m;
                        float d = Clear(origin, Rot(desired, up, a), need, up, body, out _);
                        if (d > bestD) { bestD = d; bestA = a; }
                        if (d >= need - 0.01f) { bestA = a; bestD = d; found = true; break; }
                    }
                }
                if (bestA != 0f) _avoidSide = MathF.Sign(bestA) > 0 ? 1 : -1;
                _sideUntil = now + 2500;
                _avoidAngle = bestA; _bestClear = bestD;
                Avoiding = true;

                if (what != IntPtr.Zero && (what != _lastObstacle || now > _obstacleLogAt))
                {
                    _lastObstacle = what; _obstacleLogAt = now + 5000;
                    ObstacleText = $"{NameOf(what)} · {d0:0.0} m";
                    L($"Hindernis: {ObstacleText} -> weiche {bestA:+0;-0} Grad aus (frei {bestD:0.0} m)");
                }
                return bestA;
            }

            // Höhe des Bodens unter dem Fahrzeug relativ zur Fahrzeug-Position (für die Sensorhöhe)
            float _groundOff = -0.6f;
            int _groundTick;
            void UpdateGround(Vec3 pos, Vec3 up, IntPtr body)
            {
                if (_groundTick++ % 25 != 0) return;
                int n = CastAll(false, Add(pos, Mul(up, 3f)), Mul(up, -1f), 12f, 0f);
                float best = float.MaxValue;
                for (int i = 0; i < n; i++)
                {
                    var h = HitAt(i);
                    float d = HitDist(h);
                    if (d <= 0f || d >= best) continue;
                    if (IsOwn(HitCollider(h), body)) continue;
                    best = d;
                }
                if (best < float.MaxValue) _groundOff = Math.Clamp(3f - best, -4f, 1f);
            }

            // ---------------- Lenkung ----------------
            float _steerSign = 1f;
            int _wrongTicks;
            Vec3 _lastFwd;
            long _lastTick;

            // Lenkbefehl aus Kursfehler; erkennt dabei automatisch, ob die Lenkung andersherum wirkt.
            float SteerFor(float err, Vec3 fwd, Vec3 up)
            {
                float cmd = Math.Clamp(err / 25f, -1f, 1f);
                long now = _now;
                float dt = Math.Max(1, now - _lastTick) / 1000f;
                bool learn = now > _reverseUntil + 600;               // beim Rückwärtsfahren dreht sich die Wirkung um
                if (dt < 0.5f && learn)
                {
                    float yawRate = SignedAngle(_lastFwd, fwd, up) / dt;
                    if (MathF.Abs(cmd) > 0.6f && MathF.Abs(yawRate) > 8f)
                    {
                        if (MathF.Sign(yawRate) != MathF.Sign(cmd * _steerSign)) { if (++_wrongTicks > 25) { _steerSign = -_steerSign; _wrongTicks = 0; L("Lenkrichtung umgedreht."); } }
                        else _wrongTicks = 0;
                    }
                }
                _lastFwd = fwd; _lastTick = now;
                return cmd * _steerSign;
            }

            // Zentrale Lenkung: Ausweichen + Kursregler + Feststecken. need = wie weit frei sein muss.
            // uturnSide: bei Kehrtwende (> 150 Grad) zu dieser Seite drehen.
            float Steer(IntPtr self, Vec3 desired, float throttle, Vec3 pos, Vec3 fwd, Vec3 up, float need, Vec3 uturnSide = default)
            {
                long now = _now;
                if (AvoidObstacles && need > 1.5f && mSphereCast != IntPtr.Zero && now >= _reverseUntil)
                {
                    IntPtr body = oBody >= 0 ? *(IntPtr*)(self + oBody) : IntPtr.Zero;
                    UpdateGround(pos, up, body);
                    var origin = Add(pos, Mul(up, _groundOff + SensorLift + SensorRadius));
                    float a = AvoidAngle(origin, desired, up, Math.Min(need, SightRange), body);
                    if (a != 0f) { desired = Rot(desired, up, a); throttle *= 0.7f; }
                    if (_frontClear < 3f) throttle = Math.Min(throttle, 0.4f);
                    if (Avoiding && _bestClear < 1.2f)
                    {
                        // ringsum zu: zurücksetzen und dabei die Nase zur Ausweichseite drehen
                        _reverseUntil = now + 1200;
                        _graceUntil = _reverseUntil + 3000;
                        _reverseSteer = -_avoidSide * _steerSign;
                        L($"Kein Durchkommen (frei {_bestClear:0.0} m), setze zurück.");
                    }
                }
                else if (now >= _reverseUntil) Avoiding = false;

                float err = SignedAngle(fwd, desired, up);
                if (MathF.Abs(err) > 150f && Len(uturnSide) > 0.5f)
                {
                    float toSide = SignedAngle(fwd, uturnSide, up);
                    err = MathF.Abs(err) * MathF.Sign(toSide == 0 ? 1 : toSide);
                }
                float steer = SteerFor(err, fwd, up);
                throttle *= MathF.Abs(err) > 35f ? 0.45f : 1f;
                Unstick(pos, ref throttle, ref steer);
                Drive(self, throttle, steer);
                return err;
            }

            // Auf einen Punkt zufahren; liefert Entfernung.
            float NavigateTo(IntPtr self, Vec3 target, Vec3 pos, Vec3 fwd, Vec3 up, float slowRadius, float arriveRadius)
            {
                var to = Sub(target, pos);
                float dist = FlatLen(to, up);
                float throttle = Math.Max(Throttle, 0.8f) * Math.Clamp(dist / slowRadius, 0.35f, 1f);
                Steer(self, Flat(to, up), throttle, pos, fwd, up, dist - arriveRadius);
                return dist;
            }

            // ---------------- Feststecken ----------------
            // Festgefahren = Gas gegeben, aber 2,5 s lang fast keine Geschwindigkeit UND kaum Weg.
            // Nach jedem Zurücksetzen gibt es eine Schonzeit, damit Anfahren/Umschalten nicht als
            // Feststecken zählt (sonst schaukelt sich Vor/Zurück endlos auf).
            Vec3 _stuckRef, _stuckSpot;
            long _slowSince, _graceUntil, _reverseUntil;
            float _reverseSteer;
            int _spotCount;
            bool SpotBlocked => _spotCount >= 4;   // gleiche Stelle 4x: hier geht es nicht weiter

            void ResetStuck(Vec3 pos)
            {
                _stuckRef = pos; _slowSince = 0; _reverseUntil = 0; StuckCount = 0; _spotCount = 0;
                _graceUntil = _now + 2500;
            }

            bool Unstick(Vec3 pos, ref float throttle, ref float steer)
            {
                long now = _now;
                if (now < _reverseUntil) { throttle = -0.7f; steer = _reverseSteer; return true; }

                bool pushing = MathF.Abs(throttle) >= 0.2f;
                bool slow = MathF.Abs(_speed) < 0.4f;
                if (!pushing || !slow || now < _graceUntil) { _slowSince = 0; return false; }
                if (_slowSince == 0) { _slowSince = now; _stuckRef = pos; return false; }
                if (now - _slowSince < 2500) return false;
                if (Len(Sub(pos, _stuckRef)) > 1.2f) { _slowSince = now; _stuckRef = pos; return false; }   // langsam, aber kommt voran

                StuckCount++;
                _spotCount = Len(Sub(pos, _stuckSpot)) < 4f ? _spotCount + 1 : 1;
                _stuckSpot = pos;
                // abwechselnd mit eingeschlagener Lenkung zurück, damit die Nase jedes Mal anders steht
                float side = MathF.Sign(steer == 0 ? 1 : steer);
                _reverseSteer = (_spotCount % 2 == 1) ? -side : side;
                _avoidSide = -_avoidSide; _sideUntil = now + 4000;
                _reverseUntil = now + 1400;
                _graceUntil = _reverseUntil + 3000;
                _slowSince = 0;
                L($"Festgefahren ({StuckCount}, Stelle {_spotCount}x), setze zurück. Tempo {_speed:0.00} m/s");
                return true;
            }

            // Umgekippt: spieleigenes Aufrichten auslösen
            long _flipAt;
            void CheckFlip(IntPtr self)
            {
                if (oFlipped < 0 || mFlipUpright == IntPtr.Zero || *(byte*)(self + oFlipped) == 0) return;
                long now = _now;
                if (now < _flipAt) return;
                _flipAt = now + 3000;
                Il2Cpp.Invoke(mFlipUpright, self, null, "FlipUpright");
                L("Umgekippt, richte auf.");
            }

            // ---------------- Zustand Bahnen-Modus ----------------
            int _activeMode;
            Vec3 _origin, _ref;
            const float Lookahead = 4f;     // m: wie weich auf die Linie eingeschwenkt wird
            const float MaxApproach = 70f;  // Grad: maximaler Einfahrwinkel zur Linie
            Sweep _sweep;
            float _brushOff;                // Lage der Bürste entlang der Fahrtrichtung (m, + = vorn)

            bool _hadControl;               // Bot hat gelenkt/Gas gegeben (nicht nur abgeladen)

            // Bei "kein Versatz" bleibt die Seite rechts: sie bestimmt dann nur die Wenderichtung.
            Vec3 LaneSide(Vec3 up) => Mul(Norm(Cross(up, _ref)), _shift == 0 ? -1f : 1f);

            void FinishField(IntPtr self, string why)
            {
                Hold(self);
                FieldText = why;
                L("Feld fertig: " + why);
                _sweep = Sweep.Done;
                Mode = 0;
                _activeMode = 0;
            }

            // ---------------- Bahnen nur dort fahren, wo Dreck liegt ----------------
            // Vor jeder Bahn wird ihr Streifen (Bürstenbreite) per DustUnder() abgetastet. Daraus entstehen
            // Dreck-Abschnitte entlang der Bahn. Bahn ohne Dreck: übersprungen. Sonst wird nur vom ersten bis
            // zum letzten Dreck gefahren, in der Richtung, deren Anfang näher liegt; über saubere Lücken schneller.
            const float MergeGap = 8f;      // m: kürzere saubere Lücken werden mitgefahren
            const float SegPad = 1f;        // m: Zugabe vor/nach jedem Dreck-Abschnitt
            const int MaxEmptyEndless = 8;  // endlos-Modus: so viele saubere Bahnen in Folge = fertig
            readonly List<(float a, float b)> _segs = new List<(float a, float b)>();
            bool _lanePlanned, _laneFwd, _approach;
            float _laneA, _laneB, _laneDirt;
            int _emptyRun, _lanesSkipped;

            void ResetLanes()
            {
                Lane = 0; _lanePlanned = false; _emptyRun = 0; _lanesSkipped = 0; _segs.Clear(); PlanText = "";
            }

            void PlanLane(IntPtr self, int i, Vec3 side)
            {
                _segs.Clear();
                _laneDirt = 0f;
                if (!DirtOnly || _dustUnder == null) { _segs.Add((0f, _L)); _laneDirt = _L; return; }

                float step = _L > 150f ? 1f : 0.5f;
                float lineC = i * _w * ShiftMul;
                float o1 = 0.42f * _w, o2 = 0.14f * _w;
                float segA = -1f, last = 0f;
                for (float a = 0f; a <= _L + 0.001f; a += step)
                {
                    var basePt = Add(_origin, Mul(_ref, a));
                    bool d = DustAt(self, Add(basePt, Mul(side, lineC - o1))) || DustAt(self, Add(basePt, Mul(side, lineC - o2)))
                          || DustAt(self, Add(basePt, Mul(side, lineC + o2))) || DustAt(self, Add(basePt, Mul(side, lineC + o1)));
                    if (!d) continue;
                    _laneDirt += step;
                    if (segA < 0f) segA = a;
                    else if (a - last > MergeGap) { _segs.Add((segA, last)); segA = a; }
                    last = a;
                }
                if (segA >= 0f) _segs.Add((segA, last));
                for (int k = 0; k < _segs.Count; k++)
                    _segs[k] = (Math.Max(0f, _segs[k].a - SegPad), Math.Min(_L, _segs[k].b + SegPad));
            }

            bool InDirt(float x)
            {
                foreach (var s in _segs) if (x >= s.a && x <= s.b) return true;
                return false;
            }

            void LaneDone(Vec3 pos)
            {
                Lane++; _lanePlanned = false;
                ResetStuck(pos);
            }

            void FieldDone(IntPtr self, Vec3 up)
            {
                string skipped = _lanesSkipped > 0 ? $", {_lanesSkipped} schon sauber" : "";
                L($"Alle {Lane} Bahnen erledigt{skipped}.");
                if (FinishPass && _dustUnder != null) StartScan(Math.Max(1, Lane), up);
                else FinishField(self, $"{Lane} Bahnen erledigt{skipped}");
            }

            void LanesStep(IntPtr self, Vec3 pos, Vec3 fwd, Vec3 up, Vec3 side)
            {
                int total = _count;
                var rel = Sub(pos, _origin);
                float along = Dot(rel, _ref);
                float cross = Dot(rel, side);

                // ---- nächste Bahn planen (eine pro Frame, verteilt die Abtastung) ----
                if (!_lanePlanned)
                {
                    if (total > 0 && Lane >= total) { FieldDone(self, up); return; }
                    PlanLane(self, Lane, side);
                    if (_segs.Count == 0)
                    {
                        L($"Bahn {Lane + 1}: sauber, übersprungen.");
                        _lanesSkipped++; _emptyRun++;
                        Lane++;
                        if (total == 0 && _emptyRun >= MaxEmptyEndless) { FieldDone(self, up); return; }
                        Hold(self);
                        Phase = $"Prüfe Bahn {Lane + 1}  ·  {_lanesSkipped} sauber übersprungen";
                        return;
                    }
                    _emptyRun = 0;
                    _laneA = _segs[0].a; _laneB = _segs[_segs.Count - 1].b;
                    _laneFwd = MathF.Abs(along - _laneA) <= MathF.Abs(along - _laneB);
                    _approach = true; _lanePlanned = true;
                    ResetStuck(pos);
                    PlanText = $"{_laneDirt:0} m Dreck, fahre {_laneA:0}–{_laneB:0} m";
                    L($"Bahn {Lane + 1}: {_laneDirt:0} m Dreck in {_segs.Count} Abschnitt(en), fahre {(_laneFwd ? _laneA : _laneB):0} -> {(_laneFwd ? _laneB : _laneA):0} m");
                }

                float dirS = _laneFwd ? 1f : -1f;
                var laneDir = Mul(_ref, dirS);
                float lineC = Lane * _w * ShiftMul;
                float crossErr = lineC - cross;
                float brushAlong = along + Dot(fwd, _ref) * _brushOff;
                float start = _laneFwd ? _laneA : _laneB, end = _laneFwd ? _laneB : _laneA;
                float prog = (brushAlong - start) * dirS;          // m hinter dem Bahnanfang (Bürste)
                float len = (end - start) * dirS;

                if (SpotBlocked)
                {
                    // 4x an derselben Stelle festgefahren: Rest der Bahn auslassen (Nachputzen holt es später)
                    L($"Bahn {Lane + 1} blockiert bei {prog:0} m, nehme nächste Bahn.");
                    LaneDone(pos);
                    return;
                }

                string lane = total > 0 ? $"Bahn {Math.Min(Lane + 1, total)} / {total}" : $"Bahn {Lane + 1}";

                // ---- Anfahrt: hinter den Bahnanfang, damit die Bürste den ersten Dreck sicher erwischt ----
                if (_approach)
                {
                    float leadAlong = start - dirS * (Math.Max(0f, _brushOff) + Overrun);
                    var lead = Add(_origin, Add(Mul(_ref, leadAlong), Mul(side, lineC)));
                    float ang = MathF.Abs(SignedAngle(fwd, laneDir, up));
                    float dLead = FlatLen(Sub(lead, pos), up);
                    if (dLead < 3f || (prog < -1f && ang < 35f && MathF.Abs(crossErr) < 1.5f)) _approach = false;
                    else
                    {
                        NavigateTo(self, lead, pos, fwd, up, 8f, 2f);
                        Travelled = 0f; CrossError = crossErr;
                        Phase = $"{lane}  ·  fährt an ({dLead:0} m)  ·  {PlanText}";
                        return;
                    }
                }

                // ---- Bahn abfahren ----
                float approach = Math.Clamp(MathF.Atan2(crossErr, Lookahead) * 57.29578f, -MaxApproach, MaxApproach);
                float rad = approach / 57.29578f;
                var desired = Norm(Add(Mul(laneDir, MathF.Cos(rad)), Mul(side, MathF.Sin(rad))));

                // saubere Lücke unter und vor der Bürste: schneller
                bool clean = prog > 0f && !InDirt(brushAlong) && !InDirt(brushAlong + dirS * 3f) && MathF.Abs(crossErr) < 1f;
                float throttle = clean ? Math.Min(1f, Throttle * 1.6f) : Throttle;
                float err = Steer(self, desired, throttle, pos, fwd, up, SightRange, side);

                if (prog >= len + 0.5f) { LaneDone(pos); return; }

                Travelled = Math.Clamp(prog, 0f, len);
                CrossError = crossErr;
                string what = Avoiding ? "weicht aus" : MathF.Abs(err) > 45f ? "Wenden" : clean ? $"sauber, eilt · {Travelled:0} / {len:0} m" : $"{Travelled:0} / {len:0} m";
                Phase = $"{lane}  ·  {what}  ·  Spur {crossErr:+0.0;-0.0;0.0} m";
            }

            // ---------------- Nachputzen ----------------
            readonly List<Vec3> _dirty = new List<Vec3>();
            readonly List<Vec3> _skipList = new List<Vec3>();   // nicht erreichbar (z. B. unter Felsen)
            int _scanIdx, _scanNA, _scanNC, _scanLanes, _round, _skipped;
            float _scanStep, _scanC0;
            Vec3 _scanRef, _scanSide;
            bool _fixHas, _runup;
            Vec3 _fixP;
            long _fixUntil;

            bool NearSkipped(Vec3 p)
            {
                float r = Math.Max(1f, _w);
                foreach (var s in _skipList) if (Len(Sub(s, p)) < r) return true;
                return false;
            }

            void StartScan(int lanes, Vec3 up)
            {
                float w = _w;
                _scanLanes = Math.Max(1, lanes);
                _scanStep = Math.Clamp(w * 0.5f, 0.5f, 2f);
                _scanC0 = -w * 0.5f;
                float c1 = (_scanLanes - 1) * w * ShiftMul + w * 0.5f;
                for (int k = 0; k < 8; k++)
                {
                    _scanNA = (int)(_L / _scanStep) + 1;
                    _scanNC = (int)((c1 - _scanC0) / _scanStep) + 1;
                    if ((long)_scanNA * _scanNC <= 40000) break;
                    _scanStep *= 1.5f;
                }
                _scanRef = _ref; _scanSide = LaneSide(up);
                _scanIdx = 0;
                _dirty.Clear();
                _round++;
                _sweep = Sweep.Scan;
                L($"Prüfe Feld (Runde {_round}): {_L:0} x {c1 - _scanC0:0.0} m, Raster {_scanStep:0.0} m, {_scanNA * _scanNC} Punkte");
            }

            void ScanStep(IntPtr self)
            {
                Hold(self);
                int total = _scanNA * _scanNC;
                int end = Math.Min(total, _scanIdx + 400);
                for (; _scanIdx < end; _scanIdx++)
                {
                    int ia = _scanIdx / _scanNC, ic = _scanIdx % _scanNC;
                    var p = Add(_origin, Add(Mul(_scanRef, ia * _scanStep), Mul(_scanSide, _scanC0 + ic * _scanStep)));
                    if (DustAt(self, p) && !NearSkipped(p)) _dirty.Add(p);
                }
                Phase = $"Prüfe Feld  ·  {(float)_scanIdx / Math.Max(1, total) * 100f:0} %  ·  {_dirty.Count} Stellen";
                if (_scanIdx < total) return;

                L($"Runde {_round}: {_dirty.Count} Stellen mit Staub.");
                if (_dirty.Count == 0) { FinishField(self, _skipped > 0 ? $"sauber · {_skipped} Stellen nicht erreichbar" : "komplett sauber"); return; }
                if (_round > 3) { FinishField(self, $"{_dirty.Count} Reststellen nach 3 Runden"); return; }
                _sweep = Sweep.Fix; _fixHas = false;
            }

            void FixStep(IntPtr self, Vec3 pos, Vec3 fwd, Vec3 up)
            {
                long now = _now;
                float tol = Math.Max(0.25f, _w * 0.35f);
                var right = Norm(Cross(up, fwd));
                var brush = Add(pos, Mul(fwd, _brushOff));

                // Alles, was die Bürste gerade überfährt, abhaken (wird in der nächsten Runde neu geprüft)
                for (int i = _dirty.Count - 1; i >= 0; i--)
                    if (FlatLen(Sub(_dirty[i], brush), up) < tol) _dirty.RemoveAt(i);
                if (_fixHas && FlatLen(Sub(_fixP, brush), up) < tol) _fixHas = false;

                if (!_fixHas)
                {
                    // nächste noch verschmutzte Stelle
                    int checks = 0;
                    while (_dirty.Count > 0 && checks++ < 40)
                    {
                        int bi = 0; float bd = float.MaxValue;
                        for (int i = 0; i < _dirty.Count; i++)
                        {
                            float d = FlatLen(Sub(_dirty[i], pos), up);
                            if (d < bd) { bd = d; bi = i; }
                        }
                        var p = _dirty[bi];
                        _dirty.RemoveAt(bi);
                        if (!DustAt(self, p)) continue;
                        _fixP = p; _fixHas = true; _runup = false;
                        _fixUntil = now + 25_000; ResetStuck(pos);
                        break;
                    }
                    if (!_fixHas)
                    {
                        if (_dirty.Count == 0) StartScan(_scanLanes, up);
                        else Hold(self);
                        return;
                    }
                }

                var to = Sub(_fixP, pos);
                float lf = Dot(to, fwd), lat = Dot(to, right), dist = FlatLen(to, up);
                float errTo = SignedAngle(fwd, Flat(to, up), up);

                if (now > _fixUntil || StuckCount >= 2)
                {
                    _skipped++; _fixHas = false; _skipList.Add(_fixP);
                    L($"Stelle übersprungen ({dist:0.0} m entfernt, {(StuckCount >= 2 ? "blockiert" : "Zeit")}).");
                    return;
                }

                if (_brushOff < -0.3f && lf < 0.3f && lf > _brushOff - 0.6f && MathF.Abs(lat) < tol)
                    Drive(self, 0.4f, 0f);                                     // Bürste hinten: geradeaus drüber
                else
                {
                    if (!_runup && dist < 6f && MathF.Abs(errTo) > 75f) _runup = true;   // zu eng: erst Anlauf holen
                    if (_runup && (dist > 9f || MathF.Abs(errTo) < 30f)) _runup = false;
                    if (_runup) Steer(self, fwd, 0.5f, pos, fwd, up, SightRange);
                    else Steer(self, Flat(to, up), Math.Min(Throttle, 0.6f) * Math.Clamp(dist / 6f, 0.5f, 1f), pos, fwd, up, Math.Min(SightRange, dist - 0.5f));
                }
                Phase = $"Nachputzen (Runde {_round})  ·  {_dirty.Count + 1} Stellen  ·  {dist:0} m";
            }
        }
    }
}
