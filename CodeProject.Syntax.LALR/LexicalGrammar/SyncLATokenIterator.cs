namespace CodeProject.Syntax.LALR.LexicalGrammar;

/// <summary>
/// Sync mirror of <see cref="AsyncLATokenIterator"/>. Wraps a sync
/// <see cref="ISyncIterator{T}"/> (typically <see cref="BytesLexer"/>) and
/// adds the lookahead-1 cache the parser loop needs. Same pattern as the
/// async variant — null sentinel marks "no cached lookahead", restocked on
/// next <see cref="LookAhead"/> call.
/// </summary>
public sealed class SyncLATokenIterator(ISyncIterator<Item> inputSource) : ISyncLAIterator<Item>
{
    private readonly ISyncIterator<Item> _inputSource = inputSource;
    private Item _lookAhead;

    public Item LookAhead() =>
        _lookAhead ??= _inputSource.MoveNext() ? _inputSource.Current : Item.EOF;

    public Item Current => _inputSource.Current;

    public bool MoveNext()
    {
        // If we already cached a lookahead, the parser is consuming that —
        // drop the cache and tell the caller "yes, advanced". Otherwise pump
        // the underlying iterator. Mirrors AsyncLATokenIterator exactly.
        if (_lookAhead != null)
        {
            _lookAhead = null;
            return true;
        }
        return _inputSource.MoveNext();
    }

    public void Reset()
    {
        _lookAhead = null;
        _inputSource.Reset();
    }

    public bool SupportsResetting => _inputSource.SupportsResetting;

    public void Dispose()
    {
        _lookAhead = null;
        _inputSource.Dispose();
    }
}
