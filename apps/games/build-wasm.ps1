# Compile one classic game to a wasm module for the landing site.
#
#   source -> IR-CCE -> codex/plugs/wasm -> WAT -> wat2wasm -> .wasm
#
# The same shape apps/fishtank/build-wasm.ps1 uses, with two differences.
# compile.ps1 resolves cites itself (build/quire-map.ps1, Resolve-CiteOrder),
# so there is no hand-rolled bundler here and no second copy of the quire
# table to drift. And the export wrappers are generated from the $Games table
# below rather than written as WAT by hand, so adding a game is a row.
#
# A game whose module fails to build is a PARITY finding for the wasm plug
# lane (reek), not something to work around here: report the game and the
# failing step, and leave the game's Codex source alone.
#
# Usage: pwsh apps/games/build-wasm.ps1 [-Game tictactoe] [-WatOnly]
[CmdletBinding()]
param(
    [string]$Game = 'tictactoe',
    [switch]$WatOnly,
    [string]$Kernel = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$Repo    = (Resolve-Path (Join-Path $PSScriptRoot '..' '..')).Path
$PlugCdx = Join-Path $Repo 'codex\plugs\wasm\build-output\wasm-plug.cdx'
$OutDir  = Join-Path $Repo 'apps\landing\web\games'
$WorkDir = Join-Path $PSScriptRoot 'build-output'

# Two state contracts live here, and which one a game uses is decided by
# whether its state fits a signed i32.
#
# TicTacToe threads the whole game as ONE integer (24 bits), so the page
# holds the state, the module keeps none, and the page may call
# __heap_reset before every call.
#
# Every other game is too wide for that (RoyalUr is about 71 bits, Sudoku
# 324, Spider dozens of words; games-backlog.md GAME-11 has the census), so
# the page holds a HANDLE: a record is a heap object, so the address the
# wrapper narrows to i32 IS the board. A handle is a value, which costs the
# game's Wasm chapter one copy before it applies a move, because
# list-set-at mutates in place. The page must NOT reset the heap between
# calls under this contract; it resets at a new game.
$Games = @{
    'monopoly' = @{
        Chapter = 'classic\MonopolyWasm.codex'
        Exports = @(
            @{ Name = 'mo_new';     Fn = 'mo_wasm_new';        Arity = 2 }
            @{ Name = 'mo_step';    Fn = 'mo_wasm_step';       Arity = 2 }
            @{ Name = 'mo_players'; Fn = 'mo_wasm_players';    Arity = 1 }
            @{ Name = 'mo_cash';    Fn = 'mo_wasm_cash';       Arity = 2 }
            @{ Name = 'mo_pos';     Fn = 'mo_wasm_position';   Arity = 2 }
            @{ Name = 'mo_jail';    Fn = 'mo_wasm_in_jail';    Arity = 2 }
            @{ Name = 'mo_props';   Fn = 'mo_wasm_props';      Arity = 1 }
            @{ Name = 'mo_owner';   Fn = 'mo_wasm_prop_owner'; Arity = 2 }
            @{ Name = 'mo_cost';    Fn = 'mo_wasm_prop_cost';  Arity = 2 }
            @{ Name = 'mo_color';   Fn = 'mo_wasm_prop_color'; Arity = 2 }
            @{ Name = 'mo_owned';   Fn = 'mo_wasm_owned';      Arity = 2 }
            @{ Name = 'mo_ownedat'; Fn = 'mo_wasm_owned_at';   Arity = 3 }
            @{ Name = 'mo_cur';     Fn = 'mo_wasm_cur';        Arity = 1 }
            @{ Name = 'mo_turn';    Fn = 'mo_wasm_turn';       Arity = 1 }
            @{ Name = 'mo_done';    Fn = 'mo_wasm_done';       Arity = 1 }
            @{ Name = 'mo_winner';  Fn = 'mo_wasm_winner';     Arity = 1 }
            @{ Name = 'mo_richest'; Fn = 'mo_wasm_richest';    Arity = 1 }
            @{ Name = 'mo_cap';     Fn = 'mo_wasm_turn_cap';   Arity = 0 }
        )
    }
    'minesweeper' = @{
        Chapter = 'classic\MinesweeperWasm.codex'
        Exports = @(
            @{ Name = 'ms_new';   Fn = 'ms_wasm_new';      Arity = 1 }
            @{ Name = 'ms_mine';  Fn = 'ms_wasm_mine';     Arity = 2 }
            @{ Name = 'ms_shown'; Fn = 'ms_wasm_revealed'; Arity = 2 }
            @{ Name = 'ms_adj';   Fn = 'ms_wasm_adjacent'; Arity = 2 }
            @{ Name = 'ms_count'; Fn = 'ms_wasm_count';    Arity = 1 }
            @{ Name = 'ms_hits';  Fn = 'ms_wasm_hits';     Arity = 1 }
            @{ Name = 'ms_moves'; Fn = 'ms_wasm_moves';    Arity = 1 }
            @{ Name = 'ms_done';  Fn = 'ms_wasm_done';     Arity = 1 }
            @{ Name = 'ms_won';   Fn = 'ms_wasm_won';      Arity = 1 }
            @{ Name = 'ms_safe';  Fn = 'ms_wasm_safe';     Arity = 1 }
            @{ Name = 'ms_open';  Fn = 'ms_wasm_reveal';   Arity = 2 }
            @{ Name = 'ms_ai';    Fn = 'ms_wasm_ai';       Arity = 1 }
        )
    }
    'yahtzee' = @{
        Chapter = 'classic\YahtzeeWasm.codex'
        Exports = @(
            @{ Name = 'yh_score';  Fn = 'yh_wasm_score';      Arity = 6 }
            @{ Name = 'yh_best';   Fn = 'yh_wasm_best_cat';   Arity = 5 }
            @{ Name = 'yh_new';    Fn = 'yh_wasm_new';        Arity = 1 }
            @{ Name = 'yh_card';   Fn = 'yh_wasm_card';       Arity = 2 }
            @{ Name = 'yh_done';   Fn = 'yh_wasm_scored';     Arity = 2 }
            @{ Name = 'yh_die';    Fn = 'yh_wasm_die';        Arity = 2 }
            @{ Name = 'yh_total';  Fn = 'yh_wasm_total';      Arity = 1 }
            @{ Name = 'yh_turn';   Fn = 'yh_wasm_turn';       Arity = 1 }
            @{ Name = 'yh_run';    Fn = 'yh_wasm_run';        Arity = 1 }
            @{ Name = 'yh_rscore'; Fn = 'yh_wasm_res_score';  Arity = 1 }
            @{ Name = 'yh_rturns'; Fn = 'yh_wasm_res_turns';  Arity = 1 }
        )
    }
    'war' = @{
        Chapter = 'classic\WarWasm.codex'
        Exports = @(
            @{ Name = 'wr_new';    Fn = 'wr_wasm_new';         Arity = 1 }
            @{ Name = 'wr_p1n';    Fn = 'wr_wasm_p1_size';     Arity = 1 }
            @{ Name = 'wr_p2n';    Fn = 'wr_wasm_p2_size';     Arity = 1 }
            @{ Name = 'wr_p1c';    Fn = 'wr_wasm_p1_card';     Arity = 2 }
            @{ Name = 'wr_p2c';    Fn = 'wr_wasm_p2_card';     Arity = 2 }
            @{ Name = 'wr_rank';   Fn = 'wr_wasm_rank';        Arity = 1 }
            @{ Name = 'wr_round';  Fn = 'wr_wasm_round';       Arity = 1 }
            @{ Name = 'wr_run';    Fn = 'wr_wasm_run';         Arity = 1 }
            @{ Name = 'wr_winner'; Fn = 'wr_wasm_res_winner';  Arity = 1 }
            @{ Name = 'wr_rounds'; Fn = 'wr_wasm_res_rounds';  Arity = 1 }
        )
    }
    'sudoku' = @{
        Chapter = 'classic\SudokuWasm.codex'
        Exports = @(
            @{ Name = 'sd_new';     Fn = 'sd_wasm_new';             Arity = 1 }
            @{ Name = 'sd_cell';    Fn = 'sd_wasm_cell';            Arity = 2 }
            @{ Name = 'sd_iters';   Fn = 'sd_wasm_iterations';      Arity = 1 }
            @{ Name = 'sd_givens';  Fn = 'sd_wasm_givens';          Arity = 1 }
            @{ Name = 'sd_empty';   Fn = 'sd_wasm_first_empty';     Arity = 1 }
            @{ Name = 'sd_valid';   Fn = 'sd_wasm_valid';           Arity = 4 }
            @{ Name = 'sd_solve';   Fn = 'sd_wasm_solve';           Arity = 1 }
            @{ Name = 'sd_remove';  Fn = 'sd_wasm_remove';          Arity = 3 }
            @{ Name = 'sd_run';     Fn = 'sd_wasm_run';             Arity = 0 }
            @{ Name = 'sd_rsolved'; Fn = 'sd_wasm_res_solved';      Arity = 1 }
            @{ Name = 'sd_riters';  Fn = 'sd_wasm_res_iterations';  Arity = 1 }
            @{ Name = 'sd_rgivens'; Fn = 'sd_wasm_res_givens';      Arity = 1 }
        )
    }
    'spider' = @{
        Chapter = 'classic\SpiderWasm.codex'
        Exports = @(
            @{ Name = 'sp_new';    Fn = 'sp_wasm_new';        Arity = 1 }
            @{ Name = 'sp_rank';   Fn = 'sp_wasm_rank';       Arity = 1 }
            @{ Name = 'sp_suit';   Fn = 'sp_wasm_suit';       Arity = 1 }
            @{ Name = 'sp_coln';   Fn = 'sp_wasm_col_size';   Arity = 2 }
            @{ Name = 'sp_card';   Fn = 'sp_wasm_card';       Arity = 3 }
            @{ Name = 'sp_stockn'; Fn = 'sp_wasm_stock_size'; Arity = 1 }
            @{ Name = 'sp_suits';  Fn = 'sp_wasm_suits';      Arity = 1 }
            @{ Name = 'sp_moves';  Fn = 'sp_wasm_moves';      Arity = 1 }
            @{ Name = 'sp_seqlen'; Fn = 'sp_wasm_seq_len';    Arity = 3 }
            @{ Name = 'sp_can';    Fn = 'sp_wasm_can_move';   Arity = 4 }
            @{ Name = 'sp_move';   Fn = 'sp_wasm_move';       Arity = 4 }
            @{ Name = 'sp_deal';   Fn = 'sp_wasm_deal';       Arity = 1 }
            @{ Name = 'sp_sugg';   Fn = 'sp_wasm_suggest';    Arity = 1 }
            @{ Name = 'sp_mfrom';  Fn = 'sp_wasm_move_from';  Arity = 1 }
            @{ Name = 'sp_mstart'; Fn = 'sp_wasm_move_start'; Arity = 1 }
            @{ Name = 'sp_mto';    Fn = 'sp_wasm_move_to';    Arity = 1 }
            @{ Name = 'sp_run';    Fn = 'sp_wasm_run';        Arity = 1 }
            @{ Name = 'sp_rsuits'; Fn = 'sp_wasm_res_suits';  Arity = 1 }
            @{ Name = 'sp_rmoves'; Fn = 'sp_wasm_res_moves';  Arity = 1 }
            @{ Name = 'sp_rwon';   Fn = 'sp_wasm_res_won';    Arity = 1 }
        )
    }
    'setgame' = @{
        Chapter = 'classic\SetGameWasm.codex'
        Exports = @(
            @{ Name = 'sg_number';  Fn = 'sg_wasm_number';       Arity = 1 }
            @{ Name = 'sg_shading'; Fn = 'sg_wasm_shading';      Arity = 1 }
            @{ Name = 'sg_color';   Fn = 'sg_wasm_color';        Arity = 1 }
            @{ Name = 'sg_shape';   Fn = 'sg_wasm_shape';        Arity = 1 }
            @{ Name = 'sg_valid';   Fn = 'sg_wasm_valid';        Arity = 3 }
            @{ Name = 'sg_new';     Fn = 'sg_wasm_new';          Arity = 1 }
            @{ Name = 'sg_tab';     Fn = 'sg_wasm_tableau';      Arity = 2 }
            @{ Name = 'sg_tabn';    Fn = 'sg_wasm_tableau_size'; Arity = 1 }
            @{ Name = 'sg_deck';    Fn = 'sg_wasm_deck';         Arity = 2 }
            @{ Name = 'sg_deckn';   Fn = 'sg_wasm_deck_size';    Arity = 1 }
            @{ Name = 'sg_found';   Fn = 'sg_wasm_found';        Arity = 1 }
            @{ Name = 'sg_sets';    Fn = 'sg_wasm_sets_here';    Arity = 1 }
            @{ Name = 'sg_run';     Fn = 'sg_wasm_run';          Arity = 1 }
        )
    }
    'rps' = @{
        Chapter = 'classic\RPSWasm.codex'
        Exports = @(
            @{ Name = 'rp_new';     Fn = 'rps_wasm_new';      Arity = 1 }
            @{ Name = 'rp_round';   Fn = 'rps_wasm_round';    Arity = 1 }
            @{ Name = 'rp_c1';      Fn = 'rps_wasm_p1_count'; Arity = 2 }
            @{ Name = 'rp_c2';      Fn = 'rps_wasm_p2_count'; Arity = 2 }
            @{ Name = 'rp_w1';      Fn = 'rps_wasm_p1_wins';  Arity = 1 }
            @{ Name = 'rp_w2';      Fn = 'rps_wasm_p2_wins';  Arity = 1 }
            @{ Name = 'rp_ties';    Fn = 'rps_wasm_ties';     Arity = 1 }
            @{ Name = 'rp_beats';   Fn = 'rps_wasm_beats';    Arity = 1 }
            @{ Name = 'rp_outcome'; Fn = 'rps_wasm_outcome';  Arity = 2 }
            @{ Name = 'rp_choice';  Fn = 'rps_wasm_choice';   Arity = 4 }
            @{ Name = 'rp_run';     Fn = 'rps_wasm_run';      Arity = 2 }
            @{ Name = 'rp_rp1';     Fn = 'rps_wasm_res_p1';   Arity = 1 }
            @{ Name = 'rp_rp2';     Fn = 'rps_wasm_res_p2';   Arity = 1 }
            @{ Name = 'rp_rties';   Fn = 'rps_wasm_res_ties'; Arity = 1 }
        )
    }
    'risk' = @{
        Chapter = 'classic\RiskWasm.codex'
        Exports = @(
            @{ Name = 'rk_new';    Fn = 'rk_wasm_new';         Arity = 2 }
            @{ Name = 'rk_owner';  Fn = 'rk_wasm_owner';       Arity = 2 }
            @{ Name = 'rk_armies'; Fn = 'rk_wasm_armies';      Arity = 2 }
            @{ Name = 'rk_cur';    Fn = 'rk_wasm_current';     Arity = 1 }
            @{ Name = 'rk_turnno'; Fn = 'rk_wasm_turn_number'; Arity = 1 }
            @{ Name = 'rk_done';   Fn = 'rk_wasm_done';        Arity = 1 }
            @{ Name = 'rk_winner'; Fn = 'rk_wasm_winner';      Arity = 1 }
            @{ Name = 'rk_np';     Fn = 'rk_wasm_players';     Arity = 1 }
            @{ Name = 'rk_alive';  Fn = 'rk_wasm_alive';       Arity = 2 }
            @{ Name = 'rk_total';  Fn = 'rk_wasm_total';       Arity = 2 }
            @{ Name = 'rk_reinf';  Fn = 'rk_wasm_reinf';       Arity = 2 }
            @{ Name = 'rk_adj';    Fn = 'rk_wasm_adjacent';    Arity = 2 }
            @{ Name = 'rk_cont';   Fn = 'rk_wasm_continent';   Arity = 3 }
            @{ Name = 'rk_turn';   Fn = 'rk_wasm_turn';        Arity = 2 }
            @{ Name = 'rk_run';    Fn = 'rk_wasm_run';         Arity = 2 }
            @{ Name = 'rk_rwin';   Fn = 'rk_wasm_res_winner';  Arity = 1 }
            @{ Name = 'rk_rturns'; Fn = 'rk_wasm_res_turns';   Arity = 1 }
            @{ Name = 'rk_rowner'; Fn = 'rk_wasm_res_owner';   Arity = 2 }
        )
    }
    'pokervariants' = @{
        Chapter = 'classic\PokerVariantsWasm.codex'
        Exports = @(
            @{ Name = 'pv_run';     Fn = 'pvw_wasm_run';        Arity = 3 }
            @{ Name = 'pv_winner';  Fn = 'pvw_wasm_winner';     Arity = 1 }
            @{ Name = 'pv_p1';      Fn = 'pvw_wasm_p1';         Arity = 1 }
            @{ Name = 'pv_p2';      Fn = 'pvw_wasm_p2';         Arity = 1 }
            @{ Name = 'pv_played';  Fn = 'pvw_wasm_played';     Arity = 1 }
            @{ Name = 'pv_special'; Fn = 'pvw_wasm_special';    Arity = 1 }
            @{ Name = 'pv_players'; Fn = 'pvw_wasm_players';    Arity = 1 }
            @{ Name = 'pv_best5';   Fn = 'pvw_wasm_best5';      Arity = 7 }
            @{ Name = 'pv_best5c';  Fn = 'pvw_wasm_best5_card'; Arity = 7 }
            @{ Name = 'pv_cardat';  Fn = 'pvw_wasm_card_at';    Arity = 2 }
            @{ Name = 'pv_wild';    Fn = 'pvw_wasm_wild_rank';  Arity = 6 }
            @{ Name = 'pv_wildn';   Fn = 'pvw_wasm_wild_count'; Arity = 6 }
            @{ Name = 'pv_eval5';   Fn = 'pvw_wasm_eval5';      Arity = 5 }
            @{ Name = 'pv_cmp';     Fn = 'pvw_wasm_cmp';        Arity = 2 }
            @{ Name = 'pv_rank';    Fn = 'pvw_wasm_rank';       Arity = 1 }
        )
    }
    'poker' = @{
        Chapter = 'classic\PokerWasm.codex'
        Exports = @(
            @{ Name = 'pk_hand';   Fn = 'pk_wasm_hand';      Arity = 5 }
            @{ Name = 'pk_rank';   Fn = 'pk_wasm_rank';      Arity = 1 }
            @{ Name = 'pk_pri';    Fn = 'pk_wasm_primary';   Arity = 1 }
            @{ Name = 'pk_sec';    Fn = 'pk_wasm_secondary'; Arity = 1 }
            @{ Name = 'pk_cmp';    Fn = 'pk_wasm_compare';   Arity = 2 }
            @{ Name = 'pk_deck';   Fn = 'pk_wasm_deck';      Arity = 1 }
            @{ Name = 'pk_card';   Fn = 'pk_wasm_card';      Arity = 2 }
            @{ Name = 'pk_draw';   Fn = 'pk_wasm_draw';      Arity = 5 }
            @{ Name = 'pk_play';   Fn = 'pk_wasm_session';   Arity = 2 }
            @{ Name = 'pk_p1';     Fn = 'pk_wasm_p1';        Arity = 1 }
            @{ Name = 'pk_p2';     Fn = 'pk_wasm_p2';        Arity = 1 }
            @{ Name = 'pk_winner'; Fn = 'pk_wasm_winner';    Arity = 1 }
            @{ Name = 'pk_played'; Fn = 'pk_wasm_played';    Arity = 1 }
        )
    }
    'pinochle' = @{
        Chapter = 'classic\PinochleWasm.codex'
        Exports = @(
            @{ Name = 'pn_new';    Fn = 'pn_wasm_new';    Arity = 1 }
            @{ Name = 'pn_trump';  Fn = 'pn_wasm_trump';  Arity = 1 }
            @{ Name = 'pn_card';   Fn = 'pn_wasm_card';   Arity = 3 }
            @{ Name = 'pn_meld';   Fn = 'pn_wasm_meld';   Arity = 2 }
            @{ Name = 'pn_run';    Fn = 'pn_wasm_run';    Arity = 1 }
            @{ Name = 'pn_t0';     Fn = 'pn_wasm_t0';     Arity = 1 }
            @{ Name = 'pn_t1';     Fn = 'pn_wasm_t1';     Arity = 1 }
            @{ Name = 'pn_winner'; Fn = 'pn_wasm_winner'; Arity = 1 }
        )
    }
    'othello' = @{
        Chapter = 'classic\OthelloWasm.codex'
        Exports = @(
            @{ Name = 'ot_new';    Fn = 'ot_wasm_new';    Arity = 0 }
            @{ Name = 'ot_cell';   Fn = 'ot_wasm_cell';   Arity = 2 }
            @{ Name = 'ot_player'; Fn = 'ot_wasm_player'; Arity = 1 }
            @{ Name = 'ot_black';  Fn = 'ot_wasm_black';  Arity = 1 }
            @{ Name = 'ot_white';  Fn = 'ot_wasm_white';  Arity = 1 }
            @{ Name = 'ot_moves';  Fn = 'ot_wasm_moves';  Arity = 1 }
            @{ Name = 'ot_done';   Fn = 'ot_wasm_done';   Arity = 1 }
            @{ Name = 'ot_legal';  Fn = 'ot_wasm_legal';  Arity = 2 }
            @{ Name = 'ot_flips';  Fn = 'ot_wasm_flips';  Arity = 2 }
            @{ Name = 'ot_place';  Fn = 'ot_wasm_place';  Arity = 2 }
            @{ Name = 'ot_ai';     Fn = 'ot_wasm_ai';     Arity = 1 }
            @{ Name = 'ot_winner'; Fn = 'ot_wasm_winner'; Arity = 1 }
        )
    }
    'mastermind' = @{
        Chapter = 'classic\MastermindWasm.codex'
        Exports = @(
            @{ Name = 'mm_new';     Fn = 'mm_wasm_new';        Arity = 1 }
            @{ Name = 'mm_step';    Fn = 'mm_wasm_step';       Arity = 1 }
            @{ Name = 'mm_secret';  Fn = 'mm_wasm_secret';     Arity = 1 }
            @{ Name = 'mm_pool';    Fn = 'mm_wasm_pool';       Arity = 1 }
            @{ Name = 'mm_poolat';  Fn = 'mm_wasm_pool_at';    Arity = 2 }
            @{ Name = 'mm_guesses'; Fn = 'mm_wasm_guesses';    Arity = 1 }
            @{ Name = 'mm_guess';   Fn = 'mm_wasm_last_guess'; Arity = 1 }
            @{ Name = 'mm_blacks';  Fn = 'mm_wasm_blacks';     Arity = 1 }
            @{ Name = 'mm_whites';  Fn = 'mm_wasm_whites';     Arity = 1 }
            @{ Name = 'mm_solved';  Fn = 'mm_wasm_solved';     Arity = 1 }
            @{ Name = 'mm_done';    Fn = 'mm_wasm_done';       Arity = 1 }
            @{ Name = 'mm_score';   Fn = 'mm_wasm_score';      Arity = 2 }
            @{ Name = 'mm_digit';   Fn = 'mm_wasm_digit';      Arity = 2 }
        )
    }
    'mancala' = @{
        Chapter = 'classic\MancalaWasm.codex'
        Exports = @(
            @{ Name = 'mc_new';   Fn = 'mc_wasm_new';          Arity = 0 }
            @{ Name = 'mc_pit';   Fn = 'mc_wasm_pit';          Arity = 2 }
            @{ Name = 'mc_turn';  Fn = 'mc_wasm_turn';         Arity = 1 }
            @{ Name = 'mc_done';  Fn = 'mc_wasm_done';         Arity = 1 }
            @{ Name = 'mc_legal'; Fn = 'mc_wasm_legal';        Arity = 2 }
            @{ Name = 'mc_move';  Fn = 'mc_wasm_move';         Arity = 2 }
            @{ Name = 'mc_ai';    Fn = 'mc_wasm_ai';           Arity = 2 }
            @{ Name = 'mc_south'; Fn = 'mc_wasm_south_store';  Arity = 1 }
            @{ Name = 'mc_north'; Fn = 'mc_wasm_north_store';  Arity = 1 }
        )
    }
    'mahjong' = @{
        Chapter = 'classic\MahjongWasm.codex'
        Exports = @(
            @{ Name = 'mj_new';       Fn = 'mj_wasm_new';       Arity = 1 }
            @{ Name = 'mj_tile';      Fn = 'mj_wasm_tile';      Arity = 2 }
            @{ Name = 'mj_type';      Fn = 'mj_wasm_type';      Arity = 1 }
            @{ Name = 'mj_free';      Fn = 'mj_wasm_free';      Arity = 2 }
            @{ Name = 'mj_removed';   Fn = 'mj_wasm_removed';   Arity = 1 }
            @{ Name = 'mj_matched';   Fn = 'mj_wasm_matched';   Arity = 1 }
            @{ Name = 'mj_remaining'; Fn = 'mj_wasm_remaining'; Arity = 1 }
            @{ Name = 'mj_stuck';     Fn = 'mj_wasm_stuck';     Arity = 1 }
            @{ Name = 'mj_pair';      Fn = 'mj_wasm_pair';      Arity = 1 }
            @{ Name = 'mj_step';      Fn = 'mj_wasm_step';      Arity = 1 }
            @{ Name = 'mj_done';      Fn = 'mj_wasm_done';      Arity = 1 }
        )
    }
    'life' = @{
        Chapter = 'classic\LifeWasm.codex'
        Exports = @(
            @{ Name = 'lf_new';   Fn = 'lf_wasm_new';       Arity = 1 }
            @{ Name = 'lf_blank'; Fn = 'lf_wasm_blank';     Arity = 0 }
            @{ Name = 'lf_place'; Fn = 'lf_wasm_place';     Arity = 4 }
            @{ Name = 'lf_step';  Fn = 'lf_wasm_step';      Arity = 1 }
            @{ Name = 'lf_cell';  Fn = 'lf_wasm_cell';      Arity = 3 }
            @{ Name = 'lf_alive'; Fn = 'lf_wasm_alive';     Arity = 1 }
            @{ Name = 'lf_nbrs';  Fn = 'lf_wasm_neighbors'; Arity = 3 }
        )
    }
    'liarsdice' = @{
        Chapter = 'classic\LiarsDiceWasm.codex'
        Exports = @(
            @{ Name = 'ld_new';      Fn = 'ld_wasm_new';         Arity = 2 }
            @{ Name = 'ld_step';     Fn = 'ld_wasm_step';        Arity = 1 }
            @{ Name = 'ld_players';  Fn = 'ld_wasm_players';     Arity = 1 }
            @{ Name = 'ld_dice';     Fn = 'ld_wasm_dice';        Arity = 2 }
            @{ Name = 'ld_die';      Fn = 'ld_wasm_die';         Arity = 3 }
            @{ Name = 'ld_alive';    Fn = 'ld_wasm_alive';       Arity = 2 }
            @{ Name = 'ld_alivenum'; Fn = 'ld_wasm_alive_count'; Arity = 1 }
            @{ Name = 'ld_total';    Fn = 'ld_wasm_total';       Arity = 1 }
            @{ Name = 'ld_bid';      Fn = 'ld_wasm_bid';         Arity = 1 }
            @{ Name = 'ld_qty';      Fn = 'ld_wasm_bid_qty';     Arity = 1 }
            @{ Name = 'ld_face';     Fn = 'ld_wasm_bid_face';    Arity = 1 }
            @{ Name = 'ld_turn';     Fn = 'ld_wasm_turn';        Arity = 1 }
            @{ Name = 'ld_lastbid';  Fn = 'ld_wasm_last_bidder'; Arity = 1 }
            @{ Name = 'ld_done';     Fn = 'ld_wasm_done';        Arity = 1 }
            @{ Name = 'ld_winner';   Fn = 'ld_wasm_winner';      Arity = 1 }
            @{ Name = 'ld_cface';    Fn = 'ld_wasm_count_face';  Arity = 2 }
        )
    }
    'hexwar' = @{
        Chapter = 'classic\HexWarWasm.codex'
        Exports = @(
            @{ Name = 'hw_new';      Fn = 'hw_wasm_new';            Arity = 2 }
            @{ Name = 'hw_step';     Fn = 'hw_wasm_step';           Arity = 1 }
            @{ Name = 'hw_units';    Fn = 'hw_wasm_units';          Arity = 1 }
            @{ Name = 'hw_owner';    Fn = 'hw_wasm_unit_owner';     Arity = 2 }
            @{ Name = 'hw_q';        Fn = 'hw_wasm_unit_q';         Arity = 2 }
            @{ Name = 'hw_r';        Fn = 'hw_wasm_unit_r';         Arity = 2 }
            @{ Name = 'hw_str';      Fn = 'hw_wasm_unit_strength';  Arity = 2 }
            @{ Name = 'hw_max';      Fn = 'hw_wasm_unit_max';       Arity = 2 }
            @{ Name = 'hw_dead';     Fn = 'hw_wasm_unit_dead';      Arity = 2 }
            @{ Name = 'hw_type';     Fn = 'hw_wasm_unit_type';      Arity = 2 }
            @{ Name = 'hw_alive';    Fn = 'hw_wasm_alive';          Arity = 2 }
            @{ Name = 'hw_vp';       Fn = 'hw_wasm_vp';             Arity = 2 }
            @{ Name = 'hw_turn';     Fn = 'hw_wasm_turn';           Arity = 1 }
            @{ Name = 'hw_active';   Fn = 'hw_wasm_active';         Arity = 1 }
            @{ Name = 'hw_limit';    Fn = 'hw_wasm_turn_limit';     Arity = 1 }
            @{ Name = 'hw_width';    Fn = 'hw_wasm_width';          Arity = 1 }
            @{ Name = 'hw_height';   Fn = 'hw_wasm_height';         Arity = 1 }
            @{ Name = 'hw_terrain';  Fn = 'hw_wasm_terrain';        Arity = 2 }
            @{ Name = 'hw_done';     Fn = 'hw_wasm_done';           Arity = 1 }
            @{ Name = 'hw_winner';   Fn = 'hw_wasm_winner';         Arity = 1 }
        )
    }
    'hexgame' = @{
        Chapter = 'classic\HexGameWasm.codex'
        Exports = @(
            @{ Name = 'hx_new';       Fn = 'hx_wasm_new';       Arity = 0 }
            @{ Name = 'hx_cell';      Fn = 'hx_wasm_cell';      Arity = 2 }
            @{ Name = 'hx_cur';       Fn = 'hx_wasm_cur';       Arity = 1 }
            @{ Name = 'hx_done';      Fn = 'hx_wasm_done';      Arity = 1 }
            @{ Name = 'hx_winner';    Fn = 'hx_wasm_winner';    Arity = 1 }
            @{ Name = 'hx_moves';     Fn = 'hx_wasm_moves';     Arity = 1 }
            @{ Name = 'hx_can';       Fn = 'hx_wasm_can';       Arity = 2 }
            @{ Name = 'hx_place';     Fn = 'hx_wasm_place';     Arity = 2 }
            @{ Name = 'hx_ai';        Fn = 'hx_wasm_ai';        Arity = 1 }
            @{ Name = 'hx_connected'; Fn = 'hx_wasm_connected'; Arity = 2 }
        )
    }
    'gofish' = @{
        Chapter = 'classic\GoFishWasm.codex'
        Exports = @(
            @{ Name = 'gf_new';     Fn = 'gf_wasm_new';         Arity = 2 }
            @{ Name = 'gf_step';    Fn = 'gf_wasm_step';        Arity = 1 }
            @{ Name = 'gf_has';     Fn = 'gf_wasm_has';         Arity = 3 }
            @{ Name = 'gf_size';    Fn = 'gf_wasm_size';        Arity = 2 }
            @{ Name = 'gf_books';   Fn = 'gf_wasm_books';       Arity = 2 }
            @{ Name = 'gf_total';   Fn = 'gf_wasm_total_books'; Arity = 1 }
            @{ Name = 'gf_pile';    Fn = 'gf_wasm_pile';        Arity = 1 }
            @{ Name = 'gf_players'; Fn = 'gf_wasm_players';     Arity = 1 }
            @{ Name = 'gf_cur';     Fn = 'gf_wasm_cur';         Arity = 1 }
            @{ Name = 'gf_done';    Fn = 'gf_wasm_done';        Arity = 1 }
            @{ Name = 'gf_rank';    Fn = 'gf_wasm_rank';        Arity = 1 }
            @{ Name = 'gf_rcount';  Fn = 'gf_wasm_rank_count';  Arity = 3 }
        )
    }
    'go' = @{
        Chapter = 'classic\GoWasm.codex'
        Exports = @(
            @{ Name = 'go_new';       Fn = 'go_wasm_new';       Arity = 0 }
            @{ Name = 'go_cell';      Fn = 'go_wasm_cell';      Arity = 2 }
            @{ Name = 'go_cur';       Fn = 'go_wasm_cur';       Arity = 1 }
            @{ Name = 'go_captures';  Fn = 'go_wasm_captures';  Arity = 2 }
            @{ Name = 'go_score';     Fn = 'go_wasm_score';     Arity = 2 }
            @{ Name = 'go_passes';    Fn = 'go_wasm_passes';    Arity = 1 }
            @{ Name = 'go_ko';        Fn = 'go_wasm_ko';        Arity = 1 }
            @{ Name = 'go_done';      Fn = 'go_wasm_done';      Arity = 1 }
            @{ Name = 'go_place';     Fn = 'go_wasm_place';     Arity = 2 }
            @{ Name = 'go_pass';      Fn = 'go_wasm_pass';      Arity = 1 }
            @{ Name = 'go_ai';        Fn = 'go_wasm_ai';        Arity = 2 }
            @{ Name = 'go_liberties'; Fn = 'go_wasm_liberties'; Arity = 2 }
        )
    }
    'game2048' = @{
        Chapter = 'classic\Game2048Wasm.codex'
        Exports = @(
            @{ Name = 'g2_new';   Fn = 'g2_wasm_new';   Arity = 1 }
            @{ Name = 'g2_cell';  Fn = 'g2_wasm_cell';  Arity = 2 }
            @{ Name = 'g2_score'; Fn = 'g2_wasm_score'; Arity = 1 }
            @{ Name = 'g2_moves'; Fn = 'g2_wasm_moves'; Arity = 1 }
            @{ Name = 'g2_done';  Fn = 'g2_wasm_done';  Arity = 1 }
            @{ Name = 'g2_max';   Fn = 'g2_wasm_max';   Arity = 1 }
            @{ Name = 'g2_empty'; Fn = 'g2_wasm_empty'; Arity = 1 }
            @{ Name = 'g2_sum';   Fn = 'g2_wasm_sum';   Arity = 1 }
            @{ Name = 'g2_can';   Fn = 'g2_wasm_can';   Arity = 2 }
            @{ Name = 'g2_move';  Fn = 'g2_wasm_move';  Arity = 2 }
            @{ Name = 'g2_ai';    Fn = 'g2_wasm_ai';    Arity = 1 }
        )
    }
    'dotsandboxes' = @{
        Chapter = 'classic\DotsAndBoxesWasm.codex'
        Exports = @(
            @{ Name = 'dt_new';   Fn = 'dt_wasm_new';   Arity = 1 }
            @{ Name = 'dt_edge';  Fn = 'dt_wasm_edge';  Arity = 2 }
            @{ Name = 'dt_box';   Fn = 'dt_wasm_box';   Arity = 2 }
            @{ Name = 'dt_cur';   Fn = 'dt_wasm_cur';   Arity = 1 }
            @{ Name = 'dt_score'; Fn = 'dt_wasm_score'; Arity = 2 }
            @{ Name = 'dt_moves'; Fn = 'dt_wasm_moves'; Arity = 1 }
            @{ Name = 'dt_done';  Fn = 'dt_wasm_done';  Arity = 1 }
            @{ Name = 'dt_can';   Fn = 'dt_wasm_can';   Arity = 2 }
            @{ Name = 'dt_place'; Fn = 'dt_wasm_place'; Arity = 2 }
            @{ Name = 'dt_ai';    Fn = 'dt_wasm_ai';    Arity = 1 }
        )
    }
    'crazyeights' = @{
        Chapter = 'classic\CrazyEightsWasm.codex'
        Exports = @(
            @{ Name = 'ce_new';      Fn = 'ce_wasm_new';       Arity = 2 }
            @{ Name = 'ce_step';     Fn = 'ce_wasm_step';      Arity = 1 }
            @{ Name = 'ce_has';      Fn = 'ce_wasm_has';       Arity = 3 }
            @{ Name = 'ce_size';     Fn = 'ce_wasm_size';      Arity = 2 }
            @{ Name = 'ce_can';      Fn = 'ce_wasm_can';       Arity = 3 }
            @{ Name = 'ce_players';  Fn = 'ce_wasm_players';   Arity = 1 }
            @{ Name = 'ce_cur';      Fn = 'ce_wasm_cur';       Arity = 1 }
            @{ Name = 'ce_pile';     Fn = 'ce_wasm_pile';      Arity = 1 }
            @{ Name = 'ce_drank';    Fn = 'ce_wasm_drank';     Arity = 1 }
            @{ Name = 'ce_dsuit';    Fn = 'ce_wasm_dsuit';     Arity = 1 }
            @{ Name = 'ce_declared'; Fn = 'ce_wasm_declared';  Arity = 1 }
            @{ Name = 'ce_penalty';  Fn = 'ce_wasm_penalty';   Arity = 1 }
            @{ Name = 'ce_done';     Fn = 'ce_wasm_done';      Arity = 1 }
            @{ Name = 'ce_winner';   Fn = 'ce_wasm_winner';    Arity = 1 }
            @{ Name = 'ce_rank';     Fn = 'ce_wasm_card_rank'; Arity = 1 }
            @{ Name = 'ce_suit';     Fn = 'ce_wasm_card_suit'; Arity = 1 }
        )
    }
    'checkers' = @{
        Chapter = 'classic\CheckersWasm.codex'
        Exports = @(
            @{ Name = 'ck_new';       Fn = 'ck_wasm_new';       Arity = 0 }
            @{ Name = 'ck_cell';      Fn = 'ck_wasm_cell';      Arity = 2 }
            @{ Name = 'ck_turn';      Fn = 'ck_wasm_turn';      Arity = 1 }
            @{ Name = 'ck_done';      Fn = 'ck_wasm_done';      Arity = 1 }
            @{ Name = 'ck_winner';    Fn = 'ck_wasm_winner';    Arity = 1 }
            @{ Name = 'ck_moves';     Fn = 'ck_wasm_movecount'; Arity = 1 }
            @{ Name = 'ck_move_from'; Fn = 'ck_wasm_move_from'; Arity = 2 }
            @{ Name = 'ck_move_to';   Fn = 'ck_wasm_move_to';   Arity = 2 }
            @{ Name = 'ck_move_cap';  Fn = 'ck_wasm_move_cap';  Arity = 2 }
            @{ Name = 'ck_apply';     Fn = 'ck_wasm_apply';     Arity = 2 }
            @{ Name = 'ck_ai';        Fn = 'ck_wasm_ai';        Arity = 1 }
        )
    }
    'bridge' = @{
        Chapter = 'classic\BridgeWasm.codex'
        Exports = @(
            @{ Name = 'br_new';       Fn = 'br_wasm_new';         Arity = 1 }
            @{ Name = 'br_count';     Fn = 'br_wasm_count';       Arity = 2 }
            @{ Name = 'br_card';      Fn = 'br_wasm_card';        Arity = 3 }
            @{ Name = 'br_hcp';       Fn = 'br_wasm_hcp';         Arity = 2 }
            @{ Name = 'br_trump';     Fn = 'br_wasm_trump';       Arity = 1 }
            @{ Name = 'br_contract';  Fn = 'br_wasm_contract';    Arity = 1 }
            @{ Name = 'br_declarer';  Fn = 'br_wasm_declarer';    Arity = 1 }
            @{ Name = 'br_nstricks';  Fn = 'br_wasm_nstricks';    Arity = 1 }
            @{ Name = 'br_made';      Fn = 'br_wasm_tricks_made'; Arity = 1 }
            @{ Name = 'br_score';     Fn = 'br_wasm_score';       Arity = 1 }
            @{ Name = 'br_suit';      Fn = 'br_wasm_card_suit';   Arity = 1 }
            @{ Name = 'br_rank';      Fn = 'br_wasm_card_rank';   Arity = 1 }
            @{ Name = 'br_card_hcp';  Fn = 'br_wasm_card_hcp';    Arity = 1 }
            @{ Name = 'br_beats';     Fn = 'br_wasm_beats';       Arity = 4 }
        )
    }
    'blackjack' = @{
        Chapter = 'classic\BlackjackWasm.codex'
        Exports = @(
            @{ Name = 'bj_new';        Fn = 'bj_wasm_new';        Arity = 1 }
            @{ Name = 'bj_hit';        Fn = 'bj_wasm_hit';        Arity = 1 }
            @{ Name = 'bj_stand';      Fn = 'bj_wasm_stand';      Arity = 1 }
            @{ Name = 'bj_auto';       Fn = 'bj_wasm_auto';       Arity = 1 }
            @{ Name = 'bj_pcount';     Fn = 'bj_wasm_pcount';     Arity = 1 }
            @{ Name = 'bj_dcount';     Fn = 'bj_wasm_dcount';     Arity = 1 }
            @{ Name = 'bj_pcard';      Fn = 'bj_wasm_pcard';      Arity = 2 }
            @{ Name = 'bj_dcard';      Fn = 'bj_wasm_dcard';      Arity = 2 }
            @{ Name = 'bj_pvalue';     Fn = 'bj_wasm_pvalue';     Arity = 1 }
            @{ Name = 'bj_dvalue';     Fn = 'bj_wasm_dvalue';     Arity = 1 }
            @{ Name = 'bj_card_rank';  Fn = 'bj_wasm_card_rank';  Arity = 1 }
            @{ Name = 'bj_card_value'; Fn = 'bj_wasm_card_value'; Arity = 1 }
            @{ Name = 'bj_psoft';      Fn = 'bj_wasm_psoft';      Arity = 1 }
            @{ Name = 'bj_bust';       Fn = 'bj_wasm_bust';       Arity = 1 }
            @{ Name = 'bj_result';     Fn = 'bj_wasm_result';     Arity = 1 }
            @{ Name = 'bj_deckpos';    Fn = 'bj_wasm_deckpos';    Arity = 1 }
        )
    }
    'battleship' = @{
        Chapter = 'classic\BattleshipWasm.codex'
        Exports = @(
            @{ Name = 'bs_new';    Fn = 'bs_wasm_new';    Arity = 1 }
            @{ Name = 'bs_step';   Fn = 'bs_wasm_step';   Arity = 1 }
            @{ Name = 'bs_ship';   Fn = 'bs_wasm_ship';   Arity = 4 }
            @{ Name = 'bs_track';  Fn = 'bs_wasm_track';  Arity = 4 }
            @{ Name = 'bs_hits';   Fn = 'bs_wasm_hits';   Arity = 2 }
            @{ Name = 'bs_shots';  Fn = 'bs_wasm_shots';  Arity = 2 }
            @{ Name = 'bs_done';   Fn = 'bs_wasm_done';   Arity = 1 }
            @{ Name = 'bs_winner'; Fn = 'bs_wasm_winner'; Arity = 1 }
        )
    }
    'backgammon' = @{
        Chapter = 'classic\BackgammonWasm.codex'
        Exports = @(
            @{ Name = 'bg_new';     Fn = 'bg_wasm_new';     Arity = 0 }
            @{ Name = 'bg_die';     Fn = 'bg_wasm_die';     Arity = 1 }
            @{ Name = 'bg_step';    Fn = 'bg_wasm_step';    Arity = 2 }
            @{ Name = 'bg_endturn'; Fn = 'bg_wasm_endturn'; Arity = 1 }
            @{ Name = 'bg_point';   Fn = 'bg_wasm_point';   Arity = 2 }
            @{ Name = 'bg_bar';     Fn = 'bg_wasm_bar';     Arity = 2 }
            @{ Name = 'bg_off';     Fn = 'bg_wasm_off';     Arity = 2 }
            @{ Name = 'bg_cur';     Fn = 'bg_wasm_cur';     Arity = 1 }
            @{ Name = 'bg_done';    Fn = 'bg_wasm_done';    Arity = 1 }
            @{ Name = 'bg_winner';  Fn = 'bg_wasm_winner';  Arity = 1 }
        )
    }
    'connect4' = @{
        Chapter = 'classic\Connect4Wasm.codex'
        Exports = @(
            @{ Name = 'c4_new';    Fn = 'c4_wasm_new';    Arity = 0 }
            @{ Name = 'c4_can';    Fn = 'c4_wasm_can';    Arity = 2 }
            @{ Name = 'c4_drop';   Fn = 'c4_wasm_drop';   Arity = 2 }
            @{ Name = 'c4_ai';     Fn = 'c4_wasm_ai';     Arity = 1 }
            @{ Name = 'c4_cell';   Fn = 'c4_wasm_cell';   Arity = 3 }
            @{ Name = 'c4_height'; Fn = 'c4_wasm_height'; Arity = 2 }
            @{ Name = 'c4_cur';    Fn = 'c4_wasm_cur';    Arity = 1 }
            @{ Name = 'c4_done';   Fn = 'c4_wasm_done';   Arity = 1 }
            @{ Name = 'c4_winner'; Fn = 'c4_wasm_winner'; Arity = 1 }
        )
    }
    'royalur' = @{
        Chapter = 'classic\RoyalUrWasm.codex'
        Exports = @(
            @{ Name = 'ur_new';    Fn = 'ur_wasm_new';    Arity = 0 }
            @{ Name = 'ur_roll';   Fn = 'ur_wasm_roll';   Arity = 1 }
            @{ Name = 'ur_play';   Fn = 'ur_wasm_play';   Arity = 3 }
            @{ Name = 'ur_can';    Fn = 'ur_wasm_can';    Arity = 3 }
            @{ Name = 'ur_pass';   Fn = 'ur_wasm_pass';   Arity = 1 }
            @{ Name = 'ur_ai';     Fn = 'ur_wasm_ai';     Arity = 2 }
            @{ Name = 'ur_piece';  Fn = 'ur_wasm_piece';  Arity = 3 }
            @{ Name = 'ur_cur';    Fn = 'ur_wasm_cur';    Arity = 1 }
            @{ Name = 'ur_scored'; Fn = 'ur_wasm_scored'; Arity = 2 }
            @{ Name = 'ur_done';   Fn = 'ur_wasm_done';   Arity = 1 }
            @{ Name = 'ur_winner'; Fn = 'ur_wasm_winner'; Arity = 1 }
            @{ Name = 'ur_moves';  Fn = 'ur_wasm_moves';  Arity = 1 }
        )
    }
    'tictactoe' = @{
        Chapter = 'classic\TicTacToeWasm.codex'
        Exports = @(
            @{ Name = 'ttt_new';    Fn = 'ttt_wasm_new';    Arity = 0 }
            @{ Name = 'ttt_play';   Fn = 'ttt_wasm_play';   Arity = 2 }
            @{ Name = 'ttt_ai';     Fn = 'ttt_wasm_ai';     Arity = 1 }
            @{ Name = 'ttt_cell';   Fn = 'ttt_wasm_cell';   Arity = 2 }
            @{ Name = 'ttt_cur';    Fn = 'ttt_wasm_cur';    Arity = 1 }
            @{ Name = 'ttt_done';   Fn = 'ttt_wasm_done';   Arity = 1 }
            @{ Name = 'ttt_winner'; Fn = 'ttt_wasm_winner'; Arity = 1 }
        )
    }
}

if (-not $Games.ContainsKey($Game)) {
    Write-Host "REFUSE: no wasm row for game '$Game'. Known: $(($Games.Keys | Sort-Object) -join ', ')"
    exit 2
}
$spec = $Games[$Game]
$srcPath = Join-Path $PSScriptRoot $spec.Chapter
if (-not (Test-Path -PathType Leaf $srcPath)) { Write-Host "REFUSE: missing $srcPath"; exit 2 }
if (-not (Test-Path -PathType Leaf $PlugCdx)) {
    Write-Host "REFUSE: no wasm plug at $PlugCdx (run codex/plugs/wasm/build.ps1 first)"; exit 2
}
if (-not $Kernel) { $Kernel = Join-Path $Repo 'seed\Codex.cdx' }
if (-not (Test-Path -PathType Leaf $Kernel)) { Write-Host "REFUSE: no kernel at $Kernel"; exit 2 }

. (Join-Path $Repo 'build\vm-config.ps1')
New-Item -ItemType Directory -Force -Path $WorkDir, $OutDir | Out-Null

# -- Phase 1: source -> IR-CCE ----------------------------------------
$irFile = Join-Path $WorkDir "$Game.ir"
$logFile = Join-Path $WorkDir "$Game-compile.log"
Write-Host "[games-wasm] $Game : compiling $($spec.Chapter) to IR ..."
& pwsh -NoProfile -File (Join-Path $Repo 'build\compile.ps1') `
    -Src $srcPath -Out $irFile -Log $logFile -IrCce -Kernel $Kernel
if ($LASTEXITCODE -ne 0 -or -not (Test-Path -PathType Leaf $irFile)) {
    Write-Host "FAIL: IR compile; see $logFile"
    Get-Content $logFile -ErrorAction SilentlyContinue | Select-Object -Last 25 | ForEach-Object { Write-Host "  $_" }
    exit 3
}
Write-Host "[games-wasm] IR: $((Get-Item $irFile).Length) bytes (CCE)"

# -- Phase 2: IR -> WAT through the wasm plug -------------------------
# The plug reads a CCE mode header, the IR, then a NUL.
$irBytes = [System.IO.File]::ReadAllBytes($irFile)
$hdr = [System.Collections.Generic.List[byte]]::new()
foreach ($ch in 'IR-CCE'.ToCharArray()) {
    $u = [int]$ch
    if ($u -lt 256) { $hdr.Add([byte]$script:UnicodeToCce[$u]) }
}
$hdr.Add([byte]1)
$modeHeader = $hdr.ToArray()
$combined = New-Object byte[] ($modeHeader.Length + $irBytes.Length + 1)
[Buffer]::BlockCopy($modeHeader, 0, $combined, 0, $modeHeader.Length)
[Buffer]::BlockCopy($irBytes, 0, $combined, $modeHeader.Length, $irBytes.Length)
$combined[$combined.Length - 1] = 0

$inputFile = [System.IO.Path]::GetTempFileName()
$outFile   = [System.IO.Path]::GetTempFileName()
$errFile   = [System.IO.Path]::GetTempFileName()
[System.IO.File]::WriteAllBytes($inputFile, $combined)

$vmBin = Join-Path $Repo 'tools\codex-vm.exe'
Write-Host "[games-wasm] running the wasm plug ..."
$proc = Start-Process -FilePath $vmBin -ArgumentList @(
    '-kernel', $PlugCdx, '-input', $inputFile, '-output', $outFile, '-mem', '3072', '-headless'
) -PassThru -WindowStyle Hidden -RedirectStandardError $errFile
$proc.WaitForExit(600000)
if (-not $proc.HasExited) { Stop-Process -Id $proc.Id -Force; Write-Host 'FAIL: plug timeout'; exit 4 }

# codex-vm prints 'DROPPED ... is SHORT' on a clean exit when the serial
# capture lost bytes (L-SHORT / L-UNHEARD). A truncated WAT and a wrong WAT
# are the same colour on a verdict line, so read it here rather than let a
# short module reach wat2wasm as a syntax error.
$vmErr = if (Test-Path $errFile) { Get-Content $errFile -Raw } else { '' }
if ($vmErr -and $vmErr.Contains('DROPPED')) {
    Write-Host 'FAIL: codex-vm dropped serial output; the WAT is truncated, not wrong.'
    Write-Host ($vmErr -split "`n" | Where-Object { $_.Contains('DROPPED') } | Select-Object -First 3)
    exit 5
}
if (-not (Test-Path -PathType Leaf $outFile) -or (Get-Item $outFile).Length -eq 0) {
    Write-Host 'FAIL: plug produced no output'
    if ($vmErr) { Write-Host $vmErr.Substring(0, [Math]::Min(800, $vmErr.Length)) }
    exit 5
}

