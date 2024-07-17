using AsmResolver.PE.DotNet.Metadata.Tables.Rows;
using Mono.Cecil.Cil;
using MonoMod.Cil;
using MonoMod.Core.Platforms;
using MonoMod.RuntimeDetour;
using MonoMod.Utils;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
using System.Text;

namespace Celeste.Mod.aaa;

public static class MethodInfoExtensions
{
    public static bool IsFromCeleste(this MethodBase method)
    {
        if (method?.Module == typeof(Celeste).Module)
        {
            return true;
        }
        return false;
    }
    //https://stackoverflow.com/questions/1312166/print-full-signature-of-a-method-from-a-methodinfo
    /// <summary>
    /// Return the method signature as a string.
    /// </summary>
    /// <param name="method">The Method</param>
    /// <param name="callable">Return as an callable string(public void a(string b) would return a(b))</param>
    /// <returns>Method signature</returns>
    public static string GetSignature(this MethodBase method, bool callable = false, bool source = true, bool celeste = true)
    {
        var firstParam = true;
        var sigBuilder = new StringBuilder();

        sigBuilder.Append(TypeName(method.DeclaringType));
        sigBuilder.Append('.');
        sigBuilder.Append(method.Name);

        // Add method generics
        if (method.IsGenericMethod)
        {
            sigBuilder.Append('<');
            foreach (var g in method.GetGenericArguments())
            {
                if (firstParam)
                {
                    firstParam = false;
                }
                else
                {
                    sigBuilder.Append(", ");
                }

                sigBuilder.Append(TypeName(g));
            }
            sigBuilder.Append('>');
        }

        if (source)
        {
            if ((!method.IsFromCeleste()) || celeste)
            {
                sigBuilder.Append(" @")
                //.Append(method.Module.Assembly.GetName().Name)
                .Append(method.Module.ScopeName)
                ;
            }
        }
        return sigBuilder.ToString();
    }
    //https://stackoverflow.com/questions/1312166/print-full-signature-of-a-method-from-a-methodinfo
    /// <summary>
    /// Get full type name with full namespace names
    /// </summary>
    /// <param name="type">Type. May be generic or nullable</param>
    /// <returns>Full type name, fully qualified namespaces</returns>
    public static string TypeName(Type type)
    {
        var nullableType = Nullable.GetUnderlyingType(type);
        if (nullableType != null)
        {
            return nullableType.Name + "?";
        }

        if (!(type.IsGenericType && type.Name.Contains('`')))
        {
            switch (type.Name)
            {
                case "String": return "string";
                case "Int32": return "int";
                case "Decimal": return "decimal";
                case "Object": return "object";
                case "Void": return "void";
                default:
                    {
                        return string.IsNullOrWhiteSpace(type.FullName) ? type.Name : type.FullName;
                    }
            }
        }

        var sb = new StringBuilder(type.Name[..type.Name.IndexOf('`')]
        );
        sb.Append('<');
        var first = true;
        foreach (var t in type.GetGenericArguments())
        {
            if (!first)
            {
                sb.Append(',');
            }

            sb.Append(TypeName(t));
            first = false;
        }
        sb.Append('>');
        return sb.ToString();
    }

    public static string ToOperation(this MethodBase method)
    {
        return $"{method.Name}({new string(
                        method
                        .GetParameters()
                        .SelectMany(x =>
                            x.ParameterType.Name.Append(' ')
                            .Concat((x.Name ?? "Unnamed").Append(',')))
                        .SkipLast(1)
                        .ToArray())})";
    }

}

public class aaaModule : EverestModule
{

    public static aaaModule Instance { get; private set; }

    public override Type SettingsType => typeof(aaaModuleSettings);
    public static aaaModuleSettings Settings => (aaaModuleSettings)Instance._Settings;

    public override Type SessionType => typeof(aaaModuleSession);
    public static aaaModuleSession Session => (aaaModuleSession)Instance._Session;

    public override Type SaveDataType => typeof(aaaModuleSaveData);
    public static aaaModuleSaveData SaveData => (aaaModuleSaveData)Instance._SaveData;

