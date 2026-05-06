# CodeProject.Syntax.LALR

A modernized LALR(1) parser-table generator and runtime for C#, originally adapted
from Phillip Voyle's CodeProject article
[*LALR Parse Table Generation in C#*](https://web.archive.org/web/20241115071153/http://www.codeproject.com/Articles/252399/LALR-Parse-Table-Generation-in-Csharp).
The parsing loop follows the GOLD parser pseudocode; the lexer pipeline was rewritten
to consume UTF-8 bytes end-to-end via `System.IO.Pipelines` and a typed, byte-level
DFA.

Grammars can be defined inline in C# **or** declaratively in `*.lalr.yaml` files
that a Roslyn source generator turns into a typed schema, AST records, and a
visitor surface at build time.

> **Status (May 2026):** .NET 10, C# 14, Native AOT-clean, xUnit v3 test suite
> (304 tests). Library code is allocation-light, reflection-free, and trim-/AOT-
> compatible. Single NuGet package ships the runtime *and* the source generator.

---

## Quick start (NuGet)

```xml
<ItemGroup>
  <PackageReference Include="CodeProject.Syntax.LALR" Version="2.0.0" />
  <AdditionalFiles  Include="grammar.lalr.yaml" />
</ItemGroup>
```

A minimal YAML grammar — a tiny calculator:

```yaml
# grammar.lalr.yaml
# Index 0 is the start symbol. The parser returns as soon as the stack
# settles on the start symbol, so make it distinct from the recursive
# expression — otherwise the first reduction terminates the parse before
# any operator gets matched.
symbols: [S, E, '+', n, WS]
productions:
  - derivation: none
    rules:
      - { lhs: S, rhs: [E] }   # start production; default Reduction passes E through
  - derivation: leftmost
    rules:
      - { lhs: E, rhs: [E, '+', E], action: add }
      - { lhs: E, rhs: [n],         action: number }
lexer:
  root:
    - { symbol: n,   match: '[0-9]+' }
    - { symbol: '+', match: '\+' }
    - { symbol: WS,  match: '[ \t]+', action: ignore }
```

The generator emits a partial class named after the YAML file
(`grammar.lalr.yaml` → `Grammar`), with a populated `Schema` property, one
record per action, and an `IVisitor` interface:

```csharp
using CodeProject.Syntax.LALR;
using CodeProject.Syntax.LALR.LexicalGrammar;
using CodeProject.Syntax.LALR.Schema;

class Calc : Grammar.IVisitor<int>
{
    public int Visit(Grammar.Number node) => int.Parse((string)node.Arg0.Content);
    public int Visit(Grammar.Add node)    => (int)node.Arg0.Content + (int)node.Arg2.Content;
}

var (g, lex) = Grammar.Build(new Calc());
var parser = new Parser(g);
using var lexer = PipeBytesLexer.FromString("1 + 2 + 3 + 4", lex);
using var tokens = new AsyncLATokenIterator(lexer);
var result = await parser.ParseInputAsync(tokens);
Console.WriteLine(result.Content);  // 10
```

The full working version is in [`examples/Calculator/`](examples/Calculator).
For a non-toy example see [`examples/Json/`](examples/Json) — a real JSON
parser in ~50 visitor lines that builds `Dictionary<string,object>` /
`List<object>` / primitives. [`examples/Latex/`](examples/Latex) renders
Wikipedia-style math formulas (\frac, \sqrt, scripts, Greek letters, big
operators) to Unicode plain text — `\frac{n(n+1)}{2}` → `(n(n + 1))⁄2` —
and [`examples/LatexConsole/`](examples/LatexConsole) takes the same
grammar through a different visitor that builds a TeX-style box layout
and rasterises it to the terminal as sixel / Unicode sextant blocks /
half-block ASCII art. One grammar, two visitors — the reusability story
the source generator was built for.

---

## Design goals

The original 2011 article was a teaching codebase for parse-table construction.
This fork keeps the algorithmic core but pursues a different set of properties:

1. **AOT-first.** The library is annotated `IsAotCompatible=true` and
   `IsTrimmable=true`. No reflection, no `dynamic`, no runtime code generation.
   `dotnet publish` on the demo executables produces a single ~2.5 MB native exe
   with zero AOT analyzer warnings.
2. **Bytes-to-tokens, no UTF-16 detour.** Input flows from a `PipeReader` directly
   into a UTF-8 byte DFA. There's no per-character `string`, no
   `System.Text.RegularExpressions.Regex`, and no `StreamReader` in the hot loop.
   UTF-8 decoding happens once per token, at a token boundary, into the token's
   `Content`.
3. **Async at I/O boundaries, sync in hot loops.** `PipeReader.ReadAsync` is the
   only `await` per buffer. The byte DFA loops sync across `ReadOnlySequence<byte>`
   segments; the parser loop awaits only when fetching the next token.
4. **Immutable data structures.** Every grammar/parser-table value type
   (`Production`, `PrecedenceGroup`, `Grammar`, `LR0Item`, `LR1Item`, `Action`,
   `ParseTable`, `SymbolName`, `LexRule`, the regex-AST nodes…) is a `readonly
   struct` with primary constructors and `IEquatable<T>` where equality matters.
5. **Typed regex AST.** Lexer patterns are built from `IRx` combinators
   (`CharRx`, `CharRangeRx`, `CharClassRx`, `CharSequenceRx`, `GroupRx`,
   `Multiplicity`) instead of opaque regex strings. The AST is compiled once,
   at lexer construction, into a UTF-8 byte DFA — no runtime regex engine.
6. **Grammars as data.** YAML files hold the grammar shape; a build-time source
   generator emits the schema, AST records, and a typed visitor. Inline C#
   grammars still work, but the YAML path is the recommended one — you write
   data, the generator does the boilerplate.
7. **Small public surface.** The library targets `net10.0`, depends solely on
   the BCL at runtime (YamlDotNet runs only inside the build-time generator),
   and exposes `Grammar`, `Parser`, `PipeBytesLexer`, `IRx` combinators,
   `LexRule`, `Item`, the `Schema/` types, and the source-generated YAML
   surface.

---

## Solution layout

| Project | Type | Purpose |
|---|---|---|
| `CodeProject.Syntax.LALR/` | Library (net10.0, AOT-compatible, trimmable) | Grammar model, LALR(1) parse-table generator, runtime parser, lexer infrastructure (`PipeBytesLexer`, `IRx` combinators, byte-DFA compiler), `Item` value type, `Schema/` POCOs + compiler. **The published NuGet package**. |
| `CodeProject.Syntax.LALR.SourceGenerators/` | Roslyn analyzer (netstandard2.0, `IsRoslynComponent=true`) | YAML grammar source generator. Reads `*.lalr.yaml` AdditionalFiles at build time, emits `<ClassName>.g.cs` (schema), `<ClassName>.Ast.g.cs` (record per action), and `<ClassName>.Visitor.g.cs` (typed `IVisitor` interface + `BuildActions`). YamlDotNet is build-time only. |
| `Bootstrap/` | Exe (`PublishAot=true`) | **Stage 0**: hand-codes the BNF meta-grammar in C# and parses a BNF source string with the resulting parser. Reference implementation — depends only on the runtime library, no generator, no YAML. |
| `Bootstrap.Stage1/` | Exe (`PublishAot=true`) | **Stage 1**: same BNF meta-grammar, but defined in `bnf.lalr.yaml` and consumed via the source generator + visitor pipeline. CI diffs stage0 ↔ stage1 Accept output for byte-identical parity. |
| `TestProject/` | Exe (`PublishAot=true`) | Arithmetic-expression demo with operator precedence and constant folding during reduction. Inline C# grammar; uses an inline tokenizer instead of `PipeBytesLexer` to show that any `IAsyncIterator<Item>` plugs in. |
| `Tui/` (asm `lalr-tui`) | Exe (`PublishAot=false`) | Interactive terminal grammar debugger. Loads `*.lalr.yaml` live, runs `SchemaCompiler`, builds a `Parser`, displays grammar / lexer rules / token stream / parse-table cells in a `Console.Lib` dock layout. JIT-only because YamlDotNet runtime deserializer needs reflection — and Tui's purpose is loading arbitrary YAML at runtime. |
| `examples/Calculator/` | Exe | Smallest end-to-end YAML-pipeline demo: a calculator grammar (`1 + 2 + 3 + 4 - 5 = 5`) with three visitor methods. Mirrors the README quick-start. |
| `examples/Json/` | Exe | Real JSON parser via the YAML pipeline. ~50-line `IVisitor` implementation builds `Dictionary<string,object>` / `List<object>` / primitives. |
| `examples/Latex.Grammar/` | Library | Shared LaTeX grammar partial class. Source generator runs once on `latex.lalr.yaml` and emits the `Latex` partial (Schema + AST records + `IVisitor<T>`). Both LaTeX consumers `ProjectReference` this — one grammar, multiple visitors. |
| `examples/Latex/` | Exe (`PublishAot=true`) | Wikipedia-style LaTeX math formulas via the shared `Latex.Grammar`. Visitor renders to Unicode plain text. |
| `examples/LatexConsole/` | Exe (`PublishAot=true`) | Same LaTeX grammar, different visitor: builds a `DIR.Lib.MathLayout.Box` tree (TeX-style box layout — fraction bars, scalable square-root vinculums, baseline-aligned scripts, big-operator limits) and `Console.Lib.BoxRenderer` paints it as sixel / Unicode sextant blocks / half-block ASCII art. NuGet deps: `Console.Lib` (terminal adapters) → `DIR.Lib` (RGBA renderer + font rasteriser + math-layout primitives). |
| `CodeProject.Syntax.LALR.Tests/` | xUnit v3 (Microsoft.Testing.Platform) | 304 tests covering the regex-AST builders, byte/codepoint DFAs, lexer/parser pipeline, diagnostics, schema layer, the source generator (incl. end-to-end "emit → compile → load → parse"), and parser semantics regressions. |

Shared MSBuild settings (`TargetFramework=net10.0`, `LangVersion=14`, deterministic
build, etc.) live in `Directory.Build.props`. NuGet metadata, symbol packages,
SourceLink, and the bundled-analyzer pack target live on `CodeProject.Syntax.LALR.csproj`.

---

## Architecture: how a parse happens

Three stages plus the parser. Each implements or consumes
`IAsyncIterator<T>` / `IAsyncLAIterator<T>` (the LA variant adds one-token
lookahead).

```
                 ┌──────────────────────────────────────────────┐
 UTF-8 bytes ──▶ │  PipeBytesLexer                              │ ──▶ Item tokens
   (Pipe)        │  • per-state byte DFA                        │
                 │  • IRx pattern → codepoint NFA → codepoint   │
                 │    DFA → UTF-8 byte DFA, all at construction │
                 │  • longest match, first-rule-wins on ties    │
                 │  • #pop / #ignore / push-state instructions  │
                 │  • UTF-8 decode once, at token boundary      │
                 └──────────────────────────────────────────────┘
                                       │
                                       ▼
                 ┌──────────────────────────────────────────────┐
                 │  AsyncLATokenIterator                        │ ──▶ Item + 1-token LA
                 │  • adapts any IAsyncIterator<Item> to LA     │
                 └──────────────────────────────────────────────┘
                                       │
                                       ▼
                 ┌──────────────────────────────────────────────┐
                 │  Parser                                      │ ──▶ Item (root reduction)
                 │  • parse table built once in constructor     │
                 │  • ParseInputAsync runs the standard         │
                 │    shift/reduce/accept/error loop            │
                 │  • applies per-production rewriters          │
                 └──────────────────────────────────────────────┘
```

### Stage 1: `PipeBytesLexer`

`LexicalGrammar/PipeBytesLexer.cs`. Owns a `System.IO.Pipelines.PipeReader`,
emits `Item` tokens.

The lexer is configured with a state table:
`IReadOnlyDictionary<string, LexRule[]>` where `LexRule = (int symbolId, IRx pattern, string instruction)`.
At construction, each state's rules are compiled into a single byte DFA:

1. `DfaCompiler.CompileMany` builds a codepoint NFA via Thompson construction
   from the typed `IRx` AST, then determinizes it with subset construction. Each
   rule's index becomes its DFA pattern id; on overlap the smaller id wins (so
   the rule listed first wins on equal-length matches).
