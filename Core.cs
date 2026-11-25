using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

namespace Doorstop
{
    public static class Entrypoint
    {
        // Wird von Doorstop auf dem Hauptthread aufgerufen, direkt nach il2cpp_init.
        public static void Start()
        {
            try { MoonMenu.Core.Init(); }
            catch (Exception e) { MoonMenu.Log.Write("Init fehlgeschlagen: " + e); }
        }
    }
}

namespace MoonMenu
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct Vec3 { public float X, Y, Z; public override string ToString() => $"{X:0} / {Y:0} / {Z:0}"; }

    // Einstellungen: vom Menü geschrieben, auf dem Spiel-Hauptthread angewendet.
    internal static class State
    {
        public static volatile bool Fly;
        public static float FlySpeedMul = 1f;
        public static float WalkSpeedMul = 1f;
        public static float CleanMul = 1f;
        public static float WidthMul = 1f;
        public static float TimeScale = 1f;
        public static volatile bool InfiniteBoosts;

        private static readonly object MoneyGate = new object();
        private static decimal _pendingMoney;
        public static void QueueMoney(decimal amount) { lock (MoneyGate) _pendingMoney += amount; }
        public static decimal TakeMoney() { lock (MoneyGate) { var m = _pendingMoney; _pendingMoney = 0; return m; } }

        // Teleport: Slots speichern/laden (Anfragen werden im Walker-Hook erledigt)
        public static readonly Vec3?[] Slots = new Vec3?[3];
        public static volatile int SaveSlotRequest = -1;
        public static volatile int LoadSlotRequest = -1;
        public static Vec3 Position;

        // Anzeige
        public static volatile bool PlayerFound;
        public static decimal Balance;
        public static string PlayerId = "";

        public static void ResetAll()
        {
            Fly = false; FlySpeedMul = 1f; WalkSpeedMul = 1f; CleanMul = 1f; WidthMul = 1f; TimeScale = 1f; InfiniteBoosts = false;
            Vehicle.StopAll("zurückgesetzt"); Vehicle.SpeedMul = 1f;
        }
    }

    internal static unsafe class Core
    {
        public static string GameDir;

        // MoonWalker
        static int oFlying = -1, oFlySpeed = -1, oDevSpeed = -1, oBoosts = -1, oWorld = -1, oAccount = -1, oInputs = -1, oMounted = -1;
        static IntPtr mSetUIBlocked, mGetUIBlocked, mTeleport;
        // MoonDustInventory
        static int oPlayerId = -1;
        // MoonWorld
        static int oWallets = -1;
        static IntPtr mDevAddMoney, mWalletDollars;
        // MoonBrush
        static int oRemoval = -1, oWidth = -1, oEdge = -1;
        // Unity
        static IntPtr mGetTransform, mGetPosition, mCursorGetLock, mCursorSetLock, mCursorGetVisible, mCursorSetVisible, mSetTimeScale;

        static delegate* unmanaged<IntPtr, IntPtr, void> _origWalkerUpdate;
        static delegate* unmanaged<IntPtr, IntPtr, void> _origBrushLateUpdate;

        public static void Init()
        {
            GameDir = Path.GetDirectoryName(Path.GetDirectoryName(typeof(Core).Assembly.Location));
            Log.Write("---- MoonMenu startet ----  Spielordner: " + GameDir);

            var img = Il2Cpp.FindImage("Assembly-CSharp.dll");
            if (img == IntPtr.Zero) { Log.Write("Assembly-CSharp.dll nicht gefunden."); return; }

            var walker = Il2Cpp.il2cpp_class_from_name(img, "MoonCleaners", "MoonWalker");
            var inv = Il2Cpp.il2cpp_class_from_name(img, "MoonCleaners", "MoonDustInventory");
            var world = Il2Cpp.il2cpp_class_from_name(img, "MoonCleaners", "MoonWorld");
            var brush = Il2Cpp.il2cpp_class_from_name(img, "MoonCleaners", "MoonBrush");
            var crew = Il2Cpp.il2cpp_class_from_name(img, "MoonCleaners", "MoonCrewSession");
            Log.Write($"Klassen: Walker={walker != IntPtr.Zero} Inventory={inv != IntPtr.Zero} World={world != IntPtr.Zero} Brush={brush != IntPtr.Zero} Crew={crew != IntPtr.Zero}");

            if (walker != IntPtr.Zero)
            {
                oFlying = Il2Cpp.Offset(walker, "<Flying>k__BackingField");
                oFlySpeed = Il2Cpp.Offset(walker, "<FlySpeed>k__BackingField");
                oDevSpeed = Il2Cpp.Offset(walker, "<DevSpeedMultiplier>k__BackingField");
                oBoosts = Il2Cpp.Offset(walker, "<BoostsLeft>k__BackingField");
                oWorld = Il2Cpp.Offset(walker, "world");
                oAccount = Il2Cpp.Offset(walker, "upgradeAccount");
                oInputs = Il2Cpp.Offset(walker, "inputs");
                oMounted = Il2Cpp.Offset(walker, "mountedVehicle");
                mSetUIBlocked = Il2Cpp.FindMethod(walker, "set_UIBlocked", "System.Boolean");
                mGetUIBlocked = Il2Cpp.FindMethod(walker, "get_UIBlocked");
                mTeleport = Il2Cpp.FindMethod(walker, "Teleport", "UnityEngine.Vector3", "System.Boolean");
            }
            if (inv != IntPtr.Zero) oPlayerId = Il2Cpp.Offset(inv, "playerId");
            if (world != IntPtr.Zero)
            {
                oWallets = Il2Cpp.Offset(world, "wallets");
                mDevAddMoney = Il2Cpp.Method(world, "DevAddMoney", 2);
                mWalletDollars = Il2Cpp.Method(world, "WalletDollars", 1);
            }
            if (brush != IntPtr.Zero)
            {
                oRemoval = Il2Cpp.Offset(brush, "removalRate");
                oWidth = Il2Cpp.Offset(brush, "cleaningWidth");
                oEdge = Il2Cpp.Offset(brush, "cleaningEdgeWidth");
            }

            var comp = Il2Cpp.FindClass("UnityEngine.CoreModule.dll", "UnityEngine", "Component");
            var tr = Il2Cpp.FindClass("UnityEngine.CoreModule.dll", "UnityEngine", "Transform");
            var cursor = Il2Cpp.FindClass("UnityEngine.CoreModule.dll", "UnityEngine", "Cursor");
            var time = Il2Cpp.FindClass("UnityEngine.CoreModule.dll", "UnityEngine", "Time");
            mGetTransform = Il2Cpp.FindMethod(comp, "get_transform");
            mGetPosition = Il2Cpp.FindMethod(tr, "get_position");
            mCursorGetLock = Il2Cpp.FindMethod(cursor, "get_lockState");
            mCursorSetLock = Il2Cpp.FindMethod(cursor, "set_lockState", "UnityEngine.CursorLockMode");
            mCursorGetVisible = Il2Cpp.FindMethod(cursor, "get_visible");
            mCursorSetVisible = Il2Cpp.FindMethod(cursor, "set_visible", "System.Boolean");
            mSetTimeScale = Il2Cpp.FindMethod(time, "set_timeScale", "System.Single");

            Dobby.Load(GameDir);

            if (walker != IntPtr.Zero)
            {
                var target = Il2Cpp.MethodPointer(Il2Cpp.Method(walker, "Update", 0));
                if (target != IntPtr.Zero)
                    _origWalkerUpdate = (delegate* unmanaged<IntPtr, IntPtr, void>)Dobby.Hook(target, (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, void>)&WalkerUpdateHook, "MoonWalker.Update");
            }
            if (brush != IntPtr.Zero)
            {
                var target = Il2Cpp.MethodPointer(Il2Cpp.Method(brush, "LateUpdate", 0));
                if (target != IntPtr.Zero)
                    _origBrushLateUpdate = (delegate* unmanaged<IntPtr, IntPtr, void>)Dobby.Hook(target, (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, void>)&BrushLateUpdateHook, "MoonBrush.LateUpdate");
            }

            Vehicle.Init(img);

            // Ingame-Menü: in vorhandene OnGUI-Methoden des Spiels einhaken
            HookOnGui(crew, "MoonCrewSession.OnGUI", 0);
            HookOnGui(brush, "MoonBrush.OnGUI", 1);

            Menu.Start();
            Log.Write("Init fertig.");
        }

        static readonly IntPtr[] _origOnGui = new IntPtr[2];

        static void HookOnGui(IntPtr klass, string what, int slot)
        {
            if (klass == IntPtr.Zero) return;
            var target = Il2Cpp.MethodPointer(Il2Cpp.Method(klass, "OnGUI", 0));
            if (target == IntPtr.Zero) return;
            var repl = slot == 0
                ? (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, void>)&OnGuiHook0
                : (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, void>)&OnGuiHook1;
            _origOnGui[slot] = Dobby.Hook(target, repl, what);
        }

        static bool _guiError;

        static void OnGuiCommon(int slot, IntPtr self, IntPtr method)
        {
            var orig = (delegate* unmanaged<IntPtr, IntPtr, void>)_origOnGui[slot];
            if (orig != null) orig(self, method);
            try { Overlay.OnGUI(); }
            catch (Exception e) { if (!_guiError) { _guiError = true; Log.Write("Overlay: " + e); } }
        }

        [UnmanagedCallersOnly] static void OnGuiHook0(IntPtr self, IntPtr method) => OnGuiCommon(0, self, method);
        [UnmanagedCallersOnly] static void OnGuiHook1(IntPtr self, IntPtr method) => OnGuiCommon(1, self, method);

        // ---------------- Hooks (laufen auf dem Spiel-Hauptthread) ----------------

        static bool _walkerError, _brushError;

        [UnmanagedCallersOnly]
        static void WalkerUpdateHook(IntPtr self, IntPtr method)
        {
            bool local = false;
            try { local = OnWalker(self); }
            catch (Exception e) { if (!_walkerError) { _walkerError = true; Log.Write("Walker-Hook: " + e); } }
            if (_origWalkerUpdate != null) _origWalkerUpdate(self, method);
            if (local)
            {
                try { AfterWalker(self); }
                catch (Exception e) { if (!_walkerError) { _walkerError = true; Log.Write("Walker-Hook (nach): " + e); } }
            }
        }

        [UnmanagedCallersOnly]
        static void BrushLateUpdateHook(IntPtr self, IntPtr method)
        {
            try { OnBrush(self); }
            catch (Exception e) { if (!_brushError) { _brushError = true; Log.Write("Brush-Hook: " + e); } }
            if (_origBrushLateUpdate != null) _origBrushLateUpdate(self, method);
        }

        // ---------------- Spieler ----------------

        static float _baseFlySpeed = -1f;
        static bool _wroteFly, _wroteSpeed;
        static float _appliedTimeScale = 1f;
        static int _nextBalance, _nextPos;
        static int _frame;

        static T Read<T>(IntPtr obj, int off) where T : unmanaged => *(T*)(obj + off);
        static void Write<T>(IntPtr obj, int off, T v) where T : unmanaged => *(T*)(obj + off) = v;

        static int UnboxInt(IntPtr boxed) => boxed == IntPtr.Zero ? 0 : *(int*)Il2Cpp.il2cpp_object_unbox(boxed);
        static bool UnboxBool(IntPtr boxed) => boxed != IntPtr.Zero && *(byte*)Il2Cpp.il2cpp_object_unbox(boxed) != 0;

        static bool OnWalker(IntPtr self)
        {
            // Nur der lokale Spieler hat ein Input-Asset
            if (oInputs >= 0 && Read<IntPtr>(self, oInputs) == IntPtr.Zero) return false;
            State.PlayerFound = true;
            LocalWalker = self;
            _frame++;

            var mounted = oMounted >= 0 ? Read<IntPtr>(self, oMounted) : IntPtr.Zero;
            Vehicle.SetMine(mounted);

            if (oFlying >= 0)
            {
                if (State.Fly) { Write<byte>(self, oFlying, 1); _wroteFly = true; }
                else if (_wroteFly) { Write<byte>(self, oFlying, 0); _wroteFly = false; }
            }

            if (oFlySpeed >= 0)
            {
                if (_baseFlySpeed < 0f) { _baseFlySpeed = Read<float>(self, oFlySpeed); Log.Write($"Fly-Speed Standard: {_baseFlySpeed}"); }
                if (_baseFlySpeed > 0f) Write(self, oFlySpeed, _baseFlySpeed * State.FlySpeedMul);
            }

            if (oDevSpeed >= 0)
            {
                if (State.WalkSpeedMul != 1f) { Write(self, oDevSpeed, State.WalkSpeedMul); _wroteSpeed = true; }
                else if (_wroteSpeed) { Write(self, oDevSpeed, 1f); _wroteSpeed = false; }
            }

            if (oBoosts >= 0 && State.InfiniteBoosts && Read<int>(self, oBoosts) < 3)
                Write(self, oBoosts, 3);

            if (State.TimeScale != _appliedTimeScale && mSetTimeScale != IntPtr.Zero)
            {
                float ts = State.TimeScale;
                IntPtr* a = stackalloc IntPtr[1];
                a[0] = (IntPtr)(&ts);
                Il2Cpp.Invoke(mSetTimeScale, IntPtr.Zero, a, "timeScale");
                _appliedTimeScale = ts;
            }

            DoTeleport(self);

            // Geld
            var world = oWorld >= 0 ? Read<IntPtr>(self, oWorld) : IntPtr.Zero;
            var account = oAccount >= 0 ? Read<IntPtr>(self, oAccount) : IntPtr.Zero;
            var id = account != IntPtr.Zero && oPlayerId >= 0 ? Read<IntPtr>(account, oPlayerId) : IntPtr.Zero;
            if (world == IntPtr.Zero || id == IntPtr.Zero) return true;

            if ((_frame & 15) == 0) State.PlayerId = Il2Cpp.ReadString(id) ?? "";

            var add = State.TakeMoney();
            if (add != 0m) AddMoney(world, id, add);

            int now = Environment.TickCount;
            if (now - _nextBalance >= 0)
            {
                _nextBalance = now + 500;
                if (TryWallet(world, id, out var bal)) State.Balance = bal;
            }
            return true;
        }

        // ---------------- Menü offen: Cursor frei, Spielsteuerung gesperrt ----------------

        static bool _menuApplied, _savedVisible, _savedBlocked;
        static int _savedLock;

        static void AfterWalker(IntPtr self)
        {
            if (Menu.Open)
            {
                if (!_menuApplied)
                {
                    _savedLock = UnboxInt(Il2Cpp.Invoke(mCursorGetLock, IntPtr.Zero, null, "Cursor.lockState"));
                    _savedVisible = UnboxBool(Il2Cpp.Invoke(mCursorGetVisible, IntPtr.Zero, null, "Cursor.visible"));
                    _savedBlocked = mGetUIBlocked != IntPtr.Zero && UnboxBool(Il2Cpp.Invoke(mGetUIBlocked, self, null, "UIBlocked"));
                    _menuApplied = true;
                }
                SetBlocked(self, true);
                SetCursor(0, true);
            }
            else if (_menuApplied)
            {
                SetBlocked(self, _savedBlocked);
                SetCursor(_savedLock, _savedVisible);
                _menuApplied = false;
            }
        }

        public static IntPtr LocalWalker;
        public static bool MenuBlocking => _menuApplied;

        // Fahrzeug, in dem der lokale Spieler gerade sitzt (direkt aus dem Walker gelesen)
        public static IntPtr MountedNow => LocalWalker != IntPtr.Zero && oMounted >= 0 ? Read<IntPtr>(LocalWalker, oMounted) : IntPtr.Zero;

        // Vom Fahrzeug-Hook: Sperre für einen Physik-Schritt lösen bzw. wieder setzen
        public static void SetLocalBlocked(bool v)
        {
            if (LocalWalker != IntPtr.Zero) SetBlocked(LocalWalker, v);
        }

        static void SetBlocked(IntPtr self, bool v)
        {
            if (mSetUIBlocked == IntPtr.Zero) return;
            byte b = (byte)(v ? 1 : 0);
            IntPtr* a = stackalloc IntPtr[1];
            a[0] = (IntPtr)(&b);
            Il2Cpp.Invoke(mSetUIBlocked, self, a, "set_UIBlocked");
        }

        static void SetCursor(int lockMode, bool visible)
        {
            IntPtr* a = stackalloc IntPtr[1];
            if (mCursorSetLock != IntPtr.Zero) { int l = lockMode; a[0] = (IntPtr)(&l); Il2Cpp.Invoke(mCursorSetLock, IntPtr.Zero, a, "Cursor.lockState"); }
            if (mCursorSetVisible != IntPtr.Zero) { byte v = (byte)(visible ? 1 : 0); a[0] = (IntPtr)(&v); Il2Cpp.Invoke(mCursorSetVisible, IntPtr.Zero, a, "Cursor.visible"); }
        }

        // ---------------- Teleport ----------------

        static bool TryPosition(IntPtr self, out Vec3 pos)
        {
            pos = default;
            if (mGetTransform == IntPtr.Zero || mGetPosition == IntPtr.Zero) return false;
            var t = Il2Cpp.Invoke(mGetTransform, self, null, "transform");
            if (t == IntPtr.Zero) return false;
            var boxed = Il2Cpp.Invoke(mGetPosition, t, null, "position");
            if (boxed == IntPtr.Zero) return false;
            pos = *(Vec3*)Il2Cpp.il2cpp_object_unbox(boxed);
            return true;
        }

        static void DoTeleport(IntPtr self)
        {
            int now = Environment.TickCount;
            if (now - _nextPos >= 0)
            {
                _nextPos = now + 250;
                if (TryPosition(self, out var p)) State.Position = p;
            }

            int save = State.SaveSlotRequest;
            if (save >= 0)
            {
                State.SaveSlotRequest = -1;
                if (TryPosition(self, out var p)) { State.Slots[save] = p; Log.Write($"Slot {save + 1} gespeichert: {p}"); }
            }

            int load = State.LoadSlotRequest;
            if (load >= 0)
            {
                State.LoadSlotRequest = -1;
                var slot = State.Slots[load];
                if (slot.HasValue && mTeleport != IntPtr.Zero)
                {
                    var p = slot.Value;
                    byte flag = 0;
                    IntPtr* a = stackalloc IntPtr[2];
                    a[0] = (IntPtr)(&p);
                    a[1] = (IntPtr)(&flag);
                    Il2Cpp.Invoke(mTeleport, self, a, "Teleport");
                    Log.Write($"Teleport zu Slot {load + 1}: {p}");
                }
            }
        }

        // ---------------- Geld ----------------

        static bool TryWallet(IntPtr world, IntPtr id, out decimal value)
        {
            value = 0m;
            if (mWalletDollars == IntPtr.Zero) return false;
            IntPtr* args = stackalloc IntPtr[1];
            args[0] = id;
            var boxed = Il2Cpp.Invoke(mWalletDollars, world, args, "WalletDollars");
            if (boxed == IntPtr.Zero) return false;
            value = Il2Cpp.ReadDecimal(Il2Cpp.il2cpp_object_unbox(boxed));
            return true;
        }

        static void AddMoney(IntPtr world, IntPtr id, decimal amount)
        {
            TryWallet(world, id, out var before);

            if (mDevAddMoney != IntPtr.Zero)
            {
                byte* dec = stackalloc byte[16];
                Il2Cpp.WriteDecimal((IntPtr)dec, amount);
                IntPtr* args = stackalloc IntPtr[2];
                args[0] = id;
                args[1] = (IntPtr)dec;
                Il2Cpp.Invoke(mDevAddMoney, world, args, "DevAddMoney");
            }

            TryWallet(world, id, out var after);
            if (after != before) { Log.Write($"Geld +{amount} über DevAddMoney: {before} -> {after}"); return; }

            // Fallback: Wallet direkt setzen (TeamWallet.set_Item)
            var wallets = oWallets >= 0 ? Read<IntPtr>(world, oWallets) : IntPtr.Zero;
            if (wallets == IntPtr.Zero) { Log.Write("Geld: kein Wallet-Objekt."); return; }
            var setItem = Il2Cpp.Method(Il2Cpp.il2cpp_object_get_class(wallets), "set_Item", 2);
            if (setItem == IntPtr.Zero) return;
            byte* nv = stackalloc byte[16];
            Il2Cpp.WriteDecimal((IntPtr)nv, before + amount);
            IntPtr* a2 = stackalloc IntPtr[2];
            a2[0] = id;
            a2[1] = (IntPtr)nv;
            Il2Cpp.Invoke(setItem, wallets, a2, "TeamWallet.set_Item");
            TryWallet(world, id, out after);
            Log.Write($"Geld +{amount} über Wallet: {before} -> {after}");
        }

        // ---------------- Bürste ----------------

        struct BrushVals { public float Removal, Width, Edge, WRemoval, WWidth, WEdge; }
        static readonly Dictionary<IntPtr, BrushVals> Brushes = new Dictionary<IntPtr, BrushVals>();

        static void OnBrush(IntPtr self)
        {
            if (oRemoval < 0) return;
            float r = Read<float>(self, oRemoval);
            float w = oWidth >= 0 ? Read<float>(self, oWidth) : 0f;
            float e = oEdge >= 0 ? Read<float>(self, oEdge) : 0f;

            if (!Brushes.TryGetValue(self, out var v))
                v = new BrushVals { Removal = r, Width = w, Edge = e, WRemoval = r, WWidth = w, WEdge = e };

            // Hat das Spiel selbst neue Werte gesetzt (Upgrade)? Dann als neue Basis übernehmen.
            if (r != v.WRemoval) v.Removal = r;
            if (w != v.WWidth) v.Width = w;
            if (e != v.WEdge) v.Edge = e;

            v.WRemoval = v.Removal * State.CleanMul;
            v.WWidth = v.Width * State.WidthMul;
            v.WEdge = v.Edge * State.WidthMul;
            Write(self, oRemoval, v.WRemoval);
            if (oWidth >= 0) Write(self, oWidth, v.WWidth);
            if (oEdge >= 0) Write(self, oEdge, v.WEdge);
            Brushes[self] = v;
        }
    }
}
