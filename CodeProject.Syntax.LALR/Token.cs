using System;
using System.Collections;
using System.Linq;

namespace CodeProject.Syntax.LALR
{
    public class Token : IEquatable<Token>
    {
        public static readonly Token EOF = new Token(-1, "$");

        private readonly int _id;

        public int ID
        {
            get { return _id; }
        }

        public object Content { get; set; }

        public int State { get; set; }

        public Token(int id, object content)
        {
            _id = id;
            Content = content;
            State = -1;
        }

        public bool Equals(Token other)
        {
            return ID == other.ID;
        }

        public override int GetHashCode()
        {
            return ID;
        }

        public override bool Equals(object obj)
        {
            return obj is Token && Equals((Token) obj);
        }

        private static string ContentToString(object content)
        {
            if (content == null)
            {
                return string.Empty;
            }

            var stringContent = content as string;
            if (stringContent != null)
            {
                return stringContent;
            }

            var enumerable = content as IEnumerable;
            return enumerable != null
                       ? "[" + string.Join(" ", enumerable.Cast<object>().Select(ContentToString)) + "]"
                       : content.ToString();
        }

        public override string ToString()
        {
#if TRACE
            return string.Format("{0}{1} {2}", ID, State >= 0 ? "#" + State : "", ContentToString(Content));
#else
            return ContentToString(Content);
#endif
        }
    }
}
