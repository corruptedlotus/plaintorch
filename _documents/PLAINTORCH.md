> Pleiades Affairs Intelligence Tristate Orchestrator

> [!CAUTION]
> Machines and Language Models are not allowed to modify this file.

## The Pleiadean Calendar
- [x] Add year of blood/sweat/tears.

## Backlog Levels

| Index | Level         | Task            | Notation Type                  |
| ----- | ------------- | --------------- | ------------------------------ |
| 2     | Saga          | Directive       | Directive Files                |
| 1     | Onrush Sprint | Objective       | No File, Summarised in Onrush  |
| 1     | Onrush Sprint | Executive Order | File Recorded as Onrush Assets |
| 0     | Polaris Cycle | Executive       | No File                        |
| 0     | Polaris Cycle | Reflective      | No File                        |

## Celestron
Stored as ledger, when a milestone is reached, older entries are ducked.
## Chronology & Lore

| Level   | Storage                                  | OPCON                                                 |
| ------- | ---------------------------------------- | ----------------------------------------------------- |
| Era     | `/Saga/{Era}/{Era}.md`                   | File-First                                            |
| Chapter | `/Saga/{Era}/{Chapter}/{Chapter}.md`     | File-First                                            |
| Act     | `/Saga/{Era}/{Chapter}/{Act}/{Act}.md`   | File-First                                            |
| Phase   | `/Saga/{Era}/{Chapter}/{Act}/{Phase}.md` | File-First                                            |
| Onrush  | `/Onrush/{Onrush}/{Onrush}.md`           | Auto-Created via Template<br>Summary GenAI during RCF |
| Day     | `/Journal/{StartDate}.md`                | Auto-Created via Template                             |

## Daily Reflective Generation
Should have a list of reflectives to generate from, and certain rules to prevent machinegunning them.

# PUCK
> Pleiades Unified Context Key

In the Pleiades affairs intelligence framework (PLAINFRA) there are multiple data objects belonging to a variety of contexts, each requiring their own identification scheme. However PUCK allows for a unified mapping that makes every object uniquely identifiable.

PUCK splits the ID string into multiple segments:
- **Discriminator:** Some objects require a clarification for the subtype they're referring to, for example lore pages discriminate between "Era", "Chapter", "Act"...
  This will be a simple case-insensitive discriminator for that case, and best kept short.
