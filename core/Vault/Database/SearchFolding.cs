using System.Globalization;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;
using Microsoft.EntityFrameworkCore.Query;

namespace Pleiades.Vault.Database;

/// <summary>
/// Folds text for searching — decomposed, stripped of combining marks, and lower-cased — so a search ignores case and
/// accents: "cafe" finds "Café" and "xuntas" finds "Xuntaš". Inside an EF query, <see cref="Fold"/> runs in SQLite as
/// the <see cref="FunctionName"/> function, which <see cref="SearchFoldingConnectionInterceptor"/> registers on every
/// connection; the client folds the same way before its own fuzzy matching.
/// </summary>
/// <remarks>
/// SQLite's own case-insensitivity — <c>LIKE</c>, <c>lower()</c>, the <c>NOCASE</c> collation — covers ASCII letters
/// only, and <c>instr</c>, which <see cref="string.Contains(string)"/> translates to, covers none, so a search that
/// leaned on them missed "polaris" in "Polaris".
/// </remarks>
public static class SearchFolding
{
	/// <summary>
	/// The name <see cref="Fold"/> is registered under in SQLite.
	/// </summary>
	public const string FunctionName = "p7t_fold";

	private static readonly MethodInfo FoldMethod = typeof(SearchFolding).GetMethod(nameof(Fold))!;
	private static readonly MethodInfo ContainsMethod = typeof(string).GetMethod(nameof(string.Contains), [typeof(string)])!;

	/// <summary>
	/// Folds a text for searching. Inside an EF query this translates to the <see cref="FunctionName"/> SQL function.
	/// </summary>
	/// <param name="text">The text to fold.</param>
	/// <returns>The folded text, or <see langword="null"/> for a <see langword="null"/> text.</returns>
	public static string? Fold(string? text)
	{
		if (text is null)
		{
			return null;
		}

		var decomposed = text.Normalize(NormalizationForm.FormKD);
		var folded = new StringBuilder(decomposed.Length);
		Span<char> buffer = stackalloc char[2];
		foreach (var rune in decomposed.EnumerateRunes())
		{
			if (Rune.GetUnicodeCategory(rune) is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark)
			{
				continue;
			}

			var written = Rune.ToLowerInvariant(rune).EncodeToUtf16(buffer);
			folded.Append(buffer[..written]);
		}

		return folded.ToString();
	}

	/// <summary>
	/// Splits a search into the folded words it must find.
	/// </summary>
	/// <param name="search">The search as typed.</param>
	/// <returns>The folded words, split on whitespace; none for an empty search.</returns>
	public static IReadOnlyList<string> Words(string? search)
		=> Fold(search)?.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries) ?? [];

	/// <summary>
	/// Narrows a query to the rows holding every word of a search in one of the given fields, ignoring case and
	/// accents. Each word may turn up in a different field and in any order; a search with no words narrows nothing.
	/// </summary>
	/// <typeparam name="T">The row type.</typeparam>
	/// <param name="source">The query to narrow.</param>
	/// <param name="search">The search as typed.</param>
	/// <param name="fields">The text fields a word may turn up in.</param>
	/// <returns>The narrowed query.</returns>
	public static IQueryable<T> WhereMatchesSearch<T>(this IQueryable<T> source, string? search, params Expression<Func<T, string?>>[] fields)
	{
		ArgumentNullException.ThrowIfNull(source);
		if (fields.Length == 0)
		{
			throw new ArgumentException("A search needs at least one field to look in.", nameof(fields));
		}

		foreach (var word in Words(search))
		{
			var row = Expression.Parameter(typeof(T), "row");
			// Read through a closure rather than inlined as a constant, so EF sends the word as a parameter.
			var wordValue = ((Expression<Func<string>>)(() => word)).Body;
			var inAnyField = fields
				.Select(field => (Expression)Expression.Call(
					Expression.Call(FoldMethod, ReplacingExpressionVisitor.Replace(field.Parameters[0], row, field.Body)),
					ContainsMethod,
					wordValue))
				.Aggregate(Expression.OrElse);
			source = source.Where(Expression.Lambda<Func<T, bool>>(inAnyField, row));
		}

		return source;
	}
}
