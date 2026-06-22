# TASKS

מסמך מעקב משימות חי. סמן `[x]` כשמשימה הושלמה; הוסף משימות חדשות מתחת תוך כדי עבודה.
תוכנית הבנייה המלאה ב-[MILESTONES.md](./MILESTONES.md).

## Todo
(הכל הושלם - כל ה-milestones M0-M5)

## In progress

## Done
- [x] M5 - ליטוש לוובינר: באג מכוון `Assets/Scripts/GameBoot.cs` (NRE ב-Play),
      טסטי EditMode, ו-`demo-unity-project/CLAUDE.md`. מאומת חי: run_playmode תפס את
      ה-NRE, ו-read_console הצביע על GameBoot.cs:17. (תסריט הוובינר כבר היה מלא בשלד.)
- [x] M4 - `run_playmode` + `run_tests` (אסינכרוניים דרך McpToolContext) + עמידות:
      DisableDomainReload בצד Unity, reconnect עם backoff/parking בצד השרת. מאומת חי:
      run_tests passed:2, run_playmode entered/exited, והשרת האמיתי התחבר ל-Unity (ping).
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
