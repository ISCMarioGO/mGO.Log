using GO.Log.Configuration;
using GO.Log.Extensions.Hosting.Configuration;
using GO.Log.Extensions.Hosting.Services;
using GO.Log.Providers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Configuration;
using Microsoft.Extensions.Options;

namespace Microsoft.Extensions.Logging;

/// <summary>Registration extensions for the standard logging builder.</summary>
public static class GOLogLoggingBuilderExtensions
{
    /// <summary>Registers one provider and host lifecycle service, binding a configuration section once at startup.</summary>
    public static ILoggingBuilder AddGOLog(this ILoggingBuilder builder, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        Register(builder);
        builder.Services.Configure<GOLogOptions>(configuration);
        return builder;
    }
    /// <summary>Registers one provider and configures its startup snapshot using a delegate.</summary>
    public static ILoggingBuilder AddGOLog(this ILoggingBuilder builder, Action<GOLogOptions>? configure = null)
    {
        Register(builder);
        if (configure is not null) builder.Services.Configure(configure);
        return builder;
    }
    private static void Register(ILoggingBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.AddConfiguration();
        builder.Services.AddOptions<GOLogOptions>().ValidateOnStart();
        builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<GOLogOptions>, GOLogOptionsValidator>());
        builder.Services.TryAddSingleton(sp => new GOLoggerProvider(sp.GetRequiredService<IOptions<GOLogOptions>>().Value));
        // An implementation-type registration lets TryAddEnumerable de-duplicate repeated calls.
        builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<ILoggerProvider, GOLogProviderRegistration>());
        builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, GOLogHostedService>());
        LoggerProviderOptions.RegisterProviderOptions<GOLogOptions, GOLoggerProvider>(builder.Services);
    }
}
