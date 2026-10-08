using Pleiades.Orchestration.Lifecycle;

namespace Pleiades.Plaintorch.Api.Contracts;

/// <summary>
/// How one kind's lifecycle reads (PEP103), served so a client shows a status the way the core reads it — which statuses
/// lie on the main line and which set the item aside, and which state a move enters — rather than keeping a copy of the
/// declarations of its own.
/// </summary>
/// <param name="Kind">The kind's runtime type name, as the API's <c>@type</c> carries it.</param>
/// <param name="States">The lifecycle states the kind passes through, in order.</param>
/// <param name="Statuses">
/// Its status values in value order, each with the state it stands for. Empty for a kind whose state is not a status, as a
/// checkpoint's is not.
/// </param>
public sealed record LifecycleKindDescription(string Kind, IReadOnlyList<LifecycleState> States, IReadOnlyList<LifecycleStatusDescription> Statuses);

/// <summary>
/// One status of a kind and the lifecycle state it stands for (PEP103).
/// </summary>
/// <param name="Value">The status's wire value.</param>
/// <param name="Name">Its name.</param>
/// <param name="State">The lifecycle state it stands for.</param>
/// <param name="IsDefault">Whether it is the status the core sets for its state of its own accord, the lowest-valued one.</param>
public sealed record LifecycleStatusDescription(int Value, string Name, LifecycleState State, bool IsDefault);
