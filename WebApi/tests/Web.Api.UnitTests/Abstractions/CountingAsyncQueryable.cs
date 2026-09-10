using System.Collections;
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore.Query;

namespace Web.Api.UnitTests.Abstractions;

// A LINQ-to-objects queryable that behaves enough like an EF one for CountAsync and ToListAsync to
// work, and that counts how many source elements are actually pulled. That count is the only way to
// tell apart a query that pages in the database from one that materialises everything and then
// takes a slice: both return the same page, but the second one reads the whole table to do it.
internal static class CountingAsyncQueryable
{
    public static CountingAsyncQueryable<T> Of<T>(IEnumerable<T> source) => new(source);
}

internal sealed class CountingAsyncQueryable<T> : IQueryable<T>, IAsyncEnumerable<T>
{
    private readonly IEnumerable<T> _source;
    private readonly Counter _counter;

    public CountingAsyncQueryable(IEnumerable<T> source)
    {
        _counter = new Counter();
        _source = Count(source, _counter);
        Provider = new CountingQueryProvider(_source.AsQueryable().Provider);
        Expression = _source.AsQueryable().Expression;
    }

    // How many elements have been read out of the underlying sequence so far, across every
    // operation performed on this queryable.
    public int ElementsRead => _counter.Value;

    public Type ElementType => typeof(T);

    public Expression Expression { get; }

    public IQueryProvider Provider { get; }

    public IEnumerator<T> GetEnumerator() =>
        Provider.Execute<IEnumerable<T>>(Expression).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public async IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default)
    {
        foreach (T item in Provider.Execute<IEnumerable<T>>(Expression))
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return item;
        }

        await Task.CompletedTask;
    }

    private static IEnumerable<TItem> Count<TItem>(IEnumerable<TItem> source, Counter counter)
    {
        foreach (TItem item in source)
        {
            counter.Value++;
            yield return item;
        }
    }

    private sealed class Counter
    {
        public int Value { get; set; }
    }

    private sealed class CountingQueryProvider(IQueryProvider inner) : IAsyncQueryProvider
    {
        public IQueryable CreateQuery(Expression expression) => inner.CreateQuery(expression);

        public IQueryable<TElement> CreateQuery<TElement>(Expression expression) =>
            new Forwarding<TElement>(inner.CreateQuery<TElement>(expression), this);

        public object? Execute(Expression expression) => inner.Execute(expression);

        public TResult Execute<TResult>(Expression expression) => inner.Execute<TResult>(expression);

        // CountAsync and ToListAsync route through here. The work itself is synchronous; only the
        // shape has to match what EF's async operators expect.
        public TResult ExecuteAsync<TResult>(Expression expression, CancellationToken cancellationToken = default)
        {
            Type resultType = typeof(TResult).GetGenericArguments()[0];
            object? result = inner.Execute(expression);

            return (TResult)typeof(Task)
                .GetMethod(nameof(Task.FromResult))!
                .MakeGenericMethod(resultType)
                .Invoke(null, [result])!;
        }
    }

    // Every queryable derived from the original - after Where, Skip, Take - has to stay
    // async-enumerable, or ToListAsync refuses to run on it.
    private sealed class Forwarding<TElement>(IQueryable<TElement> inner, IQueryProvider provider)
        : IQueryable<TElement>, IAsyncEnumerable<TElement>
    {
        public Type ElementType => inner.ElementType;

        public Expression Expression => inner.Expression;

        public IQueryProvider Provider => provider;

        public IEnumerator<TElement> GetEnumerator() => inner.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        public async IAsyncEnumerator<TElement> GetAsyncEnumerator(
            CancellationToken cancellationToken = default)
        {
            foreach (TElement item in inner)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return item;
            }

            await Task.CompletedTask;
        }
    }
}
