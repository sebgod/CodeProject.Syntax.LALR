using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using CodeProject.Syntax.LALR;
using CodeProject.Syntax.LALR.LexicalGrammar;
using LatexGrammar;
// `using static` exposes the nested types of the partial Latex class
// (IVisitor<T>, Add, Subtract, …) directly so we don't have to write
// `Add` everywhere — the bare `Latex` identifier inside namespace
// Examples.Latex would shadow against the enclosing namespace's last
// segment under C# name lookup rules.
using static LatexGrammar.Latex;

namespace Examples.Latex;

/// <summary>
/// Walks Wikipedia-style LaTeX math through the YAML pipeline:
/// <c>latex.lalr.yaml</c> → source-generated <c>Latex</c> partial class
/// (schema + AST records + IVisitor surface) → <see cref="Renderer"/> turns
/// each reduction into a Unicode "pretty form" string. The result is
/// approximately what a Wikipedia formula looks like when copied to a plain-
/// text context: Greek letters become Greek letters, common digits become
/// Unicode super/subscripts, fractions use the fraction slash, and
/// everything else falls back to readable LaTeX-ish notation.
/// </summary>
internal static class Program
{
    private static readonly string[] Samples =
    [
        "E = mc^2",
        "e^{i\\pi} + 1 = 0",
        "\\frac{1}{2} + \\frac{1}{3}",
        "\\sqrt{x^2 + y^2}",
        "\\sum_{i=0}^{n} i = \\frac{n(n+1)}{2}",
        "\\sin(\\alpha + \\beta) = \\sin\\alpha \\cos\\beta + \\cos\\alpha \\sin\\beta",
        "a_{n+1} = a_n + a_{n-1}",
        "\\int_0^\\infty e^{-x^2} dx = \\frac{\\sqrt{\\pi}}{2}",
    ];

    public static void Main()
    {
        Console.OutputEncoding = Encoding.UTF8;

        var renderer = new Renderer();
        // Phase 5 / slices 5 + 6 + sync surface: parser + lexer pre-baked, run
        // through BytesLexer + Parser.ParseInput. SchemaCompiler + the async
        // iterator path are unreachable in this AOT image.
        var parser = BuildParser(renderer);
        var lexerTable = BuildLexer();

        foreach (var src in Samples)
        {
            using var lexer = BytesLexer.FromString(src, lexerTable);
            using var tokens = new SyncLATokenIterator(lexer);

            // trimReductions:true folds the passthrough productions
            // (E->T, T->F, F->F2, F2->P, P->A, S->E) so the visitor only fires
            // for productions with a meaningful action — exactly what we want
            // to keep the visitor surface small.
            // ParserErrorMode.Return turns a parse error into a Item-with-IsError
            // rather than throwing, so a single bad formula doesn't bring the
            // demo down — useful for showing partial coverage.
            string rendered;
            try
            {
                var result = parser.ParseInput(tokens, debugger: null);
                rendered = result.IsError ? $"<error: {result}>" : (string)result.Content;
            }
            catch (ParseErrorException ex)
            {
                rendered = $"<{ex.Message.Split('\n')[0]}>";
            }
            catch (Exception ex)
            {
                rendered = $"<{ex.GetType().Name}: {ex.Message.Split('\n')[0]}>";
            }

            // Two columns: input on the left, rendered on the right. The
            // alignment makes regressions obvious — if a refactor changes the
            // associativity or the renderer mapping, you'll see it instantly.
            Console.WriteLine($"  {src,-60}  →  {rendered}");
        }
    }

    /// <summary>
    /// Visitor: returns the Unicode rendering of each AST node. The string is
    /// propagated up the tree via <c>Item.Content</c> so each node sees its
    /// children's already-rendered text and just decides how to glue them.
    /// </summary>
    private sealed class Renderer : IVisitor<string>
    {
        // Operators — we use real Unicode where it makes the formula nicer:
        // U+2212 for unary/binary minus (typographic minus, not hyphen-minus),
        // U+00B7 for multiplication (centred dot).
        public string Visit(Add node)      => $"{node.Arg0.Content} + {node.Arg2.Content}";
        public string Visit(Subtract node) => $"{node.Arg0.Content} − {node.Arg2.Content}";
        public string Visit(Eq node)       => $"{node.Arg0.Content} = {node.Arg2.Content}";
        public string Visit(Mul node)      => $"{node.Arg0.Content}·{node.Arg2.Content}";
        public string Visit(Div node)      => $"{node.Arg0.Content}/{node.Arg2.Content}";
        public string Visit(Juxt node)     => $"{node.Arg0.Content}{node.Arg1.Content}";
        public string Visit(Neg node)      => $"−{node.Arg1.Content}";

