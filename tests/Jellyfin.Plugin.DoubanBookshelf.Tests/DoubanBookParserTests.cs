using Jellyfin.Plugin.DoubanBookshelf.Providers.Douban;

namespace Jellyfin.Plugin.DoubanBookshelf.Tests;

public class DoubanBookParserTests
{
    [Fact]
    public void ParseSearchResults_WithRedirectUrl_ReturnsSubjectUrl()
    {
        var parser = new DoubanBookParser();

        var results = parser.ParseSearchResults(TestHelpers.GetFixture("douban-book-search.html"), 5);

        Assert.Collection(results, result => Assert.Equal("https://book.douban.com/subject/26912767/", result));
    }

    [Fact]
    public void ParseBook_WithDetailHtml_ReturnsMetadata()
    {
        var parser = new DoubanBookParser();

        var book = parser.ParseBook("https://book.douban.com/subject/26912767/", TestHelpers.GetFixture("douban-book-detail.html"));

        Assert.NotNull(book);
        Assert.Equal("26912767", book.Id);
        Assert.Equal("深入理解计算机系统 (第3版):原书第3版", book.Title);
        Assert.Equal("https://book.douban.com/subject/26912767/", book.Url);
        Assert.Equal("https://img1.doubanio.com/view/subject/l/public/s29195878.jpg", book.CoverUrl);
        Assert.Equal(9.7F, book.Rating);
        Assert.Collection(
            book.Authors,
            first => Assert.Equal("Randal E. Bryant", first),
            second => Assert.Equal("David O'Hallaron", second));
        Assert.Collection(
            book.Translators,
            first => Assert.Equal("龚奕利", first),
            second => Assert.Equal("贺莲", second));
        Assert.Equal("机械工业出版社", book.Publisher);
        Assert.Equal("2016-12", book.PublishedDate);
        Assert.Equal("9787111544937", book.Isbn);
        Assert.Equal("计算机科学丛书", book.Series);
        Assert.Equal("<p>和第2版相比，本版内容上最大的变化是转变为完全以x86-64为基础。</p>", book.Description);
        Assert.Collection(
            book.Tags,
            first => Assert.Equal("计算机", first),
            second => Assert.Equal("计算机科学", second),
            third => Assert.Equal("CSAPP", third));
        Assert.Equal("zh", book.Language);
    }
}
