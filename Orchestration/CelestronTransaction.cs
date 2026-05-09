using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Pleiades.Orchestration;

/// <summary>
/// Represents a gain or loss entry in the Celestron ledger.
/// </summary>
public sealed class CelestronTransaction
{
	[Key]
	/// <summary>
	/// Gets or sets the unique transaction identifier.
	/// </summary>
	public Guid TransactionId { get; set; } = Guid.NewGuid();

	/// <summary>
	/// Gets or sets the time the transaction occurred.
	/// </summary>
	public DateTimeOffset Time { get; set; } = DateTimeOffset.UtcNow;

	/// <summary>
	/// Gets or sets the signed transaction amount.
	/// </summary>
	public decimal Amount { get; set; }

	/// <summary>
	/// Gets or sets the related source PUCK when available.
	/// </summary>
	public string? SourcePuck { get; set; }

	/// <summary>
	/// Gets or sets the optional transaction description.
	/// </summary>
	public string? Description { get; set; }
}