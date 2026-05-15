using System.Text.Json;
using System.Text.Json.Serialization;
using Jellyfin.Plugin.DoubanBookshelf.Providers.Douban;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

var arguments = CliArguments.Parse(args);
if (arguments.ShowHelp)
{
    CliArguments.PrintHelp();
    return 0;
}

if (!arguments.IsValid)
{
    Console.Error.WriteLine("Expected one of --douban-id, --isbn, or --title.");
    CliArguments.PrintHelp();
    return 2;
}

using var services = new ServiceCollection()
    .AddLogging(builder => builder.AddSimpleConsole(options => options.SingleLine = true).SetMinimumLevel(LogLevel.Warning))
    .AddHttpClient()
    .AddSingleton<DoubanBookParser>()
    .AddSingleton<DoubanClient>()
    .BuildServiceProvider();

var client = services.GetRequiredService<DoubanClient>();
IReadOnlyList<DoubanBook> books;
if (!string.IsNullOrWhiteSpace(arguments.DoubanId))
{
    var book = await client.GetBookById(arguments.DoubanId, CancellationToken.None).ConfigureAwait(false);
    books = book is null ? [] : [book];
}
else
{
    books = await client.SearchBooks(arguments.Query, CancellationToken.None).ConfigureAwait(false);
}

var jsonOptions = new JsonSerializerOptions
{
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    WriteIndented = true
};

Console.WriteLine(JsonSerializer.Serialize(books, jsonOptions));
return books.Count > 0 ? 0 : 1;

internal sealed class CliArguments
{
    private CliArguments(string? doubanId, string query, bool showHelp)
    {
        DoubanId = doubanId;
        Query = query;
        ShowHelp = showHelp;
    }

    public string? DoubanId { get; }

    public string Query { get; }

    public bool ShowHelp { get; }

    public bool IsValid => ShowHelp || !string.IsNullOrWhiteSpace(DoubanId) || !string.IsNullOrWhiteSpace(Query);

    public static CliArguments Parse(string[] args)
    {
        string? doubanId = null;
        string? isbn = null;
        string? title = null;
        for (var index = 0; index < args.Length; index++)
        {
            var argument = args[index];
            if (argument is "-h" or "--help")
            {
                return new CliArguments(null, string.Empty, true);
            }

            if (argument is "--douban-id" or "--isbn" or "--title")
            {
                if (index + 1 >= args.Length)
                {
                    return new CliArguments(null, string.Empty, false);
                }

                var value = args[++index];
                if (argument == "--douban-id")
                {
                    doubanId = value;
                }
                else if (argument == "--isbn")
                {
                    isbn = value;
                }
                else
                {
                    title = value;
                }
            }
            else if (string.IsNullOrWhiteSpace(title))
            {
                title = argument;
            }
        }

        return new CliArguments(doubanId, isbn ?? title ?? string.Empty, false);
    }

    public static void PrintHelp()
    {
        Console.WriteLine("""
            Usage:
              dotnet run --project tools/DoubanDebugRunner -- --douban-id <subject-id>
              dotnet run --project tools/DoubanDebugRunner -- --isbn <isbn>
              dotnet run --project tools/DoubanDebugRunner -- --title <book-title>
              dotnet run --project tools/DoubanDebugRunner -- <book-title>

            Examples:
              dotnet run --project tools/DoubanDebugRunner -- --douban-id 26912767
              dotnet run --project tools/DoubanDebugRunner -- --isbn 9787111544937
              dotnet run --project tools/DoubanDebugRunner -- --title 深入理解计算机系统
            """);
    }
}
