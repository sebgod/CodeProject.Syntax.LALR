using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace LALR.CC.LexicalGrammar;

public struct AsyncEnumerableWrapper<T>(IEnumerable<T> enumerable) : IAsyncIterator<T>
{
    private readonly IEnumerator<T> _enumerator = enumerable.GetEnumerator();

    public void Dispose() => _enumerator.Dispose();

    public Task<T> CurrentAsync() => Task.FromResult(_enumerator.Current);

    public Task<bool> MoveNextAsync() => Task.FromResult(_enumerator.MoveNext());

    public void Reset() => throw new InvalidOperationException("Resetting is not supported!");

    public bool SupportsResetting => false;
}

public static class AsyncEnumerableWrapperEx
{
    public static IAsyncIterator<T> AsAsync<T>(this IEnumerable<T> @this) => new AsyncEnumerableWrapper<T>(@this);
}
