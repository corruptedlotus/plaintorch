using System.Resources;
using System.Runtime.CompilerServices;

namespace Pleiades.Resources;

/// <summary>
/// Typed access to the vault message sheet (<c>VaultMessages.resx</c>): operator-facing text about the vault as a whole
/// rather than one file in it — currently why a vault could not be activated. Each member reads the sheet entry named
/// <c>&lt;Group&gt;.&lt;Member&gt;</c>, resolved at call time against the current UI culture.
/// </summary>
public static class VaultMessages
{
	private static readonly ResourceManager Sheet = MessageSheet.For(nameof(VaultMessages));

	/// <summary>Why a vault was refused at activation; the host status carries this text as its failure message.</summary>
	public static class Activation
	{
		private const string Group = "Activation.";

		private static string Format(object?[] args, [CallerMemberName] string key = "") => MessageSheet.Format(Sheet, Group, key, args);

		/// <summary>
		/// "The database of vault '{vault}' was upgraded by a newer PLAINTORCH core (migration {migration} is unknown to this
		/// core). Update this core to open it; the database was left untouched."
		/// </summary>
		public static string DatabaseAheadOfCore(string vaultPath, string newestUnknownMigration) => Format([vaultPath, newestUnknownMigration]);
	}
}
