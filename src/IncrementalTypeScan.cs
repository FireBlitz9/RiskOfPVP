using System;
using System.Reflection;

namespace ArenaPvP
{
    // TypeDef rows are contiguous ECMA-335 metadata tokens. Resolving one row
    // avoids Assembly/Module.GetTypes, which resolves every type before returning
    // and took 13.7 seconds for MMHOOK_RoR2 in the user's Unity/Mono trace.
    internal sealed class IncrementalTypeScan
    {
        private readonly Module[] modules;
        private int moduleIndex;
        private int row = 2; // Row 1 is the metadata-only <Module> pseudo-type.
        internal int FailedTypes { get; private set; }

        internal IncrementalTypeScan(Assembly assembly) : this(assembly.GetModules(false)) { }
        internal IncrementalTypeScan(Module[] modules) { this.modules = modules; }

        // At most one runtime type resolution per call, including failures and
        // module boundaries. No type constructors or Unity APIs are invoked.
        internal bool Step(out Type type)
        {
            type = null;
            if (moduleIndex >= modules.Length) return false;
            if (row > 0x00ffffff) { moduleIndex++; row = 2; return moduleIndex < modules.Length; }
            try
            {
                type = modules[moduleIndex].ResolveType(0x02000000 | row++);
            }
            catch (ArgumentOutOfRangeException)
            {
                // Verified in the installed Mono RuntimeModule.ResolveType:
                // OutOfRange means the TypeDef table has ended, not a bad type.
                moduleIndex++; row = 2;
            }
            catch (Exception error) when (!(error is OutOfMemoryException) && !(error is System.Threading.ThreadAbortException))
            {
                // Match native partial-type recovery: an unresolved dependency
                // must not hide later loadable rows, including private types.
                FailedTypes++;
            }
            return type != null || moduleIndex < modules.Length;
        }
    }
}
