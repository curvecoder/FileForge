using System.Runtime.CompilerServices;
using FileForge.IO;

// ProtectedFileStore moved to FileForge.Core so that applications not targeting Windows can use it;
// the forward keeps assemblies compiled against the previous FileForge.dll loadable.
[assembly: TypeForwardedTo(typeof(ProtectedFileStore))]
