using Celeste.Mod;
using Mono.Cecil.Cil;
using MonoMod.Cil;
using MonoMod.Core.Platforms;
using MonoMod.Utils;
using System.Linq;
using System.Reflection;

namespace AAAADoNotInline
{
    internal class Program : EverestModule
    {
        const BindingFlags bf = 0
            | BindingFlags.Public
            | BindingFlags.NonPublic
            | BindingFlags.Instance
            | BindingFlags.Static;

        public override void Load()
        {
            var instr = typeof(Instruction).GetProperties(bf).Select(x => x.GetSetMethod());
            foreach (var method in typeof(ILCursor).GetMethods(bf).Where(x => x.DeclaringType == typeof(ILCursor)).Cast<MethodBase>()
                .Append(typeof(DynamicMethodDefinition).GetConstructor([typeof(MethodBase)]))
                .Append(typeof(ILContext).GetMethod("Invoke")).OfType<MethodBase>()
                .Concat(instr))
            {
                PlatformTriple.Current.TryDisableInlining(method);
            }

        }
        public override void Unload()
        {
        }

    }
}
