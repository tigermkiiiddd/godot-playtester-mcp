# Playtest Guide

Three-phase playtest process: **Prep → Execute → Report**. Every test must have log evidence (closed-loop verification). **Boundary/edge case testing is MANDATORY, not optional.**

## Concurrency Rule

MCP tool calls are split into two categories with different concurrency rules:

### Read-type tools — CAN be called concurrently

These only read game state, never mutate it. Safe to call multiple in parallel:

| Tool | What it reads |
|------|---------------|
| `get_game_info` | FPS, window size, engine version |
| `get_game_state` | World objects, positions, distances |
| `get_ui_layout` | UI tree structure |
| `get_scene_tree` | Full scene hierarchy |
| `get_node_properties` | Node property values |
| `get_metrics` | Registered metric values |
| `get_logs` | AI log ring buffer |
| `get_debug_logs` | Debug log ring buffer |
| `get_file_logs` | File log entries |
| `get_file_log_summary` | File log statistics |
| `screenshot` | Current frame capture |
| `get_focused_element` | Focused UI control |
| `get_macro_status` | Macro execution progress |
| `list_macros` | Macro list |
| `get_test_results` | Background test results |

**Usage:** Batch multiple reads in a single tool call block to reduce round-trips:
```
// All three fire in parallel — one round-trip instead of three
get_game_info()
get_metrics()
get_ui_layout()
```

### Control-type tools — MUST be called sequentially, NEVER concurrently

These mutate game state, simulate input, or trigger side effects. Calling concurrently causes race conditions, dropped inputs, or undefined behavior:

| Tool | What it controls |
|------|-----------------|
| `click_element` | Click UI button/element |
| `click_mouse` | Mouse button at coordinates |
| `press_key` | Keyboard key press/release |
| `move_mouse` | Move cursor position |
| `scroll_mouse` | Mouse wheel |
| `simulate_input` | Mapped input action |
| `type_text` | Text input into control |
| `select_option` | Select dropdown/list item |
| `execute_macro` | Start scripted input sequence |
| `cancel_macro` | Cancel running macro |
| `drag` | Drag from A to B |
| `double_click` | Double-click at position |
| `hover` | Move mouse + hover notification |
| `set_node_property` | Modify node property |
| `call_node_method` | Invoke node method |
| `log` | Write AI log entry |
| `clear_logs` | Clear log buffers |
| `register_metric` | Register new metric |
| `start_test` | Start background test |

**Rule:** After each control action, wait for the game to process it before sending the next. The safe pattern is:
```
1. control action (click, press, etc.)
2. one or more reads to verify (can be concurrent reads)
3. next control action
```

## Phase 1: Prep — Write Test Cases

**IRON LAW: No test execution without written test cases first. No exceptions.**

Before touching any MCP tool, write complete test cases and create a task list. Every test case MUST include both **happy-path** and **boundary** scenarios.

### Step 1: Classify Each Feature

Before writing test cases, classify each feature to determine how many boundary cases you need:

| Feature Type | Signs | Min-Max Boundary | MUST Include |
|-------------|-------|---------------|--------------|
| **Simple** | Single button, single action, no panels | 1-2 | `Repeat` or `ResourceEdge` |
| **Multi-step** | Step 1 → Step 2 → Step 3 → Result | 3-5 | `Cancel` at EACH step + one `Recovery` |
| **State-heavy** | Opens/closes panels, overlays, changes mode | 2-3 | `Cancel` + `Recovery` |

**How many test cases total?** One test case per logical feature. A "film shooting flow" is ONE test case (multi-step). A "heal button" is ONE test case (simple). Aim for **3-8 test cases** per playtest session.

### Step 2: Copy and Fill the Template

**Read `docs/test-case-template.md` for the three copy-pasteable templates with naming conventions and filled examples.** Summary:

| Feature Type | Template | Boundary Limit | Key checks |
|-------------|----------|---------------|------------|
| Simple (single button) | Template A | 1-2 | Repeated action |
| Multi-step (wizard) | Template B | 3-5 | Cancel at EACH step + re-entry |
| State-heavy (panels/overlays) | Template C | 2-3 | State recovery + re-entry |

**Naming**: Test cases use `TC{N}` IDs. Boundaries use `TC{N}-B{M}` with category prefix. See `docs/test-case-template.md` for full convention.

