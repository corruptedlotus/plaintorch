namespace Pleiades.Plaintorch.Preferences;

/// <summary>
/// Vault-bound preferences for lunar reflection (PEP104, PEP116): how many reflectives a Polaris cycle draws and
/// what finishing all of them is worth. Resolved through .NET Options — inject
/// <see cref="Microsoft.Extensions.Options.IOptionsSnapshot{T}"/>.
/// </summary>
public sealed class ReflectionPreferences
{
	/// <summary>
	/// How many orbit-less lunar reflect-decrees a beginning Polaris cycle draws as reflectives. The draw comes on top
	/// of every reflect-decree whose orbit resolves onto the cycle's day, which is always taken. Defaults to 3. A
	/// negative value reads as 0, and a pool smaller than this count is taken whole.
	/// </summary>
	public int BaseDailyReflectives { get; set; } = 3;

	/// <summary>
	/// The Celestron granted once every reflective of a single Polaris cycle is done — the daily collection reward.
	/// Defaults to 4. A value of 0 or less grants nothing. It is read when the reward is granted, so changing it never
	/// rewrites a reward already on the ledger.
	/// </summary>
	public int DailyReward { get; set; } = 4;
}
