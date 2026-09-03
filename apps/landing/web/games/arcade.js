// The arcade: one descriptor per classic game, driving that game's own wasm
// module. index.html renders these; apps/games/ar-verify.mjs grades them
// headlessly, which is why the descriptors live in a module and not inline
// in the page. A renderer that only works when a human is watching is a
// renderer nobody checks.
//
// TWO STATE CONTRACTS, and getting them backwards is silent corruption.
// TicTacToe threads the whole game through one i32, so the page may reset
// the module heap before every call. Every other game passes a HANDLE: the
// i32 IS a heap address, so the heap must NOT be reset between calls. It is
// reset when a new game starts, and only there. apps/games/build-wasm.ps1
// carries the census.
//
// There is no GC in these modules: they bump-allocate and free nothing, so
// every step of a long autoplay is permanent until the next reset. That is
// what `steps` bounds, and what makes `runs` (a whole game inside one call)
// the cheaper arm where the module offers one.

const RANK = ['2', '3', '4', '5', '6', '7', '8', '9', '10', 'J', 'Q', 'K', 'A'];
const SUIT = ['♠', '♥', '♦', '♣'];

export const card = c => c < 0 ? '--'
  : `${RANK[c % 13]}${SUIT[Math.floor(c / 13) % 4]}`;
export const red = c => c >= 0 && [1, 2].includes(Math.floor(c / 13) % 4);

const grid = (cols, cells) => ({ kind: 'grid', cols, cells });
const rows = r => ({ kind: 'rows', rows: r.filter(Boolean) });
// `i` is the index a click on this cell reports. Grid cells get their grid
// position by default; a row or column cell has no natural index, so a
// clickable one has to say what it stands for (a mancala pit, a spider
// column) or it is decoration.
const cell = (text, cls, i, style) =>
  ({ text: text === 0 ? '0' : (text || ''), cls: cls || '', i, style });
const seq = n => [...Array(n).keys()];

// A hand as one row of card chips.
const hand = (n, at) => seq(n).map(at).map(c => cardCell(c));

export const cardCell = (c, extra) =>
  ({ text: card(c), cls: 'card' + (red(c) ? ' red' : '') + (extra ? ' ' + extra : '') });

// --- Spider's two numbers -------------------------------------------------
// A spider click has to name a CARD, not just a column, or a sub-stack can
// never be picked. Cards report `SP_CARD + column * 1000 + index`; a bare
// 0 to 9 is still the column itself, which is what the column's own padding
// and an empty column report.
const SP_CARD = 10000;
export const spCardCode = (c, i) => SP_CARD + c * 1000 + i;
const spCol = code => (code >= SP_CARD ? Math.floor((code - SP_CARD) / 1000) : code);
const spIdx = code => (code >= SP_CARD ? (code - SP_CARD) % 1000 : -1);

// The tail of a column that can be lifted as one piece: the longest run of
// descending same-suit cards ending at the last card.
//
// `sp_seqlen` measures FORWARD from an index to the end of the run starting
// there, so reading it at the LAST index answers 1 for every column in every
// position. The page computed its pick-up point as `n - sp_seqlen(n - 1)`,
// which is therefore always `n - 1`: only ever the single top card, whatever
// was sitting under it. That is one call away from correct and it made the
// game unplayable, because Spider is almost entirely the moving of runs.
// The run start is the lowest index whose own run reaches the end.
const spRunStart = (e, h, c, n) => {
  let s = n - 1;
  while (s > 0 && e.sp_seqlen(h, c, s - 1) === n - (s - 1)) s--;
  return s;
};

// Whether what is being carried can land on column `c`, asked the same way
// the drop itself asks it: an exact pick-up means that card and no other, a
// column pick-up means the largest tail the destination will take.
const spFits = (e, h, sl, c) => {
  if (sl.exact) return e.sp_can(h, sl.col, sl.start, c) === 1;
  const n = e.sp_coln(h, sl.col);
  for (let s = sl.start; s < n; s++) if (e.sp_can(h, sl.col, s, c) === 1) return true;
  return false;
};

// --- Klondike's click targets --------------------------------------------
// Klondike has more places to click than a column index can name: seven
// columns, the cards inside them, a stock, a waste and four foundations. So
// the page names them all in one number, and `kdWhere` turns that back into
// the pair the engine wants -- `from` (0 to 6 a column, 7 the waste, 8 to 11
// a foundation) and `to` (0 to 6 a column, 7 to 10 a foundation). The two
// are deliberately NOT the same numbering, which is a trap the engine's own
// `kd-can-move` carries a note about.
const KD_STOCK = 90, KD_WASTE = 91, KD_FOUND = 92, KD_CARD = 10000;
const kdCardCode = (c, i) => KD_CARD + c * 1000 + i;

// The largest tail of a column that can be lifted as one piece: face up,
// descending, alternating colour, and reaching the end.
const kdRunStart = (e, h, c, n) => {
  const d = e.kd_down(h, c);
  let s = n - 1;
  while (s > d && e.kd_runlen(h, c, s - 1) === n - (s - 1)) s--;
  return s;
};

// One draw after a turn of the stock, the cards that moved carry `dealt`.
let kdDealt = false;

// War's only click target: your own deck.
const WR_DECK = 0;

// Liar's Dice: the face you are bidding on, and the quantity you say. A bid
// is qty and face together, so the face is held while you choose the number
// and the number click is what commits it.
const LD_FACE = 600, LD_QTY = 610;
let ldFace = 1;
// What the last call turned out to be, so the board can say whether the
// dice were really there. Cleared at a new game and at each new bid.
let ldSaid = null;

// Crazy Eights: a card in your hand, and the four suits an eight can call.
// An eight is held here between the two clicks it takes, and cleared at a
// new game so a fresh deal never opens mid-question.
const CE_CARD = 500, CE_SUIT = 560;
let ceEight = -1;

// Mastermind: four pegs being set, and the six colours to set them from.
// A code is four base-six digits packed low-first, which is what the
// engine's own `mm-digit` reads back.
const MM_SLOT = 400, MM_COLOUR = 410;
let mmPegs = [0, 0, 0, 0], mmAt = 0, mmLog = [];

// Battleship's players are ONE-BASED in the module: every one of its
// accessors tests `player == 1` and falls through to player two, so 0 is
// not "player one", it is the else branch. You are player one.
const BS_YOU = 1, BS_THEM = 2;

// Life's three stamps, in the engine's own `lf-wasm-place` kind order.
// 0 in `lfStamp` means a click toggles one cell; 1 to 3 mean it stamps that
// pattern with the click at its top-left. Reset is not needed at a new
// game: what you are holding is a tool, not a position.
const LF_KIND = ['glider', 'blinker', 'block'];
let lfStamp = 0;

// Set is picked three cards at a time, which the driver's single-square
// `sel` cannot carry. Reset at a new game.
let sgPick = [];

// Go Fish asks two questions per turn, a rank and a player, so its clicks
// come from two ranges. Ranks are the engine's own 0 to 12 (`gf-rank` is
// `card mod 13`, the same ace-high order the page's RANK table uses).
const GF_RANK = 300, GF_WHO = 320;
// What the last answer was, so the board can say "go fish" instead of only
// moving a count. Cleared at a new game.
let gfSaid = null;

// Sudoku's digit picker sits above the grid, so its clicks have to be told
// apart from the 81 squares: a code at or above SD_DIGIT is a digit.
const SD_DIGIT = 100;
// Which digit is in hand, and which squares were GIVEN. The givens are the
// puzzle's own non-zero cells read once at the deal, because the engine
// holds one grid and does not mark which of it was given -- and a player
// who can overwrite a given is not solving the puzzle they were handed.
let sdDigit = 1, sdGiven = new Set();

// Mahjong's 36 tile types drawn as tiles rather than as the type NUMBER the
// page printed before. Unicode's Mahjong Tiles block runs from U+1F000 and
// covers all 36 the engine deals (144 tiles, four of each).
const MJ_FACE = t => (t >= 0 && t < 36 ? String.fromCodePoint(0x1F000 + t) : '');

// Yahtzee's click targets and its scorecard, in the engine's own category
// order (Yahtzee.codex `yh-score-category`: 0 to 5 the upper section, then
// three of a kind, four of a kind, full house, the two straights, Yahtzee
// and chance). Reading the names off this list rather than off a second
// table is the point -- five games in this arcade have shipped a wrong
// label because a meaning was taken from a name instead of the wrapper.
const YH_DIE = 200, YH_CAT = 210;
const YH_CATS = ['1s', '2s', '3s', '4s', '5s', '6s',
  '3-kind', '4-kind', 'house', 'sm run', 'lg run', 'YAHTZEE', 'chance'];
const DIE_FACE = [null, '⚀', '⚁', '⚂', '⚃', '⚄', '⚅'];
// Which dice are held, as five bits, and how many of the turn's three rolls
// have been spent. Both are facts about the turn in progress rather than
// about the game, so they live here and reset at a new game.
let yhKeep = 0, yhRolls = 1;

// Rock, paper, scissors, in the engine's own move order (RPS.codex
// `rps-beats`: 0 rock, 1 paper, 2 scissors).
const RPS_GLYPH = ['✊', '✋', '✌'];
const RPS_NAME = ['rock', 'paper', 'scissors'];
// The last round played by hand, so the page can say what just happened
// rather than only moving a tally. Cleared at a new game.
let rpLast = null;

// One draw after a deal the ten new cards carry `dealt` and land. It is a
// one-shot: the view clears it as it reads it, because a class left on
// would replay the deal on every repaint after it.
let spDealt = false;

// A hand read out of a "do you hold this card" test, which is how the
// trick-taking engines expose one. Sorted by suit then rank so it reads
// like a hand somebody is holding rather than deck order.
const held = (n, has) => {
  const cards = [];
  for (let c = 0; c < n; c++) if (has(c)) cards.push(c);
  cards.sort((a, b) => Math.floor(a / 13) - Math.floor(b / 13) || (a % 13) - (b % 13));
  return cards.length ? cards.map(c => cardCell(c)) : [cell('out', 'chip')];
};

const PLAYERS = ['P1', 'P2', 'P3', 'P4'];

// Pinochle does not use the 52-card encoding the rest of this file assumes,
// so `card` renders its ids as the wrong rank in the wrong suit. Its own is
// suit * 12 + copy * 6 + rank over six ranks, and its suit order (Pinochle
// .codex: 0 clubs, 1 diamonds, 2 hearts, 3 spades) is the reverse of SUIT.
const PIN_SUIT = ['♣', '♦', '♥', '♠'];
const PIN_RANK = ['9', 'J', 'Q', 'K', '10', 'A'];
const pinCard = c => c < 0 ? '--'
  : `${PIN_RANK[c % 6]}${PIN_SUIT[Math.floor(c / 12)]}`;
const pinCell = (c, extra) => ({
  text: pinCard(c),
  cls: 'card' + ([1, 2].includes(Math.floor(c / 12)) && c >= 0 ? ' red' : '')
    + (extra ? ' ' + extra : ''),
});
// Seats at a bridge table, in the order Bridge.codex numbers them.
const BR_SEAT = ['North', 'South', 'East', 'West'];

// Poker.codex stages: two betting rounds around one draw.
const STAGE = ['first bets', 'the draw', 'last bets', 'shown down'];

// Poker.codex ranks hands 0 to 8, low to high.
const HANDS = ['high card', 'a pair', 'two pair', 'trips', 'a straight',
  'a flush', 'a full house', 'quads', 'a straight flush'];

// PokerVariants.codex, the order pvw-variant and pvt-new both map.
const PV_NAMES = ['five card draw', 'five card stud', 'seven card stud',
  'baseball', 'hi chicago', 'low chicago', 'follow the queen',
  'jacks or better'];

// RoyalUr.codex, ur-is-rosette: step 4, 8 or 14.
const UR_ROSETTE = [4, 8, 14];

// Backgammon: current-player 0 is White and moves first, so that is you.
// White runs from point 24 down to 1, which puts its home board and its
// bear-off tray at the bottom right, where the player sitting there expects
// them. The bar and the tray are moves, so they get indices past the 24
// points rather than being crammed into them.
const BG_YOU = 0, BG_BAR = 100, BG_OFF = 101;
// Index of the Roll action in backgammon's `actions` list. The board draws
// that button itself, so the two have to agree on which one it is.
const BG_ROLL = 0;

// EVERY winner code in this file is taken from the engine's own
// format-*-result function, which is the only place the convention is
// written down, and they do NOT agree with each other: Othello and Connect
// Four number their players from one, Checkers and Backgammon and Pinochle
// from zero, and a draw is 0 in the first group and -1 in the second.
// Indexing a label array by a code from the wrong group is how a won game
// came out as "a draw" -- checkers winner 0 is SOUTH, not nobody.
const named = (code, map, fallback) =>
  Object.prototype.hasOwnProperty.call(map, code) ? map[code] : fallback;

