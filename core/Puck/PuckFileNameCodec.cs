namespace Pleiades.Puck;

/// <summary>
/// The reversible filename character filter (PEP097). Entity names may contain any character except a line break, but
/// those names are synced to filenames; a handful of characters either break the filesystem (Windows disallows
/// <c>/ \ : * ? " &lt; &gt; |</c> in a path segment) or, while filesystem-legal, disable Obsidian indexing
/// (<c>[ ] ^ #</c>). This codec swaps each such character for a visually-near Unicode look-alike when a name is
/// <see cref="Encode">written</see> into a filename, and swaps it back when a filename is <see cref="Decode">read</see>
/// or scanned. The two operations are exact inverses, so a title survives a filename round-trip unchanged.
/// </summary>
/// <remarks>
/// This lives beside <see cref="PuckNamedIdentity"/>, which owns the <c>{PUCK token} - {Title}</c> filename convention:
/// encoding is applied where that convention formats a filename and decoding where it reads one back, so every
/// title↔filename path converges here. The mapping is intentionally limited to the PEP097 set; a look-alike a user
/// types by hand decodes to its plain counterpart (the inherent, accepted ambiguity of any look-alike scheme), and
/// characters outside the set — including control characters — are left as-is rather than being made irreversible.
/// </remarks>
public static class PuckFileNameCodec
{
	/// <summary>
	/// The PEP097 character mapping: each <see cref="ValueTuple{T1,T2}.Item1"/> is a forbidden character in a raw entity
	/// name; each <see cref="ValueTuple{T1,T2}.Item2"/> is the filesystem- and Obsidian-safe look-alike written in its
	/// place. Ordered as the proposal lists them (filesystem restrictions first, then Obsidian).
	/// </summary>
	public static readonly IReadOnlyList<(char Forbidden, char Replacement)> Mappings =
	[
		('/', '∕'),  // DIVISION SLASH
		('\\', '∖'), // SET MINUS
		(':', 'ː'),  // MODIFIER LETTER TRIANGULAR COLON
		('*', '⁕'),  // FLOWER PUNCTUATION MARK
		('?', '？'),  // FULLWIDTH QUESTION MARK
		('"', '″'),  // DOUBLE PRIME
		('<', '˂'),  // MODIFIER LETTER LEFT ARROWHEAD
		('>', '˃'),  // MODIFIER LETTER RIGHT ARROWHEAD
		('|', '⏐'),  // VERTICAL LINE EXTENSION
		('[', '⦋'),  // LEFT SQUARE BRACKET WITH UNDERBAR
		(']', '⦌'),  // RIGHT SQUARE BRACKET WITH UNDERBAR
		('^', 'ˆ'),  // MODIFIER LETTER CIRCUMFLEX ACCENT
		('#', '♯'),  // MUSIC SHARP SIGN
	];

	private static readonly IReadOnlyDictionary<char, char> ForbiddenToSafe =
		Mappings.ToDictionary(static mapping => mapping.Forbidden, static mapping => mapping.Replacement);

	private static readonly IReadOnlyDictionary<char, char> SafeToForbidden =
		Mappings.ToDictionary(static mapping => mapping.Replacement, static mapping => mapping.Forbidden);

	/// <summary>
	/// Replaces every forbidden character in <paramref name="value"/> with its safe look-alike, for use as (part of) a
	/// filename. Text with no forbidden character is returned unchanged.
	/// </summary>
	public static string Encode(string value) => Map(value, ForbiddenToSafe);

	/// <summary>
	/// Restores every safe look-alike in <paramref name="value"/> to its original forbidden character, recovering the
	/// entity name a filename encodes. Text with no look-alike is returned unchanged.
	/// </summary>
	public static string Decode(string value) => Map(value, SafeToForbidden);

	private static string Map(string value, IReadOnlyDictionary<char, char> table)
	{
		ArgumentNullException.ThrowIfNull(value);

		// Fast path: the overwhelming majority of names carry no mapped character, so avoid allocating a builder.
		var firstIndex = -1;
		for (var index = 0; index < value.Length; index++)
		{
			if (table.ContainsKey(value[index]))
			{
				firstIndex = index;
				break;
			}
		}

		if (firstIndex < 0)
		{
			return value;
		}

		var builder = new System.Text.StringBuilder(value.Length);
		builder.Append(value, 0, firstIndex);
		for (var index = firstIndex; index < value.Length; index++)
		{
			var character = value[index];
			builder.Append(table.TryGetValue(character, out var replacement) ? replacement : character);
		}

		return builder.ToString();
	}
}
