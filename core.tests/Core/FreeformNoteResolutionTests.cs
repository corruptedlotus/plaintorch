using Microsoft.Extensions.DependencyInjection;
using Pleiades.Orchestration;
using Pleiades.Plaintorch.Api.Abstractions;
using Pleiades.Plaintorch.Api.Contracts;
using Pleiades.Tests.Harness;
using Xunit;

namespace Pleiades.Tests.Core;

/// <summary>
/// Note-to-entity resolution must resolve a freeform entity by the note's OWN asserted frontmatter identity, even when
/// the note does not sit in the canonical self-named layout — a folder whose name differs from the file, and/or a
/// location outside the entity root. The path-shape classifier can mis-claim such a note (a freeform directive note
/// gets read as an objective hosted by its own folder); resolution now falls through to the note's frontmatter identity,
/// stored-only, so a note asserting a stored PUCK resolves and a note asserting none does not.
/// </summary>
public sealed class FreeformNoteResolutionTests : VaultTestBase
{
	private Task<StellarDirective> CreateDirectiveAsync(string title)
		=> Vault.WithScopeAsync(services => services.GetRequiredService<IDirectiveApi>()
			.CreateStandaloneAsync(title, cancellationToken: TestContext.Current.CancellationToken));

	private Task<EntityExistence> ResolveAsync(string relativePath)
		=> Vault.WithScopeAsync(services => services.GetRequiredService<ISystemApi>()
			.ResolveVaultNoteAsync(relativePath, TestContext.Current.CancellationToken));

	private void Relocate(string title, string targetRelativePath)
	{
		var canonical = Vault.AbsolutePath($"Directives/{title}/{title}.md");
		var target = Vault.AbsolutePath(targetRelativePath);
		Directory.CreateDirectory(Path.GetDirectoryName(target)!);
		File.Move(canonical, target);
	}

	[Fact]
	public async Task Freeform_directive_note_outside_the_root_with_a_nonmatching_folder_resolves()
	{
		var directive = await CreateDirectiveAsync("Directive Alpha");
		Relocate("Directive Alpha", "Alpha/Directive Alpha.md");

		var existence = await ResolveAsync("Alpha/Directive Alpha.md");

		Assert.True(existence.Exists);
		Assert.Equal("stellar-directive", existence.EntityKind);
		Assert.Equal(directive.Id, Assert.IsType<StellarDirective>(existence.Entity).Id);
		Assert.Equal("Alpha/Directive Alpha.md", existence.AssociatedNote);
	}

	[Fact]
	public async Task Freeform_directive_note_inside_the_root_with_a_nonmatching_folder_resolves()
	{
		var directive = await CreateDirectiveAsync("Directive Beta");
		Relocate("Directive Beta", "Directives/Beta/Directive Beta.md");

		var existence = await ResolveAsync("Directives/Beta/Directive Beta.md");

		Assert.True(existence.Exists);
		Assert.Equal(directive.Id, Assert.IsType<StellarDirective>(existence.Entity).Id);
		Assert.Equal("Directives/Beta/Directive Beta.md", existence.AssociatedNote);
	}

	[Fact]
	public async Task The_canonical_self_named_directive_note_still_resolves()
	{
		var directive = await CreateDirectiveAsync("Campaign");

		var existence = await ResolveAsync("Directives/Campaign/Campaign.md");

		Assert.True(existence.Exists);
		Assert.Equal(directive.Id, Assert.IsType<StellarDirective>(existence.Entity).Id);
		Assert.Equal("Directives/Campaign/Campaign.md", existence.AssociatedNote);
	}

	[Fact]
	public async Task A_plain_note_asserting_no_stored_identity_is_not_an_entity()
	{
		// Stored-only is preserved: a note that merely sits somewhere, with no frontmatter PUCK mapping to a row, is not
		// an entity — the frontmatter fallback must not resurrect the path-shape "template entity" phantom.
		Vault.WriteVaultFile("Alpha/Just A Note.md", "# Just A Note" + Environment.NewLine + "Freeform musings." + Environment.NewLine);

		var existence = await ResolveAsync("Alpha/Just A Note.md");

		Assert.False(existence.Exists);
	}

	[Fact]
	public async Task A_note_asserting_an_unknown_puck_is_not_an_entity()
	{
		// A frontmatter PUCK that maps to no stored entity resolves as not-an-entity (stored-only).
		Vault.WriteVaultFile("Alpha/Ghost.md", "---" + Environment.NewLine + "puck: A99999999" + Environment.NewLine + "---" + Environment.NewLine + "Body." + Environment.NewLine);

		var existence = await ResolveAsync("Alpha/Ghost.md");

		Assert.False(existence.Exists);
	}
}
