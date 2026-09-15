using System.Resources;
using System.Runtime.CompilerServices;

namespace Pleiades.Resources;

/// <summary>
/// Typed access to the watcher's message sheet (<c>WatcherMessages.resx</c>): every operator-facing string a watcher
/// issue can carry — reason descriptor messages, composed detail lines, storage-policy decision reasons, and freeform
/// ownership-boundary violations. Each member reads the sheet entry named <c>&lt;Group&gt;.&lt;Member&gt;</c>, so the
/// accessor is the key and the sheet is the only place prose lives; nothing in the pipeline hardcodes message text.
/// Members resolve at call time against the current UI culture, which is what a later locale setting will drive.
/// </summary>
public static class WatcherMessages
{
	private static readonly ResourceManager Sheet = MessageSheet.For(nameof(WatcherMessages));

	/// <summary>The descriptor message for each <c>WatcherOperations</c> reason code (keyed <c>Reasons.&lt;Code&gt;</c>).</summary>
	public static class Reasons
	{
		private const string Group = "Reasons.";

		/// <summary>Resolves a reason message by its key (the accessor name), for the descriptor table.</summary>
		internal static string Resolve(string key) => MessageSheet.Resolve(Sheet, Group, key);

		private static string Get([CallerMemberName] string key = "") => Resolve(key);

		public static string ScanFailed => Get();
		public static string TickFailed => Get();
		public static string DiscoveryFailed => Get();
		public static string PermissionDenied => Get();
		public static string FileInUse => Get();
		public static string MarkdownInvalid => Get();
		public static string PuckViolation => Get();
		public static string PolicyViolation => Get();
		public static string ForeignFile => Get();
		public static string SyncFailed => Get();
		public static string RelocationFailed => Get();
		public static string RootInitFailed => Get();
		public static string RootError => Get();
		public static string Fatal => Get();
		public static string VaultInaccessible => Get();
		public static string DuplicateIdentity => Get();
	}

	/// <summary>Parameterised detail lines the reporter appends to a reason message.</summary>
	public static class Details
	{
		private const string Group = "Details.";

		private static string Format(object?[] args, [CallerMemberName] string key = "") => MessageSheet.Format(Sheet, Group, key, args);

		/// <summary>"asserted by {count} files: {files}".</summary>
		public static string DuplicateIdentity(int fileCount, string files) => Format([fileCount, files]);

		/// <summary>"{count} validation issue(s). {field}: {message}".</summary>
		public static string ValidationIssues(int issueCount, string? fieldPath, string? message) => Format([issueCount, fieldPath, message]);

		/// <summary>"'{path}' is not accessible."</summary>
		public static string PathNotAccessible(string path) => Format([path]);

		/// <summary>"Vault path '{path}' is not accessible."</summary>
		public static string VaultPathNotAccessible(string path) => Format([path]);

		/// <summary>"Vault path '{path}' became inaccessible."</summary>
		public static string VaultPathBecameInaccessible(string path) => Format([path]);
	}

	/// <summary>The reason a storage-mode policy gives for its reconciliation decision (<c>VaultSyncDecision.Reason</c>).</summary>
	public static class Decisions
	{
		private const string Group = "Decisions.";

		private static string Get([CallerMemberName] string key = "") => MessageSheet.Resolve(Sheet, Group, key);

		// Shared across path-bound modes.
		public static string UntitledPlaceholderIgnored => Get();
		public static string MissingRequiredPuckInputConflict => Get();
		public static string PathIdentityExists => Get();
		public static string InvalidCandidateConflict => Get();
		// Shared across identity-driven modes.
		public static string FrontmatterIdentityExists => Get();
		// API-initiated manual init.
		public static string ManualInitializationFromFile => Get();

		public static string FreeformNoIdentityIgnored => Get();
		public static string FreeformDeletedFileRemovesEntity => Get();
		public static string FreeformMissingFileUnknownIdentity => Get();
		public static string FreeformUnrecognisedAssertionWithIssues => Get();
		public static string FreeformInvalidCandidateRewritten => Get();
		public static string FreeformUnrecognisedAssertion => Get();

		public static string ImplicitNoIdentityIgnored => Get();
		public static string ImplicitDeletedFileRemovesEntity => Get();
		public static string ImplicitBoundaryNotBegun => Get();
		public static string ImplicitMissingFileUnknownIdentity => Get();
		public static string ImplicitUnrecognisedAssertionWithIssues => Get();
		public static string ImplicitInvalidCandidateRewritten => Get();
		public static string ImplicitUnrecognisedAssertion => Get();

		public static string EnforcedMissingRequiredPuckInputPurged => Get();
		public static string EnforcedTitleOnlyPurged => Get();
		public static string EnforcedDeletedFileRewritten => Get();
		public static string EnforcedMissingFileUnknownEntity => Get();
		public static string EnforcedUnknownFileWithIssuesPurged => Get();
		public static string EnforcedInvalidCandidateRewritten => Get();
		public static string EnforcedUnknownFilePurged => Get();

		public static string OptionalMissingRequiredPuckInputIgnored => Get();
		public static string OptionalTitleOnlyIgnored => Get();
		public static string OptionalDeletedFileRemovesEntity => Get();
		public static string OptionalMissingFileUnknownEntity => Get();
		public static string OptionalInvalidStandaloneIgnored => Get();
		public static string OptionalInvalidCandidateRewritten => Get();
		public static string OptionalStandaloneIgnored => Get();

		public static string SyncedTitleOnlyCreated => Get();
		public static string SyncedDeletedFileRemovesEntity => Get();
		public static string SyncedMissingFileUnknownEntity => Get();
		public static string SyncedInvalidCandidateRewritten => Get();
		public static string SyncedFileOriginatedCreation => Get();

		public static string FileFirstTitleOnlyCreated => Get();
		public static string FileFirstDeletedFileRemovesEntity => Get();
		public static string FileFirstMissingFileUnknownEntity => Get();
		public static string FileFirstFileOriginatedCreation => Get();
	}

	/// <summary>Why a freeform directive may not assert ownership at a path (<c>VaultWatcherPathPolicy</c>).</summary>
	public static class PathViolations
	{
		private const string Group = "PathViolations.";

		private static string Get([CallerMemberName] string key = "") => MessageSheet.Resolve(Sheet, Group, key);

		public static string PathEmpty => Get();
		public static string PathOutsideVaultRoot => Get();
		public static string PathNotMarkdown => Get();
		public static string PathHasNoDirectory => Get();
		public static string CannotOwnVaultRoot => Get();
		public static string CannotOwnEntityRoot => Get();
		public static string CannotOwnPartition => Get();
		public static string UnderNonDirectiveRoot => Get();
		public static string ParentUnderNonDirectiveRoot => Get();
	}
}