2. `Utf8DfaLowering.Lower` converts each codepoint transition `[lo..hi] → s`
   into one or more UTF-8 byte chains, splitting along the 1/2/3/4-byte
   boundaries and using the standard three-way split (Russ Cox) when leading
   bytes differ. Surrogate codepoints D800..DFFF are excluded — they have no
   valid UTF-8 encoding.

The hot loop in `MoveNextAsync` reads `ReadOnlySequence<byte>` segments from the
pipe and steps the DFA byte-by-byte (sync). On a longest accepting prefix, the
matched bytes are UTF-8-decoded once into `Item.Content`; the rule's
`Instruction` drives the state stack:

| Instruction | Effect |
|---|---|
| `null` / empty | stay in the current state |
| `PipeBytesLexer.Ignore` (`"#ignore"`) | drop the matched token, restart scanning |
| `PipeBytesLexer.PopState` (`"#pop"`) | pop one state off the stack |
| any other string | push that state name |

Factories: `PipeBytesLexer.FromBytes(ReadOnlyMemory<byte>, …)`,
`FromStream(Stream, …)`, `FromString(string, …)`.

**Errors fail fast.** When input bytes don't match any rule in the current
state, the lexer's `LexerErrorMode` decides what to do:

| Mode | Effect |
|---|---|
| `Throw` (default) | Raise `LexerException` carrying the offending byte, `SourcePosition`, and lexer state name. |
| `EmitAndStop` | Emit one error `Item` (`IsError==true`, `Content` is the hex byte), then return `false` from subsequent `MoveNextAsync` calls. |
| `EmitAndSkip` | Emit an error `Item`, advance the cursor by one byte, continue scanning. |