export const GAMES = [
  {
    id: 'tictactoe', name: 'Tic-Tac-Toe', cat: 'Board', icon: '❌',
    desc: 'Perfect play. The search reads every line to the end, so it has never lost one.',
    flat: true,
    boot: e => e.ttt_new(),
    // ttt_ai answers the NEXT PACKED BOARD, not a square. Reading it as a
    // move and feeding it to ttt_play returns the board unchanged, which
    // renders as a game that never starts.
    step: (e, h) => { const n = e.ttt_ai(h); return n === h ? null : n; },
    done: (e, h) => e.ttt_done(h) === 1,
    status: (e, h) => e.ttt_done(h) === 1
      ? named(e.ttt_winner(h), { 1: 'X takes it', 2: 'O takes it' },
        'A draw, which is the best anyone gets against a perfect search')
      : (e.ttt_cur(h) === 1 ? 'X to move (you)' : 'O to move'),
    // The drawn grid, the way it is on paper: no boxes, just the two
    // strokes each way, so the marks sit in the spaces.
    view: (e, h) => ({
      kind: 'grid', cols: 3, board: 'ttt',
      cells: seq(9).map(i => {
        const v = e.ttt_cell(h, i);
        return cell(['', '×', '○'][v], 'ttt big ' + ['', 'x', 'o'][v]
          + ' r' + Math.floor(i / 3) + ' c' + (i % 3));
      }),
    }),
    human: 1,
    turn: (e, h) => e.ttt_cur(h),
    // The pointer carries your mark and paints it into the square.
    ghost: (e, h, i) => e.ttt_cell(h, i) !== 0 ? null
      : { i, cls: 'ttt big x', text: '×', carry: 'mark' },
    // Tic-Tac-Toe is the one game whose state is a plain packed integer
    // rather than a heap address, so comparing before and after IS a valid
    // refusal test here. It is not valid for any other game in this file.
    move: (e, h, i) => {
      if (e.ttt_cell(h, i) !== 0) return null;
      const n = e.ttt_play(h, i);
      return n === h ? null : { handle: n };
    },
  },
  {
    id: 'royalur', name: 'Royal Game of Ur', cat: 'Board', icon: '\u{1F3DB}',
    desc: 'The oldest rules we can still play, from about 2600 BCE. Rosettes, captures, tetrahedral dice.',
    boot: e => e.ur_new(),
    // Takes the turn's roll from the driver when there is one, so a watcher
    // sees the number the move followed from instead of a second, unrelated
    // throw made inside the step.
    step: (e, h, r, roll) => {
      const n = (roll === null || roll === undefined) ? e.ur_roll(r()) : roll;
      const m = e.ur_ai(h, n);
      return { handle: m < 0 ? e.ur_pass(h) : e.ur_play(h, m, n), roll: null };
    },
    done: (e, h) => e.ur_done(h) === 1,
    // ur_piece and ur_scored take a ONE-BASED player: the wrapper reads
    // `if player == 1 then p1-pieces else p2-pieces`, so 0 falls to the else
    // and silently answers player 2. Passing 0 and 1 swapped the two sides
    // and labelled them backwards, and ur_cur is 1-based too, so adding one
    // to it named the wrong player on every turn.
    status: (e, h, s, sel, roll) => e.ur_done(h) === 1
      ? `Player ${e.ur_winner(h)} gets all seven home`
      : `Player ${e.ur_cur(h)} to move`
      + (roll !== null && roll !== undefined
        ? (roll === 0 ? ' · rolled 0, no move' : ` · rolled ${roll}`) : '')
      + ` · ${e.ur_scored(h, 1)}-${e.ur_scored(h, 2)} home · ${e.ur_moves(h)} moves`,
    // RoyalUr.codex: a path of 14 steps. 1-4 private start, 5-12 the shared
    // middle, 13-14 private exit, 15 scored, 0 not yet entered. Rosettes
    // are steps 4, 8 and 14, which is why they sit where they do below.
    //
    // The real board is three rows of eight with two squares missing from
    // each player's row. A player's four start squares run 4,3,2,1 to the
    // left so that step 4 meets step 5 at the same column, and the two exit
    // squares are 14,13 on the right, so the whole fourteen-step path is
    // continuous across the board rather than being a list of numbers.
    view: (e, h, s, sel, roll) => {
      const at = p => {
        const m = {};
        for (let i = 0; i < 7; i++) m[e.ur_piece(h, p, i)] = i;
        return m;
      };
      const p1 = at(1), p2 = at(2);
      const priv = [4, 3, 2, 1, null, null, 14, 13];
      const mid = [5, 6, 7, 8, 9, 10, 11, 12];
      const sqr = (step, owner) => {
        if (step === null) return { gap: true };
        const mine = owner === 1 ? p1 : owner === 2 ? p2 : null;
        let who = 0, idx;
        if (mine && mine[step] !== undefined) { who = owner; idx = mine[step]; }
        else if (owner === 0) {
          if (p1[step] !== undefined) { who = 1; idx = p1[step]; }
          else if (p2[step] !== undefined) { who = 2; }
        }
        const playable = who === 1 && roll ? e.ur_can(h, idx, roll) === 1 : false;
        return {
          step, who, rosette: UR_ROSETTE.includes(step),
          i: who === 1 ? idx : undefined, playable,
        };
      };
      const waiting = p => seq(7).filter(i => e.ur_piece(h, p, i) === 0);
      const w1 = waiting(1);
      return {
        kind: 'ur',
        top: priv.map(st => sqr(st, 2)),
        mid: mid.map(st => sqr(st, 0)),
        bottom: priv.map(st => sqr(st, 1)),
        trays: {
          youWaiting: w1.length, youHome: e.ur_scored(h, 1),
          themWaiting: waiting(2).length, themHome: e.ur_scored(h, 2),
          // Clicking the tray enters the next piece, so it needs to name one.
          enter: w1.length && roll ? w1.find(i => e.ur_can(h, i, roll) === 1) : undefined,
        },
        roll,
      };
    },
    human: 1,
    turn: (e, h) => e.ur_cur(h),
    // The die comes first: without this turn's roll a piece is not a move,
    // it is just a piece. beginTurn puts the number on the table and the
    // click then names which of the seven to advance by it.
    beginTurn: (e, h, rand) => e.ur_roll(rand()),
    move: (e, h, i, st) => {
      if (st.roll === null || st.roll === undefined) return null;
      if (st.roll === 0) return { handle: e.ur_pass(h) };
      return e.ur_can(h, i, st.roll) === 1 ? { handle: e.ur_play(h, i, st.roll) } : null;
    },
    actions: [{
      label: 'Pass', run: (e, h) => ({ handle: e.ur_pass(h) }),
    }],
  },
  {
    id: 'othello', name: 'Othello', cat: 'Board', icon: '●',
    desc: '8x8 Reversi. Outflank a line of discs and every one of them turns.',
    boot: e => e.ot_new(),
    step: (e, h) => { const m = e.ot_ai(h); return m < 0 ? null : e.ot_place(h, m); },
    done: (e, h) => e.ot_done(h) === 1,
    status: (e, h) => e.ot_done(h) === 1
      ? `${named(e.ot_winner(h), { 1: 'Black takes it', 2: 'White takes it' }, 'A draw')} · ${e.ot_black(h)}-${e.ot_white(h)}`
      : `${e.ot_player(h) === 1 ? 'Black' : 'White'} to move · ${e.ot_black(h)}-${e.ot_white(h)} · move ${e.ot_moves(h)}`,
    view: (e, h) => grid(8, seq(64).map(i =>
      cell('', 'felt ' + ['', 'disc black', 'disc white'][e.ot_cell(h, i)]
        + (e.ot_legal(h, i) === 1 ? ' hint' : '')))),
    human: 1,
    turn: (e, h) => e.ot_player(h),
    move: (e, h, i) => e.ot_legal(h, i) === 1 ? { handle: e.ot_place(h, i) } : null,
    ghost: (e, h, i) => e.ot_legal(h, i) === 1
      ? { i, cls: 'felt disc black', carry: 'disc black', flips: e.ot_flips(h, i) } : null,
  },
  {
    id: 'connect4', name: 'Connect Four', cat: 'Board', icon: '\u{1F534}',
    desc: 'The AI takes a win, blocks a loss, and otherwise favours the centre.',
    boot: e => e.c4_new(),
    step: (e, h) => { const m = e.c4_ai(h); return m < 0 ? null : e.c4_drop(h, m); },
    done: (e, h) => e.c4_done(h) === 1,
    status: (e, h) => e.c4_done(h) === 1
      ? (e.c4_winner(h) ? `${['', 'Red', 'Yellow'][e.c4_winner(h)]} connects four` : 'A full board and no line')
      : `${e.c4_cur(h) === 1 ? 'Red' : 'Yellow'} to drop`,
    // Row 0 is the top and a drop lands at row 5, so the grid renders in
    // index order and gravity already runs the way the eye expects.
    view: (e, h) => grid(7, seq(42).map(i =>
      cell('', 'felt ' + ['', 'disc red', 'disc yellow'][e.c4_cell(h, Math.floor(i / 7), i % 7)]
        + (e.c4_can(h, i % 7) === 1 ? ' hint' : '')))),
    human: 1,
    turn: (e, h) => e.c4_cur(h),
    move: (e, h, i) => e.c4_can(h, i % 7) === 1 ? { handle: e.c4_drop(h, i % 7) } : null,
    // Gravity is the mechanic here and nowhere else, so this is the only
    // game whose pieces arrive by falling.
    land: 'drop',
    // Hovering a column shows the chip where it would COME TO REST, not
    // under the pointer, because that is the question a player is asking.
    // `from` is how many rows it falls, which the page animates.
    ghost: (e, h, i) => {
      const c = i % 7;
      if (e.c4_can(h, c) !== 1) return null;
      const row = 5 - e.c4_height(h, c);
      return { i: row * 7 + c, cls: 'disc red', from: row + 1, carry: 'disc red' };
    },
  },
  {
    id: 'checkers', name: 'Checkers', cat: 'Board', icon: '⛀',
    desc: 'English draughts, king promotions, minimax opponent.',
    boot: e => e.ck_new(),
    step: (e, h) => { const m = e.ck_ai(h); return m < 0 ? null : e.ck_apply(h, m); },
    done: (e, h) => e.ck_done(h) === 1,
    // Checkers.codex: winner 0 is South, 1 is North, anything else a draw.
    status: (e, h) => e.ck_done(h) === 1
      ? named(e.ck_winner(h), { 0: 'You win: South takes it', 1: 'North takes it' },
        'Neither side can force it: a draw')
      : `${e.ck_turn(h) === 0 ? 'South (you)' : 'North'} to move · ${e.ck_moves(h)} legal`,
    // South is 1 and 2, sits on rows 5 to 7, and moves first, so the user is
    // South and is already at the bottom of the board as rendered.
    view: (e, h, s, sel) => grid(8, seq(64).map(i => {
      const dark = ((Math.floor(i / 8) + i % 8) % 2) === 1;
      const v = e.ck_cell(h, i);
      const dest = sel !== null && sel !== undefined && ckMove(e, h, sel, i) >= 0;
      // The piece is drawn, not typed. A '●' glyph renders at whatever size
      // the font feels like and sat tiny in the square.
      return cell('',
        (dark ? 'sq-dark ' : 'sq-light ') + ['', 'ckp s', 'ckp s k', 'ckp n', 'ckp n k'][v]
        + (i === sel ? ' picked' : '') + (dest ? ' hint' : ''));
    })),
    human: 0,
    turn: (e, h) => e.ck_turn(h),
    // A checker slides from where it was to where it went. It does not fall.
    land: 'slide',
    // A checkers move is a source and a destination, and the module names
    // moves by INDEX, so the page has to find the index whose from and to
    // match the two squares the user clicked.
    move: (e, h, i, st) => {
      if (st.sel === null || st.sel === undefined) {
        const owns = [1, 2].includes(e.ck_cell(h, i));
        return owns && ckFrom(e, h, i) ? { sel: i } : null;
      }
      if (i === st.sel) return { sel: null };
      const m = ckMove(e, h, st.sel, i);
      if (m >= 0) return { handle: e.ck_apply(h, m) };
      const owns = [1, 2].includes(e.ck_cell(h, i));
      return owns && ckFrom(e, h, i) ? { sel: i } : null;
    },
  },
  {
    id: 'go', name: 'Go', cat: 'Board', icon: '⚫',
    desc: '9x9 with area scoring and the Ko rule.',
    boot: e => e.go_new(),
    step: (e, h, r) => { const m = e.go_ai(h, r()); return m < 0 ? e.go_pass(h) : e.go_place(h, m); },
    done: (e, h) => e.go_done(h) === 1,
    status: (e, h) => {
      const b = e.go_score(h, 1), w = e.go_score(h, 2);
      return (e.go_done(h) === 1 ? `${b > w ? 'Black' : 'White'} leads the board` : `${e.go_cur(h) === 1 ? 'Black' : 'White'} to play`)
        + ` · B ${b} W ${w} · captures ${e.go_captures(h, 1)}/${e.go_captures(h, 2)}`;
    },
    view: (e, h) => grid(9, seq(81).map(i =>
      cell('', 'goban ' + ['', 'stone black', 'stone white'][e.go_cell(h, i)]))),
    human: 1,
    turn: (e, h) => e.go_cur(h),
    // Go exports no legality test, and a HANDLE CANNOT BE COMPARED to find
    // one: every call allocates a fresh state, so `place(h,i) === h` is
    // never true and a refused move reads as a move made. The board is the
    // only witness -- if the point does not hold your stone afterwards, the
    // rules refused it (occupied, ko, or self-capture).
    move: (e, h, i) => {
      if (e.go_cell(h, i) !== 0) return null;
      const me = e.go_cur(h);
      const n = e.go_place(h, i);
      return e.go_cell(n, i) === me ? { handle: n } : null;
    },
    actions: [{ label: 'Pass', run: (e, h) => ({ handle: e.go_pass(h) }) }],
    ghost: (e, h, i) => e.go_cell(h, i) !== 0 ? null
      : { i, cls: 'goban stone black', carry: 'stone black' },
  },
  {
    id: 'hexgame', name: 'Hex', cat: 'Board', icon: '⬢',
    desc: '11x11. Connect your two edges. A finished Hex board always has exactly one winner.',
    boot: e => e.hx_new(),
    step: (e, h) => { const m = e.hx_ai(h); return m < 0 ? null : e.hx_place(h, m); },
    done: (e, h) => e.hx_done(h) === 1,
    status: (e, h) => e.hx_done(h) === 1
      ? `Player ${e.hx_winner(h)} joins ${e.hx_winner(h) === 1 ? 'top to bottom' : 'left to right'} in ${e.hx_moves(h)} moves`
      : `Player ${e.hx_cur(h)} to place · ${e.hx_moves(h)} moves`,
    view: (e, h) => ({
      kind: 'hex', cols: 11,
      cells: seq(121).map(i => cell('', ['', 'p1', 'p2'][e.hx_cell(h, i)])),
    }),
    human: 1,
    turn: (e, h) => e.hx_cur(h),
    move: (e, h, i) => e.hx_can(h, i) === 1 ? { handle: e.hx_place(h, i) } : null,
    ghost: (e, h, i) => e.hx_can(h, i) === 1 ? { i, cls: 'p1', carry: 'hexchip' } : null,
  },
  {
    id: 'mancala', name: 'Mancala', cat: 'Board', icon: '\u{1F95C}',
    desc: 'Kalah: sowing, capture, and the extra turn that lands in your own store.',
    boot: e => e.mc_new(),
    step: (e, h) => { const m = e.mc_ai(h, 4); return m < 0 ? null : e.mc_move(h, m); },
    done: (e, h) => e.mc_done(h) === 1,
    status: (e, h) => {
      const s = e.mc_south(h), n = e.mc_north(h);
      return (e.mc_done(h) === 1 ? `${s > n ? 'South' : n > s ? 'North' : 'Nobody'} wins ${s}-${n}`
        : `${e.mc_turn(h) === 0 ? 'South' : 'North'} to sow · ${s}-${n}`);
    },
    // South is pits 0-5 and moves first, so the user is South and South is
    // the BOTTOM row. North's pits run 12 down to 7 so the two rows face
    // each other the way they do on a real board.
    // A carved board, not two lines of buckets: north's six pits above
    // south's six, a store at each end, stones drawn in the cup.
    view: (e, h) => ({
      kind: 'mancala',
      north: [12, 11, 10, 9, 8, 7].map(i => ({ n: e.mc_pit(h, i) })),
      south: [0, 1, 2, 3, 4, 5].map(i =>
        ({ n: e.mc_pit(h, i), i, legal: e.mc_legal(h, i) === 1 })),
      stores: { north: e.mc_north(h), south: e.mc_south(h) },
    }),
    human: 0,
    turn: (e, h) => e.mc_turn(h),
    move: (e, h, i) => e.mc_legal(h, i) === 1 ? { handle: e.mc_move(h, i) } : null,
  },
  {
    id: 'backgammon', name: 'Backgammon', cat: 'Board', icon: '\u{1F3B2}',
    desc: '24 points, the bar, and bearing off.',
    boot: e => e.bg_new(),
    // Backgammon.codex: "Dice are 2d6; doubles = 4 moves." bg_step spends
    // ONE die, so a turn is a queue of two, or four on a double, and the
    // page spends them one at a time so each checker is seen to move.
    beginTurn: (e, h, rand) => {
      const a = e.bg_die(rand()), b = e.bg_die(rand());
      return { dice: [a, b], queue: a === b ? [a, a, a, a] : [a, b], spent: [] };
    },
    step: (e, h, rand, roll) => {
      if (!roll || !roll.queue.length) return null;
      const die = roll.queue[0];
      const moved = e.bg_step(h, die);
      const rest = {
        dice: roll.dice, queue: roll.queue.slice(1), spent: roll.spent.concat(die),
      };
      // Turn over when the dice are spent.
      return rest.queue.length
        ? { handle: moved, roll: rest }
        : { handle: e.bg_endturn(moved), roll: null };
    },
    done: (e, h) => e.bg_done(h) === 1,
    // Backgammon.codex: winner 0 is White, 1 is Black. Indexing a label
    // array by that put an EMPTY name in front of "bears off last".
    status: (e, h) => e.bg_done(h) === 1
      ? `${named(e.bg_winner(h), { 0: 'White', 1: 'Black' }, 'Nobody')} bears off last`
      : `${e.bg_cur(h) === 0 ? 'White' : 'Black'} to move · off ${e.bg_off(h, 0)}/${e.bg_off(h, 1)} · bar ${e.bg_bar(h, 0)}/${e.bg_bar(h, 1)}`,
    // A real board, drawn by the page. The photographic one that used to
    // stand in for this is the one Damian called out as looking bad, so
    // backgammon is the one game here with no scene behind it and a board
    // built out of its own points instead.
    //
    // A point holds a signed count: positive is White, negative is Black.
    // The bottom row runs 12 down to 1 right to left and the top row 13 up
    // to 24, which is how a real board reads, and it puts the user's home
    // board at the bottom right.
    view: (e, h, s, sel, roll) => {
      const dice = roll ? roll.queue : [];
      const mine = e.bg_cur(h) === BG_YOU;
      // A point you could move FROM with any die still in hand, or, once
      // one is picked up, a point that die could land it on.
      const from = p => mine && dice.some(d => e.bg_can(h, p, d) === 1);
      const dieFor = (a, b) => BG_YOU === 0 ? a - b : b - a;
      const dest = p => {
        if (!mine || sel === null || sel === undefined) return false;
        const d = dieFor(sel, p);
        return dice.includes(d) && e.bg_can(h, sel, d) === 1;
      };
      const pt = p => ({
        n: e.bg_point(h, p), i: p,
        hint: sel === null || sel === undefined ? from(p) : dest(p),
        picked: p === sel,
      });
      return {
        kind: 'backgammon',
        top: seq(12).map(i => pt(i + 12)),
        bottom: seq(12).map(i => pt(11 - i)),
      // bg_bar and bg_off read `if player == 0 then white else black`, so
      // the players are 0 and 1. Asking for 1 and 2 answered BLACK twice
      // and White never, in both the bar and the tray.
      bar: [e.bg_bar(h, 0), e.bg_bar(h, 1)],
      off: [e.bg_off(h, 0), e.bg_off(h, 1)],
      // The dice sit on the side of whoever is throwing them: White on the
      // left, Black on the right. Spent dice grey out as they are used.
      dice: roll ? roll.dice : null,
      spent: roll ? roll.spent : [],
        thrower: e.bg_cur(h) === 0 ? 'w' : 'b',
        // The engine implements no doubling, so the cube is board furniture
        // sitting where an untouched cube sits. The rules panel says so
        // rather than leaving a control that would do nothing.
        cube: 64,
        // Entering from the bar and bearing off are moves too, so they are
        // clickable in their own right.
        // Your throw, offered in the middle of the board. The opponent
        // rolls its own as part of its turn, so this only ever shows on
        // your side of the game.
        showRoll: mine && !roll,
        barHint: mine && dice.some(d => e.bg_canenter(h, d) === 1),
        offHint: mine && sel !== null && sel !== undefined &&
          dice.some(d => e.bg_can(h, sel, d) === 1 &&
            (BG_YOU === 0 ? sel - d < 0 : sel + d >= 24)),
      };
    },
    human: BG_YOU,
    turn: (e, h) => e.bg_cur(h),
    land: 'slide',
    // You throw when you are ready. Rolling for you the moment the turn
    // arrives takes away the one thing that makes a dice game feel like one.
    manualRoll: true,
    actions: [
      {
        label: '\u{1F3B2} Roll',
        // Drawn on the board itself, in the middle where a real player
        // throws, so it is not up in the toolbar away from the game.
        inBoard: true,
        enabled: (e, h, roll) => !roll,
        run: (e, h, rand) => {
          const a = e.bg_die(rand()), b = e.bg_die(rand());
          return {
            handle: h,
            roll: { dice: [a, b], queue: a === b ? [a, a, a, a] : [a, b], spent: [] },
          };
        },
      },
      {
        label: 'No move, pass',
        enabled: (e, h, roll) => !!roll && roll.queue.every(d => e.bg_any(h, d) === 0),
        run: (e, h) => ({ handle: e.bg_endturn(h), roll: null }),
      },
    ],
    // BAR and OFF are moves in their own right, so they get indices of
    // their own rather than being squeezed into the twenty-four points.
    move: (e, h, i, st) => {
      const dice = st.roll ? st.roll.queue : [];
      if (!dice.length) return null;
      const spend = (d, handle) => {
        const q = dice.slice();
        q.splice(q.indexOf(d), 1);
        const spent = (st.roll.spent || []).concat(d);
        return q.length
          ? { handle, roll: { dice: st.roll.dice, queue: q, spent } }
          : { handle: e.bg_endturn(handle), roll: null };
      };
      if (i === BG_BAR) {
        // Deterministic rather than queue order, so the same click always
        // enters on the same point.
        const cands = dice.filter(x => e.bg_canenter(h, x) === 1).sort((a, b) => a - b);
        return cands.length ? spend(cands[0], e.bg_enter(h, cands[0])) : null;
      }
      if (i === BG_OFF) {
        if (st.sel === null || st.sel === undefined) return null;
        // SPEND THE DIE THE MOVE ACTUALLY USES. Bearing off a checker from
        // the three-point uses the three; a five also satisfies the test,
        // and taking the first die that happens to work spent the five and
        // left the three still owed -- so the next move looked like the
        // three being spent twice. Exact die first, then the smallest that
        // will do.
        const need = BG_YOU === 0 ? st.sel + 1 : 24 - st.sel;
        const cands = dice
          .filter(x => e.bg_can(h, st.sel, x) === 1 &&
            (BG_YOU === 0 ? st.sel - x < 0 : st.sel + x >= 24))
          .sort((a, b) => a - b);
        if (!cands.length) return null;
        const d = cands.includes(need) ? need : cands[0];
        return spend(d, e.bg_move(h, st.sel, d));
      }
      if (st.sel === null || st.sel === undefined) {
        return dice.some(d => e.bg_can(h, i, d) === 1) ? { sel: i } : null;
      }
      if (i === st.sel) return { sel: null };
      const want = BG_YOU === 0 ? st.sel - i : i - st.sel;
      if (dice.includes(want) && e.bg_can(h, st.sel, want) === 1) {
        return spend(want, e.bg_move(h, st.sel, want));
      }
      return dice.some(d => e.bg_can(h, i, d) === 1) ? { sel: i } : null;
    },
    steps: 600,
  },

  {
    id: 'game2048', name: '2048', cat: 'Puzzle', icon: '\u{1F522}',
    desc: 'Slide and merge on a 4x4 grid.',
    boot: (e, s) => e.g2_new(s),
    step: (e, h) => { const m = e.g2_ai(h); return m < 0 ? null : e.g2_move(h, m); },
    done: (e, h) => e.g2_done(h) === 1,
    status: (e, h) => `score ${e.g2_score(h)} · best tile ${e.g2_max(h)} · ${e.g2_moves(h)} moves`
      + (e.g2_done(h) === 1 ? ' · no move left' : ''),
    view: (e, h) => grid(4, seq(16).map(i => {
      const v = e.g2_cell(h, i);
      return cell(v || '', 'tile big t' + (v <= 2048 ? v : 'big'));
    })),
    solo: true,
    keys: { ArrowUp: 0, ArrowRight: 1, ArrowDown: 2, ArrowLeft: 3 },
    key: (e, h, d) => e.g2_can(h, d) === 1 ? e.g2_move(h, d) : null,
  },
  {
    id: 'life', name: "Conway's Life", cat: 'Puzzle', icon: '\u{1F9EC}',
    desc: '20x20 toroidal, B3/S23. You do not play against it, you draw the position it starts from: click cells, stamp a glider, then let it run.',
    boot: (e, s) => e.lf_new(s),
    step: (e, h) => e.lf_step(h),
    // Life has no end. It also has no opponent, so `solo` is what makes the
    // board clickable at all.
    done: () => false,
    solo: true,
    status: (e, h) => `${e.lf_alive(h)} alive`
      + (lfStamp ? ` · stamping a ${LF_KIND[lfStamp - 1]}` : ' · click a cell to turn it on'),
    view: (e, h) => grid(20, seq(400).map(i =>
      cell('', e.lf_cell(h, Math.floor(i / 20), i % 20) ? 'life on' : 'life off'))),
    // A click is one cell, or the top-left corner of a pattern when one is
    // held. The engine's own three stamps are what the seeded grid is built
    // from, so a player gets the same vocabulary it has.
    move: (e, h, i) => {
      if (i < 0 || i >= 400) return null;
      const r = Math.floor(i / 20), c = i % 20;
      if (lfStamp) return { handle: e.lf_place(h, lfStamp - 1, r, c) };
      return { handle: e.lf_toggle(h, r, c) };
    },
    actions: [
      { label: 'Clear', run: (e) => ({ handle: e.lf_blank() }), enabled: () => true },
      ...LF_KIND.map((name, k) => ({
        label: `Stamp ${name}`,
        // A stamp is a mode, not a move: it changes what the NEXT click
        // does. It answers the same handle so the board is redrawn and the
        // status line can say what is in hand.
        run: (e, h) => { lfStamp = lfStamp === k + 1 ? 0 : k + 1; return { handle: h }; },
        enabled: () => true,
      })),
      { label: 'Draw cells', run: (e, h) => { lfStamp = 0; return { handle: h }; },
        enabled: () => lfStamp !== 0 },
    ],
    actionsInStage: true,
    steps: 300,
  },
  {
    id: 'minesweeper', name: 'Minesweeper', cat: 'Puzzle', icon: '\u{1F4A3}',
    desc: '9x9, ten mines. The AI only opens a square it can prove is safe.',
    boot: (e, s) => e.ms_new(s),
    step: (e, h) => { const m = e.ms_ai(h); return m < 0 ? null : e.ms_open(h, m); },
    done: (e, h) => e.ms_done(h) === 1,
    status: (e, h) => `${e.ms_count(h)} open · ${e.ms_hits(h)} mines hit · ${e.ms_moves(h)} moves`
      + (e.ms_done(h) === 1 ? (e.ms_won(h) === 1 ? ' · cleared' : ' · over') : ''),
    view: (e, h) => grid(9, seq(81).map(i => {
      const mine = e.ms_mine(h, i) === 1, shown = e.ms_shown(h, i) === 1;
      if (!shown) return cell(e.ms_done(h) === 1 && mine ? '\u{1F4A3}' : '', 'ms hidden');
      if (mine) return cell('\u{1F4A3}', 'ms boom');
      const a = e.ms_adj(h, i);
      return cell(a || '', 'ms open n' + a);
    })),
    solo: true,
    move: (e, h, i) => e.ms_shown(h, i) === 1 ? null : { handle: e.ms_open(h, i) },
    ghost: (e, h, i) => e.ms_shown(h, i) === 1 ? null : { i, cls: 'ms probe' },
  },
  {
    id: 'sudoku', name: 'Sudoku', cat: 'Puzzle', icon: '\u{1F9E9}',
    desc: 'A real puzzle: the grid is solved and then holes are punched in it, so every blank has a digit that belongs there. Pick a digit, then a square.',
    // The option is ATTEMPTS at punching a hole, not holes. `sudoku-remove-cells`
    // picks a random index each pass and spends the pass whether or not that
    // cell still held a digit, so asking for 45 gives about 34 blanks and the
    // exact number moves with the seed. Labelling it "45 blanks" would state a
    // number the player can count and find wrong, so it is named for the
    // difficulty it produces and the status line reports the blanks it really
    // dealt.
    options: [{
      name: 'holes', label: 'Puzzle',
      values: [36, 50, 64], labels: ['gentle', 'middling', 'hard'], def: 50,
    }],
    // `sd_new` fills only the three diagonal boxes, so it is a start and not
    // a puzzle. Solving it and THEN punching holes is what makes a grid
    // whose blanks are all fillable: the solver's answer is a real solution
    // by construction, and removing cells cannot invalidate it.
    boot: (e, s, o) => {
      const solved = e.sd_solve(e.sd_new(s));
      const puzzle = e.sd_remove(solved, s, (o && o.holes) || 50);
      sdGiven = new Set(seq(81).filter(i => e.sd_cell(puzzle, i) !== 0));
      sdDigit = 1;
      return puzzle;
    },
    // Watch mode still solves it in one step, which is the backtracking
    // solver doing what it always did.
    step: (e, h) => (e.sd_blanks(h) === 0 ? null : e.sd_solve(h)),
    done: (e, h) => e.sd_blanks(h) === 0,
    won: (e, h) => e.sd_blanks(h) === 0,
    status: (e, h) => {
      const left = e.sd_blanks(h);
      if (left === 0) return `Solved · ${e.sd_givens(h)} of 81 were given`;
      return `${left} blank${left === 1 ? '' : 's'} left · ${sdGiven.size} given`
        + ` · placing ${sdDigit}`;
    },
    view: (e, h) => {
      const cells = seq(81).map(i => {
        const v = e.sd_cell(h, i), r = Math.floor(i / 9), c = i % 9;
        const box = (Math.floor(r / 3) + Math.floor(c / 3)) % 2 ? ' shade' : '';
        const given = sdGiven.has(i);
        // A blank where the chosen digit would not go is shown as closed, so
        // the board answers "where can this go" before you click.
        const fits = !v && e.sd_fits(h, i, sdDigit) === 1;
        return cell(v || '', 'sud' + box + (given ? ' given' : v ? ' mine' : '')
          + (fits ? ' hint' : ''));
      });
      return {
        kind: 'sudoku',
        digit: sdDigit,
        digits: seq(9).map(d => ({ n: d + 1, i: SD_DIGIT + d + 1, on: sdDigit === d + 1 })),
        cells,
      };
    },
    solo: true,
    // Two kinds of click: a digit to hold, or a square to write it into.
    // Clicking a square that already holds YOUR digit clears it, which is
    // the only way back out of a mistake.
    move: (e, h, i) => {
      if (i >= SD_DIGIT) { sdDigit = i - SD_DIGIT; return { sel: null }; }
      if (i < 0 || i >= 81 || sdGiven.has(i)) return null;
      if (e.sd_cell(h, i) !== 0) return { handle: e.sd_place(h, i, 0) };
      if (e.sd_fits(h, i, sdDigit) !== 1) return null;
      return { handle: e.sd_place(h, i, sdDigit) };
    },
    steps: 2,
  },
  {
    id: 'mastermind', name: 'Mastermind', cat: 'Puzzle', icon: '\u{1F510}',
    desc: 'Four pegs from six colours, ten guesses. Black means right colour in the right place, white means right colour in the wrong one, and which is which is never said.',
    boot: (e, s) => { mmPegs = [0, 0, 0, 0]; mmAt = 0; mmLog = []; return e.mm_new(s); },
    step: (e, h) => e.mm_step(h),
    done: (e, h) => e.mm_done(h) === 1,
    won: (e, h) => e.mm_solved(h) === 1,
    status: (e, h) => {
      if (e.mm_solved(h) === 1) {
        return `Cracked in ${e.mm_guesses(h)}: ${pegs(e, e.mm_secret(h))}`;
      }
      if (e.mm_guesses(h) >= 10) return `Ten guesses gone. It was ${pegs(e, e.mm_secret(h))}`;
      return `${10 - e.mm_guesses(h)} guesses left · ${e.mm_pool(h)} codes still fit`;
    },
    // The board is the HISTORY, because Mastermind is played by reading
    // your own past guesses against their scores. The module keeps only the
    // last one, so the page keeps the list; it is the page's own record of
    // clicks and nothing the engine needs to know.
    view: (e, h) => rows([
      ['Your guess', seq(4).map(i => cell('', 'peg c' + mmPegs[i]
        + (mmAt === i ? ' picked' : ''), MM_SLOT + i))],
      ['Colour', seq(6).map(k => cell('', 'peg c' + k, MM_COLOUR + k))],
      ...mmLog.map((g, n) => [`Guess ${n + 1}`,
        seq(4).map(i => cell('', 'peg c' + e.mm_digit(g.code, i)))
          .concat([cell(`${g.black} black`, 'chip'), cell(`${g.white} white`, 'chip')])]),
      e.mm_done(h) === 1 && ['Secret',
        seq(4).map(i => cell('', 'peg c' + e.mm_digit(e.mm_secret(h), i)))],
    ]),
    solo: true,
    // Pick a slot, pick a colour for it. Guessing is an action rather than a
    // click, because the four pegs are set before anything is committed.
    move: (e, h, i) => {
      if (i >= MM_SLOT && i < MM_SLOT + 4) { mmAt = i - MM_SLOT; return { sel: null }; }
      if (i >= MM_COLOUR && i < MM_COLOUR + 6) {
        mmPegs[mmAt] = i - MM_COLOUR;
        // Walking to the next peg makes four colour clicks a whole guess.
        mmAt = (mmAt + 1) % 4;
        return { sel: null };
      }
      return null;
    },
    actions: [
      {
        label: 'Guess',
        run: (e, h) => {
          const code = mmPegs[0] + mmPegs[1] * 6 + mmPegs[2] * 36 + mmPegs[3] * 216;
          const next = e.mm_guessat(h, code);
          // The score belongs to the guess that earned it, so it is read
          // from the state AFTER the guess lands, not before.
          mmLog = mmLog.concat([{
            code, black: e.mm_blacks(next), white: e.mm_whites(next),
          }]);
          return { handle: next };
        },
        enabled: (e, h) => e.mm_canguess(h) === 1,
      },
    ],
    actionsInStage: true,
    steps: 12,
  },
  {
    id: 'mahjong', name: 'Mahjong Solitaire', cat: 'Other', icon: '\u{1F004}',
    desc: 'Match free tiles two at a time. A tile is free when nothing sits on it and one side is clear, which is the whole difficulty: the pair you want is usually buried.',
    boot: (e, s) => e.mj_new(s),
    step: (e, h) => e.mj_step(h),
    done: (e, h) => e.mj_done(h) === 1,
    won: (e, h) => e.mj_remaining(h) === 0,
    status: (e, h) => `${e.mj_matched(h)} pairs matched · ${e.mj_remaining(h)} tiles left`
      + (e.mj_remaining(h) === 0 ? ' · the board is clear'
        : e.mj_stuck(h) === 1 ? ' · stuck, nothing free matches' : ''),
    view: (e, h, s, sel, roll, seed, hint) => {
      const hm = hint === undefined ? null : hint;
      const picked = sel === null || sel === undefined ? null : sel;
      return grid(18, seq(144).map(i => {
        const t = e.mj_tile(h, i);
        // A REMOVED TILE READS 0, NOT -1. `mj-wasm-tile` answers -1 only for
        // an index off the board, and `tile-present` is `> 0`, so the test
        // was never true and every gone tile rendered the text "-1" through
        // `mj_type`'s own out-of-range answer.
        if (t <= 0) return cell('', 'tile gone');
        const free = e.mj_free(h, i) === 1;
        return cell(MJ_FACE(e.mj_type(t)),
          'tile' + (free ? ' free' : '')
          + (picked === i ? ' picked' : '')
          + (picked !== null && picked !== i && e.mj_cantake(h, picked, i) === 1 ? ' hint' : '')
          + (hm && (hm.a === i || hm.b === i) ? ' hint' : ''));
      }));
    },
    solo: true,
    // Two clicks: a free tile, then its match. Picking a tile that is not
    // free is refused rather than held, because holding it would let a
    // player build a selection that can never be spent.
    move: (e, h, i, st) => {
      if (e.mj_tile(h, i) <= 0) return null;
      const sel = st.sel;
      if (sel === null || sel === undefined) {
        return e.mj_free(h, i) === 1 ? { sel: i } : null;
      }
      if (i === sel) return { sel: null };
      if (e.mj_cantake(h, sel, i) === 1) return { handle: e.mj_take(h, sel, i) };
      return e.mj_free(h, i) === 1 ? { sel: i } : { sel: null };
    },
    hint: (e, h) => {
      const c = e.mj_pair(h);
      if (c < 0) return null;
      return { a: e.mj_paira(c), b: e.mj_pairb(c) };
    },
  },
  {
    id: 'setgame', name: 'The Set Game', cat: 'Other', icon: '\u{1F0DF}',
    desc: 'Eighty-one cards, four attributes. A set is three cards where every attribute is all-same or all-different. Find one before the machine does.',
    boot: (e, s) => { sgPick = []; return e.sg_new(s); },
    // The engine COUNTED the sets on the table and could not say where one
    // was, so this descriptor had `step: null` and never played at all.
    step: (e, h) => (e.sg_find(h) < 0 && e.sg_candeal(h) === 0 ? null : e.sg_step(h)),
    runs: (e, s) => `${e.sg_run(s)} sets found working through the deck`,
    // The game is over when no set is on the table AND there is nothing left
    // to deal, which is the real end of Set rather than a fixed card count.
    done: (e, h) => e.sg_find(h) < 0 && e.sg_candeal(h) === 0,
    won: (e, h) => e.sg_find(h) < 0 && e.sg_deckn(h) === 0,
    status: (e, h) => {
      const sets = e.sg_sets(h);
      return `${e.sg_found(h)} sets taken · ${e.sg_tabn(h)} on the table`
        + ` · ${e.sg_deckn(h)} in the deck`
        + (sets ? ` · ${sets} set${sets === 1 ? '' : 's'} here`
          : e.sg_candeal(h) === 1 ? ' · no set here, deal three more'
            : ' · no set, and the deck is out');
    },
    view: (e, h, s, sel, roll, seed, hint) => {
      const hm = hint === undefined ? null : hint;
      const picked = sgPick;
      return rows([['Tableau', seq(e.sg_tabn(h)).map(i => {
        const c = e.sg_tab(h, i);
        return cell(`${e.sg_number(c) + 1}${['●', '▲', '■'][e.sg_shape(c)]}`,
          'setcard s' + e.sg_color(c) + ' f' + e.sg_shading(c)
          + (picked.includes(i) ? ' picked' : '')
          + (hm && hm.includes(i) ? ' hint' : ''), i);
      })]]);
    },
    solo: true,
    // A set is THREE cards, so the selection is a LIST, and the driver's
    // `sel` cannot hold it: the page's two-click machinery treats `sel` as
    // one square and re-clicks it as an index to put a piece back down, so
    // an array there is passed straight back in as a click and the picks
    // corrupt. Three-card selection lives here instead, like Yahtzee's held
    // dice, and `sel` stays null.
    //
    // Clicking a picked card takes it back out, and the third click either
    // makes the set or is refused, which is the only way to learn the rule.
    move: (e, h, i) => {
      const n = e.sg_tabn(h);
      if (i < 0 || i >= n) return null;
      if (sgPick.includes(i)) { sgPick = sgPick.filter(x => x !== i); return { sel: null }; }
      if (sgPick.length < 2) { sgPick = sgPick.concat([i]); return { sel: null }; }
      const a = sgPick[0], b = sgPick[1];
      if (e.sg_cantake(h, a, b, i) === 1) {
        sgPick = [];
        return { handle: e.sg_take(h, a, b, i) };
      }
      // Three cards that are not a set: keep the newest and drop the older
      // pair, so a wrong guess costs one click rather than three.
      sgPick = [i];
      return { sel: null };
    },
    hint: (e, h) => {
      const m = e.sg_find(h);
      if (m < 0) return null;
      return [e.sg_fi(m), e.sg_fj(m), e.sg_fk(m)];
    },
    // Three cards make a move here, which the page's one-square selection
    // cannot express and a two-click harness cannot drive.
    picks: 3,
    actions: [
      {
        label: 'Deal three more',
        run: (e, h) => ({ handle: e.sg_deal(h) }),
        // Only when the table really is stuck. Offered while a set is
        // sitting there, this would let a player deal past the game.
        enabled: (e, h) => e.sg_candeal(h) === 1,
      },
    ],
    steps: 60,
  },

  {
    id: 'blackjack', name: 'Blackjack', cat: 'Card', icon: '\u{1F0CF}',
    desc: 'Basic strategy against the dealer. Aces soften.',
    boot: (e, s) => e.bj_new(s),
    step: (e, h) => e.bj_auto(h),
    // bj_result is a COMPARISON of the two totals as they stand: 1 the
    // player, 0 a push, -1 the dealer. There is no "still in" among its
    // values, so indexing a label array by it read a loss as "still in" and
    // a push as "still in", and `> 0` read a loss as a hand not yet over.
    // Whether the hand is live is the page's business, not the module's:
    // the page owns the hit-or-stand flow.
    // Solo: the hand is live until the player stands or busts, and that is
    // page state, so the driver's `settled` flag carries it.
    done: (e, h, settled) => settled === true || e.bj_bust(h) === 1,
    status: (e, h, settled) => `you ${e.bj_pvalue(h)}${e.bj_psoft(h) === 1 ? ' soft' : ''} · dealer ${e.bj_dvalue(h)}`
      + (settled || e.bj_bust(h) === 1
        ? ' · ' + (e.bj_bust(h) === 1 ? 'bust, dealer takes it'
          : { 1: 'you win', 0: 'a push', '-1': 'dealer wins' }[e.bj_result(h)])
        : ' · hit or stand'),
    // Dealer above, you below, the way it sits on a table, and the winner
    // named against whoever took it rather than only in the status line.
    view: (e, h, settled) => {
      const over = settled || e.bj_bust(h) === 1;
      const res = e.bj_bust(h) === 1 ? -1 : e.bj_result(h);
      const mark = who => !over ? '' : res === 0 ? '  --  push'
        : (res === who ? '  --  WINNER' : '');
      return rows([
        // The hole card stays face down while the hand is live. That is the
        // rule, not decoration: showing it gives away the answer.
        [`Dealer ${e.bj_dvalue(h)}${mark(-1)}`, over
          ? hand(e.bj_dcount(h), i => e.bj_dcard(h, i))
          : [cardCell(e.bj_dcard(h, 0)), { text: '', cls: 'card back' }]],
        [`You ${e.bj_pvalue(h)}${e.bj_psoft(h) === 1 ? ' soft' : ''}${mark(1)}`,
        hand(e.bj_pcount(h), i => e.bj_pcard(h, i))],
      ]);
    },
    solo: true,
    // Hit and Stand belong under your own cards, not up with New Game.
    actionsInStage: true,
    actions: [
      { label: 'Hit', run: (e, h) => ({ handle: e.bj_hit(h), settled: false }) },
      { label: 'Stand', run: (e, h) => ({ handle: e.bj_stand(h), settled: true }) },
    ],
    steps: 2,
  },
  {
    id: 'war', name: 'War', cat: 'Card', icon: '\u{1F4A5}',
    desc: 'No choices at all: the cards decide everything. What you do is turn them, and the only question is how long the deck takes to fall one way.',
    boot: (e, s) => e.wr_new(s),
    step: (e, h) => e.wr_round(h),
    // War has NO decisions in it, so there is nothing to make legal or
    // illegal and no wrapper to write: turning the card IS the whole of a
    // player's part, and the engine's own round already does it. Being
    // able to turn it yourself rather than watch it turn is the entire
    // difference between this game being playable and not.
    solo: true,
    move: (e, h, i) => (i === WR_DECK ? { handle: e.wr_round(h) } : null),
    done: (e, h) => e.wr_p1n(h) === 0 || e.wr_p2n(h) === 0,
    status: (e, h) => `${e.wr_p1n(h)} cards against ${e.wr_p2n(h)}`
      + (e.wr_p1n(h) === 0 ? ' · player 2 takes the deck'
        : e.wr_p2n(h) === 0 ? ' · player 1 takes the deck' : ''),
    // Nobody in a game of War can see their own hand, let alone the other
    // one, so laying all of it out face up was showing a thing that is not
    // on the table. Two face-down decks, and the two cards about to be
    // turned over between them.
    view: (e, h) => {
      const c1 = e.wr_p1n(h) > 0 ? e.wr_p1c(h, 0) : -1;
      const c2 = e.wr_p2n(h) > 0 ? e.wr_p2c(h, 0) : -1;
      const r1 = c1 < 0 ? -1 : e.wr_rank(c1), r2 = c2 < 0 ? -1 : e.wr_rank(c2);
      return {
        kind: 'war',
        // Your own deck is the click target, which is the physical act:
        // you turn your card and the other side turns with you.
        left: { name: 'You', n: e.wr_p1n(h), card: c1, wins: r1 > r2, i: WR_DECK },
        right: { name: 'Player 2', n: e.wr_p2n(h), card: c2, wins: r2 > r1 },
        war: c1 >= 0 && c2 >= 0 && r1 === r2,
      };
    },
    runs: (e, s) => `player ${e.wr_winner(e.wr_run(s))} after ${e.wr_rounds(e.wr_run(s))} rounds`,
    steps: 1000,
  },
  {
    id: 'poker', name: 'Poker', cat: 'Card', icon: '\u{1F0A1}',
    desc: 'Five-card draw, nine hand ranks, and a wheel that counts as a straight.',
    boot: (e, s) => e.pkt_new(s),
    step: (e, h) => e.pkt_step(h),
    done: (e, h) => e.pkt_done(h) === 1,
    human: 0,
    turn: (e, h) => e.pkt_cur(h),
    // Poker.codex: 0 is you, 1 is the opponent, -1 a split pot.
    status: (e, h) => {
      const chips = `you ${e.pkt_chips(h, 0)} · them ${e.pkt_chips(h, 1)}`;
      if (e.pkt_done(h) === 1) {
        const w = e.pkt_winner(h);
        const how = e.pkt_folded(h) === 1 ? 'they folded'
          : e.pkt_folded(h) === 0 ? 'you folded'
            : `${HANDS[e.pkt_rank(h, 0)]} against ${HANDS[e.pkt_rank(h, 1)]}`;
        return `${w === 0 ? 'the pot is yours' : w === 1 ? 'the pot is theirs' : 'split'}`
          + ` · ${how} · ${chips}`;
      }
      const owed = e.pkt_tocall(h, 0);
      const said = named(e.pkt_last(h),
        { 0: 'they folded', 1: 'they checked', 2: 'they called', 3: 'they raised', 4: 'they drew' }, '');
      return `${STAGE[e.pkt_stage(h)]} · pot ${e.pkt_pot(h)} · ${chips}`
        + (owed > 0 ? ` · ${owed} to you to stay in` : '')
        + (said ? ` · ${said}` : '')
        + ` · ${e.pkt_cur(h) === 0 ? 'your move' : 'they are thinking'}`;
    },
    // Their five are face down until the hand is over. A page that could
    // read them all along would be a page you could not lose at, and the
    // module refuses rather than the page choosing not to look.
    view: (e, h) => {
      const drawing = e.pkt_candraw(h) === 1;
      return rows([
        ['The pot', [cell(e.pkt_pot(h), 'chip big gold'),
          ...(e.pkt_bet(h, 0) ? [cell(`you in ${e.pkt_bet(h, 0)}`, 'chip')] : []),
          ...(e.pkt_bet(h, 1) ? [cell(`them in ${e.pkt_bet(h, 1)}`, 'chip')] : [])]],
        ['Your hand' + (drawing ? ' (click what you want rid of)' : ''),
          seq(5).map(i => ({
            ...cardCell(e.pkt_card(h, 0, i),
              e.pkt_marked(h, i) === 1 ? 'picked'
                : e.pkt_canmark(h, i) === 1 ? 'movable' : ''),
            i,
          }))],
        ['Them', seq(5).map(i => cardCell(e.pkt_card(h, 1, i)))],
        ['Yours reads', [cell(HANDS[e.pkt_rank(h, 0)] || '?', 'chip gold')]],
      ]);
    },
    // A click marks a card for the draw and never unmarks one: the button
    // clears them. Anywhere but the draw a click is refused, because
    // betting is what the buttons are for.
    move: (e, h, i) => {
      if (e.pkt_canmark(h, i) !== 1) return null;
      return { handle: e.pkt_mark(h, i) };
    },
    actions: [
      {
        label: 'Check or call',
        run: (e, h) => ({ handle: e.pkt_call(h) }),
        enabled: (e, h) => e.pkt_cancall(h) === 1,
      },
      {
        label: 'Raise',
        run: (e, h) => ({ handle: e.pkt_raise(h) }),
        enabled: (e, h) => e.pkt_canraise(h) === 1,
      },
      {
        label: 'Fold',
        run: (e, h) => ({ handle: e.pkt_fold(h) }),
        enabled: (e, h) => e.pkt_canfold(h) === 1,
      },
      {
        label: 'Draw',
        run: (e, h) => ({ handle: e.pkt_draw(h) }),
        enabled: (e, h) => e.pkt_candraw(h) === 1,
      },
      {
        label: 'Start the marks again',
        run: (e, h) => ({ handle: e.pkt_clear(h) }),
        enabled: (e, h) => e.pkt_candraw(h) === 1 && e.pkt_marks(h) > 0,
      },
    ],
    actionsInStage: true,
  },
  {
    id: 'pokervariants', name: 'Poker Variants', cat: 'Card', icon: '\u{1F0AA}',
    desc: 'Stud, Baseball, Hi/Low Chicago and more, each with its own wild cards.',
    // All eight variants are one table: what changes between them is how
    // many cards are dealt and how the two hands are ranked. The seed
    // picks which variant you sit down to.
    boot: (e, s) => e.pvt_new(s % 8, s),
    step: (e, h) => e.pvt_step(h),
    done: (e, h) => e.pvt_done(h) === 1,
    human: 0,
    turn: (e, h) => e.pvt_cur(h),
    status: (e, h) => {
      const v = PV_NAMES[e.pvt_variant(h)] || 'poker';
      const chips = `you ${e.pvt_chips(h, 0)} · them ${e.pvt_chips(h, 1)}`;
      if (e.pvt_done(h) === 1) {
        const w = e.pvt_winner(h);
        const how = e.pvt_folded(h) === 1 ? 'they folded'
          : e.pvt_folded(h) === 0 ? 'you folded'
            : `${HANDS[e.pvt_rank(h, 0)]} against ${HANDS[e.pvt_rank(h, 1)]}`;
        const spade = e.pvt_spade(h);
        return `${v} · ${w === 0 ? 'the pot is yours' : w === 1 ? 'the pot is theirs' : 'split'}`
          + ` · ${how}${spade ? ` · the spade in the hole went to P${spade}` : ''} · ${chips}`;
      }
      const owed = e.pvt_tocall(h, 0);
      const said = named(e.pvt_last(h),
        { 0: 'they folded', 1: 'they checked', 2: 'they called', 3: 'they raised', 4: 'they drew' }, '');
      const shut = e.pvt_cur(h) === 0 && owed === 0 && e.pvt_canopen(h) === 0;
      return `${v} · pot ${e.pvt_pot(h)} · ${chips}`
        + (owed > 0 ? ` · ${owed} to you to stay in` : '')
        + (shut ? ' · you cannot open without jacks or better' : '')
        + (said ? ` · ${said}` : '')
        + ` · ${e.pvt_cur(h) === 0 ? 'your move' : 'they are thinking'}`;
    },
    // A hand is five cards or seven, so the row is built from what the
    // table says it dealt rather than from a five nobody checked.
    view: (e, h) => {
      const n = e.pvt_size(h);
      const drawing = e.pvt_candraw(h) === 1;
      const wildOf = c => e.pvt_wildat(h, c) === 1 ? ' gold' : '';
      return rows([
        ['The pot', [cell(e.pvt_pot(h), 'chip big gold'),
          ...(e.pvt_bet(h, 0) ? [cell(`you in ${e.pvt_bet(h, 0)}`, 'chip')] : []),
          ...(e.pvt_bet(h, 1) ? [cell(`them in ${e.pvt_bet(h, 1)}`, 'chip')] : [])]],
        ['Your hand' + (drawing ? ' (click what you want rid of)' : ''),
          seq(n).map(i => ({
            ...cardCell(e.pvt_card(h, 0, i),
              (e.pvt_marked(h, i) === 1 ? 'picked'
                : e.pvt_canmark(h, i) === 1 ? 'movable' : '')
              + wildOf(e.pvt_card(h, 0, i))),
            i,
          }))],
        ['Them', seq(n).map(i => cardCell(e.pvt_shown(h, 1, i)))],
        ['Yours reads', [cell(HANDS[e.pvt_rank(h, 0)] || '?', 'chip gold')]],
      ]);
    },
    // A click marks a card for the draw and never unmarks one: the button
    // clears them. Seven-card variants have no draw, so every click there
    // is refused and the buttons are the whole of the game.
    move: (e, h, i) => {
      if (e.pvt_canmark(h, i) !== 1) return null;
      return { handle: e.pvt_mark(h, i) };
    },
    actions: [
      {
        label: 'Check or call',
        run: (e, h) => ({ handle: e.pvt_call(h) }),
        enabled: (e, h) => e.pvt_cancall(h) === 1,
      },
      {
        label: 'Raise',
        run: (e, h) => ({ handle: e.pvt_raise(h) }),
        enabled: (e, h) => e.pvt_canraise(h) === 1,
      },
      {
        label: 'Fold',
        run: (e, h) => ({ handle: e.pvt_fold(h) }),
        enabled: (e, h) => e.pvt_canfold(h) === 1,
      },
      {
        label: 'Draw',
        run: (e, h) => ({ handle: e.pvt_draw(h) }),
        enabled: (e, h) => e.pvt_candraw(h) === 1,
      },
      {
        label: 'Start the marks again',
        run: (e, h) => ({ handle: e.pvt_clear(h) }),
        enabled: (e, h) => e.pvt_candraw(h) === 1,
      },
    ],
    actionsInStage: true,
  },
  {
    id: 'pinochle', name: 'Pinochle', cat: 'Card', icon: '\u{1F0DB}',
    desc: 'Forty-eight cards, two of every one of them, which is exactly the trap in scoring the melds.',
    boot: (e, s) => e.pn_new(s),
    step: (e, h) => e.pn_step(h),
    done: (e, h) => e.pn_done(h) === 1,
    // You sit at seat 0 and you lead the first trick, so the game opens on
    // your move without anything having to be played for you first.
    human: 0,
    turn: (e, h) => e.pn_cur(h),
    // Pinochle.codex: 0 is Team0, 1 is Team1, anything else a tie.
    runs: (e, s) => named(e.pn_winner(e.pn_run(s)),
      { 0: 'team zero takes it', 1: 'team one takes it' }, 'tied'),
    status: (e, h) => {
      const you = e.pn_pts(h, 0), them = e.pn_pts(h, 1);
      if (e.pn_done(h) === 1) {
        // The melds are gone with the cards by now, so the finished line
        // reports the tricks alone and says so.
        return `trick points ${you} to ${them} · `
          + (you > them ? 'you and your partner take them'
            : you < them ? 'the other pair take them' : 'level');
      }
      return `trump ${PIN_SUIT[e.pn_trump(h)]} · your meld ${e.pn_meld(h, 0)}`
        + ` · trick points ${you} to ${them}`
        + ` · ${e.pn_tricks(h)} of 12 played`
        + ` · ${e.pn_cur(h) === 0 ? 'your lead or your card'
          : `${PLAYERS[e.pn_cur(h)]} to play`}`;
    },
    // Your partner sits opposite at seat 2. Their cards are theirs, so the
    // count is all that shows, the same way the opponents' do.
    view: (e, h) => rows([
      ['On the table', seq(4).map(p =>
        pinCell(e.pn_trick(h, p), e.pn_leader(h) === p ? 'picked' : ''))],
      ['Your hand' + (e.pn_cur(h) === 0 ? ' (to play)' : ''),
        seq(e.pn_count(h, 0)).map(i => ({
          ...pinCell(e.pn_card(h, 0, i), e.pn_legal(h, i) === 1 ? 'movable' : ''),
          i,
        }))],
      ...[1, 2, 3].map(p => [
        `${p === 2 ? 'Partner' : PLAYERS[p]}${p === e.pn_cur(h) ? ' to play' : ''}`,
        [cell(`${e.pn_count(h, p)} cards`, 'chip')],
      ]),
    ]),
    // A click is an index into YOUR hand and not a card id, because a
    // pinochle deck holds two of every card and an id names both of them.
    move: (e, h, i) => {
      if (i < 0 || i >= e.pn_count(h, 0)) return null;
      if (e.pn_legal(h, i) !== 1) return null;
      return { handle: e.pn_play(h, i) };
    },
  },
  {
    id: 'bridge', name: 'Bridge', cat: 'Card', icon: '♠',
    desc: 'Four hands, high-card-point bidding, and a contract scored at the end.',
    boot: (e, s) => e.br_new(s),
    step: (e, h) => e.br_step(h),
    done: (e, h) => e.br_done(h) === 1,
    // You are South. The opening lead belongs to the declarer's left, which
    // is South only when East-West bought the contract, so `br_new` plays
    // the seats ahead of you before it answers and the game still opens on
    // your move.
    human: 1,
    turn: (e, h) => e.br_cur(h),
    status: (e, h) => {
      const decl = e.br_declarer(h) === 0 ? 'North-South' : 'East-West';
      const need = e.br_contract(h) + 6;
      const got = e.br_made(h);
      const head = `${e.br_contract(h)}${SUIT[e.br_trump(h)] || 'NT'} by ${decl}`
        + ` · needs ${need}`;
      if (e.br_done(h) === 1) {
        return `${head} · made ${got} · ${got >= need ? 'contract home' : `down ${need - got}`}`
          + ` · ${e.br_score(h)}`;
      }
      return `${head} · NS ${e.br_nstricks(h)} EW ${e.br_ewtricks(h)}`
        + ` · ${e.br_tricks(h)} of 13 played`
        + ` · ${e.br_cur(h) === 1 ? 'yours to play' : `${BR_SEAT[e.br_cur(h)]} to play`}`;
    },
    // Every hand but yours is a count. North is your partner and the engine
    // plays it: there is no dummy here, which is the one thing a bridge
    // player will notice missing.
    view: (e, h) => rows([
      ['On the table', seq(4).map(p =>
        cardCell(e.br_trick(h, p), e.br_leader(h) === p ? 'picked' : ''))],
      ['South, you' + (e.br_cur(h) === 1 ? ' (to play)' : '')
        + ` (${e.br_hcp(h, 1)} hcp)`,
        seq(e.br_count(h, 1)).map(i => ({
          ...cardCell(e.br_card(h, 1, i), e.br_legal(h, i) === 1 ? 'movable' : ''),
          i,
        }))],
      ...[0, 2, 3].map(p => [
        `${BR_SEAT[p]}${p === 0 ? ', your partner' : ''}`
        + `${p === e.br_cur(h) ? ' to play' : ''}`,
        [cell(`${e.br_count(h, p)} cards`, 'chip')],
      ]),
    ]),
    move: (e, h, i) => {
      if (i < 0 || i >= e.br_count(h, 1)) return null;
      if (e.br_legal(h, i) !== 1) return null;
      return { handle: e.br_play(h, i) };
    },
  },
  {
    id: 'crazyeights', name: 'Crazy Eights', cat: 'Card', icon: '\u{1F0A8}',
    desc: 'Match the suit or the rank. Eights are wild and YOU name the suit, which is the whole reason to hold one back.',
    boot: (e, s) => { ceEight = -1; return e.ce_new(s, 3); },
    step: (e, h) => e.ce_step(h),
    done: (e, h) => e.ce_done(h) === 1,
    human: 0,
    turn: (e, h) => e.ce_cur(h),
    status: (e, h) => {
      if (e.ce_done(h) === 1) {
        const w = e.ce_winner(h);
        return `${w === 0 ? 'You go out' : `${named(w, PLAYERS, 'nobody')} goes out`}`
          + ` · you were left with ${e.ce_size(h, 0)}`;
      }
      const pen = e.ce_penalty(h);
      return `pile ${card(e.ce_pile(h))}`
        + (e.ce_declared(h) >= 0 ? ` (called ${SUIT[e.ce_declared(h)]})` : '')
        + ` · ${e.ce_cur(h) === 0 ? 'your turn' : `${named(e.ce_cur(h), PLAYERS, '?')} to play`}`
        + (pen > 0 ? ` · ${pen} to draw before you can play` : '')
        + (ceEight >= 0 ? ` · name a suit for the ${card(ceEight)}` : '');
    },
    // ce_has answers whether a player holds a given card, so the hand can
    // be read out of the module a card at a time rather than reduced to a
    // number. A count is not a hand.
    view: (e, h) => {
      const yours = seq(52).filter(c => e.ce_has(h, 0, c) === 1);
      return rows([
        ['Pile', [cardCell(e.ce_pile(h))]],
        // Naming a suit is a second question, asked only while an eight is
        // waiting to be laid, so the row is not there the rest of the time.
        ceEight >= 0 && ['Name a suit', seq(4).map(s =>
          cell(SUIT[s], 'chip big' + (s === 1 || s === 2 ? ' red' : ''), CE_SUIT + s))],
        ['Your hand' + (e.ce_cur(h) === 0 ? ' (to play)' : ''),
          yours.map(c => cardCell(c,
            (e.ce_canplay(h, c) === 1 ? 'movable' : '') + (ceEight === c ? ' picked' : '')))
            .map((cc, k) => ({ ...cc, i: CE_CARD + yours[k] }))],
        ...seq(e.ce_players(h)).filter(p => p !== 0).map(p => [
          `${PLAYERS[p]}${p === e.ce_cur(h) ? ' to play' : ''}`,
          // Another player's hand is theirs. The count is public.
          [cell(`${e.ce_size(h, p)} cards`, 'chip')],
        ]),
      ]);
    },
    // An eight is two clicks: the card, then the suit it calls. Everything
    // else is one.
    move: (e, h, i) => {
      if (i >= CE_SUIT && i < CE_SUIT + 4) {
        if (ceEight < 0) return null;
        const c = ceEight;
        ceEight = -1;
        return { handle: e.ce_play(h, c, i - CE_SUIT) };
      }
      if (i < CE_CARD || i >= CE_CARD + 52) return null;
      const c = i - CE_CARD;
      if (e.ce_canplay(h, c) !== 1) return null;
      // Rank 6 is the eight, in the engine's own `ce-rank` (card mod 13,
      // where 0 is the two). Hold it back until a suit is named.
      if (e.ce_rank(c) === 6) { ceEight = c; return { sel: null }; }
      return { handle: e.ce_play(h, c, -1) };
    },
    actions: [
      {
        label: 'Draw a card',
        run: (e, h) => ({ handle: e.ce_draw(h) }),
        // Only when nothing in your hand will go. Offered otherwise it is a
        // way of skipping a turn you were able to take.
        enabled: (e, h) => e.ce_cur(h) === 0 && e.ce_stuck(h) === 1,
      },
      {
        label: 'Take the penalty',
        run: (e, h) => ({ handle: e.ce_takepen(h) }),
        enabled: (e, h) => e.ce_cur(h) === 0 && e.ce_canpen(h) === 1,
      },
    ],
    actionsInStage: true,
  },
  {
    id: 'gofish', name: 'Go Fish', cat: 'Card', icon: '\u{1F41F}',
    desc: 'Ask for a rank you hold; complete four of a kind to book it.',
    boot: (e, s) => { gfSaid = null; return e.gf_new(s, 3); },
    step: (e, h) => e.gf_step(h),
    done: (e, h) => e.gf_done(h) === 1,
    // You are player 0. The engine passes the turn after every question,
    // hit or miss, so a turn is exactly one ask.
    human: 0,
    turn: (e, h) => e.gf_cur(h),
    status: (e, h) => {
      if (e.gf_done(h) === 1) {
        const books = seq(e.gf_players(h)).map(p => e.gf_books(h, p));
        const best = Math.max(...books);
        const who = books.indexOf(best);
        return `${e.gf_total(h)} books made · ${PLAYERS[who]} takes it with ${best}`;
      }
      return `${PLAYERS[e.gf_cur(h)]} to ask · ${e.gf_pile(h)} left in the pond`
        + ` · ${e.gf_total(h)} books made`
        + (gfSaid ? ` · ${gfSaid}` : '');
    },
    view: (e, h, s, sel) => {
      const me = 0;
      // The ranks you actually hold, which are the only ones you may ask
      // for. Asking for a rank you do not hold is the rule this game is
      // most often played wrong, so the board simply does not offer it.
      const mine = seq(13).filter(r => e.gf_rcount(h, me, r) > 0);
      const asking = sel === null || sel === undefined ? null : sel;
      return rows([
        ['Ask for', mine.map(r => cell(`${RANK[r === 12 ? 12 : r]} x${e.gf_rcount(h, me, r)}`,
          'chip big' + (asking === r ? ' picked' : ''), GF_RANK + r))],
        asking === null ? null
          : ['Ask whom', seq(e.gf_players(h)).filter(p => p !== me).map(p =>
            cell(PLAYERS[p], 'chip big', GF_WHO + p))],
        ...seq(e.gf_players(h)).map(p => [
          `${PLAYERS[p]}${p === me ? ' (you)' : ''} · ${e.gf_books(h, p)} books`,
          p === me ? held(52, c => e.gf_has(h, p, c) === 1)
            // Another player's hand is not yours to see. The count is.
            : [cell(`${e.gf_size(h, p)} cards`, 'chip')],
        ]),
      ]);
    },
    // Two clicks: the rank, then who to ask. A rank you do not hold is not
    // offered, so the only refusal left is asking yourself.
    move: (e, h, i, st) => {
      if (i >= GF_RANK && i < GF_RANK + 13) {
        return e.gf_canask(h, i - GF_RANK) === 1 ? { sel: i - GF_RANK } : null;
      }
      if (i >= GF_WHO && i < GF_WHO + 8) {
        const rank = st.sel;
        if (rank === null || rank === undefined) return null;
        const who = i - GF_WHO;
        if (who === 0) return null;
        const before = e.gf_rcount(h, 0, rank);
        const next = e.gf_ask(h, rank, who);
        const after = e.gf_rcount(next, 0, rank);
        gfSaid = after > before
          ? `${PLAYERS[who]} handed over ${after - before} ${RANK[rank]}`
          : `${PLAYERS[who]} said go fish`;
        return { handle: next };
      }
      return null;
    },
  },
  {
    id: 'klondike', name: 'Klondike', cat: 'Card', icon: '\u{1F0A1}',
    desc: 'The solitaire everybody means by solitaire. Seven columns, most of it face down, four foundations to build from the ace up. Turn one card and it usually goes out; turn three and it usually does not.',
    // Turning one card or three is the whole difficulty setting, and it is
    // baked into the deal rather than applied on top of it, because how many
    // you turn decides which of the stock you can ever reach.
    options: [{
      name: 'draw', label: 'Turn',
      values: [1, 3], labels: ['one', 'three'], def: 1,
    }],
    boot: (e, s, o) => e.kd_new(s, (o && o.draw) || 1),
    // The engine answers a move code, or -2 for turn the stock, -3 for
    // gather the waste back up, or -1 for nothing left worth doing.
    step: (e, h) => {
      const m = e.kd_ai(h);
      if (m === -2) { kdDealt = true; return e.kd_draw(h); }
      if (m === -3) return e.kd_recycle(h);
      if (m < 0) return null;
      return e.kd_move(h, e.kd_mfrom(m), e.kd_mstart(m), e.kd_mto(m));
    },
    done: (e, h) => e.kd_won(h) === 1,
    won: (e, h) => e.kd_won(h) === 1,
    status: (e, h) => {
      if (e.kd_won(h) === 1) return `All fifty-two up in ${e.kd_moves(h)} moves`;
      const hidden = seq(7).reduce((a, c) => a + e.kd_down(h, c), 0);
      return `${e.kd_founded(h)} of 52 up · ${e.kd_moves(h)} moves`
        + ` · ${e.kd_stockn(h)} in the stock`
        + (hidden ? ` · ${hidden} still face down` : ' · nothing left face down');
    },
    view: (e, h, s, sel, roll, seed, hint) => {
      const hm = hint === undefined ? null : hint;
      const sl = sel && typeof sel === 'object' ? sel : null;
      const fresh = kdDealt;
      kdDealt = false;
      // Where what you are carrying could land, asked of the engine for
      // every destination rather than guessed, so the board shows you the
      // legal moves for the card in your hand.
      const takes = d => sl !== null && e.kd_can(h, sl.from, sl.start, d) === 1;
      return {
        kind: 'klondike',
        stock: e.kd_stockn(h),
        waste: e.kd_wasten(h),
        wasteTop: e.kd_wastetop(h),
        canRecycle: e.kd_canrecyc(h) === 1,
        wastePicked: !!(sl && sl.from === 7),
        founds: seq(4).map(f => ({
          card: e.kd_foundcard(h, f),
          takes: takes(7 + f),
          picked: !!(sl && sl.from === 8 + f),
        })),
        won: e.kd_won(h) === 1,
        cols: seq(7).map(c => {
          const n = e.kd_coln(h, c);
          const down = e.kd_down(h, c);
          if (!n) {
            return {
              i: c, cards: [],
              takes: takes(c),
              hint: !!(hm && hm.to === c),
            };
          }
          const runFrom = kdRunStart(e, h, c, n);
          return {
            i: c,
            takes: takes(c),
            hint: false,
            cards: seq(n).map(i => {
              const v = e.kd_card(h, c, i);
              return {
                card: v,
                i: kdCardCode(c, i),
                cls: (v < 0 ? 'facedown' : '')
                  + (sl && sl.from === c && i >= sl.start ? ' picked' : '')
                  + (v >= 0 && i >= runFrom ? ' movable' : '')
                  + (takes(c) && i === n - 1 ? ' takes' : '')
                  + (hm && hm.from === c && i >= hm.start ? ' hintfrom' : '')
                  + (hm && hm.to === c && i === n - 1 ? ' hintto' : ''),
              };
            }),
          };
        }),
        fresh,
      };
    },
    solo: true,
    // A Klondike move is a source, a start index inside it, and a
    // destination, and the source may be a column, the waste or a
    // foundation. Clicking the stock is not a selection at all: it turns a
    // card, which is the one click in this game that is always available and
    // always means the same thing.
    move: (e, h, code, st) => {
      const sl = st.sel && typeof st.sel === 'object' ? st.sel : null;
      if (code === KD_STOCK) {
        if (e.kd_candraw(h) === 1) { kdDealt = true; return { handle: e.kd_draw(h) }; }
        if (e.kd_canrecyc(h) === 1) return { handle: e.kd_recycle(h) };
        return null;
      }
      // What this click names as a place to put cards, and as a place to
      // take them from.
      const to = code >= KD_CARD ? Math.floor((code - KD_CARD) / 1000)
        : code >= KD_FOUND ? 7 + (code - KD_FOUND)
          : code === KD_WASTE ? -1 : code;
      const lift = () => {
        if (code === KD_WASTE) {
          return e.kd_wasten(h) > 0 ? { sel: { from: 7, start: 0, exact: true } } : null;
        }
        if (code >= KD_FOUND && code < KD_FOUND + 4) {
          const f = code - KD_FOUND;
          return e.kd_found(h, f) >= 0 ? { sel: { from: 8 + f, start: 0, exact: true } } : null;
        }
        const c = code >= KD_CARD ? Math.floor((code - KD_CARD) / 1000) : code;
        const idx = code >= KD_CARD ? (code - KD_CARD) % 1000 : -1;
        const n = e.kd_coln(h, c);
        if (!n) return null;
        const from = kdRunStart(e, h, c, n);
        const start = idx < 0 ? from : idx;
        if (start < from || start >= n) return null;
        if (e.kd_card(h, c, start) < 0) return null;
        return { sel: { from: c, start, exact: idx >= 0 } };
      };
      if (!sl) return lift();
      // Clicking inside the column you are already holding shortens what you
      // carry, the same way Spider splits a run.
      if (sl.from < 7 && to === sl.from) {
        const idx = code >= KD_CARD ? (code - KD_CARD) % 1000 : -1;
        const n = e.kd_coln(h, sl.from);
        const from = kdRunStart(e, h, sl.from, n);
        if (idx >= from && idx < n && idx !== sl.start) {
          return { sel: { from: sl.from, start: idx, exact: true } };
        }
        return { sel: null };
      }
      if (to >= 0 && e.kd_can(h, sl.from, sl.start, to) === 1) {
        return { handle: e.kd_move(h, sl.from, sl.start, to) };
      }
      // A destination that will not take it becomes the new source where it
      // can be lifted, so a misjudged move costs one click rather than two.
      return lift() || { sel: null };
    },
    hint: (e, h) => {
      const m = e.kd_ai(h);
      if (m < 0) return null;
      return { from: e.kd_mfrom(m), start: e.kd_mstart(m), to: e.kd_mto(m) };
    },
    actions: [
      {
        label: 'Turn the stock',
        run: (e, h) => { kdDealt = true; return { handle: e.kd_draw(h) }; },
        enabled: (e, h) => e.kd_candraw(h) === 1,
      },
      {
        label: 'Gather the waste',
        run: (e, h) => ({ handle: e.kd_recycle(h) }),
        enabled: (e, h) => e.kd_canrecyc(h) === 1,
      },
    ],
    runs: (e, s) => {
      const r = e.kd_run(s, 1);
      return `${e.kd_rfound(r)} of 52 up in ${e.kd_rmoves(r)} moves`;
    },
    steps: 600,
  },
  {
    id: 'spider', name: 'Spider Solitaire', cat: 'Card', icon: '\u{1F578}',
    desc: 'A hundred and four cards in ten columns. Build a king down to an ace in one suit and it flies away. One suit is a puzzle, four is a fight.',
    // One, two and four suits are the real game's whole difficulty range.
    // The count is baked into the deck the engine deals, not applied on top
    // of it, so every rule below -- runs, moves, completions -- follows.
    options: [{
      name: 'suits', label: 'Suits',
      values: [1, 2, 3, 4], labels: ['one', 'two', 'three', 'four'], def: 2,
    }],
    boot: (e, s, o) => e.sp_new(s, (o && o.suits) || 2),
    step: (e, h) => {
      const m = e.sp_sugg(h);
      if (m < 0) {
        if (e.sp_stockn(h) <= 0) return null;
        spDealt = true;
        return e.sp_deal(h);
      }
      return e.sp_move(h, e.sp_mfrom(m), e.sp_mstart(m), e.sp_mto(m));
    },
    done: (e, h) => e.sp_suits(h) === 8,
    won: (e, h) => e.sp_suits(h) === 8,
    status: (e, h) => {
      if (e.sp_suits(h) === 8) return `All eight runs away in ${e.sp_moves(h)} moves`;
      const empty = seq(10).filter(c => e.sp_coln(h, c) === 0).length;
      return `${e.sp_suits(h)} of 8 runs away · ${e.sp_moves(h)} moves`
        + ` · ${e.sp_stockn(h)} left in the stock`
        + (empty && e.sp_stockn(h) > 0
          ? ` · fill the empty column${empty > 1 ? 's' : ''} before you can deal` : '');
    },
    view: (e, h, s, sel, roll, seed, hint) => {
      // A hint SHOWS the move rather than making it. Being told the answer
      // and being played for are different things, and only one of them is
      // still your game.
      const hm = hint === undefined ? null : hint;
      const sl = sel && typeof sel === 'object' ? sel : null;
      const fresh = spDealt;
      spDealt = false;
      return {
        kind: 'columns',
        stock: e.sp_stockn(h),
        suits: e.sp_suits(h),
        won: e.sp_suits(h) === 8,
        cols: seq(10).map(c => {
          const n = e.sp_coln(h, c);
          if (!n) {
            return [cell('', 'card empty'
              + (hm && hm.to === c ? ' hintto' : '')
              + (sl && sl.col !== c ? ' drop' : ''), c)];
          }
          // The cards a move could pick up are the tail of the column that
          // forms a descending same-suit run.
          const runFrom = spRunStart(e, h, c, n);
          // Where what you are carrying would actually land, asked of the
          // engine rather than guessed, so a legal destination shows itself
          // before you commit to the click.
          const takes = sl && sl.col !== c && spFits(e, h, sl, c);
          return seq(n).map(i => {
            const v = e.sp_card(h, c, i);
            return cell(v < 0 ? '' : card(v),
              'card' + (red(v) ? ' red' : '') + (v < 0 ? ' facedown' : '')
              + (sl && sl.col === c && i >= sl.start ? ' picked' : '')
              + (i >= runFrom ? ' movable' : '')
              + (takes && i === n - 1 ? ' takes' : '')
              + (fresh && i === n - 1 ? ' dealt' : '')
              + (hm && hm.from === c && i >= hm.start ? ' hintfrom' : '')
              + (hm && hm.to === c && i === n - 1 ? ' hintto' : ''),
              spCardCode(c, i),
              // The dealt card flies in from the stock, which sits at the
              // left of the tray, so the further right the column the
              // further it travels. Staggered so the row deals across
              // rather than arriving in one block.
              fresh && i === n - 1
                ? `--dx:${-(c * 62 + 30)}px;animation-delay:${c * 32}ms` : null);
          });
        }),
      };
    },
    solo: true,
    // A spider move is a source column, a start index inside it, and a
    // destination column, so a selection has to carry the start as well as
    // the column: `{ col, start }`. Naming only the column is what stopped
    // a run being split, because there is then nowhere to say WHICH card of
    // it you meant to pick up from.
    //
    // Splitting is legal Spider and it is not always useless: the whole
    // point of lifting three of a five-run is to leave a card exposed that
    // something else needs, or to fit what a destination will actually
    // take.
    move: (e, h, code, st) => {
      const c = spCol(code), idx = spIdx(code);
      const sl = st.sel && typeof st.sel === 'object' ? st.sel : null;
      // Pick a column up from the card that was clicked, or -- when the
      // click landed on the column rather than on a card -- from the top of
      // its longest run, which is the common case and what one click used
      // to do.
      // `exact` records whether you named a CARD or a column. Naming a card
      // means that card and no other, which is what splitting a run is.
      // Naming the column means the largest tail that the destination will
      // actually take, which is what one click has always done here and is
      // almost always what you want: demanding the whole run instead stalls
      // the game the moment a five-run meets a destination with room for
      // three.
      const lift = () => {
        const n = e.sp_coln(h, c);
        if (!n) return null;
        const from = spRunStart(e, h, c, n);
        const start = idx < 0 ? from : idx;
        return start >= from && start < n
          ? { sel: { col: c, start, exact: idx >= 0 } } : null;
      };
      if (!sl) return lift();
      if (c === sl.col) {
        // A second click inside the column you are holding SHORTENS what
        // you are carrying instead of dropping it: click the card you want
        // to move from. Clicking the card you already hold, or anything
        // that cannot be lifted, puts the stack back down.
        const n = e.sp_coln(h, c);
        const from = spRunStart(e, h, c, n);
        if (idx >= from && idx < n && idx !== sl.start) {
          return { sel: { col: c, start: idx, exact: true } };
        }
        return { sel: null };
      }
      if (sl.exact) {
        if (e.sp_can(h, sl.col, sl.start, c) === 1) {
          return { handle: e.sp_move(h, sl.col, sl.start, c) };
        }
      } else {
        const fn = e.sp_coln(h, sl.col);
        for (let s = sl.start; s < fn; s++) {
          if (e.sp_can(h, sl.col, s, c) === 1) return { handle: e.sp_move(h, sl.col, s, c) };
        }
      }
      // A destination that refuses becomes the new source where it can be
      // lifted, so a misjudged move costs one click rather than two.
      return lift() || { sel: null };
    },
    // The engine's own suggester, decoded into the three numbers a move is
    // made of, so the page can point at it.
    hint: (e, h) => {
      const m = e.sp_sugg(h);
      if (m < 0) return null;
      return { from: e.sp_mfrom(m), start: e.sp_mstart(m), to: e.sp_mto(m) };
    },
    actions: [
      {
        label: 'Deal a row', run: (e, h) => { spDealt = true; return { handle: e.sp_deal(h) }; },
        // Spider deals onto EVERY column, so it may not deal while one
        // stands empty. Showing the button as available and then quietly
        // doing nothing is worse than showing it disabled.
        enabled: (e, h) => e.sp_stockn(h) > 0 &&
          !seq(10).some(c => e.sp_coln(h, c) === 0),
      },
    ],
    runs: (e, s) => `${e.sp_rsuits(e.sp_run(s))} of 8 runs in ${e.sp_rmoves(e.sp_run(s))} moves`,
    steps: 400,
  },
  {
    id: 'liarsdice', name: "Liar's Dice", cat: 'Dice', icon: '\u{1F3B2}',
    desc: 'Bid on dice you cannot see. Every bid has to be bigger than the last, so the count climbs until somebody stops believing it.',
    boot: (e, s) => { ldFace = 1; ldSaid = null; return e.ld_new(s, 4); },
    step: (e, h) => e.ld_step(h),
    done: (e, h) => e.ld_done(h) === 1,
    human: 0,
    turn: (e, h) => e.ld_turn(h),
    won: (e, h) => e.ld_winner(h) === 0,
    status: (e, h) => {
      if (e.ld_done(h) === 1) {
        const w = e.ld_winner(h);
        return w === 0 ? 'You are the last one holding dice' : `${named(w, PLAYERS, 'nobody')} takes it`;
      }
      return (e.ld_bid(h) > 0 ? `the bid is ${e.ld_qty(h)} x ${e.ld_face(h)}` : 'no bid yet')
        + ` · ${e.ld_total(h)} dice on the table`
        + ` · ${e.ld_turn(h) === 0 ? 'your turn' : `${named(e.ld_turn(h), PLAYERS, '?')} to bid`}`
        + (ldSaid ? ` · ${ldSaid}` : '');
    },
    // YOUR DICE ONLY. The whole game is bidding on a count you cannot see,
    // so laying every player's dice face up, which is what this view did,
    // was showing the one thing nobody at the table knows.
    view: (e, h) => {
      const total = e.ld_total(h);
      const legal = q => e.ld_canbid(h, q, ldFace) === 1;
      return rows([
        ['Your dice', seq(e.ld_dice(h, 0)).map(i =>
          cell(DIE_FACE[e.ld_die(h, 0, i)] || e.ld_die(h, 0, i), 'die big'))],
        ['Face', seq(6).map(f => cell(DIE_FACE[f + 1], 'die big'
          + (ldFace === f + 1 ? ' picked' : ''), LD_FACE + f + 1))],
        // Only the quantities that would be a legal raise on the chosen
        // face are offered, so the board cannot be asked for a bid the
        // rules refuse.
        ['Say', seq(total).map(k => k + 1).filter(legal).map(q =>
          cell(q, 'chip big', LD_QTY + q))],
        ...seq(e.ld_players(h)).filter(p => p !== 0).map(p => [
          `${PLAYERS[p]}${e.ld_alive(h, p) === 1 ? '' : ' (out)'}`,
          [cell(`${e.ld_dice(h, p)} dice`, 'chip')],
        ]),
      ]);
    },
    // Pick a face, then say a number: that IS the bid, so the quantity
    // click commits it. Calling is an action, because it answers the last
    // bid rather than making one.
    move: (e, h, i) => {
      if (i >= LD_FACE && i < LD_FACE + 7) { ldFace = i - LD_FACE; return { sel: null }; }
      if (i >= LD_QTY && i < LD_QTY + 40) {
        const q = i - LD_QTY;
        if (e.ld_canbid(h, q, ldFace) !== 1) return null;
        ldSaid = null;
        return { handle: e.ld_bidat(h, q, ldFace) };
      }
      return null;
    },
    actions: [
      {
        label: 'Call it a lie',
        // The real count has to be read BEFORE the call resolves: settling
        // rerolls every die on the table, so afterwards the number that
        // decided it no longer exists anywhere.
        run: (e, h) => {
          const qty = e.ld_qty(h), face = e.ld_face(h), actual = e.ld_actual(h);
          const next = e.ld_call(h);
          ldSaid = actual >= qty
            ? `there really were ${actual} ${face}s, so you lose a die`
            : `only ${actual} ${face}s, the bid was a lie`;
          return { handle: next };
        },
        enabled: (e, h) => e.ld_turn(h) === 0 && e.ld_cancall(h) === 1,
      },
    ],
    actionsInStage: true,
    steps: 400,
  },

  {
    id: 'battleship', name: 'Battleship', cat: 'Strategy', icon: '\u{1F6A2}',
    desc: 'Seventeen cells of ship hidden in a hundred. Fire a square, read what comes back, and hunt the rest of the hull from the hit.',
    boot: (e, s) => e.bs_new(s),
    step: (e, h) => e.bs_step(h),
    done: (e, h) => e.bs_done(h) === 1,
    won: (e, h) => e.bs_winner(h) === 1,
    // `bs-wasm-hits`, `bs-wasm-shots` and `bs-wasm-track` all test
    // `player == 1` and fall through to player TWO, so they are ONE-based.
    // This page passed 0 and 1, so every counter and both grids were
    // attributed to the wrong fleet, and only the winner line disagreed --
    // measured 2026-09-02, five of five finished games said "player 1 wins"
    // beside a column labelled P2 that was the one holding seventeen hits.
    // Both fleets are driven by the same AI, so nothing else could show it.
    status: (e, h) => {
      const you = `you ${e.bs_hits(h, BS_YOU)} hits in ${e.bs_shots(h, BS_YOU)} shots`;
      const them = `them ${e.bs_hits(h, BS_THEM)} in ${e.bs_shots(h, BS_THEM)}`;
      if (e.bs_done(h) === 1) {
        const w = e.bs_winner(h);
        return `${you} · ${them} · `
          + (w === 1 ? 'your fleet takes it' : w === 2 ? 'their fleet takes it' : 'neither fleet');
      }
      return `${you} · ${them} · 17 cells of ship to sink`;
    },
    view: (e, h) => ({
      kind: 'pair', cols: 10, labels: ['You fire at', 'They fire at'],
      grids: [BS_YOU, BS_THEM].map(p => seq(100).map(i => {
        const r = Math.floor(i / 10), c = i % 10;
        const t = e.bs_track(h, p, r, c);
        // Only your own grid takes clicks; theirs is a record of what has
        // landed on you.
        return cell(['', '·', '●'][t] || '', 'sea t' + t,
          p === BS_YOU && t === 0 ? i : undefined);
      })),
    }),
    solo: true,
    // One click is one salvo: you fire, then they answer, which is what the
    // engine's own turn does for both sides at once.
    move: (e, h, i) => {
      if (i < 0 || i >= 100) return null;
      const r = Math.floor(i / 10), c = i % 10;
      if (e.bs_canfire(h, r, c) !== 1) return null;
      return { handle: e.bs_fire(h, r, c) };
    },
    steps: 400,
  },
  {
    id: 'risk', name: 'Risk', cat: 'Strategy', icon: '\u{1F30D}',
    desc: 'Twelve territories in four continents. The turn cap used to decide games nobody could see being decided.',
    boot: (e, s) => e.rk_new(s, 4),
    step: (e, h, r) => e.rk_turn(h, r()),
    // Risk is the only game here you can be knocked OUT of while it carries
    // on. The engine is not over until somebody owns all twelve, but your
    // game is over the moment you own none, and a page that kept stepping
    // would be showing a visitor a game they are no longer in.
    done: (e, h) => e.rk_done(h) === 1 || e.rk_alive(h, 0) === 0,
    // You are player one. A turn is two phases: put the reinforcement down
    // a territory at a time, then take up to three attacks, or stop.
    human: 0,
    turn: (e, h) => e.rk_cur(h),
    status: (e, h) => {
      const armies = seq(e.rk_np(h)).map(p =>
        `${PLAYERS[p]} ${e.rk_total(h, p)}`).join(' ');
      if (e.rk_done(h) === 1) {
        return `turn ${e.rk_turnno(h)} · ${armies}`
          + ` · ${named(e.rk_winner(h), PLAYERS, 'nobody')} takes the world`;
      }
      if (e.rk_alive(h, 0) === 0) {
        return `turn ${e.rk_turnno(h)} · ${armies}`
          + ' · you are off the board, and the rest fight on without you';
      }
      const yours = e.rk_cur(h) === 0;
      const what = e.rk_phase(h) === 0
        ? `${e.rk_toplace(h)} to place`
        : `${e.rk_atkleft(h)} attack${e.rk_atkleft(h) === 1 ? '' : 's'} left`;
      return `turn ${e.rk_turnno(h)} · ${armies} · ${what}`
        + ` · ${yours ? (e.rk_phase(h) === 0
          ? 'click one of yours to reinforce it'
          : 'click a territory of yours, then one to attack')
          : `${named(e.rk_cur(h), PLAYERS, '?')} to move`}`;
    },
    // Ringing what is legal is the whole of the interface here: which of
    // your territories can take an army, and which can attack from.
    view: (e, h) => grid(4, seq(12).map(i => {
      const mark = e.rk_canplace(h, i) === 1 ? ' movable'
        : e.rk_canatkfrom(h, i) === 1 ? ' movable' : '';
      return { ...cell(`${e.rk_armies(h, i)}`, 'terr o' + e.rk_owner(h, i) + mark), i };
    })),
    // Placing is one click. Attacking is two, and the second is offered
    // only where the rules allow it, so a held selection cannot be
    // spent on a territory that is not adjacent or is already yours.
    move: (e, h, i, ctx) => {
      if (i < 0 || i >= 12) return null;
      if (e.rk_phase(h) === 0) {
        if (e.rk_canplace(h, i) !== 1) return null;
        return { handle: e.rk_place(h, i) };
      }
      const held = ctx && ctx.sel;
      if (held === null || held === undefined) {
        if (e.rk_canatkfrom(h, i) !== 1) return null;
        return { sel: i };
      }
      if (e.rk_canattack(h, held, i) !== 1) return null;
      return { handle: e.rk_attack(h, held, i, ctx.rand()) };
    },
    actions: [
      {
        label: 'Stop attacking',
        run: (e, h) => ({ handle: e.rk_stop(h) }),
        enabled: (e, h) => e.rk_canstop(h) === 1,
      },
    ],
    actionsInStage: true,
    steps: 400,
  },
  {
    id: 'monopoly', name: 'Monopoly', cat: 'Strategy', icon: '\u{1F3E0}',
    desc: 'Forty spaces, simplified: property changes hands, no houses.',
    boot: (e, s) => e.mo_new(s, 4),
    step: (e, h, r) => e.mo_step(h, r()),
    done: (e, h) => e.mo_done(h) === 1,
    // You are player one. There is exactly one decision in this engine
    // (games-backlog GAME-8: no trading), so a turn is a roll and then, on
    // an unowned square you can afford, buy or pass.
    human: 0,
    turn: (e, h) => e.mo_cur(h),
    // The engine ends on bankruptcy, which is rare without trading, so most
    // sessions stop at the page's step bound rather than at mo_cap. Saying
    // "of 601" invented an ending.
    status: (e, h) => {
      const money = seq(e.mo_players(h)).map(p =>
        `${PLAYERS[p]} $${e.mo_cash(h, p)}`).join(' ');
      if (e.mo_done(h) === 1) {
        return `turn ${e.mo_turn(h)} · ${money}`
          + ` · ${named(e.mo_winner(h), PLAYERS, 'nobody')} bankrupts the rest`;
      }
      const offer = e.mo_offered(h);
      return `turn ${e.mo_turn(h)} · ${money}`
        + (e.mo_lastroll(h) ? ` · rolled ${e.mo_lastroll(h)}` : '')
        + (offer >= 0
          ? ` · ${e.mo_offercost(h)} to buy it, rent ${e.mo_offerrent(h)}`
          : ` · ${named(e.mo_richest(h), PLAYERS, '?')} richest`)
        + ` · ${e.mo_cur(h) === 0 ? (offer >= 0 ? 'buy it or pass' : 'your roll')
          : `${PLAYERS[e.mo_cur(h)]} to move`}`;
    },
    // A board SPACE is not a property index and neither is a player. This
    // read all three off the same number and so never coloured an owned
    // square: `mo_propat` is what turns a space into a deed.
    view: (e, h) => grid(10, seq(40).map(i => {
      const pi = e.mo_propat(i);
      const owner = pi >= 0 && e.mo_owner(h, pi) >= 0 ? e.mo_owner(h, pi) + 1 : 0;
      const here = seq(e.mo_players(h)).filter(p => e.mo_pos(h, p) === i);
      const offered = e.mo_offerspace(h) === i;
      return {
        ...cell(here.map(p => p + 1).join('') || '', 'space o' + owner
          + (offered ? ' movable' : '')),
        i,
      };
    })),
    // The only square a click means anything on is the one you are being
    // offered, and clicking it buys. Passing is a button, because a click
    // on nothing in particular is not a decision anybody meant to make.
    move: (e, h, i) => {
      if (e.mo_offerspace(h) !== i) return null;
      return { handle: e.mo_take(h) };
    },
    actions: [
      {
        label: 'Roll',
        run: (e, h, rand) => ({ handle: e.mo_roll(h, rand()) }),
        enabled: (e, h) => e.mo_canroll(h) === 1,
      },
      {
        label: 'Buy it',
        run: (e, h) => ({ handle: e.mo_take(h) }),
        enabled: (e, h) => e.mo_candecide(h, e.mo_cur(h)) === 1,
      },
      {
        label: 'Leave it',
        run: (e, h) => ({ handle: e.mo_leave(h) }),
        enabled: (e, h) => e.mo_candecide(h, e.mo_cur(h)) === 1,
      },
    ],
    actionsInStage: true,
    steps: 300,
  },
  {
    id: 'hexwar', name: 'Hex War', cat: 'Strategy', icon: '⚔',
    desc: 'Hex-and-counter with terrain and a combat results table.',
    // Opening a turn is its own call, so a side played by hand can be handed
    // a board with its movement restored before it is asked for a move.
    // `hw_step` takes an opened turn without beginning it again, which is
    // why opening here plays the identical game in watch mode -- hw-verify
    // holds that as an arm rather than leaving it to be believed.
    boot: (e, s) => e.hw_open(e.hw_new(s % 13, s)),
    step: (e, h) => e.hw_open(e.hw_step(h)),
    // `hw_step` takes an opened turn without beginning it a second time, so
    // opening every turn as it arrives plays the identical game -- which is
    // an arm in hw-verify rather than a claim, because it was FALSE on the
    // first attempt: a turn used to restore both sides' movement, so opening
    // one side's turn early handed the other side its movement back.
    done: (e, h) => e.hw_done(h) === 1,
    // You are side one. A turn is any number of one-hex steps while a unit
    // has movement, then at most one assault, then End turn.
    human: 0,
    turn: (e, h) => e.hw_active(h),
    status: (e, h, s, sel) => {
      const head = `turn ${e.hw_turn(h)} of ${e.hw_limit(h)} · `
        + `VP ${e.hw_vp(h, 0)}-${e.hw_vp(h, 1)} · alive ${e.hw_alive(h, 0)}/${e.hw_alive(h, 1)}`;
      if (e.hw_done(h) === 1) return `${head} · side ${e.hw_winner(h) + 1} holds the field`;
      if (e.hw_active(h) !== 0) return `${head} · side ${e.hw_active(h) + 1} to move`;
      if (sel === null || sel === undefined) {
        return `${head} · ${e.hw_atkleft(h) ? 'click one of yours to move it, or an enemy to assault'
          : 'no assault left · click one of yours, or end the turn'}`;
      }
      return `${head} · unit ${sel} has ${e.hw_movepts(h, sel)} movement`
        + ` · click a hex next to it, an enemy to assault, or it again to drop it`;
    },
    // A hex index is not a unit index and neither is an owner. The board is
    // width x height hexes and a roster is a handful of units standing on
    // some of them, so the two are converted here rather than read off one
    // number (games-backlog GAME-54).
    view: (e, h, s, sel) => {
      const w = e.hw_width(h), ht = e.hw_height(h);
      const at = {};
      for (let u = 0; u < e.hw_units(h); u++) {
        if (e.hw_dead(h, u) === 1) continue;
        at[e.hw_r(h, u) * w + e.hw_q(h, u)] = u;
      }
      const yours = e.hw_active(h) === 0 && e.hw_done(h) === 0;
      const held = sel === null || sel === undefined ? null : sel;
      return {
        kind: 'hex', cols: w,
        cells: seq(w * ht).map(i => {
          const u = at[i];
          if (u === undefined) {
            const dest = yours && held !== null
              && e.hw_canmove(h, held, i % w, Math.floor(i / w)) === 1;
            return cell('', 'terrain t' + e.hw_terrain(h, i) + (dest ? ' hint' : ''), i);
          }
          const mine = e.hw_owner(h, u) === 0;
          const mark = u === held ? ' picked'
            : yours && mine && e.hw_hasmove(h, u) === 1 ? ' movable'
            : yours && !mine && e.hw_canatk(h, u) === 1 ? ' hint'
            : '';
          return cell(e.hw_str(h, u), 'unit o' + e.hw_owner(h, u) + mark, i);
        }),
      };
    },
    // The click reports a HEX. Picking up is a unit of yours that can still
    // do something; putting down is an empty hex next to it, and an enemy on
    // the board is an assault whether or not you are holding anything,
    // because a hex is assaulted by everything of yours that can reach it.
    move: (e, h, i, ctx) => {
      const w = e.hw_width(h);
      if (i < 0 || i >= w * e.hw_height(h)) return null;
      let unit = -1;
      for (let u = 0; u < e.hw_units(h); u++) {
        if (e.hw_dead(h, u) === 1) continue;
        if (e.hw_r(h, u) * w + e.hw_q(h, u) === i) { unit = u; break; }
      }
      const held = ctx && ctx.sel !== null && ctx.sel !== undefined ? ctx.sel : null;
      // `sel` is answered BEFORE `handle` by the harness, so a result
      // carrying both is read as a selection and the move is thrown away.
      // Each branch answers exactly one of them.
      if (unit >= 0 && e.hw_canatk(h, unit) === 1) {
        return { handle: e.hw_attack(h, unit, ctx.rand()) };
      }
      if (unit >= 0 && e.hw_owner(h, unit) === 0) {
        if (unit === held) return { sel: null };
        return e.hw_hasmove(h, unit) === 1 ? { sel: unit } : null;
      }
      if (held === null) return null;
      if (e.hw_canmove(h, held, i % w, Math.floor(i / w)) !== 1) return null;
      return { handle: e.hw_move(h, held, i % w, Math.floor(i / w)) };
    },
    actions: [
      {
        label: 'End turn',
        run: (e, h) => ({ handle: e.hw_endturn(h) }),
        enabled: (e, h) => e.hw_done(h) === 0 && e.hw_active(h) === 0,
      },
    ],
    actionsInStage: true,
    steps: 200,
  },
  {
    id: 'dotsandboxes', name: 'Dots and Boxes', cat: 'Other', icon: '▢',
    desc: 'Close a box and go again.',
    boot: (e, s) => e.dt_new(s),
    step: (e, h) => { const m = e.dt_ai(h); return m < 0 ? null : e.dt_place(h, m); },
    done: (e, h) => e.dt_done(h) === 1,
    status: (e, h) => `P1 ${e.dt_score(h, 0)} · P2 ${e.dt_score(h, 1)} · ${e.dt_moves(h)} moves`
      + (e.dt_done(h) === 1 ? ' · board full' : ''),
    view: (e, h) => {
      const cells = [];
      for (let gr = 0; gr < 7; gr++) for (let gc = 0; gc < 7; gc++) {
        if (gr % 2 === 0 && gc % 2 === 0) cells.push(cell('', 'dot'));
        else if (gr % 2 === 0) {
          const ei = (gr / 2) * 3 + ((gc - 1) / 2);
          cells.push(cell('', 'hedge ' + (e.dt_edge(h, ei) ? 'on' : 'off'), ei));
        } else if (gc % 2 === 0) {
          const ei = 12 + ((gr - 1) / 2) * 4 + gc / 2;
          cells.push(cell('', 'vedge ' + (e.dt_edge(h, ei) ? 'on' : 'off'), ei));
        } else {
          const b = e.dt_box(h, ((gr - 1) / 2) * 3 + ((gc - 1) / 2));
          cells.push(cell(b || '', 'boxcell o' + b));
        }
      }
      return grid(7, cells);
    },
    human: 1,
    turn: (e, h) => e.dt_cur(h),
    // The click reports the EDGE index the cell carries, not its position in
    // the 7x7 render: three quarters of that grid are dots and boxes, which
    // are not edges and are not moves.
    move: (e, h, i) => e.dt_can(h, i) === 1 ? { handle: e.dt_place(h, i) } : null,
    ghost: (e, h, i) => e.dt_can(h, i) === 1 ? { i, cls: 'edgeghost' } : null,
  },
  {
    id: 'yahtzee', name: 'Yahtzee', cat: 'Dice', icon: '\u{1F3B2}',
    desc: 'Five dice, three rolls, thirteen boxes. Every box is spent once, so the game is choosing where to put a roll you did not want.',
    boot: (e, s) => { yhRolls = 1; yhKeep = 0; return e.yh_roll(e.yh_new(s)); },
    // Watch mode plays a whole turn at a time, which is what the engine's
    // own `yh-do-turn` does: roll, keep, roll, keep, roll, choose.
    step: (e, h) => (e.yh_filled(h) >= 13 ? null : e.yh_turn(h)),
    done: (e, h) => e.yh_filled(h) >= 13,
    won: (e, h) => e.yh_filled(h) >= 13,
    status: (e, h) => {
      const filled = e.yh_filled(h);
      if (filled >= 13) return `All thirteen boxes filled · ${e.yh_total(h)} points`;
      return `${e.yh_total(h)} points · box ${filled + 1} of 13`
        + ` · roll ${yhRolls} of 3`
        + (yhRolls < 3 ? ' · click dice to hold, then Roll again' : ' · pick a box');
    },
    view: (e, h) => {
      const dice = seq(5).map(i => e.yh_die(h, i));
      const held = i => (yhKeep >> i) & 1;
      return rows([
        ['Dice', seq(5).map(i => cell(DIE_FACE[dice[i]] || dice[i],
          'die big' + (held(i) ? ' picked' : ''), YH_DIE + i))],
        // Every box shows what it would score with the dice on the table,
        // which is the whole skill of the game. A spent box shows what it
        // actually took instead.
        ...seq(2).map(half => [half ? 'Lower' : 'Upper',
          YH_CATS.slice(half ? 6 : 0, half ? 13 : 6).map((name, k) => {
            const cat = (half ? 6 : 0) + k;
            const done = e.yh_done(h, cat) === 1;
            return cell(`${name} ${done ? e.yh_card(h, cat) : e.yh_would(h, cat)}`,
              'chip' + (done ? ' spent' : ' open'), done ? undefined : YH_CAT + cat);
          })]),
      ]);
    },
    solo: true,
    // Two kinds of click: a die is held or released, a box is scored. The
    // rolls left is the page's to count -- it is a fact about the turn in
    // progress, and the engine's state has no room for it.
    move: (e, h, i) => {
      if (i >= YH_DIE && i < YH_DIE + 5) {
        if (yhRolls >= 3) return null;
        yhKeep ^= 1 << (i - YH_DIE);
        return { sel: null };
      }
      if (i >= YH_CAT && i < YH_CAT + 13) {
        const cat = i - YH_CAT;
        if (e.yh_done(h, cat) === 1) return null;
        const next = e.yh_take(h, cat);
        // A new turn opens with a fresh roll of all five, unless that was
        // the thirteenth box and the game is over.
        yhKeep = 0;
        if (e.yh_filled(next) >= 13) { yhRolls = 3; return { handle: next }; }
        yhRolls = 1;
        return { handle: e.yh_roll(next) };
      }
      return null;
    },
    actions: [
      {
        label: 'Roll again',
        run: (e, h) => { yhRolls += 1; return { handle: e.yh_reroll(h, yhKeep) }; },
        enabled: (e, h) => yhRolls < 3 && e.yh_filled(h) < 13,
      },
    ],
    actionsInStage: true,
    runs: (e, s) => {
      const r = e.yh_run(s);
      return `${e.yh_rscore(r)} points over ${e.yh_rturns(r)} turns`;
    },
    steps: 13,
  },
  {
    id: 'rps', name: 'Rock Paper Scissors', cat: 'Other', icon: '✊',
    desc: 'Your opponent plays your own history back at you. Throw the same thing twice and find out how quickly it notices.',
    boot: (e, s) => { rpLast = null; return e.rp_new(s); },
    step: (e, h) => e.rp_round(h),
    done: (e, h) => e.rp_w1(h) + e.rp_w2(h) + e.rp_ties(h) >= 20,
    status: (e, h) => {
      const n = e.rp_w1(h) + e.rp_w2(h) + e.rp_ties(h);
      const score = `you ${e.rp_w1(h)} · them ${e.rp_w2(h)} · ties ${e.rp_ties(h)}`;
      if (n >= 20) {
        const w = e.rp_w1(h), l = e.rp_w2(h);
        return `${score} · ${w > l ? 'you take the twenty' : w < l ? 'it takes the twenty' : 'twenty rounds, dead level'}`;
      }
      return `${score} · round ${n + 1} of 20`
        + (rpLast ? ` · you threw ${RPS_NAME[rpLast.you]}, it threw ${RPS_NAME[rpLast.them]}`
          + ` · ${['a tie', 'you win it', 'it wins'][rpLast.result]}` : ' · throw one');
    },
    // Three things to click, and the last round shown beside them. Without
    // the throws a player sees only a tally move and cannot tell what just
    // happened to them.
    view: (e, h) => rows([
      ['Throw', seq(3).map(i => cell(RPS_GLYPH[i],
        'chip big' + (rpLast && rpLast.you === i ? ' picked' : ''), i))],
      ['You threw', seq(3).map(i => cell(RPS_GLYPH[i] + ' ' + e.rp_c1(h, i), 'chip'))],
      ['It threw', seq(3).map(i => cell(RPS_GLYPH[i] + ' ' + e.rp_c2(h, i), 'chip'))],
    ]),
    solo: true,
    // The opponent's throw is not in the state, and it does not need to be:
    // exactly one of its three counts goes up per round, so the one that
    // moved IS what it threw. Read before and after the same move.
    move: (e, h, i) => {
      if (i < 0 || i > 2) return null;
      const before = seq(3).map(m => e.rp_c2(h, m));
      const next = e.rp_play(h, i);
      const after = seq(3).map(m => e.rp_c2(next, m));
      const them = seq(3).find(m => after[m] > before[m]);
      rpLast = {
        you: i,
        them: them === undefined ? 0 : them,
        // 0 tie, 1 you win, 2 it wins, from the engine's own comparison
        // rather than from a table written here: rp_outcome answers 1 when
        // the FIRST throw wins, and the first throw is yours.
        result: e.rp_outcome(i, them === undefined ? 0 : them) === 0 ? 0
          : e.rp_outcome(i, them === undefined ? 0 : them) === 1 ? 1 : 2,
      };
      return { handle: next };
    },
    steps: 20,
  },
];

