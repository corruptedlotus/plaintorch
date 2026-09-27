using Pleiades.Puck;
using Xunit;

namespace Pleiades.Tests.Core;

/// <summary>
/// The reversible filename character filter (PEP097). Entity names allow any character but a line break; a subset break
/// the filesystem or Obsidian indexing when synced to a filename, so the codec swaps them for look-alikes on write and
/// back on read. These pin the mapping table and the exact-inverse guarantee the read/write chokepoints rely on.
/// </summary>
public sealed class FilenameCodecTests
{
	[Fact]
	public void Every_mapped_character_encodes_to_and_decodes_from_its_look_alike()
	{
		foreach (var (forbidden, replacement) in PuckFileNameCodec.Mappings)
		{
			Assert.Equal(replacement.ToString(), PuckFileNameCodec.Encode(forbidden.ToString()));
			Assert.Equal(forbidden.ToString(), PuckFileNameCodec.Decode(replacement.ToString()));
		}
	}

	[Fact]
	public void The_thirteen_pep097_characters_are_all_mapped()
	{
		var forbidden = PuckFileNameCodec.Mappings.Select(mapping => mapping.Forbidden).ToArray();
		Assert.Equal(13, forbidden.Length);
		Assert.Equal(forbidden.Length, forbidden.Distinct().Count());
		Assert.Equal(PuckFileNameCodec.Mappings.Count, PuckFileNameCodec.Mappings.Select(mapping => mapping.Replacement).Distinct().Count());

		char[] expected = ['/', '\\', ':', '*', '?', '"', '<', '>', '|', '[', ']', '^', '#'];
		foreach (var character in expected)
		{
			Assert.Contains(character, forbidden);
		}
	}

	[Fact]
	public void Encoding_then_decoding_restores_the_original_name()
	{
		const string name = "Plan: build /2, ship <v1>? \"final\" [draft] #1 | 50% * done \\ end";
		Assert.Equal(name, PuckFileNameCodec.Decode(PuckFileNameCodec.Encode(name)));
	}

	[Fact]
	public void An_encoded_name_carries_none_of_the_forbidden_characters()
	{
		var encoded = PuckFileNameCodec.Encode("a/b\\c:d*e?f\"g<h>i|j[k]l^m#n");
		foreach (var forbidden in PuckFileNameCodec.Mappings.Select(mapping => mapping.Forbidden))
		{
			Assert.DoesNotContain(forbidden, encoded);
		}
	}

	[Fact]
	public void Every_replacement_is_a_legal_filename_character()
	{
		var invalid = new HashSet<char>(Path.GetInvalidFileNameChars());
		foreach (var (_, replacement) in PuckFileNameCodec.Mappings)
		{
			Assert.DoesNotContain(replacement, invalid);
		}
	}

	[Fact]
	public void Text_without_a_mapped_character_passes_through_unchanged()
	{
		const string plain = "Ship it - Q1 2026 (final)";
		Assert.Same(plain, PuckFileNameCodec.Encode(plain));
		Assert.Same(plain, PuckFileNameCodec.Decode(plain));
	}

	[Fact]
	public void Encoding_is_idempotent_and_decoding_a_plain_name_is_a_no_op()
	{
		var once = PuckFileNameCodec.Encode("a:b");
		Assert.Equal(once, PuckFileNameCodec.Encode(once)); // no forbidden chars remain, so encoding again changes nothing
		Assert.Equal("a:b", PuckFileNameCodec.Decode("a:b")); // a plain name has no look-alikes to restore
	}

	[Fact]
	public void The_empty_string_round_trips()
	{
		Assert.Equal(string.Empty, PuckFileNameCodec.Encode(string.Empty));
		Assert.Equal(string.Empty, PuckFileNameCodec.Decode(string.Empty));
	}
}
