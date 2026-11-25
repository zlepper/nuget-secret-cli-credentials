{
  lib,
  buildDotnetModule,
  dotnetCorePackages,
}:

buildDotnetModule {
  pname = "nuget-plugin-op";
  version = "0.1.0";

  src = lib.fileset.toSource {
    root = ../.;
    fileset = lib.fileset.unions [
      ../src
      ../global.json
      ../NuGet.config
      ../Directory.Build.props
      ../Directory.Packages.props
    ];
  };

  projectFile = "src/nuget-plugin-op/nuget-plugin-op.csproj";
  # Regenerate after changing package references: nix build .#default.fetch-deps && ./result nix/deps.json
  nugetDeps = ./deps.json;

  dotnet-sdk = dotnetCorePackages.sdk_10_0;
  dotnet-runtime = dotnetCorePackages.runtime_10_0;

  # The project itself is portable. The RID (from the host platform, set by buildDotnetModule) and ReadyToRun
  # are only applied here, where the target platform is known. Passed to restore too, so the crossgen pack is
  # in the lock file.
  dotnetFlags = [ "-p:PublishReadyToRun=true" ];

  # NuGet runs convention-folder plugins as `dotnet <dll>`, so no apphost or $out/bin wrapper is needed.
  useAppHost = false;
  executables = [ ];

  meta = {
    description = "NuGet credential provider that reads feed tokens from 1Password (op) or the GitHub CLI (gh)";
    homepage = "https://github.com/zlepper/nuget-secret-cli-credentials";
    license = lib.licenses.mit;
    platforms = [
      "x86_64-linux"
      "aarch64-linux"
    ];
  };
}