        // Scripts: try Unicode super/subscripts when the argument is short and
        // every codepoint has a Unicode form. Otherwise fall back to caret /
        // underscore notation with parens around multi-token arguments.
        public string Visit(Sup node) =>
            TryUnicodeScript((string)node.Arg2.Content, Superscripts, out var sup)
                ? $"{node.Arg0.Content}{sup}"
                : $"{node.Arg0.Content}^{Wrap((string)node.Arg2.Content)}";

        public string Visit(Subscript node) =>
            TryUnicodeScript((string)node.Arg2.Content, Subscripts, out var sub)
                ? $"{node.Arg0.Content}{sub}"
                : $"{node.Arg0.Content}_{Wrap((string)node.Arg2.Content)}";

        // Atom leaves. Numbers and single-letter variables come through as
        // their lexer-matched bytes. \name commands are looked up in a Greek/
        // operator/function table; unknown commands keep their backslash form
        // so the user can see exactly what didn't render.
        public string Visit(Number node)   => (string)node.Arg0.Content;
        public string Visit(Variable node) => (string)node.Arg0.Content;
        public string Visit(Command node)  => RenderCommand((string)node.Arg0.Content);

        // Brackets. Parens stay visible; braces are LaTeX's "invisible group"
        // so we drop them in the rendering and just return the inner E.
        public string Visit(Paren node) => $"({node.Arg1.Content})";
        public string Visit(Group node) => (string)node.Arg1.Content;

        // \sqrt and \frac. The radical glyph U+221A renders without an overbar
        // in plain text, so we wrap multi-token arguments in parens for clarity.
        // U+2044 is the fraction slash — distinct from solidus '/', so the
        // reader can tell `\frac{1}{2}` (½-style) from `1/2` (raw division).
        public string Visit(Sqrt node) => $"√{Wrap((string)node.Arg1.Content)}";
        public string Visit(Frac node) => $"{Wrap((string)node.Arg1.Content)}⁄{Wrap((string)node.Arg2.Content)}";

        /// <summary>
        /// Wrap a sub-expression in parens if it contains anything that would
        /// be ambiguous when concatenated with adjacent text (operators or a
        /// space). Single tokens — bare digits, single letters, single Unicode
        /// glyphs — pass through untouched.
        /// </summary>
        private static string Wrap(string s)
        {
            if (string.IsNullOrEmpty(s))
            {
                return s;
            }
            // A single grapheme-ish unit (ASCII char or BMP codepoint) is safe.
            if (s.Length == 1)
            {
                return s;
            }
            // Heuristic: if the rendering already contains an operator or a
            // space, parenthesise. We don't try to track precedence properly —
            // this is a pretty-printer, not a typesetter.
            foreach (var c in s)
            {
                if (c == ' ' || c == '+' || c == '−' || c == '=' || c == '/' || c == '·' || c == '⁄')
                {
                    return $"({s})";
                }
            }
            return s;
        }

        /// <summary>
        /// Map a single rendered character through the script table. Returns
        /// false if any character has no entry — caller falls back to caret/
        /// underscore notation. Whitespace is rejected so multi-token
        /// expressions don't get silently mangled into super/subscripts.
        /// </summary>
        private static bool TryUnicodeScript(string s, IReadOnlyDictionary<char, char> table, out string mapped)
        {
            if (string.IsNullOrEmpty(s) || s.Length > 4)
            {
                mapped = null;
                return false;
            }
            var sb = new StringBuilder(s.Length);
            foreach (var c in s)
            {
                if (!table.TryGetValue(c, out var sc))
                {
                    mapped = null;
                    return false;
                }
                sb.Append(sc);
            }
            mapped = sb.ToString();
            return true;
        }

        private static string RenderCommand(string raw)
        {
            // raw is the lexer-matched bytes for a `\name` token, e.g. "\alpha".
            // Strip the leading backslash and look up; fall back to the raw
            // form so unknown commands stay debuggable.
            if (raw.Length < 2 || raw[0] != '\\')
            {
                return raw;
            }
            var name = raw.Substring(1);
            return Commands.TryGetValue(name, out var glyph) ? glyph : raw;
        }

