using Microsoft.Extensions.DependencyInjection;
using Pleiades.Plaintorch.Api.Abstractions;
using Pleiades.Tests.Harness;
using Xunit;

namespace Pleiades.Tests.Core;

/// <summary>
/// Note resolution is stored-only: a note resolves to an entity only when its identity maps to a row in the database.
/// A plain note that merely sits in an entity location must not be reported as a type-only "template" entity — the
/// detached, path-shape-driven behavior that mislabeled ordinary notes.
/// </summary>
public sealed class NoteResolutionTests : VaultTestBase
{
	[Fact]
	public async Task Plain_note_in_an_entity_location_resolves_as_not_an_entity()
	{
		var cancellationToken = TestContext.Current.CancellationToken;

		// Any '.md' under the Journal root matches the Polaris cycle model's path shape, but a plain note is not a
		// cycle: it carries no cycle identity and maps to no stored entity.
		var journalRelative = Path.GetRelativePath(Vault.VaultRoot, Vault.Layout.JournalRoot).Replace('\\', '/');
		var notePath = $"{journalRelative}/Stray Thoughts.md";
		Vault.WriteVaultFile(notePath, "Just some notes, not a cycle." + Environment.NewLine);

		var resolution = await Vault.WithScopeAsync(services => services
			.GetRequiredService<ISystemApi>()
			.ResolveVaultNoteAsync(notePath, cancellationToken));

		Assert.False(resolution.Exists);
		Assert.Null(resolution.Entity);
	}
}
