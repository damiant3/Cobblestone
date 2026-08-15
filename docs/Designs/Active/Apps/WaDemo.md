# WaDemo: the Washington population cube

*A schema for census age-sex data, the demographer's toolkit over it, and the
site that flips the bits.*

**Status: DESIGN. Nothing built.** Opened 2026-08-14 (blu) at Damian's
direction, on the extract that landed in `apps/wademo/`.

---

## 1. What is actually in the box, measured

Two files, 0.5 MB. `nhgis0017_ts_nominal_county.csv` and its codebook, from
IPUMS NHGIS extract 0017.

| | |
|---|---|
| table | A61, Persons by Sex [2] by Detailed Age [103] |
| extent | Washington, all 39 counties, none truncated |
| years | 1980, 1990, 2000, 2010, 2020 |
| geography | county, **nominal** integration |
| cells | 39 x 5 x 2 x 103 = **40,170** |
| blanks | zero, in every year |

**The age detail is single years, and that is the whole reason this dataset is
interesting.** 103 bins: under 1, then every single year 1 through 99, then
100-104, 105-109, 110 and over. Most published age-sex tables are five-year
bands. Single years are what make variable-width regrouping a real operation
rather than a relabelling, and they are what make the heaping and smoothing
tools in section 4 applicable at all.

**It reconciles.** Summed to state level the five years give 4,132,156 /
4,866,692 / 5,894,121 / 6,724,540 / 7,705,281, which are the published
decennial counts for Washington exactly. Median age computes to 29.8 in 1980
and 38.1 in 2020, both of which match the published figures. The extract is
sound and we can build on it without re-sourcing.

## 2. Three data-quality facts that the schema has to carry, not footnote

Each of these was measured here, not assumed. Each one changes what a chart is
allowed to claim, so each one belongs in the data model where a renderer can
see it. A caveat in a README is an assertion with no runner.

### 2.1 Age heaping is NOT a problem, and that was worth checking

Whipple's index, ages 23 to 62, both sexes, statewide:

| year | Whipple | UN verdict |
|---|---|---|
| 1980 | 99.9 | highly accurate |
| 1990 | 104.0 | highly accurate |
| 2000 | 102.2 | highly accurate |
| 2010 | 102.2 | highly accurate |
| 2020 | 106.3 | fairly accurate |

Single-year-of-age data from developing-country censuses routinely lands at 150
and above, where every age ending in 0 or 5 is inflated by respondents rounding
their own age. Modern US census data does not, because age is derived from a
reported date of birth. **So the graduation and smoothing machinery that
single-year data usually demands is not load-bearing here.** It stays in the
toolkit as an instrument to point at the data rather than as a correction the
pipeline must apply, which is a much better place for it.

### 2.2 The 2020 county cells are NOISY BY CONSTRUCTION, and this one bites

The 2020 census applied the TopDown disclosure-avoidance algorithm, which
injects calibrated noise. At a fine cross-tabulation in a small county the
noise is a large fraction of the cell. Measured on Garfield County, population
2,286, males aged 20 to 44:

| year | mean cell | mean absolute second difference | ratio |
|---|---|---|---|
| 2010 | 9.8 | 5.04 | 0.51 |
| 2020 | 11.0 | 9.65 | **0.88** |

The 2020 series runs `26 8 6 4 7 10 9 8 12 14 5 4 8 19 11 9 12 20 2 7 17 4 14
13 25`. A spike of 26 at age 20 and 25 at age 44 with a trough of 2 at age 38,
in a county of 2,286 people, is not a demographic feature. It is noise, and the
slight rise in the 2020 Whipple index above is the same thing showing up at
state level.

**Consequence for the design: a single-year pyramid for a small county in 2020
is a picture of the disclosure-avoidance algorithm.** The app must not draw it
without saying so. The schema carries a reliability floor per cell and the
renderer refuses or warns below it. See section 3.4.

### 2.3 Nominal integration, and why it happens to be fine HERE

NHGIS warns that nominally integrated units are matched by name and code with
boundary changes disregarded, so one code can mean different areas at different
times. That warning is serious for tracts and places. **For Washington counties
over 1980 to 2020 it is believed inert: no county boundary in the state has
changed since Pend Oreille was created in 1911.** That belief is what makes the
cohort arithmetic in section 4.3 valid at county level, so it is worth
confirming against a boundary file rather than carrying on my say-so, and the
schema records the integration mode either way so the claim stays visible.

### 2.4 Redistribution is restricted, and it constrains where the site can live

NHGIS terms: "You will not redistribute the data without permission." The
extract is therefore in the depot and **excluded from the public mirror via
`.gitignore`**. A private or internal build of the site is unaffected. A public
one is redistribution and needs either their permission, which they say they
will consider granting for free redistribution, or a re-sourcing of the same
counts from the Census Bureau directly, where the underlying decennial tables
are public domain and carry no such term. The second is the clean answer if
this ever goes public and it is cheap, because we already know exactly which
table to ask for.

