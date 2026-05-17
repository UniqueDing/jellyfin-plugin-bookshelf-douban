<h1 align="center">Jellyfin Douban Bookshelf Plugin</h1>

<p align="center">
<img alt="Jellyfin Logo Banner" src="https://raw.githubusercontent.com/jellyfin/jellyfin-ux/master/branding/SVG/banner-logo-solid.svg?sanitize=true"/>
</p>

## About

Jellyfin Douban Bookshelf is a Jellyfin Books metadata plugin that fetches book information from Douban.

The plugin can import:

- Title and subtitle
- Authors and translators
- Publisher and publication year
- Overview/description
- Tags and genre
- Douban rating
- Douban subject ID
- ISBN
- Primary cover image
- External Douban link

## Install

Add one of these plugin repository URLs in Jellyfin Dashboard:

```text
https://github.com/uniqueding/jellyfin-plugin-bookshelf-douban/releases/download/manifest/manifest.json
```

Chinese mirror:

```text
https://ghfast.top/https://github.com/uniqueding/jellyfin-plugin-bookshelf-douban/releases/download/manifest/manifest_cn.json
```

Manual installation:

1. Extract `dist/Douban-Bookshelf-0.1.0.0.zip`.
2. Copy the extracted file into a Jellyfin plugin directory such as:

```text
plugins/Douban Bookshelf/
```

3. Restart Jellyfin.
4. In Jellyfin Dashboard, enable the `Douban` metadata fetcher for the Books library.

If the `Douban ID` field appears but search is empty, check whether `Douban` is enabled in the library metadata fetchers. The external ID field and the metadata search provider are filtered separately by Jellyfin.

## Current Limitation

Douban may block server-side requests and redirect to `sec.douban.com` or return HTTP `403 Forbidden`. When that happens, Jellyfin will show empty search results because the plugin cannot parse a real Douban page.

The plugin logs these cases explicitly, for example:

```text
Douban blocked request or returned a security challenge: ... (Forbidden) ChallengeUrl=https://sec.douban.com/...
```

If the DebugRunner shows the same message, the problem is the current network path to Douban, not Jellyfin matching logic.

## Anti-Blocking Settings

The plugin includes optional Douban anti-blocking settings in Jellyfin Dashboard -> Plugins -> Douban Bookshelf.

- Enable Douban anti-blocking: slows Douban requests to a safer pace. Anonymous requests are spaced by about 5 seconds; requests with configured Douban cookies are spaced by about 3 seconds.
- Douban Cookies: optional cookies copied from a logged-in browser session, for example `bid=...; dbcl2=...`. These cookies are sent with Douban requests and are refreshed when plugin configuration changes.

When anti-blocking is enabled, the client also tries to solve Douban's `sec.douban.com` SHA-512 nonce challenge and retries the original request once. This can reduce temporary scraping blocks, but it cannot guarantee access if Douban has blocked the server IP or requires an interactive CAPTCHA/login flow.

## Project Layout

```text
.
├── Jellyfin.Plugin.DoubanBookshelf/        # Runtime plugin source
├── tests/Jellyfin.Plugin.DoubanBookshelf.Tests/  # Unit tests and HTML fixtures
├── tools/DoubanDebugRunner/               # Local CLI for real Douban requests
├── scripts/package-plugin.sh              # Builds the installable plugin zip
├── build.yaml                             # Plugin package metadata
├── Directory.Build.props                  # Shared version metadata
├── Jellyfin.Plugin.DoubanBookshelf.sln    # Solution file
├── jellyfin.ruleset                       # Analyzer rule configuration
├── shell.nix                              # Reproducible local .NET shell
└── LICENSE                                # License
```

## Runtime Source

### `Jellyfin.Plugin.DoubanBookshelf/`

Main Jellyfin plugin project.

- `Jellyfin.Plugin.DoubanBookshelf.csproj` defines the runtime assembly, target framework, analyzers, and NuGet dependencies.
- `Plugin.cs` is the Jellyfin plugin entrypoint. It sets the plugin name and stable plugin GUID.
- `PluginServiceRegistrator.cs` registers shared services used by provider classes, currently `DoubanBookParser` and `DoubanClient`.
- `Configuration/PluginConfiguration.cs` stores anti-blocking settings such as request pacing and optional Douban cookies.
- `Configuration/configPage.html` is the Jellyfin Dashboard configuration page for these settings.
- `Properties/AssemblyInfo.cs` exposes internal members to the test assembly.

### `Common/`

Filename and lookup helper code.

- `BookFileNameParser.cs` parses Jellyfin book names into title, series, index, year, and trailing Douban ID. It also normalizes names for matching.
- `BookFileNameParserResult.cs` is the parsed result model returned by the parser.

### `Providers/Douban/`

All Douban-specific provider code.

- `DoubanConstants.cs` stores provider IDs such as `Douban` and `ISBN`.
- `DoubanUrls.cs` stores Douban URL formats for search and book detail pages.
- `DoubanBook.cs` is the internal metadata model parsed from Douban HTML.
- `DoubanBookParser.cs` uses HtmlAgilityPack to parse Douban search result pages and book detail pages.
- `DoubanClient.cs` performs HTTP requests to Douban, keeps lightweight session cookies, sends browser-like headers, applies optional anti-blocking pacing, and logs Douban security blocks.
- `DoubanBooksProvider.cs` implements `IRemoteMetadataProvider<Book, BookInfo>`. It powers Jellyfin Identify/search and metadata refresh for books.
- `DoubanBooksImageProvider.cs` implements `IRemoteImageProvider`. It provides the primary cover image for books that already have a Douban ID.
- `DoubanExternalId.cs` adds the `Douban` external ID field in Jellyfin.
- `DoubanExternalUrlProvider.cs` adds the external Douban page link for identified books.