$raw = [System.IO.File]::ReadAllText($outFile)
$watLines = $raw -split "`n" | Where-Object { $_ -notmatch '^(HEAP|WD|STACK|PM):' -and $_.Trim().Length -gt 0 }
$wat = ($watLines -join "`n") -replace '^[\x00-\x1f]+', ''
Remove-Item $inputFile, $outFile, $errFile -Force -ErrorAction SilentlyContinue

if ($wat -match 'CODEGEN-ERRORS|CODEGEN-HALTED|!EXC') {
    Write-Host 'FAIL: the plug refused. First lines:'
    ($wat -split "`n" | Select-Object -First 12) | ForEach-Object { Write-Host "  $_" }
    exit 5
}

# -- Phase 2b: the exported wrappers ----------------------------------
# Codex integers are i64 and JavaScript reads i32 without BigInt, so each
# export wraps: i32 arguments widen signed on the way in, the i64 result
# wraps on the way out. A state word must therefore stay inside a signed i32.
# TicTacToe's largest is 2*4^11 + (4^11 - 1) = 12,582,911, comfortably under
# 2^31. A game whose state does not fit carries it in several words rather
# than widening this wrapper.
$missing = @()
foreach ($e in $spec.Exports) {
    if ($wat -notmatch ("(?m)^\s*\(func \\?\$" + [regex]::Escape($e.Fn) + "[\s\)]")) { $missing += $e.Fn }
}
if ($missing.Count -gt 0) {
    Write-Host "FAIL: the module does not define: $($missing -join ', ')"
    Write-Host '  (a name the emitter mangled or dropped, not a bad wrapper -- check the WAT)'
    exit 6
}

