// All comments in English as requested
using System;
using Microsoft.Extensions.DependencyInjection;
using Spirit.Core.Config;

namespace Spirit.Core.Bootstrap
{
    /// <summary>
    /// Global access to DI container and config. Keep this tiny and read-only.
    /// </summary>
    public static class SpiritHost
    {
        public static IServiceProvider Services { get; internal set; } = default!;
        public static AppConfig Config { get; internal set; } = new();

        public static T Get<T>() where T : notnull => Services.GetRequiredService<T>();
    }
}
