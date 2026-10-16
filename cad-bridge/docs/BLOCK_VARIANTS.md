# Independent dynamic block variants — local 2026 prototype

`dwg_create_block_variant` and `dwg_update_block_angles` are typed operations in
the `modify` profile. They do not accept C#, LISP, commands or arbitrary property
setters, and never save a drawing. Source and loaded runtime are distinct.

Creation requires the active titled DWG's full path/fingerprint, a complete
source reference/attribute/writable-angle snapshot from `dwg_inspect_block`,
and explicit snapshots of each edited direct definition entity. The reader's
definition geometry is local, while insertion/reference attributes use WCS.
The reference layer is obtained with `dwg_get_entity_properties`.

Supported variant scope: one independent named dynamic definition with up to500
direct entities,1..32 open planar +Z straight zero-width two-vertex polylines,
0..32 single-line attribute defaults,0..8 field-free MTexts and32 attached
single-line attributes. Source reference must have +Z normal and unit scale.
Edited handles must belong to its effective defining BTR. Name collisions,
external/layout/anonymous source definitions, source attribute fields and
changed snapshots are rejected. `dry_run=true` preflights without setters.
There is a cooperative15second deadline, not interruption of blocked native calls.

The entire defining BTR is cloned by `WblockCloneObjects` to an in-memory
database, renamed/edited there, then cloned back; `Wblock(blockId)` is not used.
Native IdMapping relationships preserve owned graph links. Inserted reference
must still be dynamic, have the same named writable angular properties, retain
requested raw values and all attributes. Correct grip/action behavior is not
proven by these checks and must be verified on the new prototype.

Native clone APIs may commit internal state independently of the outer
transaction. Failure handling attempts compensating cleanup limited to the
new reserved name and mapped newly cloned IDs. Cleanup failures are reported.
Do not claim guaranteed atomic rollback or retry uncertain timed-out mutations.
Do not erase unrelated definitions or purge a drawing to clean up a test.

Angle updates require exact expected effective name and complete named writable
angular values; values are **radians**, with native allowed-value and readback
checks. Optional binding additionally requires exact current attribute text.
Binding persists on the independent definition and applies to all its references.
Replacing it requires `expected_binding` equal to the entire current persisted
specification, with the same attribute tag. The owned Xrecord is edited in place.

The binding is a **plugin calculation**, not a native portable AutoCAD Field.
There was no field in the source, and public managed API does not provide a
verified mapping from a named property to the undocumented Field Parameter(n).
Do not infer that index from the collection position. The plugin must be loaded
for subsequent grip/command/Undo/Redo updates.

`dimension_display_degrees` reads exactly one supported direct angular Dimension
from **reference.BlockTableRecord**, the CURRENT evaluated definition. The
selector is semantic and re-resolved on every refresh; no anonymous dimension
handle is cached. `Dimension.Measurement` and native `FormatMeasurement` supply
the value, rounding, units and separators used by the annotation. A transient,
disposed non-DB MText removes formatting. The numeric legacy attribute strips
only the terminal degree symbol; decimal comma/dot/zeros are preserved exactly.
No independent Math.Round or angular-parameter formula is applied.

Supported native numeric display requires decimal degrees, no fields, literal
text override, tolerances/limits or multiple numeric values. Missing/multiple
angular dimensions, limits, truncated text and unsupported formatting refuse the
operation instead of selecting a guessed angle. Native mode accepts no custom
property names or precision. Inspection exposes `evaluated_angular_dimensions`
with actual handles/geometry/measurement/rawoverride/nativeformattedtext and
completeness under the existing bounds; no RecomputeDimensionBlock, layout
generation or Field.Evaluate is called.

Legacy `property_degrees` formats one named property; `deflection_between_arms` computes
180° minus the smaller angle between two named angular arm directions. The
choice requires a live behavior check; equal initial90° values prove nothing.
Text uses invariant numeric formatting and0..6 decimal places without a degree
symbol only in these legacy modes. They do NOT guarantee equality with the
dimension's selected angular quadrant or display precision. The technical
attribute tag is unchanged.

Command-ended/cancelled/failed callbacks start a bounded refresh episode and
attempt a transactional pass before returning. Idle performs subsequent passes
only for the active, quiescent, writable document. Database ObjectModified
callbacks only queue watched bound references/current evaluated angular dimensions;
they never open, evaluate or write objects. The retained database wrapper is
unsubscribed when its document closes. Only explicitly owned attribute/parent
writes are suppressed; a dimension event during lock release remains pending.

Each pass preflights before writes and reads the current annotation again.
Completion occurs after releasing the document lock and requires two identical
read-only observations on separate callbacks. Raw angles, current evaluated
definition/dimension identities and native measurement/display are included in a
bounded SHA256 signature. A write or late dimension event resets stability.
Six passes and 2000ms of active processing bound an episode; idle intervals are
not charged. One pass has a cooperative500ms deadline,10000 definitions,
1000 references and256KiB snapshot bound. These do not interrupt blocked native
calls. Native dirty notifications cannot continually renew an exhausted budget;
a new external command starts a fresh episode. Exhaustion is reported.

After changing an attribute, only its actual parent BlockReference is write-opened
and RecordGraphicsModified(true) is called. This supplies AutoCAD's documented
graphics update signal, not a guaranteed Properties-palette invalidation.
Selection and dynamic angles/geometry are not assigned for this notification.
Only affected attribute/parent Undo recording is temporarily disabled; database
Undo is never disabled, and UpgradeOpen can already begin native Undo filing.
Live Undo/Redo, immediate Properties refresh, COPY and reopen must be checked
separately; compilation/offline temporal tests do not establish these behaviors.

Prototype530/710: source426/560, local arms1100→1200mm and crossbars560→710mm,
sort number95, original tag `МАССА_ЕД._КГ` storing bend angle, initial90°.
User testing found attribute/annotation mismatch. Correction switches this
prototype to native dimension display by guarded replacement of its old binding.
The native display binding has been installed and guarded replacement performed
on20F76. On2026-10-14 the user reported the attribute showed the previous angle
after a grip, while a subsequent native read obtained84,2 both from attribute
and annotation. This does not establish the exact UI/cache/event cause.
The deferred/parent-notification correction compiles and offline tests pass;
that correction is not yet loaded or grip-verified. All four original variants
remain outside the write scope. One explicit user-authorized save preceded the
previous reload; the later binding replacement and user tests remain unsaved.
