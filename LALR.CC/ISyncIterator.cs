using System;
using System.Collections;

namespace LALR.CC;

/// <summary>
/// A sync mirror of <see cref="IAsyncIterator{T}"/>. The pure-sync surface
/// exists for in-memory inputs (string / <see cref="ReadOnlyMemory{Byte}"/>)
/// where the async machinery is wasted overhead — every <c>MoveNextAsync</c>
/// goes through a state machine, allocates a <c>Task&lt;T&gt;</c> in the
/// non-cached cases, and pays a context-restore even when the underlying
/// pipeline returns synchronously. The async path stays the right shape
/// for stdin / network / on-disk streaming consumers.
/// </summary>
/// <typeparam name="T">Item type to be iterated</typeparam>
public interface ISyncIterator<T> : IDisposable
{
    /// <summary>The last consumed token. <see cref="IEnumerator.Current"/>.</summary>
    T Current { get; }

    /// <summary>Advance to the next item. <see cref="IEnumerator.MoveNext"/>.</summary>
    /// <returns>True if the iterator advanced; false at end of input.</returns>
    bool MoveNext();

    /// <summary>
    /// Reset the iterator. Throws <see cref="InvalidOperationException"/> when
    /// <see cref="SupportsResetting"/> is false (i.e. forward-only iterators
    /// such as <see cref="LexicalGrammar.BytesLexer"/>).
    /// </summary>
    void Reset();

    /// <summary>True if this iterator supports <see cref="Reset"/>.</summary>
    bool SupportsResetting { get; }
}
