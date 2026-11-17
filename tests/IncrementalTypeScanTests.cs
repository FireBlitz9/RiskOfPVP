using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using ArenaPvP;

internal static class IncrementalTypeScanTests
{
    private static int checks;
    internal static bool ConstructorRan;
    private static void Check(bool value, string name)
    { checks++; if (!value) throw new Exception(name); }

    private sealed class Uninitialized<T>
    {
        static Uninitialized() { ConstructorRan = true; throw new Exception("Type scanning must not initialize types."); }
        private sealed class PrivateNested<U> { }
    }

    private sealed class FaultyModule : Module
    {
        internal int Calls;
        public override Type[] GetTypes() { throw new Exception("Whole-module enumeration is forbidden."); }
        public override Type ResolveType(int token, Type[] typeArguments, Type[] methodArguments)
        {
            Calls++;
            switch (token)
            {
                case 0x02000002: return typeof(string);
                case 0x02000003: throw new TypeLoadException("Missing optional dependency");
                case 0x02000004: return typeof(Uninitialized<>);
                case 0x02000005: throw new ArgumentException("Invalid individual type");
                case 0x02000006: return typeof(int);
                default: throw new ArgumentOutOfRangeException("metadataToken");
            }
        }
    }

    private static List<Type> Read(IncrementalTypeScan scan, out int calls)
    {
        var found = new List<Type>(); calls = 0;
        while (true)
        {
            Type type; bool pending = scan.Step(out type); calls++;
            if (type != null) found.Add(type);
            if (!pending) return found;
            if (calls > 1000000) throw new Exception("Type scan did not terminate.");
        }
    }

    private static IEnumerable<Action> Steps(IncrementalTypeScan scan, List<Type> found)
    {
        bool pending = true;
        while (pending)
            yield return () => { Type type; pending = scan.Step(out type); if (type != null) found.Add(type); };
    }

    public static int Main()
    {
        try
        {
            int steps;
            foreach (Assembly assembly in new[] { Assembly.GetExecutingAssembly(), typeof(object).Assembly })
            {
                var scan = new IncrementalTypeScan(assembly);
                var found = Read(scan, out steps);
                var expected = new HashSet<Type>(assembly.GetTypes());
                Check(expected.SetEquals(found), "Preserve every native type in " + assembly.GetName().Name);
                Check(found.Count == expected.Count, "No duplicate types in " + assembly.GetName().Name);
                // Framework mscorlib contains a reserved, unresolvable TypeDef
                // row that GetTypes also omits. Exact type-set parity above
                // checks recovery without assuming every metadata row is public.
                Check(assembly != Assembly.GetExecutingAssembly() || scan.FailedTypes == 0, "All fixture rows resolve");
                Check(steps >= found.Count && steps <= found.Count + scan.FailedTypes + assembly.GetModules(false).Length + 1, "Bounded terminal/module steps");
            }
            Check(!ConstructorRan, "Metadata enumeration never runs static constructors");
            var fault = new FaultyModule(); var other = new FaultyModule();
            var recovery = new IncrementalTypeScan(new Module[] { fault, other });
            var recovered = Read(recovery, out steps);
            Check(recovered.SequenceEqual(new[] { typeof(string), typeof(Uninitialized<>), typeof(int), typeof(string), typeof(Uninitialized<>), typeof(int) }), "Keep later private/open generic types after loader errors, across modules");
            Check(recovery.FailedTypes == 4 && fault.Calls == 6 && other.Calls == 6 && steps == 12, "One resolution per step, including errors and module transitions");
            Type ignored;
            Check(!recovery.Step(out ignored) && fault.Calls == 6 && other.Calls == 6, "Completed scans never resolve more tokens");
            Check(Read(new IncrementalTypeScan(new Module[0]), out steps).Count == 0 && steps == 1, "Empty assembly terminates");

            var dynamic = AppDomain.CurrentDomain.DefineDynamicAssembly(new AssemblyName("LargeTypeScanFixture"), AssemblyBuilderAccess.Run);
            var module = dynamic.DefineDynamicModule("LargeTypeScanFixture");
            for (int i = 0; i < 10000; i++) module.DefineType("Fixture.T" + i, TypeAttributes.Public).CreateType();
            var results = new List<Type>(); var large = new IncrementalTypeScan(dynamic);
            using (var frames = new FrameWork(Steps(large, results).GetEnumerator()))
            {
                int executed = 0;
                Check(frames.RunSlice(false, () => 0, .002, action => { action(); executed++; }) == 0 && executed == 0, "Large scans remain paused before player readiness");
                Check(frames.RunSlice(true, () => 0, .002, action => { action(); executed++; }, 7) == 7 && results.Count == 7, "A frame never eagerly resolves the other 9,993 types");
                double clock = 0;
                Check(frames.RunSlice(true, () => clock, .002, action => { action(); executed++; clock += .003; }) == 1 && results.Count == 8, "A slow individual resolution yields before the next row");
                Check(frames.RunSlice(false, () => clock, .002, action => { action(); executed++; }) == 0 && results.Count == 8, "Scene transition pauses an unfinished scan");
                int slices = 0;
                while (results.Count < 10000 && slices++ < 100)
                    frames.RunSlice(true, () => 0, .002, action => action());
                Check(results.Count == 10000 && new HashSet<Type>(dynamic.GetTypes()).SetEquals(results), "Large scan completes across frames with exactly the native type set");
            }
            Check(!ConstructorRan, "Deferred scans still never execute fixture constructors");
            Console.WriteLine("PASS: " + checks + " incremental runtime type-scan assertions, including 10,000-type frame-budget and loader-error fixtures.");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine("FAILED: " + error); return 1; }
    }
}
