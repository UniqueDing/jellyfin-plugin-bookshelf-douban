# Jellyfin 豆瓣书架插件

<p align="center">
<img alt="Jellyfin Logo Banner" src="https://raw.githubusercontent.com/jellyfin/jellyfin-ux/master/branding/SVG/banner-logo-solid.svg?sanitize=true"/>
</p>

## 关于

Jellyfin 豆瓣书架插件是一个 Jellyfin 图书元数据插件，用于从豆瓣获取图书信息。

这个版本已经精简为仅支持豆瓣。Google Books、Comic Vine、漫画压缩包解析、EPUB OPF 解析以及原 Bookshelf 插件中的相关旧功能都已移除。

插件可以导入：

- 标题和副标题
- 作者和译者
- 出版社和出版年份
- 简介/描述
- 标签和类型
- 豆瓣评分
- 豆瓣条目 ID
- ISBN
- 主封面图片
- 外部豆瓣链接

## 当前限制

豆瓣可能会拦截服务端请求，并重定向到 `sec.douban.com`，或返回 HTTP `403 Forbidden`。出现这种情况时，Jellyfin 会显示空的搜索结果，因为插件无法解析真实的豆瓣页面。

插件会明确记录这类情况，例如：

```text
Douban blocked request or returned a security challenge: ... (Forbidden) ChallengeUrl=https://sec.douban.com/...
```

如果 DebugRunner 也显示同样的信息，问题出在当前服务器到豆瓣的网络路径，而不是 Jellyfin 的匹配逻辑。

## 防封禁设置

插件在 Jellyfin 控制台 -> 插件 -> Douban Bookshelf 中提供可选的豆瓣防封禁设置。

- 启用豆瓣防封禁：降低豆瓣请求频率。未配置 Cookie 时，请求间隔约 5 秒；配置豆瓣 Cookie 后，请求间隔约 3 秒。
- 豆瓣 Cookies：可选。填入从已登录浏览器中复制的 Cookie，例如 `bid=...; dbcl2=...`。插件会在请求豆瓣时携带这些 Cookie，并在插件配置变更后自动刷新。

启用防封禁后，客户端还会尝试处理豆瓣 `sec.douban.com` 的 SHA-512 nonce 安全挑战，并在校验后重试原始请求一次。这个功能可以降低临时风控影响，但如果服务器 IP 已被豆瓣封禁，或豆瓣要求交互式验证码/登录，它不能保证恢复访问。

## 项目结构

```text
.
├── Jellyfin.Plugin.DoubanBookshelf/        # 运行时插件源码
├── tests/Jellyfin.Plugin.DoubanBookshelf.Tests/  # 单元测试和 HTML fixture
├── tools/DoubanDebugRunner/               # 用于真实豆瓣请求的本地 CLI
├── scripts/package-plugin.sh              # 构建可安装插件 zip
├── build.yaml                             # 插件包元数据
├── Directory.Build.props                  # 共享版本元数据
├── Jellyfin.Plugin.DoubanBookshelf.sln    # 解决方案文件
├── jellyfin.ruleset                       # 分析器规则配置
├── shell.nix                              # 可复现的本地 .NET shell
└── LICENSE                                # 许可证
```

## 运行时源码

### `Jellyfin.Plugin.DoubanBookshelf/`

主 Jellyfin 插件项目。

- `Jellyfin.Plugin.DoubanBookshelf.csproj` 定义运行时程序集、目标框架、分析器和 NuGet 依赖。
- `Plugin.cs` 是 Jellyfin 插件入口，设置插件名称和稳定的插件 GUID。
- `PluginServiceRegistrator.cs` 注册 Provider 使用的共享服务，目前包括 `DoubanBookParser` 和 `DoubanClient`。
- `Configuration/PluginConfiguration.cs` 保存防封禁相关设置，例如请求限速和可选豆瓣 Cookie。
- `Configuration/configPage.html` 是 Jellyfin 控制台中的插件配置页。
- `Properties/AssemblyInfo.cs` 将内部成员暴露给测试程序集。

### `Common/`

文件名和查找辅助代码。

