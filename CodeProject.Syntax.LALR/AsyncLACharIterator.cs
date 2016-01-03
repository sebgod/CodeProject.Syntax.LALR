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

        private AsyncLACharIterator(TextReader reader, int capacity = 1024)
        {
            _reader = reader;
            _chars = new StringBuilder(capacity);
            _index = NotInitialised;
        }

        public void Dispose()
        {
            _reader.Dispose();
        }

        public Task<int> LookAheadAsync()
        {
            if (_index == NotInitialised)
            {
                throw new InvalidOperationException("Did not call MoveNextAsync() yet!");
            }
            throw new NotImplementedException("LookAheadAsync");
        }

        public async Task<int> CurrentAsync()
        {
            switch (_index)
            {
                case NotInitialised:
                    throw new InvalidOperationException("Did not call MoveNextAsync() yet!");
                case EOF:
                    return _index;
            }
            char unit;
            while ((unit = _chars[_index]) == '\r')
            {
                if (_index + 1 < _chars.Length)
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
                var low = _chars[_index++];
                return char.IsLowSurrogate(low) ? char.ConvertToUtf32(unit, low) : ReplacementCodepoint;
            }
            return char.IsLowSurrogate(unit) ? ReplacementCodepoint : unit;
        }

        public async Task<bool> MoveNextAsync()
        {
            if (_index >= 0 && _index++ < _chars.Capacity)
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
            // TODO count lines + offs
            _chars.Append(readBuffer);
            if (read > 0 && char.IsHighSurrogate(_chars[read - 1]))
            {
                var readLow = await _reader.ReadAsync(readBuffer, 0, 1);
                if (readLow > 0)
                {
                    _chars.Append(readBuffer);
                    read += readLow;
                }
            }
            return read > 0 ? read : EOF;
        }

        public void Reset()
        {
            throw new InvalidOperationException("Resetting is not supported");
        }

        public bool SupportsResetting { get { return false; } }
    }
}