    public aaaModule()
    {
        Instance = this;
#if DEBUG
        // debug builds use verbose logging
        Logger.SetLogLevel(nameof(aaaModule), LogLevel.Verbose);
#else
        // release builds use info logging to reduce spam in log files
        Logger.SetLogLevel(nameof(aaaModule), LogLevel.Info);
#endif
    }
    static int Bottom = 0;
    const BindingFlags bf = 0
        | BindingFlags.Public
        | BindingFlags.NonPublic
        | BindingFlags.Instance
        | BindingFlags.Static;
    static MethodInfo ReflRefreshHooks = typeof(aaaModule).GetMethod(nameof(RefreshHooks), bf);
    static MethodInfo ReflGetOrig = typeof(aaaModule).GetMethod(nameof(GetOrig), bf);
    static void AddLogger(ILContext il, string operation)
    {
        ILCursor ic = new(il);
        void localmethod()
        {
            Bottom++;
            if (Bottom != 1)
            {
                return;
            }
            StackTrace stackTrace = new();
            List<LogInfo> infos = [];
            for (int i = 3; i < stackTrace.FrameCount; i++)
            {
                var frame = stackTrace.GetFrame(i)!;
                if (frame.GetMethod() == ReflRefreshHooks || frame.GetMethod() == ReflGetOrig)
                {
                    break;
                }

                infos.Add(new LogInfo()
                {
                    ILOffset = frame.GetILOffset(),
                    Method = frame.GetMethod()!,
                    File = frame.GetFileName()!,
                    FileRow = frame.GetFileLineNumber(),
                    FileCol = frame.GetFileColumnNumber(),
                });
                if (frame.GetMethod() == Current.Manip?.Method)
                {
                    break;
                }
            }
            var tar = infos[0];
            foreach (var info in infos.Skip(1))
            {
                tar.ILHookStackTraceNextFrame = info;
                tar = info;
            }
            var head = new LogHead() { Operation = operation, BeforeTrim = infos[0], CallStack = infos[0] };
            if (CurrentManipLogs.Count == 0 || !CurrentManipLogs[^1].OrigSame(head))
            {
                CurrentManipLogs.Add(head.FixGeneric());
            }
        };
#pragma warning disable CL0002 // Instance method passed to EmitDelegate
        ic.EmitDelegate(localmethod);
#pragma warning restore CL0002 // Instance method passed to EmitDelegate

        while (ic.TryGotoNext(MoveType.AfterLabel, i => i.MatchRet()))
        {
            static void reset() => Bottom--;
            ic.EmitDelegate(reset);
            ic.Index++;
        }

    }
    class LogHead
    {
        static Dictionary<MethodBase, string> trimDir = [];
        public required string Operation;
        public required LogInfo CallStack;
        public required LogInfo BeforeTrim;

        public static bool operator ==(LogHead l, LogHead r)
        {
            return l.Operation == r.Operation &&
                l.CallStack == r.CallStack;
        }
        static Module MonoModUtilsModule = typeof(ILCursor).Module;
        public bool OrigSame(LogHead r)
        {
            return Operation == r.Operation &&
                BeforeTrim == r.BeforeTrim;
        }
        public LogHead FixGeneric()
        {
            string GetTrimResult(MethodBase method)
            {
                if (trimDir.TryGetValue(method, out var ret))
                {
                    return ret;
                }
                return trimDir[method] = method.ToOperation();
            }

            while (CallStack?.Method.Module == MonoModUtilsModule)
            {
                Operation = GetTrimResult(CallStack.Method);
                CallStack = CallStack.ILHookStackTraceNextFrame!;
            }
            return this;
        }
        public static bool operator !=(LogHead l, LogHead r) => !(l == r);
        public override bool Equals(object? obj)
        {
            if (obj is LogHead info)
            {
                return info == this;
            }
            return base.Equals(obj);
        }

        public override int GetHashCode()
        {
            return CallStack switch
            {
                null => HashCode.Combine(Operation),
                _ => HashCode.Combine(Operation, CallStack),
            };
        }
        public bool HasChanges()
        {
            return Operation.StartsWith("_Insert(") || Operation.StartsWith("Emit") || Operation.StartsWith("Remove");
        }
    }
    class LogInfo
    {
        public required int ILOffset;
        public required MethodBase Method;
        public string? File;
        public int FileRow;
        public int FileCol;
        public LogInfo? ILHookStackTraceNextFrame;
        public static bool operator ==(LogInfo? l, LogInfo? r)
        {
            return (l is null, r is null) switch
            {
                (false, false) => l.Method == r.Method && l.ILOffset == r.ILOffset &&
                l.ILHookStackTraceNextFrame == r.ILHookStackTraceNextFrame,
                (true, true) => true,
                _ => false,
            };
        }
        public static bool operator !=(LogInfo l, LogInfo r) => !(l == r);

        public override bool Equals(object? obj)
        {
            if (obj is LogInfo info)
            {
                return info == this;
            }
            return base.Equals(obj);
        }

