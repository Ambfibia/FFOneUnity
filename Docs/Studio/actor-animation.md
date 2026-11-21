# Tutorial and gameplay actor animation

The tutorial uses the original choreography heights and a native Timeline. The player motor advances with zero input while the Timeline runs, with gravity and collision on the existing capsule. Warp commands still set their authored coordinates. The scripted NanoPowerB translation still writes each authored position sample. Timeline pause holds the motor along with the scene clock.

In the authored `map_01_01.unity` (SHA-256 `573f305d761e0b96b23fcfd092d389259aced0eb26107ff6cb0e6663fb0edbbc`), the BasicCombatA command uses Y = −100 while the MeshCollider surface at that X/Z is Y = −101.508. The same warp previously stayed 1.508 m above this surface for the entire shot. The cave and bridge warps also have small offsets in both directions relative to the native surfaces.

Retrobution 2026-08-21 evidence comes from `main.unity3d` SHA-256 `01b544976b2d54355507cf30fe6dfada2b476b92b209a3d47c1499669ed9b4ef`, assembly `Assembly - CSharp.dll`. These are static observations from bounded FusionForge inspection.

| Owner | Evidence |
| --- | --- |
| `cnAvatarThirdPersonMove` | `ForceUpdate` (0x06000130) applies the motor during events. `ProcessMovementInput` (0x06000132) filters tutorial input. `IsGravityEnabled` (0x06000153) checks dash state. |
| `cntutorialscript` | `BasicCombatEvent_A.MoveNext` (0x06000e89) and `NanoPower_Event_A2.MoveNext` (0x06000ecb) write the player transform directly. |
| `NpcAnimation` | `Pause` (0x0600094f) zeros the active state speed. `ForceAnimation` (0x06000952), `ForceAnimationOnce` (0x06000953) and `EndAnimation` (0x0600096b) govern the forced clip and its retrigger. `MoveMotion` (0x06000961) and `StandMotion` (0x06000963) preserve that forced state. |
| `DeadMotion` | `Dead` (0x06000741) arms the death effect timer. Its pose was already started by the choreography. FFOneClient's accepted NPC pose contract clamps the death clip to its terminal pose. |

The resulting tutorial pose states distinguish resting, locomotion, script-owned poses and pause. Scripted one-shots can restart at the authored end event. Gameplay hits and attacks complete and return to ready. Ordinary world NPCs and mobs use gameplay state priority driven by existing health, condition, position and combat events. A model loaded after interest entry immediately receives the current persistent state. Transient events before the model is available expire with that presentation gap.

Both player rigs already have editable Unity Animator Controllers. Tutorial and overworld on-foot movement use the existing locomotion transition driver. Both controllers keep their upper-body action layers, turn layers, body adjustments and authored jump transitions. Vehicles keep their authored landing flow.

## Native Animator migration on 2026-11-16

`NpcAnimatorPublication` enumerates the published NPC, mob and Fusion prefabs, resolved world routes, tutorial actors and Dexter Ship actors. Each animated prefab gets a native Animator on the same node that owned its original animation bindings. Its editable controller contains the original names and a looping variant where required. The original clips retain their existing identity and the controller reads ordinary Unity animation assets. The generated states are editable in the Animator window, and gameplay and Timeline commands select and blend those states at runtime.

The network state driver distinguishes waiting, walking, running, attack, hit, ready, stun, sleep and death. It retains finished death poses and starts repeated attacks from zero. World idles can choose another authored stand after a full cycle, following the accepted FFOneClient `npc_bad_max_idle_finishes_each_full_clip_before_selecting_another` behavior. The 24-byte 0104 NPC movement packet already carries signed `iMoveStyle` at offset 20. The client uses zero for walking and nonzero for running, matching the accepted FFOneClient decoder and RustyFusion encoder. Native walk and run speed multipliers come from XDT.

The tutorial uses the same Animator playback component for its independent Timeline command states. Active motion names gate clip events so an outgoing animation cannot complete the new one. The active clip can repeat at its authored end, pause at the current time or hold its terminal pose. Visual events address the original bone, material slot and cue definition. Interrupted face cues restore their previous texture. Dexter Ship pauses its animators for the first render and then advances the authored scene.

