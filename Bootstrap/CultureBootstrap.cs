// All comments in English as requested
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Threading;

namespace Spirit.Core.Bootstrap
{
    internal static class CultureBootstrap
    {
        [ModuleInitializer]
        internal static void Init()
        {
            // Enforce invariant/English culture before any other code runs
            var ci = CultureInfo.InvariantCulture; // or CultureInfo.GetCultureInfo("en-US")

            CultureInfo.DefaultThreadCurrentCulture = ci;
            CultureInfo.DefaultThreadCurrentUICulture = ci;

            // Also set for the current thread immediately
            Thread.CurrentThread.CurrentCulture = ci;
            Thread.CurrentThread.CurrentUICulture = ci;
        }
    }
}
