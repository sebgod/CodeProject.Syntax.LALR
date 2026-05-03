# CodeProject.Syntax.LALR

A modernized LALR(1) parser-table generator and runtime for C#, originally adapted
from Phillip Voyle's CodeProject article
[*LALR Parse Table Generation in C#*](https://web.archive.org/web/20241115071153/http://www.codeproject.com/Articles/252399/LALR-Parse-Table-Generation-in-Csharp).
The parsing loop follows the GOLD parser pseudocode; the lexer pipeline was rewritten
to consume UTF-8 bytes end-to-end via `System.IO.Pipelines` and a typed, byte-level
DFA.

> **Status (May 2026):** .NET 10, C# 14, Native AOT-clean, xUnit v3 test suite.
> Library code is allocation-light, reflection-free, and trim-/AOT-compatible.

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
   The DFA value type and its transitions follow the same shape.
5. **Typed regex AST.** Lexer patterns are built from `IRx` combinators
   (`CharRx`, `CharRangeRx`, `CharClassRx`, `CharSequenceRx`, `GroupRx`,
   `Multiplicity`) instead of opaque regex strings. The AST is compiled once,
   at lexer construction, into a UTF-8 byte DFA — no runtime regex engine.
6. **No deps the consumer doesn't already have.** The library targets `net10.0`
   only, depends solely on the BCL, and surfaces a small public API
   (`Grammar`, `Parser`, `PipeBytesLexer`, the `IRx` combinators, `LexRule`,
   `Item`).
7. **Composable pipeline.** Each stage has a clean interface (`IAsyncIterator<T>`
   / `IAsyncLAIterator<T>`). You can swap any stage — feed your own token source,
   replace the lexer, plug a different `PipeReader`. Tests do all of these.

What this project deliberately is **not**:

- **Not a parser generator that reads grammar files at build time.** Grammars
  are hand-written in C# today. Moving grammar definitions to YAML files (with
  imports) and a source-generator-driven parser-table build is the next
  architectural milestone, but it is not implemented yet.
- **Not a regex engine.** The `IRx` combinators only express a regular language
  — alternation lives at the *lexer-rule* level (multiple rules per state), not
  inside a single pattern. There is no alternation operator, no anchors, no
  backreferences.

---

## Solution layout

| Project | Type | What it does |
|---|---|---|
| `CodeProject.Syntax.LALR/` | Library (net10.0, AOT-compatible, trimmable) | Grammar model, LALR(1) parse-table generator, runtime parser, lexer infrastructure (`PipeBytesLexer`, `IRx` combinators, byte-DFA compiler), `Item` value type. |
| `Bootstrap/` | Exe (`PublishAot=true`) | End-to-end smoke test: hand-codes a BNF *meta-grammar* in C#, then parses a BNF source string with the resulting parser. |
| `TestProject/` | Exe (`PublishAot=true`) | End-to-end demo: arithmetic-expression grammar with operator precedence, evaluating constant sub-expressions during reduction. |
| `CodeProject.Syntax.LALR.SourceGenerators/` | Roslyn analyzer (netstandard2.0, `IsRoslynComponent=true`) | YAML grammar source generator. Reads `*.lalr.yaml` AdditionalFiles at build time, emits a populated `GrammarSchema` in the consumer's namespace. YamlDotNet is `PrivateAssets="all"` so it never ships to user binaries. |
| `CodeProject.Syntax.LALR.Tests/` | xUnit v3 (Microsoft.Testing.Platform; `OutputType=Exe`) | 273+ tests covering the regex-AST builders, byte/codepoint DFAs, lexer/parser pipeline, diagnostics, schema layer, and the source generator (incl. end-to-end "emit → compile → load → parse"). |

Shared MSBuild settings (`TargetFramework`, `LangVersion=14`, …) live in
`Directory.Build.props`.

---

## Architecture: how a parse happens

Three stages plus the parser. Each implements or consumes
`IAsyncIterator<T>` / `IAsyncLAIterator<T>` (the LA variant adds one-token
lookahead).

