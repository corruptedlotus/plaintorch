Here is a high-level, algorithmic breakdown of how the **OrbitEngine** currently operates.

At its core, the engine is a **Top-Down Backtracking Jumper**. Instead of checking every single second of the calendar to see if it matches your rules, it aggressively attempts to jump the calendar forward to the next valid mathematical state. If a constraint fails, it backtracks up the tree, rolls over the parent, and tries again.

Here is the step-by-step algorithm of how it processes time.

### Phase 1: Initialization (The Setup)

When you first instantiate `new OrbitEngine()`, three things happen before the clock even ticks:

1. **AST Normalization:** The engine inspects the root of your AST. If it doesn't start with a Year (`y`), it silently wraps it in one (e.g., `M` becomes `y[M]`). This guarantees that when a unit exhausts its limits (like Month 12), there is always a parent node above it to catch the failure and keep the timeline rolling infinitely.
    
2. **Granularity Calculation:** It scans the entire tree to find the deepest (smallest) time unit specified (e.g., `h` for hour). This becomes the **Global Granularity**.
    
3. **Cursor Snapping:** It takes your `anchorDate` and snaps it cleanly to the beginning of the Global Granularity. (e.g., If the granularity is Days, it resets the anchor's hours, minutes, and seconds to `00:00:00`).
    

### Phase 2: The `.next()` Trigger

When the consuming application calls `.next()`, the engine begins a search loop.

1. It makes a clone of the current `cursor`.
    
2. It passes this cursor to the **Constraint Solver** (`solveNext`).
    
3. If the solver finds a valid match, it saves that timestamp, **advances the internal cursor by exactly 1 tick of the Global Granularity**, and returns the result. (Advancing the cursor ensures the next call doesn't just return the exact same timestamp).
    
4. If the solver fails, it advances the cursor by 1 tick and tries again (up to a safety limit to prevent infinite calendar lockups).
    

### Phase 3: The Constraint Solver (Top-Down)

This is the heart of the engine. When the AST is passed to the solver, it evaluates the rules **outside-in** (Largest unit to smallest).

For every Time Unit Node (e.g., Month `M`), it executes a loop:

**Step A: Alignment (`alignUnit`)**

The engine looks at the current unit (e.g., Month) and asks: _"Is the cursor currently sitting on a valid value for this unit?"_ It checks three scenarios mathematically:

- **Whitelists (`{5}`):** Is it exactly the 5th?
    
- **Local Offsets (`{5}%3`):** Is it the 5th, 8th, or 11th of _this parent block_?
    
- **Global Intervals (`%3`):** Measuring absolute continuous time from the `anchorDate`, is this a valid multiple of 3?
    

If the current cursor value is invalid, the engine **mathematically calculates the next valid value and jumps the cursor forward instantly**.

**Step B: The Drill Down**

Once the parent is aligned (e.g., we found a valid Month), the engine looks to see if there is a child node (e.g., Day `d`).

- If there is no child, the solver returns `true`. The node is fully matched.
    
- If there is a child, it passes the cursor down and asks the child to align itself.
    

**Step C: The Try-and-Fail Backtrack**

This is where the magic happens. What if the parent aligns perfectly (e.g., we jumped to February), but when the child tries to align (e.g., `d{30}` - the 30th day), it realizes that value does not exist _inside_ February?

1. The child constraint fails and returns `false`.
    
2. The failure bubbles back up to the parent (Month).
    
3. The parent says, _"My child couldn't find a valid time inside me."_
    
4. **The Roll-Over:** The parent explicitly steps itself forward by 1 tick (February becomes March), **resets the child to its minimum value** (Day 1), and restarts the loop at Step A.
    

### Phase 4: Set Operations (Branching)

If the solver encounters a Set Operator (like Union `+` or Intersection `&`), it temporarily clones the cursor and acts like a fork in the road:

- **Union (`+`):** It sends one cursor down the Left tree, and one down the Right tree. Whichever tree returns a valid timestamp _chronologically sooner_ is the winner, and the engine adopts that cursor.
    
- **Intersection (`&`):** It solves the Left tree to find a valid date. It then hands that _exact_ date to the Right tree and asks, _"Does this specific timestamp satisfy your rules too?"_ If yes, it's a match. If no, it steps the Left tree forward and tries again.
    

### Summary Example

If you ask for `M[w{1}[d{1}]]` (The 1st Monday of every Month):

1. **Year:** Aligns to current Year.
    
2. **Month:** Aligns to January.
    
3. **Week:** Aligns to Week 1 of January.
    
4. **Day:** Tries to align to Monday. If Jan 1st was a Tuesday, Week 1 doesn't have a Monday. Day returns `false`.
    
5. **Backtrack:** Week receives `false`. It increments to Week 2.
    
6. **Week Check:** Week 2 fails the `{1}` whitelist. Week returns `false`.
    
7. **Backtrack:** Month receives `false`. It increments to February. Week resets to 1, Day resets to 1.
    
8. The engine instantly cascades to the next valid month without having to step through every hour of every day in January.