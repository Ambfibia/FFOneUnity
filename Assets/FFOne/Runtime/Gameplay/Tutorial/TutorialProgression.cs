using System;
using System.Linq;
using FFOne.Content;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace FFOne.Tutorial
{
    public sealed partial class TutorialController
    {
        // Chapter_02..07 stage IDs, transitions, delays, positions and task IDs are
        // from ffone-tutorial-core and tutorial_runtime/logic, not saved progression.
        Vector3? marker;
        string instruction = "";
        JObject englishText;
        float pendingWarp = -1;
        Vector3 warpTarget;

        void SetStage(int chapter, int step, bool preserveEvents = false)
        {
            Stage = TutorialStages.Get(chapter, step);
            stageTime = 0; audioCursor = 0;
            stageOrigin = Player.position; stageOriginYaw = yaw;
            if (!preserveEvents) { deaths = damageNpc = damageUser = 0; jumped = nanoPowerUsed = false; }
            missionSelected = accepted = rewardClaimed = warped = false;
            marker = null;
            instruction = LocalizeLiteral(Stage.Instruction);
            subtitle = "";
            if (!string.IsNullOrEmpty(Stage.Voice) && TutorialAuxiliaryTimeline.ForStage(chapter, step) == TutorialAuxiliarySequence.None) PlayVoice(Stage.Voice);
            if (Stage.Scene != TutorialScene.None)
            {
                interacting = 0; mainMenu = journal = false;
                cinematics.Begin(Stage.Scene);
            }
            if (chapter == 0)
            {
                marker = step switch {
                    3 => StartPosition + new Vector3(0, 2, -8),
                    4 => StartPosition + new Vector3(8, 2, 0),
                    5 => StartPosition + new Vector3(7, 5, 0),
                    6 => StartPosition + new Vector3(4, 1, 0),
                    7 => StartPosition + new Vector3(9, 1, 0),
                    10 => new Vector3(555.82f, -104, 654.2f),
                    11 => new Vector3(566, -99, 665), _ => null
                };
                if (step == 7) { yaw = 90; pitch = 12; }
            }
            SyncAuxiliary();
        }
        string LocalizeLiteral(string value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            if (!Russian) return value;
            englishText ??= FFOne.Content.NativeData.Object("localization/en")["entries"] as JObject;
            string key = englishText?.Properties().FirstOrDefault(p => (string)p.Value == value && p.Name.StartsWith("content.tutorial.", StringComparison.Ordinal))?.Name;
            return key == null ? value : Localized(key);
        }
        bool Near(int id, float radius)
        {
            if (!actors.TryGetValue(id, out var actor) || !actor) return false;
            return actor.Damaged || Vector3.Distance(Player.position, actor.transform.position) <= radius;
        }
        bool SceneDone => Stage.Scene != TutorialScene.None && cinematics.Finished;
        void Evaluate()
        {
            if (pendingWarp >= 0)
            {
                pendingWarp += Time.deltaTime; fade = Mathf.Clamp01(pendingWarp / .75f);
                if (pendingWarp < 1.5f) return;
                WarpPlayer(warpTarget); pendingWarp = -1; fade = 0; interacting = 0; warped = true;
            }
            switch (Stage.Chapter)
            {
                case 0: MovementProgress(); break;
                case 1: CombatProgress(); break;
                case 2: MinimapProgress(); break;
                case 3: MissionProgress(); break;
                case 4: InfectionProgress(); break;
                case 5: NanoProgress(); break;
            }
        }
        void MovementProgress()
        {
            switch (Stage.Step)
            {
                case 0: SetStage(0, 1); break;
                case 1: if (SceneDone) SetStage(0, 2); break;
                case 2: if (stageTime >= 1.5f) SetStage(0, 3); break;
                case 3: case 4: case 5: case 6:
                    Vector3 position = ViewCamera.WorldToScreenPoint(marker!.Value - (Stage.Step == 3 || Stage.Step == 4 || Stage.Step == 6 ? Vector3.up : Vector3.zero));
                    float halfWidth = Stage.Step < 5 ? 300 : Screen.width / 2f;
                    float halfHeight = Stage.Step < 5 ? float.PositiveInfinity : Stage.Step == 5 ? 150 : 50;
                    if (stageTime > .3f && position.z > 0 && Mathf.Abs(position.x - Screen.width / 2f) < halfWidth && Mathf.Abs(position.y - Screen.height / 2f) < halfHeight) SetStage(0, Stage.Step + 1);
                    break;
                case 7: if (Vector3.Distance(Player.position, StartPosition + new Vector3(9, 1, 0)) < 3) SetStage(0, 8); break;
                case 8: if (HorizontalDistance(Player.position, stageOrigin) > 5) SetStage(0, 9); break;
                case 9: if (HorizontalDistance(Player.position, stageOrigin) > 5 && Mathf.Abs(Mathf.DeltaAngle(yaw, stageOriginYaw)) > 40) SetStage(0, 10); break;
                case 10: if (Vector3.Distance(Player.position, new Vector3(555.82f, -105, 654.2f)) < 3) SetStage(0, 11); break;
                case 11: if (Vector3.Distance(Player.position, new Vector3(566, -99, 665)) < 3 && jumped && motor.isGrounded) SetStage(1, 0); break;
            }
        }
        static float HorizontalDistance(Vector3 a, Vector3 b) => Vector2.Distance(new Vector2(a.x, a.z), new Vector2(b.x, b.z));
        void CombatProgress()
        {
            switch (Stage.Step)
            {
                case 0: SetStage(1, 1); break;
                case 1: if (SceneDone) SetStage(1, 2); break;
                case 2: if (target && target.Hostile) SetStage(1, 3); break;
                case 3: if (damageNpc > 0) SetStage(1, 4); break;
                case 4:
                    if (deaths >= 3) { RemoveActor(1001); RemoveActor(1002); RemoveActor(1003); SetStage(1, 5); }
                    break;
                case 5: if (SceneDone) SetStage(1, 6); break;
                // Direct legacy step write preserves the death edge on the first damage tick.
                case 6: if (damageUser > 0) SetStage(1, 7, true); break;
                case 7: if (deaths > 0) SetStage(1, 8); break;
                case 8:
                    if (SceneDone) { RemoveActor(1005); RemoveActor(100); RemoveActor(101); SetStage(2, 0); }
                    break;
            }
        }
        void MinimapProgress()
        {
            switch (Stage.Step)
            {
                case 0: SetStage(2, 5); break;
                case 5: if (Near(1005, 50)) SetStage(2, 6); break;
                case 6: if (Near(1005, 25)) SetStage(3, 0); break;
            }
        }
        void MissionProgress()
        {
            switch (Stage.Step)
            {
                case 0: if (Near(1005, 5)) SetStage(3, 1); break;
                case 1: if (interacting == 1005) SetStage(3, 3); break;
                case 3: if (missionSelected) SetStage(3, 100); break;
                case 100: if (stageTime >= 9) SetStage(3, 101); break;
                case 101: if (accepted) { currentTask = 2248; SetStage(3, 102); } break;
                case 102:
                    if (interacting == 0)
                    {
                        SpawnActor(2000, 2374, new Vector3(697, -82, 794));
                        SpawnActor(1100, 2676, new Vector3(697, -82, 794), 90);
                        SetStage(3, 4);
                    }
                    break;
                case 4: if (currentTask == 2249) SetStage(3, 5); break;
                case 5: if (stageTime >= 3.5f) SetStage(3, 6); break;
                case 6: if (mainMenu) { RemoveActor(1100); SetStage(3, 7); } break;
                case 7: if (journal) { mainMenu = false; SetStage(3, 8); } break;
                case 8: if (journal && stageTime >= 4) SetStage(3, 9); break;
                case 9: if (journal && stageTime >= 2.5f) SetStage(3, 10); break;
                case 10: if (journal && stageTime >= 2.5f) SetStage(3, 11); break;
                case 11: if (journal && stageTime >= 4.2f) SetStage(3, 12); break;
                case 12: if (!journal) SetStage(3, 13); break;
                case 13: if (Near(1005, 15)) SetStage(3, 14); break;
                case 14: if (interacting == 1005) SetStage(3, 16); break;
                case 16: if (missionSelected) SetStage(3, 17); break;
                case 17: if (rewardClaimed) SetStage(3, 18); break;
                case 18:
                    if (interacting == 0)
                    {
                        SpawnActor(1007, 2672, new Vector3(624, -82, 772), 275);
                        SpawnActor(1008, 2375, new Vector3(596, -82, 784)); currentTask = 2250; SetStage(4, 0);
                    }
                    break;
            }
        }
        void InfectionProgress()
        {
            switch (Stage.Step)
            {
                case 0: if (Near(1007, 10)) SetStage(4, 1); break;
                case 1: if (interacting == 1007) SetStage(4, 2); break;
                case 2: if (accepted) { currentTask = 2251; SetStage(4, 101); } break;
                case 101: if (stageTime >= 6.3f) SetStage(4, 102); break;
                case 102:
                    if (interacting == 0) { SpawnActor(1009, 2694, new Vector3(596, -82, 784), 291); SetStage(4, 3); }
                    break;
                case 3: if (SceneDone) { RemoveActor(1012); SetStage(4, 4); } break;
                case 4:
                    if (interacting == 1009) SetStage(4, 6);
                    else if (Near(1009, 20)) SetStage(4, 5);
                    break;
                case 5: if (interacting == 1009) SetStage(4, 6); break;
                case 6:
                    if (warped)
                    {
                        SpawnActor(1010, 2376, new Vector3(595, -92, 745));
                        SpawnActor(1011, 2377, new Vector3(564.5f, -91, 724));
                        SpawnActor(1012, 2695, new Vector3(564.31f, -91, 724.16f));
                        RemoveActor(1005); currentTask = 2252; SetStage(4, 8);
                    }
                    break;
                case 8: if (Near(1012, 15)) SetStage(4, 200); break;
                case 200: if (interacting == 1012) SetStage(4, 201); break;
                case 201:
                    if (warped)
                    {
                        SpawnActor(3001, 2378, new Vector3(587, -133, 984));
                        SpawnActor(3000, 2673, new Vector3(587, -133, 984), 262); currentTask = 2253; SetStage(4, 9);
                    }
                    break;
                case 9: if (interacting == 3000) SetStage(4, 109); break;
                case 109: if (accepted) { currentTask = 2254; SetStage(4, 210); } break;
                case 210:
                    if (SceneDone)
                    {
                        SpawnActor(4000, 2678, new Vector3(563, -133, 967), 266);
                        SpawnActor(3000, 2902, new Vector3(562, -133, 967));
                        Animate(Actor(3000), "melee1event"); Animate(Actor(4000), "melee1");
                        WarpPlayer(new Vector3(588.4f, -133.5f, 983.8f)); SetStage(4, 10);
                    }
                    break;
                case 10:
                    if (deaths > 0) SetStage(4, 13);
                    else if (Near(4000, 20)) SetStage(4, 11);
                    break;
                case 11: if (deaths > 0) SetStage(4, 13); break;
                case 13:
                    if (SceneDone)
                    {
                        SpawnActor(4001, 2696, new Vector3(558, -133, 949), -132);
                        RemoveActor(3000); RemoveActor(4000); SetStage(4, 15);
                    }
                    break;
                case 15: if (interacting == 4001) SetStage(4, 16); break;
                case 16: if (warped) { StopLoops(); SetStage(5, 0); } break;
            }
        }
        void NanoProgress()
        {
            switch (Stage.Step)
            {
                case 0:
                    SpawnActor(5100, 2671, new Vector3(907, 11, 685), 180);
                    SpawnActor(5101, 2800, new Vector3(908, 11, 682), 104); SetStage(5, 1); break;
                case 1: if (SceneDone) { RemoveActor(2); SetStage(5, 2); } break;
                case 2: if (Near(5100, 28)) SetStage(5, 3); break;
                case 3:
                    if (SceneDone)
                    {
                        actors[5000].Invulnerable = !Russian; SetStage(5, 4);
                    }
                    break;
                case 4:
                    if (Russian && deaths > 0) SetStage(5, 7);
                    else if (nanoActive) SetStage(5, 6);
                    break;
                case 6: if (deaths > 0 || nanoPowerUsed) SetStage(5, 7); break;
                case 7: if (interacting == 5100) SetStage(5, 8); break;
                case 8: if (SceneDone) SetStage(5, 9); break;
                case 9: Finish(); break;
            }
        }
        void RequestWarp()
        {
            if (pendingWarp >= 0) return;
            // Accepted XDT m_pWarpData rows 253,254,255. Unity axis order is X,Z,Y /100.
            warpTarget = interacting switch {
                1009 => new Vector3(595.73f, -90.66f, 745.45f),
                1012 => new Vector3(596.12f, -133, 985.67f),
                4001 => new Vector3(907.65f, 10.43f, 715.58f),
                _ => throw new InvalidOperationException("No tutorial warp owned by this actor.")
            };
            pendingWarp = 0;
        }
        int ObjectiveNpc => Stage.Chapter switch {
            2 => 1005,
            3 => Stage.Step == 4 ? 1100 : 1005,
            4 => Stage.Step < 3 || Stage.Step == 101 || Stage.Step == 102 ? 1007
                : Stage.Step <= 6 ? 1009 : Stage.Step == 8 || Stage.Step == 200 || Stage.Step == 201 ? 1012
                : Stage.Step == 9 || Stage.Step == 109 ? 3000 : Stage.Step == 10 || Stage.Step == 11 ? 4000 : 4001,
            5 => Stage.Step == 4 || Stage.Step == 6 ? 5000 : 5100,
            _ => 0
        };
    }
}
