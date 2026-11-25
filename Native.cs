using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace MoonMenu
{
    internal static class Log
    {
        private static readonly object Gate = new object();
        private static string _path;
        public static string Last = "";

        public static void Write(string msg)
        {
            Last = msg;
            try
            {
                lock (Gate)
                {
                    _path ??= Path.Combine(Path.GetDirectoryName(typeof(Log).Assembly.Location) ?? ".", "MoonMenu.log");
                    File.AppendAllText(_path, $"[{DateTime.Now:HH:mm:ss}] {msg}{Environment.NewLine}");
                }
            }
            catch { }
        }
    }

    // Exportierte il2cpp-C-API (stabil über Unity-Versionen, keine Struct-Layouts nötig)
    internal static unsafe class Il2Cpp
    {
        private const string Lib = "GameAssembly";

        [DllImport(Lib)] public static extern IntPtr il2cpp_domain_get();
        [DllImport(Lib)] public static extern IntPtr* il2cpp_domain_get_assemblies(IntPtr domain, out UIntPtr size);
        [DllImport(Lib)] public static extern IntPtr il2cpp_assembly_get_image(IntPtr assembly);
        [DllImport(Lib)] public static extern IntPtr il2cpp_image_get_name(IntPtr image);
        [DllImport(Lib)] public static extern IntPtr il2cpp_class_from_name(IntPtr image, [MarshalAs(UnmanagedType.LPUTF8Str)] string ns, [MarshalAs(UnmanagedType.LPUTF8Str)] string name);
        [DllImport(Lib)] public static extern IntPtr il2cpp_class_get_field_from_name(IntPtr klass, [MarshalAs(UnmanagedType.LPUTF8Str)] string name);
        [DllImport(Lib)] public static extern UIntPtr il2cpp_field_get_offset(IntPtr field);
        [DllImport(Lib)] public static extern IntPtr il2cpp_class_get_method_from_name(IntPtr klass, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, int argsCount);
        [DllImport(Lib)] public static extern IntPtr il2cpp_runtime_invoke(IntPtr method, IntPtr obj, IntPtr* args, out IntPtr exc);
        [DllImport(Lib)] public static extern IntPtr il2cpp_object_unbox(IntPtr obj);
        [DllImport(Lib)] public static extern IntPtr il2cpp_object_get_class(IntPtr obj);
        [DllImport(Lib)] public static extern void il2cpp_format_exception(IntPtr exc, byte* buffer, int size);
        [DllImport(Lib)] public static extern IntPtr il2cpp_class_get_methods(IntPtr klass, ref IntPtr iter);
        [DllImport(Lib)] public static extern IntPtr il2cpp_method_get_name(IntPtr method);
        [DllImport(Lib)] public static extern uint il2cpp_method_get_param_count(IntPtr method);
        [DllImport(Lib)] public static extern IntPtr il2cpp_method_get_param(IntPtr method, uint index);
        [DllImport(Lib)] public static extern IntPtr il2cpp_type_get_name(IntPtr type);
        [DllImport(Lib)] public static extern IntPtr il2cpp_object_new(IntPtr klass);
        [DllImport(Lib)] public static extern IntPtr il2cpp_string_new([MarshalAs(UnmanagedType.LPUTF8Str)] string str);
        [DllImport(Lib)] public static extern uint il2cpp_gchandle_new(IntPtr obj, bool pinned);
        [DllImport(Lib)] public static extern void il2cpp_field_static_get_value(IntPtr field, void* value);
        [DllImport(Lib)] public static extern IntPtr il2cpp_array_new(IntPtr elementClass, UIntPtr length);
        [DllImport(Lib)] public static extern int il2cpp_class_value_size(IntPtr klass, out uint align);

        public static IntPtr FindClass(string image, string ns, string name)
        {
            var img = FindImage(image);
            if (img == IntPtr.Zero) { Log.Write($"Image fehlt: {image}"); return IntPtr.Zero; }
            var k = il2cpp_class_from_name(img, ns, name);
            if (k == IntPtr.Zero) Log.Write($"Klasse fehlt: {ns}.{name}");
            return k;
        }

        // Methode per Name + exakten Parametertypen (für Überladungen)
        public static IntPtr FindMethod(IntPtr klass, string name, params string[] paramTypes)
        {
            if (klass == IntPtr.Zero) return IntPtr.Zero;
            IntPtr iter = IntPtr.Zero, m;
            while ((m = il2cpp_class_get_methods(klass, ref iter)) != IntPtr.Zero)
            {
                if (Marshal.PtrToStringUTF8(il2cpp_method_get_name(m)) != name) continue;
                if (il2cpp_method_get_param_count(m) != paramTypes.Length) continue;
                bool ok = true;
                for (uint i = 0; i < paramTypes.Length && ok; i++)
                    ok = Marshal.PtrToStringUTF8(il2cpp_type_get_name(il2cpp_method_get_param(m, i))) == paramTypes[i];
                if (ok) return m;
            }
            Log.Write($"Methode fehlt: {name}({string.Join(", ", paramTypes)})");
            return IntPtr.Zero;
        }

        public static IntPtr FindImage(string name)
        {
            var asms = il2cpp_domain_get_assemblies(il2cpp_domain_get(), out var size);
            for (ulong i = 0; i < size.ToUInt64(); i++)
            {
                var img = il2cpp_assembly_get_image(asms[i]);
                if (Marshal.PtrToStringUTF8(il2cpp_image_get_name(img)) == name) return img;
            }
            return IntPtr.Zero;
        }

        public static int Offset(IntPtr klass, string field)
        {
            var f = il2cpp_class_get_field_from_name(klass, field);
            if (f == IntPtr.Zero) { Log.Write($"Feld fehlt: {field}"); return -1; }
            return (int)il2cpp_field_get_offset(f).ToUInt64();
        }

        public static IntPtr Method(IntPtr klass, string name, int args)
        {
            var m = il2cpp_class_get_method_from_name(klass, name, args);
            if (m == IntPtr.Zero) Log.Write($"Methode fehlt: {name}/{args}");
            return m;
        }

        // MethodInfo->methodPointer ist in allen il2cpp-Versionen das erste Feld
        public static IntPtr MethodPointer(IntPtr methodInfo) => methodInfo == IntPtr.Zero ? IntPtr.Zero : *(IntPtr*)methodInfo;

        public static IntPtr Invoke(IntPtr method, IntPtr obj, IntPtr* args, string what)
        {
            var r = il2cpp_runtime_invoke(method, obj, args, out var exc);
            if (exc != IntPtr.Zero)
            {
                var buf = stackalloc byte[2048];
                try { il2cpp_format_exception(exc, buf, 2048); Log.Write($"{what}: {Marshal.PtrToStringUTF8((IntPtr)buf)}"); }
                catch { Log.Write($"{what}: il2cpp-Exception"); }
                return IntPtr.Zero;
            }
            return r;
        }

        public static string ReadString(IntPtr str)
        {
            if (str == IntPtr.Zero) return null;
            int len = *(int*)(str + 0x10);
            return new string((char*)(str + 0x14), 0, len);
        }

        // System.Decimal in il2cpp (Mono-Corlib): flags, hi, lo, mid
        public static decimal ReadDecimal(IntPtr p)
        {
            int* d = (int*)p;
            return new decimal(new[] { d[2], d[3], d[1], d[0] });
        }

        public static void WriteDecimal(IntPtr p, decimal value)
        {
            var b = decimal.GetBits(value); // lo, mid, hi, flags
            int* d = (int*)p;
            d[0] = b[3]; d[1] = b[2]; d[2] = b[0]; d[3] = b[1];
        }
    }

    internal static unsafe class Win
    {
        [DllImport("kernel32")] public static extern bool AllocConsole();
        [DllImport("kernel32")] public static extern IntPtr GetConsoleWindow();
        [DllImport("kernel32")] public static extern bool SetConsoleTitleW([MarshalAs(UnmanagedType.LPWStr)] string title);
        [DllImport("user32")] public static extern bool ShowWindow(IntPtr hwnd, int cmd);
        [DllImport("user32")] public static extern short GetAsyncKeyState(int vk);
        [DllImport("user32")] public static extern IntPtr GetForegroundWindow();
        [DllImport("user32")] public static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
        [DllImport("kernel32")] public static extern IntPtr GetModuleHandleW([MarshalAs(UnmanagedType.LPWStr)] string name);

        [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
        [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
        [DllImport("user32")] public static extern bool GetCursorPos(out POINT p);
        [DllImport("user32")] public static extern bool ScreenToClient(IntPtr hwnd, ref POINT p);
        [DllImport("user32")] public static extern bool GetClientRect(IntPtr hwnd, out RECT r);
    }

    internal static unsafe class Dobby
    {
        private static delegate* unmanaged<IntPtr, IntPtr, IntPtr*, int> _hook;

        // dobby.dll liegt im Release neben MoonMenu.dll; ältere Installationen haben sie unter BepInEx\core.
        public static void Load(string gameDir)
        {
            var own = Path.Combine(Path.GetDirectoryName(typeof(Dobby).Assembly.Location) ?? gameDir, "dobby.dll");
            var path = File.Exists(own) ? own : Path.Combine(gameDir, "BepInEx", "core", "dobby.dll");
            Log.Write("Dobby: " + path);
            var h = NativeLibrary.Load(path);
            _hook = (delegate* unmanaged<IntPtr, IntPtr, IntPtr*, int>)NativeLibrary.GetExport(h, "DobbyHook");
        }

        public static IntPtr Hook(IntPtr target, IntPtr replacement, string what)
        {
            IntPtr orig;
            int rc = _hook(target, replacement, &orig);
            Log.Write($"Hook {what} @0x{target.ToInt64():X} rc={rc}");
            return rc == 0 ? orig : IntPtr.Zero;
        }
    }
}
