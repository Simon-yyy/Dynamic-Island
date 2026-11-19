using System;
using System.Reflection;

namespace DynamicIsland
{
    /// <summary>
    /// 订阅 / 解绑 WinRT 事件，兼容 .NET Framework 自带 csc 与较新的 Roslyn。
    /// Framework csc 引用 winmd 时，<c>+= / -=</c> 会报 CS1545，要求调用
    /// add_Xxx(TypedEventHandler) / remove_Xxx(EventRegistrationToken)。
    /// 同一组 add_/remove_ 在 Roslyn 里是事件访问器，直接写出来会报 CS0571。
    /// 因此这里不在 C# 源码里写 += 或 add_Xxx(...)，而是用反射调用同名 add_/remove_，
    /// 并保存返回的 EventRegistrationToken，运行时语义与编译器提示的调用一致。
    /// </summary>
    internal static class WinRtEvent
    {
        public static bool TryAdd(object instance, string eventName, Delegate handler, out object token)
        {
            token = null;
            if (instance == null || handler == null || string.IsNullOrEmpty(eventName)) return false;
            try
            {
                MethodInfo add = FindAdd(instance.GetType(), eventName);
                if (add == null) return false;
                token = add.Invoke(instance, new object[] { handler });
                return true;
            }
            catch
            {
                token = null;
                return false;
            }
        }

        public static void TryRemove(object instance, string eventName, ref object token, ref bool hooked)
        {
            if (!hooked) return;
            hooked = false;
            object t = token;
            token = null;
            if (instance == null || t == null || string.IsNullOrEmpty(eventName)) return;
            try
            {
                MethodInfo remove = FindRemove(instance.GetType(), eventName);
                if (remove == null) return;
                remove.Invoke(instance, new object[] { t });
            }
            catch { }
        }

        private static MethodInfo FindAdd(Type type, string eventName)
        {
            MethodInfo mi = AddMethodOn(type, eventName);
            if (mi != null) return mi;
            Type[] ifaces = type.GetInterfaces();
            for (int i = 0; i < ifaces.Length; i++)
            {
                mi = AddMethodOn(ifaces[i], eventName);
                if (mi != null) return mi;
            }
            if (type.BaseType != null && type.BaseType != typeof(object))
                return FindAdd(type.BaseType, eventName);
            return null;
        }

        private static MethodInfo FindRemove(Type type, string eventName)
        {
            MethodInfo mi = RemoveMethodOn(type, eventName);
            if (mi != null) return mi;
            Type[] ifaces = type.GetInterfaces();
            for (int i = 0; i < ifaces.Length; i++)
            {
                mi = RemoveMethodOn(ifaces[i], eventName);
                if (mi != null) return mi;
            }
            if (type.BaseType != null && type.BaseType != typeof(object))
                return FindRemove(type.BaseType, eventName);
            return null;
        }

        private static MethodInfo AddMethodOn(Type type, string eventName)
        {
            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
            EventInfo ev = type.GetEvent(eventName, flags);
            if (ev != null)
            {
                MethodInfo m = ev.GetAddMethod(true);
                if (m != null) return m;
            }
            return type.GetMethod("add_" + eventName, flags);
        }

        private static MethodInfo RemoveMethodOn(Type type, string eventName)
        {
            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
            EventInfo ev = type.GetEvent(eventName, flags);
            if (ev != null)
            {
                MethodInfo m = ev.GetRemoveMethod(true);
                if (m != null) return m;
            }
            return type.GetMethod("remove_" + eventName, flags);
        }
    }
}
