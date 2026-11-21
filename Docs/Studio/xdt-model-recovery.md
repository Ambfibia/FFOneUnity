# Recovery of 24 XDT actor records

The recovery scope is the 24 visible NPC types listed in [actor-animation.md](actor-animation.md). The imported XDT snapshot has SHA-256 `191a9d17f4d233270cceb574a9087990e1526ae697174655203fbb1c6116955c`. Type IDs resolve through `m_pNpcData.m_iMesh` and `m_pNpcMeshData[].m_pstrMMeshModelString`. The Unity catalog projects the editable `NativeDataDocument` and loads the prefab through the Addressables entry. Preserving the original IDs keeps existing server placement and world-replica behavior.

## Identified model sources

| XDT types | Model | Source and scope |
| --- | --- | --- |
| 3174 | `mob_epmetaltriangle` | Academy body, skin, atlas and four clips |
| 3181, 3182 | `mob_neverest` | Academy body, skin, atlas and 15 clips |
| 3186–3188 | `mob_stonescorpion` | Academy body, skin, atlas and 13 clips |
| 3201 | `npc_blowfish` | Separate Academy prefab, its own atlas and eight clips |
| 3254, 3255 | `mob_fusionparadox` | Academy body, three skins and 15 clips. The follow-up recovered the two original Time Deviant textures used with this body by the Academy client. |
| 3257 | `npc_bustle_chupacabra` | Turner `beta-20100119` body, skin, atlas and three clips |
| 3266 | `mob_gatekeeper` | Academy body, three skins, five meshes, three authored textures and six clips |

All seven identities have a direct `mob/<name>.kfm` entry bound to a serialized GameObject. The Academy donor is `OG-academy/DongResources_12_15.resourceFile`, SHA-256 `ef07acb95179e4657b55ad53529c7ea84cb35db7ae7927bed1179e397f080d47`, serialized asset `CustomAssetBundle-DongResources_12_15`. In the order above, its first five model owners have packed object IDs `76909267608056`, `251405663094537`, `49364532755818`, `147346826158987`, and `3258936357507`. The Gatekeeper route's PPtr has low word `2282155119`. The 2010 donor is [`beta-20100119/DongResources_07_06.resourceFile`](https://archive.org/download/fusionfallbetabuilds/beta-20100119/), SHA-256 `d275e024c265c093965e7092b23ed50d855a86b50968ab20a5251cf9508f6a52`, serialized asset `CustomAssetBundle-DongResources_07_06`, GameObject `2798267485`. Its atlas is also present in the build's `CustomAssetBundle-NpcTexture`, Texture2D `3836742310`.

The Academy main atlases for Epmetaltriangle, Neverest, Stone Scorpion and the non-hostile Blowfish match the accepted Unity PNGs byte for byte. The Turner Chupacabra atlas also matches. Gatekeeper includes three native Gatekeeper maps on its materials even though the independent logical XDT atlas key is unavailable. Fusion Paradox has the embedded jelly, toon ramp and Fusion Matter textures. Retrobution's `NpcMoveController/<SetupNPC>d__59.MoveNext` applies the texture named by `m_pstrMTextureString` to the main material only when the lookup succeeds. The initial Fusion Paradox publication used the shader's white base because the old `mob_fusionparadox.dds` lookup had no verified payload. Material evidence and full packed object receipts are under `Logs/Studio/xdt-model-recovery`.

### Time Deviant texture follow-up

The original Academy XDT `xdtdatas.asset` in `TableData.resourceFile` uses mesh row 496 for Time Deviant types 3370 and 3371. The model key agrees with row 234 of types 3254 and 3255 (`mob_fusionparadox`), and row 496 assigns `mob_timedeviant` and `mob_timedeviantjelly`. Academy's `NpcMoveController/<>c__CompilerGenerated106::MoveNext` token `0x06000f8b` assigns the main and second textures to `_MainTex` on the `main` and `sub` materials. The two source model owners in Academy have matching material slots. Time Deviant's original appearance is adopted from these same-body Academy records for current types 3254 and 3255. Their literal historical key `texture/mob_fusionparadox.dds` yielded no payload in the 35 SHA-1-distinct Turner texture containers checked. The recovered appearance is attributed to the documented donor, and the older variant's precise intended palette remains unknown.

| Native material | Texture | Academy Texture2D PPtr path ID | Original DXT5 levels | Compressed payload SHA-256 |
| --- | --- | --- | --- | --- |
| `mob_fusionparadox-main-link_a.dds` | `mob_timedeviant` | `272270088217192` | 256×256, 9 | `17c561f4c30392c4cee7cd2844b77ed10e41181cc0dd48cf959cbd1651c7c2f4` |
| `mob_fusionparadox-sub-link_b.dds` | `mob_timedeviantjelly` | `160962644483365` | 32×32, 6 | `19be2ef11284f53f28e06f129c9a373ca552311d596d6d9c5a7254e91198c504` |

The donor CAB is `CustomAssetBundle-Character_Texture_npc2` in `OG-academy/Character_Texture_npc2.resourceFile`, SHA-256 `55271aaca9357de437215771fec61fd9a7de6379376f4465af23c44d9e5d850d`. The same compressed mip hashes, decoded pixels and texture sampler are present in `beta-20100322/NpcTexture.resourceFile`, SHA-256 `bc4dc95e89898551532ef2c1a8aee00ec9f59daaaea8283104cd8f493f594f4a`, and recur in later checked Turner builds. The independent `default-jelly` material binds its own original `jelly` texture. The October 2026 Reborn public archive's texture indexes list only the secondary Time Deviant texture. Provenance and original payload exports are under `Logs/Studio/time-deviant-texture`. [Turner archive](https://archive.org/download/fusionfallbetabuilds/beta-20100322/) · [Reborn archive](https://archive.org/details/fusionfall-client-assets-extras)

