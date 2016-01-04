using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace CodeProject.Syntax.LALR
{
    public class AsyncLACharIterator : IAsyncLAIterator<int>
    {
// ReSharper disable MemberCanBePrivate.Global
        public const int EOF = -1;
        private const int NotInitialised = -2;
        public const int ReplacementCodepoint = 0xFFFD;
// ReSharper restore MemberCanBePrivate.Global

        private readonly TextReader _reader;
        private readonly StringBuilder _chars;
        private int _index;

        public AsyncLACharIterator(TextReader reader, int capacity = 1024)
        {
            _reader = reader;
            _chars = new StringBuilder(capacity);
            _index = NotInitialised;
        }

        public void Dispose()
        {
            _reader.Dispose();
        }

        public async Task<int> LookAheadAsync()
        {
            return await CodepointAtIndex(1);
        }

        public async Task<int> CurrentAsync()
        {
            return await CodepointAtIndex();
        }

        private async Task<int> CodepointAtIndex(int lookAhead = 0)
        {
            if (_index == NotInitialised)
            {
                throw new InvalidOperationException("Did not call MoveNextAsync() yet!");
            }
            if (_index + lookAhead >= _chars.Length && await RefillBuffer() <= 0)
            {
                return EOF;
            }

            char unit;
            while ((unit = _chars[_index + lookAhead]) == '\r')
            {
                if (_index + lookAhead + 1 < _chars.Length)
                {
                    _index++;
                }
                else if (await RefillBuffer() <= 0)
                {
                    return EOF;
                }
            }
            if (char.IsHighSurrogate(unit))
            {
                // RefillBuffer guarantees that a high surrogate will never be the last code unit
                var low = _chars[++_index + lookAhead];
                return char.IsLowSurrogate(low) ? char.ConvertToUtf32(unit, low) : ReplacementCodepoint;
            }
            return char.IsLowSurrogate(unit) ? ReplacementCodepoint : unit;
        }

        public async Task<bool> MoveNextAsync()
        {
            if (_index >= 0 && ++_index < _chars.Length)
            {
                return true;
            }
            return await RefillBuffer() > 0;
        }

        private async Task<int> RefillBuffer()
        {
            _index = 0;
            _chars.Clear();
            var readBuffer = new char[_chars.Capacity >> 1];
            var read = await _reader.ReadAsync(readBuffer, 0, readBuffer.Length);

            if (read > 0)
            {
                // TODO count lines + offs
                _chars.Append(readBuffer, 0, read);

                if (char.IsHighSurrogate(_chars[read - 1]))
                {
                    var readLow = await _reader.ReadAsync(readBuffer, 0, 1);
                    if (readLow > 0)
                    {
                        _chars.Append(readBuffer, 0, readLow);
                        read += readLow;
                    }
                }
                return read;
            }
            return EOF;
        }

        public void Reset()
        {
            throw new InvalidOperationException("Resetting is not supported");
        }

        public bool SupportsResetting { get { return false; } }
    }
}