// Checkers names its legal moves by index, so both of these are searches
// over that list rather than geometry the page could work out for itself.
function ckMove(e, h, from, to) {
  for (let m = 0; m < e.ck_moves(h); m++) {
    if (e.ck_move_from(h, m) === from && e.ck_move_to(h, m) === to) return m;
  }
  return -1;
}
function ckFrom(e, h, from) {
  for (let m = 0; m < e.ck_moves(h); m++) if (e.ck_move_from(h, m) === from) return true;
  return false;
}

function sq(v) { return v < 0 ? 'home' : v === 0 ? 'off' : v; }
function point(v) {
  const n = Math.abs(v);
  return cell(n || '', 'pt ' + (v > 0 ? 'p1' : v < 0 ? 'p2' : 'empty'));
}
function pegs(e, code) { return seq(4).map(i => e.mm_digit(code, i)).join(''); }

export const CHESS = {
  id: 'chess', name: 'Chess', cat: 'Board', icon: '♟',
  desc: 'Full rules with castling, en passant and promotion. Not built yet, and the row stays honest until it is.',
};

// The module writes nothing and reads nothing. If it asks, that is a defect
// in the module, not a thing to satisfy quietly.
export const IMPORTS = {
  wasi_snapshot_preview1: {
    fd_write: () => { throw new Error('fd_write: a game module must not write'); },
    fd_read: () => { throw new Error('fd_read: a game module must not read'); },
  },
};

