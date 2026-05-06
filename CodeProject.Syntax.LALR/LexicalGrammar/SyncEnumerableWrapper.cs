using System;
using System.Collections.Generic;

namespace CodeProject.Syntax.LALR.LexicalGrammar;

/// <summary>
/// Sync mirror of <see cref="AsyncEnumerableWrapper{T}"/>. Lets a plain
/// <see cref="IEnumerable{T}"/> (e.g. an inline tokenizer that yields
/// <c>Item</c> values) plug into <see cref="SyncLATokenIterator"/> and
/// <see cref="Parser.ParseInput"/> without the <c>Task.FromResult</c>
/// allocations the async wrapper pays per token.
/// </summary>
public struct SyncEnumerableWrapper<T>(IEnumerable<T> enumerable) : ISyncIterator<T>
{
    private readonly IEnumerator<T> _enumerator = enumerable.GetEnumerator();

    public void Dispose() => _enumerator.Dispose();

    public T Current => _enumerator.Current;

    public bool MoveNext() => _enumerator.MoveNext();

    public void Reset() => throw new InvalidOperationException("Resetting is not supported!");

    public bool SupportsResetting => false;
}

public static class SyncEnumerableWrapperEx
{
    /// <summary>Wrap an <see cref="IEnumerable{T}"/> as an <see cref="ISyncIterator{T}"/>.</summary>
    public static ISyncIterator<T> AsSync<T>(this IEnumerable<T> @this) => new SyncEnumerableWrapper<T>(@this);
}
