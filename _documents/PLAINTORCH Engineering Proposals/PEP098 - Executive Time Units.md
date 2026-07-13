---
status: implemented
assignee: Copilot 🤖
---
# Working Time Unit
The time unit specification for work items allow a minute-based stamp, which is translated to timespan/time-only in the application layer and to hours + fraction in the UI. 
# Executives: Time Allocations
Executives have 3 optional time allocations:
- ### Estimation
  The main allocation parameter. This parameter acts as a "progress" for the task. It's up to the user to track this accurately or treat it as a dummy progress holder. In each Polaris Cycle, the sum of the estimations indicate the total "workload" of the day.
- ### Min & Max
  These are additional optional parameters that can be set as time allocations. they are pretty self explanatory. If either is set before estimation is specified, estimation will take their value too. Estimation cannot be more than max and less than min, if it is when min or max are set, it clamps it.