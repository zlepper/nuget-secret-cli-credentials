# Evaluates the home-manager module against stub home-manager options and checks what it produces,
# without depending on home-manager itself.
{
  pkgs,
  lib,
  module,
}:

let
  stubs = {
    options = {
      home.file = lib.mkOption { type = lib.types.attrsOf lib.types.anything; };
      xdg.configFile = lib.mkOption { type = lib.types.attrsOf lib.types.anything; };
      xdg.dataHome = lib.mkOption { type = lib.types.str; };
    };
    config.xdg.dataHome = "/home/test/.local/share";
  };

  evaluated = lib.evalModules {
    modules = [
      stubs
      module
      {
        _module.args.pkgs = pkgs;
        programs.nuget-secret-cli-credentials = {
          enable = true;
          account = "team.1password.com";
          sources = [
            {
              uriPrefix = "https://nuget.pkg.github.com/acme/";
              username = "octocat";
              password = "op://Private/GitHub Packages/token";
            }
            {
              uriPrefix = "https://nuget.pkg.github.com/other/";
              username = "octocat";
              password = "gh://github.com";
            }
          ];
        };
      }
    ];
  };

  inherit (evaluated) config;
  pluginLink = config.home.file.".nuget/plugins/netcore/nuget-plugin-secret-cli";
  configFile = config.xdg.configFile."nuget-secret-cli-credentials/config.json";
in
pkgs.runCommand "nuget-secret-cli-credentials-hm-module-check"
  {
    nativeBuildInputs = [ pkgs.jq ];
    pluginSource = pluginLink.source;
    inherit (configFile) source;
  }
  ''
    test -f "$pluginSource/nuget-plugin-secret-cli.dll"
    test -f "$pluginSource/nuget-plugin-secret-cli.runtimeconfig.json"
    test "${lib.boolToString pluginLink.recursive}" = true
    grep -q 'plugins-cache' <<'EOF'
    ${pluginLink.onChange}
    EOF

    jq -e '
      .opPath == "/run/wrappers/bin/op"
      and .account == "team.1password.com"
      and .nonInteractive == "prompt"
      and .timeoutSeconds == 120
      and (has("ghPath") | not)
      and (.sources | length) == 2
      and .sources[1].password == "gh://github.com"
    ' "$source"
    touch "$out"
  ''
