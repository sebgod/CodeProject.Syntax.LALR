using System.Text;
using LALR.CC;
using CL = global::Console.Lib;

namespace LALR.CC.Tui.Model;

/// <summary>
/// One row of the parse-table view: the action/goto cells for a single LALR
/// state. Cells are pre-rendered to short strings (s12, r3, acc, …) and laid
/// out with a fixed-width state column followed by one fixed-width column per
/// grammar symbol (EOF lives in column 0). Rendering pads with spaces and
/// truncates trailing columns when the viewport is narrower than the table.
/// </summary>
internal sealed class ParseTableRow : CL.IRowFormatter
{
    public const int StateColWidth = 5;
    public const int CellWidth = 5;     // wide enough for "acc", "s99", "r99", or a 2-digit goto

    private readonly int _stateId;
    private readonly string[] _cells;     // cells[0] = EOF, cells[i+1] = symbol i
    private readonly bool[] _isShift;     // for tinting Shift cells differently from Reduce
    private readonly bool[] _isReduce;
    private readonly bool[] _isGoto;

    public ParseTableRow(int stateId, string[] cells, bool[] isShift, bool[] isReduce, bool[] isGoto)
    {
        _stateId = stateId;
        _cells = cells;
        _isShift = isShift;
        _isReduce = isReduce;
        _isGoto = isGoto;
    }

    public int StateId => _stateId;

    /// <summary>Builds an action label for one parse-table cell. Empty for Error cells.</summary>
    public static string FormatCell(Action a, bool isNonTerminal)
    {
        return a.ActionType switch
        {
            // Reducing by production 0 (the start production) is what triggers the
            // runtime accept; render it as "acc" so the table reads like a textbook
            // LALR table rather than confronting the user with a bare "r0".
            ActionType.Reduce when a.ActionParameter == 0 => "acc",
            ActionType.Reduce => "r" + a.ActionParameter,
            ActionType.Shift when isNonTerminal => a.ActionParameter.ToString(),  // goto on nonterminal column
            ActionType.Shift => "s" + a.ActionParameter,
            ActionType.ErrorRR => "RR",
            ActionType.ErrorSR => "SR",
            _ => "",
        };
    }

    public string FormatRow(int width, CL.ColorMode colorMode) => FormatRow(width, colorMode, isSelected: false);

    public string FormatRow(int width, CL.ColorMode colorMode, bool isSelected)
    {
        var bg = isSelected ? CL.SgrColor.Blue : CL.SgrColor.Black;
        var fgState = isSelected ? CL.SgrColor.BrightWhite : CL.SgrColor.BrightCyan;
        var fgPlain = isSelected ? CL.SgrColor.BrightWhite : CL.SgrColor.White;
        var fgShift = isSelected ? CL.SgrColor.BrightWhite : CL.SgrColor.BrightGreen;
        var fgReduce = isSelected ? CL.SgrColor.BrightWhite : CL.SgrColor.BrightYellow;
        var fgGoto = isSelected ? CL.SgrColor.BrightWhite : CL.SgrColor.BrightCyan;
        var fgErr = isSelected ? CL.SgrColor.BrightWhite : CL.SgrColor.BrightRed;

        var sb = new StringBuilder(width + 64);
        sb.Append(new CL.VtStyle(fgState, bg).Apply(colorMode));
        sb.Append(' ').Append(_stateId.ToString().PadLeft(StateColWidth - 2)).Append(' ');
        sb.Append(CL.VtStyle.Reset);

        var used = StateColWidth;
        for (var i = 0; i < _cells.Length && used + CellWidth <= width; i++)
        {
            var cell = _cells[i];
            CL.SgrColor fg;
            if (cell.Length == 0) fg = fgPlain;
            else if (cell == "acc") fg = fgReduce;
            else if (cell == "RR" || cell == "SR") fg = fgErr;
            else if (_isShift[i]) fg = fgShift;
            else if (_isReduce[i]) fg = fgReduce;
            else if (_isGoto[i]) fg = fgGoto;
            else fg = fgPlain;
            sb.Append(new CL.VtStyle(fg, bg).Apply(colorMode));
            sb.Append(cell.PadLeft(CellWidth - 1)).Append(' ');
            sb.Append(CL.VtStyle.Reset);
            used += CellWidth;
        }
        if (used < width)
        {
            sb.Append(new CL.VtStyle(fgPlain, bg).Apply(colorMode));
            sb.Append(' ', width - used);
            sb.Append(CL.VtStyle.Reset);
        }
        return sb.ToString();
    }
}
