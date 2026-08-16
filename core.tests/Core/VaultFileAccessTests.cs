using Pleiades.Vault.Markdown;
using Xunit;

namespace Pleiades.Tests.Core;

/// <summary>
/// Vault file-access failures are classified structurally — by exception type and OS error code — rather than by
/// matching message text (PEP108 phase C).
/// </summary>
public sealed class VaultFileAccessTests
{
	[Fact]
	public void Permission_denied_is_classified_by_type()
		=> Assert.Equal(VaultFileAccessKind.PermissionDenied, VaultFileAccessException.TryClassify(new UnauthorizedAccessException()));

	[Fact]
	public void Sharing_violation_is_classified_by_hresult()
		=> Assert.Equal(VaultFileAccessKind.InUse, VaultFileAccessException.TryClassify(new IOException("x", unchecked((int)0x80070020))));

	[Fact]
	public void Lock_violation_is_classified_by_hresult()
		=> Assert.Equal(VaultFileAccessKind.InUse, VaultFileAccessException.TryClassify(new IOException("x", unchecked((int)0x80070021))));

	[Fact]
	public void A_typed_exception_reports_its_own_kind()
	{
		var typed = new VaultFileAccessException(VaultFileAccessKind.InUse, "a.md", new IOException());
		Assert.Equal(VaultFileAccessKind.InUse, VaultFileAccessException.TryClassify(typed));
	}

	[Fact]
	public void Message_text_the_old_heuristic_matched_is_no_longer_honoured()
	{
		// A plain IOException carrying the string the previous sniffer keyed on, but no sharing-violation HResult,
		// must not be classified as in-use — proving classification no longer reads message text.
		var withOldMessage = new IOException("The process cannot access the file because it is being used by another process.");
		Assert.Null(VaultFileAccessException.TryClassify(withOldMessage));
	}
}
