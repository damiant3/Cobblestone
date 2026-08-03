# Clans -- Organizations, Economies, and Custom Rules

## Overview

Clans are player-run organizations within CodexMagic. A clan is a
group of players with shared identity, a private economy, a card
library, custom formats, and the ability to run their own tournaments
and challenge other clans. Clans transform CodexMagic from a single
card game into a **card game construction kit** -- any clan can
define its own rules, restrictions, and play experience.

The global game (Modern, Legacy, etc.) is just the "default clan"
operated by the platform. Every other clan is a parallel game world
with its own economy, its own rules, and its own metagame.

## Clan Record

```
Clan = record {
  clan-id : Hash,
  name : Text,
  tag : Text,                    -- 3-5 char shorthand (e.g., "IRON")
  founder : AccountId,
  officers : List AccountId,
  members : List AccountId,
  member-count : Integer,
  created-at : Timestamp,

  -- Economy
  clan-coin-name : Text,         -- name of the clan's currency
  clan-ledger : ManaCoinLedger,  -- internal clan currency ledger
  treasury : Integer,            -- clan's global ManaCoin balance
  exchange-rate : Integer,       -- clan coin per ManaCoin (x100)
  library : List CardToken,      -- clan-owned card pool

  -- Rules
  house-rules : List HouseRule,  -- custom format overrides
  custom-formats : List FormatDef,
  banned-cards : List Integer,   -- clan-specific bans
  restricted-cards : List Integer,

  -- Status
  rank : ClanRank,
  reputation : Integer,
  tournament-wins : Integer,
  challenge-record : ChallengeRecord
}
```

## Clan Roles

```
ClanRole =
  | Founder        -- full control, cannot be removed
  | Officer        -- manage members, run tournaments, set rules
  | Veteran        -- can check out library cards, vote on bans
  | Member         -- basic access, play in clan events
  | Recruit        -- probationary, limited library access
```

Permissions per role:

| Action | Founder | Officer | Veteran | Member | Recruit |
|--------|---------|---------|---------|--------|---------|
| Edit clan rules | Yes | Yes | No | No | No |
| Create formats | Yes | Yes | No | No | No |
| Run tournaments | Yes | Yes | Yes | No | No |
| Issue challenges | Yes | Yes | Yes | No | No |
| Check out library cards | Yes | Yes | Yes | Yes (limit) | No |
| Vote on bans | Yes | Yes | Yes | No | No |
| Trade clan coin | Yes | Yes | Yes | Yes | Yes |
| Invite members | Yes | Yes | Yes | No | No |
| Kick members | Yes | Yes | No | No | No |

## Clan Economy

### Clan Coin

Every clan mints its own currency. Clan coin is used for:
- Intra-clan card trading
- Clan tournament entry fees and prizes
- Library checkout deposits
- Clan store purchases

Clan coin is separate from global ManaCoin. It circulates only
within the clan. The clan sets its own monetary policy -- mint rate,
burn rules, reward amounts.

```
ClanEconomy = record {
  coin-name : Text,             -- "Iron Marks", "Shadow Tokens", etc.
  coin-supply : Integer,
  coin-burned : Integer,
  mint-authority : ClanRole,    -- who can mint (usually Officer+)
  exchange-enabled : Boolean,   -- can members exchange for ManaCoin?
  exchange-rate : Integer,      -- clan coin per ManaCoin (x100)
  exchange-fee : Integer        -- percentage taken on exchange
}
```

### Exchange

If `exchange-enabled` is True, members can convert between clan coin
and ManaCoin at the clan's exchange rate. The clan treasury acts as
the counterparty -- buying ManaCoin from members adds to the treasury,
selling ManaCoin to members deducts from it.

The exchange fee (percentage) is burned, creating deflationary
pressure on the clan currency.

### Clan Treasury

The treasury holds the clan's global ManaCoin balance. It is funded
by:
- Member dues (optional, set by officers)
- Tournament entry fee rake
- Exchange fees
- Donations
- Card sales on the global market

