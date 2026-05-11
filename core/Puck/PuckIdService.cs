using System.Globalization;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Pleiades.Vault.Database;

namespace Pleiades.Puck;

/// <summary>
/// Issues concrete PUCK identifiers and records their registry and sequence state.
/// </summary>
public sealed class PuckIdService(PlainfraContext context, PuckNotationParser notationParser)
{
	private readonly Random _random = Random.Shared;

	/// <summary>
	/// Generates a new identifier for a CLR type decorated with <see cref="PuckFormatAttribute"/>.
	/// </summary>
	/// <typeparam name="T">The PUCK-managed type.</typeparam>
	/// <param name="segments">Optional caller-provided segment values.</param>
	/// <returns>The generated identifier.</returns>
	public string GenerateIdFor<T>(IReadOnlyList<PuckSegmentInput>? segments = null)
	{
		return GenerateIdFor(typeof(T), segments);
	}

	/// <summary>
	/// Generates a new identifier for a CLR type decorated with <see cref="PuckFormatAttribute"/>.
	/// </summary>
	/// <param name="entityType">The PUCK-managed type.</param>
	/// <param name="segments">Optional caller-provided segment values.</param>
	/// <returns>The generated identifier.</returns>
	public string GenerateIdFor(Type entityType, IReadOnlyList<PuckSegmentInput>? segments = null)
	{
		ArgumentNullException.ThrowIfNull(entityType);
		var format = entityType.GetCustomAttribute<PuckFormatAttribute>();
		if (format is null)
		{
			throw new InvalidOperationException($"Type '{entityType.Name}' is not decorated with {nameof(PuckFormatAttribute)} and cannot receive a centrally managed PUCK identifier.");
		}

		return GenerateId(format.Notation, segments);
	}

	/// <summary>
	/// Generates a new identifier from a PUCK declaration.
	/// </summary>
	/// <param name="declaration">The PUCK declaration.</param>
	/// <param name="segments">Optional caller-provided segment values.</param>
	/// <returns>The generated identifier.</returns>
	public string GenerateId(string declaration, IReadOnlyList<PuckSegmentInput>? segments = null)
	{
		var notation = notationParser.Parse(declaration);
		var inputs = segments ?? [];
		var renderedSegments = new List<string>();

		using var transaction = context.Database.BeginTransaction();
		for (var index = 0; index < notation.Segments.Count; index++)
		{
			var segmentPattern = notation.Segments[index];
			var input = index < inputs.Count ? inputs[index] : new PuckSegmentInput();
			var discriminator = segmentPattern.UsesDynamicDiscriminator
				? input.Discriminator ?? throw new InvalidOperationException("Dynamic discriminator value missing for PUCK generation.")
				: segmentPattern.StaticDiscriminator ?? string.Empty;

			var numerator = segmentPattern.Numerator.Kind switch
			{
				PuckNumeratorKind.Manual => input.Numerator ?? throw new InvalidOperationException("Manual numerator missing for PUCK generation."),
				PuckNumeratorKind.DateStamp => (input.Date ?? DateOnly.FromDateTime(DateTime.UtcNow)).ToString("yyyyMMdd", CultureInfo.InvariantCulture),
				PuckNumeratorKind.Spiritgem => GenerateSpiritgem(context, segmentPattern.Numerator.Width),
				PuckNumeratorKind.Incremental => GenerateIncremental(context, declaration, segmentPattern.Numerator.Width, segmentPattern.Numerator.Seed),
				_ => throw new InvalidOperationException("Unsupported PUCK numerator kind."),
			};

			renderedSegments.Add($"{discriminator}{numerator}");
		}

		var id = JoinSegments(notation, renderedSegments);
		RegisterId(context, id, declaration);
		context.SaveChanges();
		transaction.Commit();
		return id;
	}

	private string GenerateSpiritgem(PlainfraContext context, int width)
	{
		while (true)
		{
			var candidate = string.Concat(Enumerable.Range(0, width).Select(_ => _random.Next(10).ToString(CultureInfo.InvariantCulture)));
			if (!IdExists(context, candidate))
			{
				return candidate;
			}
		}
	}

	private static string GenerateIncremental(PlainfraContext context, string declaration, int width, long seed)
	{
		var sequence = context.PuckSequences.SingleOrDefault(x => x.Key == declaration);
		if (sequence is null)
		{
			sequence = new PuckSequence
			{
				Key = declaration,
				NextValue = seed + 1,
			};
			context.PuckSequences.Add(sequence);
			return seed.ToString($"D{width}", CultureInfo.InvariantCulture);
		}

		var value = sequence.NextValue;
		sequence.NextValue++;
		return value.ToString($"D{width}", CultureInfo.InvariantCulture);
	}

	private static string JoinSegments(PuckNotation notation, IReadOnlyList<string> segments)
	{
		var builder = new System.Text.StringBuilder();
		for (var index = 0; index < segments.Count; index++)
		{
			if (index > 0)
			{
				var previousSegment = notation.Segments[index - 1];
				builder.Append(previousSegment.Nesting switch
				{
					PuckNestingKind.Filesystem => '/',
					PuckNestingKind.Telescope => '-',
					_ => '-',
				});
			}

			builder.Append(segments[index]);
		}

		return builder.ToString();
	}

	private static bool IdExists(PlainfraContext context, string candidate)
	{
		return context.PuckRegistryEntries.Any(x => x.Id == candidate);
	}

	private static void RegisterId(PlainfraContext context, string id, string declaration)
	{
		context.PuckRegistryEntries.Add(new PuckRegistryEntry
		{
			Id = id,
			Declaration = declaration,
			IssuedUtc = DateTimeOffset.UtcNow,
		});
	}
}