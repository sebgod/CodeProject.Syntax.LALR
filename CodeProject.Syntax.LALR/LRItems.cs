using System;
namespace CodeProject.Syntax.LALR
{
    /// <summary>
    /// An item required for an LR0 Parser Construction
    /// </summary>
    public struct LR0Item : IEquatable<LR0Item>
    {
        private readonly int _production;
        private readonly int _position;

        public int Production
        {
            get { return _production; }
        }

        public int Position
        {
            get { return _position; }
        }

        public LR0Item(int production, int position)
        {
            _production = production;
            _position = position;
        }

        public override bool Equals(object obj)
        {
            return obj is LR0Item && Equals((LR0Item) obj);
        }

        public override int GetHashCode()
        {
            return (_production << 16) ^ _position;
        }

        public bool Equals(LR0Item item)
        {
            return (Production == item.Production) && (Position == item.Position);
        }

        public override string ToString()
        {
            return string.Format("production={0} position={1}", Production, Position);
        }
    };

    /// <summary>
    /// An item required for an LR1 Parser Construction
    /// </summary>
    public struct LR1Item : IEquatable<LR1Item>
    {
        private readonly int _lr0ItemID;
        private readonly int _lookAhead;

        public int LR0ItemID
        {
            get { return _lr0ItemID; }
        }

        public int LookAhead
        {
            get { return _lookAhead; }
        }

        public LR1Item(int lr0ItemID, int lookAhead)
        {
            _lr0ItemID = lr0ItemID;
            _lookAhead = lookAhead;
        }

        public override bool Equals(object obj)
        {
            return obj is LR1Item && Equals((LR1Item) obj);
        }

        public override int GetHashCode()
        {
            return (_lr0ItemID << 16) ^ _lookAhead;
        }

        public bool Equals(LR1Item item)
        {
            return (LR0ItemID == item.LR0ItemID) && (LookAhead == item.LookAhead);
        }

        public override string ToString()
        {
            return string.Format("LR0#={0} lookAhead={1}", LR0ItemID, LookAhead);
        }
    };

    public struct LALRPropogation : IEquatable<LALRPropogation>
    {
        private readonly int _lr0TargetItem;
        private readonly int _lr0TargetState;

        public int LR0TargetState
        {
            get { return _lr0TargetState; }
        }

        public int LR0TargetItem
        {
            get { return _lr0TargetItem; }
        }

        public LALRPropogation(int lr0TargetItem, int lr0TargetState)
        {
            _lr0TargetItem = lr0TargetItem;
            _lr0TargetState = lr0TargetState;
        }

        public override bool Equals(object obj)
        {
            return obj is LALRPropogation && Equals((LALRPropogation) obj);
        }

        public override int GetHashCode()
        {
            return (LR0TargetState << 16) ^ LR0TargetItem;
        }

        public bool Equals(LALRPropogation other)
        {
            return LR0TargetItem == other.LR0TargetItem && LR0TargetState == other.LR0TargetState;
        }

        public override string ToString()
        {
            return string.Format("LR0 target={0} state={1}", LR0TargetItem, LR0TargetState);
        }
    };

}