using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace Celeste.Mod.aaa
{
    internal static class ReflectionHelper
    {
        class lambda_instance
        {
            public static lambda_instance Instance = new();
        }
        public const BindingFlags bf = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        internal static Func<object, Value>? GetGetter<Value>(Type type, string name)
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            static Func<object, Value>? _GetGetter(Type type, string name)
            {
                var f = type.GetField(name, bf);
                if (f is not null)
                {
                    return f.GetGetter<Value>();
                }
                var p = type.GetProperty(name, bf);
                if (p is not null)
                {
                    return p.GetGetter<Value>();
                }
                return null;
            }

            return _GetGetter(type, name);
        }

        //almost as fast as proprty that saved in a lambda.
        public static Func<object, Value>? GetGetter<Value>(this PropertyInfo property)
        {
            if (property.GetMethod is null)
            {
                return null;
            }
            DynamicMethod getdyn = new($"", typeof(Value), [typeof(lambda_instance), typeof(object)], typeof(lambda_instance));
            var get = property.GetMethod!;


            var ic = getdyn.GetILGenerator();
            ic.Emit(OpCodes.Ldarg_1);
            ic.Emit(OpCodes.Call, get);
            ic.Emit(OpCodes.Ret);
            return getdyn.CreateDelegate<Func<object, Value>>(lambda_instance.Instance);
        }
        //as fast as lambda.
        public static Func<object, Value> GetGetter<Value>(this FieldInfo field)
        {
            DynamicMethod get = new($"", typeof(Value), [typeof(lambda_instance), typeof(object)], typeof(lambda_instance));
            var ic = get.GetILGenerator();
            ic.Emit(OpCodes.Ldarg_1);
            ic.Emit(OpCodes.Ldfld, field);
            ic.Emit(OpCodes.Ret);

            return get.CreateDelegate<Func<object, Value>>(lambda_instance.Instance);
        }
    }
}
