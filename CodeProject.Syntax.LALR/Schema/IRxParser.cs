using System;
using System.Collections.Generic;
using System.Globalization;
using CodeProject.Syntax.LALR.LexicalGrammar;

namespace CodeProject.Syntax.LALR.Schema;

/// <summary>
/// Parses a small regex-like dialect into the typed <see cref="IRx"/> AST consumed
/// by <see cref="DfaCompiler"/>. This is the inverse of writing patterns by hand —
/// users authoring grammars as data (YAML, JSON, code) write a string here and we
/// turn it into a typed pattern tree.
/// </summary>
/// <remarks>
/// Supported syntax (deliberately minimal — alternation lives at the lexer-rule level
/// and the byte-DFA's longest-match + first-rule-wins handles it):
/// <list type="bullet">
/// <item>Literals — any character that isn't a metachar.</item>
/// <item>Escapes — <c>\\ \. \[ \] \( \) \{ \} \? \+ \* \^ \$ \- \| \/ \r \n \t</c>.</item>
/// <item>Character classes — <c>[abc]</c>, ranges <c>[a-z]</c>, mixed <c>[a-zA-Z0-9_]</c>, negation <c>[^abc]</c>.</item>
/// <item>Quantifiers — <c>?</c>, <c>+</c>, <c>*</c>, <c>{n}</c>, <c>{n,m}</c>, <c>{n,}</c>.</item>
/// <item>Grouping — <c>(...)</c>; quantifiers apply to the group as a whole.</item>
/// </list>
/// Not supported: alternation <c>|</c> (use multiple lexer rules), anchors <c>^ $</c>,
/// backreferences, lookaround, Unicode property classes <c>\p{…}</c>.
/// </remarks>
public static class IRxParser
{
    /// <summary>
    /// Parse <paramref name="pattern"/> into an <see cref="IRx"/> tree. Throws
    /// <see cref="FormatException"/> with a position pointer on bad input.
    /// </summary>
    public static IRx Parse(string pattern)
    {
        ArgumentNullException.ThrowIfNull(pattern);
        if (pattern.Length == 0)
        {
            throw new FormatException("empty pattern");
        }
        var state = new ParserState(pattern);
        var rx = ParseConcat(state);
        if (state.HasMore)
        {
            throw Fmt(state, $"unexpected '{state.Current}'");
        }
        return rx;
    }

    private sealed class ParserState
    {
        public readonly string Source;
        public int Pos;

        public ParserState(string source)
        {
            Source = source;
            Pos = 0;
        }

        public bool HasMore => Pos < Source.Length;
        public char Current => Source[Pos];

        public char Consume()
        {
            return Source[Pos++];
        }

        public bool TryConsume(char c)
        {
            if (HasMore && Current == c)
            {
                Pos++;
                return true;
            }
            return false;
        }
    }

    private static IRx ParseConcat(ParserState p)
    {
        var atoms = new List<IRx>();
        while (p.HasMore && p.Current != ')')
        {
            atoms.Add(ParseAtom(p));
        }
        if (atoms.Count == 0)
        {
            throw Fmt(p, "empty (sub)expression");
        }
        if (atoms.Count == 1)
        {
            return atoms[0];
        }
        return new GroupRx(Multiplicity.Once, atoms.ToArray());
    }

    private static IRx ParseAtom(ParserState p)
    {
        IRx atom;
        switch (p.Current)
        {
            case '(':
                atom = ParseGroup(p);
                break;
            case '[':
                atom = ParseClass(p);
                break;
            case '|':
                throw Fmt(p, "alternation '|' is not supported; use multiple lexer rules instead");
            case ')':
            case '?':
            case '+':
            case '*':
                throw Fmt(p, $"unexpected '{p.Current}'");
            case '{':
                throw Fmt(p, "'{' without preceding atom");
            case '\\':
                p.Pos++;
                if (!p.HasMore)
                {
                    throw new FormatException("trailing backslash");
                }
                atom = new CharRx(ConsumeEscapedChar(p));
                break;
            default:
                atom = new CharRx(ConsumeChar(p));
                break;
        }

        if (p.HasMore && IsQuantifierChar(p.Current))
        {
            var multiplicity = ParseQuantifier(p);
            // Wrap as GroupRx to apply the quantifier; for single-atom groups GroupRx
            // emits a tight pattern (no extra parens in the formatted output).
            atom = new GroupRx(multiplicity, atom);
        }
        return atom;
    }