## 3. The schema, as tables in `apps/data`

The data lives in the engine's own disk format: slotted pages, heap storage,
B-tree indexes, WAL, queried through `RelAlgebra`. It is a **star schema**, five
dimensions around one fact.

### 3.0 What the query surface allows, because it decides the schema

Read before designing, and it changed two decisions. `RelAlgebra.codex`:

- **`RelGroup` groups by column NAMES only.** There are no computed group keys.
- **`RelDerive` is a closed enumeration of four date bucketings**, and the
  chapter argues the position explicitly: "a bucket is a governed vocabulary
  word, not an escape hatch."
- **`PredColCmp` compares a column to a CONSTANT.** No column-to-column
  predicate. `RelEquiJoin` joins on name lists, equality only.
- **`ColumnType` is `ColInteger | ColText | ColBoolean`.** No float. Every rate
  is fixed-point.

The first two together mean **age banding cannot be a `GROUP BY age/5`**. The
tempting move is to extend `DeriveFn` with `DerAgeBand5` and friends, and it is
the wrong move: it would make every banding a language change, and Damian's ask
was *variable* ranges chosen at the UI. The right move is in 3.3, and it needs
no new operators at all.

### 3.1 The tables

```
dim_county (39)
  county_id    Integer  PK
  gisjoin      Text            -- G5300010, the NHGIS join code
  fips         Text            -- 53001
  name         Text            -- Adams County
  centroid_x   Integer         -- scaled lon, feeds SpatialIndex
  centroid_y   Integer         -- scaled lat

dim_year (5 here, ~45 in Lucky's annual series)
  year         Integer  PK
  source       Text
  provenance   Integer         -- Enumerated | Estimated | Projected | NoiseInjected

dim_sex (2)
  sex_id       Integer  PK     -- 0 male, 1 female
  label        Text

dim_age (103)
  age_id       Integer  PK     -- 0..102, source order
  age_lo       Integer
  age_hi       Integer         -- -1 means open-ended
  age_width    Integer         -- 0 where open
  label        Text
  nhgis_code   Text            -- AA..HX, provenance kept

dim_band (n bandings x 103)
  banding_id   Integer
  banding_name Text
  age_id       Integer         -- FK to dim_age
  band_label   Text
  band_lo      Integer
  band_hi      Integer
  band_order   Integer
  PK (banding_id, age_id)

fact_population (40,170)
  county_id    Integer
  year         Integer
  sex_id       Integer
  age_id       Integer
  birth_year   Integer         -- year - age_lo
  cohort_valid Boolean         -- False for the three multi-year top bins
  persons      Integer
  PK (county_id, year, sex_id, age_id)
```

Indexes, each earning its keep against a named query:

| index | serves |
|---|---|
| `pk_fact` unique on `(county_id, year, sex_id, age_id)` | the pyramid, as a **prefix range scan**, no filter pass |
| `ix_fact_cohort` on `(county_id, sex_id, birth_year)` | the cohort join in 4.3 |
| `ix_fact_year_age` on `(year, age_id)` | cross-county age queries, the choropleth |
| `ix_band` on `(banding_id, age_id)` | the banding join |

### 3.2 Why `dim_age` carries an interval and not just an index

Bins 100 to 102 are `100-104`, `105-109` and `110+`: five years, five years and
open. Modelling age as a plain 0-to-102 index makes every per-year-of-age rate,
every median and every survival ratio silently wrong at the top of the
distribution, which is exactly where the interesting mortality is. So the bin
carries its own width, an open bin says so, and **every measure that divides by
width has to look.**

`cohort_valid` is the same discipline for joins. The three top bins have no
single birth year, so they are marked and the cohort queries filter them out
before joining rather than matching a sentinel to itself and quietly producing a
ratio between two different things.

### 3.3 Variable-width banding IS A JOIN, and that is the whole trick

`dim_band` holds one row per (banding, source age bin). Regrouping is then an
equi-join and a group-by over columns that already exist:

```codex
RelScan "fact_population"
  |> RelFilter (PredAnd (PredColCmp "county_id" CmpEq (ValInteger 33))
                        (PredColCmp "year"      CmpEq (ValInteger 2020)))
  |> RelEquiJoin (RelScan "dim_band"
                    |> RelFilter (PredColCmp "banding_id" CmpEq (ValInteger 2)))
                 ["age_id"] ["age_id"]
  |> RelGroup ["sex_id", "band_label"] [AggSpec { agg-func = AggSum "persons",
                                                  agg-alias = "persons" }]
  |> RelSort [SortSpec { sort-col = "band_order", sort-dir = SortAsc }]
  |> execute cat
```