- **Numerator:** The numeric part of the ID is where the precise identification occurs. This numeric part however, can be anything as long as it's a number.
- **Telescope:**  After each `-`, you can add more enumerators.
- **Filesystem Hierarchy:** Nesting files and folders can also be a part of a PUCK ID, this method will be discussed further.
## Numeration Methods
The numerator could technically be anything, but PUCK establishes a predefined set of methods to enumerate.
### Spiritgem
Well I'm too lazy right now, so the Spiritgem is going to be a by-default 6 digit random number, test it against the database until you're sure it's unique, I suppose.
### Incremental
Starting from a base value k0, it will increment by 1 as new items are added.
### Date-stamp
The date-stamp method simply formats the reference date into `YYYYMMDD` and use it as the numerator. This is only suitable for items that are unique per-day, like Polaris cycles and onrush sprints.
### Manual
Alternatively, a PUCK can be anything numeric, including telescopic nesting.
## Declaration
PUCK IDs work best if models have a declaration for how their IDs are structured.
- **CLR Entities:** A decorator/attribute will help declare their ID format.
- **Dynamic Entities:** A `.puck` file will contain their ID format declaration.
### Declaration Syntax
- `*` denotes a dynamic discriminator.
- `Flag` denotes a static discriminator "Flag".
- `{?}` denotes a custom numerator.
- `{S:k}` denotes a Spiritgem numerator with `k` digits.
- `{I:k:i}` denotes an incremental numerator starting from `i`, padded to `k` digits.
- `{D}` denotes a date-stamp numerator.
- `-` specifies an **optional** telescopic nesting.
- `/` specifies an **optional** filesystem nesting. (See [[#Filesystem Hierarchy]])
- `%` if specified after a nesting notation, allows for the previous segment to repeat in nesting, indefinitely.
- `@` if specified after a nesting notation, allows for the entire notation to repeat in nesting, indefinitely.
- `!` if specified exactly before a nesting notation, forces the nesting. This is not compatible with nesting repetition denotators.

> [!CAUTION]
> Spaces are not allowed in PUCK.
## Filesystem Hierarchy
PUCK can also utilise directories as a part of its identification scope. For example, lore files can define their dynamic entity declaration as follows:
```
*{?}/@
```
or make it more static by declaring this:
```
Era{?}/Chapter{?}/Act{?}/Phase{?}
```
> [!NOTE]
> The filename parser of PUCK will automatically consider two important factors:
> - The ` - ` delimiter to separate the ID part of the file name from its title.
> - The default file sharing the same name with its directory.
> 
> So a file like:
> ```
> Era3 - The Old War/Chapter8 - Re[b]irth of Reality/Act9 - Her Everglow/Act9 - Her Everglow.md
> ```
> will be safely translated to the PUCK:
> ```
> Era3/Chapter8/Act9
> ```


# Models
All models that have associated markdown files will reflect all their properties into their file as markdown attributes (if the type allows), and will try to sync properties and even the name from them, so this is basically a two-way binding. Should any file-to-db sync fail for any property, that specific property should be forcefully rewritten onto the file from the db, without damaging the content of the file. Also when said associated files are deleted, they should delete their respective entry from the database as well, unless said entity is involved in a relation, in which case the file should be recreated.
## Directive
A major operational directive from commander Athena, to be carried out over the course of the story, can have sub-directives. It could also have an alternative lorefile directory if the directive is part of a large project that has its own datafiles. By default however, directives are created each as a folder, with the main markdown file named the same as the folder, inside the directives root folder, and if they have a parent, inside their parent's working directory. If the parent has a 
- [[#PUCK]] `{S:6}`
- Title
- Parent [[#Directive]] (optional)
- Status
- Tags
- Due
- Codename (unique, optional)
- Alternative Lore Directory (optional)
- Start Date (optional)
- End Date (optional)
## Onrush Sprint
A sprint during which select few directives will be focused on and multiple objectives will be met. Onrush sprints can be planned, thus having an empty start date, and when they finish they will record their end date as well.
- [[#PUCK]] `X{D}`
- Title
- Start Date (optional)
- End Date (optional)

> TBD: Executive orders.
## Objective
An objective is a goalpost to be hit while following a directive, or just as a part of the story and everyday life. An objective can be associated to an onrush sprint.
- [[#PUCK]] `J{S:8}`
- Title
- [[#Directive]] (optional)
- [[#Onrush Sprint]] (optional)
- College
- Status
- Celestron Value
## Polaris Cycle
There are two modes for this object: anecdote and forecast.
Anecdote cycles are real cycles with a start time and no forecast data.
Forecast cycles are rough plans for future days, and do not have a start time yet, instead they have a forecast reference to the day they were planned on, and a specification on how further ahead it is planning (+1d, +2d, +3d...).
Forecast cycles will become the template when you reach their day of happening, starting them will remove their forecast data and turn them into an anecdote, finishing them will record their end time.
- [[#PUCK]] `{D}`
- Forecast (optional)
	- Forecast Reference
	- Forecast Target
- Start Time (optional)
- End Time (optional)
## Executive
Defines an instance of work execution per cycle.
- [[#Polaris Cycle]]
- [[#Objective]] (optional)
- Executed
## Reflective
Daily tasks that are either given randomly or part of a specific routine.
- Description
- [[#Polaris Cycle]]
- Executed

> _TBD: Moonlight decrees and their associated reflectives.

## Celestron Transaction
Denotes a gain or loss of Celestron/Starfire.
- GUID Transaction Id
- Date & Time
- Amount
- Source [[#PUCK]] (optional)
- Description (optional)

> [!NOTE]
> Celestron value gained from objectives assigned to the **active** onrush is doubled and should be respectively recorded as double in the transaction, as the transaction doesn't distinguish between Starfire and Celestron. 
# Actions
## Polaris Cycle
- Plan (forecast)
- Begin
- End
- Get (gets the active one by default)
- Plan Executive
	- Standalone (creates an objective)
	- From Directive (creates an objective)
	- From Objective
- Update Executive
- Draw Reflectives
- Update Reflective
## Onrush Sprint
- Plan (not forecast style, just a single temporary Onrush)
- Begin
- End (consign, as onrush sprints end when all their objectives are done)
- Get (gets the active one by default)
- List
- Assign All Onrush-state Objectives to Self
## Directives
- Get, List, Find
- Create
	- Standalone
	- From Parent
- Update
	- Generic Update
	- Workflow Shifting
- Delete
## Objectives
- Get, List, Find
- Create
	- Standalone
	- From Directive
- Update
	- Generic Update
	- Workflow Shifting
	- Add to Onrush
- Delete
## System
- Brief (system date & time, active vault, active onrush, active Polaris cycle, Celestron banked)
# Other Notations
## Action Colleges
- **College of Swords:** Includes physical action, like housework or shopping etc.
- **College of Creation:** Includes technical tasks like development and dev-ops.
- **College of Lore:** Includes logistics, leadership, planning and paperwork.
- **College of Eloquence:** Includes music, poetry and literature.
- **College of Glamour:** Includes visual arts and designing.
# Service and Application Structure
The main engine of PLAINTORCH is going to be a [web]service running in the background and constantly keeping watch over its main vault. This also implies that there can only be one active vault at a time, as there's really no point in having more. This service is responsible for synchronisation, discovery and most importantly, API.
The service is going to expose an API for all of its actions through a UNIX socket file (Windows 11 natively supports that). There will be different forms of user applications that can interface with this API and interact with the core service.
## Planned User Applications
- First and foremost a CLI application to allow for testing throughout the early development of the core and allow maximum intractability with the core through a direct API-CLI translation.
- Later on a private Obsidian plugin should be developed to allow for a limited level of interaction, monitoring and more streamlined synchronisation.
- Ultimately, I will personally develop the SIPA (Sunnyside Interface for Pleiades Affairs), which will open for interop between PLAINTORCH and baseline Sunnyside. 
## PLAINTORCH CLI (Mk1)
The CLI will use a domain system, with each domain having their own commands and options etc.
```
> plaintorch <domain> <command> [--option] ...
```
Or it can be run plainly to clean the terminal, get a briefing and switch to interactive mode, which can be exit via `quit`.
```
> plaintorch
Today is 3rd or Tarākhriz, 4 A.U. (Year of Sweat)
// Active Onrush: [4536] Seventh Chance of Darkness
// Starfire: 23/68 (33%) --- Total Celestron: 554

⚠️ 
```
A default `--help` switch can be specified to show domains, commands and options available for a specific level.
```
shell> plaintorch --help
interactive> --help
Pleaides Affairs Intelligence & Tristate Orchestrator

* vault - Initialise and manage a vault.
* onrush - Plan and manage Onrush sprints.
* polaris - Plan and manage your day.

```
### Vault
```
> vault --help
Initialise and manage a vault.

* init - Initialise the current directory as a PLAINTORCH vault, create the database and base configuration, but not create folder structure or activate it.
* activate - Activate the PLAINTORCH vault in the working directory and restart the core to serve it. Deactivates the currently active vault, if any.
* deactivate - Deactivate the PLAINTORCH vault in the working directory and stop the core.

-d --directory <path> - Specify a folder other than the working directory to operate on.
```
### Onrush
```
> onrush --help
Needs an active vault.

* plan - Create a temporary onrush plan, if there already is a plan, show its assigned objectives and total starfire value.
* start - Start the onrush currently in planning, marking it as active and giving it a real PUCK.
* end - Finish the current onrush.
* add - Add an objective to the onrush being planned or active.
```
### Polaris
```
> polaris --help
Needs an active vault


```