namespace Pleiades.Vault.Markdown;

/// <summary>
/// Thrown when markdown/frontmatter cannot be hydrated into a CLR model at all (PEP108 phase C). Being a distinct
/// type lets the watcher recognise a hard deserialization failure by type rather than by matching message text.
/// Soft, per-field validation problems remain non-throwing and surface as candidate issues instead.
/// </summary>
public sealed class MarkdownDeserializationException : InvalidOperationException
{
	/// <summary>Initializes the exception with a message and optional inner exception.</summary>
	public MarkdownDeserializationException(string message, Exception? innerException = null)
		: base(message, innerException)
	{
	}
}
