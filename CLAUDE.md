# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project

LALR(1) parser-table generator and runtime in C#, originally adapted from Phillip Voyle's CodeProject article ([archive](https://web.archive.org/web/20241115071153/http://www.codeproject.com/Articles/252399/LALR-Parse-Table-Generation-in-Csharp)). The parsing loop follows the GOLD parser pseudocode. License: CPOL.

The repo was modernized to **.NET 10 / C# 14 / AOT** in 2026; older articles describing the codebase predate that and may be misleading on details.

## Common commands

```bash
# Build everything (4 projects in one solution)
dotnet build CodeProject.Syntax.LALR.sln -c Debug          # or -c Release

# Run all unit tests (xUnit v3 on Microsoft.Testing.Platform — 149+ cases)
dotnet test CodeProject.Syntax.LALR.Tests/CodeProject.Syntax.LALR.Tests.csproj -c Debug

# Run the test project directly (also via MTP — equivalent, faster startup)
dotnet run --project CodeProject.Syntax.LALR.Tests/CodeProject.Syntax.LALR.Tests.csproj -c Debug

# Run a subset of tests (xUnit v3 on MTP exposes the standard MTP --filter-* options)
dotnet run --project CodeProject.Syntax.LALR.Tests/CodeProject.Syntax.LALR.Tests.csproj -c Debug -- --filter-method "*PipeRuneIterator*"

# Run the two demo executables (they invoke the parser end-to-end)
dotnet run --project Bootstrap/Bootstrap.csproj --no-build -c Debug      # parses BNF with itself
dotnet run --project TestProject/TestProject.csproj --no-build -c Debug  # arithmetic expression demo

# AOT publish (verifies the library + demo stay AOT-clean)
dotnet publish Bootstrap/Bootstrap.csproj -c Release
dotnet publish TestProject/TestProject.csproj -c Release
```

The demo binaries print step-by-step parse traces in Debug; in Release the `[Conditional("DEBUG")]` traces drop out and only the final accept/reject line is printed.

## Solution layout

| Project | Type | Purpose |
|---|---|---|
| `CodeProject.Syntax.LALR/` | Library | Grammar model, parser-table generator, runtime parser, lexer infrastructure. `IsAotCompatible=true`, `IsTrimmable=true`. |
| `Bootstrap/` | Exe (`PublishAot=true`) | Defines a BNF grammar in code and parses a BNF source string with the generated parser. End-to-end smoke test for the byte-DFA lexer path. |
| `TestProject/` | Exe (`PublishAot=true`) | Arithmetic-expression grammar with operator precedence; demonstrates rewrite functions on productions. Uses an inline tokenizer (no PowerShell). |
| `CodeProject.Syntax.LALR.Tests/` | xUnit v3 test project (Microsoft.Testing.Platform runner; `OutputType=Exe`) | Unit tests for the lexical-grammar regex builders, the iterator infrastructure, and the DFA compiler. |

Shared MSBuild settings (`TargetFramework`, `LangVersion`, etc.) live in `Directory.Build.props`. Don't put them in individual csprojs.

## Architecture: how a parse happens

The pipeline has three stages plus the parser. Each implements/consumes `IAsyncIterator<T>` / `IAsyncLAIterator<T>` (the latter adds one-token lookahead):

1. **Bytes-to-tokens lexer** — `LexicalGrammar/PipeBytesLexer.cs`. Owns a `PipeReader`, drives one **UTF-8 byte DFA per lexer state** over the raw byte stream, emits `Item` tokens. The DFA per state is built at construction by `DfaCompiler.CompileMany` (typed `IRx` AST → codepoint NFA → codepoint DFA via Thompson + subset construction; see `LexicalGrammar/Dfa/DfaCompiler.cs`) and then `Utf8DfaLowering.Lower` (codepoint DFA → byte DFA via the standard UTF-8 chain split; see `LexicalGrammar/Dfa/Utf8DfaLowering.cs`). Each `LexRule(int symbolId, IRx pattern, string instruction)` carries its own action: `#pop`, `#ignore`, or push a named state. Longest match with first-rule-wins on ties. Bytes are UTF-8 decoded to a string only at token boundaries.
2. **Token lookahead** — `LexicalGrammar/AsyncLATokenIterator.cs`. Wraps any `IAsyncIterator<Item>` to add one-token lookahead.
3. **Parser** — `Parser.cs`. The parser table is computed once in the constructor: `PopulateProductions → InitSymbols → GenerateLR0Items → ComputeFirstSets → ConvertLR0ItemsToKernels → InitLALRTables → CalculateLookAheads → GenerateParseTable`. `ParseInputAsync` then drives the standard shift/reduce/accept/error loop, consulting `ParseTable.Actions[state, tokenId+1]`.

