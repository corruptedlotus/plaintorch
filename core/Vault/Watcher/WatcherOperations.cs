using Pleiades.Diagnostics;
using Pleiades.Resources;

namespace Pleiades.Vault.Watcher;

/// <summary>
/// The declarative catalog of watcher operations and the reason codes each can raise (PEP108), with their default
/// severity, diagnostic category, and message. This is the watcher's adapter onto the domain-neutral
/// operation-status core: instrumentation references these names instead of ad-hoc strings, and both the reporter
/// and the system API read severity/category/message from here so there is one source of truth. The message text
/// itself lives on the <see cref="WatcherMessages"/> sheet — the catalog only names the entry — so prose is
/// centralised and localisable without touching the catalog.
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
	public const string Identity = "watcher.identity";

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
	public const string DuplicateIdentity = "duplicate-identity";

	/// <summary>
	/// Describes a reason code: its diagnostic category, default severity, and the <see cref="WatcherMessages.Reasons"/>
	/// entry carrying its human-readable message.
	/// </summary>
	/// <param name="Category">The diagnostic category the reason belongs to.</param>
	/// <param name="Severity">The severity a failing check on this reason records by default.</param>
	/// <param name="MessageKey">The <see cref="WatcherMessages.Reasons"/> accessor name whose sheet entry is the message.</param>
	public sealed record ReasonDescriptor(string Category, OperationSeverity Severity, string MessageKey)
	{
		/// <summary>The human-readable message, resolved from the watcher message sheet for the current UI culture.</summary>
		public string Message => WatcherMessages.Reasons.Resolve(MessageKey);
	}

	private static readonly IReadOnlyDictionary<string, ReasonDescriptor> Descriptors = new Dictionary<string, ReasonDescriptor>(StringComparer.Ordinal)
	{
		[ScanFailed] = new("startup", OperationSeverity.Error, nameof(WatcherMessages.Reasons.ScanFailed)),
		[TickFailed] = new("runtime", OperationSeverity.Error, nameof(WatcherMessages.Reasons.TickFailed)),
		[DiscoveryFailed] = new("runtime", OperationSeverity.Error, nameof(WatcherMessages.Reasons.DiscoveryFailed)),
		[PermissionDenied] = new("filesystem", OperationSeverity.Error, nameof(WatcherMessages.Reasons.PermissionDenied)),
		[FileInUse] = new("filesystem", OperationSeverity.Suspended, nameof(WatcherMessages.Reasons.FileInUse)),
		[MarkdownInvalid] = new("validation", OperationSeverity.Error, nameof(WatcherMessages.Reasons.MarkdownInvalid)),
		[PuckViolation] = new("identity", OperationSeverity.Error, nameof(WatcherMessages.Reasons.PuckViolation)),
		[PolicyViolation] = new("policy", OperationSeverity.Error, nameof(WatcherMessages.Reasons.PolicyViolation)),
		[ForeignFile] = new("policy", OperationSeverity.Error, nameof(WatcherMessages.Reasons.ForeignFile)),
		[SyncFailed] = new("runtime", OperationSeverity.Error, nameof(WatcherMessages.Reasons.SyncFailed)),
		[RelocationFailed] = new("runtime", OperationSeverity.Error, nameof(WatcherMessages.Reasons.RelocationFailed)),
		[RootInitFailed] = new("filesystem", OperationSeverity.Error, nameof(WatcherMessages.Reasons.RootInitFailed)),
		[RootError] = new("filesystem", OperationSeverity.Warning, nameof(WatcherMessages.Reasons.RootError)),
		[Fatal] = new("runtime", OperationSeverity.Critical, nameof(WatcherMessages.Reasons.Fatal)),
		[VaultInaccessible] = new("filesystem", OperationSeverity.Error, nameof(WatcherMessages.Reasons.VaultInaccessible)),
		[DuplicateIdentity] = new("identity", OperationSeverity.Error, nameof(WatcherMessages.Reasons.DuplicateIdentity)),
	};

	/// <summary>Every catalogued reason code (the keys of the descriptor table).</summary>
	public static IReadOnlyCollection<string> ReasonCodes => Descriptors.Keys.ToArray();

	/// <summary>
	/// Resolves the descriptor for a reason code, defaulting to a runtime error for unknown codes (whose message is the
	/// code itself, since no sheet entry names it).
	/// </summary>
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
