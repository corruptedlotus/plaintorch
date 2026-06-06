---
status: implemented
assignee: Copilot 🤖
---
# Pleiadean Date-stamp
The current PUCK date-stamp (notated as `{D}`) uses Gregorian calendar with format `yyyyMMdd` to generate a unique PUCK numerator. The Pleiadean Date-stamp (to be notated as `{D:p}`) will be generated based on the Pleiadean calendar with the format `yyyMdd`.
This would mean that the date numerator now accepts an optional parameter, with the default being `{D:g}` (Gregorian) which is equivalent to `{D}`.
