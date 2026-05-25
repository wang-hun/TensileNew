using System;
using System.Linq;
using System.Reflection;
using System.Windows.Controls;

public static class ReflectDump
{
    public static void Run()
    {
        var dv = typeof(DocumentViewer);
        var field = dv.GetField("_findToolbar", BindingFlags.Instance | BindingFlags.NonPublic);
        Console.WriteLine(field == null ? "NO_FIELD" : field.FieldType.FullName);
        if (field != null)
        {
            var t = field.FieldType;
            Console.WriteLine("PROPERTIES");
            foreach (var p in t.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.FlattenHierarchy))
                Console.WriteLine($"P {p.PropertyType.FullName} {p.Name} write={p.CanWrite}");
            Console.WriteLine("METHODS");
            foreach (var m in t.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.FlattenHierarchy).Where(m => m.Name.IndexOf("find", StringComparison.OrdinalIgnoreCase) >= 0 || m.Name.IndexOf("search", StringComparison.OrdinalIgnoreCase) >= 0 || m.Name.IndexOf("text", StringComparison.OrdinalIgnoreCase) >= 0 || m.Name.IndexOf("next", StringComparison.OrdinalIgnoreCase) >= 0 || m.Name.IndexOf("previous", StringComparison.OrdinalIgnoreCase) >= 0 || m.Name.IndexOf("result", StringComparison.OrdinalIgnoreCase) >= 0))
                Console.WriteLine("M " + m);
            Console.WriteLine("FIELDS");
            foreach (var f in t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.FlattenHierarchy))
                Console.WriteLine($"F {f.FieldType.FullName} {f.Name}");
        }

        var findMethod = dv.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .FirstOrDefault(m => m.Name == "Find" && m.GetParameters().Length == 1);
        Console.WriteLine(findMethod == null ? "NO_FIND_METHOD" : "DV_FIND " + findMethod);
        if (findMethod != null)
        {
            foreach (var p in findMethod.GetParameters())
                Console.WriteLine($"PARAM {p.ParameterType.FullName} {p.Name}");
        }
    }
}
