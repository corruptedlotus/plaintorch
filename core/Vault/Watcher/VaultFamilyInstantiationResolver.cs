using Pleiades.Puck;

namespace Pleiades.Vault.Watcher;

/// <summary>
/// Resolves which concrete CLR type a path-composed entity should instantiate, from the file's PUCK identity.
/// </summary>
/// <remarks>
/// A path-sync model may anchor a polymorphic (table-per-hierarchy) family under an abstract base — the directive
/// model anchors <c>Directive</c> and spans <c>StellarDirective</c>/<c>LunarDirective</c>. Which concrete member a
/// given file is depends on its identity, not on where it lives: the identity's notation selects the member
/// (<c>A…</c> mints a stellar directive, <c>LUNA…</c> a lunar one). Selection is therefore identity-driven, matching
/// the declared PUCK notation of each family member, with no coupling to the file's location — so a member's storage
/// policy (Freeform, Synced, …) stays a pure attribute swap with no strings attached here.
///
/// When no identity is known, or the identity is minted by none or more than one member's declaration, the model's
/// declared default instantiation type is used. This preserves single-member models exactly and keeps a family's
/// declared fallback member (the model's <see cref="VaultPathSyncModel.ConcreteType"/>) as the type for brand-new,
/// as-yet-unidentified files.
/// </remarks>
public sealed class VaultFamilyInstantiationResolver(
	VaultEntityModelCatalog entityModelCatalog,
	PuckTokenizer puckTokenizer)
{
	/// <summary>
	/// Resolves the concrete CLR type to instantiate for a path-composed entity of the given model and identity.
	/// </summary>
	/// <param name="model">The path-sync model whose family (if any) supplies the candidate members.</param>
	/// <param name="identity">The file's resolved PUCK identity, or <see langword="null"/> when unknown.</param>
	/// <returns>
	/// The concrete family member whose declaration mints <paramref name="identity"/> when exactly one does;
	/// otherwise the model's declared default instantiation type.
	/// </returns>
	public Type ResolveInstantiationType(VaultPathSyncModel model, string? identity)
	{
		ArgumentNullException.ThrowIfNull(model);

		var fallback = model.InstantiationType;
		if (string.IsNullOrWhiteSpace(identity)
			|| !entityModelCatalog.TryGetFamily(model.EntityType, out var family)
			|| family is null)
		{
			return fallback;
		}

		Type? resolved = null;
		foreach (var member in family.Members)
		{
			if (!Mints(member.PuckDeclaration, identity))
			{
				continue;
			}

			if (resolved is not null)
			{
				// The identity is minted by more than one member's declaration; defer to the declared default
				// rather than guess between ambiguous siblings.
				return fallback;
			}

			resolved = member.EntityType;
		}

		return resolved ?? fallback;
	}

	/// <summary>
	/// Determines whether an identity is one the model's kind could mint: the declared PUCK notation of the model's entity
	/// type — or, when the model anchors a family, of one of its members — tokenizes it. This is how the identity a note
	/// asserts names its kind, from declarations alone and without asking the vault.
	/// </summary>
	/// <param name="model">The path-sync model whose kind is asked about.</param>
	/// <param name="identity">The identity a note asserts.</param>
	/// <returns><see langword="true"/> when the model's kind could have minted <paramref name="identity"/>.</returns>
	public bool Mints(VaultPathSyncModel model, string? identity)
	{
		ArgumentNullException.ThrowIfNull(model);
		if (string.IsNullOrWhiteSpace(identity))
		{
			return false;
		}

		var declarations = entityModelCatalog.TryGetFamily(model.EntityType, out var family) && family is not null
			? family.Members.Select(static member => member.PuckDeclaration)
			: [entityModelCatalog.TryGet(model.EntityType, out var declared) ? declared?.PuckDeclaration : null];
		return declarations.Any(declaration => Mints(declaration, identity));
	}

	private bool Mints(string? declaration, string identity)
	{
		if (string.IsNullOrWhiteSpace(declaration))
		{
			return false;
		}

		try
		{
			// Tokenization is the notation gate: a declaration only tokenizes an identity it could itself mint.
			puckTokenizer.Tokenize(declaration, identity);
			return true;
		}
		catch (Exception exception) when (exception is FormatException or InvalidOperationException or NotSupportedException)
		{
			return false;
		}
	}
}
