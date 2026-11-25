# nuget-secret-cli-credentials

A NuGet credential provider (`nuget-plugin-op`) that reads feed tokens at restore time from **1Password**, through
the `op` CLI, or from the **GitHub CLI**'s own login, through `gh auth token`. No token is ever written to
`NuGet.Config`, any other file, or an environment variable.

## The threat it addresses

NuGet on Linux can't encrypt a stored password (`Password` in `packageSourceCredentials` is Windows-only), so feed
tokens end up as `ClearTextPassword` in `~/.nuget/NuGet/NuGet.Config`. Any `cat` of that file prints the token, and
that includes a coding agent reading the file. Wrapping commands in `op run` keeps the token out of files but puts it
in the environment of everything started inside the IDE.

NuGet's cross-platform plugin protocol lets NuGet ask a plugin for credentials only when a feed needs them. That
works the same from the `dotnet` CLI, MSBuild, Rider and VS Code. This plugin answers by running `op read` or
`gh auth token`, keeps the result in memory for the plugin process's short lifetime, and never writes it anywhere.

## Install (NixOS / home-manager)

The flake provides the package and a home-manager module. The module:
- links the plugin into NuGet's convention folder, `~/.nuget/plugins/netcore/nuget-plugin-op/`, which works whatever
  `PATH` a desktop-launched IDE has;
- writes the config file;
- clears NuGet's plugins cache whenever either changes.

```nix
# flake.nix of your system configuration
inputs.nuget-secret-cli-credentials = {
  url = "github:zlepper/nuget-secret-cli-credentials";
  inputs.nixpkgs.follows = "nixpkgs";
};
```

```nix
# the NixOS module that sets up home-manager, with the flake inputs in specialArgs
{ nuget-secret-cli-credentials, ... }:
{
  home-manager.sharedModules = [ nuget-secret-cli-credentials.homeManagerModules.default ];
}
```

```nix
# any home-manager module
{
  programs.nuget-secret-cli-credentials = {
    enable = true;
    sources = [
      {
        uriPrefix = "https://nuget.pkg.github.com/my-org/";
        username = "my-github-user";
        password = "gh://github.com"; # or "op://Private/GitHub Packages/token"
      }
    ];
  };
}
```

### Options (`programs.nuget-secret-cli-credentials.*`)