Copy the template → fill it in → move to next feature. One template per feature. **Do NOT exceed the boundary limit.**

---

### Step 3: Boundary Case Cheat Sheet

For each test case, pick the most relevant boundary categories. You do NOT need to test all categories — pick based on feature type.

**Core categories (player-initiated, synchronous):**

| Category | What to test | Use when |
|----------|-------------|----------|
| **Cancel** | Cancel mid-flow, close dialog midway | Multi-step flows (Template B) — **mandatory** |
| **Recovery** | After interruption, is UI still usable? Can re-enter? | State-heavy (Template C) and Multi-step (Template B) — **mandatory** |
| **Repeat** | Double-click, rapid open/close, toggle twice | Buttons, toggle panels |
| **Empty** | No selection made, zero resources, empty list | Dropdowns, lists, resource-gated actions |
| **OutOfOrder** | Skip steps, wrong sequence | Multi-step flows with prerequisites |
| **ResourceEdge** | Full HP heal, max inventory, zero money | Economy, HP, limited resources |

**Extended categories (environment / timing):**

| Category | What to test | Use when |
|----------|-------------|----------|
| **TimingLock** | Input during animation, transition, cutscene, drag in flight | Feature has animations or transitions — **common in games** |
| **ContextSwitch** | Scene change while dialog open, panel stacking, Escape with no dialog | Game has multiple scenes or panels that can stack |

**Cancel + Recovery are the golden pair.** Most real bugs hide in: player cancels something → UI breaks → game frozen. **TimingLock** catches the second most common game bug: action fires during animation lock window.

### Step 4: Self-Check Before Proceeding

Before moving to Phase 2 (Execute), verify your test cases pass this checklist:

- [ ] Every test case has at least 1 boundary case (zero = STOP, go back and add)
- [ ] Multi-step flows have cancel test at EACH step (not just one step)
- [ ] Every cancel test includes "game NOT frozen" check
- [ ] Every cancel test includes "re-enter flow" check
- [ ] If feature has animations/transitions, at least one `TimingLock` boundary case
- [ ] If game has multiple scenes or panel stacking, consider `ContextSwitch`
- [ ] Test cases are scoped correctly (not too many — aim for 3-8 total per session)
- [ ] You know which MCP tools you'll call (click_element, press_key, etc.)

**RED FLAG — If any checkbox is unchecked, you are NOT ready to test. Fix the test cases first.**

## Phase 2: Execute — One Test at a Time

For each test case (happy path AND boundary cases):

1. `log("playtest", "开始测试: ...")` — write intent BEFORE operating
2. Execute the MCP tool(s)
3. `get_logs()` — verify game-side logged the action (closed-loop)
4. `get_game_state()` or `get_ui_layout()` — verify state changed as expected
5. `log("playtest", "测试结果: ...")` — write outcome AFTER verifying

**NEVER batch multiple untested operations together.** Test one thing, verify, then move on.

### Execution Order

```
For each Test Case:
  1. Run Happy Path → verify → log result
  2. Reset to known state (if needed)
  3. Run Boundary Case B1 → verify → log result
  4. Run Boundary Case B2 → verify → log result
  5. ... more boundary cases
  6. Move to next Test Case
```

**CRITICAL: Boundary cases are NOT optional or "if time permits". They are part of the mandatory test execution.**

### What to Watch For in Boundary Tests

After each boundary test action, specifically verify:

1. **No frozen state** — `get_game_state()` should return responsive data, `get_game_info()` uptime should keep incrementing
2. **Correct UI state** — `get_ui_layout()` should show expected panels (visible/hidden), no orphaned overlays
3. **Clean log trail** — `get_logs()` should show the cancellation/recovery was handled, no error spam
4. **Replayability** — After a cancel/error, can the player immediately redo the action? Test re-entry.

### State Recovery Verification Pattern

For every cancel/abort boundary test:
```
1. Perform cancel action (click cancel, press Escape, etc.)
2. get_game_state() — is game responsive?
3. get_ui_layout() — are panels in correct visibility state?
4. Re-attempt the original action — does it work cleanly?
5. get_logs() — is there a clean log trail?
```

If step 2, 3, 4, or 5 fails → **BUG FOUND. Log it immediately.**

### Log Closed-Loop Rule