The treasury is spent on:
- Buying cards for the library
- Global tournament entry fees
- Challenge stakes
- Exchange liquidity

All treasury transactions are on-chain and visible to members.

## Clan Library

The library is a shared pool of cards owned by the clan. Members
can **check out** cards from the library to use in their decks.

```
LibraryCheckout = record {
  token-id : Hash,
  borrower : AccountId,
  checked-out-at : Timestamp,
  due-at : Timestamp,
  deposit : Integer              -- clan coin deposit, returned on return
}
```

**Rules:**
- A checked-out card cannot be traded on the global market.
- The borrower puts up a clan-coin deposit. If the card is not
  returned by the due date, the deposit is forfeited and the card
  is flagged for recovery.
- Officers can recall cards at any time (with notice period).
- The library card's `provenance` records clan ownership -- it is
  not the borrower's card, it is the clan's.

**Acquisition:**
- Officers use treasury ManaCoin to buy cards from the global market.
- Members can donate cards to the library (tax-deductible from clan
  dues).
- Tournament prizes won by clan members can be directed to the
  library.

**Sale:**
- Officers can sell library cards on the global market for ManaCoin.
- Sale proceeds go to the treasury.
- Selling a card that is currently checked out requires recalling it
  first.

## House Rules

Clans can define **house rules** that modify game mechanics for
intra-clan play. House rules are overlays on top of the base game
rules -- they add restrictions or change parameters but cannot add
new mechanics (that requires a code change).

```
HouseRule =
  | BanCard (Integer)
  | RestrictCard (Integer) (Integer)  -- card ID, max copies
  | SetLifeTotal (Integer)
  | SetTurnCap (Integer)
  | SetDeckSize (Integer) (Integer)   -- min, max
  | SetCopyLimit (CopyLimit)
  | RequireKeyword (Keyword)          -- every deck must include N cards with this keyword
  | BanKeyword (Keyword)              -- no cards with this keyword
  | SetGeneralLife (Integer)
  | SetStartingLoyalty (Integer)
  | EnableMechanic (Text)             -- enable an EXPLORE mechanic
  | DisableMechanic (Text)            -- disable a standard mechanic
  | CustomSuddenDeath (Integer) (Integer) -- base, escalation
```

**Examples:**
- "No Flying" clan: `BanKeyword Flying`. Creates a ground-only
  metagame where reach is useless and defense matters more.
- "Pauper" clan: custom format where only Common cards are legal.
- "Giant" clan: `SetLifeTotal 40, SetTurnCap 20`. Longer, slower
  games with more development.
- "Speed" clan: `SetLifeTotal 10, SetTurnCap 6, SetDeckSize 20 20`.
  Hyper-fast micro-games.
- "Singleton Blitz" clan: combines Singleton + Blitz rules.
- "No Deathtouch" clan: `BanKeyword Deathtouch`. Tests whether
  defense becomes dominant without its primary counter.

House rules create **emergent metagames** -- each clan's rule set
produces a different game. Players who enjoy a particular playstyle
gravitate to clans that share it.

## Custom Formats

Clans can create named formats by combining a `FormatDef` with
house rules. These are playable within the clan and can be offered
in inter-clan challenges.

```
ClanFormat = record {
  name : Text,
  base-format : FormatDef,
  house-rules : List HouseRule,
  description : Text,
  created-by : AccountId,
  created-at : Timestamp
}
```

Custom formats appear in the clan's tournament menu alongside the
global formats. Ranked play within a clan uses the clan's default
format. Multiple formats can be active simultaneously.

## Clan Tournaments

Clans run their own tournaments using any format (global or custom).

