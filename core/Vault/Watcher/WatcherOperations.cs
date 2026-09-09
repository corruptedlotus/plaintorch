using Pleiades.Diagnostics;

namespace Pleiades.Vault.Watcher;

/// <summary>
/// The declarative catalog of watcher operations and the reason codes each can raise (PEP108), with their default
/// severity, diagnostic category, and message. This is the watcher's adapter onto the domain-neutral
/// operation-status core: instrumentation references these names instead of ad-hoc strings, and both the reporter
/// and the system API read severity/category/message from here so there is one source of truth.
/// </summary>
public static class WatcherOperations
{
	/// <summary>Scope key used for operations that are not scoped to a single path (startup, drain, fatal).</summary>
	public const string GlobalScope = "*";

	// Operation identifiers.
	public const string StartupScan = "watcher.startup-scan";
	public const string DrainTick = "watcher.drain-tick";
	public const string Reconcile = "watcher.reconcile";
	public const string Relocation = "watcher.relocation";
	public const string Root = "watcher.root";
	public const string Process = "watcher.process";
	public const string VaultAccess = "watcher.vault-access";

	// Reason codes.
	public const string ScanFailed = "scan-failed";
	public const string TickFailed = "tick-failed";
	public const string DiscoveryFailed = "discovery-failed";
	public const string PermissionDenied = "permission-denied";
	public const string FileInUse = "file-in-use";
	public const string MarkdownInvalid = "markdown-invalid";
	public const string PuckViolation = "puck-violation";
	public const string PolicyViolation = "policy-violation";
	public const string ForeignFile = "foreign-file";
	public const string SyncFailed = "sync-failed";
	public const string RelocationFailed = "relocation-failed";
	public const string RootInitFailed = "root-init-failed";
	public const string RootError = "root-error";
	public const string Fatal = "fatal";
	public const string VaultInaccessible = "vault-inaccessible";

	/// <summary>Describes a reason code: its diagnostic category, default severity, and human-readable message.</summary>
	public sealed record ReasonDescriptor(string Category, OperationSeverity Severity, string Message);

	private static readonly IReadOnlyDictionary<string, ReasonDescriptor> Descriptors = new Dictionary<string, ReasonDescriptor>(StringComparer.Ordinal)
	{
		[ScanFailed] = new("startup", OperationSeverity.Error, "Startup discovery scan failed and watcher is operating in degraded startup mode."),
		[TickFailed] = new("runtime", OperationSeverity.Error, "Watcher pending-drain tick failed."),
		[DiscoveryFailed] = new("runtime", OperationSeverity.Error, "Watcher candidate discovery failed for an inspected path."),
		[PermissionDenied] = new("filesystem", OperationSeverity.Error, "Watcher could not access a file due to filesystem permissions."),
		[FileInUse] = new("filesystem", OperationSeverity.Suspended, "Watcher could not access a file because it is currently in use by another process."),
		[MarkdownInvalid] = new("validation", OperationSeverity.Error, "Watcher detected markdown/frontmatter validation problems for a candidate file."),
		[PuckViolation] = new("identity", OperationSeverity.Error, "Watcher detected a PUCK identity violation for a candidate file."),
		[PolicyViolation] = new("policy", OperationSeverity.Error, "Watcher detected a storage policy violation for a candidate file."),
		[ForeignFile] = new("policy", OperationSeverity.Warning, "Watcher left an unmanaged file in place — it asserts an identity the vault does not recognise and sits outside enforced territory."),
		[SyncFailed] = new("runtime", OperationSeverity.Error, "Watcher failed to process a discovered candidate sync action."),
		[RelocationFailed] = new("runtime", OperationSeverity.Error, "Watcher failed to process a relocation candidate."),
		[RootInitFailed] = new("filesystem", OperationSeverity.Error, "Watcher failed to initialize a filesystem root observer."),
		[RootError] = new("filesystem", OperationSeverity.Warning, "Filesystem watcher reported a root-level runtime error."),
		[Fatal] = new("runtime", OperationSeverity.Critical, "Watcher encountered a fatal unhandled exception and stopped."),
		[VaultInaccessible] = new("filesystem", OperationSeverity.Error, "Watcher cannot reach the vault or one of its entity roots and has gone to sleep until access is restored."),
	};

	/// <summary>Resolves the descriptor for a reason code, defaulting to a runtime error for unknown codes.</summary>
	public static ReasonDescriptor Describe(string reasonCode)
		=> Descriptors.TryGetValue(reasonCode, out var descriptor)
			? descriptor
			: new ReasonDescriptor("runtime", OperationSeverity.Error, reasonCode);

	// The opaque issue key the API exposes on each WatcherIssueRecord and accepts back to dismiss/restore it. It packs
	// the status identity as operationId::reasonCode::scopeKey; operation ids and reason codes never contain "::", so
	// the scope key (a path, which may itself contain ':') is unambiguously the remainder after the second separator.
	private const string IssueKeySeparator = "::";

	/// <summary>Composes the opaque issue key from a status's identity parts.</summary>
	public static string ComposeIssueKey(string operationId, string reasonCode, string scopeKey)
		=> string.Concat(operationId, IssueKeySeparator, reasonCode, IssueKeySeparator, scopeKey);

	/// <summary>Parses an opaque issue key back into its identity parts. Returns false when the key is malformed.</summary>
	public static bool TryParseIssueKey(string key, out string operationId, out string reasonCode, out string scopeKey)
	{
		operationId = string.Empty;
		reasonCode = string.Empty;
		scopeKey = string.Empty;
		if (string.IsNullOrWhiteSpace(key))
		{
			return false;
		}

		var first = key.IndexOf(IssueKeySeparator, StringComparison.Ordinal);
		if (first < 0)
		{
			return false;
		}

		var second = key.IndexOf(IssueKeySeparator, first + IssueKeySeparator.Length, StringComparison.Ordinal);
		if (second < 0)
		{
			return false;
		}

		operationId = key[..first];
		reasonCode = key[(first + IssueKeySeparator.Length)..second];
		scopeKey = key[(second + IssueKeySeparator.Length)..];
		return operationId.Length > 0 && reasonCode.Length > 0 && scopeKey.Length > 0;
	}
}
