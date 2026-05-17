using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.DoubanBookshelf.Common;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;

namespace Jellyfin.Plugin.DoubanBookshelf.Providers.Douban;

/// <summary>
/// Douban Books metadata provider.
/// </summary>
public class DoubanBooksProvider : IRemoteMetadataProvider<Book, BookInfo>
{
    private readonly DoubanClient _doubanClient;

    /// <summary>
    /// Initializes a new instance of the <see cref="DoubanBooksProvider"/> class.
    /// </summary>
    /// <param name="doubanClient">The Douban Books client.</param>
    public DoubanBooksProvider(DoubanClient doubanClient)
    {
        _doubanClient = doubanClient;
    }

    /// <inheritdoc />
    public string Name => DoubanConstants.ProviderName;

    /// <inheritdoc />
    public async Task<IEnumerable<RemoteSearchResult>> GetSearchResults(BookInfo searchInfo, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var doubanId = searchInfo.GetProviderId(DoubanConstants.ProviderId);
        if (string.IsNullOrWhiteSpace(doubanId))
        {
            doubanId = BookFileNameParser.GetTrailingDoubanId(searchInfo.Name);
        }

        if (!string.IsNullOrWhiteSpace(doubanId))
        {
            var book = await _doubanClient.GetBookById(doubanId, cancellationToken).ConfigureAwait(false);
            return book is null ? [] : [CreateSearchResult(book)];
        }

        var query = GetSearchString(searchInfo);
        var results = await _doubanClient.SearchBooks(query, cancellationToken).ConfigureAwait(false);
        return results.Select(CreateSearchResult).ToList();
    }

    /// <inheritdoc />
    public async Task<MetadataResult<Book>> GetMetadata(BookInfo info, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var metadataResult = new MetadataResult<Book>
        {
            QueriedById = true
        };

        var doubanId = info.GetProviderId(DoubanConstants.ProviderId);
        if (string.IsNullOrWhiteSpace(doubanId))
        {
            doubanId = BookFileNameParser.GetTrailingDoubanId(info.Name);
        }

        if (string.IsNullOrWhiteSpace(doubanId))
        {
            doubanId = await FetchBookId(info, cancellationToken).ConfigureAwait(false);
            metadataResult.QueriedById = false;
        }

        if (string.IsNullOrWhiteSpace(doubanId))
        {
            return metadataResult;
        }

        var doubanBook = await _doubanClient.GetBookById(doubanId, cancellationToken).ConfigureAwait(false);
        if (doubanBook is null)
        {
            return metadataResult;
        }

        metadataResult.Item = ProcessBookData(doubanBook, cancellationToken);
        ProcessBookMetadata(metadataResult, doubanBook);
        metadataResult.HasMetadata = true;

        return metadataResult;
    }

    /// <inheritdoc />
    public Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken cancellationToken)
    {
        return _doubanClient.GetImageResponse(url, cancellationToken);
    }

    /// <summary>
    /// Get the search string for the item.
    /// </summary>
    /// <param name="item">BookInfo item.</param>
    /// <returns>The search query.</returns>
    internal string GetSearchString(BookInfo item)
    {
        var isbn = item.GetProviderId(DoubanConstants.IsbnProviderId);
        if (!string.IsNullOrWhiteSpace(isbn))
        {
            return isbn;
        }

        var parsedItem = BookFileNameParser.GetBookMetadata(item);
        string result = string.Empty;
        if (!string.IsNullOrWhiteSpace(parsedItem.Name))
        {
            result = parsedItem.Name;
        }
        else if (!string.IsNullOrWhiteSpace(parsedItem.SeriesName))
        {
            result = parsedItem.SeriesName;
        }

        if (parsedItem.Year.HasValue)
        {
            result = string.IsNullOrWhiteSpace(result) ? parsedItem.Year.Value.ToString(CultureInfo.InvariantCulture) : $"{result} {parsedItem.Year.Value}";
        }

        return result;
    }

    private async Task<string?> FetchBookId(BookInfo item, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var searchResults = await _doubanClient.SearchBooks(GetSearchString(item), cancellationToken).ConfigureAwait(false);
        if (searchResults.Count == 0)
        {
            return null;
        }

        var comparableName = BookFileNameParser.GetComparableString(BookFileNameParser.GetBookMetadata(item).Name, true);
        if (string.IsNullOrWhiteSpace(comparableName))
        {
            return searchResults[0].Id;
        }

        foreach (var result in searchResults)
        {
            var comparableResultName = BookFileNameParser.GetComparableString(result.Title, true);
            if (comparableResultName.Equals(comparableName, StringComparison.Ordinal))
            {
                return result.Id;
            }
        }

        return searchResults[0].Id;
    }

    private static RemoteSearchResult CreateSearchResult(DoubanBook book)
    {
        var remoteSearchResult = new RemoteSearchResult
        {
            SearchProviderName = DoubanConstants.ProviderName,
            Name = book.Title,
            Overview = WebUtility.HtmlDecode(book.Description),
            ProductionYear = GetYearFromPublishedDate(book.PublishedDate)
        };
        remoteSearchResult.SetProviderId(DoubanConstants.ProviderId, book.Id);
        if (!string.IsNullOrWhiteSpace(book.Isbn))
        {
            remoteSearchResult.SetProviderId(DoubanConstants.IsbnProviderId, book.Isbn);
        }

        return remoteSearchResult;
    }

    private static Book ProcessBookData(DoubanBook doubanBook, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var book = new Book
        {
            Name = doubanBook.Title,
            Overview = WebUtility.HtmlDecode(doubanBook.Description),
            ProductionYear = GetYearFromPublishedDate(doubanBook.PublishedDate),
            CommunityRating = doubanBook.Rating
        };

        book.SetProviderId(DoubanConstants.ProviderId, doubanBook.Id);
        if (!string.IsNullOrWhiteSpace(doubanBook.Isbn))
        {
            book.SetProviderId(DoubanConstants.IsbnProviderId, doubanBook.Isbn);
        }

        if (!string.IsNullOrWhiteSpace(doubanBook.Publisher))
        {
            book.AddStudio(doubanBook.Publisher);
        }

        if (doubanBook.Tags.Count > 0)
        {
            book.AddGenre(doubanBook.Tags[0]);
            foreach (var tag in doubanBook.Tags.Skip(1))
            {
                book.AddTag(tag);
            }
        }

        return book;
    }

    private static void ProcessBookMetadata(MetadataResult<Book> metadataResult, DoubanBook doubanBook)
    {
        foreach (var author in doubanBook.Authors)
        {
            metadataResult.AddPerson(new PersonInfo
            {
                Name = author,
                Type = PersonKind.Author
            });
        }

        foreach (var translator in doubanBook.Translators)
        {
            metadataResult.AddPerson(new PersonInfo
            {
                Name = translator,
                Type = PersonKind.Translator
            });
        }

        if (!string.IsNullOrWhiteSpace(doubanBook.Language))
        {
            metadataResult.ResultLanguage = doubanBook.Language;
        }
    }

    private static int? GetYearFromPublishedDate(string? publishedDate)
    {
        var resultYear = publishedDate?.Length > 4 ? publishedDate[..4] : publishedDate;
        if (!int.TryParse(resultYear, NumberStyles.Integer, CultureInfo.InvariantCulture, out var bookReleaseYear))
        {
            return null;
        }

        return bookReleaseYear;
    }
}