`EmitAndStop` and `EmitAndSkip` require a non-negative `errorSymbolId` argument
so the emitted `Item` is identifiable by the consumer. Clean EOF (no bytes
remaining) is always silent — it never triggers any error mode.

### Stage 1.5: `PipeRuneIterator` (alternative input path)

`LexicalGrammar/PipeRuneIterator.cs`. Standalone codepoint iterator
(`IAsyncLAIterator<int>`) that decodes UTF-8 bytes directly to Unicode scalars
via `Rune.DecodeFromUtf8`. Not used by the parse pipeline — `PipeBytesLexer`
goes from bytes to tokens directly — but available for callers that want
codepoints.

### Stage 2: `AsyncLATokenIterator`

Wraps any `IAsyncIterator<Item>` and adds one-token lookahead so the parser can
decide between shift and reduce at any state.

### Stage 3: `Parser`

`Parser.cs` + `ParserTableBuilder.cs`. The `Parser` constructor delegates to
`ParserTableBuilder` to build the LALR(1) parse table eagerly from the
`Grammar` (failing fast with `GrammarConflictException` on any unresolved S/R
or R/R conflict). The split exists so the same algorithm can be linked into
the netstandard2.0 source generator and run at compile time as part of Phase 5
(pre-baked tables / compiler-compiler mode).
`ParseInputAsync(IAsyncLAIterator<Item>, Debug?, …)` runs the standard parse
loop, consulting `ParseTable.Actions[state, tokenId+1]` for every
shift/reduce/accept/error decision. After each reduce the matching
`Production`'s rewriter (`Func<int, Item[], object>`) builds the reduced node's
`Content`; productions without a rewriter fall back to a default `Reduction`.
Visitors that legitimately want to return C# `null` are honoured —
`Production.HasRewriter` distinguishes "rewriter returned null" from "no
rewriter at all".

