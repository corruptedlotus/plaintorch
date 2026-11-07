using System.Collections.Concurrent;
using System.Text.RegularExpressions;

namespace Pleiades.Vault.Migration.Migrations;

/// <summary>
/// Renames the value of a note's frontmatter <c>status</c> line, as the vault migrations that rename a status do
/// (<see cref="ObjectiveOnboardStatusMigration"/>, <see cref="FateStoppedStatusMigration"/>).
/// </summary>
internal static class FrontmatterStatusRename
{
	private static readonly ConcurrentDictionary<string, Regex> StatusLines = new(StringComparer.OrdinalIgnoreCase);

	/// <summary>
	/// Renames the value of a note's frontmatter <c>status</c> line from <paramref name="from"/> to <paramref name="to"/>,
	/// keeping the key, any quotes and anything after the value as they are.
	/// </summary>
	/// <param name="markdown">The note.</param>
	/// <param name="from">The old value, matched in any case.</param>
	/// <param name="to">The new value.</param>
	/// <param name="renamed">The note with the value renamed, or the note itself when nothing was renamed.</param>
	/// <returns>Whether the note's frontmatter said <c>status: </c><paramref name="from"/>.</returns>
	public static bool TryRename(string markdown, string from, string to, out string renamed)
	{
		ArgumentNullException.ThrowIfNull(markdown);
		renamed = markdown;
		var lines = markdown.Split('\n');
		if (lines[0].TrimStart('﻿').TrimEnd('\r').Trim() != "---")
		{
			return false;
		}

		var statusLine = StatusLines.GetOrAdd(from, value => new Regex(
			$"""^(?<prefix>status\s*:\s*)(?<quote>["']?){Regex.Escape(value)}\k<quote>(?<rest>\s*(#.*)?)$""",
			RegexOptions.IgnoreCase | RegexOptions.CultureInvariant));
		for (var index = 1; index < lines.Length; index++)
		{
			var line = lines[index].TrimEnd('\r');
			if (line == "---")
			{
				return false;
			}

			var match = statusLine.Match(line);
			if (!match.Success)
			{
				continue;
			}

			lines[index] = string.Concat(
				match.Groups["prefix"].Value,
				match.Groups["quote"].Value,
				to,
				match.Groups["quote"].Value,
				match.Groups["rest"].Value,
				lines[index].EndsWith('\r') ? "\r" : string.Empty);
			renamed = string.Join('\n', lines);
			return true;
		}

		return false;
	}
}
