using AsmResolver.PE.DotNet.Metadata.Tables.Rows;
using Mono.Cecil.Cil;
using MonoMod.Cil;
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
//https://stackoverflow.com/questions/1312166/print-full-signature-of-a-method-from-a-methodinfo
public static class MethodInfoExtensions
{
    /// <summary>
    /// Return the method signature as a string.
    /// </summary>
    /// <param name="method">The Method</param>
    /// <param name="callable">Return as an callable string(public void a(string b) would return a(b))</param>
    /// <returns>Method signature</returns>
    public static string GetSignature(this MethodInfo method, bool callable = false)
    {
        var firstParam = true;
        var sigBuilder = new StringBuilder();
        if (callable == false)
        {
            //if (method.IsPublic)
            //{
            //    sigBuilder.Append("public ");
            //}
            //else if (method.IsPrivate)
            //{
            //    sigBuilder.Append("private ");
            //}
            //else if (method.IsAssembly)
            //{
            //    sigBuilder.Append("internal ");
            //}
            //
            //if (method.IsFamily)
            //{
            //    sigBuilder.Append("protected ");
            //}
            //
            //if (method.IsStatic)
            //{
            //    sigBuilder.Append("static ");
            //}

            sigBuilder.Append(TypeName(method.ReturnType));
            sigBuilder.Append(' ');
        }

        sigBuilder.Append(TypeName(method.DeclaringType));
        sigBuilder.Append('.');
        sigBuilder.Append(method.Name);

        // Add method generics
        if (method.IsGenericMethod)
        {
            sigBuilder.Append("<");
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
            sigBuilder.Append(">");
        }
        sigBuilder.Append("(");
        firstParam = true;
        var secondParam = false;
        foreach (var param in method.GetParameters())
        {
            if (firstParam)
            {
                firstParam = false;
                if (method.IsDefined(typeof(System.Runtime.CompilerServices.ExtensionAttribute), false))
                {
                    if (callable)
                    {
                        secondParam = true;
                        continue;
                    }
                    sigBuilder.Append("this ");
                }
            }
            else if (secondParam == true)
            {
                secondParam = false;
            }
            else
            {
                sigBuilder.Append(", ");
            }

            if (param.ParameterType.IsByRef)
            {
                sigBuilder.Append("ref ");
            }
            else if (param.IsOut)
            {
                sigBuilder.Append("out ");
            }

            if (!callable)
            {
                sigBuilder.Append(TypeName(param.ParameterType));
                sigBuilder.Append(' ');
            }
            sigBuilder.Append(param.Name);
        }
        sigBuilder.Append(") @")
            //.Append(method.Module.Assembly.GetName().Name)
            .Append(method.Module.ScopeName)
            ;
        return sigBuilder.ToString();
    }

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
    static void AddLogger(ILContext il)
    {
        ILCursor ic = new(il);
        static void localmethod()
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

                var info = new LogInfo()
                {
                    ILOffset = frame.GetILOffset(),
                    Method = frame.GetMethod()!,
                    File = frame.GetFileName()!,
                    FileRow = frame.GetFileLineNumber(),
                    FileCol = frame.GetFileColumnNumber(),
                };
                infos.Add(info);
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
            CurrentManipLogs.Add(infos[0]);
        };
        ic.EmitDelegate(localmethod);

        while (ic.TryGotoNext(MoveType.AfterLabel, i => i.MatchRet()))
        {
            static void reset() => Bottom--;
            ic.EmitDelegate(reset);
            ic.Index++;
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
    static List<LogInfo> CurrentManipLogs = [];

    static Dictionary<ManipInfo, List<LogInfo>> LatestManipLogs = [];
    static Dictionary<ManipInfo, List<LogInfo>> OrigManipLogs = [];

    static Dictionary<ManipInfo, (List<LogInfo> now, List<LogInfo> orig)> Difference = [];
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
        RefreshHooks();
        IgnoreMonoMod();
        GetOrig();
        Compare();
        Debugger.Launch();
        Clear();
    }

    private static void IgnoreMonoMod()
    {
        var toremove = LatestManipLogs.Where(x =>
        {
            var t = x.Key.Target.DeclaringType;
            if (t == typeof(ILCursor) || t == typeof(DynamicMethodDefinition) || t == typeof(ILContext))
            {
                return false;
            }
            return true;
        });
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

    private static void RefreshHooks()
    {
        //refresh doesnot exists.

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
                var hookState = new DynamicData(hook).Get("hook")!;
                var manipulator = new DynamicData(hookState).Get<ILContext.Manipulator>("Manip")!;

                try
                {
                    cloneDefContext.Invoke(manipulator);

                    var instrs = cloneDefContext.Instrs;

                }
                catch (Exception ex)
                {
                    Logger.Log("USSRNAME.Stolen.MappingUtils.ILHookDiffer", $"Failed to apply IL hook {hook.ManipulatorMethod.GetID()}: {ex}");
                }
            }
        }
    }

    public static void Prepare()
    {
        foreach (var method in typeof(ILCursor).GetMethods().Where(x => x.DeclaringType == typeof(ILCursor)))
        {
            try
            {
                ills.Add(new(method, AddLogger));
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
    }

    public override void Load()
    {
    }

    public override void Unload()
    {
    }
}