Unity serialized both textures with their original compressed bytes, bilinear filtering, Repeat and anisotropy 1. The material verifier found one `_MainTex` pointer change on each targeted material and matched the saved prefab, XDT and independent `default-jelly` material to the pre-change hashes. With the rebuilt Windows Addressables catalog, the Windows replay loaded both 3254 and 3255 through NPC world entry. The captured first frame shows the blue and white body atlas and dark blue secondary layer. The shared prefab also passed movement, attack, death and clip callbacks in this replay. It exited with code zero and reproduced the existing replay UI teardown exception after the pass marker. The serialized-asset check, catalog and bundle fingerprints, player log, and frame capture are under `Logs/Studio/time-deviant-texture`.

## Native publication

The seven editable prefabs bind 11 XDT type IDs to stable `ffone/<prefab-guid>` addresses. The independent saved-asset review matched the recovered GLBs to 64 source clips, 92 native Animator states and 91 serialized events across the variants, including exact event timing and payload. It verified the prefab GUIDs, skinned renderers, controller references and per-owner Addressables entries. The saved XDT comparison matched all 1,292,738 pre-existing serialized nodes to the Git LFS baseline, accounting for the insertion of 106 new route nodes and the resulting end-index shifts. The authored XDT document SHA-256 is `9ba324b5e40a9cf1bb37f7eeab420eb5f80b7630d65a45a20e14a6992f364d23`. Publication and independent validation receipts are `published.json` and `reference/publication-review.json` in `Logs/Studio/xdt-model-recovery`.

## Windows playback

The packaged Unity 6000.4.0f1 Windows player passed all 11 XDT type lookups using replayed 0104 NPC entry, movement, attack and leave packets through `NetworkWorldEntities`, with death requested through the same world health-update path. It checked the idle loop on the first loaded model for every type, locomotion and finite attack and death where the donor supplied the respective clips, and actual animation callbacks. The seven first-frame captures visually show the expected donor bodies, including the pale authored material fallback on Fusion Paradox. The recovered types have no entries in the production NPC/mob placement tables or in the authored tutorial actor list, so the targeted test stages them through the regular world-replica handler. The harness disables the normal startup UI and logs one `CharacterCreationController.OnDestroy` exception on shutdown after the pass marker. `player-replay/receipt.json`, `player-replay/player.log` and the seven PNGs are in `Logs/Studio/xdt-model-recovery`.

On the same Windows build, the standard client smoke test passed both the creation/world and cold occupied-roster runs against a disposable loopback RustyFusion shard. It exercised character creation, tutorial entry, all ten Tutorial cinematics and ordinary overworld entry, with zero reported errors or missing animations. The client and package hashes are recorded in `Logs/Studio/xdt-model-recovery/standard-client/validation.json`. The Studio registries passed `python Tools/Studio/studio.py validate`.

## Unresolved build content

| XDT types | Model | Observed evidence |
| --- | --- | --- |
| 2506, 2508, 2509 | `npc_car3`, `npc_car2`, `npc_car4` | Exact XDT references in the 2010 table and Retrobution. No matching authored body identified in the inspected routes. |
| 3251–3253 | `mob_squidshark` | Exact atlas retained in the 2010 and later files. Body and clips unverified. |
| 3367 | `mob_ruinrhino` | Exact atlas retained. The 20 placement records in `gruntwork.json` point to map 0 cell `00_03`. Forty SHA-1-distinct archived bundles for that cell and its immediate neighbors yielded no matching serialized model. The production loader reads `NPCs.json` and `mobs.json`; those 20 records are only in the placement override file. |
| 3395 | `npc_chowder2` | Separate body key and `npc_chowder` XDT atlas key. The earlier `npc_chowder` body does not establish the updated model identity. |
| 3396 | `npc_jake` | Academy route binds `jake_stand_in`, an unskinned 29-vertex prop with one brief `stand1` clip. Retrobution's 256×256 character atlas differs from its 128×128 Academy prop atlas. |
| 3397 | `npc_mutantkevin` | Event atlas retained. The surviving Fusion Kevin is related to the later Young Kevin variant. Their rigs, texture assignments and clips are not established as equivalent. |
| 3398 | `npc_kiva` | Event atlas retained. No verified body and rig. |
| 3408 | `mob_birthdaybasher` | Retrobution event atlas retained. The event had a distinct character derived from Barrel Basher. |
| 3417 | `npc_exterminatortent` | XDT assigns the tent mesh and atlas to Shmingerbread House. The similar name in the Academy Jake material is bound to Jake's unrelated prop texture. |

The local route search checked 2,963 raw container paths across the Retrobution June and August 2026 builds, Academy, the local Turner January 2010 build and four 2019/2020 Retro builds. The Unity Web Player host cache matches the 978-file Reborn snapshot byte for byte. The public search used the Turner archive manifests and scoped bundle scans. In the [March 2025 Retrobution archive](https://archive.org/details/retro-20250303), 349 of 363 relevant bundle entries had matches by filename, size and ZIP CRC32 in the local builds. FusionForge inspected the remaining 14 verified payloads and 13 additional event/shared candidates. Twelve October 2026 Reborn archive model indexes supplied another 1,924 serialized route aliases for comparison. Negatives refer to the inspected payloads and name probes. The [Retrobution patch notes for June 2024](https://fusionfall.fandom.com/wiki/Portal:Retrobution/Patch_Notes) distinguish the updated Chowder, Jake, Young Kevin and Kiva assets. The release notes and retained atlases identify the intended assets, while the corresponding rigged event payloads still need a verified donor.