**No new operators, no expression language, and a user-defined banding is a row
insert rather than a code change.** The UI's banding slider writes a `dim_band`
partition and re-runs the same plan. It also lands squarely on `HashJoin` and
`SortMerge`, which is engine we wanted exercised anyway.

Shipped bandings, all just partitions of `dim_band`:

| banding | bins |
|---|---|
| `single` | the source grain, 0,1,2 ... 99, 100-104, 105-109, 110+ |
| `quinquennial` | 0-4, 5-9 ... 80-84, 85+, the standard pyramid |
| `decennial` | 0-9, 10-19 ... 80-89, 90+ |
| `functional` | 0-4, 5-17, 18-24, 25-44, 45-64, 65-84, 85+ |
| `generational` | by birth year, so its rows differ per `year` |
| `custom-N` | whatever breaks the user drags |

**The rule that keeps it honest: a band boundary must fall on a source
boundary.** Above age 100 the source is already five-year, so a request for
single years there is refused rather than interpolated. In this schema that
refusal is free, because there is simply no `dim_band` row that could express
it: the grain is the join key. A constraint enforced by the shape of the data
does not need anybody to remember it.

### 3.4 Reliability travels with the row, and it is an RLS policy

`dim_year.is_noised` marks 2020. The small-cell rule from 2.2 is not a renderer
convention, it is a **row-level security policy in `Security.codex`**:

```codex
RowPolicy { rp-table = "fact_population",
            rp-name  = "small-cell",
            rp-predicate = PredColCmp "persons" CmpGe (ValInteger 5),
            rp-applies-to = AuthReadOnly }
```

A read-only session simply cannot see cells below the floor, so the site cannot
draw them by accident and an export cannot leak them. **Disclosure avoidance
implemented as the engine's actual access control** is a better answer than a
warning banner, and it is a genuine exercise of a module that usually only gets
a demo. The authoring role sees everything, which is what the aggregate queries
need.

The renderer still owes the reader an explanation rather than a silent hole: a
panel whose inputs crossed the floor says so, and offers the next coarser
banding as a one-click fix.

## 4. The demographer's toolkit

Everything below is a pure function over a `Cube` slice. Grouped by how many
populations it takes, because that is what determines where it can appear in
the UI.

### 4.1 One population, one moment: structure

| measure | notes |
|---|---|
| median age, mean age | the median needs bin widths, see 3.2 |
| sex ratio | males per 100 females, overall and age-specific |
| youth / old-age / total dependency ratio | 0-14 and 65+ over 15-64 |
| aging index | 65+ per 100 under 15 |
| **child-woman ratio** | children 0-4 per 1,000 women 15-49. A fertility proxy computable **from census counts alone**, which matters because we have no vital statistics here at all |
| proportion in band | any banding, the pyramid's own numbers |
| **Whipple, Myers' blended, Bachi** | heaping indices. Only meaningful on single-year data, which is why they belong here and not in most census apps |
| **UN Age-Sex Accuracy Index** | one number for "how much do I trust this pyramid" |
| Gini and entropy of the age distribution | concentration, useful for ranking counties |

### 4.2 Two populations, one moment: comparison

| measure | notes |
|---|---|
| **index of dissimilarity** | the single number for "how different are these two pyramids". Percent of one population that would have to change age for the two to match |
| **direct age standardization** | compare two counties with the age structure held constant. The tool that stops "county A is older" being mistaken for "county A is sicker" |
| indirect standardization, SMR-style | when one side's rates are thin |
| difference pyramid | bars are A minus B, diverging colour |
| location quotient by band | which counties are over-represented in 20-34, and by how much |

### 4.3 Two moments, same population: change, and this is where the good stuff is

The extract is **exactly decade-spaced with single-year ages**, which is the
precondition for cohort methods. This is the part that turns a pretty chart
into something that says something.

| measure | notes |
|---|---|
| **Cohort Change Ratio (CCR)** | `CCR(a) = P(a+10, t+10) / P(a, t)`. The people aged 20 in 1980 ARE the people aged 30 in 1990. The ratio is survival times net migration, and nothing else |
| **census survival ratio net migration** | divide the county CCR by the state or national CCR for the same cohort. Survival cancels; **what is left is net migration**, which is Damian's "show migration", and it needs no vital statistics |
| **Hamilton-Perry projection** | project forward from CCRs alone. The standard small-area method precisely because it needs only two censuses and no fertility or mortality schedule. Gives the animation somewhere to go past 2020 |
| growth rates | geometric and exponential, annualized, overall and age-specific |
| **Lexis surface** | age against time as a heat map, cohorts running as diagonals. The honest way to show all five censuses at once |
| aging-in-place vs migration decomposition | how much of a county getting older is its own residents aging, and how much is the young leaving |

**The cohort insight is the thing to build the site around.** A pyramid
animation that just morphs between five shapes shows change. A pyramid
animation where each bar *slides diagonally upward* into its own future bar
shows the mechanism, and the gap between where a cohort should be under state
average survival and where it actually is *is* the migration signal, rendered
as a residual on the same chart.

