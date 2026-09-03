using Pleiades.Plaintorch.Markdown;
using Pleiades.Vault;
using Pleiades.Vault.Database;
using Pleiades.Vault.Policy;

namespace Pleiades.Plaintorch.Api.Services;

/// <summary>
/// Serves the storage-mode-mechanical entity lifecycle actions — the "policy-derived" API surface (REFACTOR Alpha
/// phase 5a). These are consequences of an entity's storage <em>mode</em>, not of its domain, so one generic
/// implementation dispatched through the phase-4 mode policy objects replaces the per-entity copies that used to
/// differ only by type and audit string. Domain workflow shifts, parenting, and materialization stay explicit on the
/// per-entity APIs.
/// </summary>
public sealed class VaultEntityLifecycleService(
	VaultEntityGateway gateway,
	VaultEntityModelCatalog entityModelCatalog,
	VaultStoragePolicyEngine policyEngine,
	PlaintorchMarkdownStorageService markdownStorageService,
	VaultAuditLogService auditLogService)
{
	/// <summary>
	/// Begins the synchronization boundary of an already-created identity-driven entity: it materializes the entity's
	/// canonical markdown on disk and records the one-time <c>{kind}.begin-boundary</c> audit. Valid only for a storage
	/// mode that gates materialization on a begun boundary (Implicit); the audit action derives from the entity kind.
	/// This is the one generic action the per-entity <c>Begin*BoundaryAsync</c> methods delegate to.
	/// </summary>
	/// <param name="entityType">The CLR entity type whose boundary is begun.</param>
	/// <param name="id">The entity's PUCK identity.</param>
	/// <param name="cancellationToken">A token used to cancel the operation.</param>
	/// <returns>The tracked entity whose boundary was begun.</returns>
	public async Task<object> BeginBoundaryAsync(Type entityType, string id, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(entityType);
		ArgumentException.ThrowIfNullOrWhiteSpace(id);

		var model = entityModelCatalog.GetRequired(entityType);
		var mode = model.Storage?.Mode
			?? throw new InvalidOperationException($"Entity '{entityType.Name}' is not vault-backed and has no synchronization boundary to begin.");

		if (!policyEngine.PolicyFor(mode).BeginsSyncBoundaryOnFirstFile)
		{
			throw new InvalidOperationException(
				$"Storage mode '{mode}' for '{entityType.Name}' does not gate materialization on a synchronization boundary, so there is no boundary to begin.");
		}

		var kind = model.Kind
			?? throw new InvalidOperationException($"Entity '{entityType.Name}' declares no kind and cannot record a boundary-begin audit.");

		var entity = await gateway.FindByIdAsync(entityType, id, track: true, cancellationToken)
			?? throw new InvalidOperationException($"{entityType.Name} '{id}' was not found.");

		await markdownStorageService.RecanonicalizeAsync(entity, beginBoundary: true, cancellationToken: cancellationToken);
		await auditLogService.WriteAsync("api", $"{kind}.begin-boundary", subject: entity, cancellationToken: cancellationToken);
		return entity;
	}
}
