namespace CodeProject.Syntax.LALR;

/// <summary>
/// A sync lookahead-1 iterator. Pairs with <see cref="IAsyncLAIterator{T}"/>
/// the way <see cref="ISyncIterator{T}"/> pairs with <see cref="IAsyncIterator{T}"/>:
/// same observable behaviour, no <c>ValueTask</c> / <c>Task</c> machinery,
/// for callers that already have the input in memory.
/// </summary>
/// <typeparam name="T">Item type to be iterated</typeparam>
public interface ISyncLAIterator<T> : ISyncIterator<T>
{
    /// <summary>
    /// Peek the next item without consuming it. Subsequent
    /// <see cref="ISyncIterator{T}.MoveNext"/> consumes the same item.
    /// </summary>
    T LookAhead();
}