```
                 ┌──────────────────────────────────────────────┐
 UTF-8 bytes ──▶ │  PipeBytesLexer                              │ ──▶ Item tokens
   (Pipe)        │  • per-state byte DFA                         │
                 │  • IRx pattern → codepoint NFA → codepoint    │
                 │    DFA → UTF-8 byte DFA, all at construction  │
                 │  • longest match, first-rule-wins on ties     │
                 │  • #pop / #ignore / push-state instructions   │
                 │  • UTF-8 decode once, at token boundary       │
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
                 │  • parse table built once in constructor:    │
                 │    PopulateProductions → InitSymbols →       │
                 │    GenerateLR0Items → ComputeFirstSets →     │
                 │    ConvertLR0ItemsToKernels → InitLALRTables │
                 │    → CalculateLookAheads → GenerateParseTable│
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
| `Throw` (default) | Raise `LexerException` carrying the offending byte, `SourcePosition`, and lexer state name. Matches the fail-fast philosophy of `GrammarConflictException`. |
| `EmitAndStop` | Emit one error `Item` (`IsError==true`, `Content` is the hex byte e.g. `"\x7E"`), then return `false` from subsequent `MoveNextAsync` calls. |
| `EmitAndSkip` | Emit an error `Item`, advance the cursor by one byte, continue scanning. Skipping one byte (not one codepoint) is intentional — bare bad bytes and mid-sequence UTF-8 corruption both surface. |

`EmitAndStop` and `EmitAndSkip` require a non-negative `errorSymbolId` argument
so the emitted `Item` is identifiable by the consumer. Clean EOF (no bytes
remaining) is always silent — it never triggers any error mode.

### Stage 1.5: `PipeRuneIterator` (alternative input path)

`LexicalGrammar/PipeRuneIterator.cs`. Standalone codepoint iterator
(`IAsyncLAIterator<int>`) that decodes UTF-8 bytes directly to Unicode scalars
via `Rune.DecodeFromUtf8`. Not used by the parse pipeline — `PipeBytesLexer`
goes from bytes to tokens directly — but available for callers that want
codepoints (one-codepoint lookahead, EOL normalization, replacement on
truncated/invalid UTF-8). All three same-shape factories.

### Stage 2: `AsyncLATokenIterator`

Wraps any `IAsyncIterator<Item>` and adds one-token lookahead so the parser can
decide between shift and reduce at any state.

### Stage 3: `Parser`

`Parser.cs`. The `Parser` constructor builds the LALR(1) parse table eagerly
from the `Grammar`. `ParseInputAsync(IAsyncLAIterator<Item>, Debug, …)` then
runs the standard parse loop, consulting `ParseTable.Actions[state, tokenId+1]`
for every shift/reduce/accept/error decision. After each reduce it calls the
matching production's rewriter (`Func<int, Item[], object>`) to build the
reduced node's `Content`.

The parse loop honors a `ParserErrorMode` and a `CancellationToken`:

| Parameter | Effect |
|---|---|
| `errorMode: ParserErrorMode.Throw` (default) | Raise `ParseErrorException` on the first parse error. The exception carries the offending `Item`, the LALR(1) state, and the set of symbol ids that would have been valid (`ExpectedSymbolIds`) — built by scanning the parse-table row for non-error cells. |
| `errorMode: ParserErrorMode.Return` | Legacy behaviour: return the offending `Item` (with `IsError==true`) so the caller can produce diagnostics or feed it into a partial parse tree. Useful for IDE/LSP-style consumers. |
| `cancellationToken: ct` | Checked once per parse-loop iteration via `ThrowIfCancellationRequested`; cancellation surfaces as `OperationCanceledException`. |

`Item` (`LexicalGrammar/Item.cs`) is the unifying value type for terminal
tokens *and* reduced non-terminals. Its `ContentType` is `Scalar` (raw lexer
string), `Reduction` (`Reduction` with child `Item`s), or `Nested` (single child
`Item`). Tokens that bubble up through `IsError` propagate error state through
reductions automatically. Every `Item` also carries a `SourcePosition`
(1-based `Line`, byte-based 1-based `Column`, absolute `ByteOffset`); the
lexer populates it for terminal tokens and reductions inherit the leftmost
child's position (with epsilon reductions taking the lookahead's position).
`default(SourcePosition)` is `Unknown` and applies to `Item.EOF` plus any item
built without an explicit position.

`Debug.cs` is a tracer wrapped around a `Parser`; almost every method is
`[Conditional("DEBUG")]`, so Release builds drop the tracing entirely. Pass
`Console.Write` / `Console.Error.Write` to the constructor to emit traces, or
`null` to silence it.

---

## Grammar model

A `Grammar` is the symbol table plus an ordered list of `PrecedenceGroup`s.

```csharp
new Grammar(
    symbolTable,                                    // SymbolName[] — id == index
    new PrecedenceGroup(Derivation.None,            // tightest binding first
        new Production((int)S.Result, (_, x) => x[0], (int)S.Expr)),
    new PrecedenceGroup(Derivation.LeftMost,        // resolves S/R + R/R
        new Production((int)S.Expr, RewriteBinary, (int)S.Expr, (int)S.Times, (int)S.Expr),
        new Production((int)S.Expr, RewriteBinary, (int)S.Expr, (int)S.Divide, (int)S.Expr)),
    new PrecedenceGroup(Derivation.LeftMost,        // looser binding
        new Production((int)S.Expr, RewriteBinary, (int)S.Expr, (int)S.Plus, (int)S.Expr),
        new Production((int)S.Expr, RewriteBinary, (int)S.Expr, (int)S.Minus, (int)S.Expr)));
