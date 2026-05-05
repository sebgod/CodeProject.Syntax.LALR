using System.Collections.Generic;
using CodeProject.Syntax.LALR.LexicalGrammar;
using LatexGrammar;
using static LatexGrammar.Latex;
using DIR.Lib;
using DIR.Lib.MathLayout;

namespace Examples.LatexConsole;

/// <summary>
/// IVisitor&lt;Box&gt; implementation: maps each AST record to a TeX-lite
/// Box composition. Pairs with <c>Console.Lib.BoxRenderer</c>, which paints the
/// returned root Box into an RGBA buffer for sixel/half-block output.
///
/// Atoms (Number / Variable / Command) build a <see cref="GlyphBox"/> at the
/// configured base font size. Composites (Add / Sub / Eq / Mul / Div / Juxt)
/// combine children with <see cref="HBox"/> + small inter-token kerns.
/// Scripts use <see cref="SupSubBox"/> with the script font shrunk via
/// <see cref="BoxStyle.Smaller"/>. Frac and Sqrt build the obvious
/// dedicated boxes.
///
/// Because the AST records carry <c>Item</c> children and the visitor
/// chain lets us return any T from each Visit overload, we just propagate
/// already-built Box subtrees up the tree via <c>Item.Content</c>. Atoms
/// look at <c>Arg0.Content</c> as a string (the raw lexer-matched bytes).
/// </summary>
public sealed class BoxBuildingVisitor : IVisitor<Box>
{
    public BoxStyle Style { get; }

    public BoxBuildingVisitor(BoxStyle style)
    {
        Style = style;
    }

    private Box Child(Item item) => (Box)item.Content;

    private Box BinaryOp(Item left, string op, Item right, float kernEm = 0.25f)
    {
        var kern = new KernBox(Style.FontSize * kernEm);
        return new HBox(Child(left), kern, new GlyphBox(op, Style), kern, Child(right));
    }

    public Box Visit(Add node)      => BinaryOp(node.Arg0, "+", node.Arg2);
    public Box Visit(Subtract node) => BinaryOp(node.Arg0, "−", node.Arg2); // U+2212 minus
    public Box Visit(Eq node)       => BinaryOp(node.Arg0, "=", node.Arg2, 0.35f);
    public Box Visit(Mul node)      => BinaryOp(node.Arg0, "·", node.Arg2, 0.15f); // U+00B7 dot
    public Box Visit(Div node)      => BinaryOp(node.Arg0, "/", node.Arg2);

    /// <summary>Implicit multiplication ("xy", "n(n+1)") — a tiny kern, no operator.</summary>
    public Box Visit(Juxt node) =>
        new HBox(new KernBox(Style.FontSize * 0.05f), Child(node.Arg0), Child(node.Arg1));

    public Box Visit(Neg node) =>
        new HBox(new GlyphBox("−", Style), Child(node.Arg1));

    /// <summary>
    /// Postfix superscript. For ordinary atoms, builds a right-of-operator
    /// <see cref="SupSubBox"/>. For big operators (∫ ∑ ∏ etc.), folds into
    /// a <see cref="LimitsBox"/> so the script lands above the operator —
    /// what TeX does in display style for <c>\int^a</c>, <c>\sum^n</c>.
    /// Stacking <c>\int_0^\infty</c> works because each Sup/Subscript visit
    /// preserves the <see cref="BigOpScaffold"/>: the first script wraps
    /// the bare operator into a scaffold, the second slots into the same
    /// scaffold's other slot.
    /// </summary>
    public Box Visit(Sup node)
    {
        var smaller = Style.Smaller();
        var baseBox = Child(node.Arg0);
        if (baseBox is BigOpScaffold scaffold && !scaffold.HasUpper)
            return scaffold.WithUpper(ReBuild(node.Arg2, smaller));
        return new SupSubBox(baseBox, sup: ReBuild(node.Arg2, smaller), sub: null, Style);
    }

    /// <summary>Postfix subscript. Mirror of <see cref="Visit(Sup)"/>.</summary>
    public Box Visit(Subscript node)
    {
        var smaller = Style.Smaller();
        var baseBox = Child(node.Arg0);
        if (baseBox is BigOpScaffold scaffold && !scaffold.HasLower)
            return scaffold.WithLower(ReBuild(node.Arg2, smaller));
        return new SupSubBox(baseBox, sup: null, sub: ReBuild(node.Arg2, smaller), Style);
    }