### 4.3.1 The demographic method IS an equi-join on birth year

This is the part that makes the whole thing worth doing in our own engine rather
than in a spreadsheet.

Ages are single years and the censuses are exactly ten years apart, so a person
aged 20 in 1980 is aged 30 in 1990, and `1980 - 20` and `1990 - 30` are both
1960. **Birth year is a natural join key that the cohort shares across every
census.** Store it on the fact and the cohort change ratio is not an algorithm,
it is a query:

```codex
RelEquiJoin
  (RelScan "fact_population"
     |> RelFilter (PredAnd (PredColCmp "year" CmpEq (ValInteger 1980))
                           (PredColCmp "cohort_valid" CmpEq (ValBoolean True))))
  (RelScan "fact_population"
     |> RelFilter (PredAnd (PredColCmp "year" CmpEq (ValInteger 1990))
                           (PredColCmp "cohort_valid" CmpEq (ValBoolean True))))
  ["county_id", "sex_id", "birth_year"]
  ["county_id", "sex_id", "birth_year"]
  |> execute cat
```

One join, and every row of the result is a cohort observed twice. `persons`
from the right over `persons` from the left is the CCR. Do the same against the
state-level rollup, divide, and survival cancels: **what is left is net
migration**, per county, per sex, per single year of birth, with no vital
statistics anywhere in the pipeline.

Two honesty notes that the schema enforces rather than documents. The census
reference date is April 1, so birth year is approximate by up to a year, but it
is approximate *identically* at both ends of a ten-year gap, so the ratio is
unaffected. And `cohort_valid` keeps the three multi-year top bins out, because
`110+` in 1980 and `110+` in 1990 share a sentinel and are not a cohort.

Results are stored back as a derived table, since the site queries them far
more often than it rebuilds them:

```
fact_cohort
  county_id, sex_id, birth_year, year_from, year_to
  pop_from, pop_to
  ccr_ppm        Integer      -- CCR x 1,000,000; ColumnType has no float
  state_ccr_ppm  Integer
  net_mig_ppm    Integer      -- ccr / state_ccr, the migration signal
```

## 5. Loading it, and three things in the way

The extract is **wide**: 1,037 columns, 7 context and 1,030 data, one row per
county. The fact table is long. So the load is a pivot, 40 source rows into
40,170 fact rows, and it cannot go through `bulk-import-lines`.

1. **`TableDef.col-count` is `Integer between 0 and 256`.** A 1,037-column table
   is not expressible, by construction. This is a constraint rather than a
   defect, and it points the right way: the wide form was never the schema.
2. **`BulkLoader` has no quoted-field handling.** Measured: all seven context
   fields in this extract are quoted (`"G5300010"`, `"Adams County"`, `"001"`)
   and the 1,030 numeric fields are bare. There are no embedded commas, so
   nothing misparses, but every Text value would arrive carrying literal quote
   characters and `fips` would join against nothing. A leading-quote field would
   also read as integer zero, since `text-to-int` stops at the first non-digit.
3. **`tab-import-config` and `pipe-import-config` cannot match anything**, and
   `BulkLoader.codex` says so in its own prose: a tab and a vertical bar have no
   CCE code point and cannot appear in Codex Text at all. They are still
   exported as if they were usable configs. Reported separately; not in our way,
   since the comma path is correct.

So `apps/wademo/WaLoad.codex` does the pivot itself, strips quotes, builds
`RowData` against `table-schema`, and hands batches to `bulk-insert`, which is
already transactional and WAL-logged. The codebook is parsed alongside it to
populate `dim_age` with the NHGIS code and label per bin, so provenance is
loaded rather than transcribed.

## 6. What each engine module gets exercised by

The point of the exercise, mapped to the view that drives it. Honest about the
two that do not fit.

| module | driven by |
|---|---|
| `Page`, `Heap`, `BufferPool` | the fact table on disk, 40,170 rows |
| `Row`, `Schema`, `Catalog` | DDL for seven tables, typed row construction |
| `BulkLoader`, `Transaction`, `Wal` | the pivot load as one transaction |
| `BTreeIndex` | the pyramid as a **PK prefix range scan**, not a filter pass |
| `RelAlgebra`, `Executor` | every view in section 7 |
| `HashJoin`, `SortMerge`, `RelEquiJoin` | **the banding join (3.3) and the cohort join (4.3.1)**, which are the two loads that matter |
| `Optimizer` | `EXPLAIN` on each view; the plan cache measured across the five animation frames, which are one plan shape with a different constant |
| `ColumnStore`, `MapReduce` | the animation's hot path, all frames precomputed with `mr-sum-by` |
| `StarSchema` | `star-query` auto-discovers the five `dim_*` tables off the fact and materialises the denormalised view |
| `SpatialIndex` | the map: quadtree over county centroids, radius and KNN selection |
| `GraphStore` | county adjacency: BFS for "within two counties of Whatcom", PageRank over **migration-weighted** edges |
| `Security` | the small-cell policy in 3.4, as real access control |
| `Mvcc` | the site reads a snapshot while the cohort tables rebuild |
| `Protocol`, `Session`, `Server` | the web layer talks to the DB over the wire, not in-process |
| `DbAdmin` | its query runner is a free `EXPLAIN` surface for this schema |