- `BookFileNameParser.cs` 将 Jellyfin 图书名称解析为标题、系列、序号、年份和末尾的豆瓣 ID，并会对名称进行归一化以便匹配。
- `BookFileNameParserResult.cs` 是解析器返回的结果模型。

### `Providers/Douban/`

所有豆瓣相关的 Provider 代码。

- `DoubanConstants.cs` 保存 `Douban` 和 `ISBN` 等 Provider ID。
- `DoubanUrls.cs` 保存豆瓣搜索页和图书详情页的 URL 格式。
- `DoubanBook.cs` 是从豆瓣 HTML 中解析出的内部元数据模型。
- `DoubanBookParser.cs` 使用 HtmlAgilityPack 解析豆瓣搜索结果页和图书详情页。
- `DoubanClient.cs` 负责向豆瓣发送 HTTP 请求，维护轻量级会话 Cookie，发送类似浏览器的请求头，应用可选防封禁限速，并记录豆瓣安全拦截。
- `DoubanBooksProvider.cs` 实现 `IRemoteMetadataProvider<Book, BookInfo>`，为 Jellyfin 图书的识别、搜索和元数据刷新提供能力。
- `DoubanBooksImageProvider.cs` 实现 `IRemoteImageProvider`，为已有豆瓣 ID 的图书提供主封面图片。
- `DoubanExternalId.cs` 在 Jellyfin 中添加 `Douban` 外部 ID 字段。
- `DoubanExternalUrlProvider.cs` 为已识别的图书添加外部豆瓣页面链接。

## 测试

### `tests/Jellyfin.Plugin.DoubanBookshelf.Tests/`

单元测试覆盖解析、搜索结果创建、元数据映射、图片 Provider 行为以及豆瓣被拦截时的响应处理。

- `BookFileNameParserTests.cs` 验证文件名解析和末尾豆瓣 ID 处理。
- `DoubanBookParserTests.cs` 验证豆瓣 HTML 解析。
- `DoubanBooksProviderTests.cs` 验证搜索结果、元数据映射、Provider ID、拦截响应处理、请求头和 Cookie。
- `DoubanBooksImageProviderTests.cs` 验证封面图片 Provider 行为。
- `Http/MockHttpMessageHandler.cs` 和 `Http/MockHttpResponse.cs` 为测试提供确定性的伪 HTTP 响应。
- `Fixtures/douban-book-search.html` 和 `Fixtures/douban-book-detail.html` 是解析器和 Provider 测试使用的豆瓣 HTML 样本。
- `TestHelpers.cs` 用于加载 fixture 文件。
- `Usings.cs` 包含共享测试导入。
- `Jellyfin.Plugin.DoubanBookshelf.Tests.csproj` 定义测试项目和依赖。

## Debug Runner

### `tools/DoubanDebugRunner/`

这是一个小型本地 CLI，用于在不启动 Jellyfin 的情况下测试豆瓣客户端。

示例：

```bash
dotnet run --project tools/DoubanDebugRunner -- --douban-id 26912767
dotnet run --project tools/DoubanDebugRunner -- --isbn 9787111544937
dotnet run --project tools/DoubanDebugRunner -- --title 深入理解计算机系统
```

Runner 会执行真实 HTTP 请求，并以 JSON 输出解析后的元数据。它适合用来检查当前机器是否能访问豆瓣。

## 构建和测试

如果使用 Nix，可以通过 `nix-shell --run` 进入项目提供的 shell：

```bash
nix-shell --run 'dotnet test "Jellyfin.Plugin.DoubanBookshelf.sln" --configuration Release --logger "console;verbosity=minimal"'
nix-shell --run 'dotnet build "Jellyfin.Plugin.DoubanBookshelf.sln" --configuration Release --no-restore'
```

不使用 Nix 时，请安装 .NET 9 SDK，并直接运行相同的 `dotnet` 命令。

## 测试工具