    public Box Visit(Number node) => new GlyphBox((string)node.Arg0.Content, Style);
    public Box Visit(Variable node) => new GlyphBox((string)node.Arg0.Content, Style);

    /// <summary>
    /// \name commands. Greek + symbol lookups produce a single Unicode glyph;
    /// function names like \sin become an upright-style multi-letter atom;
    /// unknown commands fall back to the raw \name text so it's debuggable.
    /// </summary>
    public Box Visit(Command node)
    {
        var raw = (string)node.Arg0.Content;
        var name = raw.Length > 1 && raw[0] == '\\' ? raw.Substring(1) : raw;
        var glyph = Commands.TryGetValue(name, out var g) ? g : raw;
        if (LimitOps.Contains(name))
        {
            // Display-style big operator: render the glyph at 1.5x and wrap
            // in a scaffold so subsequent _/^ scripts fold into a LimitsBox
            // (limits above/below) instead of a SupSubBox (right-of).
            return new BigOpScaffold(new GlyphBox(glyph, Style, Style.FontSize * 1.5f), null, null, Style);
        }
        return new GlyphBox(glyph, Style);
    }

    /// <summary>(E) — render a parenthesised expression with scalable parens.</summary>
    public Box Visit(Paren node) => new BracketBox(Child(node.Arg1), BracketKind.Paren, Style);

    /// <summary>{ E } in LaTeX is an invisible group — render the inner E unchanged.</summary>
    public Box Visit(Group node) => Child(node.Arg1);

    public Box Visit(Sqrt node) => new SqrtBox(Child(node.Arg1), Style);

    public Box Visit(Frac node) => new FracBox(Child(node.Arg1), Child(node.Arg2), Style);

    /// <summary>
    /// Re-build a sub-tree at a smaller font size. The parser already turned
    /// the inner sub-expression into a Box at the *outer* font size — for
    /// scripts we want the sub-tree at a smaller size, so we rebuild from
    /// the script's leaves. For atom scripts (digits, single letters,
    /// commands) the parser-built Box is a single <see cref="GlyphBox"/>,
    /// which we rebuild at the smaller style (using the new
    /// <see cref="GlyphBox.Text"/> property exposed in DIR.Lib 2.8) so the
    /// glyph itself shrinks — what TeX does for x², n³ etc.
    ///
    /// For composite scripts (<c>x^{a+b}</c>) the parser-built Box is an
    /// HBox whose children were also built at parent style; we'd need a
    /// deeper rebuild from the AST to truly shrink them. The current
    /// implementation passes composites through unchanged — accepting that
    /// inner glyphs read slightly large until the visitor threads style
    /// context through every Visit method. Good enough for the demo corpus.
    /// </summary>
    private static Box ReBuild(Item arg, BoxStyle smaller)
    {
        var box = (Box)arg.Content;
        if (box is GlyphBox gb)
            return new GlyphBox(gb.Text, smaller);
        return box;
    }

