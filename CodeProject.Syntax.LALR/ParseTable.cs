using System;
namespace CodeProject.Syntax.LALR
{
    /// <summary>
    /// the type of action the parser will perform
    /// </summary>
    public enum ActionType : byte
    {
        Error,
        ErrorRR,
        ErrorSR,
        Reduce,
        Shift
    }

    /// <summary>
    /// A parse table entry
    /// </summary>
    public struct Action : IEquatable<Action>
    {
        private readonly int _actionParameter;
        private readonly ActionType _actionType;

        public ActionType ActionType
        {
            get { return _actionType; }
        }

        public int ActionParameter
        {
            get { return _actionParameter; }
        }

        public Action(ActionType actionType, int actionParameter)
        {
            _actionType = actionType;
            _actionParameter = actionParameter;
        }

        public override bool Equals(object obj)
        {
            return obj is Action && Equals((Action) obj);
        }

        public override int GetHashCode()
        {
            return ((int) ActionType << 24) ^ ActionParameter;
        }

        public bool Equals(Action action)
        {
            return (ActionType == action.ActionType) && (ActionParameter == action.ActionParameter);
        }

        public override string ToString()
        {
            return string.Format("{0} {1}", ActionType, ActionParameter);
        }
    };

    /// <summary>
    /// Directs the parser on which action to perform at a given state on a particular input
    /// </summary>
    public class ParseTable
    {
        public Action[,] Actions { get; set; }

        public int States
        {
            get { return Actions.GetLength(0); }
        }

        public int Tokens
        {
            get { return Actions.GetLength(1); }
        }
    }
}