# Economy — Packs, Tokens, Trading, Currency

## Overview

CodexMagic's economy is built on a blockchain-backed token system.
Every card is a fungible token. Players acquire cards by cracking packs,
trading with other players, buying directly from the card shop, or
winning tournament prizes. An in-game cryptocurrency (Mana Coin,
working name) serves as the medium of exchange. Revenue comes from
Mana Coin sales, subscription tiers, and a rake on player-published
card sales.

## Pack Cracking

Packs are the primary way new cards enter circulation. A pack contains
a randomized selection of cards from the current season's pool.

```
Pack = record {
  edition : SeasonId,
  cards : List CardToken,
  price : ManaCoinAmount
}
```

**Standard pack contents (15 cards):**
- 10 Common
- 3 Uncommon
- 1 Rare (with a chance to upgrade to Mythic or Legendary-Mythic)
- 1 Wildcard slot (any rarity, weighted toward Uncommon)

**Rarity upgrade probabilities:**
- Rare slot → Mythic: 1 in 8
- Rare slot → Legendary-Mythic: 1 in 1000 (if any remain unminted)

Pack opening is an on-chain transaction. The randomization seed is
derived from the block hash at purchase time combined with the player's
account hash — verifiably random, not manipulable by either party.

### Subscription Upgrade Bonus

Subscribers get automatic rarity upgrades on pack openings. When a
pack is cracked, the subscription bonus randomly selects cards from
the pack and bumps each one up one rarity tier (Common → Uncommon,
Uncommon → Rare, Rare → Mythic). Mythic cards are not upgraded
further — they're already near the ceiling.

```
SubscriptionTier =
  | Free          -- no upgrade bonus
  | Bronze        -- +1 upgrade (1 random card bumped per pack)
  | Silver        -- +2 upgrades
  | Gold          -- +3 upgrades
  | Platinum      -- +5 upgrades (max subscription)
```

The upgrade bonus applies only to pack openings, not to direct card
purchases or tournament prizes. The upgraded cards are minted at their
new rarity — they are real cards of that rarity, indistinguishable
from naturally pulled ones.

**Tournament fairness:** Subscription bonuses affect collection
building, not gameplay. In tournaments and leagues, players choose
their competition tier:

```
CompetitionTier =
  | Open          -- subscription bonuses active on collection
  | Fair          -- subscription bonuses suppressed for matchmaking
                  -- rating; deck power is the only variable
```

In Fair tier, matchmaking ignores collection depth advantages from
subscriptions. The cards themselves are identical regardless of how
they were obtained — a Rare pulled naturally plays the same as a Rare
upgraded by subscription. Fair tier simply ensures the matchmaking
algorithm doesn't pair a Platinum subscriber's deep collection against
a free player's starter pool in competitive ranked play.

### A-La-Carte Pulls

Players can buy individual pulls from the card machine without
committing to a full pack. A single pull produces one random card
from the current season pool at the standard rarity distribution.

```
SinglePull = record {
  edition : SeasonId,
  price : ManaCoinAmount,    -- slightly higher per-card than pack rate
  subscription-bonus : Boolean  -- upgrade bonus applies if subscribed
}
```

Single pulls cost more per card than buying a full pack (no bulk
discount), but they let players spend small amounts or chase specific
rarity tiers. The subscription upgrade bonus applies to single pulls.

### Pack Types

| Pack | Cards | Price | Notes |
|------|-------|-------|-------|
| Standard | 15 | Base price | Current season pool |
| Premium | 15 | 3x base | Guaranteed Rare+ in wildcard slot, foil art variant |
| Draft | 45 (3 packs) | 2.5x base | Used for draft format entry |
| Vintage | 15 | Variable | Previous season pool, price rises over time |

## Card Tokens

Every card instance is a token on the blockchain.