The parse loop honors a `ParserErrorMode` and a `CancellationToken`:

| Parameter | Effect |
|---|---|
| `errorMode: ParserErrorMode.Throw` (default) | Raise `ParseErrorException` on the first parse error. The exception carries the offending `Item`, the LALR(1) state, and the set of symbol ids that would have been valid (`ExpectedSymbolIds`). |
| `errorMode: ParserErrorMode.Return` | Legacy: return the offending `Item` (with `IsError==true`) so the caller can produce diagnostics or feed it into a partial parse tree. |
| `cancellationToken: ct` | Checked once per parse-loop iteration via `ThrowIfCancellationRequested`. |
| `debugger: null` (default) | No tracing — methods on `Debug` are `[Conditional("DEBUG")]` anyway, but this skips even the call site. |

`Item` is the unifying value type for terminal tokens *and* reduced
non-terminals. Its `ContentType` is `Scalar` (raw lexer string), `Reduction`
(default), or `Nested`. Tokens that bubble up through `IsError` propagate error
state through reductions automatically. Every `Item` carries a `SourcePosition`
(1-based `Line`, byte-based 1-based `Column`, absolute `ByteOffset`).

---

## Two ways to define a grammar

### Inline C# (`Bootstrap/Program.cs`, `TestProject/Main.cs`)

