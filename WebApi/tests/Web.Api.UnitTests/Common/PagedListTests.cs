using Web.Api.Common;
using Web.Api.UnitTests.Abstractions;

namespace Web.Api.UnitTests.Common;

public sealed class PagedListTests
{
    // The property the whole design turns on: only one page of rows is ever read out of the
    // source. A correct implementation reads the sequence once to count it and then only
    // pageSize elements for the page itself; one that called ToListAsync first and sliced the
    // result afterwards would read all 100 elements twice.
    [Fact]
    public async Task CreateAsync_Should_ReadOnlyOnePageOfRows_NotTheWholeSequence()
    {
        // Arrange
        CountingAsyncQueryable<int> source = CountingAsyncQueryable.Of(Enumerable.Range(1, 100));

        // Act
        PagedList<int> page = await PagedList<int>.CreateAsync(source, page: 1, pageSize: 5);

        // Assert
        page.Items.ShouldBe([1, 2, 3, 4, 5]);
        page.TotalCount.ShouldBe(100);
        source.ElementsRead.ShouldBe(100 + 5);
    }

    [Fact]
    public async Task CreateAsync_Should_SkipTheEarlierPages()
    {
        // Arrange
        CountingAsyncQueryable<int> source = CountingAsyncQueryable.Of(Enumerable.Range(1, 100));

        // Act
        PagedList<int> page = await PagedList<int>.CreateAsync(source, page: 3, pageSize: 10);

        // Assert
        page.Items.ShouldBe([21, 22, 23, 24, 25, 26, 27, 28, 29, 30]);
        page.HasPreviousPage.ShouldBeTrue();
        page.HasNextPage.ShouldBeTrue();
    }

    [Fact]
    public async Task CreateAsync_Should_ReportNoNextPage_OnTheLastPage()
    {
        // Arrange
        CountingAsyncQueryable<int> source = CountingAsyncQueryable.Of(Enumerable.Range(1, 25));

        // Act
        PagedList<int> page = await PagedList<int>.CreateAsync(source, page: 3, pageSize: 10);

        // Assert
        page.Items.Count.ShouldBe(5);
        page.HasNextPage.ShouldBeFalse();
    }

    // Clamping lives in PagedList rather than in each slice so that no endpoint can be written
    // that lets a caller ask the database for the whole table.
    [Fact]
    public async Task CreateAsync_Should_CapThePageSize()
    {
        // Arrange
        CountingAsyncQueryable<int> source = CountingAsyncQueryable.Of(Enumerable.Range(1, 5000));

        // Act
        PagedList<int> page = await PagedList<int>.CreateAsync(source, page: 1, pageSize: int.MaxValue);

        // Assert
        page.PageSize.ShouldBe(PagedList<int>.MaxPageSize);
        page.Items.Count.ShouldBe(PagedList<int>.MaxPageSize);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public async Task CreateAsync_Should_TreatANonPositivePageAsTheFirstOne(int requestedPage)
    {
        // Arrange
        CountingAsyncQueryable<int> source = CountingAsyncQueryable.Of(Enumerable.Range(1, 10));

        // Act
        PagedList<int> page = await PagedList<int>.CreateAsync(source, requestedPage, pageSize: 3);

        // Assert
        page.Page.ShouldBe(1);
        page.Items.ShouldBe([1, 2, 3]);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task CreateAsync_Should_FallBackToTheDefaultPageSize_WhenItIsNonPositive(int requestedPageSize)
    {
        // Arrange
        CountingAsyncQueryable<int> source = CountingAsyncQueryable.Of(Enumerable.Range(1, 100));

        // Act
        PagedList<int> page = await PagedList<int>.CreateAsync(source, page: 1, requestedPageSize);

        // Assert
        page.PageSize.ShouldBe(PagedList<int>.DefaultPageSize);
    }

    [Fact]
    public async Task CreateAsync_Should_ReturnAnEmptyPage_BeyondTheEnd()
    {
        // Arrange
        CountingAsyncQueryable<int> source = CountingAsyncQueryable.Of(Enumerable.Range(1, 10));

        // Act
        PagedList<int> page = await PagedList<int>.CreateAsync(source, page: 99, pageSize: 10);

        // Assert
        page.Items.ShouldBeEmpty();
        page.TotalCount.ShouldBe(10);
        page.HasNextPage.ShouldBeFalse();
    }
}
