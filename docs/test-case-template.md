# Test Case Templates

Copy the appropriate template, fill it in, one per feature. Do NOT execute tests until all test cases are written.

## Naming Convention

### Test Case ID

| Format | Meaning | Example |
|--------|---------|---------|
| `TC{N}` | Test case N (sequential, global per session) | `TC1`, `TC2`, `TC3` |
| `TC{N}-HP` | Happy path of TC N (implicit, written inline) | — |
| `TC{N}-B{M}` | Boundary case M of TC N (sequential within TC) | `TC1-B1`, `TC1-B2` |

### Field Names (use these EXACT field names, do not invent new ones)

| Field | Required | Meaning |
|-------|----------|---------|
| `Type` | Yes | `Simple` / `Multi-step` / `State-heavy` |
| `Feature` | Yes | 1-sentence description of what is being tested |
| `Flow` | Multi-step only | The step chain: `[Step1] → [Step2] → [Result]` |
| `States` | State-heavy only | What toggles: `[idle] ↔ [active state]` |
| `UI` | Yes | Comma-separated list of MCP node names involved |
| `Steps` | Yes | Numbered list of MCP tool calls |
| `Expected` | HP only | What should change after the steps |
| `Evidence` | HP only | Which `get_logs` entry proves success |
| `Check` | Boundary only | What to verify (game NOT frozen + UI state + re-entry) |

### Boundary Case Naming

Boundary case titles must follow this format:
```
TC{N}-B{M}: [Category] — [short description]
```

**Category** must be one of:

### Core Categories (player-initiated, synchronous)
| Category | When to use | Bug it catches |
|----------|-------------|---------------|
| `Cancel` | Cancel/abort mid-flow at any step | Panel/orphan state after cancel → game frozen |
| `Recovery` | Re-enter flow after cancel, state restoration | Stale data from cancelled attempt leaking into retry |
| `Repeat` | Double-click, rapid open/close, toggle twice | Duplicate action (charged twice, two items created) |
| `Empty` | No selection made, empty list, zero resources | Crash on null/empty, confirm button not gated |
| `OutOfOrder` | Skip steps, wrong sequence | Hidden prerequisites bypassed, partial state |
| `ResourceEdge` | Full HP, max inventory, zero money | Integer overflow, cap bypass, negative values |

### Extended Categories (environment / timing)
| Category | When to use | Bug it catches |
|----------|-------------|---------------|
| `TimingLock` | Input during animation, transition, cutscene, drag in flight | Action fires during lock window → duplicated or corrupted state |
| `ContextSwitch` | Scene change while dialog open, panel stacking, viewport change | Dangling references, stacked modals, orphan overlays after scene load |

**When to use extended categories:**
- `TimingLock`: Use when feature has animations, transitions, or drag operations (most games)
- `ContextSwitch`: Use when game has multiple scenes, panels that can stack, or timed events

**Examples:**
```
TC1-B1: Cancel — cancel at genre selection step
TC1-B2: Cancel — cancel at zone selection (deepest step)
TC1-B3: Recovery — re-enter flow after cancel
TC1-B4: Empty — confirm without selecting genre
TC1-B5: TimingLock — click confirm during panel slide-in animation
TC2-B1: Repeat — double-click heal button rapidly
TC3-B1: ResourceEdge — heal when HP is already full
TC4-B1: ContextSwitch — scene transition while inventory panel open
```

### Boundary Case Limits

**Do NOT exceed these limits.** More is not better — focused beats exhaustive.

| Feature Type | Min | Max | Must include |
|-------------|-----|-----|-------------|
| Simple | 1 | 2 | At least one: `Repeat` or `ResourceEdge` |
| Multi-step | 3 | 6 | `Cancel` at each step + one `Recovery` + optional `TimingLock`/`Empty` |
| State-heavy | 2 | 4 | One `Cancel` + one `Recovery` + optional `ContextSwitch` |

If you find yourself writing more boundary cases than the max, you are testing too granularly. Merge or remove.

---

## Template A — Simple Feature

Use for: single button, single action, no panel state changes (heal, attack, buy item)

```markdown
TC{N}: {Feature Name}
  Type: Simple
  Feature: {1-sentence description}
  UI: {Node1, Node2, Node3}

  HP:
    Steps:
      1. {tool call}
      2. {tool call}
    Expected: {what changes}
    Evidence: {get_logs entry}

  TC{N}-B1: {Category} — {short description}
    Steps:
      1. {tool call}
      2. {tool call}
    Check: {what get_game_state/get_ui_layout should show}
```

### Filled Example

```markdown
TC1: Heal Button
  Type: Simple
  Feature: Player clicks heal button, gains 50 HP, button enters 10s cooldown
  UI: HealBtn, HPBar, ToastLabel

  HP:
    Steps:
      1. get_game_state() — record current HP
      2. click_element(name="HealBtn")
      3. get_logs(limit=3) — verify "恢复了50点生命值"
      4. get_game_state() — verify HP increased by 50
    Expected: HP +50, toast appears, button disabled for 10s
    Evidence: get_logs shows heal entry

  TC1-B1: Repeat — click heal during cooldown
    Steps:
      1. click_element(name="HealBtn") — first heal
      2. click_element(name="HealBtn") — second click during cooldown
      3. get_game_state() — verify HP only increased once
      4. get_logs(limit=5) — verify only one heal log entry
    Check: HP increased exactly 50 (not 100), only one heal log entry, HealBtn disabled
```

---

## Template B — Multi-Step Flow

Use for: wizard, chain, selection sequence (film shooting → select genre → select actor → select zone → confirm)