        public override int GetHashCode()
        {
            return ILHookStackTraceNextFrame switch
            {
                null => HashCode.Combine(Method, ILOffset),
                _ => HashCode.Combine(Method, ILOffset, ILHookStackTraceNextFrame),
            };
        }
    }
    struct ManipInfo
    {
        public ILContext.Manipulator Manip;
        public MethodBase Target;
        public override readonly int GetHashCode()
        {
            return HashCode.Combine(Manip, Target);
        }
        public override readonly bool Equals([NotNullWhen(true)] object? obj)
        {
            if (obj is ManipInfo manip)
            {
                return (Manip, Target) == (manip.Manip, manip.Target);
            }
            return base.Equals(obj);
        }
    }
    static ManipInfo Current;
    static List<LogHead> CurrentManipLogs = [];

    static Dictionary<ManipInfo, List<LogHead>> LatestManipLogs = [];
    static Dictionary<ManipInfo, List<LogHead>> OrigManipLogs = [];

    static Dictionary<ManipInfo, (List<LogHead> now, List<LogHead> orig)> Difference = [];
    static List<string> DifferenceToString = [];

    static List<ILHook> ills = [];
    static List<Hook> ls = [];
    public static void GetOrig()
    {
        var latest = LatestManipLogs;
        LatestManipLogs = [];
        foreach (var (manip, log) in latest)
        {
            using var dmd = new DynamicMethodDefinition(manip.Target);
            ILContext il = new(dmd.Definition);
            il.Invoke(manip.Manip);
        }
        OrigManipLogs = LatestManipLogs;
        LatestManipLogs = latest;
    }
    [Monocle.Command("GetHookCollision", "")]
    public static void GetCollision()
    {
        Prepare();
        //GetDMD();
        Debugger.Launch();
        RefreshHooks();
        IgnoreSelf();
        GetOrig();
        Compare();
        IsEmpty();
        Interop();
        Clear();
    }
    static Dictionary<Module, Dictionary<ManipInfo, List<LogHead>>> Mod = [];
    static string ModReport;

    private static void Interop()
    {
        Dictionary<ManipInfo, List<LogHead>> empty = [];
        foreach (var (manip, log) in LatestManipLogs)
        {
            if (!manip.Target.IsFromCeleste())
            {
                empty.Add(manip, log);
            }
        }
        (Mod, ModReport) = BuildReport(empty);
    }

    static Dictionary<Module, Dictionary<ManipInfo, List<LogHead>>> Empty = [];
    static string EmptyReport;
    private static void IsEmpty()
    {
        Dictionary<ManipInfo, List<LogHead>> empty = [];
        foreach (var (manip, log) in LatestManipLogs)
        {
            if (!log.Any(x => x.HasChanges()))
            {
                empty.Add(manip, log);
            }
        }
        (Empty, EmptyReport) = BuildReport(empty);
    }

    private static (Dictionary<Module, Dictionary<ManipInfo, List<LogHead>>>, string) BuildReport(Dictionary<ManipInfo, List<LogHead>> empty)
    {
        var Empty = empty.GroupBy(x => x.Key.Manip.Method.Module, x => (x.Key, x.Value)).ToDictionary(x => x.Key, x => x.ToDictionary(x => x.Key, x => x.Value));
        StringBuilder sb = new();
        foreach (var (mod, dir) in Empty.OrderBy(x => x.Key.ScopeName))
        {
            sb.AppendLine(mod.ScopeName);
            foreach (var (tar, man) in dir.Keys.Select(manip =>
                (tar: manip.Target.GetSignature(source: true, celeste: false),
                man: manip.Manip.Method.GetSignature(source: false))
            ).OrderBy(b => b.man))
            {
                sb.Append('\t')
                    .Append(man)
                    .Append(" => ")
                    .Append(tar)
                    ;
                sb.AppendLine();
            }
        }
        return (Empty, sb.ToString());
    }

    //private static void GetDMD()
    //{
    //    Hook i = new(typeof(DynamicMethodDefinition).GetMethod("Generate", bf, [])!,
    //    (Func<DynamicMethodDefinition, MethodInfo> orig, DynamicMethodDefinition self) =>
    //    {
    //        var t = orig(self);
    //        _InsertDMD ??= orig(self);
    //        return t;
    //    });
    //    ills.Add(new(typeof(ILCursor).GetMethod("_Insert", bf), i => { }));
    //    i.Dispose();
    //}

    private static void IgnoreSelf()
    {
        var toremove = LatestManipLogs.Where(x =>
        {
            var t = x.Key.Manip.Method.DeclaringType.Module;
            if (t == typeof(aaaModule).Module)
            {
                return true;
            }
            return false;
        });
        foreach (var item in toremove)
        {
            LatestManipLogs.Remove(item.Key);
        }
    }

