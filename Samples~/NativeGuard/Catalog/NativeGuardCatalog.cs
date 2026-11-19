using AIBT.Burst;
using AIBT.StandardNodes;

namespace AIBT.Samples.NativeGuard
{
    // The whole opt-in: this project's catalog set lists the package's native node library.
    [AibtCatalogSet("aibt.samples.native-guard.catalog", 1u, typeof(StandardNativeShard))]
    public static partial class NativeGuardCatalog { }
}
