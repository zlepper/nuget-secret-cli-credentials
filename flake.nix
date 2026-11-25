{
  description = "NuGet credential provider that reads feed tokens from 1Password (op) or the GitHub CLI (gh)";

  inputs.nixpkgs.url = "github:NixOS/nixpkgs/nixos-26.05";

  outputs =
    { self, nixpkgs }:
    let
      inherit (nixpkgs) lib;
      systems = [
        "x86_64-linux"
        "aarch64-linux"
      ];
      forAllSystems = f: lib.genAttrs systems (system: f nixpkgs.legacyPackages.${system});
    in
    {
      packages = forAllSystems (pkgs: {
        default = pkgs.callPackage ./nix/package.nix { };
      });

      homeManagerModules.default = import ./nix/hm-module.nix self.packages;

      devShells = forAllSystems (pkgs: {
        default = pkgs.mkShell {
          packages = [
            pkgs.dotnetCorePackages.sdk_10_0
            pkgs.gh
          ];
          # op comes from the system (/run/wrappers/bin/op), because the desktop integration needs its wrapper.
        };
      });

      checks = forAllSystems (pkgs: {
        package = self.packages.${pkgs.stdenv.hostPlatform.system}.default;
        hm-module = import ./nix/hm-module-check.nix {
          inherit pkgs lib;
          module = self.homeManagerModules.default;
        };
      });

      formatter = forAllSystems (pkgs: pkgs.nixfmt-tree);
    };
}