    private static void Compare()
    {
        Difference.Clear();
        DifferenceToString.Clear();
        foreach (var (manip, now, orig) in
            LatestManipLogs.Select(z => (z.Key, z.Value, OrigManipLogs[z.Key])))
        {
            if (!now.SequenceEqual(orig))
            {
                Difference[manip] = (now, orig);
            }
        }
        foreach (var (manip, t) in Difference)
        {
            DifferenceToString.Add(manip.Manip.Method.GetSignature());
        }
    }

    private static Lazy<FieldInfo> DetourManager_detourStates = new(() => typeof(DetourManager).GetField("detourStates", BindingFlags.Static | BindingFlags.NonPublic)!);
    static Func<object, object>? fun1;
    static Func<object, ILContext.Manipulator>? fun2;
    private static void RefreshHooks()
    {
        // Replay all hooks.
        //// refresh doesnot exists.

        foreach (MethodBase orig in ((IDictionary)DetourManager_detourStates.Value.GetValue(null)!).Keys)
        {
            //https://github.com/JaThePlayer/CelesteMappingUtils/blob/main/Helpers/MethodDiff.cs#L35

            var hooked = DetourManager.GetDetourInfo(orig).ILHooks;
            //AppliedHookNames = hooked.Select(h => h.ManipulatorMethod.GetID(simple: true)).ToList();

            using var cloneDef = new DynamicMethodDefinition(orig);
            using var cloneDefContext = new ILContext(cloneDef.Definition);

            //_Instructions = cloneDef.Definition.Body.Instructions.Select(instr => new Instr(ElementType.Unchanged, instr, null, new(0))).ToList();

            if (hooked is not { })
            {
                return;
            }

            foreach (var hook in hooked)
            {
                // ILHookInfo only gives us public access to the method the manipulator delegate calls.
                // We need to retrieve the actual delegate passed to the original IL hook, as the manipulator method it calls may be non-static...
                // Time to use monomod to access monomod internals :)
                fun1 ??= ReflectionHelper.GetGetter<object>(hook.GetType(), "hook");
                var hookState = fun1(hook);
                fun2 ??= ReflectionHelper.GetGetter<ILContext.Manipulator>(hookState.GetType(), "Manip");
                var manipulator = fun2(hookState);

                try
                {
                    cloneDefContext.Invoke(manipulator);

                    var instrs = cloneDefContext.Instrs;

                }
                catch (Exception ex)
                {
                    Logger.Log(LogLevel.Error,"USSRNAME.Stolen.MappingUtils.ILHookDiffer", $"Failed to apply IL hook {hook.ManipulatorMethod.GetID()}: {ex}");
                }
            }
        }
    }
    public static void Prepare()
    {
        foreach (var method in typeof(ILCursor).GetMethods(bf).Where(x => x.DeclaringType == typeof(ILCursor)))
        {
            try
            {
                ills.Add(new(method, i => AddLogger(i, method.ToOperation())));
            }
            catch (Exception)
            {
                // Generic Method
            }
        }
        ls.Add(new(typeof(DynamicMethodDefinition).GetConstructor([typeof(MethodBase)])!,
            (Action<DynamicMethodDefinition, MethodBase> orig, DynamicMethodDefinition self, MethodBase mb) =>
        {
            orig(self, mb);
            Current.Target = mb;
        }));

        ls.Add(new(typeof(ILContext).GetMethod("Invoke")!,
            (Action<ILContext, ILContext.Manipulator> orig, ILContext self, ILContext.Manipulator def) =>
        {
            CurrentManipLogs.Clear();
            Current.Manip = def;
            orig(self, def);
            LatestManipLogs[Current] = CurrentManipLogs;
            CurrentManipLogs = [];
        }));
    }

    public static void Clear()
    {
        foreach (var m in ls)
        {
            m.Dispose();
        }
        foreach (var m in ills)
        {
            m.Dispose();
        }
        ls.Clear();
        ills.Clear();
    }

    public override void Load()
    {
        foreach (var method in typeof(ILCursor).GetMethods(bf).Where(x => x.DeclaringType == typeof(ILCursor)).Cast<MethodBase>()
            .Append(typeof(DynamicMethodDefinition).GetConstructor([typeof(MethodBase)]))
            .Append(typeof(ILContext).GetMethod("Invoke")).OfType<MethodBase>())
        {
            PlatformTriple.Current.TryDisableInlining(method);
        }

    }

    public override void Unload()
    {
    }
}