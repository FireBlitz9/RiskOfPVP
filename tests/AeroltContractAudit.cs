using System;
using System.Linq;
using Mono.Cecil;

// Optional, metadata-only verification against the user's copied assemblies.
// Neither assembly is required to compile or run RiskOfArena.
internal static class AeroltContractAudit
{
    private static int checks;
    private static void Check(bool value, string message) { checks++; if (!value) throw new Exception(message); }
    private static bool Calls(MethodDefinition method, string type, string name)
    { return method.Body.Instructions.Any(i => i.Operand is MethodReference && ((MethodReference)i.Operand).DeclaringType.FullName == type && ((MethodReference)i.Operand).Name == name); }
    public static int Main(string[] args)
    {
        try
        {
            using (var aerolt = AssemblyDefinition.ReadAssembly(args[0]))
            using (var universe = AssemblyDefinition.ReadAssembly(args[1]))
            {
                var load = aerolt.MainModule.GetType("Aerolt.Load");
                Check((string)load.Fields.Single(f => f.Name == "Version").Constant == "4.3.1", "Unsupported Aerolt version");
                var callback = load.Methods.Single(m => m.Name == "OnRunStart");
                Check(callback.IsStatic && callback.ReturnType.FullName == "System.Void" && callback.Parameters.Count == 1 && callback.Parameters[0].ParameterType.FullName == "RoR2.Run", "Aerolt callback signature changed");
                Check(Calls(callback, "Aerolt.UI.AeroltUniverseUI", "Bootstrap"), "Aerolt's cold initialization no longer calls Bootstrap");
                var bootstrap = aerolt.MainModule.GetType("Aerolt.UI.AeroltUniverseUI").Methods.Single(m => m.Name == "Bootstrap");
                Check(Calls(bootstrap, "UniverseLib.Universe", "Init"), "Aerolt Bootstrap no longer initializes UniverseLib");
                Check(universe.Name.Version == new Version(1, 5, 1, 0), "Unsupported UniverseLib version");
                var reflection = universe.MainModule.GetType("UniverseLib.ReflectionUtility");
                var init = reflection.Methods.Single(m => m.Name == "Init");
                Check(init.IsStatic && init.Parameters.Count == 0 && init.ReturnType.FullName == "System.Void", "Reflection entry point changed");
                Check(Calls(init, "UniverseLib.ReflectionPatches", "Init") && Calls(init, "UniverseLib.ReflectionUtility", "Initialize"), "Native reflection init sequence changed");
                Check(reflection.Properties.Single(p => p.Name == "Instance").SetMethod != null && reflection.Methods.Any(m => m.IsConstructor && m.Parameters.Count == 0 && m.IsPublic), "Reflection instance initialization contract changed");
                Check(reflection.Fields.Single(f => f.Name == "Initializing").FieldType.FullName == "System.Boolean", "Native ready gate changed");
                Check(reflection.Fields.Single(f => f.Name == "AllTypes").FieldType.FullName == "System.Collections.Generic.SortedDictionary`2<System.String,System.Type>", "Type cache changed");
                Check(reflection.Fields.Single(f => f.Name == "AllNamespaces").FieldType.FullName == "System.Collections.Generic.List`1<System.String>", "Namespace list changed");
                Check(reflection.Fields.Single(f => f.Name == "uniqueNamespaces").FieldType.FullName == "System.Collections.Generic.HashSet`1<System.String>", "Namespace deduplication changed");
                Check(reflection.Fields.Single(f => f.Name == "OnTypeLoaded").FieldType.FullName == "System.Action`1<System.Type>", "Type notification changed");
                var setup = universe.MainModule.GetType("UniverseLib.Universe").NestedTypes.Single(t => t.Name.StartsWith("<SetupCoroutine>"));
                Check(setup.Methods.Single(m => m.Name == "MoveNext").Body.Instructions.Any(i => i.Operand is FieldReference && ((FieldReference)i.Operand).Name == "Initializing"), "Native UI must wait for reflection readiness");
                var texture = universe.MainModule.GetType("UniverseLib.Runtime.Mono.MonoTextureHelper").NestedTypes.Single(t => t.Name.StartsWith("<InitCoro>"));
                Check(texture.Methods.Single(m => m.Name == "MoveNext").Body.Instructions.Any(i => i.Operand is FieldReference && ((FieldReference)i.Operand).Name == "Initializing"), "Native texture setup must wait for reflection readiness");
                Check(reflection.Methods.Any(m => m.Name == "ForceLoadManagedAssemblies") && reflection.Methods.Any(m => m.Name == "CacheTypes"), "Measured cold scan path changed");
            }
            if (args.Length > 2)
                using (var dropin = AssemblyDefinition.ReadAssembly(args[2]))
                {
                    var type = dropin.MainModule.GetType("DropInMultiplayer.DropInMultiplayer");
                    var plugin = type.CustomAttributes.Single(a => a.AttributeType.FullName == "BepInEx.BepInPlugin");
                    Check((string)plugin.ConstructorArguments[0].Value == "com.niwith.DropInMultiplayer", "DropIn plugin identity changed");
                    var grant = type.Methods.Single(m => m.Name == "GiveCatchUpItems");
                    Check(grant.IsStatic && grant.ReturnType.FullName == "System.Void" && grant.Parameters.Count == 1 && grant.Parameters[0].ParameterType.FullName == "RoR2.NetworkUser", "DropIn grant signature changed");
                    Check(Calls(grant, "System.Linq.Enumerable", "Average") && Calls(grant, "DropInMultiplayer.DropInMultiplayer", "GiveRandomItems"), "Native DropIn catch-up path changed");
                    var spawn = type.Methods.Single(m => m.Name == "SpawnNewPlayerWithBody");
                    Check(Calls(spawn, "RoR2.Run", "OnUserAdded") && Calls(spawn, "RoR2.CharacterMaster", "SpawnBody") && Calls(spawn, "DropInMultiplayer.DropInMultiplayer", "GiveCatchUpItems"), "Native admission/spawn/catch-up sequence changed");
                }
            Console.WriteLine("PASS: " + checks + " optional Aerolt/UniverseLib/DropIn compatibility contract checks."); return 0;
        }
        catch (Exception error) { Console.Error.WriteLine("FAILED: " + error); return 1; }
    }
}
