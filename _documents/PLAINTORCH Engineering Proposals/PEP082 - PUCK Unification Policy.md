---
status: implemented
patches:
  - Patch082.1 - Revert Discrimination Boundary & Fix Uniqueness Logic
---
# PUCK Unification
## PUCK Entity Subtype Discrimination
To allow unification of sibling types under a single data table (entity types that derive from the same base, share the same table and can exist alongside each other, but are ultimately different types), PUCK should allow adjacent notations for them via a difference in their discriminators.
For example if type A and B are siblings, they can have a PUCK of `w{I:4}` and `x{I:4}` respectively, and PUCK should be able to detect which PUCK belongs to which.
To prevent confusion for the PUCK engine, we're going to explicitly mark the discrimination boundary in the notation with parentheses, as `(w){I:4}` and `(x){I:4}`.
## Review and Model Update
To unify the current PUCK-driven entities, we'll go over them again (this table might include changes to the notations which must be enforced as a part of PEP082):

| Entity        | PUCK Notation               | Tokens                                 |
| ------------- | --------------------------- | -------------------------------------- |
| Directive     | `{S:6}`                     | -                                      |
| Objective     | `j{S:8}`                    | -                                      |
| Onrush Sprint | `x{I:4:100}`                | -                                      |
| Polaris Cycle | `{D:p}`                     | -                                      |
| Saga Lorepage | `Era{?}/Cha{?}/Act{?}/p{?}` | Era, Chapter (`Cha`), Act, Phase (`p`) |
# Enforcement
## Discrimination
PUCK shouldn't allow discriminated sibling types that don't have a discrimination boundary.
## Uniqueness
When PUCK is precompiling models, it should check all PUCK formats for a uniqueness criteria, in a way that any generated PUCK can be resolved into its correct entity.
To do this, a variety of factors should be checked, including discriminators, numerator lengths and required nesting.
# Reverse Resolution
With uniqueness ensured, we should now have a central logic and API for resolving any PUCK to its respective entity.
## API
The endpoint `/system/resolve/{id}` should be used to allow entity resolution via PUCK, returning an `EntityExistence` object that yields:
- Entity Type
- The Entity Itself
- Associated Note (if exists)

# Patches
## Patch082.1 - Revert Discrimination Boundary & Fix Uniqueness Logic
- Removed discrimination boundary. Sibling types now rely only on uniqueness.
- Fixed PUCK uniqueness criteria.