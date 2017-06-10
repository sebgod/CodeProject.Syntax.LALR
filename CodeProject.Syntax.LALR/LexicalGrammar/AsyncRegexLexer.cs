using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using RegexTuple = System.Tuple<int, System.Text.RegularExpressions.Regex, string>;

namespace CodeProject.Syntax.LALR.LexicalGrammar
{
    public class AsyncRegexLexer : IAsyncIterator<Item>
    {
        public const string RootState = "root";
        public const string PopState = "#pop";
        public const string Ignore = "#ignore";

        private readonly IAsyncLAIterator<int> _charSource;
        private readonly IReadOnlyDictionary<string, RegexTuple[]> _patternTable;
        private readonly Stack<string> _states; 

        public AsyncRegexLexer(IAsyncLAIterator<int> charSource, params RegexTuple[] patterns)
            : this(charSource, new Dictionary<string, RegexTuple[]> { { RootState, patterns } })
        {
            // calls below
        }

        public AsyncRegexLexer(IAsyncLAIterator<int> charSource,
            IReadOnlyDictionary<string, RegexTuple[]> patternTable)
        {
            _charSource = charSource;
            _patternTable = patternTable;
            _states = new Stack<string>(new []{ RootState });
        }

        public void Dispose()
        {
            _charSource.Dispose();
        }

        private Item _currentItem;
        public Task<Item> CurrentAsync()
        {
            if (_currentItem == null)
            {
                throw new InvalidOperationException("Did not call MoveNextAsync() yet!");
            }

            return Task.FromResult(_currentItem);
        }

        public async Task<bool> MoveNextAsync()
        {
            var buffer = new StringBuilder(4);

            var statePatterns = _patternTable[_states.Peek()];
            var matchingPatterns = new List<RegexTuple>(statePatterns);

            do
            {
                if (!await _charSource.MoveNextAsync())
                {
                    return false;
                }

                var textWithLA = await FillBufferAsync(buffer);

                for (var i = 0; i < matchingPatterns.Count; i++)
                {
                    var pattern = matchingPatterns[i];
                    var match = pattern.Item2.Match(textWithLA);
                    if (!match.Success || match.Length == 0)
                    {
                        matchingPatterns.RemoveAt(i--);
                    }
                }
            } while (matchingPatterns.Count > 1);

            if (matchingPatterns.Count == 0)
            {
                return false;
            }

            var matchingPattern = matchingPatterns[0];

            do
            {
                var lookAhead = await FillLookAheadAsync(buffer, true);

                if (lookAhead == null)
                {
                    break;
                }

                var textWithLA = buffer.ToString();
                var match = matchingPattern.Item2.Match(textWithLA);
                
                if (!match.Success || match.Length < textWithLA.Length)
                {
                    RemoveLookAhead(buffer, lookAhead);
                    break;
                }

                if (!await _charSource.MoveNextAsync())
                {
                    break;
                }
            } while (true);

            _currentItem = new Item(matchingPattern.Item1, buffer.ToString());

            var instructionList = matchingPattern.Item3;

            if (!string.IsNullOrEmpty(instructionList))
            {
                foreach (var instruction in instructionList.Split(new[] {','}, StringSplitOptions.RemoveEmptyEntries))
                { 
                    switch (instruction)
                    {
                        case PopState:
                            _states.Pop();
                            break;

                        case Ignore:
                            return await MoveNextAsync();

                        default:
                            _states.Push(matchingPattern.Item3);
                            break;
                    }   
                }
            }
            return true;
        }

        private async Task<string> FillBufferAsync(StringBuilder buffer, bool useLookAhead = true)
        {
            var current = char.ConvertFromUtf32(await _charSource.CurrentAsync());
            buffer.Append(current);

            var lookAhead = await FillLookAheadAsync(buffer, useLookAhead);

            var text = buffer.ToString();

            RemoveLookAhead(buffer, lookAhead);
            return text;
        }

        private static void RemoveLookAhead(StringBuilder buffer, string lookAhead)
        {
            if (lookAhead != null)
            {
                buffer.Remove(buffer.Length - lookAhead.Length, lookAhead.Length);
            }
        }

        private async Task<string> FillLookAheadAsync(StringBuilder buffer, bool useLookAhead)
        {
            string lookAhead;
            int lookAheadCodePoint;

            if (useLookAhead && (lookAheadCodePoint = await _charSource.LookAheadAsync()) > 0)
            {
                lookAhead = char.ConvertFromUtf32(lookAheadCodePoint);
                buffer.Append(lookAhead);
            }
            else
            {
                lookAhead = null;
            }
            return lookAhead;
        }

        public void Reset()
        {
            throw new InvalidOperationException("Resetting is not supported");
        }

        public bool SupportsResetting { get { return false; } }
    }
}