```markdown
TC{N}: {Flow Name}
  Type: Multi-step
  Feature: {1-sentence description}
  Flow: {Step1} → {Step2} → {Step3} → {Result}
  UI: {Node1, Node2, Node3}

  HP:
    Steps:
      1. {enter flow}
      2. {complete Step1}
      3. {complete Step2}
      4. {complete Step3}
      5. {confirm/finish}
    Expected: {final result}
    Evidence: {get_logs entries}

  TC{N}-B1: Cancel — cancel at {Step1}
    Steps:
      1. {reach Step1}
      2. {cancel}
    Check: game NOT frozen + UI correct state + re-enter works

  TC{N}-B2: Cancel — cancel at {deepest step}
    Steps:
      1. {reach deepest step}
      2. {cancel}
    Check: game NOT frozen + no orphaned overlays + re-enter works

  TC{N}-B3: Recovery — re-enter flow after cancel
    Steps:
      1. {cancel midway in B1 or B2}
      2. {redo full flow from start}
    Check: fresh state, no leftover data, flow completes successfully
```

### Filled Example

```markdown
TC2: Film Shooting Flow
  Type: Multi-step
  Feature: Player selects genre, picks actors, selects a zone, begins filming
  Flow: OpenPanel → SelectGenre → SelectActor → SelectZone → StartFilming
  UI: FilmBtn, GenreOptionBtn, ActorList, ConfirmBtn, CancelBtn, ZoneTiles, StartFilmingBtn

  HP:
    Steps:
      1. click_element(name="FilmBtn") — open filming panel
      2. select_option(name="GenreOptionBtn", index=0) — select Action
      3. click_element on ActorList item — select actor
      4. click_element(name="ConfirmBtn") — enter zone mode
      5. click_element(name="ZoneTile_2_3") — select zone
      6. click_element(name="StartFilmingBtn") — start filming
      7. get_logs(limit=5) — verify "filming_started" with all params
    Expected: Filming begins with genre=Action, selected actor, selected zone
    Evidence: get_logs shows "filming_started" with all three parameters

  TC2-B1: Cancel — cancel at genre selection (first step)
    Steps:
      1. click_element(name="FilmBtn") — open panel
      2. click_element(name="CancelBtn") — cancel immediately
      3. get_ui_layout() — verify no filming panels visible
      4. get_game_state() — verify game NOT frozen
    Check: filming panel closed, no overlays, game responsive, can re-open

  TC2-B2: Cancel — cancel at zone selection (deepest step)
    Steps:
      1. FilmBtn → select genre → select actor → ConfirmBtn — reach zone mode
      2. click_element(name="CancelBtn") — cancel zone selection
      3. get_ui_layout() — verify filmingPanel visible, no zone overlay
      4. get_game_state() — verify game NOT frozen
    Check: zone overlay removed, filming panel visible, game NOT frozen

  TC2-B3: Recovery — re-enter flow after zone cancel
    Steps:
      1. From B2 state (just cancelled zone selection)
      2. click_element(name="FilmBtn") — re-enter flow
      3. Select Comedy genre → select different actor → ConfirmBtn
      4. select_option(name="ZoneTile_1_1") → StartFilmingBtn
      5. get_logs(limit=5) — verify genre=Comedy (NOT old Action)
    Check: fresh state, no leftover data from first attempt, filming starts correctly
```

---

## Template C — State-Heavy Feature

Use for: panel open/close, overlay toggle, mode switch (inventory, shop, settings panel)

```markdown
TC{N}: {Feature Name}
  Type: State-heavy
  Feature: {1-sentence description}
  States: {idle state} ↔ {active state: panels/overlays that toggle}
  UI: {Node1, Node2}

  HP:
    Steps:
      1. {enter state — e.g. click_element(OpenBtn)}
      2. get_ui_layout() — verify panel visible
      3. {do action inside state}
      4. {exit state — click_element(CloseBtn)}
    Expected: {state entered → action → state exited cleanly}
    Evidence: {get_logs entries for open + action + close}

  TC{N}-B1: Cancel — interrupt mid-operation
    Steps:
      1. {enter state}
      2. {start action}
      3. press_key(key="Escape") or click_element(name="CancelBtn")
    Check: UI panels correct visibility + game NOT frozen

  TC{N}-B2: Recovery — re-enter after interrupt
    Steps:
      1. {from B1 — after interrupt}
      2. {immediately re-enter state}
      3. {complete action}
    Check: clean re-entry, no stale data, action succeeds
```

### Filled Example

```markdown
TC3: Inventory Panel
  Type: State-heavy
  Feature: Open inventory panel, view items, close panel
  States: idle (no inventory) ↔ active (InvPanel visible, dimOverlay visible)
  UI: InvBtn, InvPanel, CloseBtn, InvGrid

  HP:
    Steps:
      1. click_element(name="InvBtn") — open inventory
      2. get_ui_layout(path="InvGrid") — verify items visible
      3. click_element(name="CloseBtn") — close inventory
      4. get_logs(limit=3) — verify "inventory_open" + "inventory_close"
    Expected: Panel opens, shows items, closes cleanly
    Evidence: get_logs has open + close entries

  TC3-B1: Cancel — close via Escape key
    Steps:
      1. click_element(name="InvBtn") — open inventory
      2. press_key(key="Escape") — close via Escape
      3. get_ui_layout() — verify InvPanel hidden
      4. get_game_state() — verify game NOT frozen
    Check: InvPanel hidden, dimOverlay gone, game responsive

  TC3-B2: Recovery — reopen after Escape close
    Steps:
      1. From B1 state (just closed via Escape)
      2. click_element(name="InvBtn") — reopen
      3. get_ui_layout(path="InvGrid") — verify items still present
    Check: inventory reopens cleanly, items intact, no state corruption
```