    private static IRx ParseGroup(ParserState p)
    {
        p.Pos++; // consume '('
        var inner = ParseConcat(p);
        if (!p.TryConsume(')'))
        {
            throw Fmt(p, "expected ')'");
        }
        return inner;
    }

    private static IRx ParseClass(ParserState p)
    {
        p.Pos++; // consume '['
        var positive = !p.TryConsume('^');
        var items = new List<ISingleCharRx>();
        while (p.HasMore && p.Current != ']')
        {
            items.Add(ParseClassItem(p));
        }
        if (!p.TryConsume(']'))
        {
            throw new FormatException("unterminated character class");
        }
        if (items.Count == 0)
        {
            throw new FormatException("empty character class");
        }
        return new CharClassRx(positive, items.ToArray());
    }

    private static ISingleCharRx ParseClassItem(ParserState p)
    {
        var first = ConsumeClassChar(p);
        // Range only if the next char is '-' AND the one after isn't ']' (so [a-]
        // means literal 'a' then literal '-').
        if (p.HasMore && p.Current == '-' && p.Pos + 1 < p.Source.Length && p.Source[p.Pos + 1] != ']')
        {
            p.Pos++; // consume '-'
            var second = ConsumeClassChar(p);
            return new CharRangeRx(first, second);
        }
        return new CharRx(first);
    }

    private static int ConsumeClassChar(ParserState p)
    {
        if (p.Current == '\\')
        {
            p.Pos++;
            if (!p.HasMore)
            {
                throw new FormatException("trailing backslash in character class");
            }
            return ConsumeEscapedChar(p);
        }
        return ConsumeChar(p);
    }

    private static int ConsumeChar(ParserState p)
    {
        var c = p.Source[p.Pos];
        if (char.IsHighSurrogate(c) && p.Pos + 1 < p.Source.Length && char.IsLowSurrogate(p.Source[p.Pos + 1]))
        {
            var cp = char.ConvertToUtf32(c, p.Source[p.Pos + 1]);
            p.Pos += 2;
            return cp;
        }
        p.Pos++;
        return c;
    }

    private static int ConsumeEscapedChar(ParserState p)
    {
        var c = p.Consume();
        return c switch
        {
            'n' => '\n',
            'r' => '\r',
            't' => '\t',
            // Pass-through escapes: backslash strips meta-meaning; the literal char comes through.
            '\\' or '.' or '[' or ']' or '(' or ')' or '{' or '}'
                or '?' or '+' or '*' or '^' or '$' or '-' or '|' or '/' => c,
            _ => throw new FormatException($"unknown escape '\\{c}' at position {p.Pos - 1}"),
        };
    }

    private static bool IsQuantifierChar(char c) => c is '?' or '+' or '*' or '{';

    private static Multiplicity ParseQuantifier(ParserState p)
    {
        var c = p.Consume();
        switch (c)
        {
            case '?': return Multiplicity.ZeroOrOnce;
            case '+': return Multiplicity.OneOrMore;
            case '*': return Multiplicity.ZeroOrMore;
            case '{':
                var from = ParseDigits(p);
                if (p.TryConsume('}'))
                {
                    return new Multiplicity(from);
                }
                if (!p.TryConsume(','))
                {
                    throw Fmt(p, "expected ',' or '}' in quantifier");
                }
                if (p.TryConsume('}'))
                {
                    return new Multiplicity(from, -1);
                }
                var to = ParseDigits(p);
                if (!p.TryConsume('}'))
                {
                    throw Fmt(p, "expected '}' in quantifier");
                }
                return new Multiplicity(from, to);
            default:
                // unreachable — Consume() returned a quantifier char by IsQuantifierChar.
                throw Fmt(p, $"unexpected quantifier '{c}'");
        }
    }

    private static int ParseDigits(ParserState p)
    {
        var start = p.Pos;
        while (p.HasMore && char.IsDigit(p.Current))
        {
            p.Pos++;
        }
        if (p.Pos == start)
        {
            throw Fmt(p, "expected digit");
        }
        return int.Parse(p.Source[start..p.Pos], CultureInfo.InvariantCulture);
    }

    private static FormatException Fmt(ParserState p, string what)
    {
        return new FormatException(string.Create(CultureInfo.InvariantCulture,
            $"{what} at position {p.Pos} in pattern \"{p.Source}\""));
    }
}
