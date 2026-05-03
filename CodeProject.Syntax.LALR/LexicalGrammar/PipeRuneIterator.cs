using System;
using System.Buffers;
using System.IO;
using System.IO.Pipelines;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CodeProject.Syntax.LALR.LexicalGrammar;

/// <summary>
/// Lookahead iterator over Unicode codepoints decoded directly from a UTF-8 byte source
/// via <see cref="PipeReader"/>. Bytes are decoded once, with <see cref="Rune.DecodeFromUtf8(ReadOnlySpan{byte}, out Rune, out int)"/>;
/// no UTF-16 round trip happens inside this class.
/// </summary>
public sealed class PipeRuneIterator : IAsyncLAIterator<int>
{
    public const int EOF = -1;
    public const int ReplacementCodepoint = 0xFFFD;
    private const int NotInitialised = -2;

    private readonly PipeReader _reader;
    private readonly bool _normalizeCarriageReturn;
    private readonly CancellationToken _cancellationToken;

    private int _current = NotInitialised;
    private int _lookAhead = NotInitialised;
    private bool _readerCompleted;

    public PipeRuneIterator(PipeReader reader, bool normalizeCarriageReturn = true, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reader);
        _reader = reader;
        _normalizeCarriageReturn = normalizeCarriageReturn;
        _cancellationToken = cancellationToken;
    }

    /// <summary>
    /// Wraps a UTF-8 encoded <see cref="Stream"/>. Uses Pipelines for buffered async reads;
    /// the bytes are decoded directly to runes — no <see cref="StreamReader"/> in the path.
    /// </summary>
    public static PipeRuneIterator FromStream(Stream stream, bool leaveOpen = false, bool normalizeCarriageReturn = true, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        var reader = PipeReader.Create(stream, new StreamPipeReaderOptions(leaveOpen: leaveOpen));
        return new PipeRuneIterator(reader, normalizeCarriageReturn, cancellationToken);
    }

    /// <summary>
    /// Wraps an existing UTF-8 byte buffer. Preferred over <see cref="FromString"/> when the
    /// source is already bytes, because no UTF-16 → UTF-8 encoding pass is required.
    /// </summary>
    public static PipeRuneIterator FromBytes(ReadOnlyMemory<byte> utf8Bytes, bool normalizeCarriageReturn = true, CancellationToken cancellationToken = default)
    {
        var reader = PipeReader.Create(new ReadOnlySequence<byte>(utf8Bytes));
        return new PipeRuneIterator(reader, normalizeCarriageReturn, cancellationToken);
    }

    /// <summary>
    /// Convenience constructor: encodes a UTF-16 <see cref="string"/> to UTF-8 bytes once,
    /// then iterates. Intended for tests and embedded grammar literals; for file/stream input
    /// prefer <see cref="FromStream"/> or <see cref="FromBytes"/> to skip the UTF-16 → UTF-8 pass.
    /// </summary>
    public static PipeRuneIterator FromString(string text, bool normalizeCarriageReturn = true, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(text);
        var bytes = Encoding.UTF8.GetBytes(text);
        return FromBytes(bytes, normalizeCarriageReturn, cancellationToken);
    }

    public async Task<bool> MoveNextAsync()
    {
        if (_lookAhead != NotInitialised)
        {
            _current = _lookAhead;
            _lookAhead = NotInitialised;
        }
        else
        {
            _current = await ReadNextCodepointAsync().ConfigureAwait(false);
        }
        return _current != EOF;
    }

    public Task<int> CurrentAsync()
    {
        if (_current == NotInitialised)
        {
            throw new InvalidOperationException("Did not call MoveNextAsync() yet!");
        }
        return Task.FromResult(_current);
    }

    public async Task<int> LookAheadAsync()
    {
        if (_current == NotInitialised)
        {
            throw new InvalidOperationException("Did not call MoveNextAsync() yet!");
        }
        if (_lookAhead == NotInitialised)
        {
            _lookAhead = await ReadNextCodepointAsync().ConfigureAwait(false);
        }
        return _lookAhead;
    }

    private async ValueTask<int> ReadNextCodepointAsync()
    {
        while (true)
        {
            if (_readerCompleted)
            {
                return EOF;
            }

            var result = await _reader.ReadAsync(_cancellationToken).ConfigureAwait(false);
            var buffer = result.Buffer;

            if (buffer.IsEmpty && result.IsCompleted)
            {
                _reader.AdvanceTo(buffer.End);
                _readerCompleted = true;
                return EOF;
            }

            var codepoint = TryDecodeOne(buffer, result.IsCompleted, out var consumed, out var needMore);

            if (codepoint == NotInitialised)
            {
                if (needMore)
                {
                    // Incomplete rune at end of buffer — leave bytes unconsumed and ask for more.
                    _reader.AdvanceTo(buffer.Start, buffer.End);
                    if (result.IsCompleted)
                    {
                        _readerCompleted = true;
                        return EOF;
                    }
                    continue;
                }

                _reader.AdvanceTo(buffer.End);
                _readerCompleted = true;
                return EOF;
            }

            _reader.AdvanceTo(consumed);

            if (_normalizeCarriageReturn && codepoint == '\r')
            {
                continue;
            }

            return codepoint;
        }
    }

    private static int TryDecodeOne(ReadOnlySequence<byte> buffer, bool isFinalBlock, out SequencePosition consumed, out bool needMore)
    {
        if (buffer.IsEmpty)
        {
            consumed = buffer.Start;
            needMore = !isFinalBlock;
            return NotInitialised;
        }

        OperationStatus status;
        Rune rune;
        int bytesConsumed;

        var firstSpan = buffer.FirstSpan;
        // Fast path: a complete UTF-8 sequence is at most 4 bytes, so if we either have the
        // whole buffer in one span, or at least 4 bytes contiguous, decode in place.
        if (buffer.IsSingleSegment || firstSpan.Length >= 4)
        {
            status = Rune.DecodeFromUtf8(firstSpan, out rune, out bytesConsumed);
        }
        else
        {
            // Cross-segment: copy up to 4 bytes onto the stack and decode from there.
            Span<byte> stackBuffer = stackalloc byte[4];
            var copyLen = (int)Math.Min(stackBuffer.Length, buffer.Length);
            buffer.Slice(0, copyLen).CopyTo(stackBuffer);
            status = Rune.DecodeFromUtf8(stackBuffer[..copyLen], out rune, out bytesConsumed);
        }

        switch (status)
        {
            case OperationStatus.Done:
                consumed = buffer.GetPosition(bytesConsumed);
                needMore = false;
                return rune.Value;

            case OperationStatus.NeedMoreData:
                if (isFinalBlock)
                {
                    // Truncated UTF-8 at EOF — emit replacement and consume the orphan bytes.
                    consumed = buffer.GetPosition(Math.Max(1, bytesConsumed));
                    needMore = false;
                    return ReplacementCodepoint;
                }
                consumed = buffer.Start;
                needMore = true;
                return NotInitialised;

            case OperationStatus.InvalidData:
            default:
                consumed = buffer.GetPosition(Math.Max(1, bytesConsumed));
                needMore = false;
                return ReplacementCodepoint;
        }
    }

    public void Reset() => throw new InvalidOperationException("Resetting is not supported");

    public bool SupportsResetting => false;

    public void Dispose() => _reader.Complete();
}