`LexicalGrammar/PipeRuneIterator.cs` is still in the codebase as a standalone codepoint iterator (`IAsyncLAIterator<int>`) for callers that want UTF-8-decoded codepoints rather than tokens, but no part of the parse pipeline uses it anymore — it's an alternative input path, not a building block of `PipeBytesLexer`.

Grammars are described by:
- `SymbolName[]` — terminal and non-terminal names; index = symbol ID (the LHS=0 symbol is the start symbol).
- `Production` — `Left` (int symbol ID), `Right` (int[] of symbol IDs), optional `Func<int, Item[], object>` rewriter.
- `PrecedenceGroup` — list of productions sharing precedence and a `Derivation` (`None` / `LeftMost` / `RightMost`) that resolves S/R and R/R conflicts. Groups are ordered by descending precedence.
- `Grammar` aggregates the symbol table and the precedence groups.

`Item` (`LexicalGrammar/Item.cs`) is the unifying value type for both terminal tokens and reduced non-terminals. Its `ContentType` is `Scalar` (raw lexer payload), `Reduction` (a `Reduction` with child `Item`s), or `Nested` (single child `Item`). Tokens that bubble up through `IsError` propagate error state through reductions automatically.

`Debug.cs` is a tracer wrapped around a `Parser`; almost every method is `[Conditional("DEBUG")]`. Pass `Console.Write` / `Console.Error.Write` actions to its constructor to emit traces; pass `null` to silence it.

## Conventions and constraints to respect