```

| Type | Purpose |
|---|---|
| `SymbolName(int id, string name)` | Terminal/non-terminal entry; id == index in the symbol table; symbol id 0 is the start symbol. |
| `Production(int left, Func<int, Item[], object> rewriter, params int[] right)` | A grammar production with an optional semantic rewriter. The rewriter receives the LHS id and the matched right-hand `Item`s; whatever it returns becomes the reduced item's `Content`. Returning `null` falls back to a default `Reduction`. |
| `PrecedenceGroup(Derivation, params Production[])` | A bundle of productions sharing precedence. Earlier groups bind more tightly; `Derivation` (`None` / `LeftMost` / `RightMost`) decides shift-vs-reduce and reduce-vs-reduce conflicts. |

**Conflicts fail at construction.** Any S/R or R/R conflict left unresolved by
the precedence groups throws `GrammarConflictException` immediately from
`new Parser(grammar)`. The exception's `Conflicts` collection enumerates each
offending state, lookahead symbol, and the competing productions; the message
identifies the symbol by name and prints each production's right-hand-side.
Resolve by placing the colliding productions in a `PrecedenceGroup` with
`Derivation.LeftMost` (prefer reduce) or `Derivation.RightMost` (prefer shift),
or by restructuring the grammar.

---

## Lexer model

A lexer is a state machine: each named state has an array of `LexRule`s, and the
initial state is `PipeBytesLexer.RootState` (`"root"`).

```csharp
const string stringState = "string";

var table = new Dictionary<string, LexRule[]>
{
    { PipeBytesLexer.RootState, [
        new(NumberId, new GroupRx(Multiplicity.OneOrMore,
            new CharClassRx(true, [new CharRangeRx('0', '9')]))),
        new(PlusId, new CharRx('+')),
        new(QuoteId, new CharRx('"'), stringState),                  // push state
        new(WsId,  new CharClassRx(true, ' ', '\t'),
            PipeBytesLexer.Ignore),                                  // drop token
    ] },
    { stringState, [
        new(QuoteId, new CharRx('"'), PipeBytesLexer.PopState),
        new(StringCharId, new CharClassRx(false, '"')),              // any char except '"'
    ] },
};

using var lexer = PipeBytesLexer.FromString(input, table);
using var tokenIterator = new AsyncLATokenIterator(lexer);
```

Patterns are built from the `IRx` combinator AST:

| Combinator | Means |
|---|---|
| `CharRx(int codepoint)` | a single Unicode codepoint |
| `CharRangeRx(int from, int to)` | inclusive codepoint range — only valid inside a `CharClassRx` |
| `CharClassRx(bool positive, …)` | union of single-char nodes; `positive=false` complements |
| `CharSequenceRx("…")` or `CharSequenceRx(params CharRx[])` | concatenation of literal codepoints |
| `GroupRx(Multiplicity, params IRx[])` | concatenation + repetition |
| `Multiplicity` | `Once`, `ZeroOrOnce`, `ZeroOrMore`, `OneOrMore`, or any `(from, to)` |

There is no alternation inside a single pattern — express alternatives by
listing multiple `LexRule`s in the same state. The DFA still picks the longest
match; on a tie the rule that appears first wins.

---

## The two demo executables

### `Bootstrap` — self-describing BNF meta-grammar

`Bootstrap/Program.cs` hand-codes a grammar in C# that *describes the BNF
notation itself*. The `BNF` string constant is a small BNF document — the same
notation the grammar accepts:

```bnf
<syntax>     ::= <rule> | <rule>, <syntax>.
<rule>       ::= "<", <rule-name>, ">", "::=",  <expression>, <rule-end>.
<expression> ::= <list> | <list>,  "|",  <expression>.
…
```

Bootstrap then asks: *"can the parser we built from our hand-coded BNF
meta-grammar parse this BNF source?"* If it accepts, the meta-grammar is at
least powerful enough to describe itself, and the whole pipeline (lexer →
LA-iterator → parser → reductions) is end-to-end working.

It is **not** a parser generator that reads grammar files at build time, and it
does not produce code. It exists to:

1. Be an end-to-end smoke test of the lexer + parser path on non-trivial input
   (multiple lexer states, push/pop state on quoted literals, ignored
   whitespace/EOL, ambiguity resolution via precedence groups).
2. Demonstrate how to wire a non-trivial grammar — list reductions, quoted
   literals, named tuples in semantic actions, etc.
3. Set up the eventual transition to *real* grammar bootstrapping: once
   YAML-defined grammars and a source generator land, Bootstrap will be the
   reference for parsing the YAML grammar file format.

In Debug it prints a step-by-step parse trace; in Release it prints just the
final accepted parse tree. AOT-published it parses the BNF document in well
under a millisecond on the development machine.

### `TestProject` — arithmetic expressions with constant folding

`TestProject/Main.cs` defines a small operator-precedence grammar
(`+ − × ÷` with parentheses), demonstrates a per-production rewriter that does
**constant folding during reduction** (e.g. `(24 / 12) + 2 * (3-4)` reduces to
`0` at parse time, not at evaluation time), and uses an inline tokenizer
(`TokenizeArithmetic`) instead of `PipeBytesLexer` to show that any
`IAsyncIterator<Item>` source plugs in.

This exists because:

- It exercises the precedence-group ordering and the `Derivation.LeftMost`
  ambiguity resolution.
- It shows the `OverflowException` -> error-token path (`Item.IsError` bubbles
  up through reductions automatically).
- It demonstrates that the lexer interface is genuinely pluggable — you don't
  have to use the byte DFA if you don't want to.

---

## Building, testing, publishing

```bash
# Build the whole solution
dotnet build CodeProject.Syntax.LALR.sln -c Debug          # or -c Release

