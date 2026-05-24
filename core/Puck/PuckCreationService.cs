namespace Pleiades.Puck;

/// <summary>
/// Resolves caller-supplied PUCK input against a declaration, requiring manual segments and ignoring caller input for auto-generated segments.
/// </summary>
public sealed class PuckCreationService(
	PuckIdService puckIdService,
	PuckRuntimeCompilationCatalog compilationCatalog,
	PuckTokenizer puckTokenizer)
{
	/// <summary>
	/// Determines whether a type's PUCK declaration requires caller-provided input.
	/// </summary>
	public bool RequiresCallerInputFor<T>()
	{
		return RequiresCallerInputFor(typeof(T));
	}

	/// <summary>
	/// Determines whether a type's PUCK declaration requires caller-provided input.
	/// </summary>
	public bool RequiresCallerInputFor(Type entityType)
	{
		ArgumentNullException.ThrowIfNull(entityType);
		return compilationCatalog.GetCompiled(entityType).RequiresCallerInput;
	}

	/// <summary>
	/// Creates an identifier for a PUCK-managed type, merging required caller input with system-generated segments.
	/// </summary>
	public string CreateIdFor<T>(string? requestedId = null, IReadOnlyList<PuckSegmentInput>? systemSegments = null)
	{
		return CreateIdFor(typeof(T), requestedId, systemSegments);
	}

	/// <summary>
	/// Creates an identifier for a PUCK-managed type, merging required caller input with system-generated segments.
	/// </summary>
	public string CreateIdFor(Type entityType, string? requestedId = null, IReadOnlyList<PuckSegmentInput>? systemSegments = null)
	{
		ArgumentNullException.ThrowIfNull(entityType);
		var compiled = compilationCatalog.GetCompiled(entityType);
		if (!compiled.RequiresCallerInput)
		{
			return puckIdService.GenerateIdFor(entityType, systemSegments);
		}

		if (string.IsNullOrWhiteSpace(requestedId))
		{
			throw new InvalidOperationException($"Type '{entityType.Name}' requires caller-provided PUCK input because its declaration contains manual or dynamic segments.");
		}

		var requestedTokens = puckTokenizer.Tokenize(compiled.Notation, requestedId);
		if (requestedTokens.Segments.Count < compiled.Notation.Segments.Count)
		{
			throw new InvalidOperationException($"Requested PUCK '{requestedId}' does not provide enough segments for type '{entityType.Name}'.");
		}

		var mergedInputs = MergeInputs(compiled.Notation, requestedTokens, systemSegments);
		return puckIdService.GenerateIdFor(entityType, mergedInputs);
	}

	private static IReadOnlyList<PuckSegmentInput> MergeInputs(
		PuckNotation notation,
		PuckTokenization requestedTokens,
		IReadOnlyList<PuckSegmentInput>? systemSegments)
	{
		var merged = new List<PuckSegmentInput>(notation.Segments.Count);
		for (var index = 0; index < notation.Segments.Count; index++)
		{
			var pattern = notation.Segments[index];
			var token = requestedTokens.Segments[index];
			var systemInput = index < (systemSegments?.Count ?? 0)
				? systemSegments![index]
				: new PuckSegmentInput();

			merged.Add(new PuckSegmentInput(
				Discriminator: pattern.UsesDynamicDiscriminator ? token.Discriminator : systemInput.Discriminator,
				Numerator: pattern.Numerator.Kind == PuckNumeratorKind.Manual ? token.Numerator : systemInput.Numerator,
				Date: systemInput.Date));
		}

		return merged;
	}

	private static bool RequiresCallerInput(PuckSegmentPattern pattern)
	{
		return pattern.UsesDynamicDiscriminator || pattern.Numerator.Kind == PuckNumeratorKind.Manual;
	}
}