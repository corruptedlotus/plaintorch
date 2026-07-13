---
status: idea
assignee: Soraya 🧙‍♀️
---
# Executive Orders
Each Onrush Sprint can have a collection of Executive Orders. These orders are special constraints or directions that shape the way an onrush is moved through.
## Model
The Executive Order needs:
- Title
- Summary
- Effective From
- Effective Until
### PUCK
Executive Orders use the PUCK notation `x{?}-o{I:2}`, where the first input is the numeric part of its owning Onrush ID (`Onrush x0180 -> E.O. x180-o01`).
### Storage Policy
Executive Orders are stored in `Synced` mode, as single files, within their parent's `ExecutiveOrders` partition folder, with an index stored PUCK.