Every significant game action MUST write to at least one MCP log tier (AI Log preferred). This ensures the agent can verify operations by reading logs — not just trusting `ok: true` returns.

**Minimum events that MUST log:**
- UI open/close (panels, dialogs, menus)
- Button clicks that trigger game actions (heal, buy, craft)
- Drag-and-drop start/complete
- Scene transitions
- Player state changes (HP change, level up, death)
- Error/failure conditions
- **Cancellation/abort of multi-step flows** (CRITICAL — most bugs hide here)

**Verification pattern** (agent-side):
```
1. Agent sends command (click_element, execute_macro, etc.)
2. Agent reads get_logs() to verify game logged the action
3. If log entry missing → operation may not have reached game code
```

## Phase 3: Report — Evidence-Based Summary

After all tests, read logs and compile:

```
## Playtest Report — [Date]

### Test Results

| # | Test | Type | Tool(s) | Result | Evidence (log entries) |
|---|------|------|---------|--------|----------------------|
| 1 | Rectangle walk | Happy | execute_macro + get_game_state | PASS | macro done in 4.8s, pos≈(0,0) |
| 1b | Walk during dialog | Boundary | press_key during modal | PASS | walk blocked, dialog intact |
| 2 | State queries | Happy | get_game_state + get_ui_layout + get_metrics | PASS | 34 tools, 4 metrics, 24 grid cells |
| 3 | Open/close inventory | Happy | click_element | PASS | log: "背包按钮打开" + "背包X按钮关闭" |
| 3b | Open while shop open | Boundary | click_element during shop | PASS | shop closes first |
| 3c | Close via Escape | Boundary | press_key(Escape) | PASS | inventory hidden |
| 4 | Drag swap | Happy | execute_macro(drag) | PASS | log: "拖拽开始: 铁剑" + "拖拽完成" |
| 5 | Heal button | Happy | click_element | PASS | log: "恢复了50点生命值!" |

### Bugs Found
| # | Description | Boundary Test That Caught It | Severity |
|---|-------------|------------------------------|----------|
| 1 | Game freezes after canceling zone selection in film shooting | TC1-B1 | Critical |

### Coverage
- Test cases: [N] happy paths + [M] boundary cases
- Tools tested: [N]/34
- Boundary coverage: [N] cancel, [N] state recovery, [N] repeated action, etc.
- Log closed-loop: [Y/N for each test]
```

## Example: Complete Playtest Session

### Test 1: Open Inventory (Happy Path)
```
Agent: log(category="playtest", message="TC2 开始: 库存打开/关闭")
Agent: click_element(name="InvBtn")              → ok: true
Agent: get_logs(limit=3)                         → {category:"ui", message:"inventory_open"}
Result: PASS — log closed-loop verified
```

### Test 1b: Open Inventory via Escape (Boundary)
```
Agent: log(category="playtest", message="TC2-B1: 关闭库存通过Escape键")
Agent: click_element(name="InvBtn")              → open inventory
Agent: press_key(key="Escape")                   → close via Escape
Agent: get_ui_layout(path="HUDLayer")            → InvPanel visible=false
Agent: get_game_state()                          → game responsive
Result: PASS — clean close, game still responsive
```

### Test 2: Drag Item (Happy Path)
```
Agent: log(category="playtest", message="TC4 开始: 拖拽铁剑")
Agent: click_element(name="InvBtn")              → open inventory
Agent: execute_macro(steps=[{drag from (233,136) to (233,388)}]) → macro_001 completed
Agent: get_logs(limit=5) → "拖拽开始: 铁剑 (0,0)" + "拖拽完成: ↔ 铁剑"
Agent: get_ui_layout(path=InvGrid) → cell (0,0) empty, cell (0,3) has 铁剑
Result: PASS — both log evidence AND data verification
```

### Test 2b: Drag Cancel (Boundary)
```
Agent: log(category="playtest", message="TC4-B1: 取消拖拽中途")
Agent: click_element(name="InvBtn")              → open inventory
Agent: execute_macro(steps=[
  {action:"drag", from_x:233, from_y:136, to_x:400, to_y:300, duration:0.3},
  {action:"tap_key", keys:"Escape"}
]) → macro executed
Agent: get_ui_layout(path=InvGrid)               → items unchanged (no partial swap)
Agent: get_game_state()                          → game responsive
Result: PASS — drag cancelled cleanly, no item corruption
```
