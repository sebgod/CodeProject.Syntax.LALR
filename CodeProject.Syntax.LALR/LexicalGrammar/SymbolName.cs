using System;

namespace CodeProject.Syntax.LALR.LexicalGrammar
{
    public struct SymbolName : IEquatable<SymbolName>
    {
        private readonly int _id;

        private readonly string _name;

        public int ID { get { return _id; } }

        public string Name { get { return _name; } }

        public SymbolName(int id, string name)
        {
            _id = id;
            _name = name;
        }

        public override bool Equals(object obj)
        {
            return obj is SymbolName && Equals((SymbolName)obj);
        }

        public override int GetHashCode()
        {
            return _id;
        }

        public override string ToString()
        {
            return string.Format("{0}: {1}", ID, Name);
        }

        public bool Equals(SymbolName other)
        {
            return _id == other.ID;
        }
    }
}