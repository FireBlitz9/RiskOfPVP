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
            {
                var load = aerolt.MainModule.GetType("Aerolt.Load");
                Check((string)load.Fields.Single(f => f.Name == "Version").Constant == "4.3.1", "Unsupported Aerolt version");
                var callback = load.Methods.Single(m => m.Name == "OnRunStart");
                Check(callback.IsStatic && callback.ReturnType.FullName == "System.Void" && callback.Parameters.Count == 1 && callback.Parameters[0].ParameterType.FullName == "RoR2.Run", "Aerolt callback signature changed");
                Check(Calls(callback, "Aerolt.UI.AeroltUniverseUI", "Bootstrap"), "Aerolt's cold callback no longer starts only its menu");
                var ui = aerolt.MainModule.GetType("Aerolt.UI.AeroltUniverseUI");
                var ready = ui.Properties.Single(p => p.Name == "IsReady");
                Check(ready.PropertyType.FullName == "System.Boolean" && ready.GetMethod.IsPublic && ready.GetMethod.IsStatic, "Public menu readiness API changed");
                var bootstrap = ui.Methods.Single(m => m.Name == "Bootstrap");
                Check(bootstrap.IsPublic && bootstrap.IsStatic && bootstrap.ReturnType.FullName == "System.Void" && bootstrap.Parameters.Count == 0, "Public menu bootstrap API changed");
                var visible = ui.Methods.Single(m => m.Name == "SetVisible");
                Check(visible.IsPublic && visible.IsStatic && visible.ReturnType.FullName == "System.Void" && visible.Parameters.Count == 1 && visible.Parameters[0].ParameterType.FullName == "System.Boolean", "Public idempotent menu visibility API changed");
                var keys = load.Fields.Single(f => f.Name == "KeyBinds");
                var dictionary = keys.FieldType as GenericInstanceType;
                Check(keys.IsPublic && keys.IsStatic && dictionary != null && dictionary.ElementType.FullName == "System.Collections.Generic.Dictionary`2", "Public keybind dictionary contract changed");
                var key = aerolt.MainModule.GetType(dictionary.GenericArguments[0].FullName);
                Check(key.IsEnum && key.Fields.Any(f => f.Name == "OpenMenu" && f.HasConstant), "Native OpenMenu binding identifier changed");
                var pressed = load.Methods.Single(m => m.Name == "GetKeyPressed");
                Check(pressed.IsPublic && pressed.IsStatic && pressed.ReturnType.FullName == "System.Boolean" && pressed.Parameters.Count == 1 && pressed.Parameters[0].ParameterType.FullName == dictionary.GenericArguments[1].FullName, "Public configured-key query contract changed");
                Check(Calls(pressed, "UnityEngine.Input", "GetKeyDown"), "Menu activation must use a fresh configured key-down, not a held key");
            }
            if (args.Length > 1)
                using (var dropin = AssemblyDefinition.ReadAssembly(args[1]))
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
            Console.WriteLine("PASS: " + checks + " optional public Aerolt menu/DropIn compatibility contract checks."); return 0;
        }
        catch (Exception error) { Console.Error.WriteLine("FAILED: " + error); return 1; }
    }
}
