/**
 * What a fetcher answers with when no trustworthy answer came back — a timeout, a dropped connection, a
 * backend that failed — as opposed to one that answered there is nothing.
 *
 * A fetcher has three things it can say. A value: this is the entity, or the view. `undefined`: the backend
 * answered, and there is nothing here — which removes a known entity, and empties a view. And this: nothing
 * can be said, so whatever was known stays as it was, still stale, and is asked for again next time.
 *
 * It is a statement about reachability only. Why the request failed is not carried here.
 */
export const unresolved: unique symbol = Symbol("unresolved")

/** The type of {@link unresolved}. */
export type Unresolved = typeof unresolved
