# Native Unity collision contract

Recorded on 2026-11-04 for the collision, jump and tutorial repair.

The 170 authored world scenes own their physics. Their 59,635 standard Unity
MeshCollider components use 6,444 scene-embedded Unity meshes. No runtime mesh
conversion is needed. Complex rocks, bridges, openings and invisible boundaries
retain the accepted triangle surfaces; they must not be replaced by enclosing
boxes or convex hulls, which would change the traversable routes.

Large faces that exceed PhysX's 500-unit edge guidance are bisected in the editor
to a maximum edge of 256 units. Midpoints stay on the original edges and surfaces;
winding, boundary and surface area remain unchanged. The scene refinement pass
also saves, reopens and verifies every affected mesh. This avoids oversized-face
cooking warnings without replacing the authored surface with an approximation.

The former representation used 59,465 placed collision meshes and 170 terrain
surfaces. Most placed meshes still used render-oriented triangle winding. Only
the tutorial's 441 placements had been published to separate `Collision/Cooked`
assets. The native scene migration restored winding on 59,017 placements. Water
retains its upward front face; terrain retains its existing Unity winding.

For provenance, keep the accepted `Imported/Content.asset` manifests and source
documents. They describe the former meshes and placement identities. Active
authored scene MeshColliders reference their embedded meshes instead. Collision
bindings, positions, rotations, scales, enabled state and trigger/convex flags
are preserved. Do not associate a collider with a visual object by proximity.

WorldStreamer requires an authored scene address and does not reconstruct missing
cells from legacy collision meshes. The moving downtown bus also owns its standard
MeshCollider in the visual prefab: its 134-vertex, 531-index shape is embedded as a
prefab subasset under the original collision node. NetworkWorldSlider no longer
loads a separate collision GLB. `FFOne/World/Rebuild Native Slider Collider` retains
that source shape and verifies the saved prefab; the old source remains provenance.

`FFOne/World/Rebuild Native Scene Colliders` is the editor migration command.
Run outside Play Mode, with cell scenes closed. It saves each scene, closes it,
reopens it, and verifies geometry hashes, transforms and component flags.
`Logs/NativeSceneCollision/Before` contains the pre-migration scene copies;
per-cell JSON reports record previous mesh paths, source node identities,
winding decisions and SHA-256 geometry hashes. `result.txt` records the aggregate
result. These local reports are evidence, not runtime dependencies.

The original OwnUser gameplay capsule is independent of avatar appearance:
height 1.6, radius 0.3, centre Y 0.8, skin 0.03, step offset 0.3, slope limit 50.
Use `NativePlayerMotor` in both tutorial and overworld. Retain PhysX Below contact
on narrow edges, supplement it with a downward support probe, reject zero-distance
overlap normals, and reset contact when teleporting. Jump control follows
`cnAvatarThirdPersonMove.ExecuteJump/UpdateAirControl`: initial multiplier 1.45,
normal gravity 10 during the first 0.5 seconds while held, then gravity 20; an
early release cuts upward velocity above 3 to one quarter. Vehicle jumps keep
their separate behavior.

Behavior evidence was inspected through FusionForge against immutable
`builds/retrobution-20260821/main.unity3d` (SHA-256
`01b544976b2d54355507cf30fe6dfada2b476b92b209a3d47c1499669ed9b4ef`).
Accepted FFOneClient character identities and appearance variants remain the
appearance reference.

Regression coverage includes the saved tutorial ledge route, bridge NPC support,
near-plane camera clearance, short/held jumps at 10/30/60/120 fps, descending
slopes, teleport contact reset, and Russian tutorial instructions. The actual
client validator also measures tap/held jump height through InputSystem and the
production tutorial motor.

Repeat the standalone validation with the isolated local server running:
`python Tools/run_resource_loading_validation.py --output Logs/NativeSceneCollision/ClientVerified --tutorial-cinematics --overworld --native-screen`.
It exercises creation, the first rendered scene frames, all ten tutorial
cinematics, HUD, InputSystem movement and jump/landing, ordinary world entry,
then an occupied roster in a fresh process. `ClientVerified/validation.json`
records the executable assembly/catalog hashes and per-phase results; captures
come from the full player framebuffer, including IMGUI subtitles and hints.