```
CardToken = record {
  token-id : Hash,          -- unique on-chain identifier
  card-id : Hash,           -- which card template this is
  owner : AccountId,        -- current owner
  mint-time : Timestamp,    -- when this copy was minted
  mint-source : MintSource, -- pack, prize, promo, etc.
  provenance : List Trade,  -- full ownership history
  match-history : List MatchRef, -- games this copy was played in
  prominence : Integer      -- computed prominence score
}
```

### Prominence

A card's prominence score is a composite metric that drives its market
value. Prominence is computed from:

1. **Game utility** — how often the card appears in winning decks,
   its win-rate contribution, archetype importance
2. **Effect rarity** — how unique the card's mechanic combination is
   across the entire card pool
3. **Art reception** — community rating, trade velocity, collection
   rate for this card's art
4. **Tournament history** — appearances in top-8 finishes, decisive
   plays captured on replay, association with famous matches

Prominence is recalculated periodically from on-chain data. It is not
stored on the token itself but is derived — anyone can verify it from
the public ledger. High-prominence cards are visually distinguished in
the client with glow effects, border treatments, and prominence badges.

### Foils and Variants

Some card tokens have visual variants:

```
CardVariant =
  | Standard        -- normal art, normal border
  | Foil            -- animated foil effect overlay
  | FullArt         -- extended art, no text box border
  | Promo           -- alternate art, special border
  | Signature       -- includes tournament player's mark
```

Variants are cosmetic — same game mechanics, different presentation.
They affect market value through collector demand.

### The Ubiquitous Card

Packs have a special bonus slot that can contain a **Ubiquitous
token** — a one-use publishing right. When a player pulls a Ubiquitous
token, they can attach it to any card they own to make that card
**copiable**. The original card becomes the **master copy**, and other
players can purchase minted copies at a price set by the master copy's
owner.

```
UbiquitousToken = record {
  token-id : Hash,
  attached-to : CardToken | None,  -- the master copy
  copy-price : ManaCoinAmount,     -- set by the owner
  copies-sold : Integer,
  revenue-earned : ManaCoinAmount,
  rake-paid : ManaCoinAmount
}
```

**How it works:**

1. Player pulls a Ubiquitous token from a pack (rare — roughly 1 in 50
   packs, appears in the bonus slot alongside the 15 normal cards)
2. Player attaches it to a card they own. That card becomes the master.
3. Player sets a copy price in Mana Coin.
4. The card appears in the **Ubiquitous Market** — a special storefront
   where anyone can buy a freshly minted copy at the listed price.
5. Each sale mints a new copy of the card for the buyer. The seller
   receives the price minus our rake.

**Rake:** We take a percentage of each Ubiquitous sale (e.g., 15-20%).
This is a direct revenue stream on top of Mana Coin sales.

**Why this is powerful:** A player who pulls a high-demand card and a
Ubiquitous token can become a card publisher. They set the price,
control supply (they can delist at any time), and earn passive income
as copies sell. Cards with strong game utility, great art, or
tournament pedigree will command premium copy prices. The master copy
itself becomes more valuable — it's the origin, and its provenance
shows it as the source of all copies.

**Copies vs. originals:** Copies minted via Ubiquitous are
mechanically identical to the master but carry a `mint-source` of
`UbiquitousCopy` and reference the master's token-id. Collectors may
value the original more, but in gameplay there's no difference.

**Supply dynamics:** The owner controls the price but not demand.
If they price too high, nobody buys. Too low, and they leave money
on the table. The market finds equilibrium. If the card gets banned,
demand drops and copies stop selling — risk is on the publisher.

## Direct Card Sales

In addition to packs and the Ubiquitous Market, we sell curated cards
directly:

- **Starter decks** — pre-built decks for new players, priced to
  onboard without overwhelming pack-cracking
- **Season spotlight** — a rotating selection of individual cards
  from the current season, available for direct purchase at fixed
  Mana Coin prices
