# Playtest Guide

Three-phase playtest process: **Prep → Execute → Report**. Every test must have log evidence (closed-loop verification).

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

Before touching any MCP tool, write test cases as `log` entries and create a task list:

```
Test Case 1: Movement — walk 200x200 rectangle via macro
  Steps: execute_macro(rectangle_walk) → get_game_state → verify position ≈ (0,0)
  Evidence: get_logs has macro completion entry

Test Case 2: State queries — read game state, UI, metrics
  Steps: get_game_state + get_ui_layout + get_metrics
  Evidence: returns structured data with expected fields

Test Case 3: UI interaction — open inventory, read items, close
  Steps: click_element(背包按钮) → get_ui_layout(path=InvGrid) → click_element(CloseBtn)
  Evidence: get_logs has "背包按钮打开" + "背包X按钮关闭"

Test Case 4: Drag-and-drop — swap inventory items
  Steps: execute_macro(drag from cell A to cell B) → get_ui_layout → verify items swapped
  Evidence: get_logs has "拖拽开始" + "拖拽完成"

Test Case 5: Button actions — heal button
  Steps: click_element(治疗按钮)
  Evidence: get_logs has "恢复了50点生命值!"
```

## Phase 2: Execute — One Test at a Time

For each test case:
1. `log("playtest", "开始测试: ...")` — write intent BEFORE operating
2. Execute the MCP tool(s)
3. `get_logs()` — verify game-side logged the action (closed-loop)
4. `get_game_state()` or `get_ui_layout()` — verify state changed as expected
5. `log("playtest", "测试结果: ...")` — write outcome AFTER verifying

**NEVER batch multiple untested operations together.** Test one thing, verify, then move on.

### Log Closed-Loop Rule

Every significant game action MUST write to at least one MCP log tier (AI Log preferred). This ensures the agent can verify operations by reading logs — not just trusting `ok: true` returns.

**Minimum events that MUST log:**
- UI open/close (panels, dialogs, menus)
- Button clicks that trigger game actions (heal, buy, craft)
- Drag-and-drop start/complete
- Scene transitions
- Player state changes (HP change, level up, death)
- Error/failure conditions

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

| # | Test | Tool(s) | Result | Evidence (log entries) |
|---|------|---------|--------|----------------------|
| 1 | Rectangle walk | execute_macro + get_game_state | PASS | macro done in 4.8s, pos≈(0,0) |
| 2 | State queries | get_game_state + get_ui_layout + get_metrics | PASS | 34 tools, 4 metrics, 24 grid cells |
| 3 | Open/close inventory | click_element | PASS | log: "背包按钮打开" + "背包X按钮关闭" |
| 4 | Drag swap | execute_macro(drag) | PASS | log: "拖拽开始: 铁剑" + "拖拽完成" |
| 5 | Heal button | click_element | PASS | log: "恢复了50点生命值!" |

### Log Trail (get_logs)
[paste full log output here]

### Issues Found
- [any bugs, missing logs, unexpected behavior]

### Coverage
- Tools tested: [N]/34
- Log closed-loop: [Y/N for each test]
```

## Example: Complete Playtest Session

### Test 1: Open Inventory
```
Agent: click_element(name="背包按钮")      → ok: true
Agent: get_logs(limit=3)                   → {category:"ui", message:"背包按钮打开"}
Result: PASS — log closed-loop verified
```

### Test 2: Drag Item
```
Agent: log(category="playtest", message="拖拽测试: 铁剑(0,0) → 空格(0,3)")
Agent: execute_macro(steps=[{drag from (233,136) to (233,388)}]) → macro_001 completed
Agent: get_logs(limit=5) → "拖拽开始: 铁剑 (0,0)" + "拖拽完成: ↔ 铁剑"
Agent: get_ui_layout(path=InvGrid) → cell (0,0) empty, cell (0,3) has 铁剑
Result: PASS — both log evidence AND data verification
```

### Test 3: Close + Heal
```
Agent: click_element(name="CloseBtn")      → ok: true
Agent: click_element(name="治疗按钮")       → ok: true
Agent: get_logs(limit=10)                  → "背包X按钮关闭" + "恢复了50点生命值!"
Result: PASS — both actions logged by game code
```
