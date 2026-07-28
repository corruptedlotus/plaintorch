# Issues: Watcher
- [x] Frontmatter serialisation is strict and destroys custom properties on files.
- [x] Frontmatter validity check should only run against properties marked with `[MarkdownField]`.
- [x] `Objective.Directive` should be auto-included.
- [x] Onrush and Polaris default naming should be "Polaris/Onrush `{d} of {MMMM} {y}`"
- [x] Objectives under non-default Directive root are not detected.
- [x] Markdown deserialiser doesn't parse Date correctly.
- [ ] Invalid PUCK-ed files in some folders are turned into zombie objectives. 
# Tasks: Watcher
- [ ] Revamp the watcher issue system and its API.
# Issues: UI
- [x] `EnityItem` has bloated layouting in modals.
# Tasks: UI
- [x] Polaris Briefing Card & Executive Item
- [x] Add to Polaris
- [x] Add to Polaris (Briefing)
- [x] Global Editability Component
- [x] Briefing cards: Link to entity
- [x] Directives Banner
- [x] Onrush Banner
- [x] Polaris Banner
- [ ] Entity Item context menu
- [ ] Status bar
- [ ] Add constraints to editable plaintext
## Extras
- [x] Use SignalR to update briefing — shipped as server-sent events instead ([[PEP106 - Frontend Repository System]]). One-directional was all it needed, since writes already go over REST, and Kestrel is HTTP/1.1-only on both listeners, which makes a websocket upgrade over the socket the fragile path.
- [ ] Make the quantum time/starfire editor
- [x] Add a global loading mechanism and unify entity components. — entity components are unified: they declare a type name and the base resolves and observes for them. Loading/error state is exposed on the reference controllers but not yet rendered; see `core/.DISCUSSION.md`.