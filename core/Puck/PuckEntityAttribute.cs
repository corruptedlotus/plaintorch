using System.Reflection;

namespace Pleiades.Puck;

/// <summary>
/// Declares the stable, client-agnostic entity kind key used to identify a PUCK-powered model type across
/// API boundaries. The kind is domain metadata owned by the entity itself, so resolution surfaces never need
/// to hard-code a CLR-type-to-kind mapping.
/// Not inherited, so sibling types in a shared-table hierarchy each declare their own kind explicitly.
/// </summary>
/// <param name="kind">The stable entity kind key, for example <c>directive</c> or <c>lore-page</c>.</param>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class PuckEntityAttribute(string kind) : Attribute
{
	/// <summary>
	/// Gets the stable, client-agnostic entity kind key.
	/// </summary>
	public string Kind { get; } = kind;

	/// <summary>
	/// Resolves the declared entity kind for a CLR type, or <see langword="null"/> when the type is not decorated
	/// with <see cref="PuckEntityAttribute"/>.
	/// </summary>
	/// <param name="entityType">The entity CLR type to inspect.</param>
	/// <returns>The declared entity kind, or <see langword="null"/> when none is present.</returns>
	public static string? ResolveKind(Type entityType)
	{
		ArgumentNullException.ThrowIfNull(entityType);
		return entityType.GetCustomAttribute<PuckEntityAttribute>(inherit: false)?.Kind;
	}
}