```csharp
new Grammar(
    symbolTable,                                    // SymbolName[] — id == index
    new PrecedenceGroup(Derivation.None,            // tightest binding first
        new Production((int)S.Result, (_, x) => x[0], (int)S.Expr)),
    new PrecedenceGroup(Derivation.LeftMost,
        new Production((int)S.Expr, RewriteBinary, (int)S.Expr, (int)S.Plus, (int)S.Expr)));
```

| Type | Purpose |
|---|---|
| `SymbolName(int id, string name)` | Terminal/non-terminal entry; id == index in the symbol table; symbol id 0 is the start symbol. |
| `Production(int left, Func<int, Item[], object> rewriter, params int[] right)` | A production with an optional semantic rewriter. The rewriter receives the LHS id and the matched right-hand `Item`s; whatever it returns becomes the reduced item's `Content`. |
| `PrecedenceGroup(Derivation, params Production[])` | A bundle of productions sharing precedence. Earlier groups bind more tightly; `Derivation` (`None` / `LeftMost` / `RightMost`) decides shift-vs-reduce and reduce-vs-reduce conflicts. |

Inline grammars are still useful for small, static cases (and are how stage0
`Bootstrap` and `TestProject` are written), but they entangle grammar shape
with action code.

### YAML + source generator (recommended)

Author `<name>.lalr.yaml` next to your code, list it under `<AdditionalFiles>`
in your csproj, and the source generator emits three companion files:

| Emitted file | Contents |
|---|---|
| `<Name>.g.cs` | `public static partial class <Name> { public static GrammarSchema Schema { get; } = new() { … }; }` |
| `<Name>.Ast.g.cs` | One `public sealed record <Action>(Item Arg0, …)` per distinct `action:` name in the YAML. |
| `<Name>.Visitor.g.cs` | A nested `public interface IVisitor<out T>` with one `T Visit(<Record>)` overload per record, plus `public static IReadOnlyDictionary<string, Func<int, Item[], object>> BuildActions<T>(IVisitor<T> visitor)` that constructs records from the parser's reduction frame and dispatches by C# overload resolution. Use `IVisitor<int>` for an evaluator, `IVisitor<object>` when methods need different shapes per production. |