The player animation driver resolves the equipment family from XDT `m_iEquipType`, including the first world pose, normal locomotion and the upper-body attack. The existing controllers retain separate base and upper-layer requests. The tutorial render gate freezes the separately rooted player along with its Timeline. The scripted poses use the exact `staying`, `standup` and `run` names. Retrobution `cnAvatarAnimation.EndAnimation` (0x06000088) uses a 0.3 s return blend. The `standup` end is at 2.0833335 s in both accepted rig clips, path IDs 34404 and 34533 in CAB `CustomAssetBundle-ce09c4c9be8a046ca92e0044f22d1b99` from `CharacterSelection.resourceFile` SHA-256 `96a3362fdfff0da0107bd1aea5666431b417380c9855c6f09fceebc8d372ae93`. BasicMove explicitly replaces `staying` after one second.

At the time of this audit, 24 visible NPC type rows across 18 names lacked prefab bindings. The exact IDs are 2506, 2508, 2509, 3174, 3181, 3182, 3186–3188, 3201, 3251–3255, 3257, 3266, 3367, 3395–3398, 3408 and 3417. The scoped XDT source SHA-256 is `191a9d17f4d233270cceb574a9087990e1526ae697174655203fbb1c6116955c`. Each row resolves its logical name through `m_pNpcTable.m_pNpcData`, `m_iMesh` and `m_pNpcMeshData[].m_pstrMMeshModelString`. See [XDT model recovery](xdt-model-recovery.md) for the follow-up asset search and publication. Model-specific NPC skill selection and the historical delayed tutorial death sink remain separate reconstruction gaps.

### Verification on 2026-11-16

Unity verified 380 native actor controllers and six static prefabs. The serialization comparison covered 6,209 native clip bindings from 4,359 original clips, including 462 copies with events. The prefab GUID and GameObject identifier comparisons matched. Curve and event mappings matched, including the four RXCOM clips with equivalent numeric or packed representations. Its two empty bind-pose variants serialize a one-second length in Unity 6. The active `stand1` timing is exact. The audit found all 100 sound and three face cue bindings.

Unity 6000.4.0f1 passed 200 selected EditMode cases and built the Windows client with rebuilt Addressables. The tests include native transform bindings, timed face and end events, cutscene pause and held death, the NPC transition from walk to run and return to ready, authored player weapon poses, separate player layers, and the first-frame gate.

The final Windows client passed all ten tutorial timelines, both Dexter Ship scenes, the tutorial combat and movement checks, world entry, and the cold occupied roster on a disposable SQLite fixture. The run observed 321 animation sound callbacks and three root effects. The keyboard and gamepad input checks passed. The standalone retry used the same Addressables settings and catalog bytes.

The targeted network player test confirmed the server-side health change after a player attack. It then observed the mob's native attack state advance after the real RustyFusion combat packet. The targeted run used a disposable server database and the player's normal login, model load and attack path. It omitted the separate transport-harness exercise.

The full network player run passed 247 checks with complete content and zero reported player exceptions. It verified RXCOM's inactive source body, both native clip bindings, unchanged node hierarchy and the visible Recall Point effects. The run also covered a real mob attack, player death, revival and world teardown. The review build changes the development-only RXCOM assertion and uses the same game logic and catalog as the final tutorial and targeted combat build.

See `Logs/Studio/actor-animator-20261116/publication-serialization-verification.json`, `tests-release.xml`, `build-combat.log`, `ClientFinal/validation.json`, `live-actor-animation.json`, and `live-gameplay-review.json` for the asset, test, build, standalone, targeted combat, and broader network results.

## Verification on 2026-11-15

Unity 6000.4.0f1 built the Windows client and passed 183 selected EditMode tests. The authored-map check settled the player on the floor at five cinematic warps. The broader development run had 192 passes and one failure. `TutorialTests.ImportedTutorialHasExactTerrainAndCollisionWorld` expects `Skybox/6 Sided`, while the current material uses `FFOne/TutorialSkybox`.

The Windows client ran all ten tutorial scenes, the tutorial combat and locomotion checks, overworld entry, keyboard and gamepad movement, and the cold occupied roster against an isolated RustyFusion instance. The runner reported zero exceptions and zero missing tutorial animations. Captures of BasicCombatA, NanoPowerB, landing, creation, world entry and the occupied roster were inspected. This exercises tutorial combat and ordinary world loading. The network NPC state transitions are covered in EditMode.

Evidence is under `Logs/Studio/tutorial-animation-20261115`. `tests-final.xml` contains the selected tests, `Client/validation.json` pins the client assembly and catalog, and `Client/*/captures` contains the native framebuffer captures. The server was started on loopback ports 24452 and 24453 with the task-local SQLite database in the same evidence directory.