```
ClanTournament = record {
  tournament-id : Hash,
  clan-id : Hash,
  name : Text,
  format : ClanFormat,
  structure : TournamentStructure,
  entry-fee : Integer,           -- in clan coin
  prize-pool : Integer,          -- in clan coin
  participants : List AccountId,
  max-players : Integer,
  status : TournamentStatus,
  results : List TournamentPlacement
}

TournamentStructure =
  | Swiss (Integer)              -- N rounds
  | SingleElim
  | DoubleElim
  | RoundRobin
  | League (Integer) (Integer)   -- min games, duration in days

TournamentStatus =
  | Registration
  | InProgress
  | Completed
  | Cancelled
```

**Prize distribution:**
- Entry fees collected in clan coin.
- Platform rake: 5% of entry fees go to the clan treasury.
- Remaining pool distributed to top finishers.
- Officers set the distribution (e.g., 60/30/10 for top 3).

## Inter-Clan Challenges

Clans can challenge other clans to matches or tournaments.

```
ClanChallenge = record {
  challenge-id : Hash,
  challenger-clan : Hash,
  defending-clan : Hash,
  format : ClanFormat,           -- proposed format (must be agreed)
  structure : TournamentStructure,
  players-per-side : Integer,    -- how many from each clan
  stake : ChallengeStake,
  status : ChallengeStatus,
  results : List MatchResult
}

ChallengeStake =
  | Honor                        -- reputation only, no currency
  | ManaCoinStake (Integer)      -- from each clan's treasury
  | CardStake (List Hash)        -- specific cards wagered
  | TrophyStake                  -- winner gets a trophy token

ChallengeStatus =
  | Proposed
  | Accepted
  | InProgress
  | Completed
  | Declined
```

**Challenge flow:**
1. Clan A proposes a challenge with format, structure, and stakes.
2. Clan B reviews and accepts, modifies, or declines.
3. If accepted, both clans select their representatives.
4. Matches are played (on the global network, not locally).
5. Results are posted on-chain. Winner collects stakes.
6. Both clans' reputation scores update.

**Format negotiation:** The challenger proposes a format. The
defender can accept or counter-propose. If neither clan has the
other's custom format, they must agree on a global format or one
clan adopts the other's house rules for the challenge.

