# home-manager module: installs the plugin into NuGet's convention folder and writes its config.
packages:
{
  config,
  lib,
  pkgs,
  ...
}:

let
  cfg = config.programs.nuget-secret-cli-credentials;
  inherit (lib) mkOption types;

  sourceType = types.submodule {
    options = {
      uriPrefix = mkOption {
        type = types.strMatching "https://.*";
        example = "https://nuget.pkg.github.com/my-org/";
        description = "Feed URI prefix. Requests are matched by longest prefix; end it with a slash.";
      };
      username = mkOption {
        type = types.str;
        example = "octocat";
        description = "User name: a literal, an `op://` reference or `gh://<host>`.";
      };
      password = mkOption {
        type = types.str;
        example = "op://Private/GitHub Packages/token";
        description = ''
          Password: an `op://` reference (read with `op read`) or `gh://<host>` (the GitHub CLI's own token,
          `gh auth token --hostname <host>`). Never put the secret itself here: it would land in the Nix store.
        '';
      };
    };
  };

  settings = lib.filterAttrs (_: value: value != null) {
    inherit (cfg)
      opPath
      ghPath
      account
      nonInteractive
      timeoutSeconds
      sources
      ;
  };

  configFile = (pkgs.formats.json { }).generate "nuget-secret-cli-credentials.json" settings;

  # NuGet caches each plugin's operation claims for 30 days; drop them whenever the plugin or its sources change.
  clearPluginsCache = ''
    rm -rf "${config.xdg.dataHome}/NuGet/plugins-cache"
  '';
in
{
  options.programs.nuget-secret-cli-credentials = {
    enable = lib.mkEnableOption "the nuget-plugin-secret-cli NuGet credential provider";

    package = mkOption {
      type = types.package;
      default = packages.${pkgs.stdenv.hostPlatform.system}.default;
      defaultText = lib.literalExpression "nuget-secret-cli-credentials.packages.\${system}.default";
      description = "The plugin package.";
    };

    opPath = mkOption {
      type = types.nullOr types.str;
      default = "/run/wrappers/bin/op";
      description = ''
        Path to `op`. The NixOS 1Password module's setgid wrapper is required for the desktop-app integration.
        Null searches PATH.
      '';
    };

    ghPath = mkOption {
      type = types.nullOr types.str;
      default = null;
      example = lib.literalExpression ''"''${pkgs.gh}/bin/gh"'';
      description = "Path to `gh`. Null searches PATH, then the NixOS profile directories.";
    };

    account = mkOption {
      type = types.nullOr types.str;
      default = null;
      example = "my-team.1password.com";
      description = "1Password account passed to `op --account`, for setups with several accounts.";
    };

    nonInteractive = mkOption {
      type = types.enum [
        "prompt"
        "skip"
      ];
      default = "prompt";
      description = ''
        What to do when NuGet restores non-interactively (IDEs, plain `dotnet restore`) and 1Password is locked.
        "prompt" lets 1Password show its approval dialog; "skip" answers "not found" instead.
      '';
    };

    timeoutSeconds = mkOption {
      type = types.ints.positive;
      default = 120;
      description = "How long `op` or `gh` may run, including waiting for the 1Password approval.";
    };

    sources = mkOption {
      type = types.listOf sourceType;
      default = [ ];
      description = "Feeds the plugin provides credentials for.";
    };
  };

  config = lib.mkIf cfg.enable {
    # Linked file by file, so deps.json and runtimeconfig.json sit next to the dll.
    home.file.".nuget/plugins/netcore/nuget-plugin-secret-cli" = {
      source = "${cfg.package}/lib/nuget-plugin-secret-cli";
      recursive = true;
      onChange = clearPluginsCache;
    };

    xdg.configFile."nuget-secret-cli-credentials/config.json" = {
      source = configFile;
      onChange = clearPluginsCache;
    };
  };
}
