namespace EntityFrameworkToolKit;

/// <summary>
/// Thrown when client-supplied query input (paging or sorting) is invalid (e.g. <c>?page=0</c> or <c>?sort=unknown</c>).
/// Map it to a 400 response; <see cref="ArgumentException.ParamName"/> holds the query parameter name
/// (<c>page</c>, <c>pageSize</c> or <c>sort</c>).
/// </summary>
/// <remarks>
/// Only client input raises this type. Programming errors (null arguments, invalid configuration) keep throwing the
/// base argument exceptions, so they surface as server errors instead of being reported to the caller as a bad request.
/// </remarks>
public sealed class InvalidQueryRequestException : ArgumentException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="InvalidQueryRequestException"/> class.
    /// </summary>
    /// <param name="message">A message that is safe to return to the client.</param>
    /// <param name="paramName">The query parameter that was invalid.</param>
    public InvalidQueryRequestException(string message, string paramName) : base(message, paramName)
    {
        _message = message;
    }

    private readonly string _message;

    /// <summary>
    /// The client-safe message, without the <c>(Parameter '…')</c> suffix <see cref="ArgumentException"/> appends;
    /// the parameter is available separately in <see cref="ArgumentException.ParamName"/>.
    /// </summary>
    public override string Message => _message;
}
