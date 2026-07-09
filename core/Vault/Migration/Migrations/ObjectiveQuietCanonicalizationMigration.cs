using Pleiades.Orchestration;
using Pleiades.Plaintorch.Markdown;
using Pleiades.Vault.Database;

namespace Pleiades.Vault.Migration.Migrations;

/// <summary>
/// Migrates objectives from the pre-PEP091 filename-embedded PUCK form to the current quiet frontmatter identity.
/// </summary>
/// <remarks>
/// Advances the vault from v1 to v2. Legacy objective files named <c>{id} - Title.md</c> (with no <c>puck</c>
/// frontmatter) are re-canonicalised to <c>Title.md</c> with a <c>puck</c> frontmatter field. Because objectives are now
/// implicit, each converted file also begins its synchronization boundary so subsequent deletions are authoritative.
/// </remarks>
public sealed class ObjectiveQuietCanonicalizationMigration(
	VaultLoader loader,
	VaultConventionSetFactory conventionSetFactory,
	PlainfraContext context,
	PlaintorchMarkdownStorageService storageService,
	VaultAuditLogService auditLogService,
	VaultLayout layout,
	ILogger<ObjectiveQuietCanonicalizationMigration> logger)
	: VaultRecanonicalizationMigration(loader, conventionSetFactory, context, storageService, auditLogService, layout, logger)
{
	/// <inheritdoc />
	public override int FromVersion => VaultSchema.BaselineVersion;

	/// <inheritdoc />
	public override int ToVersion => 2;

	/// <inheritdoc />
	public override string Id => "20260710_0001_ObjectiveQuietCanonicalization";

	/// <inheritdoc />
	public override string Description => "Convert filename-embedded objective PUCK to quiet frontmatter identity (PEP091).";

	/// <inheritdoc />
	protected override IReadOnlyCollection<Type> TargetTypes => [typeof(Objective)];

	/// <inheritdoc />
	protected override bool BeginBoundary => true;
}