# Run all tests (xUnit v3 on Microsoft.Testing.Platform — currently 173)
dotnet test CodeProject.Syntax.LALR.Tests/CodeProject.Syntax.LALR.Tests.csproj -c Debug

# Run the test exe directly (also via MTP — equivalent, faster startup)
dotnet run  --project CodeProject.Syntax.LALR.Tests/CodeProject.Syntax.LALR.Tests.csproj -c Debug

# Run only a subset of tests (MTP --filter-method, glob-syntax)
dotnet run  --project CodeProject.Syntax.LALR.Tests/CodeProject.Syntax.LALR.Tests.csproj -c Debug -- --filter-method "*PipeBytesLexer*"

# Run the two demo executables end-to-end
dotnet run  --project Bootstrap/Bootstrap.csproj -c Debug          # parses BNF with itself
dotnet run  --project TestProject/TestProject.csproj -c Debug      # arithmetic + constant folding

# Native AOT publish (verifies library + demo stay AOT-clean)
dotnet publish Bootstrap/Bootstrap.csproj -c Release
dotnet publish TestProject/TestProject.csproj -c Release
```

The demo binaries print step-by-step parse traces in Debug; in Release the
`[Conditional("DEBUG")]` traces drop out and only the final accept/reject line
is printed.

---

## Roadmap and known limitations

The project is usable as-is. The following items are deliberately *not* done
yet and tracked as the next architectural milestones:

- **Productions still embed semantic actions.** `Production` carries a
  `Func<int, Item[], object>` rewriter, so grammar definitions and code stay
  entangled. The intended direction is a **typed AST + visitor split**, with
  grammar files in YAML and a source generator producing the parser table at
  build time. Don't double down on inline rewriters in new grammars if you can
  help it.
- **Grammars-as-data is built; YAML support is build-time only.**
  `CodeProject.Syntax.LALR/Schema/` defines `GrammarSchema` POCOs and a
  `SchemaCompiler` that turns them into runtime `Grammar` + `LexRule[]` (with a
  tiny in-house regex-to-`IRx` parser handling the lexer-rule `match:` strings).
  `CodeProject.Syntax.LALR.SourceGenerators/` is a Roslyn `IIncrementalGenerator`
  that reads `*.lalr.yaml` AdditionalFiles at build time (via YamlDotNet, kept
  private so it never lands in the consumer's runtime binary) and emits a
  populated `GrammarSchema` in a `static partial class` named after the YAML
  file. What's still missing: typed-AST / visitor split (Phase 3) so semantic
  actions stop living inside `Production` rewriters, and the self-host where
  Bootstrap's grammar moves from C# to a `.lalr.yaml` file (Phase 4).
- **Async-per-token at the parser/iterator boundary.** The lexer's inner loop
  is sync and the only `await` per byte-buffer is `PipeReader.ReadAsync`,
  which is the right shape — but `IAsyncIterator<Item>` and the parser's
  `ParseInputAsync` still await per token. A pure-sync, `ref struct`-style
  parser with async only at the I/O edge is possible; not a priority.
- **No alternation inside a single `IRx` pattern.** Use multiple `LexRule`s in
  the same state to express alternatives. (Adding alternation to the AST is
  a small change; just hasn't been needed yet.)

---

## Provenance & license

Originally adapted from Phillip Voyle's
[*LALR Parse Table Generation in C#*](https://web.archive.org/web/20241115071153/http://www.codeproject.com/Articles/252399/LALR-Parse-Table-Generation-in-Csharp).
The core LALR algorithm follows the
[GOLD parser pseudocode](http://www.goldparser.org/doc/engine-pseudo/parse-token.htm).

Licensed under the
[Code Project Open License (CPOL)](http://www.codeproject.com/info/cpol10.aspx).
