namespace Pleiades.Orbits;

/// <summary>
/// The implicit cycle of an indexed global, mirroring the TypeScript <c>cycles.ts</c>. An indexed node written at the
/// top level (the root, or an operand of a top-level set operation) is bound to the cycle its unit naturally recurs in:
/// <c>m{15}</c> is <c>h[m{15}]</c>, minute 15 of every hour; <c>h{11}</c> is <c>d[h{11}]</c>, every day at 11:00;
/// <c>w{12}</c> is <c>y[w{12}]</c>, calendar week 12. A year has none: it is the largest unit, and it never recurs.
/// </summary>
/// <remarks>
/// The binding is a plain rewrite, so an indexed global reads in every way as if its cycle had been written around it:
/// its limits count per cycle, and the schedule starts at the cycle's first complete period. A bare global
/// (<c>d</c>, <c>w[d{1}]</c>) is not indexed and stays as written.
/// </remarks>
public static class OrbitCycles
{
	/// <summary>
	/// The cycle a unit naturally recurs in: <c>s → m → h → d → M → y</c>, and <c>w → y</c>. <see langword="null"/>
	/// for a year.
	/// </summary>
	public static OrbitUnit? ImplicitCycle(OrbitUnit unit) => unit switch
	{
		OrbitUnit.Second => OrbitUnit.Minute,
		OrbitUnit.Minute => OrbitUnit.Hour,
		OrbitUnit.Hour => OrbitUnit.Day,
		OrbitUnit.Day => OrbitUnit.Month,
		OrbitUnit.Week => OrbitUnit.Year,
		OrbitUnit.Month => OrbitUnit.Year,
		_ => null,
	};

	/// <summary>
	/// Binds every indexed global of <paramref name="node"/> to its implicit cycle. A <c>z{h:m}</c> literal is an indexed
	/// hour, so it binds to the day; a <c>Z{…}</c> literal is rooted at its year and stays as written.
	/// </summary>
	public static OrbitAstNode Bind(OrbitAstNode node)
	{
		if (node is OrbitSetOperationNode set)
		{
			return new OrbitSetOperationNode { Operator = set.Operator, Left = Bind(set.Left), Right = Bind(set.Right) };
		}

		OrbitUnit? unit = node switch
		{
			OrbitDateTimeLiteralNode literal => literal.Year is not null ? OrbitUnit.Year : OrbitUnit.Hour,
			OrbitTimeUnitNode { Indices: not null } indexed => indexed.Unit,
			_ => null,
		};

		return unit is { } bound && ImplicitCycle(bound) is { } cycle
			? new OrbitTimeUnitNode { Unit = cycle, Child = node }
			: node;
	}
}
