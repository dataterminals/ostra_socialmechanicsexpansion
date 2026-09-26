# Social Mechanics Expansion: design

Status (0.1.0): Thaw, Drift and Gossip are built; Shared History and Favors are not yet.

One BepInEx plugin, five features, each switchable in the config. Like Romantic
Flexibility, everything builds on the tally the game already keeps and never uses.

## What the game gives us

- **Relationship** (one per person, one-way) holds a tally per social stat (`dictConds`),
  labels (`aRelationships`: RELFriend, RELEnemy, RELBioMother…), history strings (`aEvents`,
  e.g. "Became Enemy during: Serve Hard Time") and reveals. All of it is saved with the game,
  so anything we write there persists for free.
- **`Relationship.StoreIAConds(coUs, dict, coThem)`** runs on both sides after every social
  exchange: it adds half the exchange's stat effects to the tally, recomputes familiarity
  (sum of each stat capped at ±25), kindness and animosity, then flips labels. It only
  reconsiders friend/enemy at familiarity **200**, and it never un-makes an enemy.
- **`CondTrigger.TriggeredREL`** lets any interaction require or forbid a label on the
  relationship. That's how family-only lines work, and it's how new lines can be gated.
- **`GigManager.MakeJob`** picks a client with `StarSystem.GetPerson(spec, …)`; person specs
  can already say "find someone related to X" (`strCTRelFind`).
- Gossip in the base game is only scripted flavor in plot scenes (police shakedown). Nothing
  spreads.

**The trap:** while someone is focused on a person (`CondOwner.strLastSocial`), the game adds
that relationship's tally onto their live stats (`ApplyConds`) and subtracts it again when
focus moves. Changing a tally in between leaves the stats permanently off. Every feature that
touches a tally skips the currently focused relationship, or removes, edits and re-applies it.

## 1. Thaw: labels that can change

Patch `StoreIAConds` (postfix, after the game's own logic):

- Friend/enemy verdict at a configurable familiarity (default **120**, game 200).
- **Enemy → Acquaintance** once kindness is over 55% of familiarity at **60**+ ("made peace";
  logs an event). **Friend → Acquaintance** the same way with animosity ("drifted apart").
- Labels stay one-way, like the game. Today: Isabel sees Rachel 72% kind at 70.5, so she'd
  thaw on their next exchange. Rachel sees Isabel 93% hostile, so Rachel stays an enemy.

Config: `VerdictAt`, `ThawAt`, `ThawShare`, `CoolAt`.

## 2. Drift: time apart fades feelings

A slow tick (once per game hour) over loaded people's relationships:

- Each tally entry decays toward 0. Hostility fades faster than kindness (defaults: half-life
  20 game-days hostile, 40 kind), so grudges cool and friendships need occasional upkeep.
- Paused for pairs that shared a room recently, which the plugin remembers in memory.
- Labels don't change on drift; the next exchange re-evaluates with Thaw's rules. So an old
  enemy you haven't seen in a month can come back as an acquaintance after one decent chat.
- Limit: an NPC's view only drifts while they're loaded (on your station or ship). The
  player's view of everyone always drifts. Catching up unloaded NPCs would need a sidecar
  file, so it's out of scope for v1.

Config: `HostileHalfLifeDays`, `KindHalfLifeDays`, `PauseWhenTogether`.

## 3. Gossip: opinions travel

Also hooked on `StoreIAConds`. When a listener regards the speaker as a friend (or as a kind
acquaintance):

- With `GossipChance` (default 0.25), at most once per pair per game-day, the speaker passes
  on one opinion: the third person they feel most strongly about (familiarity × kind/hostile
  lean), preferring people the listener knows or who are nearby.
- The listener's tally on that person moves by `GossipShare` (10%) of the speaker's, capped
  per rumor. If the listener didn't know them, they're added as a stranger with the event
  "Heard about them from <speaker>". Labels still wait for a real meeting, so a first meeting
  with someone whose friend hates you starts off frosty.
- Rachel as the subject is the fun part: her reputation spreads through friendships, not just
  through faction numbers. When it happens in her room, it goes in her log.
- Rachel as a listener is off by default, so her feelings stay the ones you played. It's a
  toggle.

Config: `GossipChance`, `GossipShare`, `GossipCap`, `GossipReachesPlayer`, `GossipLog`.

## 4. Shared history: what you've been through counts

History labels shown next to the relationship label, like the family ones: **Cellmate**
(served hard time together), **Shipmate** (crew together for N days), **Survivor** (lived
through a fire or blowout in the same room), **Client** (you finished a job for them).

- Earned by hooks the plugin already knows from OstraScope: the hard-time interaction, crew
  membership, fires and venting in a shared room, and gig turn-in.
- Each label is a small one-time kind nudge plus an `aEvents` line, so it shows in the game's
  own history UI.
- Each label unlocks a couple of "reminisce" lines in conversation, gated with
  `TriggeredREL`: mildly kind, cheap, only between people who share that history.
- The mod carries its few conds and interactions as JSON inside the plugin and registers them
  after the game loads its data, so it's still one folder to install.

Needs writing: the label names and the reminisce lines (two or three per label).

## 5. Favors: friends are worth something

- **Friend clients:** while `MakeJob` is picking a client, `FavorChance` (default 0.5) that a
  loaded friend who fits the job's client spec gets it instead of a random stranger.
- **Friend rate:** jobs from friends pay `FriendBonus` more (default +15%). Enemies aren't
  picked as clients.
- **Jobs build bonds:** a job turned in adds kindness to the client's view of Rachel, and the
  Client history label. An abandoned job adds animosity.
- Stretch: an "Ask for a Favor" line with friends (a small loan, or an item they have),
  limited by their kindness and a cooldown.

Config: `FavorChance`, `FriendBonus`, `JobBond`, `AbandonHurts`.

## Build order

1. **Thaw.** One postfix; unsticks Isabel-style relationships right away.
2. **Drift.** A tick and the focus trap.
3. **Gossip.** Same hook as Thaw, plus stranger creation and logging.
4. **History.** Data injection, labels, reminisce lines.
5. **Favors.** Touches gigs and money, so it's the riskiest and goes last.

OstraScope grows with it, like it does for Romantic Flexibility's verdicts: `social` shows
what the next exchange would thaw or cool, drift since last contact, rumors heard and from
whom, and history labels.

## Open questions

- Who writes the reminisce lines and history label names. Everything players read lives in
  `src/Text.cs`; the strings there now are placeholders.
- Should gossip ever reach the player character's feelings? It's off by default for now
  (`Gossip.ReachesPlayer`).