$wrappers = [System.Collections.Generic.List[string]]::new()
foreach ($e in $spec.Exports) {
    $params = @(); $args = @()
    for ($i = 0; $i -lt $e.Arity; $i++) {
        $params += "(param `$a$i i32)"
        $args   += "(i64.extend_i32_s (local.get `$a$i))"
    }
    $ps = if ($params.Count) { ($params -join ' ') + ' ' } else { '' }
    $as = if ($args.Count) { ' ' + ($args -join ' ') } else { '' }
    $wrappers.Add("  (func `$api_$($e.Name) $ps(result i32) (i32.wrap_i64 (call `$$($e.Fn)$as)))")
}
foreach ($e in $spec.Exports) {
    $wrappers.Add("  (export `"$($e.Name)`" (func `$api_$($e.Name)))")
}
$wat = $wat.TrimEnd() -replace '\)\s*$', (($wrappers -join "`n") + "`n)")

$watFile = Join-Path $WorkDir "$Game.wat"
[System.IO.File]::WriteAllText($watFile, $wat, [System.Text.UTF8Encoding]::new($false))
Write-Host "[games-wasm] WAT: $watFile ($($wat.Length) chars)"

# -- Phase 3: WAT -> WASM ---------------------------------------------
if ($WatOnly) { Write-Host '[games-wasm] -WatOnly given; stopping.'; exit 0 }

if (-not (Get-Command 'wat2wasm' -ErrorAction SilentlyContinue)) {
    Write-Host "FAIL: wat2wasm is not on the Path; WAT is at $watFile"
    exit 7
}
$wasmFile = Join-Path $OutDir "$Game.wasm"
# --enable-tail-call: the emitter writes `return_call` for a tail position and
# wat2wasm will not assemble one without permission. Tail calls are baseline in
# every major browser; this is the same flag codex/plugs/wasm/build-page.ps1 and
# wasm-e2e.ps1 pass for the same reason.
& wat2wasm --enable-tail-call $watFile -o $wasmFile
if ($LASTEXITCODE -ne 0) { Write-Host "FAIL: wat2wasm; WAT is at $watFile"; exit 7 }
Write-Host "[games-wasm] WASM: $wasmFile ($((Get-Item $wasmFile).Length) bytes)"
Write-Host "[games-wasm] done"
exit 0
