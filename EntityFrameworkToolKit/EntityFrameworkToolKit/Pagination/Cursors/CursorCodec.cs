using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace EntityFrameworkToolKit.Pagination.Cursors;

/// <summary>
/// Turns a position (the sort key values of one row) into an opaque cursor string and back.
/// A cursor is base64url JSON: <c>{"s":"&lt;sort fingerprint&gt;","v":[…key values…]}</c>.
/// </summary>
/// <remarks>
/// Cursors are not signed: a client can craft one. Decoding is therefore strict, and the values only ever become
/// typed SQL parameters, so a crafted cursor can at worst point at another position, like editing <c>?page=</c>.
/// </remarks>
internal static class CursorCodec
{
    /// <summary>Cursors longer than this are rejected without being decoded.</summary>
    internal const int MaxLength = 4096;

    private static readonly JsonSerializerOptions Options = new()
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    /// <summary>Encodes the key <paramref name="values"/> of a row for the sort identified by <paramref name="fingerprint"/>.</summary>
    public static string Encode(string fingerprint, IReadOnlyList<object?> values, IReadOnlyList<Type> types)
    {
        var elements = values.Select((v, i) => JsonSerializer.SerializeToElement(v, types[i], Options)).ToArray();
        var json = JsonSerializer.SerializeToUtf8Bytes(new Payload { Sort = fingerprint, Values = elements }, Options);
        return ToBase64Url(json);
    }

    /// <summary>
    /// Decodes <paramref name="cursor"/> into one value per key type, checking it was made for the same sort.
    /// </summary>
    /// <exception cref="InvalidQueryRequestException">The cursor is malformed, has the wrong values, or belongs to another sort.</exception>
    public static object[] Decode(string cursor, string fingerprint, IReadOnlyList<Type> types, string paramName)
    {
        var payload = Parse(cursor);
        if (payload?.Sort is null || payload.Values is null)
            throw Invalid(paramName);
        if (payload.Sort != fingerprint)
            throw new InvalidQueryRequestException(
                $"The {paramName} cursor was created for a different sort. Start again from the first page.", paramName);
        if (payload.Values.Length != types.Count)
            throw Invalid(paramName);

        var values = new object[types.Count];
        for (var i = 0; i < types.Count; i++)
        {
            try
            {
                values[i] = payload.Values[i].Deserialize(types[i], Options) ?? throw Invalid(paramName);
            }
            catch (Exception ex) when (ex is JsonException or FormatException or NotSupportedException or InvalidOperationException)
            {
                throw Invalid(paramName);
            }
        }

        return values;
    }

    internal static string ToBase64Url(ReadOnlySpan<byte> bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static Payload? Parse(string cursor)
    {
        if (cursor.Length > MaxLength)
            return null;

        var base64 = new StringBuilder(cursor.Length + 2).Append(cursor).Replace('-', '+').Replace('_', '/');
        base64.Append('=', (4 - base64.Length % 4) % 4);
        var bytes = new byte[base64.Length];
        if (!Convert.TryFromBase64String(base64.ToString(), bytes, out var written))
            return null;

        try
        {
            return JsonSerializer.Deserialize<Payload>(bytes.AsSpan(0, written), Options);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static InvalidQueryRequestException Invalid(string paramName) =>
        new($"The {paramName} cursor is not valid. Use a cursor returned by this endpoint.", paramName);

    private sealed class Payload
    {
        [JsonPropertyName("s")]
        public string? Sort { get; set; }

        [JsonPropertyName("v")]
        public JsonElement[]? Values { get; set; }
    }
}
