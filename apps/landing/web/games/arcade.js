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
const cell = (text, cls) => ({ text: text === 0 ? '0' : (text || ''), cls: cls || '' });
const seq = n => [...Array(n).keys()];

// A hand as one row of card chips.
const hand = (n, at) => seq(n).map(at).map(c =>
  ({ text: card(c), cls: 'card' + (red(c) ? ' red' : '') }));

const PLAYERS = ['P1', 'P2', 'P3', 'P4'];

export const GAMES = [
  {
    id: 'tictactoe', name: 'Tic-Tac-Toe', cat: 'Board', icon: '❌',
    desc: 'Perfect play. The search reads every line to the end, so it has never lost one.',
    page: 'tictactoe.html', flat: true,
    boot: e => e.ttt_new(),
    // ttt_ai answers the NEXT PACKED BOARD, not a square. Reading it as a
    // move and feeding it to ttt_play returns the board unchanged, which
    // renders as a game that never starts.
    step: (e, h) => { const n = e.ttt_ai(h); return n === h ? null : n; },
    done: (e, h) => e.ttt_done(h) === 1,
    status: (e, h) => e.ttt_done(h) === 1
      ? ['A draw, which is the best anyone gets', 'X takes it', 'O takes it'][e.ttt_winner(h)]
      : (e.ttt_cur(h) === 1 ? 'X to move' : 'O to move'),
    view: (e, h) => grid(3, seq(9).map(i => {
      const v = e.ttt_cell(h, i);
      return cell(['', '×', '○'][v], 'big ' + ['', 'x', 'o'][v]);
    })),
  },
  {
    id: 'royalur', name: 'Royal Game of Ur', cat: 'Board', icon: '\u{1F3DB}',
    desc: 'The oldest rules we can still play, from about 2600 BCE. Rosettes, captures, tetrahedral dice.',
    page: 'royalur.html',
    boot: e => e.ur_new(),
    step: (e, h, r) => {
      const roll = e.ur_roll(r());
      const m = e.ur_ai(h, roll);
      return m < 0 ? e.ur_pass(h) : e.ur_play(h, m, roll);
    },
    done: (e, h) => e.ur_done(h) === 1,
    status: (e, h) => e.ur_done(h) === 1
      ? `Player ${e.ur_winner(h)} gets all seven home`
      : `Player ${e.ur_cur(h) + 1} to move · ${e.ur_scored(h, 0)}-${e.ur_scored(h, 1)} home · ${e.ur_moves(h)} moves`,
    view: (e, h) => rows([
      ['Player 1', seq(7).map(i => cell(sq(e.ur_piece(h, 0, i)), 'chip p1'))],
      ['Player 2', seq(7).map(i => cell(sq(e.ur_piece(h, 1, i)), 'chip p2'))],
      ['Home', [cell(e.ur_scored(h, 0), 'chip p1'), cell(e.ur_scored(h, 1), 'chip p2')]],
    ]),
  },
  {
    id: 'othello', name: 'Othello', cat: 'Board', icon: '●',
    desc: '8x8 Reversi. Outflank a line of discs and every one of them turns.',
    boot: e => e.ot_new(),
    step: (e, h) => { const m = e.ot_ai(h); return m < 0 ? null : e.ot_place(h, m); },
    done: (e, h) => e.ot_done(h) === 1,
    status: (e, h) => e.ot_done(h) === 1
      ? `${['A draw', 'Black', 'White'][e.ot_winner(h)]} · ${e.ot_black(h)}-${e.ot_white(h)}`
      : `${e.ot_player(h) === 1 ? 'Black' : 'White'} to move · ${e.ot_black(h)}-${e.ot_white(h)} · move ${e.ot_moves(h)}`,
    view: (e, h) => grid(8, seq(64).map(i =>
      cell('', 'felt ' + ['', 'disc black', 'disc white'][e.ot_cell(h, i)]))),
    click: (e, h, i) => e.ot_legal(h, i) === 1 ? e.ot_place(h, i) : null,
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
    view: (e, h) => grid(7, seq(42).map(i =>
      cell('', 'felt ' + ['', 'disc red', 'disc yellow'][e.c4_cell(h, Math.floor(i / 7), i % 7)]))),
    click: (e, h, i) => e.c4_can(h, i % 7) === 1 ? e.c4_drop(h, i % 7) : null,
  },
  {
    id: 'checkers', name: 'Checkers', cat: 'Board', icon: '⛀',
    desc: 'English draughts, king promotions, minimax opponent.',
    boot: e => e.ck_new(),
    step: (e, h) => { const m = e.ck_ai(h); return m < 0 ? null : e.ck_apply(h, m); },
    done: (e, h) => e.ck_done(h) === 1,
    status: (e, h) => e.ck_done(h) === 1
      ? ['Neither side can force it: a draw', 'South wins', 'North wins'][e.ck_winner(h)]
      : `${e.ck_turn(h) === 0 ? 'South' : 'North'} to move · ${e.ck_moves(h)} legal`,
    view: (e, h) => grid(8, seq(64).map(i => {
      const dark = ((Math.floor(i / 8) + i % 8) % 2) === 1;
      const v = e.ck_cell(h, i);
      return cell(['', '●', '♚', '●', '♚'][v],
        (dark ? 'sq-dark ' : 'sq-light ') + ['', 'south', 'south king', 'north', 'north king'][v]);
    })),
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
    click: (e, h, i) => e.go_cell(h, i) === 0 ? e.go_place(h, i) : null,
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
    click: (e, h, i) => e.hx_can(h, i) === 1 ? e.hx_place(h, i) : null,
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
    view: (e, h) => rows([
      ['North', [12, 11, 10, 9, 8, 7].map(i => cell(e.mc_pit(h, i), 'pit north'))],
      ['Stores', [cell(e.mc_north(h), 'store north'), cell(e.mc_south(h), 'store south')]],
      ['South', [0, 1, 2, 3, 4, 5].map(i => cell(e.mc_pit(h, i), 'pit south'))],
    ]),
  },
  {
    id: 'backgammon', name: 'Backgammon', cat: 'Board', icon: '\u{1F3B2}',
    desc: '24 points, the bar, and bearing off.',
    boot: e => e.bg_new(),
    step: (e, h, r) => e.bg_endturn(e.bg_step(h, e.bg_die(r()))),
    done: (e, h) => e.bg_done(h) === 1,
    status: (e, h) => e.bg_done(h) === 1
      ? `${['', 'White', 'Black'][e.bg_winner(h)]} bears off last`
      : `${e.bg_cur(h) === 1 ? 'White' : 'Black'} to move · off ${e.bg_off(h, 1)}/${e.bg_off(h, 2)} · bar ${e.bg_bar(h, 1)}/${e.bg_bar(h, 2)}`,
    view: (e, h) => rows([
      ['Points 1-12', seq(12).map(i => point(e.bg_point(h, i)))],
      ['Points 13-24', seq(12).map(i => point(e.bg_point(h, i + 12)))],
      ['Bar / off', [cell(e.bg_bar(h, 1), 'chip p1'), cell(e.bg_bar(h, 2), 'chip p2'),
        cell(e.bg_off(h, 1), 'chip p1'), cell(e.bg_off(h, 2), 'chip p2')]],
    ]),
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
    keys: { ArrowUp: 0, ArrowRight: 1, ArrowDown: 2, ArrowLeft: 3 },
    key: (e, h, d) => e.g2_can(h, d) === 1 ? e.g2_move(h, d) : null,
  },
  {
    id: 'life', name: "Conway's Life", cat: 'Puzzle', icon: '\u{1F9EC}',
    desc: '20x20 toroidal B3/S23. Nobody plays; it just goes.',
    boot: (e, s) => e.lf_new(s),
    step: (e, h) => e.lf_step(h),
    done: () => false,
    status: (e, h) => `${e.lf_alive(h)} alive`,
    view: (e, h) => grid(20, seq(400).map(i =>
      cell('', e.lf_cell(h, Math.floor(i / 20), i % 20) ? 'life on' : 'life off'))),
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
    click: (e, h, i) => e.ms_shown(h, i) === 1 ? null : e.ms_open(h, i),
  },
  {
    id: 'sudoku', name: 'Sudoku', cat: 'Puzzle', icon: '\u{1F9E9}',
    desc: 'A generated grid and a backtracking solver. One step solves it.',
    boot: (e, s) => e.sd_new(s),
    step: (e, h) => e.sd_first_empty ? e.sd_solve(h) : e.sd_solve(h),
    done: (e, h) => e.sd_empty(h) < 0,
    status: (e, h) => `${e.sd_givens(h)} givens · ${e.sd_iters(h)} iterations`
      + (e.sd_empty(h) < 0 ? ' · solved' : ' · unsolved'),
    view: (e, h) => grid(9, seq(81).map(i => {
      const v = e.sd_cell(h, i), r = Math.floor(i / 9), c = i % 9;
      const box = (Math.floor(r / 3) + Math.floor(c / 3)) % 2 ? ' shade' : '';
      return cell(v || '', 'sud' + box);
    })),
    steps: 2,
  },
  {
    id: 'mastermind', name: 'Mastermind', cat: 'Puzzle', icon: '\u{1F510}',
    desc: 'Four pegs. The solver keeps only codes consistent with every score so far.',
    boot: (e, s) => e.mm_new(s),
    step: (e, h) => e.mm_step(h),
    done: (e, h) => e.mm_done(h) === 1,
    status: (e, h) => `${e.mm_guesses(h)} guesses · ${e.mm_pool(h)} codes still possible`
      + (e.mm_solved(h) === 1 ? ` · cracked ${pegs(e, e.mm_secret(h))}` : ''),
    view: (e, h) => rows([
      ['Last guess', seq(4).map(i => cell(e.mm_digit(e.mm_guess(h), i), 'peg c' + e.mm_digit(e.mm_guess(h), i)))],
      ['Score', [cell(e.mm_blacks(h) + ' black', 'chip'), cell(e.mm_whites(h) + ' white', 'chip')]],
      e.mm_solved(h) === 1 && ['Secret', seq(4).map(i => cell(e.mm_digit(e.mm_secret(h), i), 'peg c' + e.mm_digit(e.mm_secret(h), i)))],
    ]),
  },
  {
    id: 'mahjong', name: 'Mahjong Solitaire', cat: 'Other', icon: '\u{1F004}',
    desc: 'Shanghai layout. Match free tiles until nothing free matches.',
    boot: (e, s) => e.mj_new(s),
    step: (e, h) => e.mj_step(h),
    done: (e, h) => e.mj_done(h) === 1,
    status: (e, h) => `${e.mj_matched(h)} pairs matched · ${e.mj_remaining(h)} tiles left`
      + (e.mj_stuck(h) === 1 ? ' · stuck' : ''),
    view: (e, h) => grid(18, seq(144).map(i => {
      const t = e.mj_tile(h, i);
      return t < 0 ? cell('', 'tile gone')
        : cell(e.mj_type(t), 'tile' + (e.mj_free(h, i) === 1 ? ' free' : ''));
    })),
  },
  {
    id: 'setgame', name: 'The Set Game', cat: 'Other', icon: '\u{1F0DF}',
    desc: 'Eighty-one cards, four attributes. A set is three cards where every attribute is all-same or all-different.',
    boot: (e, s) => e.sg_new(s),
    step: null,
    runs: (e, s) => `${e.sg_run(s)} sets found working through the deck`,
    done: () => true,
    status: (e, h) => `${e.sg_tabn(h)} on the table · ${e.sg_deckn(h)} in the deck · ${e.sg_sets(h)} sets present`,
    view: (e, h) => rows([['Tableau', seq(e.sg_tabn(h)).map(i => {
      const c = e.sg_tab(h, i);
      return cell(`${e.sg_number(c) + 1}${['●', '▲', '■'][e.sg_shape(c)]}`,
        'setcard s' + e.sg_color(c) + ' f' + e.sg_shading(c));
    })]]),
  },

  {
    id: 'blackjack', name: 'Blackjack', cat: 'Card', icon: '\u{1F0CF}',
    desc: 'Basic strategy against the dealer. Aces soften.',
    boot: (e, s) => e.bj_new(s),
    step: (e, h) => e.bj_auto(h),
    // bj_result answers -1 while the hand is live, so "not zero" reads as
    // finished the moment it is dealt and the hand is never played.
    done: (e, h) => e.bj_result(h) > 0 || e.bj_bust(h) === 1,
    status: (e, h) => `you ${e.bj_pvalue(h)}${e.bj_psoft(h) === 1 ? ' soft' : ''} · dealer ${e.bj_dvalue(h)}`
      + ' · ' + (e.bj_bust(h) === 1 ? 'bust'
        : ['still in', 'you win', 'dealer wins', 'push'][Math.max(0, e.bj_result(h))]),
    view: (e, h) => rows([
      ['You', hand(e.bj_pcount(h), i => e.bj_pcard(h, i))],
      ['Dealer', hand(e.bj_dcount(h), i => e.bj_dcard(h, i))],
    ]),
    steps: 2,
  },
  {
    id: 'war', name: 'War', cat: 'Card', icon: '\u{1F4A5}',
    desc: 'No choices at all, which is what makes it a good test: the deal and the bookkeeping are the only things to get wrong.',
    boot: (e, s) => e.wr_new(s),
    step: (e, h) => e.wr_round(h),
    done: (e, h) => e.wr_p1n(h) === 0 || e.wr_p2n(h) === 0,
    status: (e, h) => `${e.wr_p1n(h)} cards against ${e.wr_p2n(h)}`
      + (e.wr_p1n(h) === 0 ? ' · player 2 takes the deck'
        : e.wr_p2n(h) === 0 ? ' · player 1 takes the deck' : ''),
    view: (e, h) => rows([
      ['Player 1', hand(Math.min(13, e.wr_p1n(h)), i => e.wr_p1c(h, i))],
      ['Player 2', hand(Math.min(13, e.wr_p2n(h)), i => e.wr_p2c(h, i))],
    ]),
    runs: (e, s) => `player ${e.wr_winner(e.wr_run(s))} after ${e.wr_rounds(e.wr_run(s))} rounds`,
    steps: 1000,
  },
  {
    id: 'poker', name: 'Poker', cat: 'Card', icon: '\u{1F0A1}',
    desc: 'Five-card draw, nine hand ranks, and a wheel that counts as a straight.',
    boot: (e, s) => e.pk_play(s, 10),
    step: null,
    done: () => true,
    status: (e, h) => `P1 ${e.pk_p1(h)} · P2 ${e.pk_p2(h)} over ${e.pk_played(h)} hands · `
      + ['a tied session', 'player 1 ahead', 'player 2 ahead'][e.pk_winner(h)],
    view: (e, h) => rows([['Session', [cell(e.pk_p1(h) + ' - ' + e.pk_p2(h), 'chip big')]]]),
  },
  {
    id: 'pokervariants', name: 'Poker Variants', cat: 'Card', icon: '\u{1F0AA}',
    desc: 'Stud, Baseball, Hi/Low Chicago and more, each with its own wild cards.',
    // pv_run is (variant, seed, players), not (seed, variant, players).
    boot: (e, s) => e.pv_run(s % 7, s, 4),
    step: null,
    done: () => true,
    status: (e, h) => `${e.pv_players(h)} players · ${e.pv_played(h)} hands · `
      + `P1 ${e.pv_p1(h)} P2 ${e.pv_p2(h)} · winner P${e.pv_winner(h)}`
      + (e.pv_special(h) ? ' · wild cards in play' : ''),
    view: (e, h) => rows([['Result', [cell('P' + e.pv_winner(h), 'chip big')]]]),
  },
  {
    id: 'pinochle', name: 'Pinochle', cat: 'Card', icon: '\u{1F0DB}',
    desc: 'Forty-eight cards, two of every one of them, which is exactly the trap in scoring the melds.',
    boot: (e, s) => e.pn_new(s),
    step: null,
    done: () => true,
    runs: (e, s) => `team ${e.pn_winner(e.pn_run(s))} takes it`,
    status: (e, h) => `trump ${SUIT[e.pn_trump(h)]} · melds `
      + seq(4).map(p => e.pn_meld(h, p)).join(' / '),
    view: (e, h) => rows(seq(4).map(p =>
      [`Hand ${p + 1}`, hand(12, i => e.pn_card(h, p, i))])),
  },
  {
    id: 'bridge', name: 'Bridge', cat: 'Card', icon: '♠',
    desc: 'Four hands, high-card-point bidding, and a contract scored at the end.',
    boot: (e, s) => e.br_new(s),
    step: null,
    done: () => true,
    status: (e, h) => `contract ${e.br_contract(h)}${SUIT[e.br_trump(h)] || 'NT'} by ${PLAYERS[e.br_declarer(h)]}`
      + ` · ${e.br_nstricks(h)} tricks · ${e.br_made(h) === 1 ? 'made' : 'down'} · ${e.br_score(h)}`,
    view: (e, h) => rows(seq(4).map(p =>
      [`${PLAYERS[p]} (${e.br_hcp(h, p)} hcp)`, hand(e.br_count(h, p), i => e.br_card(h, p, i))])),
  },
  {
    id: 'crazyeights', name: 'Crazy Eights', cat: 'Card', icon: '\u{1F0A8}',
    desc: 'Match suit or rank. Eights are wild and name the suit.',
    boot: (e, s) => e.ce_new(s, 3),
    step: (e, h) => e.ce_step(h),
    done: (e, h) => e.ce_done(h) === 1,
    status: (e, h) => `pile ${card(e.ce_pile(h))}`
      + (e.ce_declared(h) >= 0 ? ` (called ${SUIT[e.ce_declared(h)]})` : '')
      + ` · ${PLAYERS[e.ce_cur(h)]} to play`
      + (e.ce_done(h) === 1 ? ` · ${PLAYERS[e.ce_winner(h)]} goes out` : ''),
    view: (e, h) => rows(seq(e.ce_players(h)).map(p =>
      [PLAYERS[p], [cell(e.ce_size(h, p) + ' cards', 'chip')]])),
  },
  {
    id: 'gofish', name: 'Go Fish', cat: 'Card', icon: '\u{1F41F}',
    desc: 'Ask for a rank you hold; complete four of a kind to book it.',
    boot: (e, s) => e.gf_new(s, 3),
    step: (e, h) => e.gf_step(h),
    done: (e, h) => e.gf_done(h) === 1,
    status: (e, h) => `${e.gf_pile(h)} left in the pond · ${e.gf_total(h)} books made`,
    view: (e, h) => rows(seq(e.gf_players(h)).map(p =>
      [PLAYERS[p], [cell(e.gf_size(h, p) + ' cards', 'chip'), cell(e.gf_books(h, p) + ' books', 'chip gold')]])),
  },
  {
    id: 'spider', name: 'Spider Solitaire', cat: 'Card', icon: '\u{1F578}',
    desc: 'Two suits, 104 cards, ten columns. The label and the encoding disagreed here for months.',
    boot: (e, s) => e.sp_new(s),
    step: (e, h) => {
      const m = e.sp_sugg(h);
      if (m < 0) return e.sp_stockn(h) > 0 ? e.sp_deal(h) : null;
      return e.sp_move(h, e.sp_mfrom(m), e.sp_mstart(m), e.sp_mto(m));
    },
    done: (e, h) => e.sp_suits(h) === 8,
    status: (e, h) => `${e.sp_suits(h)} of 8 suits away · ${e.sp_moves(h)} moves · ${e.sp_stockn(h)} in stock`,
    view: (e, h) => ({
      kind: 'columns',
      cols: seq(10).map(c => seq(e.sp_coln(h, c)).map(i => {
        const v = e.sp_card(h, c, i);
        return cell(v < 0 ? '\u{1F0A0}' : card(v), 'card' + (red(v) ? ' red' : ''));
      })),
    }),
    runs: (e, s) => `${e.sp_rsuits(e.sp_run(s))} of 8 suits in ${e.sp_rmoves(e.sp_run(s))} moves`,
    steps: 400,
  },

  {
    id: 'yahtzee', name: 'Yahtzee', cat: 'Dice', icon: '\u{1F3AF}',
    desc: 'Five dice, thirteen categories, and a scorer proved over all 7,776 combinations.',
    boot: (e, s) => e.yh_new(s),
    step: (e, h) => e.yh_turn(h),
    done: (e, h) => seq(13).every(c => e.yh_done(h, c) === 1),
    status: (e, h) => `${e.yh_total(h)} points · ${seq(13).filter(c => e.yh_done(h, c) === 1).length} of 13 categories`,
    view: (e, h) => rows([
      ['Dice', seq(5).map(i => cell(e.yh_die(h, i), 'die'))],
      ['Card', seq(13).map(c => cell(e.yh_done(h, c) === 1 ? e.yh_card(h, c) : '·', 'chip'))],
    ]),
    steps: 15,
  },
  {
    id: 'liarsdice', name: "Liar's Dice", cat: 'Dice', icon: '\u{1F3B2}',
    desc: 'Bid on dice you cannot see, and call the bluff when the count stops being plausible.',
    boot: (e, s) => e.ld_new(s, 4),
    step: (e, h) => e.ld_step(h),
    done: (e, h) => e.ld_done(h) === 1,
    status: (e, h) => (e.ld_bid(h) > 0 ? `bid ${e.ld_qty(h)} × ${e.ld_face(h)}` : 'no bid yet')
      + ` · ${e.ld_total(h)} dice on the table · ${e.ld_alivenum(h)} players alive`
      + (e.ld_done(h) === 1 ? ` · ${PLAYERS[e.ld_winner(h)]} wins` : ''),
    view: (e, h) => rows(seq(e.ld_players(h)).map(p =>
      [PLAYERS[p] + (e.ld_alive(h, p) === 1 ? '' : ' (out)'),
      seq(e.ld_dice(h, p)).map(i => cell(e.ld_die(h, p, i), 'die'))])),
  },

  {
    id: 'battleship', name: 'Battleship', cat: 'Strategy', icon: '\u{1F6A2}',
    desc: '10x10, hunt and target. Both fleets are hidden until they are hit.',
    boot: (e, s) => e.bs_new(s),
    step: (e, h) => e.bs_step(h),
    done: (e, h) => e.bs_done(h) === 1,
    status: (e, h) => `P1 ${e.bs_hits(h, 0)} hits in ${e.bs_shots(h, 0)} shots · P2 ${e.bs_hits(h, 1)} in ${e.bs_shots(h, 1)}`
      + (e.bs_done(h) === 1 ? ` · player ${e.bs_winner(h)} wins` : ''),
    view: (e, h) => ({
      kind: 'pair', cols: 10, labels: ['Player 1 fires at', 'Player 2 fires at'],
      grids: [0, 1].map(p => seq(100).map(i => {
        const r = Math.floor(i / 10), c = i % 10;
        const t = e.bs_track(h, p, r, c);
        return cell(['', '·', '●'][t] || '', 'sea t' + t);
      })),
    }),
    steps: 400,
  },
  {
    id: 'risk', name: 'Risk', cat: 'Strategy', icon: '\u{1F30D}',
    desc: 'Twelve territories in four continents. The turn cap used to decide games nobody could see being decided.',
    boot: (e, s) => e.rk_new(s, 4),
    step: (e, h, r) => e.rk_turn(h, r()),
    done: (e, h) => e.rk_done(h) === 1,
    status: (e, h) => `turn ${e.rk_turnno(h)} · `
      + seq(e.rk_np(h)).map(p => `${PLAYERS[p]} ${e.rk_total(h, p)}`).join(' ')
      + (e.rk_done(h) === 1 ? ` · ${PLAYERS[e.rk_winner(h)]} takes the world`
        : ` · ${PLAYERS[e.rk_cur(h)]} to move`),
    view: (e, h) => grid(4, seq(12).map(i =>
      cell(`${e.rk_armies(h, i)}`, 'terr o' + e.rk_owner(h, i)))),
    steps: 400,
  },
  {
    id: 'monopoly', name: 'Monopoly', cat: 'Strategy', icon: '\u{1F3E0}',
    desc: 'Forty spaces, simplified: property changes hands, no houses.',
    boot: (e, s) => e.mo_new(s, 4),
    step: (e, h, r) => e.mo_step(h, r()),
    done: (e, h) => e.mo_done(h) === 1,
    // The engine ends on bankruptcy, which is rare without trading
    // (games-backlog GAME-8), so most sessions stop at the page's step
    // bound rather than at mo_cap. Saying "of 601" invented an ending.
    status: (e, h) => `turn ${e.mo_turn(h)} · `
      + seq(e.mo_players(h)).map(p => `${PLAYERS[p]} $${e.mo_cash(h, p)}`).join(' ')
      + (e.mo_done(h) === 1 ? ` · ${PLAYERS[e.mo_winner(h)]} bankrupts the rest`
        : ` · ${PLAYERS[e.mo_richest(h)]} richest`),
    view: (e, h) => grid(10, seq(40).map(i => {
      const owner = e.mo_owned(h, i) === 1 ? e.mo_owner(h, i) + 1 : 0;
      const here = seq(e.mo_players(h)).filter(p => e.mo_pos(h, p) === i);
      return cell(here.map(p => p + 1).join('') || '', 'space o' + owner);
    })),
    steps: 300,
  },
  {
    id: 'hexwar', name: 'Hex War', cat: 'Strategy', icon: '⚔',
    desc: 'Hex-and-counter with terrain and a combat results table.',
    boot: (e, s) => e.hw_new(s % 13, s),
    step: (e, h) => e.hw_step(h),
    done: (e, h) => e.hw_done(h) === 1,
    status: (e, h) => `turn ${e.hw_turn(h)} of ${e.hw_limit(h)} · side ${e.hw_active(h) + 1} · `
      + `VP ${e.hw_vp(h, 0)}-${e.hw_vp(h, 1)} · alive ${e.hw_alive(h, 0)}/${e.hw_alive(h, 1)}`
      + (e.hw_done(h) === 1 ? ` · side ${e.hw_winner(h) + 1} holds the field` : ''),
    view: (e, h) => {
      const w = e.hw_width(h), ht = e.hw_height(h);
      const at = {};
      for (let u = 0; u < e.hw_units(h); u++) {
        if (e.hw_dead(h, u) === 1) continue;
        at[e.hw_r(h, u) * w + e.hw_q(h, u)] = u;
      }
      return {
        kind: 'hex', cols: w,
        cells: seq(w * ht).map(i => {
          const u = at[i];
          return u === undefined
            ? cell('', 'terrain t' + e.hw_terrain(h, i))
            : cell(e.hw_str(h, u), 'unit o' + e.hw_owner(h, u));
        }),
      };
    },
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
        else if (gr % 2 === 0) cells.push(cell('', 'hedge ' + (e.dt_edge(h, (gr / 2) * 3 + ((gc - 1) / 2)) ? 'on' : 'off')));
        else if (gc % 2 === 0) cells.push(cell('', 'vedge ' + (e.dt_edge(h, 12 + ((gr - 1) / 2) * 4 + gc / 2) ? 'on' : 'off')));
        else {
          const b = e.dt_box(h, ((gr - 1) / 2) * 3 + ((gc - 1) / 2));
          cells.push(cell(b || '', 'boxcell o' + b));
        }
      }
      return grid(7, cells);
    },
  },
  {
    id: 'rps', name: 'Rock Paper Scissors', cat: 'Other', icon: '✊',
    desc: 'One side plays its own history back at it, which is invisible to any score the two of them share.',
    boot: (e, s) => e.rp_new(s),
    step: (e, h) => e.rp_round(h),
    done: (e, h) => e.rp_w1(h) + e.rp_w2(h) + e.rp_ties(h) >= 20,
    status: (e, h) => `P1 ${e.rp_w1(h)} · P2 ${e.rp_w2(h)} · ties ${e.rp_ties(h)}`,
    view: (e, h) => rows([
      ['P1 threw', seq(3).map(i => cell(['✊', '✋', '✌'][i] + ' ' + e.rp_c1(h, i), 'chip'))],
      ['P2 threw', seq(3).map(i => cell(['✊', '✋', '✌'][i] + ' ' + e.rp_c2(h, i), 'chip'))],
    ]),
    steps: 20,
  },
];

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