**Two do not fit and should not be forced.** `TimeSeries` is built for
append-optimized high-frequency data, and five decennial points is not that;
using it would be a demo rather than a use. `FullText` has nothing to index
here beyond 39 county names. Saying so is cheaper than a contrived view that
teaches nobody anything.

### 4.4 What is deliberately excluded

No life tables, no TFR, no age-specific fertility or mortality rates. All of
them need vital statistics and we have census counts only. **The child-woman
ratio and the census survival ratio exist precisely because they are the
count-only substitutes**, and quietly computing a "fertility rate" from counts
would be the same class of invention as interpolating above age 100.

## 7. The site

Five views over one schema. No view invents a number.

1. **Pyramid.** Variable banding on a slider from single years to decades, plus
   the functional and generational presets. Counts or percent of total, the
   latter being the one that makes a small county comparable to King.
2. **Time.** The five censuses, with cohort diagonals highlighted on hover and a
   selected birth cohort tracked across all five. Hamilton-Perry projections to
   2030 and 2040 rendered in a visibly different register from the measured
   bars, because a projection that looks like a measurement is the failure mode.
3. **Map.** The 39 counties, click to select, shift to multi-select, selection
   sums into one pyramid. Choropleth by any 4.1 measure.
4. **Compare.** Two selections side by side, difference pyramid beneath, the
   dissimilarity index and the standardized comparison as headline numbers.
5. **Migration.** CCR-derived net migration by age band and county, the view
   that answers "who leaves Whitman County at 22 and where do they turn up".

### 7.1 The animation problem, and why Lucky's data dissolves it

This extract is five decennial snapshots, so tweening between them would produce
smooth motion out of interpolated values: legitimate as motion, illegitimate as
data.

**Lucky's own series is year over year, WA only** (Damian, 2026-08-14), which
removes the problem rather than managing it. Roughly 45 annual frames of
measured values means the animation interpolates nothing, and three things get
better at once:

- **Cohort change becomes a one-year ratio** instead of a ten-year one, so the
  birth-year equi-join in 4.3.1 runs between adjacent years and net migration
  resolves to the year it happened in.
- **The Lexis surface becomes dense.** Single years of age against single years
  of time is the classic Lexis diagram, and with 103 ages by 45 years it is a
  real surface rather than five stripes.
- 39 x 45 x 2 x 103 is **361,530 fact rows**, nine times this sample. At the
  measured 3,174 bytes per row that load is 1.1 GB and does not fit; at the
  bulk-path figure it is about 20 MB. **The bulk load path is not an
  optimisation for Lucky's data, it is the difference between running and not.**

The one thing annual data brings with it is provenance. Annual county age-sex
figures for Washington are estimates rather than enumerations, almost certainly
the state OFM series between censuses. That is why `dim_year.provenance` is an
enum and not the `is_noised` boolean this document first proposed: a census year
and an intercensal estimate are different kinds of number and the chart must be
able to say which it is drawing.

### 7.2 The globe, which mostly already exists

Damian, 2026-08-14: load the data, fly the globe to the region, highlight the
counties, click one. **The web globe can do this and the bare-metal globe
cannot**, and the split is worth stating plainly before anyone starts.

`apps/globe/web/globe.html` is a working **WebGPU** renderer, 41 KB, and it
already has every piece this needs: `geoTo3D` for lat/lon onto the sphere,
`perspective` / `lookAt` / `project` for the camera, `buildSphere`, an
`earthColor` ramp, and an established sixteen-layer overlay pattern with a panel
builder to slot into. County rings become vertices through `geoTo3D`,
triangulate, and draw as one more overlay. Fly-to is an animation of the camera
that is already there. Click is an unproject to lat/lon then point-in-polygon.
`GlobeTypes` contributes the colour-ramp functions for the choropleth.

**The bare-metal globe is types and state only.** Its own README: "no 3D
rendering (camera and overlay state exist but no framebuffer draw calls)", "no
`opening` entry point", "fully spec'd but not yet executable". Wiring wademo
into that path means writing the renderer first, which is a different and much
larger project.

That split is also the right answer for shipping. A researcher at another
university gets a web page, not a bare-metal image and a copy of codex-vm.
WebGPU needs a current browser, which is the one real constraint to tell them
about.

### 7.3 The map needs geometry we do not have