- **Library is AOT-compatible (`IsAotCompatible=true`)**. No reflection beyond what BCL collections do; no `dynamic`; no runtime-codegen serializers. If you add a feature, keep it allocation-light and reflection-free, and run `dotnet publish` on `Bootstrap/` to verify no AOT warnings.
- **`Conditional("DEBUG")` calls compile to no-ops in Release**. Don't put side-effecting work inside `Dump*` / `Flush` methods.
- **Immutable structs everywhere** in the grammar/parser-table model (`LR0Item`, `LR1Item`, `LALRPropogation`, `Action`, `ParseTable`, `Production`, `PrecedenceGroup`, `Grammar`, `SymbolName`, `CharRx`, etc.). Match that style for any new value types — `readonly struct` with primary constructors and `IEquatable<T>` where equality is meaningful.
- **`Parser`'s constructor fails fast on unresolved grammar conflicts.** Any S/R or R/R conflict left after the precedence-group derivation pass throws `GrammarConflictException` (with `Conflicts` enumerating each offending state + lookahead + competing productions). Don't catch and ignore — fix the grammar by adding the colliding productions to a `PrecedenceGroup` with `Derivation.LeftMost` / `Derivation.RightMost`, or by restructuring.
- **`Item` carries a `SourcePosition` (1-based `Line`, byte-based 1-based `Column`, absolute `ByteOffset`).** `PipeBytesLexer` populates it; the parser reduces propagate the leftmost child's position (epsilon reductions take the lookahead's position). `default(SourcePosition)` is `Unknown` (`Line==0`); `Item.EOF` and items built without a position both return Unknown. **Column is bytes, not codepoints** — for codepoint columns, decode the matched bytes from the source text.
- **`PipeBytesLexer` fails fast on unrecognized bytes by default.** Constructor / factories take a `LexerErrorMode` (`Throw` default, `EmitAndStop`, `EmitAndSkip`) and an `errorSymbolId`. `Throw` raises `LexerException` with the offending byte, `SourcePosition`, and lexer state name. The two emit modes require a non-negative `errorSymbolId` and emit an `Item` with `IsError==true` and `Content` set to the hex byte (e.g. `"\x7E"`). Existing callers that passed `cancellationToken` positionally must switch to named-arg form (`cancellationToken: ct`) since the parameter sits after the new mode/id pair.
- **`Parser.ParseInputAsync` mirrors that pattern.** New `errorMode` parameter (`ParserErrorMode.Throw` default, `Return` keeps the legacy "return offending Item" behavior). On `ActionType.Error`, Throw mode raises `ParseErrorException` carrying the offending `Item`, the LALR(1) state, and the set of symbol ids that *would* have been valid at this state — derived by scanning the parse-table row for non-error cells. Also takes `CancellationToken cancellationToken = default` and checks it once per loop iteration. Same named-arg note: `cancellationToken: ct` is the supported call pattern.
- **Bug fix while in there:** the parse-error path used `state < 0 ? state : -state` to mark `Item.State` negative — but `-0 == 0`, so an error at the initial state left `IsError==false`. Now uses `-(state+1)` so every error item is genuinely marked as error. Don't depend on the old behavior anywhere.
- **Schema layer in `CodeProject.Syntax.LALR/Schema/`** is Phase 1 of the YAML-grammars-and-source-generator arc. `GrammarSchema` + `ProductionGroupSchema` + `ProductionSchema` + `LexRuleSchema` are plain POCOs (settable, deserializer-friendly) describing a grammar declaratively. `IRxParser.Parse(string)` turns a small regex dialect (literals, escapes `\n \r \t \\ \. \[ \] etc`, classes `[a-z]`, `[^abc]`, quantifiers `? + * {n} {n,m} {n,}`, groups `(…)`, **no alternation** — express alternatives as multiple lexer rules) into the typed `IRx` AST. `SchemaCompiler.Compile(schema, actions?)` returns `(Grammar, Dictionary<string, LexRule[]>)`, validates symbol references, lexer-instruction mutual-exclusion, missing root state, etc., surfacing every failure as `SchemaCompilationException` with a path-style locator (`productions[1].rules[3].rhs[2]`).
- **Source generator in `CodeProject.Syntax.LALR.SourceGenerators/`** is Phase 2: a Roslyn `IIncrementalGenerator` (netstandard2.0, `IsRoslynComponent=true`) that reads `*.lalr.yaml` AdditionalFiles, deserializes them via YamlDotNet (build-time only — `PrivateAssets="all"`, never ships to the user binary), and emits a `static partial class XYZ { static GrammarSchema Schema { get; } = new() { … }; }` in the consumer's `RootNamespace`. Class name is derived from the YAML filename (`arithmetic.lalr.yaml` → `Arithmetic`). YAML parse errors surface as Roslyn diagnostic `LALR0001` with file/line locators. The generator references the runtime library's types in *emitted* C# source only — it never compiles against them. Tests in `CodeProject.Syntax.LALR.Tests/SourceGenerators/` drive the generator via `CSharpGeneratorDriver` and include an end-to-end "emit → compile → load → parse" integration test. Generator consumers using `<ProjectReference OutputItemType="Analyzer">` won't get YamlDotNet automatically (it's PrivateAssets); add `<PackageReference Include="YamlDotNet" />` in the consumer's csproj for now (proper analyzer-DLL packaging comes when we publish a NuGet).
- **`PrintableList<T>`** in tests is a `List<T>` with `ToString` for friendlier failure messages — use it (not raw `List<T>`) when authoring `MemberData` rows that contain collections.

## Test framework gotchas (xUnit v3)

Tests use **xUnit v3 (3.2.x)** running on **Microsoft.Testing.Platform**, with `<UseMicrosoftTestingPlatformRunner>true</UseMicrosoftTestingPlatformRunner>` and `<TestingPlatformDotnetTestSupport>true</TestingPlatformDotnetTestSupport>` enabled. The project sets `<OutputType>Exe</OutputType>` because xunit.v3 emits its own `Main`. There is no `Microsoft.NET.Test.Sdk`, no `xunit.runner.visualstudio`, and no NUnit-era VSTest adapter — `dotnet test` and `dotnet run` both route through MTP.

Patterns to follow when adding or editing tests:

- `[Fact]` for parameterless tests; `[Theory]` + `[InlineData(...)]` for inline-parameterized cases. There is **no** `ExpectedResult`; pass the expected value as the last `InlineData` argument and assert it inside the test body with `Assert.Equal(expected, actual)`.
- For `[InlineData]` arguments typed as `int[]` (or any non-`object[]` array), spell the literal as `new int[] { ... }` — xUnit's analyzer (xUnit1010) rejects `new[] { 'a', 'b' }` because the inferred element type is `char`, not `int`.
- `[MemberData(nameof(Source))]` for runtime-generated cases; the source is a static method or property returning `IEnumerable<object[]>` (untyped) or `TheoryData<...>` (typed). Each row's element order matches the test method's parameter order. Throwing cases go in a separate source and assert with `Assert.Throws<T>` — there is no `TestCaseData.Throws(...)` equivalent.
- For exception assertions, use `Assert.Throws<T>(() => ...)` (sync) or `await Assert.ThrowsAsync<T>(async () => ...)` (async). Don't try to assert exceptions on a `Task<T>` returned synchronously without awaiting it inside `ThrowsAsync`.
- Pass `TestContext.Current.CancellationToken` to async APIs that accept a `CancellationToken` (xUnit1051). `TestContext.Current` is a static accessor unique to xUnit v3.
- xUnit `Assert.Equal(expected, actual)` argument order is **opposite** to NUnit's `Assert.That(actual, Is.EqualTo(expected))`. Watch the diagnostics — getting it backwards still passes, just shows confusing diff output on failures.

## Examples are stress tests, not safe demos

The grammars under `examples/` exist to **exercise the parser/generator pipeline against shapes that haven't come up before** — not just to look pretty. When an example uncovers a runtime bug (parse table misroute, AOT warning, generator diagnostic gap, lexer edge case), the fix goes into the runtime/generator/library, not into the example's grammar. Working around a bug at the grammar level (e.g. inserting a terminal between two non-terminals to dodge a state-tracking quirk in `Parser.ParseInputAsync`) hides the problem from the *next* user who writes a grammar with that shape, defeating the point of having the example in the first place.

Concretely:
- If a natural-looking grammar fails to parse, **trace the parser** (`new Debug(parser, Console.Out.Write, Console.Error.Write)`) and find the misbehaving table cell, code path, or table-construction step. Fix it there.
- If `dotnet publish -c Release` emits an AOT trim warning on an example, fix the warning in the runtime (or annotate the API), not by turning AOT off in the example.
- If the source generator produces awkward emitted code that an example has to paper over, fix the emitter in `CodeProject.Syntax.LALR.SourceGenerators/`.
- Add a regression test alongside the fix so the case is locked in independently of the example.

The Wikipedia-style LaTeX example (`examples/Latex/`) is the current poster child: its `A -> cmdfrac A A` rule (two non-terminals adjacent in an RHS, separated only by whatever sits inside their internal `{ E }` reductions) was the first grammar to expose a long-standing latent bug where `Parser.cs` stashed `production.Left` (LHS symbol id) on a reduction's `Item.State` instead of the goto-target parser state — the next reduction's `lastState = Peek().State` then mis-routed the goto. The fix landed in `Parser.cs`/`Debug.cs` rather than rewriting the LaTeX grammar.

## Known tech debt

- **Productions still embed semantic actions.** `Production` carries a `Func<int, Item[], object>` rewriter, so grammar definitions and code are entangled. Future direction is a typed AST + visitor split with grammar definitions in YAML; don't double down on the rewriter pattern in new grammars.
- **Async-per-token overhead.** `PipeBytesLexer` already keeps the inner DFA loop sync (it iterates `ReadOnlySequence<byte>` segments without awaiting per byte), so async only happens at `PipeReader.ReadAsync` boundaries — that's the right shape. The remaining async-per-token cost lives in `IAsyncIterator<Item>` callers and the parser loop's `ParseInputAsync`. A pure-sync lexer/parser with async only at the I/O edge is still possible but no longer urgent.

## Environment

- Targets `net10.0` only. SDK version pinned by what's installed (currently 10.0.200).
- The repo lives on Windows ARM64 in development (this affects only the AOT publish RID auto-detection).
- Comments in source are kept even when terse; **don't remove comments unless they're outdated or wrong** (per global instructions).