export function driver(game, exports) {
  let handle = null, seed = 1, rng = 1;
  const rand = () => (rng = (rng * 1103515245 + 12345) & 0x7fffffff);
  return {
    game,
    get handle() { return handle; },
    reset(s) {
      seed = s === undefined ? (Date.now() % 90000) + 1 : s;
      rng = seed;
      if (exports.__heap_reset) exports.__heap_reset();
      handle = game.boot(exports, seed);
      return handle;
    },
    // A flat game may reset the heap freely; a handle game must not, because
    // the handle IS the address of the state the reset would reclaim.
    step() {
      if (handle === null || game.done(exports, handle)) return false;
      if (!game.step) return false;
      const next = game.step(exports, handle, rand);
      if (next === null || next === undefined) return false;
      handle = next;
      return true;
    },
    done() { return handle !== null && game.done(exports, handle); },
    status() { return handle === null ? '' : game.status(exports, handle); },
    view() { return handle === null ? null : game.view(exports, handle); },
    click(i) {
      if (handle === null || !game.click) return false;
      const next = game.click(exports, handle, i);
      if (next === null || next === undefined) return false;
      handle = next;
      return true;
    },
    key(code) {
      if (handle === null || !game.key || !game.keys) return false;
      const d = game.keys[code];
      if (d === undefined) return false;
      const next = game.key(exports, handle, d);
      if (next === null || next === undefined) return false;
      handle = next;
      return true;
    },
    runs() { return game.runs ? game.runs(exports, seed) : null; },
    seed: () => seed,
  };
}
