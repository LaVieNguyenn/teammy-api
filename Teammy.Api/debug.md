{
  "message": "I want to create a task about drawing class diagram and put  it into a new milestone"
}

## Notes (after gateway normalization)

- Draft now returns a fully-shaped `draft.actionPayload` (all expected keys exist; missing values are `null`) so FE can render a stable edit form.
- Title extraction collapses repeated spaces and trims trailing clauses like "and put it into a new milestone".
- If the message implies "new milestone", Draft will suggest a `milestoneName` when missing.

## Commit request (recommended)

Send the approved draft directly (no wrapper):

```json
{
  "actionType": "create_backlog_and_task",
  "actionPayload": { "...": "..." }
}
```

{
  "answerText": "Got it. I drafted an action for you—please review/edit it and confirm to commit.",
  "questions": [
    "What should the new milestone be named? (You can rename my suggestion)",
    "Any extra details (assignees, priority, due date)?"
  ],
  "draft": {
    "actionType": "create_backlog_and_task",
    "actionPayload": {
      "title": "drawing class diagram and put  it",
      "description": "Deliverable: produce a clear class diagram for drawing class diagram and put  it.\n- Identify key entities/classes and relationships (association, inheritance, composition).\n- Include main attributes/methods at a high level (no over-detail).\n- Export diagram (PNG/PDF) and attach source file (draw.io/PlantUML).\nAcceptance: diagram is readable, consistent naming, and covers core domain objects.",
      "milestoneName": "drawing class diagram and put  it milestone"
    }
  },
  "dedupe": {
    "similarItems": []
  },
  "candidates": {
    "columns": [
      {
        "columnId": "74e40ffb-8ee7-4585-99cb-bd0c41e5b965",
        "name": "To Do",
        "isDone": false
      },
      {
        "columnId": "3da437ef-bbca-48ed-8fc8-1fbf4ba6368c",
        "name": "In Progress",
        "isDone": false
      },
      {
        "columnId": "73bd50e4-1b38-4996-aea8-2082813f030f",
        "name": "Done",
        "isDone": true
       }
    ],
    "milestones": [],
    "backlogItems": [],
    "tasks": [],
    "members": [
      {
        "userId": "647996be-b304-4b12-bcf0-65d5da9a6d95",
        "displayName": "Trần Hải Sơn - 01",
        "email": "zzvuadaulauzz@gmail.com"
      },
      {
        "userId": "0653b2f2-bf82-4443-ae76-d9172e9639d7",
        "displayName": "NguyenPhiHung",
        "email": "phihungnguyen03022003@gmail.com"
      }
    ]
  }
}