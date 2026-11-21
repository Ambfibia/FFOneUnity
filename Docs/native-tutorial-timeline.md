# Native tutorial Timeline playback

The ten finite tutorial scenes play their authored `TimelineAsset` directly on a
`PlayableDirector`. `TutorialActionClip` creates a real
`ScriptPlayable<TutorialActionBehaviour>`. Each graph instance owns its command
flags, source sequence frame and captured origin; these never mutate an asset.

`TutorialCinematics` no longer constructs a montage `JObject` from the Timeline,
loads the legacy cinematic JSON as a fallback, or accumulates a separate elapsed
clock/action/audio cursor. Elapsed time comes from the native graph. The director
uses manual updates so the existing presentation gate and blocked/paused gameplay
can hold the first prepared frame without advancing the native clock.

The scene script mixer combines the command tracks in start-time/source-order
order. Commands crossed by a long frame still see camera/movement state at their
own timestamps. Active sequences use native clip-local time; intermediate and
terminal samples preserve the original stepped fades, shakes, typewriter and
movement formulas. Resizing a clip scales that sequence's source time without
rewriting its payload. Unity's Timeline compiler excludes muted tracks/groups.
Other native Timeline output types retain their outputs.

Gameplay commands still use the existing expression/actor/effect adapters. Their
small action documents are read directly from serialized nodes when the graph is
built, without serializing JSON strings or parsing them again. The montage
`Definition()` exporter exists only in the editor for inspection and source
contract checks.

Skip stops the graph, suppresses further evaluation, and executes the original
scene-specific/common cleanup, loop ownership, final fade and bars. Restart
creates fresh clip state. Gameplay spawn/capture commands are forward-only;
restart the scene before seeking backwards through them. Playable callback failures are rethrown at
the controller update boundary so its existing error cleanup still runs.
Persistent world scenes
and their collision meshes are independent of this graph.

Validation evidence is recorded under `Logs/NativeTutorialTimeline`.
