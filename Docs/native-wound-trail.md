# Native wound projectile trail

Generation 12 projectile ribbons use LineRenderer and a vertex shader. Production
playback no longer creates a Mesh, vertex/UV/color/index lists, or uploads rebuilt
triangle geometry each frame. The native renderer builds its fixed strip once.

The controller retains the accepted history: 16 or 32 points from
numberPerGeneration, quarter-step interpolation between the previous and current
head position, and one final current-position sample per rendered frame. The
legacy trailLength=30 field does not replace the accepted effective point count.
A GraphicsBuffer uploads only this world-space centerline. Every instance owns and
disposes its buffer; its property block keeps shared materials independent.

The shader applies the exact source ribbon rule: camera-right rotated around the
current head direction by 360 degrees along the history plus 720 degrees per
second. A stationary head uses camera-right without rotation. Both rails meet on
the positive edge at the tip. The source UV range and the intensity/alpha ramp
min(index,count-2)/(count-1) retain the original end behavior. The particle shader's
texture decoding, tint, blend, fog and fragment stage are unchanged.

LineRenderer's first rail has V=1. Reversing the history parameter preserves the
source diagonal through each non-planar segment. Custom world bounds enclose the
history plus the full radial width, independently of the parent transform.

NativeWoundTrailTests compare GPU-rendered native ribbons with the former source
mesh for all nine accepted projectile closures: ES15, 321, 391, 747, 788, 789, 797,
798 and 808. Both tutorial and world/fog material paths receive fifteen observations per effect
covering travel, bends, stopping,
transformed parents, scale and camera orientation. The reference mesh exists only
in editor/development verification code.

A development player launched with --native-wound-trail-smoke repeats those 270
image comparisons using packaged Addressables and writes paired native/reference
images plus result.json to FFONE_WOUND_TRAIL_REPORT. The normal account recovery
scenario also asserts that actual tutorial combat creates native projectile trails.
