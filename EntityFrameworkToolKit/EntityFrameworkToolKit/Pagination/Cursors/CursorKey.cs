using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore.Query;

namespace EntityFrameworkToolKit.Pagination.Cursors;

/// <summary>One sort key of a cursor-paged query: the key selector and its direction.</summary>
internal readonly record struct CursorKey(LambdaExpression Selector, bool Descending)
{
    public Type Type => Selector.ReturnType;

    /// <summary>The selector's body, rewritten to read from <paramref name="item"/> so several keys can share one lambda.</summary>
    public Expression BodyFor(ParameterExpression item) =>
        ReplacingExpressionVisitor.Replace(Selector.Parameters[0], item, Selector.Body);
}
