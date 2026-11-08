import { css } from '@a11d/lit'

/**
 * How a surface that was *handed* its entity — a grid row, a list item, a briefing card — looks if it is still
 * there once that entity has been removed.
 *
 * Normally it is not there: the store takes a removed entity out of the listings and collections that held it,
 * so whatever rendered the surface re-renders without it. This is for what the store cannot reach — an array a
 * component built for itself, a single reference, a type whose removals are kept — where the surface goes on
 * holding an instance that is no longer anyone's. It stands down: faded, and deaf to the pointer, so nothing
 * can be opened or edited on an entity that is gone. {@link EntityWatch} sets the attribute.
 */
export const removedEntityStyles = css`
	:host([entity-removed]) {
		opacity: .35;
		filter: grayscale(1);
		pointer-events: none;
		transition: opacity .3s ease, filter .3s ease;
	}
`
