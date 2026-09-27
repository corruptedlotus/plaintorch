![[Overview.base#p3b]]
# Indexed Cache

> **2. Obsidian keeps a persistent metadata cache.** As far as I know (it's an internal API, not documented), it works like this:
> 
> - **Parsing:** each file's frontmatter, links, tags and headings are parsed once, in background workers.
> - **Storage:** the results are kept in the vault's IndexedDB, keyed by a content hash. For each path it stores the modification time, size and hash.
>- **Startup:** it re-parses only files whose time or size changed. A rename doesn't force a re-parse, because the metadata is keyed by content.
>- **Bases:** as far as I know, they query this in-memory cache and update when a file changes; they don't read files.