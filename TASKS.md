# TASKS

מסמך מעקב משימות חי. סמן `[x]` כשמשימה הושלמה; הוסף משימות חדשות מתחת תוך כדי עבודה.
תוכנית הבנייה המלאה ב-[MILESTONES.md](./MILESTONES.md).

## Todo
- [ ] M4 - `run_playmode` + `run_tests` + עמידות reload/reconnect
- [ ] M5 - ליטוש לוובינר (באג מכוון, טסטים, תסריט)

## In progress

## Done
- [x] M3 - `create_gameobject` (primitive/ריק) + `add_component`. פתרון יעד לפי
      שם או instanceId, פתרון טיפוס רכיב על פני האסמבליות, Undo. מאומת חי: cube
      "Player" + Rigidbody (לפי שם) + BoxCollider (לפי instanceId), ו-Light ל-GO ריק.
- [x] M2 - `read_console` + `ToolRegistry` (reflection scan) + ring buffer.
      `ping` אוחד גם הוא ל-`[McpTool]`; serialization דרך Newtonsoft. מאומת חי:
      read_console levels=[Error] החזיר את ה-Debug.LogError עם stack trace מלא.
- [x] M1 - הצינור: ping עובר end-to-end. מאומת headless ב-Unity batchmode -
      ה-package מתקמפל נקי, ה-bridge מאזין, ובדיקת WebSocket חיה החזירה
      `{"ok":true,"result":{"pong":true,"unityVersion":"6000.3.7f1"}}`.
      צד השרת: dotnet build נקי + MCP initialize/tools/list עובדים.
- [x] M0 - Scaffolding (משיכת האפיון והשלד מ-GitHub, חיבור ל-Agents hub, git init)