Your code implements `IVisitor`, calls `Schema/SchemaCompiler.Compile(Schema, BuildActions(visitor))`,
and feeds the result to a `Parser`. See the Quick start above and `examples/Json/`
for a worked example. `bnf.lalr.yaml` (Bootstrap.Stage1) is a larger reference
showing a multi-state lexer (push-pop quoted strings) and 17 actions.

The YAML schema is documented inline in `CodeProject.Syntax.LALR/Schema/GrammarSchema.cs`.
The regex dialect for `match:` patterns is described in
`CodeProject.Syntax.LALR/Schema/IRxParser.cs` — it's deliberately small (no
alternation; express alternatives as multiple lexer rules).

---

## Examples

### `Bootstrap` — self-describing BNF (stage 0)

Hand-codes a grammar in C# that *describes the BNF notation itself*, then parses
a BNF source string with the resulting parser. Reference implementation that
depends only on the runtime library — no source generator, no YAML, always
buildable from a clean checkout. Useful as a regression baseline.

### `Bootstrap.Stage1` — same grammar, YAML-driven (stage 1)

Same BNF meta-grammar, but defined in `bnf.lalr.yaml` and consumed via the
generator + a typed `IVisitor` implementation. Demonstrates a full multi-state
lexer (push state on `"`, pop on the matching close). CI diffs stage0 ↔ stage1
Accept output to catch any regression in the YAML/schema/generator stack.

### `TestProject` — arithmetic with constant folding

`+ − × ÷` with parentheses, a per-production rewriter that does constant
folding during reduction (e.g. `(24 / 12) + 2 * (3-4)` reduces to `0` at parse
time), and an inline tokenizer instead of `PipeBytesLexer` to show that any
`IAsyncIterator<Item>` plugs in.

### `examples/Calculator` — minimal YAML demo

Three productions, one lexer state, two visitor methods. Mirrors the README
quick-start. The smallest grammar that exercises every interesting bit of the
YAML pipeline (start production, leftmost-derivation conflict resolution,
ignored whitespace, an emitted record per action).

### `examples/Json` — real JSON via the YAML pipeline

`json.lalr.yaml` (16 productions) plus a ~50-line `IVisitor` implementation
that builds `Dictionary<string, object>` / `List<object>` / primitives. The
canonical "non-toy grammar end-to-end via the new pipeline" reference. Lexer
limitations (no backslash escapes inside strings) are documented in the YAML
header.

### `examples/Latex` — Wikipedia-style math formulas (Unicode renderer)

`latex.lalr.yaml` (in `examples/Latex/`, consumed via the shared `Latex.Grammar`
library) plus an `IVisitor<string>` that pretty-prints to Unicode.
Six precedence levels (`E → T → F → F2 → P → A`) with a deliberate F/F2
split so that juxtaposition (`mc^2`, `n(n+1)`) coexists with subtraction
without an S/R conflict. Handles `\frac{a}{b}`, `\sqrt{x}`, `^` / `_`
scripts, `()` / `{}` grouping, and a catchall `\name` lexer rule fed by a
Greek/operator/function table — `\sum`, `\int`, `\alpha`, `\sin`, `\infty`,
etc. Sample input/output pairs:

```
\frac{1}{2} + \frac{1}{3}            →  1⁄2 + 1⁄3
\sqrt{x^2 + y^2}                     →  √(x² + y²)
\sum_{i=0}^{n} i = \frac{n(n+1)}{2}  →  ∑_(i = 0)ⁿi = (n(n + 1))⁄2
\int_0^\infty e^{-x^2} dx            →  ∫₀^∞e^(−x²)dx
```