County boundaries are not in this extract. **Source them from Census TIGER, not
from NHGIS**, for the reason in 2.4: TIGER is public domain and carries no
redistribution term, so the geometry can ship even if the counts cannot.
Simplified to a few hundred points per county, 39 counties is a small asset, and
the same rings serve the globe overlay, the flat map and the point-in-polygon
pick.

## 8. The reader, and what the app owes him

**The target reader is a retired demographer, 75, with near-total vision loss**
(Damian, 2026-08-14). He is the primary user, not an accommodation added to a
design aimed at someone else, so this section governs the others rather than
following them.

### 8.1 The principle everything else falls out of

**The barrier is perception, not comprehension.** He spent a career on this
material and knows more demography than this app will ever contain. So nothing
here simplifies the content, softens a number, or explains what a cohort is.
What it does is carry the SAME expert information through channels that do not
require seeing a chart.

Every failure mode in accessible data design comes from getting that backwards:
large type wrapped around thin content, a chart with an alt text that says
"population pyramid for King County" and nothing else, a tour that teaches the
basics to someone who wrote the textbook. **The test for every feature below is
whether an expert who cannot see the screen learns exactly what a sighted expert
learns.**

### 8.2 The pyramid is a visual artifact, so it needs three non-visual twins

A population pyramid encodes its meaning entirely in shape. That does not
survive the loss of sight, so the shape has to be re-expressed rather than
described.

**The data table, and it is not a fallback.** A real `<table>` with proper
`<th>` scope, one row per age band, columns for male count, female count, male
percent, female percent. A screen reader navigates a table cell by cell with
row and column headers announced, which is how a blind analyst actually reads
data and is often *better* than a chart for exact values. It is a peer view with
its own keyboard route, not a hidden alternative behind a link.

**The computed read-out.** Not a caption anybody wrote: prose generated from the
measures in section 4.1, at the level one demographer would say to another.

> King County, 2020. Median age 37.1. Sex ratio 99 males per 100 females.
> Old-age dependency 21 per 100. Largest single band, 30 to 34, at 8.2 percent.
> Against the state, a deficit at 18 to 24 of minus 12 percent and a surplus at
> 25 to 39 of plus 9 percent.

**This is where section 4's toolkit pays off twice.** The structural measures
were designed as analysis, and they turn out to be the accessible description of
the shape: an aging index and a dependency ratio say what the silhouette says.
Nothing new has to be invented, only rendered as sentences.

**Sonification, which is the one that will actually delight him.** Sweep age 0
to 100 over a few seconds, map count to pitch, male in the left channel and
female in the right. A baby boom is an audible swell; a war deficit is a notch;
the sex imbalance at the top of the distribution is the right channel outlasting
the left. Auditory graphs are established technique and a demographer will read
one fluently after about two passes. Controls: play, pause, scrub by age with
the arrow keys, and a spoken age announcement on demand so a feature can be
located exactly. Two pyramids can be swept in sequence for a comparison, or
panned as two voices.

### 8.3 Low vision, which is not the same as blindness and needs its own answers

Near-total loss usually means some usable vision, and the design has to serve
both that and none.

| requirement | what it means concretely |
|---|---|
| **Type scale** | user-controlled to 400 per cent with no reflow breakage and no horizontal scroll. Not a fixed "large" preset |
| **Contrast** | WCAG AAA, 7:1, as the floor rather than the target. Plus explicit high-contrast themes including **yellow on black**, which many low-vision readers prefer to white on black |
| **Never colour alone** | male and female bars differ by fill PATTERN as well as hue; the choropleth carries a hatch scale as well as a colour ramp |
| **No thin anything** | no hairline rules, no light font weights, no grey-on-grey secondary text. Every stroke at least 3px at default zoom |
| **Magnifier-safe layout** | he will likely run 4x to 8x OS or ZoomText magnification and see a fraction of the screen at once. **Nothing critical in the periphery, no information that depends on seeing two distant regions together**, and state changes announced rather than merely shown |
| **Focus** | a focus ring impossible to miss: 4px, high contrast, plus a persistent "you are here" line in the status region |
| **Motion** | `prefers-reduced-motion` honoured, and **the year animation is OFF by default**. Auto-playing motion is actively hostile at high magnification, where a moving target cannot be tracked |

### 8.4 Keyboard, because a mouse is the hardest input at low vision

Every action reachable and discoverable from the keyboard, with no hover-only
information anywhere and no drag-only interaction.

- Left and right arrows step the year; Home and End jump to the first and last
- Up and down move county in an alphabetical list; type-ahead jumps by name
- The banding control steps by keyboard **and accepts a typed value**, because a
  slider is the single worst control for this reader
- A single key toggles the sonification, another the read-out
- A visible, printable shortcut list, and the first Tab stop on the page is a
  link to it