    private static readonly Dictionary<string, string> Commands = new(System.StringComparer.Ordinal)
    {
        // Lowercase Greek
        ["alpha"] = "α", ["beta"] = "β", ["gamma"] = "γ", ["delta"] = "δ",
        ["epsilon"] = "ε", ["zeta"] = "ζ", ["eta"] = "η", ["theta"] = "θ",
        ["iota"] = "ι", ["kappa"] = "κ", ["lambda"] = "λ", ["mu"] = "μ",
        ["nu"] = "ν", ["xi"] = "ξ", ["pi"] = "π", ["rho"] = "ρ",
        ["sigma"] = "σ", ["tau"] = "τ", ["upsilon"] = "υ", ["phi"] = "φ",
        ["chi"] = "χ", ["psi"] = "ψ", ["omega"] = "ω",
        // Uppercase Greek (ones that aren't Latin lookalikes)
        ["Gamma"] = "Γ", ["Delta"] = "Δ", ["Theta"] = "Θ", ["Lambda"] = "Λ",
        ["Xi"] = "Ξ", ["Pi"] = "Π", ["Sigma"] = "Σ", ["Phi"] = "Φ",
        ["Psi"] = "Ψ", ["Omega"] = "Ω",
        // Function names — kept multi-letter; rendered as upright run.
        ["sin"] = "sin", ["cos"] = "cos", ["tan"] = "tan",
        ["sec"] = "sec", ["csc"] = "csc", ["cot"] = "cot",
        ["arcsin"] = "arcsin", ["arccos"] = "arccos", ["arctan"] = "arctan",
        ["sinh"] = "sinh", ["cosh"] = "cosh", ["tanh"] = "tanh",
        ["log"] = "log", ["ln"] = "ln", ["exp"] = "exp",
        ["lim"] = "lim", ["max"] = "max", ["min"] = "min",
        ["det"] = "det", ["dim"] = "dim", ["gcd"] = "gcd",
        // Big operators
        ["sum"] = "∑", ["prod"] = "∏", ["int"] = "∫", ["oint"] = "∮",
        ["bigcup"] = "⋃", ["bigcap"] = "⋂",
        // Constants and relation symbols
        ["infty"] = "∞", ["partial"] = "∂", ["nabla"] = "∇",
        ["pm"] = "±", ["mp"] = "∓",
        ["to"] = "→", ["leftarrow"] = "←", ["rightarrow"] = "→",
        ["leq"] = "≤", ["geq"] = "≥", ["neq"] = "≠",
        ["approx"] = "≈", ["equiv"] = "≡",
        ["in"] = "∈", ["notin"] = "∉", ["subset"] = "⊂",
        ["cup"] = "∪", ["cap"] = "∩",
    };

    /// <summary>
    /// Commands that take their <c>_</c>/<c>^</c> as below/above limits in
    /// display style (the LimitsBox rendering) rather than as right-of
    /// scripts. Big operators (∑ ∏ ∫ ∮ ⋃ ⋂) plus the limit-style functions
    /// (lim sup inf max min etc.).
    /// </summary>
    private static readonly HashSet<string> LimitOps = new(System.StringComparer.Ordinal)
    {
        "sum", "prod", "int", "oint", "bigcup", "bigcap",
        "lim", "limsup", "liminf", "max", "min", "sup", "inf",
        "argmax", "argmin", "det", "gcd",
    };

    /// <summary>
    /// Transient marker box: a big operator that hasn't yet absorbed its
    /// scripts. <see cref="Visit(Sup)"/> and <see cref="Visit(Subscript)"/>
    /// look for this on their base — if found, they fold the script into
    /// this scaffold's empty slot and return a new scaffold (still itself
    /// a Box, materialising into a <see cref="LimitsBox"/> on demand). The
    /// double-script case <c>\int_0^\infty</c> walks Subscript-then-Sup
    /// (or Sup-then-Subscript) over the same scaffold, ending with both
    /// slots filled before any consumer asks for Width/Height/Draw.
    ///
    /// Standalone <c>\sum</c> (no scripts) flows through unchanged: the
    /// scaffold materialises to a LimitsBox with both slots null, which
    /// just renders the centred operator with no limits.
    /// </summary>
    private sealed class BigOpScaffold : Box
    {
        private readonly Box _base;
        private readonly Box? _lower;
        private readonly Box? _upper;
        private readonly BoxStyle _style;
        private LimitsBox? _materialized;

        public BigOpScaffold(Box @base, Box? lower, Box? upper, BoxStyle style)
        {
            _base = @base;
            _lower = lower;
            _upper = upper;
            _style = style;
        }

        public bool HasLower => _lower is not null;
        public bool HasUpper => _upper is not null;

        public BigOpScaffold WithLower(Box lower) => new(_base, lower, _upper, _style);
        public BigOpScaffold WithUpper(Box upper) => new(_base, _lower, upper, _style);

        private LimitsBox Materialize() => _materialized ??= new LimitsBox(_base, _lower, _upper, _style);

        public override float Width => Materialize().Width;
        public override float Height => Materialize().Height;
        public override float Depth => Materialize().Depth;

        public override void Draw(RgbaImageRenderer renderer, float penX, float baselineY, BoxStyle style)
            => Materialize().Draw(renderer, penX, baselineY, style);
    }
}