- 单元测试：`nix-shell --run 'dotnet test "Jellyfin.Plugin.DoubanBookshelf.sln" --configuration Release --logger "console;verbosity=minimal"'`。用于验证文件名解析、豆瓣 HTML 解析、元数据映射、图片 Provider、配置 Cookie 以及拦截响应处理。
- DebugRunner：`dotnet run --project tools/DoubanDebugRunner -- --douban-id <id>`、`--isbn <isbn>` 或 `--title <title>`。它会执行真实豆瓣请求，并以 JSON 输出解析出的图书；退出码 `0` 表示至少解析到一本书，`1` 表示无结果，`2` 表示参数无效或构建/运行环境失败。
- 打包校验：`nix-shell --run './scripts/package-plugin.sh'` 会构建 `dist/Douban-Bookshelf-0.1.0.0.zip` 和对应 `.sha256`。可以用 `unzip -l dist/Douban-Bookshelf-0.1.0.0.zip` 确认发布 zip 只包含 `Jellyfin.Plugin.DoubanBookshelf.dll`。
- Manifest 校验：`GITHUB_REPOSITORY='uniqueding/jellyfin-plugin-bookshelf-douban' python3 ./scripts/generate_manifest.py ./dist/Douban-Bookshelf-0.1.0.0.zip v0.1.0` 会在本地生成 `manifest.json` 供检查。检查后应删除该文件，因为 release workflow 会把它作为发布产物上传。

## 打包

创建可安装的插件 zip：

```bash
nix-shell --run './scripts/package-plugin.sh'
```

打包脚本会读取 `build.yaml`，发布插件，并创建：

```text
dist/Douban-Bookshelf-0.1.0.0.zip
dist/Douban-Bookshelf-0.1.0.0.zip.sha256
```

zip 中应只包含：

```text
Jellyfin.Plugin.DoubanBookshelf.dll
```

发布 Release 时还会生成 `manifest.json`。这是 Jellyfin 插件仓库清单，用于描述插件名称、GUID、版本、目标 ABI、安装包 URL、校验和以及更新日志。workflow 会把它发布到固定的 `manifest` release tag。

## 安装

正式发布后，可以在 Jellyfin 控制台中添加这个插件仓库 URL：

```text
https://github.com/uniqueding/jellyfin-plugin-bookshelf-douban/releases/download/manifest/manifest.json
```

手动安装时：

1. 解压 `dist/Douban-Bookshelf-0.1.0.0.zip`。
2. 将解压后的文件复制到 Jellyfin 插件目录，例如：

```text
plugins/Douban Bookshelf/
```

3. 重启 Jellyfin。
4. 在 Jellyfin 控制台中，为图书媒体库启用 `Douban` 元数据抓取器。

如果能看到 `Douban ID` 字段但搜索结果为空，请检查媒体库元数据抓取器中是否已启用 `Douban`。Jellyfin 会分别过滤外部 ID 字段和元数据搜索 Provider。

## 使用建议

- 准确率最高：直接使用精确的豆瓣条目 ID 进行识别。
- 准确率较好：使用干净的图书文件名，例如 `百年孤独.epub`。
- 避免在文件名中包含来源站点、质量标签或营销描述等额外词语。
- 如果文件名以 6 到 9 位数字结尾，插件可能会将其视为豆瓣条目 ID。
- 如果自动抓取匹配到错误图书，建议手动使用豆瓣 ID 识别。

## 故障排查

### 搜索结果为空

按顺序检查：

1. 图书媒体库已启用 `Douban` 元数据抓取器。
2. 插件已安装，并在 Jellyfin 重启后处于启用状态。
3. 服务器所在网络可以访问豆瓣。
4. DebugRunner 没有显示 `sec.douban.com` 安全挑战。

### DebugRunner 返回 `Forbidden` 或 `sec.douban.com`

豆瓣正在拦截当前网络路径。插件会清晰记录该情况，但无法自行绕过豆瓣的外部安全挑战。

可选处理方式：

- 稍后重试。
- 为 Jellyfin 服务器使用不同的网络路径。
- 在详情页仍可访问时，通过已知豆瓣 ID 进行识别。
- 未来增加 Google Books 或 Open Library 等备用 Provider。

### 搜索结果封面异常，但保存后的封面正常

Jellyfin 搜索结果使用直接的 `RemoteSearchResult.ImageUrl`。保存后的封面使用 Jellyfin 的图片 Provider 路径。豆瓣图片防盗链限制可能会对这两种路径产生不同影响。

### 缺少作者/人物图片

本插件当前只导入作者和译者名称，不实现人物元数据或人物图片 Provider。
