using System;
using System.Collections;
using System.Linq;

namespace LALR.CC.LexicalGrammar;

public enum ContentType : byte
{
    Reduction,
    Nested,
    Scalar,
}

public class Item : IEquatable<Item>
{
    public static readonly Item EOF = new(-1, "$");

    private readonly object _content;
    private int _state;
    private bool? _inError;

    public int ID { get; }

    public object Content => _content;

    public Reduction Reduction => (Reduction)_content;

    public Item Nested => (Item)_content;

    /// <summary>
    /// Where this token started in the input, or the position of the leftmost child
    /// for a reduced non-terminal. <see cref="SourcePosition.Unknown"/> for
    /// <see cref="EOF"/>, default-constructed items, and items built without an explicit
    /// position.
    /// </summary>
    public SourcePosition Position { get; }

    public int State
    {
        get => _state;
        set
        {
            _state = value;
            _inError = null;
        }
    }

    public ContentType ContentType { get; }

    public Item(int id, object content)
        : this(id, content, SourcePosition.Unknown)
    {
        // delegate to position-aware constructor
    }

    public Item(int id, object content, SourcePosition position)
    {
        ID = id;
        _content = content;
        Position = position;
        ContentType = DetermineContentType(_content);

        State = ContentType == ContentType.Reduction && Reduction.Children.Any(p => p.IsError) ? -1 : id;
    }

    private static ContentType DetermineContentType(object content)
    {
        if (content is null) return ContentType.Scalar;
        if (content is Item) return ContentType.Nested;
        if (content is Reduction) return ContentType.Reduction;
        return ContentType.Scalar;
    }

    public bool IsError
    {
        get
        {
            if (_inError.HasValue)
            {
                return _inError.Value;
            }
            if (State < 0)
            {
                return true;
            }

            _inError = false;
            switch (ContentType)
            {
                case ContentType.Nested:
                    _inError = ((Item)_content).IsError;
                    break;

                case ContentType.Reduction:
                    _inError = Reduction.Children.Any(p => p.IsError);
                    break;
            }
            if (_inError == true)
            {
                State = -1;
            }
            return _inError.Value;
        }
    }

    public bool Equals(Item other) => other is not null && ID == other.ID;

    public override int GetHashCode() => ID;

    public override bool Equals(object obj) => obj is Item item && Equals(item);

    public static bool operator ==(Item left, Item right)
    {
        if (left is null)
        {
            return right is null;
        }
        return left.Equals(right);
    }

    public static bool operator !=(Item left, Item right) => !(left == right);

    private static string ContentToString(object content)
    {
        if (content is null)
        {
            return string.Empty;
        }

        if (content is string stringContent)
        {
            return stringContent;
        }

        return content is IEnumerable enumerable
            ? "[" + string.Join(" ", enumerable.Cast<object>().Select(ContentToString)) + "]"
            : content.ToString();
    }

    public override string ToString() => ContentToString(Content);
}