## Tests

### `tests/Jellyfin.Plugin.DoubanBookshelf.Tests/`

Unit tests for parsing, search result creation, metadata mapping, image provider behavior, and blocked Douban responses.

- `BookFileNameParserTests.cs` verifies filename parsing and trailing Douban ID handling.
- `DoubanBookParserTests.cs` verifies Douban HTML parsing.
- `DoubanBooksProviderTests.cs` verifies search results, metadata mapping, provider IDs, blocked-response handling, headers, and cookies.
- `DoubanBooksImageProviderTests.cs` verifies cover image provider behavior.
- `Http/MockHttpMessageHandler.cs` and `Http/MockHttpResponse.cs` provide deterministic fake HTTP responses for tests.
- `Fixtures/douban-book-search.html` and `Fixtures/douban-book-detail.html` are saved Douban HTML samples used by parser/provider tests.
- `TestHelpers.cs` loads fixture files.
- `Usings.cs` contains shared test imports.
- `Jellyfin.Plugin.DoubanBookshelf.Tests.csproj` defines the test project and dependencies.

## Debug Runner

### `tools/DoubanDebugRunner/`

Small local CLI for testing the Douban client without starting Jellyfin.

Examples:

```bash
dotnet run --project tools/DoubanDebugRunner -- --douban-id 26912767
dotnet run --project tools/DoubanDebugRunner -- --isbn 9787111544937
dotnet run --project tools/DoubanDebugRunner -- --title 深入理解计算机系统
```

The runner performs real HTTP requests and prints parsed metadata as JSON. It is useful for checking whether Douban is reachable from the current machine.

## Build And Test

If you use Nix, enter the provided shell implicitly with `nix-shell --run`:

```bash
nix-shell --run 'dotnet test "Jellyfin.Plugin.DoubanBookshelf.sln" --configuration Release --logger "console;verbosity=minimal"'
nix-shell --run 'dotnet build "Jellyfin.Plugin.DoubanBookshelf.sln" --configuration Release --no-restore'
```

Without Nix, use a .NET 9 SDK and run the same `dotnet` commands directly.

## Test Tools

- Unit tests: `nix-shell --run 'dotnet test "Jellyfin.Plugin.DoubanBookshelf.sln" --configuration Release --logger "console;verbosity=minimal"'`. These verify filename parsing, Douban HTML parsing, metadata mapping, image provider behavior, configured cookies, and blocked-response handling.
- DebugRunner: `dotnet run --project tools/DoubanDebugRunner -- --douban-id <id>`, `--isbn <isbn>`, or `--title <title>`. It performs real Douban requests and prints parsed books as JSON; exit code `0` means at least one book was parsed, exit code `1` means no result, and exit code `2` means invalid arguments or build/runtime setup failure.
- Package verification: `nix-shell --run './scripts/package-plugin.sh'` builds `dist/Douban-Bookshelf-0.1.0.0.zip` and its `.sha256`. Use `unzip -l dist/Douban-Bookshelf-0.1.0.0.zip` to confirm the release zip contains only `Jellyfin.Plugin.DoubanBookshelf.dll`.
- Manifest verification: after packaging, run `GITHUB_REPOSITORY='uniqueding/jellyfin-plugin-bookshelf-douban' python3 ./scripts/generate_manifest.py ./dist/Douban-Bookshelf-0.1.0.0.zip v0.1.0` to generate `manifest.json` and `manifest_cn.json` locally.

## Package

Create an installable plugin zip:

```bash
nix-shell --run './scripts/package-plugin.sh'
```

The package script reads `build.yaml`, publishes the plugin, and creates:

```text
dist/Douban-Bookshelf-0.1.0.0.zip
dist/Douban-Bookshelf-0.1.0.0.zip.sha256
```

The zip should contain exactly:

```text
Jellyfin.Plugin.DoubanBookshelf.dll
```

The release process also publishes `manifest.json` and `manifest_cn.json`. These Jellyfin plugin repository manifests describe the plugin name, GUID, version, target ABI, package URL, checksum, and changelog.

## Usage Tips

- Best accuracy: identify by exact Douban subject ID.
- Good accuracy: use clean book filenames, for example `百年孤独.epub`.
- Avoid extra words in filenames such as source site names, quality labels, or marketing descriptions.
- If a filename ends with a 6-9 digit number, the plugin may treat it as a Douban subject ID.
- If automatic scraping picks wrong books, prefer manual Identify with a Douban ID.

## Troubleshooting

### Search results are empty

Check these in order:

1. The Books library has the `Douban` metadata fetcher enabled.
2. The plugin is installed and active after Jellyfin restart.
3. The server can access Douban from its own network.
4. The DebugRunner does not show a `sec.douban.com` security challenge.

### DebugRunner returns `Forbidden` or `sec.douban.com`

Douban is blocking the current network path. The plugin logs this clearly but cannot solve Douban's external security challenge by itself.

Possible options:

- Try again later.
- Use a different network path for the Jellyfin server.
- Identify by known Douban ID when detail pages are still reachable.
- Add a future fallback provider such as Google Books or Open Library.

### Search result cover looks broken but saved cover works

Jellyfin search results use the direct `RemoteSearchResult.ImageUrl`. Saved covers use Jellyfin's image provider path. Douban image hotlink restrictions can affect these paths differently.

### Author/person images are missing

This plugin currently imports author and translator names only. It does not implement a person metadata or person image provider.
