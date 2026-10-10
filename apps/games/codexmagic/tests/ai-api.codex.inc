Chapter: ApiCheck
  cites CodexMagic chapter MagicServer
  cites CodexMagic chapter AIGameplayTest
  cites CodexMagic chapter CardPool

 We say:

  api-glw-store : GameState -> MagicStore
  api-glw-store (gs) =
    let base = store-turn (__record-set api-store-automatic "orders" [ar-orders, dw-reply-orders]) (pa-turn gs) True
    in (dispatch-request base "GET /api/magic/game/loyalty?index=0 HTTP/1.1").store

  api-glw-committed : Boolean
  api-glw-committed =
    let store = api-glw-store (queue-loyalty-order glw-base 0 0 (0 - 3) ai-stage-upkeep "General life")
    in let shown = handle-game-state store
    in let gs = (shown.store).game
    in text-contains (shown.resp) "\"pending-kind\":\"loyalty\"" & text-contains (shown.resp) "\"loyalty-used\":true"
    & (list-at (gs.queued-loyalty) 0).source < 0 & ((get-player gs 0).general).life == 30
    & ((get-player gs 0).general).loyalty-turn == gs.turn-number & army-loyalty gs 0 == 1 & list-length (((gs.disruption).window).choices) == 2

  api-glw-off : Boolean
  api-glw-off =
    let base = store-turn (__record-set api-store-automatic "orders" [ar-orders, posture-unattended]) (pa-turn glw-base) True
    in let done = dispatch-request base "GET /api/magic/game/loyalty?index=0 HTTP/1.1"
    in disruption-pending ((done.store).game) == False & ((get-player ((done.store).game) 0).general).life == 34 & ((done.store).turn).paused

  api-glw-refuse : Boolean
  api-glw-refuse =
    let store = api-glw-store glw-base
    in let window = ((store.game).disruption).window
    in let refused = handle-game-disruption-at store (window.nonce) (ag-hand-card (store.game) 1 6) (window.deadline - 1)
    in text-contains (refused.resp) "Disruption cannot respond to a General loyalty ability"
    & disruption-pending ((refused.store).game) & ((get-player ((refused.store).game) 1).prismatic).rays-used == 0

  api-glw-override : Boolean
  api-glw-override =
    let store = api-glw-store glw-base
    in let window = ((store.game).disruption).window
    in let refused = handle-game-override-at store (window.nonce) (window.deadline - 1)
    in text-contains (refused.resp) "cannot override a committed General response window" & disruption-pending ((refused.store).game)
    & ((get-player ((refused.store).game) 0).general).loyalty-turn == ((refused.store).game).turn-number

  api-glw-timeout : Boolean
  api-glw-timeout =
    let store = api-glw-store glw-base
    in let window = ((store.game).disruption).window
    in let done = handle-game-disruption-at store (window.nonce) (0 - 1) (window.deadline)
    in let used = dispatch-request (done.store) "GET /api/magic/game/loyalty?index=0 HTTP/1.1"
    in ((get-player ((done.store).game) 0).general).life == 34 & ((done.store).turn).paused
    & text-contains (used.resp) "General ability unavailable" & text-contains (done.resp) "\"response-kind\":\"pass\""

  api-glw-pause : Boolean
  api-glw-pause =
    let store = api-glw-store glw-base
    in let window = ((store.game).disruption).window
    in let paused = handle-game-pause-at store (window.deadline - 7000)
    in let reload = handle-game-state (paused.store)
    in let resumed = handle-game-resume-at (reload.store) (window.deadline + 90000)
    in (reload.store).pause-remaining == 7000 & text-contains (reload.resp) "\"remaining-ms\":7000"
    & (((resumed.store).game).disruption).window.deadline == window.deadline + 97000
    & ((get-player ((resumed.store).game) 0).general).loyalty-turn == (resumed.store).game.turn-number
    & ((get-player ((resumed.store).game) 0).general).life == 30

  api-glw-lethal : Boolean
  api-glw-lethal =
    let gs = glw-game (GainLife 99) (pa-cost 0 False False 0) (DealDamage 40)
    in let store = api-glw-store (queue-loyalty-order gs 0 0 (0 - 3) ai-stage-upkeep "General life")
    in let window = ((store.game).disruption).window
    in let done = handle-game-response-ability-at store (window.nonce) 20 0 (0 - 1) (window.deadline - 1)
    in ((done.store).game).game-over & ((done.store).game).winner == 1 & text-contains (done.resp) "\"abandoned\":true"
    & text-contains (done.resp) "\"pending-kind\":\"loyalty\"" & (list-at (((done.store).game).queued-loyalty) 0).source < 0
    & ((get-player ((done.store).game) 0).general).loyalty-turn == ((done.store).game).turn-number

  api-ccf-payment : Boolean
  api-ccf-payment =
    let store = __record-set (api-ccf-store ai-stage-blockers) "orders" [ccf-orders, __record-set ccf-orders "manual-payment" True]
    in let card = ag-hand-card (store.game) 1 8
    in let target-refusal = dispatch-request store ("GET /api/magic/game/play?card=" & show card & "&player=1&target=999&chains=w HTTP/1.1")
    in let payment-refusal = dispatch-request (target-refusal.store) ("GET /api/magic/game/play?card=" & show card & "&player=1&target=-1 HTTP/1.1")
    in let kept = ((payment-refusal.store).turn).combat-cantrip-stage == (0 - 1) & ((get-player ((payment-refusal.store).game) 1).prismatic).rays-used == 0
    in let played = dispatch-request (payment-refusal.store) ("GET /api/magic/game/play?card=" & show card & "&player=1&target=-1&chains=w HTTP/1.1")
    in kept & text-contains (target-refusal.resp) "invalid spell target" & text-contains (payment-refusal.resp) "invalid gemstone payment"
    & ((get-player ((played.store).game) 0).general).life == 27 & ((get-player ((played.store).game) 0).prismatic).rays-used == 0
    & ((get-player ((played.store).game) 1).prismatic).rays-used == 1

  api-ccf-pause-orders : Boolean
  api-ccf-pause-orders =
    let store = api-ccf-store ai-stage-blockers
    in let card = ag-hand-card (store.game) 1 8
    in let played = dispatch-request store ("GET /api/magic/game/play?card=" & show card & "&player=1 HTTP/1.1")
    in let paused = handle-game-pause-at (played.store) 100
    in let changed = dispatch-request (paused.store) "GET /api/magic/game/spells?player=1&policy=3&reserve=0 HTTP/1.1"
    in let resumed = handle-game-resume-at (changed.store) 999
    in let second = ag-hand-card ((resumed.store).game) 1 8
    in let refused = dispatch-request (resumed.store) ("GET /api/magic/game/play?card=" & show second & "&player=1 HTTP/1.1")
    in text-contains (refused.resp) "card cannot be played at this pause" & ((resumed.store).turn).paused
    & ((get-player ((refused.store).game) 0).general).life == 27 & ((refused.store).turn).combat-cantrip-stage == ai-stage-blockers

  api-ccf-store : Integer -> MagicStore
  api-ccf-store (stage) = store-turn (__record-set api-store-automatic "orders" [ccf-orders, ccf-orders]) (ccf-turn stage) True

  api-ccf-owner : Integer, Integer -> Boolean
  api-ccf-owner (stage) (player) =
    let store = api-ccf-store stage
    in let card = ag-hand-card (store.game) player 8
    in let enemy = if player == 0 then 1 else 0
    in let preview = dispatch-request store ("GET /api/magic/game/targets?card=" & show card & "&player=" & show player & " HTTP/1.1")
    in let played = dispatch-request (preview.store) ("GET /api/magic/game/play?card=" & show card & "&player=" & show player & "&target=" & show (0 - 1 - enemy) & " HTTP/1.1")
    in text-contains (preview.resp) "\"targets\"" & ((get-player ((played.store).game) enemy).general).life == 27
    & ((played.store).turn).paused & text-contains (played.resp) "\"combat-cantrip-used\":true"
    & text-contains (played.resp) ("\"manual-player\":" & show player)

  api-ccf-wrong : Boolean
  api-ccf-wrong =
    let store = api-ccf-store ai-stage-blockers
    in let card = ag-hand-card (store.game) 1 8
    in let preview = dispatch-request store ("GET /api/magic/game/targets?card=" & show card & "&player=0 HTTP/1.1")
    in let refused = dispatch-request (preview.store) ("GET /api/magic/game/play?card=" & show card & "&player=0 HTTP/1.1")
    in let missing = dispatch-request (refused.store) ("GET /api/magic/game/play?card=" & show card & " HTTP/1.1")
    in text-contains (preview.resp) "wrong player for this combat freeze" & text-contains (refused.resp) "wrong player for this combat freeze"
    & text-contains (missing.resp) "wrong player for this combat freeze" & ((missing.store).turn).combat-cantrip-stage == (0 - 1)
    & ((get-player ((missing.store).game) 1).prismatic).rays-used == 0

  api-ccf-reload : Boolean
  api-ccf-reload =
    let store = api-ccf-store ai-stage-blockers
    in let card = ag-hand-card (store.game) 1 8
    in let played = dispatch-request store ("GET /api/magic/game/play?card=" & show card & "&player=1 HTTP/1.1")
    in let reload = handle-game-state (played.store)
    in let second = ag-hand-card ((reload.store).game) 1 8
    in let refused = dispatch-request (reload.store) ("GET /api/magic/game/play?card=" & show second & "&player=1 HTTP/1.1")
    in text-contains (refused.resp) "card cannot be played at this pause"
    & ((get-player ((refused.store).game) 0).general).life == 27 & ((get-player ((refused.store).game) 1).prismatic).rays-used == 1

  api-aw-store : MagicStore
  api-aw-store = api-aw-store-for aw-base

  api-aw-store-for : GameState -> MagicStore
  api-aw-store-for (gs) =
    let base = store-turn (__record-set api-store-automatic "orders" [ar-orders, dw-reply-orders]) (pa-turn gs) True
    in (dispatch-request base "GET /api/magic/game/activate?permanent=10&index=0 HTTP/1.1").store

  api-aw-paid : Boolean
  api-aw-paid =
    let store = api-aw-store
    in let window = ((store.game).disruption).window
    in let shown = handle-game-state store
    in window.nonce > 0 & disruption-pending (store.game) & ((get-player (store.game) 0).general).life == 28
    & (bg-perm (store.game) 10).tapped & text-contains (shown.resp) "\"pending-kind\":\"ability\""
    & text-contains (shown.resp) "Channeler #10" & (((shown.store).game).disruption).index == 0 & list-length (window.choices) == 2

  api-aw-off : Boolean
  api-aw-off =
    let base = store-turn (__record-set api-store-automatic "orders" [ar-orders, posture-unattended]) (pa-turn aw-base) True
    in let done = dispatch-request base "GET /api/magic/game/activate?permanent=10&index=0 HTTP/1.1"
    in disruption-pending ((done.store).game) == False & ((get-player ((done.store).game) 0).general).life == 32 & ((done.store).turn).paused

  api-aw-timeout : Boolean
  api-aw-timeout =
    let store = api-aw-store
    in let window = ((store.game).disruption).window
    in let done = handle-game-disruption-at store (window.nonce) (0 - 1) (window.deadline)
    in let again = handle-game-disruption-at (done.store) (window.nonce) (0 - 1) (window.deadline + 1)
    in ((get-player ((done.store).game) 0).general).life == 32 & ((get-player ((done.store).game) 1).prismatic).rays-used == 0
    & ((done.store).turn).paused & text-contains (done.resp) "\"response-kind\":\"pass\""
    & text-contains (again.resp) "no longer pending" & disruption-pending ((again.store).game) == False

  api-aw-refuse : Boolean
  api-aw-refuse =
    let store = api-aw-store
    in let window = ((store.game).disruption).window
    in let card = ag-hand-card (store.game) 1 6
    in let refused = handle-game-disruption-at store (window.nonce) card (window.deadline - 1)
    in text-contains (refused.resp) "Disruption cannot respond to an activated ability"
    & disruption-pending ((refused.store).game) & ((get-player ((refused.store).game) 1).prismatic).rays-used == 0
    & list-length ((get-player ((refused.store).game) 1).hand) == 2

  api-aw-pause : Boolean
  api-aw-pause =
    let store = api-aw-store
    in let window = ((store.game).disruption).window
    in let paused = handle-game-pause-at store (window.deadline - 7000)
    in let reload = handle-game-state (paused.store)
    in let blocked = handle-game-disruption-at (reload.store) (window.nonce) (0 - 1) (window.deadline + 90000)
    in let resumed = handle-game-resume-at (blocked.store) (window.deadline + 90000)
    in let w = (((resumed.store).game).disruption).window
    in (reload.store).pause-remaining == 7000 & text-contains (reload.resp) "\"remaining-ms\":7000"
    & text-contains (blocked.resp) "game is paused" & disruption-pending ((resumed.store).game)
    & w.deadline == window.deadline + 97000 & w.nonce == window.nonce
    & ((get-player ((resumed.store).game) 0).general).life == 28

  api-aw-lethal : Boolean
  api-aw-lethal =
    let g = aw-game (pa-cost 1 True False 0) (GainLife 99) (pa-cost 0 False False 0) (DealDamage 40)
    in let store = api-aw-store-for g
    in let window = ((store.game).disruption).window
    in let done = handle-game-response-ability-at store (window.nonce) 20 0 (0 - 1) (window.deadline - 1)
    in ((done.store).game).game-over & ((done.store).game).winner == 1
    & text-contains (done.resp) "\"abandoned\":true" & text-contains (done.resp) "\"pending-kind\":\"ability\""
    & list-length ((get-player ((done.store).game) 0).graveyard) == 0

  api-ql-store : MagicStore
  api-ql-store = store-turn api-store-automatic (pa-turn (ql-game 9 DestroyTarget)) True

  api-ql-queue : Boolean
  api-ql-queue =
    let base = api-ql-store
    in let preview = dispatch-request base "GET /api/magic/game/queue-loyalty-targets?player=0&index=0 HTTP/1.1"
    in let queued = dispatch-request (preview.store) "GET /api/magic/game/queue-loyalty?choice=queue&player=0&index=0&target=21 HTTP/1.1"
    in let shown = handle-game-state (queued.store)
    in let q = list-at (((shown.store).game).queued-loyalty) 0
    in text-contains (preview.resp) "\"targets\"" & q.source == 0 & q.target == 21
    & text-contains (shown.resp) "Waiting for army loyalty 9" & text-contains (shown.resp) "\"queue-loyalty-options\""
    & ((get-player ((shown.store).game) 0).general).loyalty-turn == (0 - 1)

  api-ql-pause : Boolean
  api-ql-pause =
    let paused = handle-game-pause-at api-ql-store 100
    in let queued = dispatch-request (paused.store) "GET /api/magic/game/queue-loyalty?choice=queue&player=0&index=0&target=21 HTTP/1.1"
    in let shown = handle-game-state (queued.store)
    in let held = (shown.store).pause-active & (list-at (((shown.store).game).queued-loyalty) 0).target == 21
    in let blocked = dispatch-request (shown.store) "GET /api/magic/game/loyalty?index=1 HTTP/1.1"
    in let cancelled = dispatch-request (blocked.store) "GET /api/magic/game/queue-loyalty?choice=cancel&player=0 HTTP/1.1"
    in held & text-contains (blocked.resp) "paused" & (cancelled.store).pause-active & (list-at (((cancelled.store).game).queued-loyalty) 0).source < 0

  api-ql-refuse : Boolean
  api-ql-refuse =
    let queued = dispatch-request api-ql-store "GET /api/magic/game/queue-loyalty?choice=queue&player=0&index=0&target=21 HTTP/1.1"
    in let bad-index = dispatch-request (queued.store) "GET /api/magic/game/queue-loyalty?choice=queue&player=0&index=99 HTTP/1.1"
    in let bad-target = dispatch-request (bad-index.store) "GET /api/magic/game/queue-loyalty?choice=queue&player=0&index=0&target=999 HTTP/1.1"
    in let bad-player = dispatch-request (bad-target.store) "GET /api/magic/game/queue-loyalty?choice=queue&player=9&index=0 HTTP/1.1"
    in text-contains (bad-index.resp) "\"error\"" & text-contains (bad-target.resp) "\"error\"" & text-contains (bad-player.resp) "\"error\""
    & (list-at (((bad-player.store).game).queued-loyalty) 0).target == 21

  api-ql-reset : Boolean
  api-ql-reset =
    let queued = dispatch-request api-ql-store "GET /api/magic/game/queue-loyalty?choice=queue&player=0&index=0&target=21 HTTP/1.1"
    in let refused = handle-game-new (__record-set (queued.store) "templates" [pool-ruby]) "GET /api/magic/game/new HTTP/1.1"
    in let retained = text-contains (refused.resp) "\"error\"" & (list-at (((refused.store).game).queued-loyalty) 0).target == 21
    in let fresh = handle-game-new (__record-set (queued.store) "templates" full-card-pool) "GET /api/magic/game/new HTTP/1.1"
    in let cancelled = handle-game-cancel (queued.store)
    in retained & (fresh.store).game-active & (list-at (((fresh.store).game).queued-loyalty) 0).source < 0
    & (cancelled.store).game-active == False & (list-at (((cancelled.store).game).queued-loyalty) 0).source < 0

  api-ls-stats : Boolean
  api-ls-stats =
    let base = store-turn api-store-automatic (pa-turn ls-token-game) True
    in let shown = handle-game-state base
    in let value = api-j-value (shown.resp)
    in let card = api-j-array value "battlefield"
    in let general = api-j-array value "generals"
    in let repeated = handle-game-state (shown.store)
    in json-get-int card "power" (0 - 1) == 4 & json-get-int card "toughness" (0 - 1) == 5 & json-get-int card "defense" (0 - 1) == 4
    & json-get-int general "power" (0 - 1) == 2 & json-get-int general "toughness" (0 - 1) == 4
    & shown.resp == repeated.resp & (bg-perm ((repeated.store).game) 10).power-mod == 0
    & text-contains (shown.resp) "\"loyalty-abilities\":[]"

  api-ls-loss : Boolean
  api-ls-loss =
    let gs = destroy-permanent ls-token-game 10
    in let shown = handle-game-state (store-turn api-store-automatic (pa-turn gs) True)
    in let token = api-j-array (api-j-value (shown.resp)) "battlefield"
    in json-get-int token "id" (0 - 1) == 11 & json-get-int token "power" (0 - 1) == 2
    & json-get-int token "toughness" (0 - 1) == 2 & json-get-int token "defense" (0 - 1) == 0

  api-ls-cost : Boolean
  api-ls-cost =
    let base = store-turn api-store-automatic (pa-turn ls-cost-game) True
    in let normal = dispatch-request base "GET /api/magic/game/activate?permanent=10&index=0&target=11 HTTP/1.1"
    in let window-store = store-turn (__record-set api-store-automatic "orders" [ar-orders, dw-reply-orders]) ls-cost-response-pending True
    in let window = ((window-store.game).disruption).window
    in let response = handle-game-response-ability-at window-store (window.nonce) 30 0 31 (window.deadline - 1)
    in list-contains (((normal.store).game).battlefield) (\p -> p.id == 11) == False
    & list-contains (((normal.store).game).battlefield) (\p -> p.id == 12) & (bg-perm ((normal.store).game) 12).toughness-mod == 0
    & text-contains (response.resp) "\"reply-fizzled\":true" & list-length (((response.store).game).battlefield) == 1 & list-contains (((response.store).game).battlefield) (\p -> p.id == 32) & (bg-perm ((response.store).game) 32).toughness-mod == 0

  api-lg-play : Boolean
  api-lg-play =
    let g = ly-game (ly-general [LoyaltyGainAbility 1 (GainLife 4)]) [] [0] 1
    in let orders = __record-set posture-unattended "spell-policy" ManualSpells
    in let base = store-turn (__record-set api-store-automatic "orders" [orders, posture-unattended]) (pa-turn g) True
    in let card = ag-hand-card g 0 0
    in let played = dispatch-request base ("GET /api/magic/game/play?card=" & show card & " HTTP/1.1")
    in let settled = ((get-player ((played.store).game) 0).general).life == 34 & loyalty-pending ((played.store).game) == False & text-contains (played.resp) "\"life\":34"
    in let one = handle-game-state (played.store)
    in let two = handle-game-state (one.store)
    in settled & ((get-player ((two.store).game) 0).general).life == 34 & ((two.store).turn).paused
    & ((get-player ((two.store).game) 0).general).loyalty-turn == (0 - 1) & loyalty-pending ((two.store).game) == False

  api-lg-budget : Boolean
  api-lg-budget =
    let stopped = settle-loyalty (lg-game (lg-abilities 501 []) [] [ag-perm 10 0 0]) dw-orders posture-unattended
    in let store = store-turn (__record-set api-store-automatic "orders" [dw-orders, posture-unattended]) (qf-turn stopped ai-stage-main-1 True) True
    in let before = handle-game-state store
    in let blocked = text-contains (before.resp) "Automatic action budget" & ((get-player ((before.store).game) 0).general).life == 530
    in let continued = handle-game-step (before.store) True
    in blocked & ((get-player ((continued.store).game) 0).general).life == 531
    & game-budget-stopped ((continued.store).game) == False & loyalty-pending ((continued.store).game) == False

  api-lg-pause : Boolean
  api-lg-pause =
    let g = check-state-based-actions (lg-game [LoyaltyGainAbility 1 (GainLife 4)] [] [ag-perm 10 0 0])
    in let base = store-turn api-store-automatic (pa-turn g) True
    in let paused = handle-game-pause-at base 100
    in let held = ((get-player ((paused.store).game) 0).general).life == 30 & loyalty-pending ((paused.store).game)
    in let resumed = handle-game-resume-at (paused.store) 500
    in held & ((get-player ((resumed.store).game) 0).general).life == 34 & ((resumed.store).turn).paused

  api-lg-reset : Boolean
  api-lg-reset =
    let g = check-state-based-actions (lg-game [LoyaltyGainAbility 1 (GainLife 4)] [] [ag-perm 10 0 0])
    in let base = store-turn api-store-automatic (pa-turn g) True
    in let refused = handle-game-new (__record-set (store-turn api-store-automatic (pa-turn g) True) "templates" [pool-ruby]) "GET /api/magic/game/new HTTP/1.1"
    in let kept = text-contains (refused.resp) "\"error\"" & loyalty-pending ((refused.store).game)
    in let fresh = handle-game-new base "GET /api/magic/game/new HTTP/1.1"
    in let cancelled = handle-game-cancel base
    in kept & loyalty-pending ((fresh.store).game) == False & (((fresh.store).game).loyalty-state).seen-a == 0
    & (cancelled.store).game-active == False & loyalty-pending ((cancelled.store).game) == False & (((cancelled.store).game).loyalty-state).seen-a == 0

  api-ar-store : MagicStore
  api-ar-store = store-turn (__record-set api-store-automatic "orders" [ar-orders, dw-reply-orders]) (ar-pending (pa-cost 1 True False 0) (DealDamage 3) [6, 8]) True

  api-ar-once : Boolean
  api-ar-once =
    let base = api-ar-store
    in let window = ((base.game).disruption).window
    in let preview = dispatch-request base ("GET /api/magic/game/response-ability-targets?nonce=" & show (window.nonce) & "&permanent=30&index=0 HTTP/1.1")
    in let available = text-contains ((handle-game-state base).resp) "\"kind\":\"ability\"" & text-contains (preview.resp) "\"recommended\":-1"
    in let done = handle-game-response-ability-at (preview.store) (window.nonce) 30 0 (0 - 1) (window.deadline - 1)
    in let again = handle-game-disruption-at (done.store) (window.nonce) (ag-hand-card (base.game) 1 6) (window.deadline - 1)
    in available & text-contains (done.resp) "\"response-kind\":\"ability\"" & text-contains (done.resp) "\"reply-source\":30"
    & text-contains (again.resp) "no longer pending" & ((get-player ((done.store).game) 1).prismatic).rays-used == 1

  api-ar-expired : Boolean
  api-ar-expired =
    let base = api-ar-store
    in let window = ((base.game).disruption).window
    in let result = handle-game-response-ability-at base (window.nonce) 30 0 (0 - 1) (window.deadline)
    in text-contains (result.resp) "\"response-kind\":\"pass\"" & ((get-player ((result.store).game) 1).prismatic).rays-used == 0
    & (bg-perm ((result.store).game) 30).tapped == False & ((get-player ((result.store).game) 0).general).life == 30

  api-ar-refuse : Boolean
  api-ar-refuse =
    let base = api-ar-store
    in let window = ((base.game).disruption).window
    in let wrong = handle-game-response-ability-at base (window.nonce) 999 0 (0 - 1) (window.deadline - 1)
    in let stale = handle-game-response-ability-at base (window.nonce + 1) 30 0 (0 - 1) (window.deadline - 1)
    in let paused = handle-game-pause-at base (window.deadline - 500)
    in let blocked = handle-game-response-ability-at (paused.store) (window.nonce) 30 0 (0 - 1) (window.deadline - 1)
    in text-contains (wrong.resp) "no longer payable" & text-contains (stale.resp) "no longer pending"
    & text-contains (blocked.resp) "game is paused" & ((get-player ((blocked.store).game) 1).prismatic).rays-used == 0

  api-ar-zero : Boolean
  api-ar-zero =
    let base = store-turn (__record-set api-store-automatic "orders" [ar-orders, dw-reply-orders]) (ar-pending (pa-cost 0 False False 30) (GainLife 99) []) True
    in let window = ((base.game).disruption).window
    in let done = handle-game-response-ability-at base (window.nonce) 30 0 (0 - 3) (window.deadline - 1)
    in (done.store).game-active == False & ((done.store).game).game-over & ((done.store).game).winner == 0
    & text-contains (done.resp) "\"abandoned\":true"

  api-ar-route : Boolean
  api-ar-route =
    let base = ar-game (pa-cost 1 True False 0) (DealDamage 3) []
    in let gs = __record-set base "battlefield" (base.battlefield & [ag-perm 40 0 0])
    in let reply = __record-set dw-reply-orders "primary-targets" [PriorityPermanent 40]
    in let pending = advance-supervised (cm-turn gs) ar-orders reply False
    in let store = store-turn (__record-set api-store-automatic "orders" [ar-orders, reply]) pending True
    in let nonce = (((store.game).disruption).window).nonce
    in let done = dispatch-request store ("GET /api/magic/game/response-ability?nonce=" & show nonce & "&permanent=30&index=0 HTTP/1.1")
    in text-contains (done.resp) "\"response-kind\":\"ability\""
    & list-contains (((done.store).game).battlefield) (\p -> p.id == 40) == False
    & ((get-player ((done.store).game) 0).general).life == 30

  api-ov-clear : Boolean
  api-ov-clear =
    let base = api-commitment-store
    in let pending = (base.turn).commitment
    in let result = handle-game-override-at base (pending.nonce) (pending.deadline - 1)
    in let polled = handle-game-state (result.store)
    in let resumed = handle-game-step (polled.store) True
    in text-contains (polled.resp) "\"manual-override\":true" & text-contains (polled.resp) "\"ready\":true"
    & ((get-player ((polled.store).game) 0).prismatic).rays-used == 0
    & (((resumed.store).turn).manual-override == False) & ((resumed.store).turn).stage == ai-stage-attackers
    & (list-at ((resumed.store).orders) 0).spell-policy == (list-at (base.orders) 0).spell-policy

  api-ov-refused : Boolean
  api-ov-refused =
    let base = api-commitment-store
    in let pending = (base.turn).commitment
    in let stale = handle-game-override-at base (pending.nonce + 1) (pending.deadline - 1)
    in let paused = handle-game-pause-at base (pending.deadline - 500)
    in let blocked = handle-game-override-at (paused.store) (pending.nonce) (pending.deadline - 1)
    in let paid = dispatch-request api-cr-store "GET /api/magic/game/override?nonce=1 HTTP/1.1"
    in text-contains (stale.resp) "no longer pending" & text-contains (blocked.resp) "game is paused"
    & text-contains (paid.resp) "cannot override a paid response window" & disruption-pending ((paid.store).game)

  api-ov-pause : Boolean
  api-ov-pause =
    let base = api-commitment-store
    in let pending = (base.turn).commitment
    in let changed = handle-game-override-at base (pending.nonce) (pending.deadline - 1)
    in let paused = handle-game-pause-at (changed.store) 100
    in let resumed = handle-game-resume-at (paused.store) 999
    in let cancelled = handle-game-cancel (resumed.store)
    in ((resumed.store).turn).manual-override & ((resumed.store).turn).paused
    & ((cancelled.store).turn).manual-override == False

  api-ov-expired : Boolean
  api-ov-expired =
    let base = api-commitment-store
    in let pending = (base.turn).commitment
    in let late = handle-game-override-at base (pending.nonce) (pending.deadline)
    in ((late.store).turn).manual-override == False & commitment-pending (((late.store).turn).commitment) == False
    & ((get-player ((late.store).game) 0).prismatic).rays-used == 0

  api-ov-new : Boolean
  api-ov-new =
    let base = api-commitment-store
    in let pending = (base.turn).commitment
    in let changed = handle-game-override-at base (pending.nonce) (pending.deadline - 1)
    in let fresh = handle-game-new (changed.store) "GET /api/magic/game/new HTTP/1.1"
    in ((fresh.store).turn).manual-override == False & (fresh.store).game-active

  api-ov-edits : Boolean
  api-ov-edits =
    let base = api-commitment-store
    in let pending = (base.turn).commitment
    in let changed = handle-game-override-at base (pending.nonce) (pending.deadline - 1)
    in let edited = dispatch-request (changed.store) "GET /api/magic/game/skill?player=0&level=3 HTTP/1.1"
    in let refused = handle-game-new (__record-set (edited.store) "templates" [pool-ruby]) "GET /api/magic/game/new HTTP/1.1"
    in let polled = handle-game-state (refused.store)
    in text-contains (refused.resp) "\"error\"" & text-contains (polled.resp) "\"manual-override\":true"
    & ((polled.store).turn).paused & ((polled.store).turn).stage == ai-stage-main-1
    & (list-at ((polled.store).orders) 0).skill == 3 & ((get-player ((polled.store).game) 0).prismatic).rays-used == 0

  api-store-automatic : MagicStore
  api-store-automatic = __record-set magic-store-new "orders" [posture-unattended, posture-unattended]

  api-sk-orders : Boolean
  api-sk-orders =
    let base = magic-store-new
    in let changed = dispatch-request base "GET /api/magic/game/skill?player=1&level=3 HTTP/1.1"
    in let invalid = dispatch-request (changed.store) "GET /api/magic/game/skill?player=1&level=9 HTTP/1.1"
    in let stance = handle-game-posture (invalid.store) "GET /api/magic/game/posture?player=1&stance=2&freeze=1 HTTP/1.1"
    in let fresh = handle-game-new (stance.store) "GET /api/magic/game/new HTTP/1.1"
    in (list-at (base.orders) 1).skill == 2 & (list-at ((fresh.store).orders) 1).skill == 3
    & (list-at ((fresh.store).orders) 0).skill == 2 & text-contains (invalid.resp) "invalid skill level"
    & text-contains (fresh.resp) "\"skill\":3"

  api-sk-pending : Boolean
  api-sk-pending =
    let base = api-commitment-store
    in let d = (base.turn).commitment
    in let changed = dispatch-request base "GET /api/magic/game/skill?player=0&level=0 HTTP/1.1"
    in (list-at ((changed.store).orders) 0).skill == 0 & (((changed.store).turn).commitment).nonce == d.nonce
    & (((changed.store).turn).commitment).deadline == d.deadline & (((changed.store).turn).commitment).card == d.card

  api-cr-store : MagicStore
  api-cr-store = store-turn (__record-set api-store-automatic "orders" [dw-orders, dw-reply-orders]) cr-pending True

  api-cr-once : Boolean
  api-cr-once =
    let base = api-cr-store
    in let window = ((base.game).disruption).window
    in let card = ag-hand-card (base.game) 1 8
    in let counter = ag-hand-card (base.game) 1 6
    in let preview = dispatch-request base ("GET /api/magic/game/response-targets?nonce=" & show (window.nonce) & "&card=" & show card & " HTTP/1.1")
    in let untouched = ((get-player ((preview.store).game) 1).prismatic).rays-used == 0 & text-contains (preview.resp) "\"recommended\":-1"
    in let answered = handle-game-cantrip-at (preview.store) (window.nonce) card (0 - 1) (window.deadline - 1)
    in let repeated = handle-game-disruption-at (answered.store) (window.nonce) counter (window.deadline - 1)
    in untouched & text-contains (answered.resp) "\"response-kind\":\"cantrip\"" & text-contains (repeated.resp) "no longer pending"
    & ((get-player ((repeated.store).game) 1).prismatic).rays-used == 1 & int-list-has ((get-player ((repeated.store).game) 1).hand) counter

  api-cr-fizzle : Boolean
  api-cr-fizzle =
    let base = api-cr-store
    in let window = ((base.game).disruption).window
    in let answered = handle-game-cantrip-at base (window.nonce) (ag-hand-card (base.game) 1 8) 999999 (window.deadline - 1)
    in text-contains (answered.resp) "\"reply-fizzled\":true" & text-contains (answered.resp) "\"fizzled\":false"
    & ((get-player ((answered.store).game) 1).prismatic).rays-used == 1 & ((get-player ((answered.store).game) 0).general).life == 30

  api-cr-expired : Boolean
  api-cr-expired =
    let base = api-cr-store
    in let window = ((base.game).disruption).window
    in let card = ag-hand-card (base.game) 1 8
    in let answered = handle-game-cantrip-at base (window.nonce) card (0 - 1) (window.deadline)
    in int-list-has ((get-player ((answered.store).game) 1).hand) card & ((get-player ((answered.store).game) 1).prismatic).rays-used == 0
    & ((((answered.store).game).disruption).result).reply < 0 & disruption-pending ((answered.store).game) == False

  api-cr-paused : Boolean
  api-cr-paused =
    let base = api-cr-store
    in let window = ((base.game).disruption).window
    in let card = ag-hand-card (base.game) 1 8
    in let paused = handle-game-pause-at base (window.deadline - 2000)
    in let blocked = handle-game-cantrip-at (paused.store) (window.nonce) card (0 - 1) (window.deadline + 90000)
    in blocked.resp == "{\"error\":\"game is paused\"}" & disruption-pending ((blocked.store).game)
    & ((get-player ((blocked.store).game) 1).prismatic).rays-used == 0

  api-cr-win : Boolean
  api-cr-win =
    let base = store-turn (__record-set api-store-automatic "orders" [dw-orders, dw-reply-orders]) cr-win-turn True
    in let window = ((base.game).disruption).window
    in let card = ag-hand-card (base.game) 1 8
    in let answered = handle-game-cantrip-at base (window.nonce) card (0 - 1) (window.deadline - 1)
    in let reward = ledger-balance ((answered.store).ledger) 0
    in let repeated = handle-game-cantrip-at (answered.store) (window.nonce) card (0 - 1) (window.deadline - 1)
    in ((answered.store).game).game-over & ((answered.store).game).winner == 1 & text-contains (answered.resp) "\"abandoned\":true"
    & ((get-player ((answered.store).game) 1).general).life == 30 & ledger-balance ((repeated.store).ledger) 0 == reward

  api-qf-store : MagicStore
  api-qf-store = store-turn api-store-automatic (qf-turn (ag-game [] [] [] [0, 0] 2) ai-stage-main-1 True) True

  api-qf-order : MagicStore, Integer -> MagicStep
  api-qf-order (store) (card) = dispatch-request store ("GET /api/magic/game/queue-card?choice=queue&player=0&card=" & show card & " HTTP/1.1")

  api-qf-retain : Boolean
  api-qf-retain =
    let base = api-qf-store
    in let card = list-at ((get-player (base.game) 0).hand) 0
    in let ordered = api-qf-order base card
    in let bad = api-qf-order (ordered.store) 999999
    in let paused = handle-game-pause-at (bad.store) 100
    in let reload = dispatch-request (paused.store) "GET /api/magic/game/state HTTP/1.1"
    in text-contains (bad.resp) "card cannot be queued" & text-contains (reload.resp) "Waiting for a later own main phase"
    & (list-at (((reload.store).game).queued-cards) 0).card == card & ((get-player ((reload.store).game) 0).prismatic).rays-used == 0
    & (reload.store).pause-active

  api-qf-cancel : Boolean
  api-qf-cancel =
    let base = api-qf-store
    in let ordered = api-qf-order base (list-at ((get-player (base.game) 0).hand) 0)
    in let cleared = dispatch-request (ordered.store) "GET /api/magic/game/queue-card?choice=cancel&player=0 HTTP/1.1"
    in (list-at (((cleared.store).game).queued-cards) 0).card < 0 & list-length ((get-player ((cleared.store).game) 0).hand) == 2

  api-qf-reset : Boolean
  api-qf-reset =
    let base = api-qf-store
    in let both = ag-give-hand (base.game) 1 [0]
    in let first = qf-order both (list-at ((get-player both 0).hand) 0) (0 - 3)
    in let second = queue-card-order first 1 (list-at ((get-player first 1).hand) 0) (0 - 3) ai-stage-main-1 "Opponent card"
    in let queued = store-queued-game base second
    in let fresh = dispatch-request queued "GET /api/magic/game/new HTTP/1.1"
    in let cancelled = dispatch-request queued "GET /api/magic/game/cancel HTTP/1.1"
    in (fresh.store).game-active & (cancelled.store).game-active == False
    & (list-at (((fresh.store).game).queued-cards) 0).card < 0 & (list-at (((fresh.store).game).queued-cards) 1).card < 0
    & (list-at (((cancelled.store).game).queued-cards) 0).card < 0 & (list-at (((cancelled.store).game).queued-cards) 1).card < 0

  api-qf-manual : Boolean
  api-qf-manual =
    let base = api-qf-store
    in let card = list-at ((get-player (base.game) 0).hand) 0
    in let ordered = api-qf-order base card
    in let policy = handle-game-spells (ordered.store) "GET /api/magic/game/spells?player=0&policy=3&reserve=0 HTTP/1.1"
    in let played = dispatch-request (policy.store) ("GET /api/magic/game/play?card=" & show card & " HTTP/1.1")
    in (list-at (((played.store).game).queued-cards) 0).card < 0 & list-length (((played.store).game).battlefield) == 1

  api-bs-manual : Boolean
  api-bs-manual =
    let stopped = bs-stop
    in let base = store-turn api-store-automatic stopped True
    in let shown = dispatch-request base "GET /api/magic/game/state HTTP/1.1"
    in let card = list-at ((get-player (base.game) 0).hand) 0
    in let ready = text-contains (shown.resp) "Automatic action budget stopped at 500" & text-contains (shown.resp) "\"ready\":true"
    in let played = dispatch-request (shown.store) ("GET /api/magic/game/play?card=" & show card & " HTTP/1.1")
    in let manual = list-length ((get-player ((played.store).game) 0).hand) == 0 & ((played.store).turn).paused & game-budget-stopped ((played.store).game)
    in let continued = handle-game-step (played.store) True
    in ready & manual & (continued.store).pause-active == False & game-budget-stopped ((continued.store).game) == False
    & ((get-player ((continued.store).game) 0).general).life == 32

  api-bs-reload : Boolean
  api-bs-reload =
    let base = store-turn api-store-automatic bs-stop True
    in let repeated = handle-game-step base False
    in let changed = dispatch-request (repeated.store) "GET /api/magic/game/posture?player=0&stance=2&freeze=0 HTTP/1.1"
    in let reload = dispatch-request (changed.store) "GET /api/magic/game/state HTTP/1.1"
    in game-budget-stopped ((reload.store).game) & ((reload.store).turn).paused
    & ((get-player ((reload.store).game) 0).general).life == 31
    & text-contains (reload.resp) "last action: Kind word" & ((reload.store).game).game-over == False

  api-hl-store : MagicStore
  api-hl-store =
    let orders = __record-set posture-unattended "spell-policy" ManualSpells
    in store-turn (__record-set api-store-automatic "orders" [orders, posture-unattended]) (hl-turn hl-game) True

  api-hl-manual : Boolean
  api-hl-manual =
    let base = api-hl-store
    in let hand = (get-player (base.game) 0).hand
    in let card = list-at hand 1
    in let shown = dispatch-request base "GET /api/magic/game/state HTTP/1.1"
    in let chosen = dispatch-request (shown.store) ("GET /api/magic/game/discard?card=" & show card & " HTTP/1.1")
    in let repeat = dispatch-request (chosen.store) ("GET /api/magic/game/discard?card=" & show card & " HTTP/1.1")
    in let refused = text-contains (repeat.resp) "cannot be discarded"
    in let still = handle-game-step (repeat.store) True
    in let waiting = ((still.store).turn).stage == ai-stage-end & text-contains (still.resp) "\"count\":1"
    in let second = dispatch-request (still.store) ("GET /api/magic/game/discard?card=" & show (list-at hand 0) & " HTTP/1.1")
    in let done = handle-game-step (second.store) True
    in text-contains (shown.resp) "\"cleanup\":{\"count\":2,\"manual\":true" & refused & waiting
    & ((done.store).game).turn-number == 2 & list-length ((get-player ((done.store).game) 0).hand) == 7

  api-hl-pause : Boolean
  api-hl-pause =
    let base = api-hl-store
    in let card = list-at ((get-player (base.game) 0).hand) 0
    in let paused = handle-game-pause-at base 100
    in let blocked = dispatch-request (paused.store) ("GET /api/magic/game/discard?card=" & show card & " HTTP/1.1")
    in let resumed = handle-game-resume-at (blocked.store) 200
    in let bad = dispatch-request (resumed.store) "GET /api/magic/game/discard?card=999999 HTTP/1.1"
    in blocked.resp == "{\"error\":\"game is paused\"}" & text-contains (bad.resp) "cannot be discarded"
    & list-length ((get-player ((bad.store).game) 0).hand) == 9 & ((bad.store).turn).paused

  api-hl-auto : Boolean
  api-hl-auto =
    let base = store-turn api-store-automatic (hl-turn hl-game) True
    in let shown = handle-game-state base
    in let done = handle-game-step (shown.store) True
    in text-contains (shown.resp) "\"manual\":false" & list-length ((get-player ((done.store).game) 0).hand) == 7
    & list-length ((get-player ((done.store).game) 0).graveyard) == 2 & ((done.store).game).turn-number == 2

  api-pn-commitment : Boolean
  api-pn-commitment =
    let base = api-commitment-store
    in let d = (base.turn).commitment
    in let paused = handle-game-pause-at base (d.deadline - 7000)
    in let repeat = handle-game-pause-at (paused.store) (d.deadline + 90000)
    in let reload = dispatch-request (repeat.store) "GET /api/magic/game/state HTTP/1.1"
    in let blocked = handle-game-commitment-at (reload.store) (d.nonce) True (d.deadline + 90000)
    in let resumed = handle-game-resume-at (blocked.store) (d.deadline + 90000)
    in (paused.store).pause-active & (repeat.store).pause-remaining == 7000
    & text-contains (reload.resp) "\"pause-active\":true" & text-contains (reload.resp) "\"remaining-ms\":7000"
    & blocked.resp == "{\"error\":\"game is paused\"}"
    & (resumed.store).pause-active == False & (((resumed.store).turn).commitment).nonce == d.nonce
    & (((resumed.store).turn).commitment).deadline == d.deadline + 97000
    & (base.turn).commitment.deadline == d.deadline & base.pause-active == False
    & ((get-player ((resumed.store).game) 0).prismatic).rays-used == 0

  api-pn-combat : Boolean
  api-pn-combat =
    let base = api-combat-store
    in let d = (base.turn).combat-choice
    in let paused = handle-game-pause-at base (d.deadline - 3000)
    in let blocked = handle-game-combat-choice-at (paused.store) (d.nonce) True (d.deadline + 90000)
    in let resumed = handle-game-resume-at (blocked.store) (d.deadline + 90000)
    in let early = handle-game-combat-choice-at (resumed.store) (d.nonce) True (d.deadline + 92000)
    in let intact = (((early.store).turn).combat-choice).pending & (bg-perm ((early.store).game) 10).tapped == False
    in let expired = handle-game-combat-choice-at (early.store) (d.nonce) True (d.deadline + 93000)
    in intact & blocked.resp == "{\"error\":\"game is paused\"}" & ((expired.store).turn).stage == ai-stage-blockers
    & (bg-perm ((expired.store).game) 10).tapped & (((expired.store).turn).combat).num-attackers == 2

  api-pn-disruption : Boolean
  api-pn-disruption =
    let base = api-dw-store
    in let d = ((base.game).disruption).window
    in let paused = handle-game-pause-at base (d.deadline - 4000)
    in let blocked = handle-game-disruption-at (paused.store) (d.nonce) (0 - 1) (d.deadline + 90000)
    in let reload = dispatch-request (blocked.store) "GET /api/magic/game/state HTTP/1.1"
    in let resumed = handle-game-resume-at (reload.store) (d.deadline + 90000)
    in let after = (((resumed.store).game).disruption).window
    in text-contains (reload.resp) "\"remaining-ms\":4000" & after.nonce == d.nonce
    & after.deadline == d.deadline + 94000 & ((get-player ((resumed.store).game) 1).general).life == 30
    & ((get-player ((resumed.store).game) 1).prismatic).rays-used == 0

  api-pn-phase : Boolean
  api-pn-phase =
    let base = api-pa-store (pa-cost 0 False False 0) (GainLife 2)
    in let paused = handle-game-pause-at base 100
    in let resumed = handle-game-resume-at (paused.store) 90000
    in let repeated = handle-game-resume-at (resumed.store) 95000
    in (base.turn).paused & ((repeated.store).turn).paused & ((repeated.store).turn).stage == (base.turn).stage
    & ((get-player ((repeated.store).game) 0).general).life == 30 & (repeated.store).pause-active == False

  api-pn-actions : Boolean
  api-pn-actions =
    let base = api-pa-store (pa-cost 0 False False 0) (GainLife 2)
    in let paused = handle-game-pause-at base 100
    in let blocked = dispatch-request (paused.store) "GET /api/magic/game/activate?permanent=10&index=0 HTTP/1.1"
    in let changed = dispatch-request (blocked.store) "GET /api/magic/game/posture?player=0&stance=2&freeze=0 HTTP/1.1"
    in let still = (changed.store).pause-active & (list-at ((changed.store).orders) 0).stance == Defensive
    in let stopped = dispatch-request (changed.store) "GET /api/magic/game/cancel HTTP/1.1"
    in blocked.resp == "{\"error\":\"game is paused\"}" & still
    & ((get-player ((changed.store).game) 0).general).life == 30
    & (stopped.store).pause-active == False & (stopped.store).game-active == False

  api-pn-step : Boolean
  api-pn-step =
    let base = (handle-game-new api-store-automatic "GET /api/magic/game/new HTTP/1.1").store
    in let paused = handle-game-pause-at base 100
    in let step = handle-game-step (paused.store) False
    in let same = (step.store).pause-active & ((step.store).game).turn-number == 1
    in let resumed = handle-game-resume-at (step.store) 90000
    in same & (resumed.store).pause-active == False & ((resumed.store).game).turn-number == 2

  api-play-template : MagicStore, Integer, Text -> MagicStep
  api-play-template (store) (template) (suffix) =
    let card = ag-hand-card (store.game) ((store.game).active-player) template
    in dispatch-request store ("GET /api/magic/game/play?card=" & show card & suffix & " HTTP/1.1")

  api-orders : Boolean
  api-orders =
    let fresh = api-store-automatic
    in let changed = handle-game-posture fresh "GET /api/magic/game/posture?player=0&stance=2&freeze=1&blocks=0 HTTP/1.1"
    in let p = list-at ((changed.store).orders) 0
    in let bad = handle-game-posture (changed.store) "GET /api/magic/game/posture?player=0&stance=oops&freeze=0&blocks=1 HTTP/1.1"
    in p.stance == Defensive & posture-freezes p ai-stage-attackers & p.block-policy == ManualBlock
    & (list-at (fresh.orders) 0).stance == Balanced
    & (list-at ((bad.store).orders) 0).stance == Defensive
    & game-query-id "99999999999999999999999" == 0 - 1

  api-generals : Boolean
  api-generals =
    let created = handle-game-new api-store-automatic "GET /api/magic/game/new?general-a=1&general-b=3 HTTP/1.1"
    in let g = (created.store).game
    in (get-general g 0).id == 1 & (get-general g 1).id == 3
    & ((get-player g 0).general).life == 30 & ((get-player g 1).general).life == 25

  api-pause : Boolean
  api-pause =
    let created = handle-game-new api-store-automatic "GET /api/magic/game/new HTTP/1.1"
    in let ordered = handle-game-posture (created.store) "GET /api/magic/game/posture?player=0&stance=1&freeze=1&blocks=1 HTTP/1.1"
    in let paused = handle-game-step (ordered.store) False
    in let n = (((paused.store).turn).game).turn-number
    in let repeated = handle-game-step (paused.store) False
    in let still = (((repeated.store).turn).game).turn-number
    in let resumed = handle-game-step (repeated.store) True
    in ((paused.store).turn).paused & n == 1 & still == 1
    & (((resumed.store).turn).game).turn-number == 2

  api-general-manual : Boolean
  api-general-manual =
    let g = ag-game [] [] [ag-perm 10 0 0] [] 0
    in let base = store-turn api-store-automatic (supervised-start g) True
    in let fixture = __record-set base "templates" ag-templates
    in let ordered = handle-game-posture fixture "GET /api/magic/game/posture?player=1&stance=2&freeze=0&blocks=0 HTTP/1.1"
    in let paused = handle-game-step (ordered.store) False
    in let invalid = handle-game-block (paused.store) "GET /api/magic/game/block?blocker=oops&attacker=10 HTTP/1.1"
    in let rejected = (((invalid.store).turn).combat).num-blockers == 0
    in let chosen = handle-game-block (invalid.store) "GET /api/magic/game/block?blocker=general&attacker=10 HTTP/1.1"
    in let after = handle-game-step (chosen.store) True
    in rejected & (((chosen.store).turn).combat).num-blockers == 1
    & ((get-player ((after.store).game) 1).general).life == 28
    & list-length (((after.store).game).battlefield) == 0

  api-target-fixture : MagicStore
  api-target-fixture =
    let g = tg-game [] [ag-perm 20 0 1, ag-perm 21 3 1, ag-perm 30 0 0] [5]
    in let store = store-turn api-store-automatic (supervised-start g) True
    in __record-set store "templates" tg-templates

  api-target-orders : Boolean
  api-target-orders =
    let base = api-target-fixture
    in let a = handle-game-target base "GET /api/magic/game/target?player=0&kind=permanent&id=20 HTTP/1.1"
    in let b = handle-game-target (a.store) "GET /api/magic/game/target?player=0&kind=permanent&id=20 HTTP/1.1"
    in let c = handle-game-target (b.store) "GET /api/magic/game/target?player=0&kind=general HTTP/1.1"
    in let preserved = handle-game-posture (c.store) "GET /api/magic/game/posture?player=0&stance=1&freeze=1&blocks=1 HTTP/1.1"
    in let removed = handle-game-target (preserved.store) "GET /api/magic/game/target?player=0&kind=remove&index=0 HTTP/1.1"
    in let cleared = handle-game-target (removed.store) "GET /api/magic/game/target?player=0&kind=clear HTTP/1.1"
    in list-length ((list-at (base.orders) 0).primary-targets) == 0
    & list-length ((list-at ((a.store).orders) 0).primary-targets) == 1
    & list-length ((list-at ((b.store).orders) 0).primary-targets) == 1
    & list-length ((list-at ((preserved.store).orders) 0).primary-targets) == 2
    & same-target-order (list-at ((list-at ((removed.store).orders) 0).primary-targets) 0) PriorityGeneral
    & list-length ((list-at ((cleared.store).orders) 0).primary-targets) == 0

  api-target-refusals : Boolean
  api-target-refusals =
    let base = api-target-fixture
    in let own = handle-game-target base "GET /api/magic/game/target?player=0&kind=permanent&id=30 HTTP/1.1"
    in let absent = handle-game-target (own.store) "GET /api/magic/game/target?player=0&kind=permanent&id=999 HTTP/1.1"
    in let bad = handle-game-target (absent.store) "GET /api/magic/game/target?player=0&kind=threat&level=3 HTTP/1.1"
    in let unknown = handle-game-target (bad.store) "GET /api/magic/game/target?player=0&kind=type&type=Invented HTTP/1.1"
    in let valid = handle-game-target (unknown.store) "GET /api/magic/game/target?player=0&kind=type&type=Creature HTTP/1.1"
    in let threat = handle-game-target (valid.store) "GET /api/magic/game/target?player=0&kind=threat&level=1 HTTP/1.1"
    in list-length ((list-at ((unknown.store).orders) 0).primary-targets) == 0
    & list-length ((list-at ((threat.store).orders) 0).primary-targets) == 2

  api-target-execution : Boolean
  api-target-execution =
    let selected = dispatch-request api-target-fixture "GET /api/magic/game/target?player=0&kind=permanent&id=20 HTTP/1.1"
    in let victim = (bg-perm ((selected.store).game) 20).card-id
    in let spell = ag-hand-card ((selected.store).game) 0 5
    in let after = dispatch-request (selected.store) "GET /api/magic/game/step HTTP/1.1"
    in let g = (after.store).game
    in tg-has g 20 == False & tg-has g 21
    & list-at ((get-player g 1).graveyard) 0 == victim
    & list-at ((get-player g 0).graveyard) 0 == spell
    & list-length ((get-player g 0).hand) == 0

  api-phase-boundaries : Boolean
  api-phase-boundaries =
    let created = handle-game-new api-store-automatic "GET /api/magic/game/new HTTP/1.1"
    in let a = dispatch-request (created.store) "GET /api/magic/game/freeze?player=0&phase=0&enabled=1 HTTP/1.1"
    in let b = dispatch-request (a.store) "GET /api/magic/game/freeze?player=0&phase=1&enabled=1 HTTP/1.1"
    in let paused = handle-game-step (b.store) False
    in let rays = ((get-player ((paused.store).game) 0).prismatic).rays-total
    in let held = handle-game-step (paused.store) False
    in let unchanged = ((held.store).turn).stage == ai-stage-upkeep & ((get-player ((held.store).game) 0).prismatic).rays-total == rays
    in let resumed = handle-game-step (held.store) True
    in rays == 0 & unchanged & ((paused.store).turn).paused
    & ((resumed.store).turn).stage == ai-stage-draw & ((resumed.store).turn).paused
    & ((get-player ((resumed.store).game) 0).prismatic).rays-total == 1

  api-phase-settings : Boolean
  api-phase-settings =
    let initial = api-store-automatic
    in let bad = dispatch-request initial "GET /api/magic/game/freeze?player=0&phase=8&enabled=1 HTTP/1.1"
    in let a = dispatch-request (bad.store) "GET /api/magic/game/freeze?player=0&phase=1&enabled=1 HTTP/1.1"
    in let b = handle-game-posture (a.store) "GET /api/magic/game/posture?player=0&stance=2&freeze=1&blocks=1 HTTP/1.1"
    in let c = handle-game-posture (b.store) "GET /api/magic/game/posture?player=0&stance=2&freeze=0&blocks=1 HTTP/1.1"
    in list-length ((list-at ((bad.store).orders) 0).phase-freezes) == 0
    & list-length ((list-at (initial.orders) 0).phase-freezes) == 0
    & posture-freezes (list-at ((b.store).orders) 0) ai-stage-draw
    & posture-freezes (list-at ((b.store).orders) 0) ai-stage-attackers
    & posture-freezes (list-at ((c.store).orders) 0) ai-stage-draw
    & posture-freezes (list-at ((c.store).orders) 0) ai-stage-attackers == False

  api-phase-end : Boolean
  api-phase-end =
    let base = store-turn api-store-automatic (supervised-start (ph-game 0)) True
    in let fixture = __record-set base "templates" ag-templates
    in let configured = dispatch-request fixture "GET /api/magic/game/freeze?player=0&phase=7&enabled=1 HTTP/1.1"
    in let paused = handle-game-step (configured.store) False
    in let before = ((get-player ((paused.store).game) 1).general).damage-marked
    in let number = ((paused.store).game).turn-number
    in let completed = handle-game-step (paused.store) True
    in before == 4 & number == 3 & ((paused.store).turn).stage == ai-stage-end
    & ((completed.store).game).turn-number == 4 & ((completed.store).turn).stage == ai-stage-upkeep
    & ((get-player ((completed.store).game) 1).general).damage-marked == 0

  opening : [Console] Nothing = act
    ag-report "API General response cannot be undone by unpaid override" api-glw-override
    ag-report "API ordinary activation with response choice off resolves immediately" api-aw-off
    ag-report "API General activation with response choice off resolves immediately" api-glw-off
    ag-report "API General window exposes counted use and cleared queued loyalty" api-glw-committed
    ag-report "API refuses Disruption by name against General loyalty" api-glw-refuse
    ag-report "API General timeout Passes once and retains used activation" api-glw-timeout
    ag-report "API Pause preserves the General response and its activation use" api-glw-pause
    ag-report "API winning General reply abandons effect with use and queue committed" api-glw-lethal
    ag-report "API combat target and payment refusals retain the defender allowance" api-ccf-payment
    ag-report "API combat allowance survives Pause resume and order edits" api-ccf-pause-orders
    ag-report "API attackers freeze accepts only its attacker Cantrip" (api-ccf-owner ai-stage-attackers 0)
    ag-report "API blockers freeze accepts only its defender Cantrip" (api-ccf-owner ai-stage-blockers 1)
    ag-report "API damage freeze accepts only its attacker Cantrip" (api-ccf-owner ai-stage-damage 0)
    ag-report "API combat preview and play refuse wrong or missing player by name" api-ccf-wrong
    ag-report "API combat Cantrip allowance remains consumed after reload" api-ccf-reload
    ag-report "API activation advertises a paid ability response window" api-aw-paid
    ag-report "API activation timeout Passes once without response costs" api-aw-timeout
    ag-report "API refuses Disruption by name for an activation window" api-aw-refuse
    ag-report "API Pause and reload suspend activation response deadline" api-aw-pause
    ag-report "API game-ending reply reports abandoned activation" api-aw-lethal
    ag-report "API queues General below threshold with a locked target and visible reason" api-ql-queue
    ag-report "API Pause permits General order edits and reload while blocking activation" api-ql-pause
    ag-report "API invalid General queue replacement preserves the existing order" api-ql-refuse
    ag-report "API New Game and cancel clear General queue while refusal retains it" api-ql-reset
    ag-report "API renders live scaled creature stats while General stats and polls stay unchanged" api-ls-stats
    ag-report "API token loses aura when the contributing army leaves" api-ls-loss
    ag-report "API activation and response settle sacrifice aura loss before effects" api-ls-cost
    ag-report "API manual card crossing settles LoyaltyGain once across polls" api-lg-play
    ag-report "API Continue resumes a LoyaltyGain chain after the named budget stop" api-lg-budget
    ag-report "API Pause holds pending LoyaltyGain until resume" api-lg-pause
    ag-report "API New Game clears LoyaltyGain state while refusal preserves it" api-lg-reset
    ag-report "API ability response preview and result consume the same nonce" api-ar-once
    ag-report "API ability response timeout Passes without costs" api-ar-expired
    ag-report "API ability response rejects wrong stale and paused requests" api-ar-refuse
    ag-report "API life-to-zero ability response finalizes before pending spell" api-ar-zero
    ag-report "API ability submission route uses omitted-target responder orders" api-ar-route
    ag-report "API override publishes manual controls and preserves policy on Continue" api-ov-clear
    ag-report "API override refuses stale paused and paid response attempts" api-ov-refused
    ag-report "API override survives Pause and reload and clears at cancel" api-ov-pause
    ag-report "API expired override follows the existing timeout policy" api-ov-expired
    ag-report "API New Game clears manual override" api-ov-new
    ag-report "API order edits and refused New Game preserve override" api-ov-edits
    ag-report "API skill defaults to Expert validates levels and survives other edits and New Game" api-sk-orders
    ag-report "API skill change preserves the pending choice nonce and deadline" api-sk-pending
    ag-report "API Cantrip target preview is read-only and one response consumes the nonce" api-cr-once
    ag-report "API illegal resolution target spends the Cantrip and reports its fizzle" api-cr-fizzle
    ag-report "API late Cantrip response becomes Pass without payment" api-cr-expired
    ag-report "API Pause now blocks the Cantrip response and its deadline" api-cr-paused
    ag-report "API winning Cantrip response abandons the spell and finalizes once" api-cr-win
    ag-report "API queued card survives reload Pause and refused replacement" api-qf-retain
    ag-report "API player can cancel a queued card without spending" api-qf-cancel
    ag-report "API new match and cancellation clear both queued cards" api-qf-reset
    ag-report "API manual play clears the queued copy when it leaves hand" api-qf-manual
    ag-report "API budget stop permits manual play and Continue refreshes work" api-bs-manual
    ag-report "API budget reason survives reload and order edits without advancing" api-bs-reload
    ag-report "API manual cleanup chooses copies and refuses duplicate or early Continue" api-hl-manual
    ag-report "API cleanup rejects foreign cards and respects Pause now" api-hl-pause
    ag-report "API automatic cleanup proceeds with exactly seven cards" api-hl-auto
    ag-report "API Pause suspends commitment across reload and repeated Pause" api-pn-commitment
    ag-report "API Pause resumes combat with saved time and plan" api-pn-combat
    ag-report "API Pause suspends disruption without paying or resolving" api-pn-disruption
    ag-report "API Continue restores an existing phase pause" api-pn-phase
    ag-report "API Pause blocks gameplay while allowing orders and cancellation" api-pn-actions
    ag-report "API Continue from an unpaused boundary advances normally" api-pn-step
    ag-report "API queue preview works outside the owners turn without spending" api-qa-preview
    ag-report "API queued order retains waiting reason across reload" api-qa-retain
    ag-report "API player can cancel a retained queued order" api-qa-cancel
    ag-report "API invalid replacement preserves the existing queued order" api-qa-refuse
    ag-report "API queued order binds the explicitly selected target" api-qa-target
    ag-report "API new match and cancellation clear both queued orders" api-qa-reset
    ag-report "API queued life refusal exposes the last-life reason" api-qa-life
    ag-report "API queued source and target labels preserve escaped text" api-qa-json
    ag-report "API disruption window survives reload and repeated Continue" api-dw-reload
    ag-report "API disruption accepts one reply and rejects stale nonce" api-dw-once
    ag-report "API disruption timeout and unavailable clock always Pass" api-dw-timeout
    ag-report "API disruption rejects foreign card without payment" api-dw-reject
    ag-report "API disruption policy edit preserves decision" api-dw-policy
    ag-report "API cancellation takes precedence over expired disruption" api-dw-cancel
    ag-report "API contest seed resets only after successful game creation" api-dw-seed
    ag-report "API response payment is rechecked after kept-stone edit" api-dw-payment
    ag-report "API early disruption timeout preserves the window" api-dw-early
    ag-report "API ability decision reload preserves nonce and costs" api-aa-pending
    ag-report "API missing ability source remains visible for Hold" api-aa-missing
    ag-report "API ability deadline and missing clock always Hold" api-aa-timeout
    ag-report "API ability choice pays exactly once" api-aa-accept
    ag-report "API sacrifice toggle preserves pending choice" api-aa-policy
    ag-report "API cancellation discards pending ability" api-aa-cancel
    ag-report "API new match drops only permanent target orders" api-match-targets-new
    ag-report "API cancellation clears both players permanent targets" api-match-targets-cancel
    ag-report "API refused new match retains permanent target orders" api-match-targets-refuse
    ag-report "API cancellation wins over expired spell choice" api-cancel-spell
    ag-report "API cancellation wins over expired combat choice" api-cancel-combat
    ag-report "API cancellation is idempotent and preserves posture" api-cancel-orders
    ag-report "API cancelled finished game reloads as setup" api-cancel-finished
    ag-report "API permanent ability preview is read only and costed" api-pa-preview
    ag-report "API permanent activation validates target before costs" api-pa-target
    ag-report "API tapped and foreign permanent activations are refused" api-pa-refuse
    ag-report "API permanent ray costs ignore gemstone drafting mode" api-pa-payment
    ag-report "API life payment to zero finalizes the loss once" api-pa-zero
    ag-report "API activation source is consumed by sacrifice" api-pa-sacrifice
    ag-report "API combat policy defaults off and survives posture edits" api-combat-policy
    ag-report "API combat decision preserves nonce deadline and plan" api-combat-pending
    ag-report "API untouched combat timeout executes the shown AI plan" api-combat-timeout
    ag-report "API attacker edit cancels timeout until Continue" api-combat-edit
    ag-report "API combat acceptance spends its nonce once" api-combat-once
    ag-report "API new game cancels combat deadline" api-combat-new
    ag-report "API late attacker edit cannot replace expired AI plan" api-combat-late-edit
    ag-report "API target-pause policy defaults off and survives edits" api-target-policy
    ag-report "API target prompt preserves candidates nonce and deadline" api-target-pending
    ag-report "API target choice spends once at the selected target" api-target-choice
    ag-report "API target timeout uses the shown recommendation" api-target-timeout
    ag-report "API target timeout Holds an all-rays spend" api-target-timeout-all
    ag-report "API changed reserves and card hold prevent timeout casting" api-target-timeout-orders
    ag-report "API stale recommendation times out to Hold" api-target-timeout-stale
    ag-report "API manual target preview is read only" api-target-preview
    ag-report "API manual target overrides preference for one cast" api-target-play
    ag-report "API explicit General target uses the opponent" api-target-general
    ag-report "API stale and malformed targets spend nothing" api-target-refuse
    ag-report "API manual chains retain the explicit target" api-target-payment
    ag-report "API attacker plan survives reload and empty override" api-attack-empty
    ag-report "API attacker edits are idempotent and active-player owned" api-attack-select
    ag-report "API illegal attacker requests preserve the plan" api-attack-refuse
    ag-report "API flags distinguish duplicate card instances" api-card-flags
    ag-report "API invalid and hidden card flags are refused" api-card-flags-refuse
    ag-report "API manual play overrides holds and consumes Next" api-card-flags-manual
    ag-report "API recast flags belong to the original owner" api-card-flags-board
    ag-report "API new game clears instance flags" api-card-flags-new
    ag-report "API flag edit preserves commitment deadline" api-card-flags-commitment
    ag-report "API selects one of two same-template instances" api-instance-play
    ag-report "API accepts instance IDs beyond the template count" api-instance-gem
    ag-report "API commitment default and policy preservation" api-commitment-policy
    ag-report "API commitment nonce prevents repeated Cast" api-commitment-cast
    ag-report "API deadline resolves late Cast to Hold" api-commitment-timeout
    ag-report "API pending choice rejects stale payment" api-commitment-revalidate
    ag-report "API new game cancels an expired commitment" api-commitment-new
    ag-report "API entry Fluorescence survives cleanup" api-entry
    ag-report "API friendly boost survives pause and expires" api-grant
    ag-report "API posture and invalid input" api-orders
    ag-report "API selected Generals" api-generals
    ag-report "API pause and resume" api-pause
    ag-report "API manual General block" api-general-manual
    ag-report "API target order persistence" api-target-orders
    ag-report "API invalid target refusals" api-target-refusals
    ag-report "API target reaches spell execution" api-target-execution
    ag-report "API phase boundaries" api-phase-boundaries
    ag-report "API phase settings and legacy toggle" api-phase-settings
    ag-report "API end freeze precedes cleanup" api-phase-end
    ag-report "API spell policy preservation" api-spell-preserve
    ag-report "API spell policy rejects invalid input" api-spell-reject
    ag-report "API spell policy changes casting" api-spell-cast
    ag-report "API manual card decisions" api-manual-play
    ag-report "API manual play refusal and victory" api-manual-end
    ag-report "API gem payment rejects reuse" api-payment
    ag-report "API Diamond quote and payment" api-diamond
    ag-report "API mana order persistence and refusal" api-kept
    ag-report "API mana order reaches automatic casting" api-kept-cast
    ag-report "API manual payment is atomic" api-manual-payment
    ag-report "API payment mode preserves other orders" api-payment-mode
    ag-report "API draw spell succeeds with unchanged hand size" api-payment-draw
    ag-report "API Fluorescence spends once and expires" api-fluorescence
    ag-report "API active Fluorescence requires a legal target" api-fluorescence-target
    ag-report "API loyalty activation and second-use refusal" api-loyalty-once
    ag-report "API loyalty lethal finalizes once" api-loyalty-lethal
    ag-report "API block policy persists through stance edits" api-block-orders
    ag-report "API invalid block policy is refused" api-block-refusal
    ag-report "API block policies reach combat" api-block-combat
    ag-report "API starter exhaustion returns named error without cards" api-starter-error
    ag-report "API refused new game preserves spell decision and state" api-starter-spell-refusal
    ag-report "API refused second deck preserves combat decision and state" api-starter-combat-refusal
    ag-report "API deck validation accepts generated and basic-heavy decks" api-dv-valid
    ag-report "API deck validation names excessive copies" api-dv-copies
    ag-report "API deck validation enforces size and color identity" api-dv-size-color
    ag-report "API deck validation refuses malformed IDs General and format" api-dv-inputs
    ag-report "API deck validation refuses tokens and Generals with escaped names" api-dv-types
    ag-report "API JSON error profile and exports preserve text" api-j-accounts
    ag-report "API JSON registration and login preserve text" api-j-auth
    ag-report "API JSON template and General names preserve text" api-j-names
  end

  api-block-orders : Boolean
  api-block-orders =
    let base = api-store-automatic
    in let a = dispatch-request base "GET /api/magic/game/blocks?player=1&policy=3&threshold=27 HTTP/1.1"
    in let b = handle-game-posture (a.store) "GET /api/magic/game/posture?player=1&stance=2&freeze=1 HTTP/1.1"
    in let kept = block-policy-index ((list-at ((b.store).orders) 1).block-policy) == 3 & block-policy-threshold ((list-at ((b.store).orders) 1).block-policy) == 27
    in let c = handle-game-posture (b.store) "GET /api/magic/game/posture?player=1&stance=2&freeze=1&blocks=0 HTTP/1.1"
    in kept & (list-at (base.orders) 1).block-policy == AutoBlock
    & (list-at ((c.store).orders) 1).block-policy == ManualBlock
    & (list-at ((c.store).orders) 0).block-policy == AutoBlock

  api-block-refusal : Boolean
  api-block-refusal =
    let a = handle-game-block-policy api-store-automatic "GET /api/magic/game/blocks?player=0&policy=2&threshold=10 HTTP/1.1"
    in let b = handle-game-block-policy (a.store) "GET /api/magic/game/blocks?player=0&policy=4&threshold=10 HTTP/1.1"
    in let c = handle-game-block-policy (b.store) "GET /api/magic/game/blocks?player=0&policy=3&threshold=0 HTTP/1.1"
    in let d = handle-game-block-policy (c.store) "GET /api/magic/game/blocks?player=0&policy=3&threshold=1000 HTTP/1.1"
    in let e = handle-game-posture (d.store) "GET /api/magic/game/posture?player=0&stance=1&freeze=0&blocks=2 HTTP/1.1"
    in (list-at ((e.store).orders) 0).block-policy == NoBlock

  api-block-game : Integer -> MagicStore
  api-block-game (unused) =
    let g = ag-game [] [] [ag-perm 10 3 0, ag-perm 20 1 1] [] 0
    in let base = store-turn api-store-automatic (supervised-start g) True
    in __record-set base "templates" ag-templates

  api-block-combat : Boolean
  api-block-combat =
    let a = handle-game-posture (api-block-game 0) "GET /api/magic/game/posture?player=0&stance=1&freeze=0&blocks=1 HTTP/1.1"
    in let b = handle-game-block-policy (a.store) "GET /api/magic/game/blocks?player=1&policy=2&threshold=10 HTTP/1.1"
    in let no = handle-game-step (b.store) False
    in let loss = ((get-player ((no.store).game) 1).general).life == 26
    in let c = handle-game-posture (api-block-game 0) "GET /api/magic/game/posture?player=0&stance=1&freeze=0&blocks=1 HTTP/1.1"
    in let d = handle-game-block-policy (c.store) "GET /api/magic/game/blocks?player=1&policy=3&threshold=27 HTTP/1.1"
    in let protected = handle-game-step (d.store) False
    in loss & ((get-player ((protected.store).game) 1).general).life == 30

  api-loyalty-once : Boolean
  api-loyalty-once =
    let gen = ly-general [LoyaltyCostAbility 1 (GainLife 4), LoyaltyCostAbility 1 (LoseLife 3)]
    in let g = ly-game gen [ag-perm 10 0 0] [] 0
    in let base = store-turn api-store-automatic (supervised-start g) True
    in let fixture = __record-set base "templates" ag-templates
    in let outside = dispatch-request fixture "GET /api/magic/game/loyalty?index=0 HTTP/1.1"
    in let unchanged = ((get-player ((outside.store).game) 0).general).loyalty-turn == (0 - 1)
    in let configured = handle-game-spells (outside.store) "GET /api/magic/game/spells?player=0&policy=3&reserve=0 HTTP/1.1"
    in let paused = handle-game-step (configured.store) False
    in let used = dispatch-request (paused.store) "GET /api/magic/game/loyalty?index=0 HTTP/1.1"
    in let again = dispatch-request (used.store) "GET /api/magic/game/loyalty?index=1 HTTP/1.1"
    in unchanged & text-starts-with (again.resp) "{\"error\""
    & ((get-player ((again.store).game) 0).general).life == 34
    & ((get-player ((again.store).game) 1).general).life == 30
    & army-loyalty ((again.store).game) 0 == 1 & ((again.store).turn).paused

  api-loyalty-lethal : Boolean
  api-loyalty-lethal =
    let gen = ly-general [LoyaltyCostAbility 1 (DealDamage 3)]
    in let g = ly-game gen [ag-perm 10 0 0] [] 0
    in let p = get-player g 1
    in let low = __record-set (p.general) "life" 3
    in let fixture = set-player g 1 (__record-set p "general" low)
    in let base = store-turn api-store-automatic (supervised-start fixture) True
    in let configured = handle-game-spells base "GET /api/magic/game/spells?player=0&policy=3&reserve=0 HTTP/1.1"
    in let paused = handle-game-step (configured.store) False
    in let used = dispatch-request (paused.store) "GET /api/magic/game/loyalty?index=0 HTTP/1.1"
    in let repeated = dispatch-request (used.store) "GET /api/magic/game/loyalty?index=0 HTTP/1.1"
    in (used.store).game-active == False & ((used.store).game).game-over
    & ((used.store).turn).stage == ai-stage-done & text-starts-with (repeated.resp) "{\"error\""

  api-fluorescence : Boolean
  api-fluorescence =
    let base = store-turn api-store-automatic (supervised-start (fl-game [fl-obsidian 50 0] [17] 1)) True
    in let wrong = dispatch-request base "GET /api/magic/game/fluorescence?stone=50 HTTP/1.1"
    in let unchanged = ((get-player ((wrong.store).game) 0).prismatic).fluorescence-active == False
    in let configured = handle-game-spells (wrong.store) "GET /api/magic/game/spells?player=0&policy=3&reserve=0 HTTP/1.1"
    in let kept = handle-game-mana (configured.store) "GET /api/magic/game/mana?player=0&stone=6&keep=1 HTTP/1.1"
    in let paused = handle-game-step (kept.store) False
    in let lit = dispatch-request (paused.store) "GET /api/magic/game/fluorescence?stone=50 HTTP/1.1"
    in let again = dispatch-request (lit.store) "GET /api/magic/game/fluorescence?stone=50 HTTP/1.1"
    in let once = ((get-player ((again.store).game) 0).prismatic).rays-used == 1 & text-starts-with (again.resp) "{\"error\""
    in let played = api-play-template (again.store) 17 ""
    in let hit = ((get-player ((played.store).game) 1).general).life == 27
    in let second = handle-game-step (played.store) True
    in let done = handle-game-step (second.store) True
    in unchanged & once & hit & ((get-player ((done.store).game) 0).prismatic).fluorescence-active == False
    & ((get-player ((done.store).game) 0).prismatic).rays-used == 2

  api-fluorescence-target : Boolean
  api-fluorescence-target =
    let base = store-turn api-store-automatic (supervised-start (fl-game [fl-obsidian 50 0] [21] 2)) True
    in let configured = handle-game-spells base "GET /api/magic/game/spells?player=0&policy=3&reserve=0 HTTP/1.1"
    in let paused = handle-game-step (configured.store) False
    in let lit = handle-game-fluorescence (paused.store) "GET /api/magic/game/fluorescence?stone=50 HTTP/1.1"
    in let refused = api-play-template (lit.store) 21 ""
    in text-starts-with (refused.resp) "{\"error\""
    & list-length ((get-player ((refused.store).game) 0).hand) == 1
    & ((get-player ((refused.store).game) 0).prismatic).rays-used == 1

  api-payment-draw : Boolean
  api-payment-draw =
    let fixture = ag-game-templates (dp-templates & [make-cantrip 16 "Cycle" light-cost-zero [StaticAbility (DrawCards 1)]]) [] [] [] [16] 0
    in let expected-draw = list-at ((get-player fixture 0).library) 0
    in let base = store-turn api-store-automatic (supervised-start fixture) True
    in let configured = handle-game-payment-mode base "GET /api/magic/game/payment-mode?player=0&manual=1 HTTP/1.1"
    in let paused = handle-game-step (configured.store) False
    in let after = api-play-template (paused.store) 16 "&chains="
    in text-starts-with (after.resp) "{\"error\"" == False
    & list-length ((get-player ((after.store).game) 0).hand) == 1
    & list-at ((get-player ((after.store).game) 0).hand) 0 == expected-draw
    & list-length ((get-player ((after.store).game) 0).library) == 2

  api-manual-payment : Boolean
  api-manual-payment =
    let g = dp-game [dp-diamond 60, dp-diamond 61] [15] 0
    in let base = store-turn api-store-automatic (supervised-start g) True
    in let configured = dispatch-request base "GET /api/magic/game/payment-mode?player=0&manual=1 HTTP/1.1"
    in let paused = handle-game-step (configured.store) False
    in let missing = api-play-template (paused.store) 15 ""
    in let duplicate = api-play-template (missing.store) 15 "&chains=60,60"
    in let malformed = api-play-template (duplicate.store) 15 "&chains=60,61;"
    in let held = list-length ((get-player ((malformed.store).game) 0).hand) == 1 & ((get-player ((malformed.store).game) 0).prismatic).rays-used == 0
    in let after = api-play-template (malformed.store) 15 "&chains=61,60"
    in held & list-length ((get-player ((after.store).game) 0).hand) == 0
    & ((get-player ((after.store).game) 0).prismatic).rays-used == 1 & ((after.store).turn).paused

  api-payment-mode : Boolean
  api-payment-mode =
    let base = api-store-automatic
    in let a = handle-game-payment-mode base "GET /api/magic/game/payment-mode?player=1&manual=1 HTTP/1.1"
    in let b = handle-game-mana (a.store) "GET /api/magic/game/mana?player=1&stone=0&keep=1 HTTP/1.1"
    in let c = handle-game-spells (b.store) "GET /api/magic/game/spells?player=1&policy=1&reserve=0 HTTP/1.1"
    in let d = handle-game-payment-mode (c.store) "GET /api/magic/game/payment-mode?player=1&manual=2 HTTP/1.1"
    in (list-at (base.orders) 1).manual-payment == False
    & (list-at ((d.store).orders) 1).manual-payment
    & (list-at ((d.store).orders) 1).preserved-stones == 1
    & spell-policy-index ((list-at ((d.store).orders) 1).spell-policy) == 1
    & (list-at ((d.store).orders) 0).manual-payment == False

  api-kept : Boolean
  api-kept =
    let base = api-store-automatic
    in let a = dispatch-request base "GET /api/magic/game/mana?player=0&stone=0&keep=1 HTTP/1.1"
    in let b = dispatch-request (a.store) "GET /api/magic/game/mana?player=0&stone=0&keep=1 HTTP/1.1"
    in let c = handle-game-spells (b.store) "GET /api/magic/game/spells?player=0&policy=1&reserve=0 HTTP/1.1"
    in let d = handle-game-posture (c.store) "GET /api/magic/game/posture?player=0&stance=2&freeze=1&blocks=1 HTTP/1.1"
    in let e = handle-game-freeze (d.store) "GET /api/magic/game/freeze?player=0&phase=7&enabled=1 HTTP/1.1"
    in let f = handle-game-target (e.store) "GET /api/magic/game/target?player=0&kind=general HTTP/1.1"
    in let bad = handle-game-mana (f.store) "GET /api/magic/game/mana?player=0&stone=7&keep=1 HTTP/1.1"
    in let old = (list-at ((bad.store).orders) 0).preserved-stones
    in let cleared = handle-game-mana (bad.store) "GET /api/magic/game/mana?player=0&stone=0&keep=0 HTTP/1.1"
    in (list-at (base.orders) 0).preserved-stones == 0 & old == 1
    & (list-at ((bad.store).orders) 1).preserved-stones == 0
    & (list-at ((cleared.store).orders) 0).preserved-stones == 0

  api-kept-cast : Boolean
  api-kept-cast =
    let g = py-game [py-gem 50 0 0] [10, 0] 2
    in let base = store-turn api-store-automatic (supervised-start g) True
    in let a = dispatch-request base "GET /api/magic/game/mana?player=0&stone=0&keep=1 HTTP/1.1"
    in let after = handle-game-step (a.store) False
    in let gs = (after.store).game
    in list-length (gs.battlefield) == 2 & ag-has-template gs ((get-player gs 0).hand) 10
    & (find-permanent (gs.battlefield) 50 0 2).tapped == False

  api-diamond : Boolean
  api-diamond =
    let g = dp-game [dp-diamond 60, dp-diamond 61] [15] 0
    in let base = store-turn api-store-automatic (supervised-start g) True
    in let configured = handle-game-spells base "GET /api/magic/game/spells?player=0&policy=3&reserve=0 HTTP/1.1"
    in let paused = handle-game-step (configured.store) False
    in let quote = manual-hand-json (paused.store) 0 ""
    in let card = ag-hand-card ((paused.store).game) 0 15
    in let expected = "{\"id\":" & show card & ",\"template-id\":15,\"name\":\"White giant\",\"cost\":\"{4}\",\"rays\":1,\"gem\":false,\"play-order\":0,\"ready\":true}"
    in let after = api-play-template (paused.store) 15 ""
    in quote == expected & list-length ((get-player ((after.store).game) 0).hand) == 0
    & ((get-player ((after.store).game) 0).prismatic).rays-used == 1

  api-payment : Boolean
  api-payment =
    let base = store-turn api-store-automatic (supervised-start (py-game [py-gem 50 0 0] [10, 10] 3)) True
    in let configured = handle-game-spells base "GET /api/magic/game/spells?player=0&policy=3&reserve=0 HTTP/1.1"
    in let paused = handle-game-step (configured.store) False
    in let first = api-play-template (paused.store) 10 ""
    in let second = api-play-template (first.store) 10 ""
    in let gs = (second.store).game
    in list-length (gs.battlefield) == 2 & list-length ((get-player gs 0).hand) == 1
    & ((get-player gs 0).prismatic).rays-used == 1
    & (find-permanent (gs.battlefield) 50 0 (list-length (gs.battlefield))).tapped

  api-manual-play : Boolean
  api-manual-play =
    let base = store-turn api-store-automatic (supervised-start (ag-game [] [] [] [1, 3] 2)) True
    in let a = dispatch-request base "GET /api/magic/game/spells?player=0&policy=3&reserve=0 HTTP/1.1"
    in let paused = handle-game-step (a.store) False
    in let before = list-length ((get-player ((paused.store).game) 0).hand)
    in let chosen-card = ag-hand-card ((paused.store).game) 0 1
    in let chosen = api-play-template (paused.store) 1 ""
    in let played = list-length (((chosen.store).game).battlefield) == 1 & (list-at (((chosen.store).game).battlefield) 0).template-id == 1
    in let refused = dispatch-request (chosen.store) ("GET /api/magic/game/play?card=" & show chosen-card & " HTTP/1.1")
    in let next = handle-game-step (refused.store) True
    in before == 2 & played & ((next.store).turn).stage == ai-stage-main-2 & ((next.store).turn).paused
    & list-length ((get-player ((next.store).game) 0).hand) == 1

  api-manual-end : Boolean
  api-manual-end =
    let g = tg-game [] [] [5]
    in let p = get-player g 1
    in let low = __record-set (p.general) "life" 3
    in let fixture = set-player g 1 (__record-set p "general" low)
    in let base = store-turn api-store-automatic (supervised-start fixture) True
    in let chosen-card = ag-hand-card (base.game) 0 5
    in let wrong = api-play-template base 5 ""
    in let kept = list-length ((get-player ((wrong.store).game) 0).hand) == 1
    in let configured = handle-game-spells (wrong.store) "GET /api/magic/game/spells?player=0&policy=3&reserve=0 HTTP/1.1"
    in let paused = handle-game-step (configured.store) False
    in let won = api-play-template (paused.store) 5 ""
    in let rays = ((get-player ((won.store).game) 0).prismatic).rays-used
    in let refused = dispatch-request (won.store) ("GET /api/magic/game/play?card=" & show chosen-card & " HTTP/1.1")
    in kept & ((won.store).game).game-over & (won.store).game-active == False
    & ((won.store).turn).stage == ai-stage-done & ((won.store).turn).paused == False
    & ((get-player ((refused.store).game) 0).prismatic).rays-used == rays

  api-spell-preserve : Boolean
  api-spell-preserve =
    let base = api-store-automatic
    in let a = dispatch-request base "GET /api/magic/game/spells?player=1&policy=2&reserve=3 HTTP/1.1"
    in let b = handle-game-posture (a.store) "GET /api/magic/game/posture?player=1&stance=2&freeze=1&blocks=0 HTTP/1.1"
    in let c = handle-game-freeze (b.store) "GET /api/magic/game/freeze?player=1&phase=7&enabled=1 HTTP/1.1"
    in let d = handle-game-target (c.store) "GET /api/magic/game/target?player=1&kind=general HTTP/1.1"
    in let p = list-at ((d.store).orders) 1
    in spell-policy-index ((list-at (base.orders) 1).spell-policy) == 0
    & spell-policy-reserve (p.spell-policy) == 3 & p.stance == Defensive & p.block-policy == ManualBlock
    & posture-freezes p ai-stage-end & list-length (p.primary-targets) == 1
    & spell-policy-index ((list-at ((d.store).orders) 0).spell-policy) == 0

  api-spell-reject : Boolean
  api-spell-reject =
    let a = handle-game-spells api-store-automatic "GET /api/magic/game/spells?player=0&policy=1&reserve=0 HTTP/1.1"
    in let b = handle-game-spells (a.store) "GET /api/magic/game/spells?player=0&policy=4&reserve=0 HTTP/1.1"
    in let c = handle-game-spells (b.store) "GET /api/magic/game/spells?player=0&policy=2&reserve=1000 HTTP/1.1"
    in let d = handle-game-spells (c.store) "GET /api/magic/game/spells?player=2&policy=0&reserve=0 HTTP/1.1"
    in let e = handle-game-spells (d.store) "GET /api/magic/game/spells?player=0&policy=2&reserve=-1 HTTP/1.1"
    in spell-policy-index ((list-at ((e.store).orders) 0).spell-policy) == 1

  api-spell-cast : Boolean
  api-spell-cast =
    let base = store-turn api-store-automatic (supervised-start (sp-game [5, 0, 1, 3] 3)) True
    in let a = dispatch-request base "GET /api/magic/game/spells?player=0&policy=1&reserve=0 HTTP/1.1"
    in let after = handle-game-step (a.store) False
    in list-length (((after.store).game).battlefield) == 2
    & prismatic-available-rays ((get-player ((after.store).game) 0).prismatic) == 2
    & ag-has-template ((after.store).game) ((get-player ((after.store).game) 0).hand) 5

  api-grant : Boolean
  api-grant =
    let base = store-turn api-store-automatic (supervised-start (bg-game phase-main-1)) True
    in let orders = handle-game-spells base "GET /api/magic/game/spells?player=0&policy=3&reserve=0 HTTP/1.1"
    in let paused = handle-game-step (orders.store) False
    in let played = api-play-template (paused.store) 5 ""
    in let boosted = (bg-perm ((played.store).game) 90).power-mod == 3
    in let second = handle-game-step (played.store) True
    in let still = (bg-perm ((second.store).game) 90).power-mod == 3
    in let done = handle-game-step (second.store) True
    in boosted & still & (bg-perm ((done.store).game) 90).power-mod == 0
  api-entry : Boolean
  api-entry =
    let g = __record-set (fe-game [fl-obsidian 50 0] [23] 3) "phase" phase-main-1
    in let turn = SupervisedTurn { combat-cantrip-stage = 0 - 1, manual-override = False, game = g, combat = ag-empty 0, stage = ai-stage-main-1, paused = True, commitment = commitment-none, held-cards = [], combat-choice = combat-choice-none, used-abilities = [] }
    in let base = store-turn api-store-automatic turn True
    in let orders = handle-game-spells base "GET /api/magic/game/spells?player=0&policy=3&reserve=0 HTTP/1.1"
    in let lit = dispatch-request (orders.store) "GET /api/magic/game/fluorescence?stone=50 HTTP/1.1"
    in let played = api-play-template (lit.store) 23 ""
    in let boosted = (bg-perm ((played.store).game) 100).power-mod == 2
    in let second = handle-game-step (played.store) True
    in let done = handle-game-step (second.store) True
    in let permanent = bg-perm ((done.store).game) 100
    in boosted & permanent.power-mod == 2 & has-keyword (permanent.keywords) Exsanguinate

  api-commitment-store : MagicStore
  api-commitment-store =
    let base = store-turn magic-store-new (cm-turn (ag-game [] [] [] [0] 1)) True
    in let configured = __record-set base "orders" [cm-orders, posture-unattended]
    in (handle-game-step configured False).store

  api-commitment-policy : Boolean
  api-commitment-policy =
    let base = magic-store-new
    in let defaults = (list-at (base.orders) 0).commitment-pause & (list-at (base.orders) 1).commitment-pause
    in let off = handle-game-commitment-policy base "GET /api/magic/game/commitment-policy?player=0&enabled=0 HTTP/1.1"
    in let stance = handle-game-posture (off.store) "GET /api/magic/game/posture?player=0&stance=2&freeze=1 HTTP/1.1"
    in let invalid = handle-game-commitment-policy (stance.store) "GET /api/magic/game/commitment-policy?player=0&enabled=2 HTTP/1.1"
    in defaults & (list-at ((invalid.store).orders) 0).commitment-pause == False
    & (list-at ((invalid.store).orders) 1).commitment-pause & (list-at ((invalid.store).orders) 0).stance == Defensive

  api-commitment-cast : Boolean
  api-commitment-cast =
    let before = game-clock-ms
    in let pending = api-commitment-store
    in let after = game-clock-ms
    in let decision = (pending.turn).commitment
    in let repeated-step = handle-game-step pending True
    in let unchanged = (((repeated-step.store).turn).commitment).nonce == decision.nonce & (((repeated-step.store).turn).commitment).deadline == decision.deadline
    in let played = handle-game-commitment-at (repeated-step.store) (decision.nonce) True (decision.deadline - 1)
    in let repeated = handle-game-commitment-at (played.store) (decision.nonce) True (decision.deadline - 1)
    in unchanged & decision.nonce > 0 & decision.deadline >= before + 15000 & decision.deadline <= after + 15000
    & list-length (((repeated.store).game).battlefield) == 1
    & ((get-player ((repeated.store).game) 0).prismatic).rays-used == 1
    & repeated.resp == "{\"error\":\"decision is no longer pending\"}"

  api-commitment-timeout : Boolean
  api-commitment-timeout =
    let pending = api-commitment-store
    in let decision = (pending.turn).commitment
    in let held = handle-game-commitment-at pending (decision.nonce) True (decision.deadline)
    in list-length (((held.store).game).battlefield) == 0
    & list-length ((get-player ((held.store).game) 0).hand) == 1
    & ((get-player ((held.store).game) 0).prismatic).rays-used == 0
    & ((held.store).turn).stage == ai-stage-attackers

  api-commitment-revalidate : Boolean
  api-commitment-revalidate =
    let pending = api-commitment-store
    in let decision = (pending.turn).commitment
    in let changed = handle-game-spells pending "GET /api/magic/game/spells?player=0&policy=2&reserve=1 HTTP/1.1"
    in let refused = handle-game-commitment-at (changed.store) (decision.nonce) True (decision.deadline - 1)
    in let still = (((refused.store).turn).commitment).nonce == decision.nonce
    in let held = handle-game-commitment-at (refused.store) (decision.nonce) False (decision.deadline - 1)
    in still & list-length ((get-player ((held.store).game) 0).hand) == 1
    & ((get-player ((held.store).game) 0).prismatic).rays-used == 0

  api-commitment-new : Boolean
  api-commitment-new =
    let pending = api-commitment-store
    in let expired = __record-set ((pending.turn).commitment) "deadline" 0
    in let legal = __record-set pending "templates" full-card-pool
    in let fresh = dispatch-request legal "GET /api/magic/game/new?general-a=1&general-b=3 HTTP/1.1"
    in (get-general ((fresh.store).game) 0).id == 1 & (((fresh.store).turn).commitment).card < 0

  api-instance-store : GameState -> MagicStore
  api-instance-store (g) = __record-set (__record-set (store-turn api-store-automatic (ci-turn g) True) "orders" [ms-orders, posture-unattended]) "templates" (g.templates)

  api-instance-play : Boolean
  api-instance-play =
    let g = ag-game-templates tg-templates [] [] [] [5, 5] 3
    in let first = list-at ((get-player g 0).hand) 0
    in let second = list-at ((get-player g 0).hand) 1
    in let store = api-instance-store g
    in let metadata = text-starts-with (manual-hand-json store 0 "") "{\"id\":0,\"template-id\":5,\"name\":\"Bolt\""
    in let request = "GET /api/magic/game/play?card=" & show second & " HTTP/1.1"
    in let played = dispatch-request store request
    in let again = dispatch-request (played.store) request
    in let after = (again.store).game
    in metadata & first /= second & list-at ((get-player after 0).hand) 0 == first
    & list-length ((get-player after 0).hand) == 1 & list-at ((get-player after 0).graveyard) 0 == second
    & list-length ((get-player after 0).graveyard) == 1 & ((get-player after 1).general).life == 27
    & card-template-id after first == 5 & card-template-id after second == 5

  api-instance-gem : Boolean
  api-instance-gem =
    let g = ci-game [0, 0, 0, 0, 0, 0, 1] [1, 1] 0
    in let card = list-at ((get-player g 0).hand) 6
    in let store = api-instance-store g
    in let metadata = text-starts-with (manual-hand-json store 6 "") "{\"id\":6,\"template-id\":1,\"name\":\"Ruby\""
    in let played = dispatch-request store ("GET /api/magic/game/play?card=" & show card & " HTTP/1.1")
    in let after = (played.store).game
    in metadata & card >= list-length (after.templates) & (bg-perm after 0).card-id == card
    & (bg-perm after 0).template-id == 1 & list-length ((get-player after 0).hand) == 6 & ci-conserved after 0

  api-set-card-order : MagicStore, Integer, Integer, Integer -> MagicStep
  api-set-card-order (store) (player) (card) (mode) = dispatch-request store ("GET /api/magic/game/card-order?player=" & show player & "&card=" & show card & "&order=" & show mode & " HTTP/1.1")

  api-card-flags : Boolean
  api-card-flags =
    let base = api-instance-store (ci-game [0, 0] [1, 1] 3)
    in let held = api-set-card-order base 0 0 1
    in let next = api-set-card-order (held.store) 0 1 2
    in let g = (next.store).game
    in card-play-order (base.game) 0 == card-order-auto & card-play-order g 0 == card-order-hold
    & card-play-order g 1 == card-order-next & card-play-order (((next.store).turn).game) 1 == card-order-next
    & list-length ((get-player g 0).hand) == 2 & ((get-player g 0).prismatic).rays-used == 0

  api-card-flags-refuse : Boolean
  api-card-flags-refuse =
    let base = api-instance-store (ci-game [0, 0] [1, 1] 3)
    in let foreign = api-set-card-order base 1 0 1
    in let unknown = api-set-card-order (foreign.store) 0 999 1
    in let hidden = api-set-card-order (unknown.store) 0 2 1
    in let invalid = api-set-card-order (hidden.store) 0 0 3
    in text-starts-with (foreign.resp) "{\"error\"" & text-starts-with (unknown.resp) "{\"error\""
    & text-starts-with (hidden.resp) "{\"error\"" & text-starts-with (invalid.resp) "{\"error\""
    & list-length (((invalid.store).game).card-orders) == 0

  api-card-flags-manual : Boolean
  api-card-flags-manual =
    let base = api-instance-store (ci-game [0, 0] [1, 1] 3)
    in let held = api-set-card-order base 0 0 1
    in let next = api-set-card-order (held.store) 0 1 2
    in let first = dispatch-request (next.store) "GET /api/magic/game/play?card=0 HTTP/1.1"
    in let second = dispatch-request (first.store) "GET /api/magic/game/play?card=1 HTTP/1.1"
    in let g = (second.store).game
    in list-length (g.battlefield) == 2 & card-play-order g 0 == card-order-hold
    & card-play-order g 1 == card-order-auto & ((get-player g 0).prismatic).rays-used == 2

  api-card-flags-board : Boolean
  api-card-flags-board =
    let stolen = __record-set (ag-perm 90 0 1) "owner" 0
    in let g = ag-game [] [] [stolen] [] 1
    in let card = (bg-perm g 90).card-id
    in let base = api-instance-store g
    in let wrong = api-set-card-order base 1 card 1
    in let right = api-set-card-order (wrong.store) 0 card 1
    in let returned = apply-effect-direct ((right.store).game) ReturnToHand 0 [90]
    in text-starts-with (wrong.resp) "{\"error\"" & card-play-order returned card == card-order-hold
    & list-at ((get-player returned 0).hand) 0 == card

  api-card-flags-new : Boolean
  api-card-flags-new =
    let base = api-instance-store (ci-game [0] [1, 1] 3)
    in let held = api-set-card-order base 0 0 1
    in let legal = __record-set (held.store) "templates" full-card-pool
    in let fresh = dispatch-request legal "GET /api/magic/game/new HTTP/1.1"
    in card-play-order ((fresh.store).game) 0 == card-order-auto & list-length (((fresh.store).game).card-orders) == 0

  api-card-flags-commitment : Boolean
  api-card-flags-commitment =
    let base = api-commitment-store
    in let decision = (base.turn).commitment
    in let held = api-set-card-order base 0 (decision.card) 1
    in let preserved = (((held.store).turn).commitment).nonce == decision.nonce & (((held.store).turn).commitment).deadline == decision.deadline
    in let played = handle-game-commitment-at (held.store) (decision.nonce) True (decision.deadline - 1)
    in preserved & list-length (((played.store).game).battlefield) == 1
    & card-play-order ((played.store).game) (decision.card) == card-order-hold

  api-attack-store : Stance -> MagicStore
  api-attack-store (stance) =
    let g = ag-game [] [] [ag-perm 10 0 0, ag-perm 11 1 0, ag-perm 20 3 1] [] 0
    in let store = store-turn api-store-automatic (ac-pause g stance) True
    in __record-set (__record-set store "templates" (g.templates)) "orders" [ac-orders stance, ac-orders stance]

  api-attack-empty : Boolean
  api-attack-empty =
    let base = api-attack-store Aggressive
    in let first = dispatch-request base "GET /api/magic/game/attack?attacker=10&enabled=0 HTTP/1.1"
    in let none = dispatch-request (first.store) "GET /api/magic/game/attack?attacker=11&enabled=0 HTTP/1.1"
    in let reload = dispatch-request (none.store) "GET /api/magic/game/state HTTP/1.1"
    in let repeated = dispatch-request (reload.store) "GET /api/magic/game/step HTTP/1.1"
    in let after = dispatch-request (repeated.store) "GET /api/magic/game/step?resume=1 HTTP/1.1"
    in ((base.turn).combat).num-attackers == 2 & (((after.store).turn).combat).num-attackers == 0
    & ((after.store).turn).stage == ai-stage-blockers & ((after.store).turn).paused
    & text-contains (reload.resp) "\"attackers\":[]" & text-contains (reload.resp) "\"attack-ready\":true"
    & (find-permanent (((after.store).game).battlefield) 10 0 3).tapped == False

  api-attack-select : Boolean
  api-attack-select =
    let base = api-attack-store Defensive
    in let chosen = dispatch-request base "GET /api/magic/game/attack?attacker=11&enabled=1 HTTP/1.1"
    in let repeated = dispatch-request (chosen.store) "GET /api/magic/game/attack?attacker=11&enabled=1 HTTP/1.1"
    in let after = dispatch-request (repeated.store) "GET /api/magic/game/step?resume=1 HTTP/1.1"
    in ((base.turn).combat).num-attackers == 0 & (((after.store).turn).combat).num-attackers == 1
    & text-contains (repeated.resp) "\"attackers\":[{\"id\":11}]"
    & (find-permanent (((after.store).game).battlefield) 11 0 3).tapped
    & (find-permanent (((after.store).game).battlefield) 10 0 3).tapped == False

  api-attack-refuse : Boolean
  api-attack-refuse =
    let base = api-attack-store Aggressive
    in let wrong = dispatch-request base "GET /api/magic/game/attack?attacker=20&enabled=1 HTTP/1.1"
    in let invalid = dispatch-request (wrong.store) "GET /api/magic/game/attack?attacker=oops&enabled=1 HTTP/1.1"
    in let mode = dispatch-request (invalid.store) "GET /api/magic/game/attack?attacker=10&enabled=2 HTTP/1.1"
    in let resumed = handle-game-step base True
    in let late = dispatch-request (resumed.store) "GET /api/magic/game/attack?attacker=10&enabled=0 HTTP/1.1"
    in text-starts-with (wrong.resp) "{\"error\"" & text-starts-with (invalid.resp) "{\"error\""
    & text-starts-with (mode.resp) "{\"error\"" & text-starts-with (late.resp) "{\"error\""
    & (((mode.store).turn).combat).num-attackers == 2 & (((late.store).turn).combat).num-attackers == 2

  api-target-store : Integer -> MagicStore
  api-target-store (unused) =
    let g = tg-game [] [ag-perm 20 0 1, ag-perm 21 3 1] [5]
    in let store = api-instance-store g
    in __record-set store "orders" [target-choice-posture ms-orders (0 - 2), posture-unattended]

  api-target-preview : Boolean
  api-target-preview =
    let store = api-target-store 0
    in let card = ag-hand-card (store.game) 0 5
    in let preview = dispatch-request store ("GET /api/magic/game/targets?card=" & show card & " HTTP/1.1")
    in let wrong = dispatch-request store "GET /api/magic/game/targets?card=999 HTTP/1.1"
    in text-contains (preview.resp) "\"recommended\":-2" & text-contains (preview.resp) "Bear #20" & text-contains (preview.resp) "Giant #21"
    & list-length ((get-player ((preview.store).game) 0).hand) == 1
    & ((get-player ((preview.store).game) 0).prismatic).rays-used == 0 & text-starts-with (wrong.resp) "{\"error\""

  api-target-play : Boolean
  api-target-play =
    let store = api-target-store 0
    in let played = api-play-template store 5 "&target=20"
    in let g = (played.store).game
    in tg-has g 20 == False & tg-has g 21 & ((get-player g 1).general).life == 30
    & ((get-player g 0).prismatic).rays-used == 1
    & (when list-at ((list-at ((played.store).orders) 0).primary-targets) 0 is PriorityGeneral -> True is otherwise -> False)

  api-target-general : Boolean
  api-target-general =
    let played = api-play-template (api-target-store 0) 5 "&target=-2"
    in let g = (played.store).game
    in ((get-player g 1).general).life == 27 & tg-has g 20 & tg-has g 21

  api-target-refuse : Boolean
  api-target-refuse =
    let store = api-target-store 0
    in let wrong = api-play-template store 5 "&target=-1"
    in let malformed = api-play-template (wrong.store) 5 "&target=oops"
    in let stale-game = apply-effect-direct ((malformed.store).game) ExileTarget 0 [20]
    in let stale = api-play-template (api-instance-store stale-game) 5 "&target=20"
    in text-starts-with (wrong.resp) "{\"error\"" & text-starts-with (malformed.resp) "{\"error\"" & text-starts-with (stale.resp) "{\"error\""
    & ((get-player ((stale.store).game) 0).prismatic).rays-used == 0 & list-length ((get-player ((stale.store).game) 0).hand) == 1

  api-target-payment : Boolean
  api-target-payment =
    let base = api-target-store 0
    in let manual = handle-game-payment-mode base "GET /api/magic/game/payment-mode?player=0&manual=1 HTTP/1.1"
    in let bad = api-play-template (manual.store) 5 "&chains=w&target=999"
    in let good = api-play-template (bad.store) 5 "&chains=w&target=21"
    in text-starts-with (bad.resp) "{\"error\"" & tg-has ((good.store).game) 20
    & (bg-perm ((good.store).game) 21).damage-marked == 3
    & ((get-player ((good.store).game) 0).prismatic).rays-used == 1

  api-target-decision : Integer -> MagicStore
  api-target-decision (rays) =
    let g = tc-game rays
    in let base = __record-set (__record-set api-store-automatic "templates" (g.templates)) "orders" [tc-orders-on, posture-unattended]
    in store-turn base (tc-start g tc-orders-on) True

  api-target-policy : Boolean
  api-target-policy =
    let base = magic-store-new
    in let on = handle-game-target-policy base "GET /api/magic/game/target-policy?player=0&enabled=1 HTTP/1.1"
    in let stance = handle-game-posture (on.store) "GET /api/magic/game/posture?player=0&stance=2&freeze=1 HTTP/1.1"
    in let bad = handle-game-target-policy (stance.store) "GET /api/magic/game/target-policy?player=0&enabled=2 HTTP/1.1"
    in (list-at (base.orders) 0).target-pause == False & (list-at ((bad.store).orders) 0).target-pause
    & (list-at ((bad.store).orders) 1).target-pause == False & text-starts-with (bad.resp) "{\"error\""

  api-target-pending : Boolean
  api-target-pending =
    let base = api-target-decision 3
    in let d = (base.turn).commitment
    in let repeat = handle-game-step base True
    in let policy = handle-game-target-policy (repeat.store) "GET /api/magic/game/target-policy?player=0&enabled=0 HTTP/1.1"
    in let saved = ((policy.store).turn).commitment
    in saved.nonce == d.nonce & saved.deadline == d.deadline & saved.recommended == 21 & list-length (saved.choices) == 2
    & text-contains (policy.resp) "\"all-rays\":false" & text-contains (policy.resp) "Bear #20"
    & ((get-player ((policy.store).game) 0).prismatic).rays-used == 0

  api-target-choice : Boolean
  api-target-choice =
    let base = api-target-decision 3
    in let d = (base.turn).commitment
    in let bad = handle-game-commitment-target-at base (d.nonce) True 999 (d.deadline - 1)
    in let chosen = handle-game-commitment-target-at (bad.store) (d.nonce) True 20 (d.deadline - 1)
    in let repeat = handle-game-commitment-target-at (chosen.store) (d.nonce) True 20 (d.deadline - 1)
    in text-starts-with (bad.resp) "{\"error\"" & text-starts-with (repeat.resp) "{\"error\""
    & tg-has ((chosen.store).game) 20 == False & tg-has ((chosen.store).game) 21
    & ((get-player ((repeat.store).game) 0).prismatic).rays-used == 1

  api-target-timeout : Boolean
  api-target-timeout =
    let base = api-target-decision 3
    in let d = (base.turn).commitment
    in let changed = handle-game-target (base) "GET /api/magic/game/target?player=0&kind=permanent&id=20 HTTP/1.1"
    in let expired = handle-game-commitment-target-at (changed.store) (d.nonce) True 20 (d.deadline)
    in tg-has ((expired.store).game) 21 == False & tg-has ((expired.store).game) 20
    & ((get-player ((expired.store).game) 0).prismatic).rays-used == 1 & ((expired.store).turn).stage == ai-stage-attackers

  api-target-timeout-all : Boolean
  api-target-timeout-all =
    let base = api-target-decision 1
    in let d = (base.turn).commitment
    in let expired = handle-game-commitment-target-at base (d.nonce) True 20 (d.deadline)
    in tg-has ((expired.store).game) 20 & tg-has ((expired.store).game) 21
    & ((get-player ((expired.store).game) 0).prismatic).rays-used == 0 & list-length ((get-player ((expired.store).game) 0).hand) == 1

  api-target-timeout-orders : Boolean
  api-target-timeout-orders =
    let base = api-target-decision 3
    in let d = (base.turn).commitment
    in let reserved = handle-game-spells base "GET /api/magic/game/spells?player=0&policy=2&reserve=2 HTTP/1.1"
    in let expired = handle-game-commitment-at (reserved.store) (d.nonce) False (d.deadline)
    in let other = api-target-decision 3
    in let od = (other.turn).commitment
    in let held = api-set-card-order other 0 (od.card) 1
    in let held-expired = handle-game-commitment-at (held.store) (od.nonce) False (od.deadline)
    in let third = api-target-decision 3
    in let td = (third.turn).commitment
    in let manual = handle-game-payment-mode third "GET /api/magic/game/payment-mode?player=0&manual=1 HTTP/1.1"
    in let manual-expired = handle-game-commitment-at (manual.store) (td.nonce) False (td.deadline)
    in ((get-player ((expired.store).game) 0).prismatic).rays-used == 0 & tg-has ((expired.store).game) 21
    & ((get-player ((held-expired.store).game) 0).prismatic).rays-used == 0 & tg-has ((held-expired.store).game) 21
    & ((get-player ((manual-expired.store).game) 0).prismatic).rays-used == 0 & tg-has ((manual-expired.store).game) 21

  api-target-timeout-stale : Boolean
  api-target-timeout-stale =
    let base = api-target-decision 3
    in let d = (base.turn).commitment
    in let gone = apply-effect-direct (base.game) ExileTarget 0 [21]
    in let turn = __record-set (base.turn) "game" gone
    in let stale = store-turn base turn True
    in let expired = handle-game-commitment-at stale (d.nonce) False (d.deadline)
    in ((get-player ((expired.store).game) 0).prismatic).rays-used == 0 & tg-has ((expired.store).game) 20
    & ((expired.store).turn).stage == ai-stage-attackers & (((expired.store).turn).commitment).card < 0

  api-combat-store : MagicStore
  api-combat-store =
    let g = cb-game 1 False
    in let base = __record-set (__record-set api-store-automatic "templates" (g.templates)) "orders" [cb-orders, cb-orders]
    in store-turn base (cb-start g cb-orders) True

  api-combat-policy : Boolean
  api-combat-policy =
    let base = magic-store-new
    in let on = handle-game-combat-policy base "GET /api/magic/game/combat-policy?player=0&enabled=1 HTTP/1.1"
    in let posture = handle-game-posture (on.store) "GET /api/magic/game/posture?player=0&stance=2&freeze=0 HTTP/1.1"
    in let bad = handle-game-combat-policy (posture.store) "GET /api/magic/game/combat-policy?player=0&enabled=2 HTTP/1.1"
    in (list-at (base.orders) 0).combat-pause == False & (list-at ((bad.store).orders) 0).combat-pause
    & (list-at ((bad.store).orders) 1).combat-pause == False & text-starts-with (bad.resp) "{\"error\""

  api-combat-pending : Boolean
  api-combat-pending =
    let before = game-clock-ms
    in let base = api-combat-store
    in let after = game-clock-ms
    in let choice = (base.turn).combat-choice
    in let repeat = handle-game-step base True
    in let early = handle-game-combat-choice-at (repeat.store) (choice.nonce) True (choice.deadline - 1)
    in let changed = handle-game-combat-policy (early.store) "GET /api/magic/game/combat-policy?player=0&enabled=0 HTTP/1.1"
    in let saved = ((changed.store).turn).combat-choice
    in saved.pending & saved.nonce == choice.nonce & saved.deadline == choice.deadline
    & saved.deadline >= before + 15000 & saved.deadline <= after + 15000
    & (((changed.store).turn).combat).num-attackers == 2 & (bg-perm ((changed.store).game) 10).tapped == False
    & text-contains (changed.resp) "\"combat-choice\":{\"nonce\":"

  api-combat-timeout : Boolean
  api-combat-timeout =
    let base = api-combat-store
    in let choice = (base.turn).combat-choice
    in let changed = handle-game-posture base "GET /api/magic/game/posture?player=0&stance=2&freeze=0 HTTP/1.1"
    in let expired = handle-game-combat-choice-at (changed.store) (choice.nonce) True (choice.deadline)
    in ((expired.store).turn).stage == ai-stage-blockers & (((expired.store).turn).combat).num-attackers == 2
    & (bg-perm ((expired.store).game) 10).tapped & (bg-perm ((expired.store).game) 11).tapped
    & (((expired.store).turn).combat-choice).pending == False

  api-combat-edit : Boolean
  api-combat-edit =
    let base = api-combat-store
    in let choice = (base.turn).combat-choice
    in let edited = handle-game-attack base "GET /api/magic/game/attack?attacker=10&enabled=0 HTTP/1.1"
    in let expired = handle-game-combat-choice-at (edited.store) (choice.nonce) True (choice.deadline)
    in let after = handle-game-step (expired.store) True
    in text-starts-with (expired.resp) "{\"error\"" & ((expired.store).turn).stage == ai-stage-attackers
    & (((after.store).turn).combat).num-attackers == 1 & (bg-perm ((after.store).game) 10).tapped == False
    & (bg-perm ((after.store).game) 11).tapped & (((after.store).turn).combat-choice).pending == False

  api-combat-once : Boolean
  api-combat-once =
    let base = api-combat-store
    in let choice = (base.turn).combat-choice
    in let wrong = handle-game-combat-choice-at base (choice.nonce + 1) False (choice.deadline - 1)
    in let chosen = handle-game-combat-choice-at (wrong.store) (choice.nonce) False (choice.deadline - 1)
    in let again = handle-game-combat-choice-at (chosen.store) (choice.nonce) False (choice.deadline - 1)
    in text-starts-with (wrong.resp) "{\"error\"" & text-starts-with (again.resp) "{\"error\""
    & ((again.store).turn).stage == ai-stage-blockers & (((again.store).turn).combat).num-attackers == 2

  api-combat-new : Boolean
  api-combat-new =
    let base = api-combat-store
    in let choice = (base.turn).combat-choice
    in let legal = __record-set base "templates" full-card-pool
    in let fresh = handle-game-new legal "GET /api/magic/game/new HTTP/1.1"
    in let late = handle-game-combat-choice-at (fresh.store) (choice.nonce) True (choice.deadline)
    in (((late.store).turn).combat-choice).pending == False & ((late.store).game).turn-number == 1
    & text-starts-with (late.resp) "{\"error\""

  api-combat-late-edit : Boolean
  api-combat-late-edit =
    let base = api-combat-store
    in let expired = __record-set ((base.turn).combat-choice) "deadline" 0
    in let turn = __record-set (base.turn) "combat-choice" expired
    in let late = dispatch-request (store-turn base turn True) "GET /api/magic/game/attack?attacker=10&enabled=0 HTTP/1.1"
    in ((late.store).turn).stage == ai-stage-blockers & (((late.store).turn).combat).num-attackers == 2
    & (bg-perm ((late.store).game) 10).tapped & (bg-perm ((late.store).game) 11).tapped

  api-qa-json : Boolean
  api-qa-json =
    let base = api-qa-store (pa-cost 0 False False 0) (DealDamage 2)
    in let source = __record-set (pa-template (pa-cost 0 False False 0) (DealDamage 2)) "name" api-j-text
    in let target = make-creature 3 api-j-text (light-cost-w 1) 4 4 0 keywords-none
    in let templates = list-set-at (list-set-at (for t in (base.game).templates -> t) 5 source) 3 target
    in let changed = __record-set (base.game) "templates" templates
    in let queued = handle-game-queue-ability base "GET /api/magic/game/queue-ability?choice=queue&player=0&permanent=10&index=0&target=20 HTTP/1.1"
    in let parsed = api-j-array (api-j-value (queued.resp)) "queued-abilities"
    in json-get-str parsed "label" "" == (list-at (((queued.store).game).queued-abilities) 0).label
      & json-get-bool parsed "pending" False & text-contains (json-get-str parsed "label" "") api-j-text

  api-qa-store : ActivationCost, Effect -> MagicStore
  api-qa-store (cost) (eff) = __record-set (api-pa-store cost eff) "orders" [qa-orders, posture-unattended]

  api-qa-preview : Boolean
  api-qa-preview =
    let base = api-qa-store (pa-cost 5 True False 0) (DealDamage 2)
    in let active = __record-set (base.game) "active-player" 1
    in let shown = dispatch-request base "GET /api/magic/game/queue-targets?player=0&permanent=10&index=0 HTTP/1.1"
    in text-contains (shown.resp) "\"id\":20" & (list-at (((shown.store).game).queued-abilities) 0).source < 0
      & ((get-player ((shown.store).game) 0).prismatic).rays-used == 0
      & text-contains (queue-options-json (shown.store)) "Channeler #10"

  api-qa-retained : MagicStore
  api-qa-retained =
    let base = api-qa-store (pa-cost 4 False False 0) (GainLife 2)
    in let queued = handle-game-queue-ability base "GET /api/magic/game/queue-ability?choice=queue&player=0&permanent=10&index=0 HTTP/1.1"
    in let ready = store-turn (queued.store) (qa-main-two ((queued.store).game)) True
    in (handle-game-step ready False).store

  api-qa-retain : Boolean
  api-qa-retain =
    let base = api-qa-retained
    in let reload = handle-game-state base
    in let q = list-at (((reload.store).game).queued-abilities) 0
    in q.source == 10 & q.reason == "Waiting for enough rays" & text-contains (reload.resp) "Waiting for enough rays"
      & ((get-player ((reload.store).game) 0).prismatic).rays-used == 0

  api-qa-cancel : Boolean
  api-qa-cancel =
    let base = api-qa-retained
    in let cancelled = dispatch-request base "GET /api/magic/game/queue-ability?choice=cancel&player=0 HTTP/1.1"
    in let again = dispatch-request (cancelled.store) "GET /api/magic/game/queue-ability?choice=cancel&player=0 HTTP/1.1"
    in (list-at (((again.store).game).queued-abilities) 0).source < 0
      & ((get-player ((again.store).game) 0).prismatic).rays-used == 0 & (list-at ((base.game).queued-abilities) 0).source == 10

  api-qa-refuse : Boolean
  api-qa-refuse =
    let base = api-qa-retained
    in let foreign = handle-game-queue-ability base "GET /api/magic/game/queue-ability?choice=queue&player=1&permanent=10&index=0 HTTP/1.1"
    in let invalid = handle-game-queue-ability (foreign.store) "GET /api/magic/game/queue-ability?choice=queue&player=0&permanent=10&index=0&target=999 HTTP/1.1"
    in text-contains (foreign.resp) "cannot be queued" & text-contains (invalid.resp) "invalid queued target"
      & (list-at (((invalid.store).game).queued-abilities) 0).source == 10 & (list-at (((invalid.store).game).queued-abilities) 1).source < 0

  api-qa-target : Boolean
  api-qa-target =
    let base = api-qa-store (pa-cost 0 False False 0) (DealDamage 2)
    in let first = handle-game-queue-ability base "GET /api/magic/game/queue-ability?choice=queue&player=0&permanent=10&index=0&target=-2 HTTP/1.1"
    in let second = handle-game-queue-ability (first.store) "GET /api/magic/game/queue-ability?choice=queue&player=0&permanent=10&index=0&target=20 HTTP/1.1"
    in let ready = store-turn (second.store) (qa-main-two ((second.store).game)) True
    in let after = handle-game-step ready False
    in (bg-perm ((after.store).game) 20).damage-marked == 2 & ((get-player ((after.store).game) 1).general).life == 30
      & (list-at (((after.store).game).queued-abilities) 0).source < 0

  api-qa-reset : Boolean
  api-qa-reset =
    let g = ag-game-templates (ag-templates & [pa-template (pa-cost 0 False False 0) (GainLife 2)]) [] [] [tg-perm 10 5 0 keywords-none, tg-perm 20 5 1 keywords-none] [] 3
    in let queued = queue-ability-order (queue-ability-order g 0 10 0 (0 - 3) ai-stage-upkeep "A") 1 20 0 (0 - 3) ai-stage-upkeep "B"
    in let base = store-turn api-store-automatic (cm-turn queued) True
    in let refused = handle-game-new base "GET /api/magic/game/new?general-a=999 HTTP/1.1"
    in let kept = (list-at (((refused.store).game).queued-abilities) 0).source == 10 & (list-at (((refused.store).game).queued-abilities) 1).source == 20
    in let fresh = handle-game-new base "GET /api/magic/game/new HTTP/1.1"
    in let cancelled = handle-game-cancel base
    in kept & (list-at (((fresh.store).game).queued-abilities) 0).source < 0 & (list-at (((fresh.store).game).queued-abilities) 1).source < 0
      & (list-at (((cancelled.store).game).queued-abilities) 0).source < 0 & (list-at (((cancelled.store).game).queued-abilities) 1).source < 0

  api-qa-life : Boolean
  api-qa-life =
    let turn = advance-supervised (cm-turn (qa-game (pa-cost 0 False False 30) (GainLife 99) (0 - 3) ai-stage-upkeep)) qa-orders posture-unattended False
    in let shown = handle-game-state (store-turn api-store-automatic turn True)
    in text-contains (shown.resp) "Would pay last life" & (list-at (((shown.store).game).queued-abilities) 0).source == 10
      & ((shown.store).game).game-over == False

  api-dw-payment : Boolean
  api-dw-payment =
    let asked = advance-supervised (cm-turn dw-colored) dw-orders dw-reply-orders False
    in let base = store-turn (__record-set api-store-automatic "orders" [dw-orders, dw-reply-orders]) asked True
    in let card = ag-hand-card (base.game) 1 6
    in let window = ((base.game).disruption).window
    in let changed = handle-game-mana base "GET /api/magic/game/mana?player=1&stone=0&keep=1 HTTP/1.1"
    in let refused = handle-game-disruption-at (changed.store) (window.nonce) card (window.deadline - 1)
    in let unpaid = ((get-player ((refused.store).game) 1).prismatic).rays-used == 0 & (bg-perm ((refused.store).game) 20).tapped == False
    in let passed = handle-game-disruption-at (refused.store) (window.nonce) (0 - 1) (window.deadline - 1)
    in text-contains (refused.resp) "choose Pass" & unpaid & ((get-player ((passed.store).game) 1).general).life == 27

  api-dw-early : Boolean
  api-dw-early =
    let base = api-dw-store
    in let window = ((base.game).disruption).window
    in let clock = game-clock-ms
    in let next = handle-game-disruption base ("GET /api/magic/game/disruption?nonce=" & show (window.nonce) & "&choice=timeout HTTP/1.1")
    in if clock < 0 then disruption-pending ((next.store).game) == False
      else ((((next.store).game).disruption).window).nonce == window.nonce & ((get-player ((next.store).game) 1).prismatic).rays-used == 0

  api-dw-store : MagicStore
  api-dw-store = store-turn (__record-set api-store-automatic "orders" [dw-orders, dw-reply-orders]) dw-pending True

  api-dw-reload : Boolean
  api-dw-reload =
    let base = api-dw-store
    in let window = ((base.game).disruption).window
    in let shown = handle-game-state base
    in let repeated = handle-game-step (shown.store) True
    in let again = (((repeated.store).game).disruption).window
    in window.nonce > 0 & again.nonce == window.nonce & again.deadline == window.deadline
    & ((get-player ((repeated.store).game) 0).prismatic).rays-used == 1
    & ((get-player ((repeated.store).game) 1).prismatic).rays-used == 0
    & text-contains (shown.resp) "\"disruption\":{\"nonce\":" & text-contains (shown.resp) "\"chance\":100"

  api-dw-once : Boolean
  api-dw-once =
    let base = api-dw-store
    in let window = ((base.game).disruption).window
    in let card = ag-hand-card (base.game) 1 6
    in let first = handle-game-disruption-at base (window.nonce) card (window.deadline - 1)
    in let second = handle-game-disruption-at (first.store) (window.nonce) card (window.deadline - 1)
    in (((second.store).game).disruption).index == 1 & ((get-player ((second.store).game) 1).prismatic).rays-used == 1
    & text-contains (first.resp) "\"disrupted\":true" & text-contains (second.resp) "no longer pending"

  api-dw-timeout : Boolean
  api-dw-timeout =
    let base = api-dw-store
    in let window = ((base.game).disruption).window
    in let late = handle-game-disruption-at base (window.nonce) (ag-hand-card (base.game) 1 6) (window.deadline)
    in let passed = ((get-player ((late.store).game) 1).general).life == 27 & ((get-player ((late.store).game) 1).prismatic).rays-used == 0
    in let other = api-dw-store
    in let missing = handle-game-disruption-at other ((((other.game).disruption).window).nonce) (ag-hand-card (other.game) 1 6) (0 - 1)
    in passed & ((get-player ((missing.store).game) 1).general).life == 27 & (((missing.store).game).disruption).index == 0

  api-dw-reject : Boolean
  api-dw-reject =
    let base = api-dw-store
    in let window = ((base.game).disruption).window
    in let refused = handle-game-disruption-at base (window.nonce) 999 (window.deadline - 1)
    in let malformed = handle-game-disruption (refused.store) "GET /api/magic/game/disruption?nonce=oops&choice=disrupt&card=1 HTTP/1.1"
    in text-contains (refused.resp) "choose Pass" & text-contains (malformed.resp) "invalid disruption choice"
    & ((((refused.store).game).disruption).window).nonce == window.nonce & ((get-player ((refused.store).game) 1).prismatic).rays-used == 0

  api-dw-policy : Boolean
  api-dw-policy =
    let base = api-dw-store
    in let window = ((base.game).disruption).window
    in let changed = handle-game-disruption-policy base "GET /api/magic/game/disruption-policy?player=1&enabled=0 HTTP/1.1"
    in let shown = handle-game-step (changed.store) True
    in (list-at ((changed.store).orders) 1).disruption-pause == False
    & ((((shown.store).game).disruption).window).nonce == window.nonce
    & ((((shown.store).game).disruption).window).deadline == window.deadline
    & (list-at (base.orders) 1).disruption-pause

  api-dw-cancel : Boolean
  api-dw-cancel =
    let base = api-dw-store
    in let expired = __record-set (((base.game).disruption).window) "deadline" 1
    in let cancelled = dispatch-request base "GET /api/magic/game/cancel HTTP/1.1"
    in (cancelled.store).game-active == False & disruption-pending ((cancelled.store).game) == False
    & (((cancelled.store).game).disruption).index == 0 & ((get-player (base.game) 1).general).life == 30

  api-dw-seed : Boolean
  api-dw-seed =
    let base = api-dw-store
    in let refused = handle-game-new base "GET /api/magic/game/new?seed=bad HTTP/1.1"
    in let preserved = disruption-pending ((refused.store).game) & ((((refused.store).game).disruption).window).nonce == (((base.game).disruption).window).nonce
    in let fresh = handle-game-new (refused.store) "GET /api/magic/game/new?seed=123 HTTP/1.1"
    in preserved & (((fresh.store).game).disruption).seed == 123 & (((fresh.store).game).disruption).index == 0
    & disruption-pending ((fresh.store).game) == False

  api-aa-store : MagicStore
  api-aa-store = store-turn (__record-set api-store-automatic "orders" [aa-orders, posture-unattended]) aa-pending True

  api-aa-pending : Boolean
  api-aa-pending =
    let base = api-aa-store
    in let nonce = ((base.turn).commitment).nonce
    in let first = handle-game-state base
    in let second = handle-game-step (first.store) True
    in nonce > 0 & (((second.store).turn).commitment).nonce == nonce
    & (((second.store).turn).commitment).deadline == ((base.turn).commitment).deadline
    & text-contains (first.resp) "\"ability\":true" & text-contains (first.resp) "sacrifice this permanent"
    & ((get-player ((second.store).game) 0).prismatic).rays-used == 0

  api-aa-missing : Boolean
  api-aa-missing =
    let base = api-aa-store
    in let turn = base.turn
    in let missing = ability-turn turn (destroy-permanent (turn.game) 10) True (turn.commitment) (turn.used-abilities)
    in let saved = store-turn base missing True
    in let shown = handle-game-state saved
    in let p = (saved.turn).commitment
    in let refused = handle-game-commitment-target-at saved (p.nonce) True (0 - 3) (p.deadline - 1)
    in let held = handle-game-commitment-target-at (refused.store) (p.nonce) False (0 - 3) (p.deadline - 1)
    in text-contains (shown.resp) "Unavailable permanent" & text-contains (refused.resp) "choose Hold"
    & commitment-pending (((held.store).turn).commitment) == False

  api-aa-timeout : Boolean
  api-aa-timeout =
    let base = api-aa-store
    in let p = (base.turn).commitment
    in let early = handle-game-commitment-target-at base (p.nonce) False (0 - 3) (p.deadline - 1)
    in let late = handle-game-commitment-target-at base (p.nonce) True (0 - 3) (p.deadline)
    in let no-clock = handle-game-commitment-target-at base (p.nonce) True (0 - 3) (0 - 1)
    in tg-has ((early.store).game) 10 & tg-has ((late.store).game) 10 & tg-has ((no-clock.store).game) 10
    & ((get-player ((late.store).game) 0).prismatic).rays-used == 0 & (((late.store).turn).commitment).source < 0

  api-aa-accept : Boolean
  api-aa-accept =
    let base = api-aa-store
    in let p = (base.turn).commitment
    in let first = handle-game-commitment-target-at base (p.nonce) True (0 - 3) (p.deadline - 1)
    in let second = handle-game-commitment-target-at (first.store) (p.nonce) True (0 - 3) (p.deadline - 1)
    in tg-has ((first.store).game) 10 == False & ((get-player ((second.store).game) 0).general).life == 32
    & ((get-player ((second.store).game) 0).prismatic).rays-used == 1 & text-contains (second.resp) "no longer pending"

  api-aa-policy : Boolean
  api-aa-policy =
    let base = api-aa-store
    in let changed = handle-game-sacrifice-policy base "GET /api/magic/game/sacrifice-policy?player=0&enabled=0 HTTP/1.1"
    in let p = ((changed.store).turn).commitment
    in let refused = handle-game-commitment-target-at (changed.store) (p.nonce) True (0 - 3) (p.deadline - 1)
    in let held = handle-game-commitment-target-at (refused.store) (p.nonce) False (0 - 3) (p.deadline - 1)
    in (list-at ((changed.store).orders) 0).sacrifice-pause == False & p.nonce == ((base.turn).commitment).nonce
    & text-contains (refused.resp) "choose Hold" & tg-has ((held.store).game) 10

  api-aa-cancel : Boolean
  api-aa-cancel =
    let base = api-aa-store
    in let cancelled = handle-game-cancel base
    in (cancelled.store).game-active == False & commitment-pending (((cancelled.store).turn).commitment) == False
    & list-length (((cancelled.store).turn).used-abilities) == 0

  api-pa-store : ActivationCost, Effect -> MagicStore
  api-pa-store (cost) (eff) =
    let g = pa-game cost eff
    in __record-set (store-turn api-store-automatic (pa-turn g) True) "templates" (g.templates)

  api-pa-preview : Boolean
  api-pa-preview =
    let base = api-pa-store (pa-cost 1 True False 2) (DealDamage 3)
    in let preview = dispatch-request base "GET /api/magic/game/ability-targets?permanent=10&index=0 HTTP/1.1"
    in let state = handle-game-state (preview.store)
    in text-contains (preview.resp) "\"recommended\":20" & text-contains (preview.resp) "Giant #20"
    & text-contains (state.resp) "Channeler #10: Deal 3 damage" & text-contains (state.resp) "1 ray, tap, pay 2 life"
    & ((get-player ((state.store).game) 0).general).life == 30 & ((get-player ((state.store).game) 0).prismatic).rays-used == 0
    & (bg-perm ((state.store).game) 10).tapped == False

  api-pa-target : Boolean
  api-pa-target =
    let base = api-pa-store (pa-cost 1 True False 2) (DealDamage 3)
    in let bad = dispatch-request base "GET /api/magic/game/activate?permanent=10&index=0&target=11 HTTP/1.1"
    in let unchanged = ((get-player ((bad.store).game) 0).general).life == 30 & ((get-player ((bad.store).game) 0).prismatic).rays-used == 0 & (bg-perm ((bad.store).game) 10).tapped == False
    in let good = dispatch-request (bad.store) "GET /api/magic/game/activate?permanent=10&index=0&target=-2 HTTP/1.1"
    in text-starts-with (bad.resp) "{\"error\"" & unchanged & ((get-player ((good.store).game) 1).general).life == 27
    & ((get-player ((good.store).game) 0).general).life == 28 & ((get-player ((good.store).game) 0).prismatic).rays-used == 1 & (bg-perm ((good.store).game) 10).tapped

  api-pa-refuse : Boolean
  api-pa-refuse =
    let base = api-pa-store (pa-cost 0 True False 0) (GainLife 2)
    in let foreign = dispatch-request base "GET /api/magic/game/activate?permanent=20&index=0 HTTP/1.1"
    in let wrong = dispatch-request (foreign.store) "GET /api/magic/game/activate?permanent=10&index=9 HTTP/1.1"
    in let first = dispatch-request (wrong.store) "GET /api/magic/game/activate?permanent=10&index=0 HTTP/1.1"
    in let second = dispatch-request (first.store) "GET /api/magic/game/activate?permanent=10&index=0 HTTP/1.1"
    in text-starts-with (foreign.resp) "{\"error\"" & text-starts-with (wrong.resp) "{\"error\"" & text-starts-with (second.resp) "{\"error\""
    & ((get-player ((second.store).game) 0).general).life == 32

  api-pa-payment : Boolean
  api-pa-payment =
    let base = api-pa-store (pa-cost 1 True False 0) (GainLife 2)
    in let manual = handle-game-payment-mode base "GET /api/magic/game/payment-mode?player=0&manual=1 HTTP/1.1"
    in let used = dispatch-request (manual.store) "GET /api/magic/game/activate?permanent=10&index=0 HTTP/1.1"
    in ((get-player ((used.store).game) 0).prismatic).rays-used == 1 & ((get-player ((used.store).game) 0).general).life == 32
    & ((used.store).turn).paused & ((used.store).turn).stage == ai-stage-main-1

  api-pa-zero : Boolean
  api-pa-zero =
    let base = api-pa-store (pa-cost 0 False False 30) (GainLife 99)
    in let lost = dispatch-request base "GET /api/magic/game/activate?permanent=10&index=0 HTTP/1.1"
    in let again = dispatch-request (lost.store) "GET /api/magic/game/activate?permanent=10&index=0 HTTP/1.1"
    in ((lost.store).game).game-over & ((lost.store).game).winner == 1 & (lost.store).game-active == False
    & ((get-player ((again.store).game) 0).general).life == 0 & ((again.store).turn).stage == ai-stage-done
    & text-starts-with (again.resp) "{\"error\""

  api-pa-sacrifice : Boolean
  api-pa-sacrifice =
    let base = api-pa-store (pa-cost 1 True True 0) (DealDamage 3)
    in let card = (bg-perm (base.game) 10).card-id
    in let used = dispatch-request base "GET /api/magic/game/activate?permanent=10&index=0&target=20 HTTP/1.1"
    in let repeat = dispatch-request (used.store) "GET /api/magic/game/activate?permanent=10&index=0&target=20 HTTP/1.1"
    in tg-has ((used.store).game) 10 == False & list-at ((get-player ((used.store).game) 0).graveyard) 0 == card
    & (bg-perm ((repeat.store).game) 20).damage-marked == 3 & ((get-player ((repeat.store).game) 0).prismatic).rays-used == 1
    & text-starts-with (repeat.resp) "{\"error\""

  api-starter-error : Boolean
  api-starter-error =
    let tiny = [make-basic-gem 0 "Topaz" 1 8, make-creature 1 "Only spell" (light-cost-y 1) 1 1 0 keywords-none]
    in let store = __record-set magic-store-new "templates" tiny
    in let response = dispatch-request store "GET /api/magic/deck/generate?general=4&seed=99 HTTP/1.1"
    in text-starts-with (response.resp) "{\"error\":\"Starter deck refused"
    & text-contains (response.resp) (general-dawn-commander.name)
    & text-contains (response.resp) "seed 99" & text-contains (response.resp) "32 cards short"
    & text-contains (response.resp) "\"cards\":[" == False

  api-starter-red-pool : Integer, List CardTemplate -> List CardTemplate
  api-starter-red-pool (id) (acc) =
    if id > 9 then acc & [make-basic-gem 10 "Topaz" 1 8]
    else api-starter-red-pool (id + 1) (acc & [make-creature id ("Red spell " & show id) (light-cost-r 1) 1 1 0 keywords-none])

  api-starter-refusal-state : MagicStore, List CardTemplate, Text, Integer -> Boolean
  api-starter-refusal-state (base) (pool) (general) (seed) =
    let store = __record-set base "templates" pool
    in let expired-spell = __record-set ((store.turn).commitment) "deadline" 0
    in let expired-combat = __record-set ((store.turn).combat-choice) "deadline" 0
    in let spell-nonce = ((store.turn).commitment).nonce
    in let combat-nonce = ((store.turn).combat-choice).nonce
    in let next-nonce = store.next-nonce
    in let before = (handle-game-state store).resp
    in let response = dispatch-request store "GET /api/magic/game/new HTTP/1.1"
    in let after = (handle-game-state (response.store)).resp
    in text-starts-with (response.resp) "{\"error\":\"Starter deck refused"
    & text-contains (response.resp) general & text-contains (response.resp) ("seed " & show seed)
    & text-contains (response.resp) "36 cards short" & before == after
    & (((response.store).turn).commitment).nonce == spell-nonce
    & (((response.store).turn).combat-choice).nonce == combat-nonce
    & (((response.store).turn).commitment).deadline == 0
    & (((response.store).turn).combat-choice).deadline == 0
    & (response.store).next-nonce == next-nonce

  api-starter-spell-refusal : Boolean
  api-starter-spell-refusal = api-starter-refusal-state api-commitment-store [pool-ruby] (general-warlord.name) 42

  api-starter-combat-refusal : Boolean
  api-starter-combat-refusal = api-starter-refusal-state api-combat-store (api-starter-red-pool 1 [pool-ruby]) (general-dawn-commander.name) 99

  api-match-targets-base : MagicStore
  api-match-targets-base =
    let base = api-pa-store (pa-cost 0 False False 0) (GainLife 1)
    in let a = __record-set (__record-set (__record-set cb-orders "primary-targets" [PriorityPermanent 20, PriorityGeneral, PriorityType Creature, PriorityThreat 1, PriorityPermanent 21]) "spell-policy" (ConserveMana 2)) "preserved-stones" 5
    in let b = __record-set (ag-posture Control False True) "primary-targets" [PriorityPermanent 10]
    in __record-set (__record-set base "orders" [a, b]) "templates" full-card-pool

  api-match-targets-kept : MagicStore -> Boolean
  api-match-targets-kept (store) =
    let a = list-at (store.orders) 0
    in let targets = a.primary-targets
    in list-length targets == 3 & list-length ((list-at (store.orders) 1).primary-targets) == 0
    & (when list-at targets 0 is PriorityGeneral -> True is otherwise -> False)
    & (when list-at targets 1 is PriorityType (kind) -> kind == Creature is otherwise -> False)
    & (when list-at targets 2 is PriorityThreat (level) -> level == 1 is otherwise -> False)
    & a.stance == Aggressive & a.preserved-stones == 5 & a.combat-pause
    & (when a.spell-policy is ConserveMana (n) -> n == 2 is otherwise -> False)
    & (list-at (store.orders) 1).stance == Control

  api-match-targets-new : Boolean
  api-match-targets-new =
    let base = api-match-targets-base
    in let fresh = dispatch-request base "GET /api/magic/game/new HTTP/1.1"
    in api-match-targets-kept (fresh.store) & (fresh.store).game-active
    & ((fresh.store).turn).stage == ai-stage-upkeep & text-starts-with (fresh.resp) "{\"error\"" == False
    & list-length ((list-at (base.orders) 0).primary-targets) == 5

  api-match-targets-cancel : Boolean
  api-match-targets-cancel =
    let base = api-match-targets-base
    in let stopped = dispatch-request base "GET /api/magic/game/cancel HTTP/1.1"
    in api-match-targets-kept (stopped.store) & (stopped.store).game-active == False
    & list-length ((list-at (base.orders) 0).primary-targets) == 5

  api-match-targets-refuse : Boolean
  api-match-targets-refuse =
    let base = __record-set api-match-targets-base "templates" [pool-ruby]
    in let before = (handle-game-state base).resp
    in let refused = dispatch-request base "GET /api/magic/game/new HTTP/1.1"
    in let after = (handle-game-state (refused.store)).resp
    in before == after & text-starts-with (refused.resp) "{\"error\""
    & list-length ((list-at ((refused.store).orders) 0).primary-targets) == 5
    & list-length ((list-at ((refused.store).orders) 1).primary-targets) == 1

  api-cancel-spell : Boolean
  api-cancel-spell =
    let base = api-target-decision 3
    in let nonce = ((base.turn).commitment).nonce
    in let expired = __record-set ((base.turn).commitment) "deadline" 0
    in let stopped = dispatch-request base "GET /api/magic/game/cancel HTTP/1.1"
    in let late = handle-game-commitment-at (stopped.store) nonce True 0
    in (stopped.store).game-active == False & ((stopped.store).game).game-over == False
    & list-length (((stopped.store).game).battlefield) == 0 & list-length (((stopped.store).game).card-templates) == 0
    & (((stopped.store).turn).commitment).card < 0 & text-starts-with (late.resp) "{\"error\""

  api-cancel-combat : Boolean
  api-cancel-combat =
    let base = api-combat-store
    in let nonce = ((base.turn).combat-choice).nonce
    in let expired = __record-set ((base.turn).combat-choice) "deadline" 0
    in let stopped = dispatch-request base "GET /api/magic/game/cancel HTTP/1.1"
    in let late = handle-game-combat-choice-at (stopped.store) nonce True 0
    in (stopped.store).game-active == False & (((stopped.store).turn).combat-choice).pending == False
    & ((stopped.store).turn).paused == False & list-length (((stopped.store).game).battlefield) == 0
    & text-starts-with (late.resp) "{\"error\""

  api-cancel-orders : Boolean
  api-cancel-orders =
    let base = api-combat-store
    in let nonce = base.next-nonce
    in let first = dispatch-request base "GET /api/magic/game/cancel HTTP/1.1"
    in let second = dispatch-request (first.store) "GET /api/magic/game/cancel HTTP/1.1"
    in let step = handle-game-step (second.store) True
    in (second.store).next-nonce == nonce & (list-at ((second.store).orders) 0).combat-pause
    & (second.store).game-active == False & ((second.store).game).game-over == False
    & text-starts-with (step.resp) "{\"error\""

  api-cancel-finished : Boolean
  api-cancel-finished =
    let base = api-pa-store (pa-cost 0 False False 30) (GainLife 99)
    in let lost = dispatch-request base "GET /api/magic/game/activate?permanent=10&index=0 HTTP/1.1"
    in let stopped = dispatch-request (lost.store) "GET /api/magic/game/cancel HTTP/1.1"
    in let reload = handle-game-state (stopped.store)
    in ((lost.store).game).game-over & ((reload.store).game).game-over == False
    & (reload.store).game-active == False & text-contains (reload.resp) "\"game-active\":false"
    & text-contains (reload.resp) "\"game-over\":false" & (reload.store).num-accounts == (lost.store).num-accounts

  api-dv-request : MagicStore, Text, Text -> Text
  api-dv-request (store) (query) (cards) =
    (dispatch-request store ("GET /api/magic/deck/validate?" & query & "&cards=" & cards & " HTTP/1.1")).resp

  api-dv-csv : List Integer -> Text
  api-dv-csv (cards) = int-list-to-str cards 0 (list-length cards) ""

  api-dv-result : Text, Boolean, Text -> Boolean
  api-dv-result (response) (valid) (reason) =
    response == ("{\"valid\":" & jbool valid & ",\"reason\":" & jstr reason & "}")

  api-dv-valid : Boolean
  api-dv-valid =
    let store = magic-store-new
    in let basics = api-dv-csv (add-n-copies [] 0 60)
    in let generated = when gen-starter-deck general-dawn-commander full-card-pool 99
      is StarterRefused (why) -> False
      is StarterDeck (deck) -> api-dv-result (api-dv-request store "general=4" (api-dv-csv (deck.cards))) True ""
    in generated & api-dv-result (api-dv-request store "general=4&format=Standard" basics) True ""
    & api-dv-result (api-dv-request store "general=4" (basics & ",0")) True ""
    & api-dv-result (api-dv-request store "general=4" (text-replace basics "," "%2C")) True ""
    & api-dv-result (api-dv-request store "general=4" (text-replace basics "," "%2c")) True ""

  api-dv-copies : Boolean
  api-dv-copies =
    let cards = add-n-copies (add-n-copies [] 0 55) 37 5
    in let response = api-dv-request magic-store-new "general=4" (api-dv-csv cards)
    in api-dv-result response False "Iron Golem appears 5 times, max is 4"

  api-dv-size-color : Boolean
  api-dv-size-color =
    let store = magic-store-new
    in let basics = api-dv-csv (add-n-copies [] 0 59)
    in api-dv-result (api-dv-request store "general=4" basics) False "Deck has 59 cards, minimum is 60"
    & api-dv-result (api-dv-request store "general=4" "") False "Deck has 0 cards, minimum is 60"
    & api-dv-result (api-dv-request store "general=4" (basics & ",7")) False "Ruby Golem is outside General's color identity"

  api-dv-inputs : Boolean
  api-dv-inputs =
    let store = magic-store-new
    in api-dv-result (api-dv-request store "general=4" "0,,1") False "Invalid card template ID at position 2: "
    & api-dv-result (api-dv-request store "general=4" "0,abc") False "Invalid card template ID at position 2: abc"
    & api-dv-result (api-dv-request store "general=4" "-1") False "Invalid card template ID at position 1: -1"
    & api-dv-result (api-dv-request store "general=4" "00") False "Invalid card template ID at position 1: 00"
    & api-dv-result (api-dv-request store "general=4" "999999999999") False "Invalid card template ID at position 1: 999999999999"
    & api-dv-result (api-dv-request store "general=4" "0,") False "Invalid card template ID at position 2: "
    & api-dv-result (api-dv-request store "general=4" "999999999") False "Unknown card template ID: 999999999"
    & api-dv-result (api-dv-request store "general=999" "0") False "Unknown General: 999"
    & api-dv-result (api-dv-request store "general=oops" "0") False "Unknown General: oops"
    & api-dv-result (api-dv-request store "general=4&format=draft" "0") False "Unsupported deck format: draft; expected Standard"

  api-dv-types : Boolean
  api-dv-types =
    let token = __record-set (make-basic-gem 0 "Token \"Gem\"" 0 1) "is-token" True
    in let token-store = __record-set magic-store-new "templates" [token]
    in let general = __record-set (make-basic-gem 0 "Command Gem" 0 1) "card-type" General
    in let general-store = __record-set magic-store-new "templates" [general]
    in let nul = char-to-text (code-to-char 0)
    in let control = __record-set (make-basic-gem 0 ("Null" & nul & "\\\nGem") 0 1) "is-token" True
    in let control-store = __record-set magic-store-new "templates" [control]
    in let cards = api-dv-csv (add-n-copies [] 0 60)
    in api-dv-request token-store "general=4" cards == "{\"valid\":false,\"reason\":\"Token \\\"Gem\\\" is a token and cannot appear in the deck\"}"
    & api-dv-result (api-dv-request general-store "general=4" cards) False "Command Gem is a General and cannot appear in the deck"
    & api-dv-request control-store "general=4" cards == "{\"valid\":false,\"reason\":\"Null\\u0000\\\\\\nGem is a token and cannot appear in the deck\"}"

  api-j-text : Text
  api-j-text = "quote\" slash\\ line\nnull" & char-to-text (code-to-char 0) & " café À Ω 中"

  api-j-value : Text -> JsonValue
  api-j-value (body) = when json-parse body is Just (value) -> value is None -> JsonNull

  api-j-first : JsonValue -> JsonValue
  api-j-first (value) = when json-array-at value 0 is Just (item) -> item is None -> JsonNull

  api-j-array : JsonValue, Text -> JsonValue
  api-j-array (value) (key) = when json-get value key is Just (items) -> api-j-first items is None -> JsonNull

  api-j-accounts : Boolean
  api-j-accounts =
    let text = api-j-text
    in let account = set-account-session (set-account-admin (account-new 0 "json-user" text text 3 SubFree) True) "session"
    in let store = __record-set (__record-set magic-store-new "accounts" [account]) "num-accounts" 1
    in let err = api-j-value ((magic-respond-error store text).resp)
    in let me = api-j-value ((handle-me store "GET /api/auth/me?t=session HTTP/1.1").resp)
    in let profile = api-j-value ((handle-web-profile store "GET /api/auth/profile?t=session HTTP/1.1").resp)
    in let dumped = api-j-array (api-j-value ((handle-state-dump store).resp)) "accounts"
    in let admin = api-j-array (api-j-value ((handle-admin-users store).resp)) "users"
    in json-get-str err "error" "" == text & json-get-str me "display" "" == text
    & json-get-str profile "display" "" == text & json-get-bool profile "admin" False
    & json-get-str dumped "Display" "" == text & json-get-str dumped "Password" "" == text
    & json-get-bool dumped "Admin" False & json-get-bool dumped "Banned" True == False
    & json-get-str admin "display" "" == text & json-get-bool admin "admin" False

  api-j-auth : Boolean
  api-j-auth =
    let display = "café\"\\Ω中"
    in let created = handle-register (magic-store-keyed magic-store-new [1, 2, 3, 4]) ("GET /api/auth/register?u=json-user&d=" & display & "&p=dummy-pass HTTP/1.1")
    in let registered = api-j-value (created.resp)
    in let logged = api-j-value ((handle-login (created.store) "GET /api/auth/login?u=json-user&p=dummy-pass HTTP/1.1").resp)
    in json-get-str registered "display" "" == display & json-get-bool registered "admin" False
    & json-get-bool registered "starterGrant" False & json-get-int registered "starterBalance" 0 == 500
    & json-get-str logged "display" "" == display & json-get-str logged "token" "" /= ""

  api-j-names : Boolean
  api-j-names =
    let text = api-j-text
    in let template = make-creature 0 text light-cost-zero 1 1 0 keywords-none
    in let general = __record-set general-warlord "name" text
    in let card = api-j-first (api-j-value ("[" & templates-to-json-str [template] 0 1 "" & "]"))
    in let gen = api-j-first (api-j-value ("[" & generals-list-to-json [general] 0 1 "" & "]"))
    in json-get-str card "name" "" == text & json-get-str gen "name" "" == text