**Mixed-format challenges:** A challenge can specify different
formats for different matches (e.g., "Best of 5: Game 1-2 in
challenger's format, Game 3-4 in defender's format, Game 5 in
Modern"). This creates interesting "home-and-away" dynamics.

## Clan Rank

Clans earn reputation from:
- Tournament participation and results
- Challenge wins
- Member count and activity
- Library size and quality
- Economic activity (trade volume)

```
ClanRank =
  | Unranked
  | Bronze
  | Silver
  | Gold
  | Platinum
  | Diamond
  | Champion

ChallengeRecord = record {
  challenges-issued : Integer,
  challenges-accepted : Integer,
  challenges-won : Integer,
  challenges-lost : Integer
}
```

Higher-ranked clans get:
- Priority matchmaking for challenges
- Higher visibility in the clan directory
- Seasonal rewards (ManaCoin, exclusive cards)
- Display badges on member profiles

## The Construction Kit

The combination of house rules, custom formats, clan economies, and
challenges makes CodexMagic a **construction kit**:

- **Want a chess-like game?** Singleton, no randomness (fixed deck
  order), high life, no draws.
- **Want a speed game?** 20 cards, 5-turn cap, 10 life, no lands
  (all spells cost 0).
- **Want a creature-only game?** Ban all instants, sorceries, and
  enchantments.
- **Want a draft-only community?** Clan that only runs draft events,
  with a shared library for between-draft play.
- **Want to test new mechanics?** Enable EXPLORE mechanics in house
  rules, run tournaments to collect data.
- **Want balanced play?** Ban the top 5 cards from simulation data,
  see if the meta improves.

Each clan is an experiment in game design. Successful experiments
(popular house rules, healthy metagames) can be promoted to global
formats. The global game evolves based on what clans discover.

## API Endpoints

| Endpoint | Description |
|----------|-------------|
| `/clan/create` | Create a new clan |
| `/clan/profile` | Clan info, members, rules, formats |
| `/clan/join` | Join a clan |
| `/clan/leave` | Leave a clan |
| `/clan/invite` | Invite a player |
| `/clan/kick` | Remove a member |
| `/clan/rules/set` | Add/remove house rules |
| `/clan/format/create` | Create a custom format |
| `/clan/tournament/create` | Create a clan tournament |
| `/clan/tournament/join` | Join a clan tournament |
| `/clan/challenge/issue` | Issue a challenge to another clan |
| `/clan/challenge/respond` | Accept/decline a challenge |
| `/clan/library/list` | List clan library cards |
| `/clan/library/checkout` | Check out a card |
| `/clan/library/return` | Return a card |
| `/clan/library/buy` | Buy a card for the library |
| `/clan/library/sell` | Sell a library card |
| `/clan/coin/mint` | Mint clan coin (officer+) |
| `/clan/coin/transfer` | Transfer clan coin |
| `/clan/coin/exchange` | Exchange clan coin for ManaCoin |
| `/clan/treasury/balance` | View treasury |
| `/clan/treasury/deposit` | Deposit ManaCoin to treasury |

## Custom Packs and Content Theming

### The Idea

Clans don't just play the game -- they can **skin it**. A clan
configures its own pack product: what mechanics are included, what
art style is generated, what flavor and tone the cards carry. The
underlying game engine is the same. The presentation, theme, and
card pool are the clan's to define.

A church youth group makes packs with biblical themes, pastoral art,
and creature types like "Angel" and "Shepherd." A sci-fi clan makes
packs with robots, lasers, and "Mech" creatures. A horror clan makes
packs with dark art and creature types like "Wraith" and "Abomination."
They all play the same combat math, the same stack, the same defense
stat. But the *experience* is theirs.

Kids play in a walled garden of age-appropriate content that their
parents chose. Teens get more expressive themes. Adults get whatever
they want. And they can all play against each other when they choose
to -- the cards are mechanically interchangeable, just themed
differently.

### Clan Pack Definition

```
ClanPackDef = record {
  pack-id : Hash,
  clan-id : Hash,
  name : Text,                   -- "Holy Orders Booster"
  description : Text,
  card-count : Integer,          -- cards per pack
  rarity-distribution : RarityDist,
  mechanic-filter : MechanicFilter,
  content-theme : ContentTheme,
  art-direction : ArtDirection,
  price : Integer,               -- ManaCoin cost to crack
  clan-revenue-share : Integer,  -- percentage to clan treasury
  platform-fee : Integer,        -- percentage to platform (fixed)
  approved : Boolean,            -- passed content review
  public : Boolean               -- listed on global market?
}
```

### Rarity Distribution

Clans configure how many cards of each rarity appear per pack.
The total must equal `card-count`.

```
RarityDist = record {
  commons : Integer,
  uncommons : Integer,
  rares : Integer,
  mythics : Integer,
  guaranteed-rare-plus : Boolean,
  wildcard-slot : Boolean
}
```

A clan focused on draft might want flatter distributions (more
uncommons, fewer mythics) for a more balanced limited experience.
A collector clan might want higher mythic rates for chase cards.

### Mechanic Filter

Clans choose which mechanics appear in their pack's card pool.

```
MechanicFilter = record {
  allowed-keywords : List Keyword,  -- empty = all allowed
  banned-keywords : List Keyword,
  allowed-card-types : List CardType,
  banned-card-types : List CardType,
  max-cmc : Integer,                -- highest mana cost allowed
  min-creatures-pct : Integer,      -- min percentage creatures
  require-defense : Boolean,        -- all creatures must have defense > 0
  allow-tokens : Boolean,
  complexity-cap : Integer          -- max abilities per card (1-5)
}
```

**Examples:**
- Kids clan: `banned-keywords = [Deathtouch]`, `complexity-cap = 2`,
  `max-cmc = 5`. Simple, no "death" themed mechanics, low curve.
- Competitive clan: no restrictions, all keywords, full complexity.
- "Ground War" clan: `banned-keywords = [Flying]`, all creatures
  must fight on the ground.
- "Big Creatures" clan: `min-creatures-pct = 80`, `max-cmc = 10`,
  focus on massive beaters.

### Content Theme

The theme defines the flavor layer -- names, flavor text, creature
types, and art prompts. The AI content generator uses the theme
to produce cards that fit the clan's aesthetic.

```
ContentTheme = record {
  theme-name : Text,             -- "Biblical", "Sci-Fi", "Horror"
  flavor-tone : FlavorTone,
  creature-types : List Text,    -- custom subtypes
  setting : Text,                -- "Ancient Holy Land", "Cyberpunk Mars"
  color-flavor : List ColorFlavor,
  name-style : NameStyle,
  profanity-level : ProfanityLevel
}

FlavorTone =
  | Wholesome        -- uplifting, positive, family-friendly
  | Heroic           -- epic, noble, adventure
  | Neutral          -- generic fantasy
  | Dark             -- grim, edgy, mature themes
  | Comedic          -- silly, puns, pop culture
  | Academic         -- scholarly, historical, educational

NameStyle =
  | ClassicFantasy   -- "Ironclad Sentinel", "Azure Scholar"
  | SciFi            -- "Mech-7 Assault Unit", "Plasma Conduit"
  | Biblical         -- "Shepherd of the Valley", "Angel of Mercy"
  | Modern           -- "Street Brawler", "Tech Startup"
  | Mythological     -- "Thor's Hammer", "Medusa's Gaze"
  | Custom (Text)    -- clan provides naming guidelines

ProfanityLevel =
  | Clean            -- no violence, no death, no scary imagery
  | Mild             -- fantasy violence, "destroy" not "kill"
  | Standard         -- typical card game level (MTG-like)
  | Mature           -- blood, horror, dark themes
  | Unrestricted     -- anything goes (adult unmoderated only)

ColorFlavor = record {
  color : ManaColor,
  theme : Text       -- "Healing Light" for White, "Holy Fire" for Red
}
```

**Age bracket enforcement on ProfanityLevel:**

| Bracket | Max ProfanityLevel |
|---------|-------------------|
| Child | Clean |
| Teen | Standard |
| Adult (Moderated) | Mature |
| Adult (Unmoderated) | Unrestricted |

A KidsClan cannot create packs above Clean level. The system
enforces this regardless of what the clan officers set. A
FamilyClan defaults to Clean but can be raised to Mild with
guardian approval.

### Art Direction

The AI art generator uses the art direction to produce consistent
visual themes across a clan's card pool.

```
ArtDirection = record {
  style : ArtStyle,
  palette : Text,               -- "warm earth tones", "neon cyberpunk"
  medium : Text,                -- "oil painting", "pixel art", "3D render"
  motifs : List Text,           -- recurring visual elements
  border-style : Text,          -- "gold filigree", "circuit board", "vines"
  content-restrictions : List Text  -- "no skulls", "no blood", "no weapons"
}

ArtStyle =
  | Realistic
  | Stylized
  | Cartoon
  | Anime
  | PixelArt
  | Watercolor
  | Abstract
  | Stained Glass              -- great for church clans
  | ComicBook
  | Custom (Text)
```

A church youth group might use:
```
art-direction = ArtDirection {
  style = StainedGlass,
  palette = "warm golds, deep blues, white light",
  medium = "stained glass window",
  motifs = ["doves", "olive branches", "rays of light", "scrolls"],
  border-style = "stone arch",
  content-restrictions = ["no skulls", "no blood", "no demons",
    "no undead", "no dark magic"]
}
```

A sci-fi clan might use:
```
art-direction = ArtDirection {
  style = Realistic,
  palette = "chrome, neon blue, deep space black",
  medium = "3D render",
  motifs = ["circuit patterns", "holograms", "star fields"],
  border-style = "circuit board traces",
  content-restrictions = []
}
```

### Pack Economics

Clan packs cost ManaCoin to crack -- this is where the platform
monetizes. The revenue splits:

| Recipient | Share | Notes |
|-----------|-------|-------|
| Platform | 30% | Fixed, non-negotiable |
| Clan treasury | 40-60% | Set by clan officers |
| Card generation | 10% | AI compute cost for art + mechanics |

The clan sets the total pack price. Higher-quality packs (more
rares, better art, unique mechanics) can charge more. The market
decides whether the price is fair.

**Global market listing:** If `public = True`, the clan's packs
appear in the global store alongside the platform's standard packs.
Anyone can buy them. Cards from clan packs are fully tradeable on
the global market -- they are standard tokens with clan provenance
in their metadata.

**Cross-clan compatibility:** All cards use the same game engine.
A card from "Holy Orders" clan plays against a card from "Cyber
Warfare" clan with identical rules. The art and names differ; the
P/T/D, keywords, and effects are mechanically identical cards from
the same template pool. Clan theming is a skin, not a fork.

### Content Approval Pipeline

Before a clan pack goes live, it passes through content review:

1. **Mechanic review (automated):** Verify all cards compile,
   type-check, and pass the QA simulation gate. Same as the global
   card generation pipeline.

2. **Content review (AI + human):**
   - ProfanityLevel matches the clan's age class
   - Art meets content-restriction requirements
   - Card names and flavor text match the tone
   - No trademark/copyright issues in the theme
   - Nothing that would violate platform terms

3. **Economic review (automated):** Pack price is within sane
   bounds. Rarity distribution is not degenerate (e.g., all mythics
   at 1 ManaCoin). Revenue share adds up.

Packs that fail review are returned to the clan with notes. The
clan can fix and resubmit. Packs that pass are `approved = True`
and can be cracked.

### The Vision

A 10-year-old in a church youth group cracks "Shepherd's Light"
packs with stained-glass art and Angel creatures. They play in
their KidsClan with EmotesOnly chat, supervised by their youth
pastor (who is an Officer with a guardian account). They learn
the game, build decks, and compete in the clan's weekly Swiss
tournament.

At 13, they graduate to a TeenClan. They join "Knights of the
Round" with Arthurian themes -- Heroic tone, Stylized art, slightly
more complex mechanics. They play in teen tournaments, start
drafting, and build a collection.

At 18, they go Adult. They join "Neon Abyss" -- a cyberpunk clan
with Dark tone, Realistic art, full keyword complexity. They play
in open tournaments, trade on the global market, and their cards
from every phase of their life are still in their collection, still
playable, still valued.

And every step of the way, they were playing the same game. The
rules never changed. The experience evolved with them.

Or they join the "Xbox Live Experience" clan, Adult Unmoderated,
where the chat is free-for-all chaos and the pack art is whatever
the officers thought was funny. That's fine too. The platform
supports it by not pretending adults need protection from words.

The key: **every player gets the experience they want, and no
player is forced into one they don't.** The clan system is the
mechanism. The age brackets are the guardrails. The content theming
is the expression layer. The game engine underneath is universal.

## Age Brackets and Safety

### Account Age Classification

Every account has an age bracket. The bracket determines the
communication interface, matchmaking pool, and parental controls.

```
AgeBracket =
  | Child             -- under 13, COPPA-level protections
  | Teen              -- 13-17, moderate protections
  | Adult             -- 18+, full access
```

Age bracket is set at account creation and verified. Changing
bracket requires re-verification. The bracket is immutable for
Child accounts without parental action.

### Communication Tiers

Communication is layered by age bracket AND by moderation
preference. Each player has a `ChatTier` that determines what
they can send and receive.

```
ChatTier =
  | EmotesOnly         -- preset emotes, stickers, quick-chat phrases
  | ModeratedChat      -- free text, AI-monitored for sentiment
  | UnmoderatedChat    -- free text, no filtering

ChatPolicy = record {
  send-tier : ChatTier,
  receive-tier : ChatTier,
  block-list : List AccountId,
  accept-cross-age : Boolean   -- willing to interact with other brackets?
}
```

**Default by age:**

| Bracket | Send | Receive | Cross-age |
|---------|------|---------|-----------|
| Child | EmotesOnly | EmotesOnly | No |
| Teen | ModeratedChat | ModeratedChat | Teens + Adults (moderated) |
| Adult | UnmoderatedChat | UnmoderatedChat | Yes |

**Moderated chat enforcement:**

In ModeratedChat tier, all messages pass through AI sentiment
analysis before delivery. The system checks for:
- Profanity and slurs (blocked)
- Harassment, threats, bullying (blocked, flagged for review)
- Attempts to circumvent filters (creative spelling, unicode
  substitution, etc.) -- detected and treated as violations
- Persistent negative sentiment (not a single bad word, but a
  pattern of hostility)

Violations accumulate. Consequences escalate:
1. First offense: message blocked, warning shown
2. Three offenses in a session: muted for the session
3. Pattern of violations: demoted to EmotesOnly for 24 hours
4. Repeated demotion: permanent EmotesOnly or account review

Adults can choose their own tier:
- **Adult (Moderated)**: opt into the filtered experience. Good for
  adults who prefer civil interaction. This is the default for
  adults.
- **Adult (Unmoderated)**: full free chat. Matched only with other
  unmoderated adults. Opt-in, not default.

### Cross-Bracket Interaction

When two players from different brackets are matched, the
interaction interface drops to the **lowest common tier**.

| Player A | Player B | Effective Chat |
|----------|----------|---------------|
| Child | Child | EmotesOnly |
| Child | Teen | EmotesOnly |
| Child | Adult | EmotesOnly (if child allows cross-age) |
| Teen | Teen | ModeratedChat |
| Teen | Adult | ModeratedChat |
| Adult (Mod) | Adult (Mod) | ModeratedChat |
| Adult (Mod) | Adult (Unmod) | ModeratedChat |
| Adult (Unmod) | Adult (Unmod) | UnmoderatedChat |

**Opt-out:** Any account can refuse cross-age matching entirely.
A Child account with `accept-cross-age = False` will never be
paired with a Teen or Adult in matchmaking. This is the default
for Child accounts.

### Parental Controls

Child accounts have a linked parent/guardian account.

```
ParentalControls = record {
  guardian : AccountId,
  play-hours : TimeWindow,      -- allowed play times
  daily-limit : Integer,        -- max games per day
  spending-limit : Integer,     -- max ManaCoin per day
  clan-approval : Boolean,      -- guardian must approve clan joins
  cross-age-allowed : Boolean,  -- can play against teens/adults?
  chat-override : ChatTier,     -- guardian can lock to EmotesOnly
  friend-approval : Boolean     -- guardian must approve friend adds
}

TimeWindow = record {
  start-hour : Integer,
  end-hour : Integer,
  timezone : Text
}
```

The guardian account receives:
- Weekly play reports (time played, games, spending)
- Notifications for clan invites, friend requests
- Ability to remotely adjust settings
- Full chat log access for their child's account

### Clan Age Classification

Clans are classified by their membership composition:

```
ClanAgeClass =
  | KidsClan            -- majority Child members, guardian-supervised
  | TeenClan            -- majority Teen members
  | AdultClan           -- majority Adult members
  | MixedClan           -- no majority, cross-age rules apply
  | FamilyClan          -- explicitly cross-age, guardian-managed
```

Age class is computed automatically from membership and affects:
- **Default chat tier** within the clan
- **Matchmaking pool** for clan tournaments
- **Challenge eligibility** (KidsClan cannot challenge AdultClan
  unless both opt in)
- **Content filtering** on clan names, descriptions, and
  tournament names

**KidsClan special rules:**
- At least one guardian must be an officer
- All chat is EmotesOnly regardless of individual settings
- Spending limits enforced at the clan level
- Clan coin exchange disabled (no real-money-adjacent transactions)
- Tournament stakes limited to Honor only (no ManaCoin wagering)

**FamilyClan:**
- Explicitly designed for mixed-age play (parents + children)
- Guardian is always Founder or Officer
- Chat drops to EmotesOnly when children are present in a channel
- Can run cross-age tournaments with parental oversight

### Clan Size Classification

Clans are also classified by size, which affects tournament
eligibility and challenge matchmaking.

```
ClanSize =
  | Micro              -- 2-10 members
  | Small              -- 11-50 members
  | Medium             -- 51-200 members
  | Large              -- 201-1000 members
  | Massive            -- 1000+ members
```

Size classification affects:
- **Tournament player minimums** (a Micro clan can't run a 32-player
  bracket)
- **Challenge fairness** (challenges between clans of different sizes
  use per-capita scoring or fixed team sizes)
- **Library size expectations** (larger clans naturally have bigger
  libraries)
- **Treasury management** (larger clans may need multi-officer
  approval for large transactions)

### Format Preference Tags

Clans declare format preferences so players can find communities
that match their playstyle.

```
FormatPreference =
  | ConstructedFocus     -- primarily Modern/Legacy constructed
  | LimitedFocus         -- primarily Draft/Sealed events
  | CasualFocus          -- kitchen-table, house rules, fun first
  | CompetitiveFocus     -- ranked, tournaments, prizes
  | BrewerFocus          -- custom formats, experimental rules
  | CollectorFocus       -- trading, library building, card value
```

Clans can have multiple tags. The clan directory is searchable by
age class, size, format preference, and activity level.

### Matchmaking Integration

The matchmaking system respects age brackets:

1. **Queue segregation:** Child queues, Teen queues, Adult queues.
   Cross-age queuing requires both players to have
   `accept-cross-age = True`.

2. **Tournament segregation:** Tournaments can be age-restricted.
   A "Kids Tournament" only allows Child accounts. An "Open
   Tournament" allows all ages with lowest-common-tier chat.

3. **Clan challenges:** Cross-age-class challenges require explicit
   opt-in from both clans. A KidsClan officer (who must be a
   guardian) must approve challenges against TeenClan or AdultClan.

4. **Ranked ladders:** Separate ladders per age bracket with the
   option to view a combined leaderboard. A Child's #1 rank is just
   as valid as an Adult's #1 rank -- they just play in different
   pools.

### Reporting and Trust

Players can report others for:
- Chat violations (with message log attached)
- Inappropriate clan/account names
- Suspected age misrepresentation
- Unsportsmanlike conduct

Reports are reviewed by AI first (sentiment analysis, pattern
matching), then escalated to human review for serious cases. The
trust system tracks report outcomes:

```
TrustScore = record {
  reports-received : Integer,
  reports-upheld : Integer,
  reports-dismissed : Integer,
  current-standing : TrustLevel
}

TrustLevel =
  | Trusted            -- clean record
  | Cautioned          -- minor infractions
  | Restricted         -- limited features (EmotesOnly, no clan creation)
  | Suspended          -- temporary ban from play
  | Banned             -- permanent
```

Trust scores are not visible to other players -- only the
consequences are (e.g., a Restricted player appears as EmotesOnly
to others, indistinguishable from someone who chose that setting).

## File Plan

```
codex.magic/
  Clan.codex           -- clan record, roles, membership, age class
  ClanEconomy.codex    -- clan coin, treasury, exchange
  ClanLibrary.codex    -- shared card pool, checkout/return
  ClanFormat.codex     -- house rules, custom formats
  ClanTournament.codex -- clan-run tournaments
  ClanChallenge.codex  -- inter-clan challenges
  ClanPacks.codex      -- custom pack definitions, theming, content pipeline
  Safety.codex         -- age brackets, chat tiers, parental controls
  Trust.codex          -- reporting, trust scores, moderation
```
