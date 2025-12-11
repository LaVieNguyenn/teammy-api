# Board / Backlog / Milestone Relationships

This note explains how the Teammy API fits backlog planning, milestones, and the Kanban board together. The models in `ProjectTrackingController` and `BoardController` intentionally share identifiers so work can move from high-level plans into day-to-day execution without manual duplication.

## 1. Backlog as the single source of work
- `BacklogItemVm` captures every planned deliverable (scope, priority, owner, due date, and optional story points).
- Backlog endpoints (`/tracking/backlog`) are the only place new scope is created; items stay here through discovery, estimation, and grooming.
- When a backlog item is archived it is also considered done for reporting and is hidden from milestone rollups.

## 2. Promoting backlog items into Kanban tasks
- The `PromoteBacklogItemRequest` endpoint converts a backlog item into an executable Kanban task. Under the hood `ProjectTrackingService` calls `KanbanRepository.CreateTaskAsync` with the backlog item’s title/description/priority.
- The created task keeps a `backlog_item_id` reference. This makes the backlog row the authoritative record while the board reflects its operational state.
- Task updates propagate back through `KanbanRepository` (`UpdateTaskAsync`, `MoveTaskAsync`, `DeleteTaskAsync`) to keep the linked backlog item in sync (column info, completion status, and due dates).

## 3. Columns reflect execution stages
- Each board column is tied to a group board (`columns.board_id`). Moving a task updates its `sort_order` and column metadata, while linked backlog items inherit column completion flags.
- Because `ProjectTrackingService` always calls `kanban.EnsureBoardForGroupAsync`, a board is auto-created the first time any backlog item is promoted.

## 4. Milestones group backlog items
- Milestones live entirely in the tracking API (`/tracking/milestones`). Assigning items records the relationship without duplicating tasks.
- Progress metrics (`MilestoneVm.TotalItems`, `CompletedItems`, `CompletionPercent`) rely on backlog item statuses, so finishing a task (or archiving its backlog item) immediately lifts milestone progress.
- Removing an item from a milestone never deletes the backlog or task; it only breaks the association.

## 5. Reporting ties everything together
- `ProjectReportVm` aggregates backlog counts, board column throughput, and milestone health into one payload for dashboards.
- Because both tasks and milestones derive from the backlog list, the report can always reconcile: every live task maps to a backlog item, and every milestone displays the same titles/team owners.

## 6. Collaboration artifacts hang off tasks
- Comments and shared files are scoped to Kanban tasks (`task_id` foreign key). This keeps execution history close to the daily board while backlog/milestone records stay light-weight.
- Since tasks point back to backlog items, a consumer can retrieve the backlog item first, then pivot to its task to read comments/files or vice-versa using `LinkedTaskId`.

In short: backlog items describe *what* to build, milestones define *when* those items should land, and Kanban tasks capture *how* the team executes. The API keeps them connected through shared IDs so progress flows automatically from board activity up to project reports without extra bookkeeping.