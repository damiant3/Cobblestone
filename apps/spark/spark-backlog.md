# Spark -- open capabilities

App-domain backlog. There is no platform-wide register any more:
`docs/PM/BACKLOG.md` was deleted 2026-07-23 and must not be recreated.
`docs/PM/CurrentPlan.md` carries the shape and the priority order for
the platform. Anything that is this application's own behaviour lives
here.

The rules are the same ones: an entry says what is still missing and
nothing else, a closed entry is DELETED rather than annotated, and a
gap that is still real is never quietly dropped.

| # | Capability | State of the gap |
|---|---|---|
| SPARK-2 | **MeshBoolean classifies coincident faces** | `FaceBoundary` is declared at `MeshBoolean.codex:17` and never constructed anywhere in the tree; `csg-point-inside` answers only FaceInside/FaceOutside from ray parity, which is arbitrary for a centroid lying exactly on the tool's surface, and `csg-keep-a`/`csg-keep-b` have no FaceBoundary arm. Coincident faces are the entire content of a self-boolean, so all three operations are undefined on them. Measured: self-union gives 24 verts where A union A = A is 36; self-difference gives 36 where A minus A is 0. `.expected` pins the correct answers and `spark-boolean-test` stays skipped until the code produces them. The work is coincident-face classification plus a FaceBoundary arm in both keep functions: union and intersect keep A's copy and drop B's, difference drops both. |
| SPARK-3 | **SparkServer's network identity is hardcoded to QEMU's** | Found 2026-08-07 while collapsing SPARK-1: `SparkServer.codex:148` pins the MAC to `[82, 84, 0, 18, 52, 99]` (52:54:00:12:34:63, QEMU's default), where `Net--WebServer`'s `local-mac` is `net-driver-mac` and asks the driver. That is why the two could not be collapsed and the app's four constants were prefixed `spark-*` instead. On real hardware the server therefore announces a MAC the NIC does not have. The IP triple (`10.0.2.15`, gateway `10.0.2.2`, host `127.0.0.1`) is QEMU's user-mode slirp network too. Unmeasured on metal -- nothing has flown SparkServer on a real NIC, so the failure mode is inferred from the constant, not observed. The work is deciding whether the app should take `net-driver-mac` and a configured address rather than QEMU's, which is a behaviour change and wants a ruling before it is made. |
