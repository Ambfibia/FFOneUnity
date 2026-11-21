# Native VFX publication

`NativeEffectPublication.RebuildAll` compiles the editable `NativeDataDocument`
closures into `NativeEffectDefinition`, `NativeWorldParticleDefinition` and
`NativeWorldEffectLibrary` assets under `Assets/FFOne/Content/VFX/Published`.
It runs before world and Addressables publication in `ContentLayoutSetup`.
After saving definitions it removes unreferenced generated materials and textures,
so changes to source data do not accumulate obsolete GPU assets.

Only the editor decodes source texture bytes, translates source shader state and
builds immutable materials. Player definitions contain ordinary Unity texture,
material and prefab references, typed particle settings, keys and curves. The
source closures remain editable provenance; they have no player address. Cell
libraries contain controller metadata with `effectPrefabClosures` removed.

Effect instances share published textures and immutable material templates.
`TutorialEffectPlayback` clones only mesh materials with animated properties;
these mutable instances are disposed with the playback. Wound trails keep their
own history buffer and renderer property block, while sharing a material with
the shader variant enabled during publication. Disposing a playback never
releases a shared published asset. World particle accounting and the global
2,048-particle budget retain the existing admission and expiry rules.

`NativeEffectLoading.PrepareWorldAsync` prepares combat, buff, NPC and vehicle
definitions before entering the world. Tutorial preparation loads its published
definitions asynchronously. Runtime spawn does not parse closure JSON or shader
source and does not create textures or immutable materials.

The publisher reads the accepted Unity data tables rather than enumerating only
retained JSON files, so the separately published buff closures are included.
Texture formats, mip chains, sampler settings, tint, blend, culling, depth state,
UV transforms, source curves, controller keys and particle identities are
preserved. Authored scene colliders are independent of this publication.

Validation evidence is recorded in `Logs/NativeVfxPublication`.

Verified on 2026-11-05:

- 67 editor checks passed, including published references, accepted texture pixels,
  buff content, particle budgets, world bindings and native wound trails.
- The standalone Vulkan player passed 270 wound-trail raster comparisons.
- Creation, all ten tutorial scenes, ordinary world entry and six asynchronous
  HNPC appearances passed in 185.124 seconds. A separate cold occupied roster
  passed in 10.142 seconds. Neither run recorded a synchronous VFX-definition/closure load or a blocking
  HNPC/occupied-portrait load. The full flow still has 17 waits in other content
  paths; the cold roster has none.
- World evidence recorded 136 ParticleSystems, 508 live particles, 137 native
  AnimationClips and zero resident legacy closure documents.
- All 170 authored scene hashes are unchanged. Migration validation preserved
  all 176,229 original asset GUIDs. All 1,207 published VFX assets are reachable;
  there are no unused generated resources in the publication.

See `Logs/NativeVfxPublication/FinalClient/validation.json`,
`FinalClient/native-environment.json`, `ClientProbe/result.json`, `tests-2.xml`,
`published-assets.json`, `scenes-validation.json` and `build-2.log` for evidence.
