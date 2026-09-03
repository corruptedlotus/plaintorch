using Pleiades.Plaintorch.Markdown;
using Pleiades.Puck;
using Pleiades.Vault;
using Pleiades.Vault.Database;
using Pleiades.Vault.Markdown;
using Pleiades.Vault.Policy;
using Pleiades.Vault.Watcher;

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
	VaultLayout layout,
	VaultMarkdownDiscoveryService discoveryService,
	VaultWatcherSyncService watcherSyncService,
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

	/// <summary>
	/// Creates a new entity from an existing user-authored file — the <c>init</c> action — for any identity-driven mode
	/// that supports it (Freeform and Implicit, per the mode policy's <see cref="IVaultStorageModePolicyService.CanCreateFromFile"/>).
	/// A file with no frontmatter PUCK is adopted as a manual init that mints a fresh identity; the audit action derives
	/// from the entity kind. This is the one generic action the per-entity <c>InitializeFromPathAsync</c> methods delegate to.
	/// </summary>
	/// <param name="entityType">The CLR entity type to create (a family anchor materializes its identity-selected member).</param>
	/// <param name="vaultRelativePath">The vault-relative path of the file to adopt.</param>
	/// <param name="cancellationToken">A token used to cancel the operation.</param>
	/// <returns>The created, tracked entity.</returns>
	public async Task<object> InitializeFromFileAsync(Type entityType, string vaultRelativePath, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(entityType);
		ArgumentException.ThrowIfNullOrWhiteSpace(vaultRelativePath);

		var model = entityModelCatalog.GetRequired(entityType);
		var mode = model.Storage?.Mode
			?? throw new InvalidOperationException($"Entity '{entityType.Name}' is not vault-backed and cannot be initialized from a file.");
		if (!policyEngine.PolicyFor(mode).CanCreateFromFile)
		{
			throw new InvalidOperationException($"Storage mode '{mode}' for '{entityType.Name}' does not support initialization from a file.");
		}

		var kind = model.Kind
			?? throw new InvalidOperationException($"Entity '{entityType.Name}' declares no kind and cannot record an init audit.");

		var normalizedRelativePath = vaultRelativePath
			.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar)
			.TrimStart(Path.DirectorySeparatorChar);
		var absolutePath = Path.GetFullPath(Path.Combine(layout.VaultRoot, normalizedRelativePath));
		var normalizedRoot = layout.VaultRoot.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
		if (!absolutePath.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
		{
			throw new InvalidOperationException($"{kind} init path escapes the active vault root.");
		}

		var candidate = await discoveryService.InspectPathAsync(absolutePath, "api-init", cancellationToken)
			?? await discoveryService.InspectInitPathAsync(absolutePath, entityType, "api-init-fallback", cancellationToken)
			?? throw new InvalidOperationException($"Init path '{normalizedRelativePath}' is not a markdown candidate eligible for {kind} initialization.");

		if (candidate.Model.EntityType != entityType)
		{
			throw new InvalidOperationException($"Init path '{normalizedRelativePath}' resolved to '{candidate.Model.EntityName}', not {entityType.Name}.");
		}

		// A user-authored file carrying no frontmatter PUCK is a manual init: the mode adopts it and mints a fresh
		// identity. This is now structural — an ignore decision with no path identity whose only issues are the
		// missing-required-PUCK-input kind — replacing the old reason-string sniff.
		if (IsManualInitCandidate(candidate))
		{
			candidate = NormalizeManualInitCandidate(candidate);
		}

		if (!candidate.IsValid)
		{
			var reasons = string.Join("; ", candidate.Issues.Select(issue => $"{issue.FieldPath}: {issue.Message}"));
			throw new InvalidOperationException($"Init path '{normalizedRelativePath}' violates {kind} init policy: {reasons}");
		}

		if (candidate.SuggestedAction != VaultSyncAction.CreateFromFile)
		{
			throw new InvalidOperationException(
				$"Init path '{normalizedRelativePath}' is not eligible for initialization. Suggested action '{candidate.SuggestedAction}' indicates this path must be handled by watcher reconciliation policy instead. Reason: {candidate.SuggestedReason ?? "n/a"}");
		}

		await watcherSyncService.InitializeFromFileAsync(candidate, "api-init", cancellationToken);
		var createdId = ((IPuckNamedEntity)candidate.ParsedModel).Id;
		var created = await gateway.FindByIdAsync(entityType, createdId, track: true, cancellationToken)
			?? throw new InvalidOperationException($"{kind} initialization completed but the created entity could not be loaded.");

		await auditLogService.WriteAsync("api", $"{kind}.init", subject: created, details: new { path = normalizedRelativePath }, cancellationToken: cancellationToken);
		return created;
	}

	// A no-identity file whose only issues (if any) are the missing-required-PUCK-input kind: the user authored a file
	// the mode would ignore for want of an identity, and init adopts it by minting one.
	private static bool IsManualInitCandidate(VaultSyncCandidate candidate)
		=> candidate.SuggestedAction == VaultSyncAction.Ignore
			&& string.IsNullOrWhiteSpace(candidate.PathId)
			&& candidate.Issues.All(IsMissingRequiredPuckInputIssue);

	private static VaultSyncCandidate NormalizeManualInitCandidate(VaultSyncCandidate candidate)
		=> candidate with
		{
			Issues = candidate.Issues.Where(issue => !IsMissingRequiredPuckInputIssue(issue)).ToList(),
			SuggestedAction = VaultSyncAction.CreateFromFile,
			SuggestedReason = "Manual initialization from a file without a frontmatter PUCK, requested through the API.",
		};

	private static bool IsMissingRequiredPuckInputIssue(MarkdownValidationIssue issue)
		=> string.Equals(issue.FieldPath, "id", StringComparison.OrdinalIgnoreCase)
			&& issue.Message.Contains("missing required caller-provided PUCK input", StringComparison.OrdinalIgnoreCase);
}