**The county list is the primary navigation, and the globe is secondary.** A
globe is irreducibly visual. Flying to a highlighted region is genuinely good
for his sighted collaborators and for a talk, and it must never be the only way
to select a county.

### 8.5 Help, in the three forms that actually get used

1. **Answering "what am I looking at right now"** on one key, from anywhere:
   the current selection, the current banding, the provenance of the year, and
   the read-out. This is the single most valuable help feature for someone who
   cannot glance at the screen to re-orient, and glancing is exactly what
   sighted users do dozens of times a minute.
2. **A plain-language note on every number's provenance**, because the honesty
   work in section 2 is useless if it is conveyed by a hatched bar he cannot
   see. Census enumeration, intercensal estimate, or noise-injected must be
   SPOKEN, and a suppressed small cell must announce itself rather than read as
   zero. **A silent hole is the worst possible outcome for a non-visual reader**
   and it is the failure this app is most likely to ship by accident.
3. **Worked examples in his own vocabulary.** Not "what is a population
   pyramid" but "how this app computes net migration", naming the census
   survival ratio method, so he can check our arithmetic against what he would
   have done. He is more likely to audit us than to need teaching, and the help
   should assume that.

### 8.6 What this changes elsewhere in the design

- **Section 7.1's animation is off by default** and gains a step-by-year
  keyboard mode, which is the accessible way to see change anyway.
- **Section 3.4's small-cell policy must announce, never silently omit.** As
  specified it hides rows from a read-only session; for this reader that is
  indistinguishable from a zero. The policy stays and the app states the
  suppression explicitly in both the table and the read-out.
- **The site ships large-format print output.** A CCTV magnifier on paper is a
  normal tool for this reader and sometimes the most comfortable one.

## 9. The target: a web page, WASM, WebGPU

Damian, 2026-08-14. The pipeline for this is built and proven, not speculative.

### 9.1 What already exists, measured

`codex/plugs/wasm/build-spark.ps1` is a working four-phase build that has
already shipped `apps/spark` to WebGPU in the browser:

| phase | tool | note |
|---|---|---|
| source to IR-CCE | `build/compile.ps1 -IrCce` | ours |
| IR to WAT | `wasm-plug.cdx` under codex-vm | ours, `WasmEmitter.codex` is 68 KB |
| WAT to `.wasm` | `wat2wasm` | **theirs**, npm wabt. Present on this box |
| HTML assembly | template + CSS + JS | hand-written glue in `apps/spark` |

Built artifacts are on disk from 2026-08-09: `spark-webgpu.wasm`,
`spark-webgpu.html` at 136 KB, which loads WebAssembly and requests a `webgpu`
context. There is a `wgsl` plug (`WgslEmitter.codex`, 33 KB) for shader
generation and an `html` plug (`HtmlEmitter.codex`, 56 KB) beside it.

**The one outside dependency is `wat2wasm`**, which encodes WAT text to the WASM
binary format. Everything either side of it is ours. The build degrades to
WAT-only when it is absent, so this does not block anyone, but it is the single
link in the chain we did not write and it belongs on the record.

### 9.2 The layering, and the accessibility requirement decides it

**A WebGPU canvas is completely opaque to a screen reader.** Section 8 makes a
near-blind demographer the primary reader, so the usual arrangement, where the
canvas is the app and the DOM is chrome around it, is exactly backwards here.

```
  WASM  (Codex)     the DB engine, the toolkit, the loader, the parser
        |           computes every number the app shows
        v
  DOM   (accessible)  the data table, the computed read-out, the controls,
        |             ARIA live regions, keyboard routing. THE PRIMARY VIEW.
        v
  WebGPU (canvas)     the globe, the Lexis surface, the pyramid as a picture.
                      Decoration over a DOM that is already complete.
```

**The DOM is the primary rendering target and the GPU is the decoration.** The
test is that the app remains fully usable with the canvas removed entirely, and
that is worth wiring as an actual switch rather than an aspiration, because a
switch can be checked and an aspiration cannot.

The GPU still earns its place. A Lexis surface at 103 ages by 45 years is a
texture, which is the one thing a GPU does better than anything else, and the
globe renderer in `apps/globe/web` is already WebGPU with the projection and
camera written.

### 9.3 What runs in WASM, and it is the part that matters

The engine exercise in section 6 happens in the browser: `Page`, `Heap`,
`BufferPool`, `BTreeIndex`, `Catalog`, `RelAlgebra`, `Executor`, the join
family, `ColumnStore`, `MapReduce`, plus `NhgisCodebook` and the loader. **A
relational engine written in Codex, compiled to WASM, running a star schema and
an equi-join in a browser tab, is a considerably better demonstration of the
platform than the same engine on bare metal**, because the audience can open it.

The 3 GB bare-metal budget becomes a browser tab's budget, which is tighter and
less forgiving. At the measured 3,174 bytes per row, Lucky's 361,530 annual rows
want 1.1 GB in a tab and will not survive it. At the bulk-path figure they want
about 20 MB. The load path decides whether this ships.