| Option | Default | Meaning |
|---|---|---|
| `enable` | `false` | Install the plugin and write the config. |
| `package` | this flake's package | The plugin build. |
| `sources` | `[ ]` | Feeds to serve: `{ uriPrefix, username, password }`. |
| `opPath` | `"/run/wrappers/bin/op"` | `op` binary. The NixOS 1Password setgid wrapper is needed for the desktop-app integration. `null` searches `PATH`. |
| `ghPath` | `null` | `gh` binary. `null` searches `PATH`, then `/etc/profiles/per-user/$USER/bin` and `/run/current-system/sw/bin`. |
| `account` | `null` | Passed as `op --account`, when you have several 1Password accounts. |
| `nonInteractive` | `"prompt"` | `"prompt"` or `"skip"`; see [Non-interactive restores](#non-interactive-restores). |
| `timeoutSeconds` | `120` | How long `op` or `gh` may run, including the wait for 1Password approval. |

### Config file

The module writes `$XDG_CONFIG_HOME/nuget-secret-cli-credentials/config.json` (default `~/.config/…`). It holds
references only, never secrets. Without Nix, write it by hand:

```json
{
  "opPath": "/run/wrappers/bin/op",
  "nonInteractive": "prompt",
  "timeoutSeconds": 120,
  "sources": [
    {
      "uriPrefix": "https://nuget.pkg.github.com/my-org/",
      "username": "my-github-user",
      "password": "op://Private/GitHub Packages/token"
    }
  ]
}
```

- **Values.** `username` and `password` can each be a literal, an `op://vault/item/field` reference, or
  `gh://<host>`. The plugin logs a warning when a `password` is a literal.
- **Matching.** A request goes to the source with the longest `uriPrefix` it starts with. Scheme and host compare
  case-insensitively, and the path compares exactly. End prefixes with `/`.
- **HTTPS only.** `http://` prefixes are refused when the config loads.
- **Invalid config.** Unknown keys are an error. A missing or invalid file makes the plugin claim no feeds and log why
  once; it never breaks a restore.

### Without Nix

```sh
dotnet publish src/nuget-plugin-op -c Release -o ~/.nuget/plugins/netcore/nuget-plugin-op
dotnet nuget locals plugins-cache --clear
```

The folder name has to match the dll name. On NuGet 6.13 and later, `NUGET_NETCORE_PLUGIN_PATHS` or `NUGET_PLUGIN_PATHS`
can also point at the dll, but setting either one turns off convention-folder discovery for every plugin.

## Choosing `op://` or `gh://`

| | `op://` (1Password) | `gh://github.com` (GitHub CLI) |
|---|---|---|
| Token | A classic PAT you create with only `read:packages`. | `gh`'s own OAuth token, which needs `read:packages` added. |
| Scope | As narrow as GitHub allows. | Broad: whatever `gh` holds, typically `repo`, `read:org`, `gist`. |
| Reading it | `op read`, behind 1Password's approval dialog, with a session of about 10 minutes. | `gh auth token` hands it out with no prompt to anything running as you. |
| Upkeep | Rotate the PAT. | Nothing to rotate. |

Either way the token stays out of `NuGet.Config`, the environment and agent tool output. The difference is what a
process running as you can obtain deliberately. With `gh://` that is already possible today: any shell can run
`gh auth token`. Use `op://` if you want a narrow token behind an approval step.

For `gh://`, add the scope once: `gh auth refresh --hostname github.com --scopes read:packages`. That works against
GitHub Packages' NuGet registry; we tested it in November 2026.

### GitHub Packages and token types

- **NuGet registry:** GitHub's docs say it only accepts a **classic PAT** (`read:packages` is enough to restore).
  The GitHub CLI's OAuth token with `read:packages` works as well.
- **Fine-grained PATs:** they **don't** work with GitHub Packages. GitHub lists this as a known limitation, and
  github/roadmap#558 is open with no date.
- **SAML SSO:** if the organization uses it, authorize the token for the organization.

## Migrating from a cleartext token

1. Pick a source. Either put the token in 1Password (`op://…`), or run
   `gh auth refresh --hostname github.com --scopes read:packages` (`gh://github.com`).
2. In `~/.nuget/NuGet/NuGet.Config`, delete the source's `packageSourceCredentials` block. Keep the source in
   `packageSources`.
3. Enable the home-manager module with that source and rebuild. The module clears the plugins cache.
4. If you installed some other way, run `dotnet nuget locals plugins-cache --clear`.
5. Check with `dotnet restore --interactive` once, then restore from your IDE.
6. Rotate the old token: it sat in cleartext.
7. Confirm it's gone: `grep -r '<old token>' ~/.nuget ~/.config <your repos>` should find nothing.

## Non-interactive restores

IDE restores and plain `dotnet restore` tell the plugin they are non-interactive.

- **`"prompt"` (the default).** The plugin still runs `op read`, so 1Password's desktop dialog can appear. That's
  what makes IDE restores work. A long wait for approval is fine: the plugin keeps NuGet's 5 s request timeout alive
  with progress messages.
- **`"skip"`.** For headless machines. When the restore is non-interactive, the plugin first runs `op whoami`. If op
  isn't authorized, it answers "not found" with the message *1Password is locked; run an interactive restore
  (`dotnet restore --interactive`) or unlock op*. The 1Password docs don't say whether `op whoami` can open the
  approval dialog, so check that on your setup.

`gh://` sources never prompt, so the setting has no effect on them.

## Troubleshooting

- **Watch the plugin.** `dotnet restore -v detailed` shows plugin discovery
  (`Using …/nuget-plugin-op.dll as a credential provider plugin`) and the plugin's `[nuget-plugin-op]` log lines.
- **Plugin not asked, or feeds changed but nothing happens.** NuGet caches each plugin's claims for 30 days in
  `~/.local/share/NuGet/plugins-cache` (or `NUGET_PLUGINS_CACHE_PATH`). Run
  `dotnet nuget locals plugins-cache --clear`. The home-manager module does this on every change.
- **`warning : Your request could not be authenticated by the GitHub Packages service` once per restore.** This is
  NuGet echoing the 401 from its first, anonymous request, before it asks credential providers. The restore
  continues with the plugin's credentials. It's harmless.
- **`op` or `gh` not found from the IDE.** A desktop-launched IDE often has a different `PATH`. Set `opPath` or
  `ghPath`.
- **No 1Password prompt appears.** Turn on *Settings → Developer → Integrate with 1Password CLI* in the desktop app.
  On NixOS, `op` must be the setgid wrapper at `/run/wrappers/bin/op`
  (`programs._1password.enable` and `programs._1password-gui.enable`).
- **Restore stalls, then fails with "timed out".** Nobody answered the approval dialog within `timeoutSeconds`.

## Development

```sh
nix develop          # .NET 10 SDK and gh; op comes from the system
dotnet test          # unit, protocol (in and out of process) and end-to-end restore tests
nix flake check      # builds the package and checks the home-manager module
nix build            # result/lib/nuget-plugin-op/nuget-plugin-op.dll
```

- **Tests** use fake `op` and `gh` shell scripts, so they never touch your real accounts. The end-to-end test runs
  `dotnet restore` against a local feed that requires basic auth, with a `NuGet.Config` that has no credentials.
- **Testing another build.** `NUGET_PLUGIN_OP_TEST_PLUGIN_PATH=/abs/path/to/nuget-plugin-op.dll dotnet test`
  points the out-of-process and end-to-end tests at that build, for example the Nix build.
- **After changing package references,** regenerate the Nix lock file:
  `nix build .#default.fetch-deps -o fetch-deps && ./fetch-deps nix/deps.json`.
- **Why no Native AOT.** NuGet runs convention-folder plugins as `dotnet <dll>`, and NuGet.Protocol serializes with
  Newtonsoft.Json, which uses reflection. The project stays portable; the Nix package adds the RID and ReadyToRun at
  publish time for faster startup.
