// ArchTweaker 2.1 unit tests. Compiled together with src/**/*.cs by tools/run-tests.sh (one assembly, so
// internal members are reachable; private statics are reached by reflection). Lightweight runner — no
// xUnit/dotnet dependency, because the product is built with Mono's mcs against .NET Framework 4.5.
// Convention: behavior-named [Test] methods, Arrange-Act-Assert, one logical assertion each.
using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using ArchTweaker;

sealed class TestAttribute : Attribute { }

static class T
{
    public static int Pass, Fail;
    public static void Eq(object a, object b, string m)
    {
        if (!object.Equals(a, b)) { Fail++; Console.WriteLine("  FAIL " + m + "  (expected [" + b + "], got [" + a + "])"); }
        else Pass++;
    }
    public static void True(bool c, string m) { if (!c) { Fail++; Console.WriteLine("  FAIL " + m); } else Pass++; }
    public static void False(bool c, string m) { True(!c, m); }

    // reflection access to private statics
    public static object Call(Type t, string name, params object[] args)
    {
        var mi = t.GetMethod(name, BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static);
        if (mi == null) throw new MissingMethodException(t.Name + "." + name);
        return mi.Invoke(null, args);
    }
    public static object Field(Type t, string name)
    {
        var fi = t.GetField(name, BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static);
        if (fi == null) throw new MissingFieldException(t.Name + "." + name);
        return fi.GetValue(null);
    }
}

static class Runner
{
    static void Main()
    {
        var methods = Assembly.GetExecutingAssembly().GetTypes()
            .SelectMany(ty => ty.GetMethods(BindingFlags.Public | BindingFlags.Static))
            .Where(m => m.GetCustomAttributes(typeof(TestAttribute), false).Any())
            .OrderBy(m => m.DeclaringType.Name).ThenBy(m => m.Name).ToList();
        L.Use("en");   // 3.42: the tests expect English text, whatever language this PC's ArchTweaker uses
        Console.WriteLine("Running " + methods.Count + " tests\n");
        string cls = null;
        foreach (var m in methods)
        {
            if (m.DeclaringType.Name != cls) { cls = m.DeclaringType.Name; Console.WriteLine(cls); }
            try { m.Invoke(null, null); }
            catch (Exception ex) { T.Fail++; Console.WriteLine("  ERROR " + m.Name + ": " + (ex.InnerException ?? ex).Message); }
        }
        Console.WriteLine("\n" + T.Pass + " passed, " + T.Fail + " failed");
        Environment.Exit(T.Fail == 0 ? 0 : 1);
    }
}

// ----------------- Winapp2 (deep-clean) -----------------
