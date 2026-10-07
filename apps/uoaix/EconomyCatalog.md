# Economy resources and recipe closure

`EconomyCatalog.codex` describes the currently supported stage E item types,
harvest sources and recipes. `eg-standard 0` constructs the table;
`eg-validate catalog` returns 0 for closure, -1 for malformed or over-budget
tables, or the first positive item ID with no reachable production path.
Item IDs are one-based positions in `items`; resource and recipe lists are
zero-based when passed to `list-at`. Names are labels, not identifiers.

Every admitted item must be reachable from a harvest source, including seed
and tool items. A source becomes reachable only after its required tool is
reachable; a recipe requires every material and its tool. A hand-gathered
stone, fallen branch or clay source starts the graph. Stone tools and a
clay bucket close the tool and water bootstrap paths. No tool is silently
granted to a vendor. Seed reachability assumes a spawned wild source, as the
economy rules require. Empty farmland alone supplies no seed; runtime planting
must consume existing seed and runtime gathering must find an actual source.
The catalog proves that a path exists, not that a
particular town has the workers, buildings, supplies or permissions to run it.

The standard catalog covers metal, gold, leather, cloth, food, wood and
reagents. The supported type set is deliberately explicit; no claim covers
every graphic or item definition in the UO client. Adding an item requires
adding its harvest/recipe path before admission succeeds. The eventual world
item adapter must refuse types outside an accepted catalog.

## Records and codes

An item stores name and chain. Chain codes are metal 1, gold 2, leather 3,
cloth 4, food 5, wood 6, reagents 7. A resource stores output item, optional
seed/cutting item, required tool, skill, suitable ground, regrowth hours and
maximum harvest quantity. Zero tool means hand gathering. Ground 0 denotes
a nonplantable source, 1 farmland, 2 tree soil. A plantable source requires a
nonzero seed/cutting and ground; a nonplantable source has neither.

A recipe stores output and quantity, three distinct input/count pairs,
station, optional tool, and skill. The first input is mandatory. An unused
input pair is exactly 0/0. Quantities are 1..1000 and regrowth is 1..8760
game hours. The validator rejects unknown IDs, duplicate inputs, empty
outputs, unrecognized station codes and invalid optional pairs.

| Station | Code |
|---|---:|
| Handcraft workplace | 1 |
| Mill | 2 |
| Smelter | 3 |
| Spinning wheel | 4 |
| Loom | 5 |
| Bakery/cooking oven | 6 |
| Tanning vat | 7 |
| Carpenter's bench | 8 |
| Forge and anvil | 9 |
| Kiln | 10 |
| Tailor's bench | 11 |
| Royal mint | 12 |

Skill codes are no trained skill 0, mining 1, gold mining 2, hunting 3,
fishing 4, farming 5, lumber/carpentry 6, Herbalism 7, metal/pottery work 8,
cooking 9, textile/leather work 10. These simulation categories are not the
client's skill wire IDs; a server adapter must map the relevant actual skill.

Ginseng, garlic, mandrake and nightshade use a trowel and Herbalism and yield
their own planting material. Blood moss and silk require a knife; pearls a
fishing rod; ash a pickaxe. All reagent sources require Herbalism. Resource
IDs alone do not establish a valid place to harvest: the world adapter must
bind plants, rocks/trees, webs or slain spiders, oyster beds and volcanic
vents to the corresponding resource. No catalog entry produces a loose
reagent spawn.

The timber source produces logs and a sapling and has a seven-game-day
regrowth parameter. Growth phases, felling, stump graphics, tending and
seed consumption are production/world operations, not closure validation.
The graph permits alternate recipes, such as thread from cotton, flax or
wool, and cooked food from meat or fish.

The table's quantities and regrowth rates are simulation fixture defaults.
The gold-ingot-to-coin edge establishes material provenance only. Its unit
quantity is a closure placeholder, not a ruling on R8's mint price or coin
yield. Production must refuse minting until explicit mint terms and authority
are configured and must consume ingots in the same action as issuance.

## Runtime contract and cost

Validate before using a catalog, then treat the accepted records as immutable.
The validator does not freeze records. A changed catalog must be revalidated.
Recipe execution, owned workplaces, inventory admission, skill checks,
tool wear, prices and resource growth belong to the production unit. The
catalog alone grants no harvesting or crafting authority and moves no items.

Limits are 128 items, 64 resources and 128 recipes. Native item records have
2 fields (16 bytes), resources 7 (56 bytes), recipes 11 (88 bytes), and the
catalog 3 (24 bytes), plus list and text storage. Closure uses a
129-byte scratch flag array, reclaimed before returning even when an item is
unreachable. Malformed tables are rejected before indexing the flag array.

Validation performs at most item-count passes over resources and recipes:
O(items * (resources + recipes)); native recursion is bounded by the table
limits. Flags only change from unreachable to reachable. An unrooted cycle
cannot mark its own members, and item-count passes cover the longest accepted
chain even when recipes are stored in reverse dependency order.

`proofs/EconomyCatalogProof.codex` checks the standard table, reagent and mint
fields, scratch reclamation, material/tool cycles, orphan admission, malformed
tables, capacity refusal and a maximum reversed chain. Compile normally and
poisoned with an explicit depot kernel and compare the complete output with
`proofs/EconomyCatalogProof.expected`. Runtime provenance and the 30-day stage E
census remain unproved by this static catalog check.