Doubles as a stress test for the parser: its `A → cmdfrac A A` rule (two
non-terminals adjacent in an RHS) was the first grammar to surface a long-
standing latent bug in `Parser.cs` where reductions stashed the LHS symbol
id on `Item.State` instead of the goto-target parser state, mis-routing
`Peek().State` lookups when one reduction sat below another reduction's
children. See `CLAUDE.md` § "Examples are stress tests, not safe demos".

### `examples/LatexConsole` — same grammar, terminal-rasterised math

Same `latex.lalr.yaml` (consumed via the shared `Latex.Grammar` library —
generator runs once, both consumers see identical emitted code), but the
visitor builds a `DIR.Lib.MathLayout.Box` tree instead of a string. The Box
tree is a TeX-lite layout engine: `GlyphBox` for atoms, `HBox` / `KernBox`
for horizontal composition, `FracBox` / `SqrtBox` for stacked structures
with bars and vinculums, `SupSubBox` / `LimitsBox` for scripts (right-of for
ordinary atoms, above/below for big operators like `\sum`, `\int`).
`Console.Lib.BoxRenderer` then paints the root Box into an RGBA buffer and
ships it to the terminal as one of three encodings, auto-detected via DA1
device-attribute query:

| Encoding | Fidelity | Requires |
|---|---|---|
| Sixel | best (1px granularity) | sixel-capable terminal (Windows Terminal 1.22+, iTerm2, mintty, xterm +sixel, kitty's image protocol mapper) |
| Unicode sextant | good (2×3 sub-pixels per cell) | modern Unicode 13 fonts (most current terminal emulators) |
| Half-block ASCII | universal but coarse | any 24-bit-colour terminal |

Pulls `Console.Lib` (terminal adapters) → `DIR.Lib` (RGBA renderer +
font rasteriser + the math-layout primitives) from NuGet. Demonstrates the
grammar-as-a-reusable-artefact story end-to-end: one YAML, one source-generator
run, two completely different output channels.

---

## Building, testing, releasing

```bash
# Build the whole solution
dotnet build CodeProject.Syntax.LALR.sln -c Debug          # or -c Release

# Run all tests (xUnit v3 on Microsoft.Testing.Platform — 304 tests)
dotnet test CodeProject.Syntax.LALR.Tests/CodeProject.Syntax.LALR.Tests.csproj -c Debug

# Run a subset of tests (MTP --filter-method, glob-syntax)
dotnet run --project CodeProject.Syntax.LALR.Tests/CodeProject.Syntax.LALR.Tests.csproj -c Debug -- --filter-method "*PipeBytesLexer*"

# Run the demos / examples end-to-end
dotnet run --project Bootstrap/Bootstrap.csproj                    -c Release    # stage 0 (inline grammar)
dotnet run --project Bootstrap.Stage1/Bootstrap.Stage1.csproj      -c Release    # stage 1 (YAML pipeline)
dotnet run --project TestProject/TestProject.csproj                -c Release    # arithmetic + constant folding
dotnet run --project examples/Calculator/Examples.Calculator.csproj -c Release  # minimal YAML demo
dotnet run --project examples/Json/Examples.Json.csproj            -c Release    # real JSON via visitor
dotnet run --project examples/Latex/Examples.Latex.csproj          -c Release    # Wikipedia-style math → Unicode
dotnet run --project examples/LatexConsole/Examples.LatexConsole.csproj -c Release  # same grammar → terminal raster
dotnet run --project Tui/CodeProject.Syntax.LALR.Tui.csproj        -c Release    # interactive grammar debugger (lalr-tui)

# Native AOT publish (verifies library + AOT-flagged consumers stay AOT-clean)
dotnet publish Bootstrap/Bootstrap.csproj                                -c Release
dotnet publish Bootstrap.Stage1/Bootstrap.Stage1.csproj                  -c Release
dotnet publish TestProject/TestProject.csproj                            -c Release
dotnet publish examples/LatexConsole/Examples.LatexConsole.csproj        -c Release

# Local NuGet pack (runtime + bundled source generator + YamlDotNet)
dotnet pack CodeProject.Syntax.LALR/CodeProject.Syntax.LALR.csproj -c Release -o packages
```

The demo binaries print step-by-step parse traces in Debug; in Release the
`[Conditional("DEBUG")]` traces drop out and only the final accept/reject line
is printed.

### CI/CD

`.github/workflows/dotnet.yml` runs on every push and PR:

- Restore, build (Release), test, stage0-vs-stage1 Accept-output diff.
- On master push: also pack the runtime nupkg + snupkg and upload as a build artifact.
- On `v*` tag push: verify the tag matches the csproj `<Version>`, then publish
  to NuGet using the `NUGET_SECRET` repo secret (`--skip-duplicate` for
  idempotent re-runs). The `.snupkg` is auto-routed to symbols.nuget.org.

To cut a release: bump `<Version>` in `CodeProject.Syntax.LALR/CodeProject.Syntax.LALR.csproj`,
commit, tag `vX.Y.Z` matching the version, push the tag.

---

## Roadmap and known limitations

The project is usable as-is and on NuGet. Items still on the list, ranked:

- **Phase 5 — pre-baked parse tables (compiler-compiler mode).** Today the
  generator emits a `GrammarSchema` POCO; consumers call
  `SchemaCompiler.Compile(schema, …)` at runtime to build the LALR(1) parse
  table. Phase 5 runs the schema compiler + `ParserTableBuilder` *at build
  time* and emits the populated `Grammar` + `ParseTable.Actions[,]` directly,
  trimming table-build code out of consumer AOT images and surfacing S/R + R/R
  conflicts as `LALR0004` Roslyn diagnostics with YAML locators (instead of
  runtime `GrammarConflictException`). Slice 1 (extract `ParserTableBuilder`
  from `Parser.cs` so the algorithm becomes pure C# and linkable into the
  netstandard2.0 generator) has landed; slices 2–5 (link, emit, diagnose,
  migrate consumers) are in progress.
- **Generator-time regex validation.** Slice 1 of generator-time validation
  (`LALR0003` — structural errors via `SchemaValidator`) shipped in 2.1.0.
  Linking `IRxParser` into the generator would surface bad `match:` regexes
  at build time too. Naturally pairs with Phase 5 / slice 2 (same linking
  work).
- **Codepoint columns on `Item.SourcePosition`.** `Column` is byte-based
  (documented). An optional codepoint-column decoder is available via
  `SourcePosition.GetCodepointColumn(ReadOnlySpan<byte> source)` for
  diagnostics-quality positions on non-ASCII grammars.
- **More example grammars.** TOML config, C declaration syntax (the famous
  "Lexer Hack"), an arithmetic-with-unary-ops upgrade for `TestProject`. Each
  ships under `examples/`.
- **No alternation inside a single `IRx` pattern.** Use multiple `LexRule`s in
  the same state to express alternatives. (Adding `|` to the AST is small;
  hasn't been needed yet.)
- **Async-per-token at the parser/iterator boundary.** The lexer's inner loop
  is sync and the only `await` per byte-buffer is `PipeReader.ReadAsync`. The
  remaining async-per-token cost lives in `IAsyncIterator<Item>` callers and
  the parser loop. A pure-sync, `ref struct`-style parser is possible; not a
  priority.

---

## Provenance & license

Originally adapted from Phillip Voyle's
[*LALR Parse Table Generation in C#*](https://web.archive.org/web/20241115071153/http://www.codeproject.com/Articles/252399/LALR-Parse-Table-Generation-in-Csharp).
The core LALR algorithm follows the
[GOLD parser pseudocode](http://www.goldparser.org/doc/engine-pseudo/parse-token.htm).

Licensed under the
[Code Project Open License (CPOL) 1.02](http://www.codeproject.com/info/cpol10.aspx).
See `LICENSE.html` for the full text and `LICENSE.md` for a non-binding
summary.
