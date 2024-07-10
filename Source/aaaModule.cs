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

namespace Celeste.Mod.aaa;

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
        GetOrig();
        Compare();
        Debugger.Launch();
        Clear();
    }

    private static void Compare()
    {
        
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