using System.Reflection;

namespace NuGetOpCredentialProvider.Tests.Support;

internal static class PluginLocation
{
    /// <summary>The plugin's own build output (with its deps.json and runtimeconfig.json), as NuGet would run it.</summary>
    public static string DllPath { get; } = typeof(PluginLocation).Assembly
        .GetCustomAttributes<AssemblyMetadataAttribute>()
        .Single(attribute => attribute.Key == "PluginPath")
        .Value!;

    /// <summary>The <c>dotnet</c> host running the tests, so the plugin and <c>dotnet restore</c> use the same SDK.</summary>
    public static string DotnetHost { get; } = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet";
}
