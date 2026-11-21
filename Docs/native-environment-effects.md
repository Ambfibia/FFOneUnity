# Native environment particles and motion

Ordinary world and tutorial emitters use Unity ParticleSystem components and
ParticleSystemRenderer. Unity owns particle storage, lifetime, force integration,
size curves, bounds and rendering. One shared unit quad supplies the source
geometry. Emitters no longer rebuild a Mesh every frame.

The source controller still admits seeded bursts, advances ScriptKeys and applies
range tests. World emitters share the original 2048-particle budget across resident
cells. Expired particles stop rendering immediately, while their budget is released
at most 32 entries per frame. The original persistent-particle exception and stable
near-to-far admission order remain. Tutorial/combat emitters retain their original
separate admission behavior.

Manual Simulate uses the source frame step. Time.maximumParticleDeltaTime is
raised only for the synchronous call, then restored in finally. Otherwise Unity
subdivides long frames and changes the accepted semi-implicit force trajectory.
Camera-facing, fixed and upright bases and absolute roll curves retain their source
orientation. The particle shaders read native AgePercent to select source atlas
frames and five-color palettes, including the world palette's 16 rounded samples.
The wound projectile ribbon (generation 12) uses LineRenderer topology and GPU
deformation; see native-wound-trail.md for its source history, twist and fade contract.

WorldCellPublication publishes TRS channels, platform paths and world spins as
editable AnimationClip assets under Content/Environment/Animation/Imported.
WorldCellDefinition.Motion holds direct references. Addressables gives these clips
one shared owner. Publication reads editable NativeData documents, preserves curve
tangents and source coordinate reflection, and never opens or saves authored scenes.
Unchanged source retains authored edits to a published clip. Changed source receives
a new source-hashed clip and updates the definition binding.

The runtime samples these clips through Unity. It retains source clocks, visibility
pauses, per-channel infinity periods and world-time platform phases. Platform path
corners and the source spin discontinuity at turnaround are preserved. Material
float curves and camera-dependent billboards keep their independent source controls.
Authored MeshCollider geometry and its bindings are untouched.

NativeEnvironmentEffectsTests compare Unity output with source Hermite samples,
all 11 platform modes, native particle geometry and shared admission/expiry behavior.
The real account recovery scenario records native-environment.json at world entry
alongside first-frame, character creation, occupied roster and collision evidence.
