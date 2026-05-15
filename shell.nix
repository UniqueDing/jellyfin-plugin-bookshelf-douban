{ pkgs ? import <nixpkgs> { } }:

let
  dotnetSdk =
    if pkgs ? dotnet-sdk_9 then
      pkgs.dotnet-sdk_9
    else
      pkgs.dotnetCorePackages.sdk_9_0;
in
pkgs.mkShell {
  packages = [
    dotnetSdk
    pkgs.git
  ];

  DOTNET_CLI_TELEMETRY_OPTOUT = "1";
  DOTNET_NOLOGO = "1";
  DOTNET_ROOT = "${dotnetSdk}";
  TMPDIR = "/tmp";
}
