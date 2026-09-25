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
	/// <summary>Scope key used for operations that are not scoped to a single path (startup, drain, roots, fatal).</summary>
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
	public const string RootsUnresolved = "roots-unresolved";
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

	// One severity per reason, graded by what the issue means for the watcher (see OperationSeverity) rather than by the
	// stage that noticed it. The three content reasons (markdown-invalid, puck-violation, policy-violation) are errors
	// only while the problem stands in the user's file: when the watcher enforced it (rewrote or purged the file) the
	// reporter records them as warnings instead (see WatcherStatusReporter.ContentSeverity).
	private static readonly IReadOnlyDictionary<string, ReasonDescriptor> Descriptors = new Dictionary<string, ReasonDescriptor>(StringComparer.Ordinal)
	{
		// Fatal: the watcher cannot do its job at all; it sleeps and retries, and health reads standby.
		[ScanFailed] = new("startup", OperationSeverity.Fatal, nameof(WatcherMessages.Reasons.ScanFailed)),
		[VaultInaccessible] = new("filesystem", OperationSeverity.Fatal, nameof(WatcherMessages.Reasons.VaultInaccessible)),
		[RootsUnresolved] = new("filesystem", OperationSeverity.Fatal, nameof(WatcherMessages.Reasons.RootsUnresolved)),
		[Fatal] = new("runtime", OperationSeverity.Fatal, nameof(WatcherMessages.Reasons.Fatal)),

		// Critical: a technical failure blocks part of the job (a file, a root, a tick).
		[TickFailed] = new("runtime", OperationSeverity.Critical, nameof(WatcherMessages.Reasons.TickFailed)),
		[DiscoveryFailed] = new("runtime", OperationSeverity.Critical, nameof(WatcherMessages.Reasons.DiscoveryFailed)),
		[PermissionDenied] = new("filesystem", OperationSeverity.Critical, nameof(WatcherMessages.Reasons.PermissionDenied)),
		[SyncFailed] = new("runtime", OperationSeverity.Critical, nameof(WatcherMessages.Reasons.SyncFailed)),
		[RootInitFailed] = new("filesystem", OperationSeverity.Critical, nameof(WatcherMessages.Reasons.RootInitFailed)),
		[RootError] = new("filesystem", OperationSeverity.Critical, nameof(WatcherMessages.Reasons.RootError)),

		// Error: invalid or illegal content the user must resolve.
		[MarkdownInvalid] = new("validation", OperationSeverity.Error, nameof(WatcherMessages.Reasons.MarkdownInvalid)),
		[PuckViolation] = new("identity", OperationSeverity.Error, nameof(WatcherMessages.Reasons.PuckViolation)),
		[PolicyViolation] = new("policy", OperationSeverity.Error, nameof(WatcherMessages.Reasons.PolicyViolation)),
		[ForeignFile] = new("policy", OperationSeverity.Error, nameof(WatcherMessages.Reasons.ForeignFile)),
		[DuplicateIdentity] = new("identity", OperationSeverity.Error, nameof(WatcherMessages.Reasons.DuplicateIdentity)),

		// Warning: nothing breaks, and the watcher gets past it on its own.
		[FileInUse] = new("filesystem", OperationSeverity.Warning, nameof(WatcherMessages.Reasons.FileInUse)),
		[RelocationFailed] = new("runtime", OperationSeverity.Warning, nameof(WatcherMessages.Reasons.RelocationFailed)),
	};

	/// <summary>Every catalogued reason code (the keys of the descriptor table).</summary>
	public static IReadOnlyCollection<string> ReasonCodes => Descriptors.Keys.ToArray();

	/// <summary>
	/// Resolves the descriptor for a reason code, defaulting to a critical runtime failure for unknown codes (an
	/// unclassified failure is technical, not a matter of content; its message is the code itself, since no sheet entry
	/// names it).
	/// </summary>
	public static ReasonDescriptor Describe(string reasonCode)
		=> Descriptors.TryGetValue(reasonCode, out var descriptor)
			? descriptor
			: new ReasonDescriptor("runtime", OperationSeverity.Critical, reasonCode);

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