- **Event cards** — limited-time promotional cards tied to real-world
  events or in-game milestones

Direct sales are another injection point for cards into the economy,
alongside packs. Prices are set by us, not the market.

## Mana Coin (Working Name)

The in-game cryptocurrency. All transactions denominated in Mana Coin.

**Acquiring Mana Coin:**
- Purchase with real currency (primary revenue stream)
- Win tournament prizes
- Sell cards to other players
- Earn rake from Ubiquitous card copies
- Daily/weekly play rewards (small amounts, incentivize engagement)
- Season achievement rewards

**Spending Mana Coin:**
- Buy packs
- Buy single pulls from the card machine
- Buy cards from other players
- Buy cards from the Ubiquitous Market
- Buy direct-sale cards (starters, spotlights, events)
- Enter tournament events
- Purchase cosmetics (card sleeves, board skins, avatars)

**Economic controls:**
- Pack prices are set by us (not the market) to anchor the economy
- Mana Coin supply is managed — we mint when sold, burn a percentage
  on pack purchases to prevent hyperinflation
- Tournament prize pools are funded from entry fees + a seasonal pool
- No direct Mana Coin → real currency cashout from us; players trade
  on secondary markets at their own risk

## Trading

Peer-to-peer card trading is an on-chain transaction.

```
Trade = record {
  seller : AccountId,
  buyer : AccountId,
  cards-offered : List CardToken,
  cards-requested : List CardToken,
  coin-offered : ManaCoinAmount,
  coin-requested : ManaCoinAmount,
  timestamp : Timestamp,
  trade-id : Hash
}
```

**Trade types:**
- **Direct trade** — two players agree on a card-for-card or
  card-for-coin swap
- **Market listing** — a player lists a card at an asking price;
  anyone can buy it
- **Auction** — timed bidding, highest bid wins
- **Bulk trade** — trade entire decks or collections

All trades are recorded on-chain. A card's `provenance` is its full
trade history — who owned it, when, and for how much. Provenance
contributes to prominence for high-profile cards.

**Trade fees:**
- A small percentage of each trade (in Mana Coin) is burned, serving
  as a deflationary pressure and anti-manipulation measure
- Free trades between friends are limited per day to prevent wash trading

## Monetization Summary

Five revenue streams:

1. **Mana Coin sales** — players buy coin with real currency to spend
   in-game. This is the primary revenue driver.
2. **Subscriptions** — monthly tiers (Bronze through Platinum) provide
   pack upgrade bonuses. Recurring revenue, incentivizes engagement.
3. **Ubiquitous rake** — we take 15-20% of every Ubiquitous copy sale.
   Revenue scales with player-driven card publishing activity.
4. **Direct card sales** — starter decks, season spotlights, event
   cards sold for Mana Coin at prices we set.
5. **Tournament entry fees** — a portion of competitive entry fees
   funds operations; the rest goes to prize pools.

Players spend Mana Coin on packs, single pulls, direct card purchases,
Ubiquitous Market copies, tournament entries, and cosmetics. We control
pack and direct-sale prices to regulate card injection into the economy.

## Anti-Fraud

- Pack randomization is verifiably random from block + account hash
- All transactions are on-chain and auditable
- Wash trading detection via graph analysis of trade patterns
- Rate limits on free trades
- Bot detection on pack purchasing patterns
- Coin supply is transparent and auditable

## Clan Economies

Clans operate parallel economies alongside the global Mana Coin
system. Each clan mints its own currency for intra-clan trading,
tournament prizes, and library deposits. Clan coin can be exchanged
for Mana Coin at a clan-set exchange rate, with the clan treasury
acting as counterparty.

Clan packs are sold for Mana Coin (30% platform fee, 40-60% to clan
treasury). Cards from clan packs are standard tokens tradeable on
the global market.

See [Clans.md](Clans.md) for full clan economy design including
treasury management, exchange mechanics, and custom pack definitions.
