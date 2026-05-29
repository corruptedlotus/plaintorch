namespace Pleiades.Vault.Watcher;

/// <summary>
/// Defines the canonical watcher issue types tracked by the declarative issue registry.
/// </summary>
public enum VaultWatcherIssueType
{
	/// <summary>
	/// Startup discovery scan failed.
	/// </summary>
	StartupScan,

	/// <summary>
	/// Periodic pending-drain tick failed.
	/// </summary>
	DrainTick,

	/// <summary>
	/// Path discovery failed for an inspected candidate.
	/// </summary>
	Discovery,

	/// <summary>
	/// Candidate synchronization execution failed.
	/// </summary>
	Sync,

	/// <summary>
	/// File access was denied by filesystem permissions.
	/// </summary>
	FilePermissionDenied,

	/// <summary>
	/// File could not be accessed because it is currently locked/in use.
	/// </summary>
	FileInUse,

	/// <summary>
	/// Markdown frontmatter/validation parsing failed for a candidate.
	/// </summary>
	MarkdownValidationError,

	/// <summary>
	/// Candidate violated PUCK identity requirements.
	/// </summary>
	PuckViolation,

	/// <summary>
	/// Candidate violated storage policy constraints.
	/// </summary>
	PolicyViolation,

	/// <summary>
	/// Relocation processing failed.
	/// </summary>
	Relocation,

	/// <summary>
	/// Filesystem watcher initialization for a root failed.
	/// </summary>
	RootInitialization,

	/// <summary>
	/// Runtime filesystem watcher error was observed for a root.
	/// </summary>
	FilesystemRootError,

	/// <summary>
	/// Fatal unhandled watcher crash occurred.
	/// </summary>
	Fatal,
}
