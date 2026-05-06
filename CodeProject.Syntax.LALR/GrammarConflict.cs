using System;
using System.Collections.Generic;
using System.Text;

namespace CodeProject.Syntax.LALR;

/// <summary>
/// Kind of unresolved parse-table conflict.
/// </summary>
public enum ConflictKind : byte
{
    /// <summary>
    /// Two or more reduce actions tie at the same (state, lookahead) cell and the
    /// surrounding <see cref="PrecedenceGroup"/>'s <see cref="Derivation"/> doesn't pick a winner.
    /// </summary>
    ReduceReduce,

    /// <summary>
    /// A shift action ties with one or more reduce actions at the same (state, lookahead)
    /// cell and the surrounding <see cref="PrecedenceGroup"/>'s <see cref="Derivation"/>
    /// doesn't pick a winner.
    /// </summary>
    ShiftReduce,
}

/// <summary>
/// One entry in the conflict report attached to <see cref="GrammarConflictException"/>.
/// All ids are interpretable through <see cref="Grammar.SymbolNames"/> and
/// <see cref="Parser.Productions"/> on the grammar that triggered the conflict.
/// </summary>
public readonly struct GrammarConflict(int state, int lookaheadSymbolId, ConflictKind kind, int shiftTargetState, int[] reduceProductionIds) : IEquatable<GrammarConflict>
{
    /// <summary>LALR(1) parser state at which the conflict occurs.</summary>
    public int State { get; } = state;

    /// <summary>Lookahead token symbol id, or -1 for end-of-input.</summary>
    public int LookaheadSymbolId { get; } = lookaheadSymbolId;

    public ConflictKind Kind { get; } = kind;

    /// <summary>Target state of the would-be shift, or -1 for a pure reduce-reduce conflict.</summary>
    public int ShiftTargetState { get; } = shiftTargetState;

    /// <summary>Production indices of the competing reduce actions (length ≥ 1).</summary>
    public int[] ReduceProductionIds { get; } = reduceProductionIds;

    public bool Equals(GrammarConflict other)
        => State == other.State
        && LookaheadSymbolId == other.LookaheadSymbolId
        && Kind == other.Kind
        && ShiftTargetState == other.ShiftTargetState
        && ReduceArraysEqual(ReduceProductionIds, other.ReduceProductionIds);

    private static bool ReduceArraysEqual(int[] a, int[] b)
    {
        if (ReferenceEquals(a, b))
        {
            return true;
        }
        if (a is null || b is null || a.Length != b.Length)
        {
            return false;
        }
        for (var i = 0; i < a.Length; i++)
        {
            if (a[i] != b[i])
            {
                return false;
            }
        }
        return true;
    }

    public override bool Equals(object obj) => obj is GrammarConflict other && Equals(other);

    public override int GetHashCode()
    {
        // Manual fold rather than System.HashCode.Combine: this file is
        // <Compile Link>-shared into the netstandard2.0 source generator, and
        // System.HashCode is .NET Core 2.1+ / netstandard 2.1+ — not available
        // in netstandard2.0.
        unchecked
        {
            var h = 17;
            h = h * 31 + State;
            h = h * 31 + LookaheadSymbolId;
            h = h * 31 + (byte)Kind;
            h = h * 31 + ShiftTargetState;
            h = h * 31 + (ReduceProductionIds?.Length ?? 0);
            return h;
        }
    }

    public static bool operator ==(GrammarConflict a, GrammarConflict b) => a.Equals(b);
    public static bool operator !=(GrammarConflict a, GrammarConflict b) => !a.Equals(b);
}

/// <summary>
/// Thrown by <see cref="Parser"/>'s constructor when the input grammar has unresolved
/// shift-reduce or reduce-reduce conflicts. <see cref="Conflicts"/> enumerates each
/// offending parse-table cell so callers (or generators) can produce diagnostics.
/// </summary>
public sealed class GrammarConflictException : InvalidOperationException
{
    public IReadOnlyList<GrammarConflict> Conflicts { get; }

    public GrammarConflictException(IReadOnlyList<GrammarConflict> conflicts, string message)
        : base(message)
    {
        Conflicts = conflicts;
    }

    /// <summary>
    /// Builds a human-readable diagnostic listing every conflict, with the lookahead
    /// symbol name and the right-hand-side of each competing production resolved
    /// through <paramref name="grammar"/> and <paramref name="productions"/>.
    /// </summary>
    internal static string FormatMessage(IReadOnlyList<GrammarConflict> conflicts, Grammar grammar, IReadOnlyList<Production> productions)
    {
        var sb = new StringBuilder();
        sb.Append("Grammar has ").Append(conflicts.Count)
          .Append(conflicts.Count == 1 ? " unresolved conflict:" : " unresolved conflicts:")
          .AppendLine();

        foreach (var c in conflicts)
        {
            sb.Append("  State ").Append(c.State).Append(", lookahead ");
            sb.Append(c.LookaheadSymbolId < 0
                ? "$"
                : "'" + grammar.SymbolNames[c.LookaheadSymbolId].Name + "'");
            sb.Append(": ");
            switch (c.Kind)
            {
                case ConflictKind.ShiftReduce:
                    sb.Append("SHIFT-REDUCE — shift to state ").Append(c.ShiftTargetState);
                    foreach (var pid in c.ReduceProductionIds)
                    {
                        sb.Append(" vs reduce ").Append(FormatProduction(pid, grammar, productions));
                    }
                    break;
                case ConflictKind.ReduceReduce:
                    sb.Append("REDUCE-REDUCE — ");
                    for (var i = 0; i < c.ReduceProductionIds.Length; i++)
                    {
                        if (i > 0)
                        {
                            sb.Append(" vs ");
                        }
                        sb.Append("reduce ").Append(FormatProduction(c.ReduceProductionIds[i], grammar, productions));
                    }
                    break;
            }
            sb.AppendLine();
        }

        sb.AppendLine().Append("Resolve by placing the conflicting productions in a PrecedenceGroup with Derivation.LeftMost (prefer reduce) or Derivation.RightMost (prefer shift), or by restructuring the grammar.");
        return sb.ToString();
    }

    private static string FormatProduction(int productionIndex, Grammar grammar, IReadOnlyList<Production> productions)
    {
        var p = productions[productionIndex];
        var sb = new StringBuilder();
        sb.Append('#').Append(productionIndex).Append(" (");
        sb.Append(grammar.SymbolNames[p.Left].Name).Append(" ->");
        if (p.Right is { Length: > 0 })
        {
            foreach (var sym in p.Right)
            {
                sb.Append(' ').Append(grammar.SymbolNames[sym].Name);
            }
        }
        else
        {
            sb.Append(" ε");
        }
        sb.Append(')');
        return sb.ToString();
    }
}