        /// <summary>
        /// LaTeX command → Unicode glyph mappings. Greek letters, common
        /// function names, big operators, and a handful of relations.
        /// Function names like "sin" stay as "sin" (no Unicode glyph) — the
        /// rendered output then reads like a typeset formula in plain text.
        /// </summary>
        private static readonly Dictionary<string, string> Commands = new(StringComparer.Ordinal)
        {
            // Lowercase Greek
            ["alpha"] = "α", ["beta"] = "β", ["gamma"] = "γ",
            ["delta"] = "δ", ["epsilon"] = "ε", ["zeta"] = "ζ",
            ["eta"] = "η", ["theta"] = "θ", ["iota"] = "ι",
            ["kappa"] = "κ", ["lambda"] = "λ", ["mu"] = "μ",
            ["nu"] = "ν", ["xi"] = "ξ", ["pi"] = "π",
            ["rho"] = "ρ", ["sigma"] = "σ", ["tau"] = "τ",
            ["upsilon"] = "υ", ["phi"] = "φ", ["chi"] = "χ",
            ["psi"] = "ψ", ["omega"] = "ω",
            // Uppercase Greek (only the ones that aren't visually identical to Latin)
            ["Gamma"] = "Γ", ["Delta"] = "Δ", ["Theta"] = "Θ",
            ["Lambda"] = "Λ", ["Xi"] = "Ξ", ["Pi"] = "Π",
            ["Sigma"] = "Σ", ["Phi"] = "Φ", ["Psi"] = "Ψ",
            ["Omega"] = "Ω",
            // Common function names — kept as their letters so the formula
            // reads naturally (sin, cos, log, …). LaTeX renders them upright
            // rather than italic; we lose that distinction in plain text.
            ["sin"] = "sin", ["cos"] = "cos", ["tan"] = "tan",
            ["sec"] = "sec", ["csc"] = "csc", ["cot"] = "cot",
            ["arcsin"] = "arcsin", ["arccos"] = "arccos", ["arctan"] = "arctan",
            ["sinh"] = "sinh", ["cosh"] = "cosh", ["tanh"] = "tanh",
            ["log"] = "log", ["ln"] = "ln", ["exp"] = "exp",
            ["lim"] = "lim", ["max"] = "max", ["min"] = "min",
            ["det"] = "det", ["dim"] = "dim", ["gcd"] = "gcd",
            // Big operators
            ["sum"] = "∑", ["prod"] = "∏", ["int"] = "∫",
            ["oint"] = "∮", ["bigcup"] = "⋃", ["bigcap"] = "⋂",
            // Constants and symbols
            ["infty"] = "∞", ["partial"] = "∂", ["nabla"] = "∇",
            ["pm"] = "±", ["mp"] = "∓", ["to"] = "→",
            ["leftarrow"] = "←", ["rightarrow"] = "→",
            ["leq"] = "≤", ["geq"] = "≥", ["neq"] = "≠",
            ["approx"] = "≈", ["equiv"] = "≡",
            ["in"] = "∈", ["notin"] = "∉", ["subset"] = "⊂",
            ["cup"] = "∪", ["cap"] = "∩",
        };

        // Unicode super/subscript tables — only entries that exist as proper
        // codepoints. Anything missing forces the caret/underscore fallback.
        private static readonly Dictionary<char, char> Superscripts = new()
        {
            ['0'] = '⁰', ['1'] = '¹', ['2'] = '²', ['3'] = '³',
            ['4'] = '⁴', ['5'] = '⁵', ['6'] = '⁶', ['7'] = '⁷',
            ['8'] = '⁸', ['9'] = '⁹',
            ['+'] = '⁺', ['-'] = '⁻', ['='] = '⁼', ['('] = '⁽', [')'] = '⁾',
            ['n'] = 'ⁿ', ['i'] = 'ⁱ',
        };

        private static readonly Dictionary<char, char> Subscripts = new()
        {
            ['0'] = '₀', ['1'] = '₁', ['2'] = '₂', ['3'] = '₃',
            ['4'] = '₄', ['5'] = '₅', ['6'] = '₆', ['7'] = '₇',
            ['8'] = '₈', ['9'] = '₉',
            ['+'] = '₊', ['-'] = '₋', ['='] = '₌', ['('] = '₍', [')'] = '₎',
            // Lowercase letters with Unicode subscript forms (incomplete coverage).
            ['a'] = 'ₐ', ['e'] = 'ₑ', ['h'] = 'ₕ', ['i'] = 'ᵢ',
            ['j'] = 'ⱼ', ['k'] = 'ₖ', ['l'] = 'ₗ', ['m'] = 'ₘ',
            ['n'] = 'ₙ', ['o'] = 'ₒ', ['p'] = 'ₚ', ['r'] = 'ᵣ',
            ['s'] = 'ₛ', ['t'] = 'ₜ', ['u'] = 'ᵤ', ['v'] = 'ᵥ', ['x'] = 'ₓ',
        };
    }
}