### 9.4 He supplies the data, which dissolves section 2.4

The app ships **empty**. Lucky opens the page, picks his own extract with a file
input, and it is parsed by our WASM in his own tab. No server, no install, no
upload.

Three problems close at once. **We never redistribute anything**, so the NHGIS
term in 2.4 stops applying to us entirely. **His data never leaves his machine**,
which is the answer any university will want to hear. And **a colleague at
another institution gets one page and their own file**, which is what "share it
with other researchers" has to mean in practice.

It also forces the generic ingest to be real rather than aspirational, since the
first thing anyone will do is feed it an extract we have never seen.

### 9.5 Where the data lives when deployed: no server, and OPFS is a block device

**No server.** The page is static, the file input is the only ingest, and the
data never leaves his machine. That is what closes 2.4 and what a university
wants to hear.

**"No server" is not "no persistence", and conflating them would wreck the
app.** Re-picking a file every session is precisely the friction that makes a
tool unusable for the reader in section 8. So the deployment question is really
a storage-engine question, and we already have the storage engine.

**OPFS is the right target, and it is a real block device.** The Origin Private
File System gives a Worker synchronous access handles that read and write at
byte offsets. Our layer is already 8 KB slotted pages with a `BufferPool` doing
LRU with pin counts and dirty tracking, and a `Wal` beside it. The mapping is
almost one to one:

| ours | OPFS |
|---|---|
| `page-id` | file offset `page-id * 8192` |
| `BufferPool` dirty frame eviction | write at offset |
| `Wal` append | a second file |
| the bulk loader's finished pages | one sequential write |

That is a paged storage engine on a real block device inside a browser tab, and
**nothing in the tree does it yet.** `apps/spark` persists through IndexedDB
(`spark-app.js:614`), which is a key-value store: usable as a compatibility
fallback by storing page blobs, but it throws away the offset addressing that
makes a buffer pool worth having.

**The File System Access API is the export, and for a researcher it is the
important one.** `showSaveFilePicker` puts the database in a file on his own
disk that he can back up, archive alongside the paper, or send to a
collaborator. "Here is the file I used" is a better reproducibility artifact
than "I ran the app", and demographers are judged on the former.

**Quota is a real caveat and must be visible rather than implied.** Browsers may
evict origin storage under pressure unless `navigator.storage.persist()` is
granted. So OPFS is the working copy and the exported file is the durable one,
and the app should say which it is holding rather than let the user assume.

`localStorage` already carries preferences across this tree and is where the
section 8 settings belong: type scale, contrast theme, sonification state.
Those surviving a tab close is the difference between usable and not for this
reader, and they are tiny.

**A server is only needed for multi-user**, a departmental shared dataset or a
published link where the reader supplies no data of their own. That is a
different product, it re-opens the redistribution question in 2.4, and it should
be a deliberate later choice rather than a default.

One consequence for the loader: the bulk path in `WaBulk.codex` bypasses the WAL
deliberately, which is right for a load into an empty table from a file the user
still holds. **Once pages persist to OPFS and anything mutates after load, the
WAL stops being optional.**

## 10. Open questions

1. **Confirm the boundary-stability claim in 2.3** against a TIGER file before
   any cohort figure is published. It is believed true and it is load-bearing:
   the whole of 4.3.1 assumes a county code means the same ground in 1980 and
   2020.
2. **How coarse is the reliability floor?** The `persons >= 5` in 3.4 is a
   placeholder. Setting it properly needs a pass over all 39 counties at each
   banding, not the Garfield spot check.
3. **Is the site public?** Decides 2.4, and therefore whether the counts are
   baked into the page or re-sourced from Census.
4. **Does the fixed-point scale want to be per-million everywhere?** `ccr_ppm`
   and `net_mig_ppm` are per-million; the 4.1 structural measures are more
   natural per-100 or per-1,000. One scale is easier to reason about and loses
   precision on ratios near one, which is exactly where the migration signal
   lives.

## 11. Decided against, and why, so it is not re-proposed

- **A dense in-memory cube with direct addressing.** 40,170 cells is small
  enough that `((g * years + p) * 2 + s) * bins + b` would beat any index, and
  that was the first design. It is the wrong answer to the question asked: the
  point is to exercise the engine, and a hand-rolled array exercises nothing.
  The cube survives as what `ColumnStore` materialises for the renderer, which
  is the engine's own on-demand materialisation doing the same job.
- **Extending `DeriveFn` with age-band vocabulary words.** Tempting, symmetric
  with the date buckets, and it would make every new banding a language change.
  The join in 3.3 gets user-defined bandings for free.
- **Computing rates in the query layer.** `ColumnType` has no float, and
  inventing one for this app would be the largest possible change for the
  smallest possible reason. Fixed-point, scale documented per column.
