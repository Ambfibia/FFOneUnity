using System;
using System.Collections.Generic;
using System.Linq;

namespace FFOne.Tutorial
{
    // Numeric chapter/step contracts and permissions: ffone-tutorial-core/src/tutorial.rs,
    // recovered cntutorialscript Chapter_02 .. Chapter_07. Never renumber legacy steps.
    public enum TutorialScene { None, BasicMove, BasicCombatA, BasicCombatB, BasicCombatC, InfectionA, InfectionB, InfectionC, InfectionD, NanoPowerA, NanoPowerA2, NanoPowerB }
    public readonly struct TutorialStage
    {
        public readonly int Chapter, Step, AllowedInputs;
        public readonly string Name, Instruction, Voice;
        public readonly TutorialScene Scene;
        public TutorialStage(int chapter, int step, string name, string instruction, string voice, TutorialScene scene, int allowedInputs)
        { Chapter = chapter; Step = step; Name = name; Instruction = instruction; Voice = voice; Scene = scene; AllowedInputs = allowedInputs; }
        public bool Allows(int index) => (AllowedInputs & (1 << index)) != 0;
    }
    public static class TutorialStages
    {
        const int INPUT_FRONT = 1 << 0;
        const int INPUT_BACK = 1 << 1;
        const int INPUT_SIDE = 1 << 2;
        const int INPUT_TURN = 1 << 3;
        const int INPUT_JUMP = 1 << 4;
        const int INPUT_ATTACK = 1 << 5;
        const int INPUT_NANO_POWER = 1 << 6;
        const int INPUT_WEAPON_CHANGE = 1 << 7;
        const int INPUT_MENU = 1 << 8;
        const int INPUT_INVENTORY = 1 << 9;
        const int INPUT_OPTION = 1 << 10;
        const int INPUT_JOURNAL = 1 << 11;
        const int INPUT_SOCIAL = 1 << 12;
        const int INPUT_WORLD_MAP = 1 << 13;
        const int INPUT_EMAIL = 1 << 14;
        const int INPUT_QUIT = 1 << 15;
        const int INPUT_MODE_CHANGE = 1 << 16;
        const int INPUT_MOUSE = 1 << 17;
        const int INPUT_NANO_ACTIVE = 1 << 18;
        const int LOOK_INPUT = INPUT_MOUSE;
        const int FORWARD_INPUT = INPUT_FRONT | INPUT_MOUSE;
        const int BACKWARD_INPUT = INPUT_BACK | INPUT_MOUSE;
        const int STEER_INPUT = INPUT_FRONT | INPUT_TURN | INPUT_MOUSE;
        const int WALK_INPUT = INPUT_FRONT | INPUT_BACK | INPUT_SIDE | INPUT_TURN | INPUT_MOUSE;
        const int MOVE_INPUT = WALK_INPUT | INPUT_JUMP;
        const int COMBAT_INPUT = MOVE_INPUT | INPUT_ATTACK;
        const int TALK_INPUT = INPUT_TURN | INPUT_ATTACK | INPUT_MOUSE;
        const int TALK_CLOSE_INPUT = TALK_INPUT | INPUT_MENU | INPUT_MODE_CHANGE;
        const int COMBAT_MENU_INPUT = COMBAT_INPUT | INPUT_MENU | INPUT_MODE_CHANGE;
        const int MENU_JOURNAL_INPUT = INPUT_MENU | INPUT_JOURNAL;
        const int GENERAL_INPUT = COMBAT_MENU_INPUT | INPUT_JOURNAL;
        const int NPC_MENU_INPUT = COMBAT_INPUT | INPUT_JOURNAL;
        const int NANO_SUMMON_INPUT = INPUT_NANO_ACTIVE;
        const int NANO_COMBAT_INPUT = GENERAL_INPUT | INPUT_NANO_POWER | INPUT_NANO_ACTIVE;
        public static readonly TutorialStage[] All = {
            new(0, 0, "AwaitIntro", null, null, TutorialScene.None, 0),
            new(0, 1, "IntroCutscene", null, null, TutorialScene.BasicMove, 0),
            new(0, 2, "PostIntro", null, "Computress_Tut02", TutorialScene.None, 0),
            new(0, 3, "LookRight", "Move your mouse to the right and find the marker.", "Computress_Tut03", TutorialScene.None, LOOK_INPUT),
            new(0, 4, "LookLeft", "Move your mouse to the left and find the marker.", "Computress_Tut04", TutorialScene.None, LOOK_INPUT),
            new(0, 5, "LookUp", "Push your mouse forward and find the marker.", "Computress_Tut05", TutorialScene.None, LOOK_INPUT),
            new(0, 6, "LookDown", "Pull your mouse back and find the marker.", "Computress_Tut06", TutorialScene.None, LOOK_INPUT),
            new(0, 7, "MoveForward", "Press and hold the \"W\" key to move forward.", "Computress_Tut07", TutorialScene.None, FORWARD_INPUT),
            new(0, 8, "MoveBackward", "Press the \"S\" key to move backwards.", "Computress_Tut09", TutorialScene.None, BACKWARD_INPUT),
            new(0, 9, "MoveAndSteer", "Press and hold the \"W\" key to move forward while steering with the mouse.", "Computress_Tut10", TutorialScene.None, STEER_INPUT),
            new(0, 10, "ReachLedge", "Move to the marker near the ledge.", "Computress_Tut11", TutorialScene.None, WALK_INPUT),
            new(0, 11, "JumpAndLand", "Jump onto the ledge by pressing the Space Bar.", "Computress_Tut12", TutorialScene.None, MOVE_INPUT),
            new(1, 0, "AwaitIntro", null, null, TutorialScene.None, 0),
            new(1, 1, "CombatIntro", null, null, TutorialScene.BasicCombatA, 0),
            new(1, 2, "TargetHostile", "Use your mouse to target a monster.", "Computress_Tut13", TutorialScene.None, COMBAT_INPUT),
            new(1, 3, "FirstAttack", "Click the Left Mouse button to attack. Hold the button down for continuous fire.", "Computress_Tut14", TutorialScene.None, COMBAT_INPUT),
            new(1, 4, "KillThree", "Click the Left Mouse button to attack. Hold the button down for continuous fire.", "Computress_Tut15", TutorialScene.None, COMBAT_INPUT),
            new(1, 5, "CyberusIntro", null, null, TutorialScene.BasicCombatB, 0),
            new(1, 6, "FightCyberus", "Defeat the monster.", null, TutorialScene.None, COMBAT_INPUT),
            new(1, 7, "PlayerWasHit", "Defeat the monster.", "Computress_Tut18", TutorialScene.None, COMBAT_INPUT),
            new(1, 8, "PlanetFusionOutro", null, null, TutorialScene.BasicCombatC, 0),
            new(2, 0, "AwaitSequence", null, null, TutorialScene.None, 0),
            new(2, 5, "MinimapSequence", "Go to Numbuh Two.", "NumTwo_Tut01", TutorialScene.None, COMBAT_INPUT),
            new(2, 6, "ApproachNumbuhTwo", "Go to Numbuh Two.", "Computress_Tut23", TutorialScene.None, MOVE_INPUT),
            new(3, 0, "Approach", "Go to Numbuh Two.", null, TutorialScene.None, MOVE_INPUT),
            new(3, 1, "TargetAndTalk", "Target Numbuh Two and click the Left Mouse button to talk.", "Computress_Tut24", TutorialScene.None, TALK_INPUT),
            new(3, 3, "MissionMenu", "Select \"Transmitter Critters\" from mission menu.", "Computress_Tut25", TutorialScene.None, TALK_INPUT),
            new(3, 4, "ObjectiveCombat", null, "Computress_Tut29", TutorialScene.None, COMBAT_MENU_INPUT),
            new(3, 5, "NumbuhTwoResponse", null, "NumTwo_Tut03", TutorialScene.None, 0),
            new(3, 6, "OpenMenu", "Press \"Enter\" key to access the main menu. Then select \"Journal\".", "Computress_Tut30", TutorialScene.None, MENU_JOURNAL_INPUT),
            new(3, 7, "SelectJournal", "Press \"Enter\" key to access the main menu. Then select \"Journal\".", null, TutorialScene.None, MENU_JOURNAL_INPUT),
            new(3, 8, "JournalIntro", null, "Computress_Tut31", TutorialScene.None, 0),
            new(3, 9, "JournalMissionTab", null, "Computress_Tut32", TutorialScene.None, 0),
            new(3, 10, "JournalObjective", null, "Computress_Tut33", TutorialScene.None, 0),
            new(3, 11, "JournalDetail", null, "Computress_Tut34", TutorialScene.None, 0),
            new(3, 12, "CloseJournal", null, "Computress_Tut35", TutorialScene.None, GENERAL_INPUT),
            new(3, 13, "ReturnToNumbuhTwo", null, "Computress_Tut36", TutorialScene.None, GENERAL_INPUT),
            new(3, 14, "NearNumbuhTwo", null, "Computress_Tut37", TutorialScene.None, GENERAL_INPUT),
            new(3, 16, "RewardMenu", "Select \"Transmitter Critters\" from mission menu.", "Computress_Tut38", TutorialScene.None, NPC_MENU_INPUT),
            new(3, 17, "ClaimReward", null, "Computress_Tut39", TutorialScene.None, NPC_MENU_INPUT),
            new(3, 18, "CloseReward", "Click the \"Close\" button.", "Computress_Tut40", TutorialScene.None, GENERAL_INPUT),
            new(3, 19, "Handoff", null, null, TutorialScene.None, 0),
            new(3, 100, "NumbuhTwoDelay", null, "NumTwo_Tut02", TutorialScene.None, 0),
            new(3, 101, "AcceptMission", null, "Computress_Tut27", TutorialScene.None, TALK_INPUT),
            new(3, 102, "CloseAccept", "Click the \"Close\" button.", "Computress_Tut28", TutorialScene.None, TALK_CLOSE_INPUT),
            new(4, 0, "ApproachButtercup", "Talk to Buttercup.", null, TutorialScene.None, GENERAL_INPUT),
            new(4, 1, "TalkButtercup", "Talk to Buttercup.", null, TutorialScene.None, GENERAL_INPUT),
            new(4, 2, "SelectMission", "Select \"A Fusion Matter\" from mission menu.", "Computress_Tut44", TutorialScene.None, NPC_MENU_INPUT),
            new(4, 3, "InfectedFlythrough", null, null, TutorialScene.InfectionA, 0),
            new(4, 4, "TravelToGate", null, "Computress_Tut46", TutorialScene.None, GENERAL_INPUT),
            new(4, 5, "SelectAttendant", "Select the Dexbot attendant to use the warp gate.", "Computress_Tut47", TutorialScene.None, GENERAL_INPUT),
            new(4, 6, "WarpFromTechSquare", "Click the \"Warp\" button to enter Fusion Buttercup's lair.", null, TutorialScene.None, GENERAL_INPUT),
            new(4, 8, "OutsidePortal", "Go to the Fusion Portal.", "Dexter_Tut01", TutorialScene.None, GENERAL_INPUT),
            new(4, 9, "TalkDexter", "Talk to Dexter.", "Dexter_Tut02", TutorialScene.None, GENERAL_INPUT),
            new(4, 10, "ApproachFusion", null, null, TutorialScene.None, GENERAL_INPUT),
            new(4, 11, "FightFusion", null, null, TutorialScene.None, GENERAL_INPUT),
            new(4, 13, "NanoCreation", null, null, TutorialScene.InfectionC, 0),
            new(4, 15, "ExitPrompt", "Use the warp gate to exit.", "Computress_Tut56", TutorialScene.None, GENERAL_INPUT),
            new(4, 16, "WarpOut", "Click the \"Warp\" button to exit the lair.", null, TutorialScene.None, GENERAL_INPUT),
            new(4, 101, "ButtercupDelay", null, "Btrcup_Tut07", TutorialScene.None, 0),
            new(4, 102, "CloseMission", "Click the \"Close\" button.", "Computress_Tut45", TutorialScene.None, GENERAL_INPUT),
            new(4, 109, "SelectDexterMission", "Select \"A Fusion Matter\" from mission menu.", "Computress_Tut53", TutorialScene.None, NPC_MENU_INPUT),
            new(4, 200, "SelectTentacles", "Select the tentacles to use the Fusion Portal.", "Computress_Tut50", TutorialScene.None, GENERAL_INPUT),
            new(4, 201, "WarpIntoLair", "Click the \"Warp\" button to enter Fusion Buttercup's lair.", "Computress_Tut51", TutorialScene.None, GENERAL_INPUT),
            new(4, 210, "DexterCutscene", null, null, TutorialScene.InfectionB, 0),
            new(5, 0, "AwaitCollapse", null, null, TutorialScene.None, 0),
            new(5, 1, "CollapseCutscene", null, null, TutorialScene.NanoPowerA, 0),
            new(5, 2, "CrossBridge", "Cross the bridge to escape.", null, TutorialScene.None, GENERAL_INPUT),
            new(5, 3, "DemoMonsterCutscene", null, null, TutorialScene.NanoPowerA2, 0),
            new(5, 4, "SummonNano", "Press \"1\" to summon your Buttercup Nano.", "Computress_Tut59", TutorialScene.None, NANO_SUMMON_INPUT),
            new(5, 6, "UseNanoPower", "Stun the monster.", "Computress_Tut60", TutorialScene.None, NANO_COMBAT_INPUT),
            new(5, 7, "ReturnToNumbuhTwo", "Talk to Numbuh Two.", "Computress_Tut62", TutorialScene.None, NANO_COMBAT_INPUT),
            new(5, 8, "FinaleCutscene", null, null, TutorialScene.NanoPowerB, 0),
            new(5, 9, "Exit", null, null, TutorialScene.None, 0),
        };
        public static TutorialStage Get(int chapter, int step) => All.Single(s => s.Chapter == chapter && s.Step == step);
    }
}