// THE DEFAULT IS THAT YOU ARE PLAYING, and you are player one. A game the
// visitor can take a turn in opens in 'play' mode with the human on move;
// 'watch' is the deliberate second choice. Games with no human move (War
// has no choices; Life has no players) are watch-only and say so.
//
// `human` and `turn` are in each GAME'S OWN convention, because those are
// the numbers its accessors answer -- Royal Ur counts players from one,
// Mancala from zero, and normalising them here is how the two get confused.
// --- rendering -----------------------------------------------------------
// This lives in the module, not in the page, so that ar-verify.mjs runs the
// SAME code the browser runs. Rendering kept in an inline <script> can only
// ever be syntax-checked, and a board that comes out blank is exactly the
// kind of defect a syntax check cannot see.
export const esc = s => String(s).replace(/[&<>"]/g, c =>
  ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' }[c]));

// A card is DRAWN, never a picture. Painted court cards look like an
// illustration of a game rather than a game, they cannot be read at a
// glance in a ten-column tableau, and they carry a megabyte of art that a
// rank and a pip say better. Every card here is the same shape: index in
// the corner, suit through the middle.
// Where a printed deck puts the pips, as percentages of the card's inner
// field. Two columns at 26 and 74, a middle column at 50, and the vertical
// stops a real card uses: the pair rows at 12 and 88, the quarter rows that
// nine and ten need, and the in-between stops that carry seven's and
// eight's odd pip. The lower half is drawn inverted, which is what makes a
// printed card read the same from either end.
const CARD_PIPS = {
  A: [[50, 50]],
  2: [[50, 12], [50, 88]],
  3: [[50, 12], [50, 50], [50, 88]],
  4: [[26, 12], [74, 12], [26, 88], [74, 88]],
  5: [[26, 12], [74, 12], [50, 50], [26, 88], [74, 88]],
  6: [[26, 12], [74, 12], [26, 50], [74, 50], [26, 88], [74, 88]],
  7: [[26, 12], [74, 12], [50, 31], [26, 50], [74, 50], [26, 88], [74, 88]],
  8: [[26, 12], [74, 12], [50, 31], [26, 50], [74, 50], [50, 69], [26, 88], [74, 88]],
  9: [[26, 12], [74, 12], [26, 37], [74, 37], [50, 50], [26, 63], [74, 63], [26, 88], [74, 88]],
  10: [[26, 12], [74, 12], [50, 30], [26, 37], [74, 37], [26, 63], [74, 63], [50, 70],
    [26, 88], [74, 88]],
};

function cardHtml(c, attr) {
  const cls = c.cls || '';
  const style = c.style ? ` style="${esc(c.style)}"` : '';
  if (cls.includes('back') || cls.includes('facedown')) {
    return `<span class="${cls}"${attr}${style}></span>`;
  }
  const m = /^(10|[2-9AJQK])(.)$/.exec(c.text || '');
  if (!m) return `<span class="${cls}"${attr}${style}>${esc(c.text)}</span>`;
  const rank = m[1], suit = esc(m[2]);
  // The index is the rank over its suit in the corner, which is the half of
  // a card that has to survive being covered: in a ten-column tableau only
  // the top few millimetres of a buried card is showing.
  const index = `<i>${esc(rank)}<em>${suit}</em></i>`;
  // The ace takes the LETTER path with the courts rather than its one big pip:
  // the middle of a card carries the rank, and the suit is in the corner index
  // (Damian, 2026-09-02). So 2 to 10 are pips and A, J, Q, K are letters.
  const pips = rank === 'A' ? undefined : CARD_PIPS[rank];
  const body = pips
    ? '<span class="pips">' + pips.map(([x, y]) =>
      `<span class="pip${y > 50 ? ' inv' : ''}" style="left:${x}%;top:${y}%">${suit}</span>`)
      .join('') + '</span>'
    : `<span class="court">${esc(rank)}</span>`;
  return `<span class="${cls}"${attr}${style}>${index}${body}</span>`;
}

const gcell = (c, i) =>
  `<div class="g ${c.cls}" data-i="${c.i === undefined ? i : c.i}">${esc(c.text)}</div>`;

function gridHtml(cols, cells, clickable, board) {
  const px = Math.max(15, Math.min(48, Math.floor(560 / cols)));
  return `<div class="grid n${cols}${board ? ' board-' + board : ''}` +
    `${clickable ? ' clickable' : ''}" ` +
    `style="grid-template-columns:repeat(${cols},${px}px)">` +
    cells.map(gcell).join('') + '</div>';
}

// A die face as real pips rather than a numeral.
const PIPS = [[], [4], [0, 8], [0, 4, 8], [0, 2, 6, 8], [0, 2, 4, 6, 8], [0, 2, 3, 5, 6, 8]];
function pips(n) {
  const on = PIPS[n] || [];
  let h = '';
  for (let i = 0; i < 9; i++) h += `<i${on.includes(i) ? ' class="on"' : ''}></i>`;
  return h;
}

// Stones drawn in the cup rather than a bare number. Past a handful they
// stop being countable by eye anyway, so the number carries it from there.
function stones(n) {
  let h = '<span class="mstones">';
  for (let i = 0; i < Math.min(n, 8); i++) h += '<i></i>';
  return h + '</span>';
}

function bgPoint(p, down, alt, style) {
  const v = p.n, n = Math.abs(v), who = v > 0 ? 'w' : 'b';
  let h = `<div class="pt${down ? ' dn' : ''}${alt ? ' alt' : ''}` +
    `${p.hint ? ' hint' : ''}${p.picked ? ' picked' : ''}" ` +
    `data-i="${p.i}" style="${style || ''}">`;
  for (let i = 0; i < Math.min(5, n); i++) h += `<div class="chk ${who}"></div>`;
  if (n > 5) h += `<div class="chk more">${n}</div>`;
  return h + '</div>';
}

export function renderHtml(v, clickable) {
  if (!v) return '';

  if (v.kind === 'grid') return gridHtml(v.cols, v.cells, clickable, v.board);

  if (v.kind === 'mancala') {
    const pit = (p, side) =>
      `<div class="mpit ${side}${p.legal ? ' hint' : ''}"` +
      (p.i === undefined ? '' : ` data-i="${p.i}"`) + `>` +
      `<span class="mcount">${p.n}</span>${stones(p.n)}</div>`;
    return '<div class="mboard">' +
      `<div class="mstore north"><span>N</span><b>${v.stores.north}</b></div>` +
      '<div class="mrows">' +
      '<div class="mrow">' + v.north.map(p => pit(p, 'north')).join('') + '</div>' +
      '<div class="mrow">' + v.south.map(p => pit(p, 'south')).join('') + '</div>' +
      '</div>' +
      `<div class="mstore south"><span>you</span><b>${v.stores.south}</b></div>` +
      '</div>';
  }

  if (v.kind === 'hex') {
    let h = `<div class="hex${clickable ? ' clickable' : ''}">`;
    for (let r = 0; r * v.cols < v.cells.length; r++) {
      h += `<div class="hexrow" style="margin-left:${r * 14}px">`;
      for (let c = 0; c < v.cols; c++) {
        const i = r * v.cols + c;
        if (i < v.cells.length) h += gcell(v.cells[i], i);
      }
      h += '</div>';
    }
    return h + '</div>';
  }

  if (v.kind === 'rows') {
    return `<div class="rows${clickable ? ' clickable' : ''}">` + v.rows.map(row => {
      const cells = row[1];
      return `<div class="rowline"><span class="rowlab">${esc(row[0])}</span>` +
        cells.map(c => {
          const attr = c.i === undefined ? '' : ` data-i="${c.i}"`;
          return (c.cls || '').includes('card') ? cardHtml(c, attr)
            : `<span class="${c.cls}"${attr}>${esc(c.text)}</span>`;
        }).join('') + '</div>';
    }).join('') + '</div>';
  }

  if (v.kind === 'sudoku') {
    // The digit you are holding, then the grid. A picker is not a board, so
    // it is drawn above one rather than made to live inside it.
    const picker = '<div class="sdpick">' + v.digits.map(d =>
      `<span class="chip big${d.on ? ' picked' : ''}" data-i="${d.i}">${d.n}</span>`)
      .join('') + '</div>';
    return picker + gridHtml(9, v.cells, clickable);
  }

  if (v.kind === 'klondike') {
    // The stock, the waste, four foundations, then the tableau. The top row
    // is the game's whole state that is not a column, and it is where a
    // player looks first.
    const chip = (val, extra, i, style) => cardHtml({
      text: val < 0 ? '' : card(val),
      cls: 'card' + (val >= 0 && red(val) ? ' red' : '') + (extra ? ' ' + extra : ''),
      style,
    }, i === undefined ? '' : ` data-i="${i}"`);

    const stock = v.stock > 0
      ? `<div class="kstock" data-i="${KD_STOCK}"><b>${v.stock}</b><span>turn</span></div>`
      : `<div class="kstock spent${v.canRecycle ? ' ready' : ''}" data-i="${KD_STOCK}">` +
        `<span>${v.canRecycle ? 'gather up' : 'empty'}</span></div>`;

    const waste = v.waste > 0
      ? chip(v.wasteTop, 'kwaste' + (v.wastePicked ? ' picked' : '')
        + (v.fresh ? ' dealt' : ''), KD_WASTE)
      : '<div class="card empty kwaste"></div>';

    // An empty foundation shows the suit it is waiting for, which is the
    // only thing that tells a player where an ace is supposed to go.
    const founds = v.founds.map((f, s) => f.card >= 0
      ? chip(f.card, 'kfound' + (f.takes ? ' takes' : '') + (f.picked ? ' picked' : ''),
        KD_FOUND + s)
      : `<span class="card empty kfound${f.takes ? ' takes' : ''}` +
        `${s === 1 || s === 2 ? ' red' : ''}" data-i="${KD_FOUND + s}">` +
        `${SUIT[s]}</span>`).join('');

    const top = `<div class="ktop">${stock}${waste}<span class="kgap"></span>${founds}</div>`;

    const cols = `<div class="cols kcols${clickable ? ' clickable' : ''}` +
      `${v.won ? ' won' : ''}">` +
      v.cols.map(col => `<div class="col" data-i="${col.i}">` +
        (col.cards.length
          ? col.cards.map(c => chip(c.card, c.cls, c.i)).join('')
          : `<span class="card empty${col.takes ? ' drop' : ''}` +
            `${col.hint ? ' hintto' : ''}" data-i="${col.i}"></span>`) +
        '</div>').join('') + '</div>';

    return top + cols +
      (v.won ? '<div class="wonbanner">all fifty-two up</div>' : '');
  }

  if (v.kind === 'columns') {
    // The stock and the eight runs. Spider has no ace foundations -- a
    // completed king-down-to-ace run leaves the tableau on its own and
    // never comes back -- so what the piles show is the eight that have
    // gone, which is the game's whole score. The engine counts them
    // without recording which suit each was, so a banked run is a king
    // and no suit: every completed run is a king down to an ace.
    const tray = v.stock === undefined ? '' :
      '<div class="tray">' +
      `<div class="stockpile${v.stock ? '' : ' out'}"><b>${v.stock}</b>` +
      '<span>in the stock</span></div>' +
      '<div class="runs">' + seq(8).map(i =>
        `<div class="runslot${i < v.suits ? ' done' : ''}">${i < v.suits ? 'K' : ''}</div>`)
        .join('') +
      `<span class="runlab">${v.suits} of 8 runs away</span></div></div>`;
    const body = `<div class="cols${clickable ? ' clickable' : ''}${v.won ? ' won' : ''}">` +
      v.cols.map((col, ci) =>
        `<div class="col" data-i="${ci}">` +
        col.map(c => cardHtml(c, ` data-i="${c.i === undefined ? ci : c.i}"`)).join('') +
        '</div>').join('') + '</div>';
    return tray + (v.won ? body + '<div class="wonbanner">all eight runs away</div>' : body);
  }

  if (v.kind === 'pair') {
    return '<div class="pair">' + v.grids.map((cells, i) =>
      `<figure><figcaption>${esc(v.labels[i])}</figcaption>` +
      gridHtml(v.cols, cells, false) + '</figure>').join('') + '</div>';
  }

  if (v.kind === 'backgammon') {
    // EVERY cell is placed explicitly. The bar and the off-tray span all
    // three rows, and grid auto-placement does not reserve the columns they
    // will occupy for items that come later in source order -- so the
    // bottom row flowed into the bar's column and the whole board sheared
    // by one point. Naming the column and row of each cell is the fix.
    let h = '<div class="bgb">';
    v.top.forEach((p, i) => {
      h += bgPoint(p, false, i % 2 === 0, `grid-column:${i < 6 ? i + 1 : i + 2};grid-row:1`);
    });
    v.bottom.forEach((p, i) => {
      h += bgPoint(p, true, i % 2 === 1, `grid-column:${i < 6 ? i + 1 : i + 2};grid-row:3`);
    });
    // The bar is two halves, not one column spanning all three rows: White's
    // checkers centred in the top half, Black's in the bottom, so the middle
    // strip is left clear for the dice and the throw rather than being
    // overlapped by them.
    const barHalf = (n, who, row, hint) =>
      `<div class="bgbar${hint ? ' hint' : ''}"${hint ? ` data-i="${BG_BAR}"` : ''} ` +
      `style="grid-column:7;grid-row:${row}">` +
      (n ? `<div class="chk ${who}">${n > 1 ? n : ''}</div>` : '') + '</div>';
    h += barHalf(v.bar[0], 'w', 1, v.barHint) + barHalf(v.bar[1], 'b', 3, false);
    // The tray is two wells, one per side, each centred in its own half of
    // the board rather than three things stacked down one column. The count
    // sits under the checker instead of inside it, where it did not fit.
    const well = (n, who, row, hint) =>
      `<div class="bgoff${hint ? ' hint' : ''}"${hint ? ` data-i="${BG_OFF}"` : ''} ` +
      `style="grid-column:14;grid-row:${row}">` +
      `<span class="chk ${who}"></span><b>${n}</b><span class="lbl">off</span></div>`;
    // White bears off at the bottom, where White's home board is, and the
    // cube waits between the two trays: the middle of the bear-off zone.
    h += well(v.off[1], 'b', 1, false) +
      '<div class="bgcubewell" style="grid-column:14;grid-row:2">' +
      `<span class="bgcube" title="this engine implements no doubling">${v.cube}</span></div>` +
      well(v.off[0], 'w', 3, v.offHint);
    // The dice tray runs across the middle: White throws on the left half,
    // Black on the right, and a die that has been spent goes flat.
    // Each side throws its own dice: White's are white, Black's are black
    // with white pips.
    const die = (n, i) =>
      `<span class="bgdie ${v.thrower}` +
      `${v.spent.filter(x => x === n).length > i ? ' spent' : ''}">${pips(n)}</span>`;
    const tray = v.dice
      ? `<span class="bgdice">${v.dice.map((n, i) => die(n, i)).join('')}</span>` : '';
    // The middle of the board: dice on their thrower's side, and the throw
    // itself offered between them, where a player reaches to roll.
    const rollBtn = v.showRoll
      ? `<button class="bgroll" data-act="${BG_ROLL}">\u{1F3B2} Roll</button>` : '';
    h += '<div class="bgmid" style="grid-column:1 / 14;grid-row:2">' +
      `<span class="bghalf ${v.thrower === 'w' ? 'live' : ''}">${v.thrower === 'w' ? tray : ''}</span>` +
      rollBtn +
      `<span class="bghalf ${v.thrower === 'b' ? 'live' : ''}">${v.thrower === 'b' ? tray : ''}</span>` +
      '</div>';
    return h + '</div>';
  }

  if (v.kind === 'war') {
    const side = (s, which) =>
      `<div class="warside ${which}">` +
      `<div class="warcount">${s.n}</div>` +
      `<div class="wardeck${s.n ? '' : ' empty'}"` +
      `${s.i === undefined ? '' : ` data-i="${s.i}"`}><span>${s.name}</span></div>` +
      `<div class="warcard${s.wins ? ' takes' : ''}">` +
      (s.card < 0 ? '' : cardHtml(cardCell(s.card, 'flip'), '')) + '</div></div>';
    return '<div class="warboard">' + side(v.left, 'l') +
      `<div class="warmid">${v.war ? 'WAR' : 'vs'}</div>` +
      side(v.right, 'r') + '</div>';
  }

  if (v.kind === 'ur') {
    const sq = (c, row) => {
      if (c.gap) return '<div class="ursq gap"></div>';
      const cls = 'ursq' + (c.rosette ? ' rosette' : '') +
        (c.playable ? ' hint' : '') + ' row' + row;
      const attr = c.i === undefined ? '' : ` data-i="${c.i}"`;
      const piece = c.who ? `<i class="urp p${c.who}"></i>` : '';
      return `<div class="${cls}"${attr}><span class="urstep">${c.step}</span>${piece}</div>`;
    };
    const tray = (label, n, who) =>
      `<div class="urtray"><span>${label}</span><b class="urp p${who}">${n}</b></div>`;
    const t = v.trays;
    return '<div class="urwrap">' +
      '<div class="urside">' + tray('waiting', t.themWaiting, 2) + tray('home', t.themHome, 2) + '</div>' +
      '<div class="urboard">' +
      '<div class="urrow">' + v.top.map(c => sq(c, 2)).join('') + '</div>' +
      '<div class="urrow">' + v.mid.map(c => sq(c, 0)).join('') + '</div>' +
      '<div class="urrow">' + v.bottom.map(c => sq(c, 1)).join('') + '</div>' +
      '</div>' +
      '<div class="urside">' +
      `<div class="urtray${t.enter !== undefined ? ' hint' : ''}"` +
      (t.enter === undefined ? '' : ` data-i="${t.enter}"`) +
      `><span>waiting</span><b class="urp p1">${t.youWaiting}</b></div>` +
      tray('home', t.youHome, 1) + '</div>' +
      '</div>';
  }

  return '';
}

export function driver(game, exports) {
  let handle = null, seed = 1, rng = 1, settled = false, sel = null, roll = null;
  // Undo is nearly free here and it is worth saying why: a move does not
  // mutate the board, it answers a NEW one, so every position the game has
  // been in is still sitting in the module's heap, still valid, still
  // reachable by its handle. Undo is a stack of those. The one thing that
  // invalidates them is a heap reset, which only happens at a new game --
  // and a new game clears the stack anyway.
  let past = [];
  const remember = () => {
    past.push({ handle, settled, roll });
    if (past.length > 400) past.shift();
  };
  // Per-game choices the page offers, such as Spider's suit count.
  let opts = {};
  // The hint currently being shown, if the player asked for one.
  let shown = null;

  // THE LOW BITS HAVE TO BE GOOD HERE, and a plain LCG's are not.
  //
  // Rng.codex says it plainly: `rng-new` does not advance, it just stores
  // the seed, so `bg-wasm-die (seed) = rng-range (rng-new seed) 1 6` is
  // exactly `1 + (seed mod 6)`. The value handed in IS the die. And a
  // linear congruential generator carries entropy upward only -- its low
  // bit strictly alternates, which the chapter itself calls "the comb
  // 0101" -- so `mod 6`, which reads the bottom bits, cycled with a tiny
  // period. Backgammon threw doubles constantly and rolled the same number
  // four turns running.
  //
  // xorshift32 with a strong output finalizer instead, and shifted to stay
  // inside a positive i32 because these go across the wasm boundary as
  // signed integers, where a negative seed would make `rng-mod` answer a
  // negative die and the move would be silently refused.
  const rand = () => {
    rng ^= rng << 13; rng >>>= 0;
    rng ^= rng >>> 17;
    rng ^= rng << 5; rng >>>= 0;
    let x = rng;
    x ^= x >>> 16; x = Math.imul(x, 0x7feb352d) >>> 0;
    x ^= x >>> 15; x = Math.imul(x, 0x846ca68b) >>> 0;
    x ^= x >>> 16;
    return (x >>> 1);
  };
  const isDone = () => handle !== null && game.done(exports, handle, settled);

  const self = {
    game,
    mode: game.solo || game.click || game.move ? 'play' : 'watch',
    get handle() { return handle; },
    get exports() { return exports; },
    // The generator itself, so a grader can measure what the dice actually
    // do rather than take the driver's word for it.
    randForTest: rand,
    get settled() { return settled; },
    playable: !!(game.solo || game.click || game.move || game.keys),

    reset(s) {
      seed = s === undefined ? (Date.now() % 90000) + 1 : s;
      // xorshift32 is dead at zero, so it never starts there.
      rng = (seed >>> 0) || 0x9e3779b9;
      settled = false;
      sel = null;
      roll = null;
      past = [];
      shown = null;
      // A flat game may reset the heap freely; a handle game must NOT,
      // because the handle IS the address the reset would reclaim. Either
      // way a new game is the one safe moment to do it.
      if (exports.__heap_reset) exports.__heap_reset();
      handle = game.boot(exports, seed, opts);
      return handle;
    },

    // --- what the page may choose ---------------------------------------
    get opts() { return opts; },
    option(name, value) {
      opts = Object.assign({}, opts, { [name]: value });
      return opts;
    },

    // --- taking it back --------------------------------------------------
    canUndo() { return past.length > 0; },
    undo() {
      const prev = past.pop();
      if (!prev) return false;
      handle = prev.handle;
      settled = prev.settled;
      roll = prev.roll;
      sel = null;
      return true;
    },

    // Whose move it is, in the game's own numbering. null for solo games.
    turn() { return handle === null || !game.turn ? null : game.turn(exports, handle); },
    yourTurn() {
      if (handle === null || isDone()) return false;
      if (game.solo) return true;
      if (self.mode !== 'play' || !game.move) return false;
      return game.turn ? game.turn(exports, handle) === game.human : true;
    },

    step() {
      if (handle === null || isDone() || !game.step) return false;
      // A game whose turn opens with dice gets them BEFORE the step, so a
      // watcher sees the roll that the move follows from rather than only
      // the position it left behind. Never on YOUR turn in a game you throw
      // for yourself, though, or a stray step rolls and moves for you.
      if (game.beginTurn && roll === null &&
        !(game.manualRoll && self.mode === 'play' && self.yourTurn())) {
        roll = game.beginTurn(exports, handle, rand);
      }
      const out = game.step(exports, handle, rand, roll);
      if (out === null || out === undefined) return false;
      // A step may answer a bare handle, or a handle and what is left of
      // the turn: backgammon spends its dice one at a time.
      if (typeof out === 'object') {
        if (out.handle === null || out.handle === undefined) return false;
        handle = out.handle;
        if ('roll' in out) roll = out.roll;
      } else {
        handle = out;
      }
      return true;
    },

    // The human moves and the opponent answers. Without this a game "lets
    // you play" only in the sense that it accepts your move and then sits
    // there, which is what the arcade did before.
    //
    // A move is not always one click. Checkers and Spider need a source and
    // a destination, and Royal Ur needs the turn's roll before a piece means
    // anything, so `move` answers one of three things: {handle} for a move
    // that was made, {sel} to hold a selection and wait for the second
    // click, or null for a click the rules refuse.
    move(i) {
      if (!self.yourTurn() || !game.move) return false;
      const out = game.move(exports, handle, i, { sel, roll, rand });
      if (!out) return false;
      // A selection is not a move, and saying so is the difference between
      // a caller that can count moves and one that counts clicks. Picking a
      // piece up, and putting it back down, both answer 'select'.
      if (out.sel !== undefined) { sel = out.sel; return 'select'; }
      if (out.handle === null || out.handle === undefined) return false;
      remember();
      handle = out.handle;
      sel = null;
      // A hint is about the position it was asked in, so it does not
      // survive the move that answers it.
      shown = null;
      // A move may spend only PART of the turn. Royal Ur's roll is used up
      // by the move that follows it, but backgammon hands back what is left
      // of the dice, and clearing that unconditionally threw away three
      // quarters of a double and never ended the turn -- so one side played
      // the whole game while the other never got a move.
      roll = ('roll' in out) ? out.roll : null;
      if (out.settled) settled = true;
      return 'moved';
    },
    get sel() { return sel; },
    get roll() { return roll; },
    clearSel() { sel = null; },
    // Games whose turn opens with a die give the page the number first;
    // until it exists, no piece on the board means anything.
    beginTurn() {
      if (!game.beginTurn || roll !== null || isDone()) return roll;
      // A game that wants you to throw for yourself is not rolled for.
      if (game.manualRoll && self.mode === 'play' && self.yourTurn()) return null;
      roll = game.beginTurn(exports, handle, rand);
      return roll;
    },
    endTurn() { roll = null; sel = null; },
    // Let the opponent take its turns until the board is yours again. It is
    // bounded: an engine that never yields the turn would otherwise hang the
    // page, and that is a defect to see, not to spin on.
    reply(limit = 40) {
      if (self.mode !== 'play' || game.solo) return 0;
      let n = 0;
      while (n < limit && !isDone() && !self.yourTurn()) {
        if (!self.step()) break;
        n++;
      }
      return n;
    },

    act(index) {
      const a = game.actions && game.actions[index];
      if (!a || handle === null || isDone()) return false;
      const out = a.run(exports, handle, rand, roll);
      if (!out || out.handle === null || out.handle === undefined) return false;
      remember();
      handle = out.handle;
      // An action may also be what starts or ends a turn: rolling the dice
      // changes nothing on the board and everything about what you can do.
      if ('roll' in out) roll = out.roll;
      if (out.settled) settled = true;
      return true;
    },
    actions() {
      if (!game.actions || handle === null) return [];
      return game.actions.map((a, i) => ({
        label: a.label, index: i, inBoard: !!a.inBoard,
        enabled: !isDone() && (!a.enabled || a.enabled(exports, handle, roll)),
      }));
    },

    key(code) {
      if (handle === null || !game.key || !game.keys || isDone()) return false;
      const d = game.keys[code];
      if (d === undefined) return false;
      const next = game.key(exports, handle, d);
      if (next === null || next === undefined) return false;
      remember();
      handle = next;
      return true;
    },

    done() { return isDone(); },
    status() { return handle === null ? '' : game.status(exports, handle, settled, sel, roll, seed); },
    view() { return handle === null ? null : game.view(exports, handle, settled, sel, roll, seed, shown); },

    // A hint the page has asked to SEE. Held here rather than passed
    // through the view's positional arguments, where it landed in the slot
    // meant for the roll and quietly read as one.
    get hint() { return shown; },
    showHint() {
      shown = game.hint ? game.hint(exports, handle) : null;
      return shown;
    },
    clearHint() { shown = null; },
    runs() { return game.runs ? game.runs(exports, seed) : null; },
    seed: () => seed,
  };
  return self;
}
