#nullable enable
using System.Collections.Immutable;
using System;
using System.Drawing;
using System.Windows.Forms;
using System.Collections.Generic;
using System.Threading;

namespace Uoaix.Assist;

public sealed partial class AssistWindow
{
    void BuildCircuits()
    {
        // The tailoring circuit (Damian, 2026-10-17: "make a new tab in uoaix assist for this that shows the details of
        // the job, vendors state, sales totals, thread buys etc."): the job and the pack, each thread vendor's stock
        // and restock, and the thread bought and shirts sold this session and in all.
        // The circuit's setup (Damian, 2026-10-17: "the circuit tab in the assist UI should give me select the circuit,
        // configure its properties, and launch in all its modes, with helpful tips"; "for each, let me pick the list of
        // shops to visit in case we need to adjust on the fly"): the circuit, its lists, its shops by name, its modes.
        // Its own group above the setup; "General" is the Info page's group, and a "Circuit " title puts it on the
        // Circuits tab.
        Panel g = Group("Circuit general settings");
        m_circGeneralGroup = g;
        // The settings every circuit shares, above the circuit picker (Damian, 2026-10-18: "put general settings for
        // circuits like bank gold over and keeping above the selector for the circuit"): the bank's gold amounts ("need
        // a configurable bank gold amount control in ui").
        FieldGrid gen = Grid(g);
        gen.Field("Bank gold over", Num(0, 60000, m_f.BankOver, v => m_f.BankOver = v, 100),
            "A circuit walks to a bank only to deposit when the pack holds more gold than this (after a sale, before "
            + "a wait).");
        gen.Field("Keep in pack", Num(0, 60000, m_f.PurseKeep, v => m_f.PurseKeep = v, 50),
            "The gold left in the pack at every deposit.");
        gen.Field("Recall over (ms)",
            Num(0, TimingCatalog.MaxMs, m_f.Timings.Ms("recall-walk"), v => m_f.Timings.Set("recall-walk", v), 1000),
            "Recall to a rune instead of walking when the walk runs over this. A recall to a rune in the pack "
            + "replaces a walk only when the walk would run longer than this many ms (the straight line at the step "
            + "pace) and the rune lands nearer the target: shops a short run apart are walked. The rune's landing is "
            + "learned at its first recall; until then the shop its label names.");
        CheckBox sellOff = gen.Wide(new CheckBox { Text = "Sell off: take any offer at the sell list's lowest price",
            AutoSize = true, Checked = m_f.SellOff },
            "While ticked, a circuit sells at any offer at or over the sell list's own lowest price, instead of "
            + "holding out for the best price of the last 7 days or the survey's.");
        sellOff.CheckedChanged += (s, e) => m_f.SellOff = sellOff.Checked;
        g = Group("Circuit setup");
        // What starts, stops or steps a circuit is on the Circuit job card (Damian, 2026-10-24: "move all the
        // start/stop controls and their cousins that engage action to the Circuit Job card and out of Circuit setup").
        FieldGrid setup = Grid(g);
        List<Control> circRow = new();
        List<Control> jobRow = new();
        m_circPick = Pick(130, "Tailoring", "Buy reagents", "Survey town", "Refresh", "Cave mining", "Smithing",
            "Buy ingots", "Advertise", "Spellbooks", "Mark runes", "Lumberjacking", "Feathers", "Reagents");
        m_circPick.SelectedIndex = Math.Max(0, s_circuitKeys.IndexOf(m_f.Cfg.Get("circuit.selected", "tailor")));
        circRow.Add(m_circPick);
        m_tipper.SetToolTip(m_circPick,
            "Tailoring buys thread, weaves, sews and sells shirts. Buy reagents buys the shopping list at the picked "
            + "shops and stores it in the bank. Survey town reads every vendor's stock in a town and buys nothing. "
            + "Refresh buys food, eats until full, feeds the mount, banks the gold and refills the reagent loadout. "
            + "Buy ingots buys iron ingots at the smith shops and stores them in the bank. Advertise buys blank "
            + "books, writes the ad in them and puts them down at the banks, shops and spots listed. Spellbooks buys "
            + "spellbooks and the scrolls they lack at the town's mage shops, inscribes what your skill makes without "
            + "failing much, fills the books and stocks them in the bank.");
        Action<string, string, Func<string?>> launch = (text, tip, run) =>
        {
            Button b = Btn(text,
                () => { string? w = run(); m_circSetupNote.Text = w == null ? text + ": started." : text + ": " + w; });
            m_tipper.SetToolTip(b, tip);
            jobRow.Add(b);
            m_circLaunch = m_circLaunch.Set(text, b);
        };
        // One Go for the circuit picked, and a circuit's own extra buttons only while it is picked (Damian, 2026-10-18:
        // "pick a circuit from the drop down by profession, then if that profession has sub options like tailor, they
        // show up, other wise, go").
        launch("Go",
            "Start the circuit picked. Tailoring: sell the shirts, buy thread, weave, sew, and sell trips. Buy "
            + "reagents: the shopping list at the picked shops, into the bank. Survey town: every ticked shop's "
            + "stock, nothing bought. Refresh: food, the mount fed, the gold banked, the loadout refilled. Cave "
            + "mining: the cave picked below (or where you stand), dug by 1 or 2 shovels at targets picked and walked "
            + "to on their own, smelted at the forge set up below, the ingots stored. Smithing: at the anvil you "
            + "stand by, the ladder's item for your skill, sold to the smiths in reach, until 100.0.",
            () =>
            {
                Map<string, Func<string?>> starts = Map<string, Func<string?>>.Of(
                    ("tailor", m_f.CircuitGo), ("buy", m_f.CircuitBuy), ("survey", m_f.CircuitSurvey),
                    ("refresh", m_f.CircuitRefresh), ("mine", m_f.CircuitMine), ("smith", m_f.CircuitSmith),
                    ("ingots", m_f.CircuitBuyIngots), ("advert", m_f.CircuitAdvert),
                    ("spellbook", m_f.CircuitSpellbooks), ("mark", m_f.CircuitMark),
                    ("lumber", m_f.CircuitLumber), ("feathers", m_f.CircuitFeathers),
                    ("reagents", m_f.CircuitReagents));
                return starts[CircuitPicked]!();
            });
        // Lumberjacking's two starts (Damian, 2026-10-22: "we need a resume and new chop to the lumberjack circuit"),
        // in place of Go while it is picked.
        launch("Resume",
            "Go on: the piles at your feet are kept and the collection still counts them; Lumberjacking keeps its "
            + "path too.", () => (CircuitPicked == "feathers" ? m_f.CircuitFeathers() : m_f.CircuitLumber())!);
        // Feathers' fresh start, as New chop (AssistHarvest.md stage 3).
        launch("New hunt",
            "Start afresh from where you stand: the piles at your feet are forgotten and Feathers to, the collection "
            + "point, becomes your tile. The blade, At my feet, Range and Collect at are kept. Asks first when piles "
            + "are kept.",
            () =>
            {
                int piles = m_f.FeatherPileCount();
                if (piles > 0
                    && MessageBox.Show(this,
                    "New hunt forgets the " + m_f.FeatherDropped() + " feathers in " + piles
                    + (" piles at your feet: they stay on the ground and the collection no longer carries "
                    + "them.\n\nCollect them first? Cancel here, then Collect now."), "New hunt",
                    MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK)
                    return "not started";
                return m_f.CircuitFeathersNew()!;
            });
        launch("New chop",
            "Start afresh from where you stand: everything but the axe is cleared (the path, the piles at your feet, "
            + "the sentinel, the empty chunks, Chop from), and Logs to, the collection point, becomes your tile. At "
            + "my feet, Range and Collect at are kept. Asks first; Collect now before it carries the piles to the old "
            + "Logs to.",
            () =>
            {
                int piles = m_f.LumberPiles().Count;
                string clears = "New chop clears the path, the sentinel, the empty chunks and Chop from, and Logs to "
                    + "becomes the tile you stand on.";
                string ask = piles > 0
                    ? clears + " It forgets the " + m_f.LumberDropped() + " logs in " + piles
                    + (" piles at your feet: they stay on the ground and the collection no longer carries "
                    + "them.\n\nCollect them first? Cancel here, then Collect now.") : clears + " Start afresh?";
                if (MessageBox.Show(this, ask, "New chop", MessageBoxButtons.OKCancel,
                    piles > 0 ? MessageBoxIcon.Warning : MessageBoxIcon.Question) != DialogResult.OK)
                    return "not started";
                string? w = m_f.CircuitLumberNew();
                if (w == null)
                {
                    lock (m_trail)
                        m_trail.Clear();
                    if (m_mapView != null)
                        m_mapView.Invalidate();
                }
                return w!;
            });
        launch("Sell only",
            "Sell the shirts in the pack at the picked tailor shops and bank the gold; nothing else is bought.",
            m_f.CircuitSell);
        launch("Tour",
            "Visit every picked tailor shop once: one shirt to each tailor that buys, and all its thread. The report "
            + "is in the job notes.", m_f.CircuitTour);
        launch("Buy thread",
            "Visit every tailor whose thread is due, city by city, buy all its thread, and bank the thread in each "
            + "city before leaving it. Nothing is made or sold.", m_f.CircuitThread);
        launch("Keep thread",
            "Keep every thread vendor of the approved shops bought out, until stopped: the city whose vendor was "
            + "asked longest ago next, every vendor there asked (none is an answer, asked again next lap), walked "
            + "between shops in Jhelom and Britain. The purse from the bank box's top-level gold, the thread past the "
            + "carry limit stashed out of sight in the shop and fetched later, the pack's thread put on the house "
            + "pile when nearly full.", m_f.CircuitKeep);
        Button stop = Btn("Stop", () => { m_f.CircuitHalt("stop asked"); m_circSetupNote.Text = "Stop asked."; });
        jobRow.Add(stop);
        setup.Line("Circuit", Strip(circRow.ToArray()));
        // The resets on a row of their own under the circuit row (Damian, 2026-10-18: "the buttons in the circuit setup
        // section paint window, need to put reinit and the others after it below in second row").
        Button reinit = Btn("Reinitialize",
            () => { m_f.Reinitialize(); m_circSetupNote.Text = "Circuit state reinitialized: every wait and block "
            + "cleared."; });
        m_tipper.SetToolTip(reinit,
            "The circuit's state: vendors, banks and shops passed over (no answer, unreachable, under the floor, "
            + "full), the repeat guard on a line that got no answer, the spare kit's wait.");
        Button resetOurs = Btn("Reset",
            () => { m_f.ResetOurs(); m_circSetupNote.Text = "Tools and bags reset: pick them again."; });
        m_tipper.SetToolTip(resetOurs,
            "Our own things: the pack's tool bag, the bank's toolbag, the bank bags picked (store, city runes, shop "
            + "runes), and the labels read off our runes.");
        Button rediscover = Btn("Rediscover",
            () => { if (MessageBox.Show(this,
            "Forget every NPC, vendor stock list and place (anvils, forges, looms, houses), the banks, the vendors' "
            + "homes, the dead teleporters and the tailors' stock? They are seen again as you go.", "Rediscover",
            MessageBoxButtons.OKCancel)
            != DialogResult.OK) return; m_f.Rediscover(); m_circSetupNote.Text = "The world forgotten, to be seen "
            + "again."; });
        setup.Line("Clear", Strip(reinit, resetOurs, rediscover));
        // Training (AssistCircuitBrain.md, Training): a stop of the Tailoring circuit is a critique of its step.
        CheckBox training = setup.Wide(new CheckBox { Text = "Training: when I stop Tailoring, learn from my [ "
            + "comment and my next moves", AutoSize = true, Checked = m_f.Training },
            "While ticked, stopping the Tailoring circuit yourself (a step of your own, Stop, or circuit stop; never "
            + "an alarm) logs the step it was running as rejected and starts Record: say [ and what was wrong or what "
            + "would be better, then play the better move by hand. Go ends the critique and its recording. Everything "
            + "goes to decisions-<server>.csv for training.");
        training.CheckedChanged += (s, e) => m_f.Training = training.Checked;
        // Tailoring's only (Damian, 2026-10-20: "the training button for tailoring should be collapsed and hidden in
        // other circuits"): shown while Tailoring is the circuit picked (FillCircuitSetup).
        m_setupGrid = setup;
        m_trainingBox = training;
        // Suggest (AssistFeatures.Suggest): each step the circuit would take waits for Do it or Skip.
        CheckBox suggest = new()
        {
            Text = "Suggest",
            AutoSize = true,
            Checked = m_f.Suggest
        };
        Button suggestDo = Btn("Do it", () => m_f.SuggestAnswer(true));
        Button suggestSkip = Btn("Skip", () => m_f.SuggestAnswer(false));
        m_suggestNote = Words("");
        suggest.CheckedChanged += (s, e) => m_f.Suggest = suggest.Checked;
        FlowLayoutPanel suggestLine = Strip(suggest, suggestDo, suggestSkip, m_suggestNote);
        m_tipper.SetToolTip(suggest,
            "While ticked, the circuit stops before every step it would take and names it here with your pack's "
            + "counts: Do it runs it, Skip refuses it and the next step it would take is offered. Every answer goes "
            + "to decisions-<server>.csv for the brain's training.");
        // Collapsed, the setup names the circuit picked ("put slected circuit in collapsed expando for setup").
        Summarize(g, () => m_circPick.SelectedItem as string ?? "");
        m_tipper.SetToolTip(rediscover,
            "The world: every NPC and vendor stock list, every place (anvils, forges, looms, houses, guildstones), "
            + "the bank signs and banks that answered, the vendors' homes, the dead teleporters and refused "
            + "Teleports, the shops looked in and surveyed, each tailor's stock. The trades and the learned restocks "
            + "stay.");
        m_tipper.SetToolTip(stop,
            "Stops the running circuit after its current step. Taking a step yourself also stops it.");
        // The mining circuit's setup in the window (Damian, 2026-10-18: "where is the cave miner circuit?"; "need a
        // drop down for 1 or 2 shovel digging, a tool bag selection"; "a list of caves to choose from that can be
        // configed on the fly "mark cave here""), each step run off the window's thread and its answer shown in the
        // note.
        Action<string, Func<string>> mineDo = (text, run) =>
        {
            m_circSetupNote.Text = text + "...";
            ThreadPool.QueueUserWorkItem(_ => { string w = run(); BeginInvoke(new Action(() => { m_circSetupNote.Text = text
                + ": "
                + w; FillCaves(); })); });
        };
        FieldGrid mine = Own("mine", Grid(g));
        ComboBox shovelMode = mine.Field("Shovels", Pick(85, "1 shovel", "2 shovels"),
            "1 shovel: one dig at a time. 2 shovels: two digs in flight, each in its own 8x8 chunk, or two spots in "
            + "one chunk when only one has metal; a shovel is never used while its own dig is in flight. Targets are "
            + "picked and walked to on their own.");
        shovelMode.SelectedIndex = m_f.MineMode == 1 ? 0 : 1;
        shovelMode.SelectedIndexChanged += (s, e) => m_f.MineMode = shovelMode.SelectedIndex == 0 ? 1 : 2;
        ComboBox storePick = mine.Field("Storage",
            Pick(130, "Backpack", "Ingot stash", "House vendor (for sale)", "Targeted place"),
            "Backpack: kept in the pack while mining; the circuit ends when the pack is full. Ingot stash: onto the "
            + "stash targeted below, in the house (holds more, not secured) or in your bank box (secured, holds less; "
            + "reached by the bank rune, never walked to). House vendor: put up for sale on your vendor (Houses tab, "
            + "House vendor) in piles of up to 1000 at the price per ingot below; secured, holds less, and the stash "
            + "takes the rest when it is full. Targeted place: wherever Store to targeted, a tile, a bag, a pile or a "
            + "pack animal; a pack animal beside you takes them where you stand.");
        storePick.SelectedIndex = m_f.MineStorage == "stash" ? 1
            : m_f.MineStorage == "vendor" ? 2 : m_f.MineStorage == "target" ? 3 : 0;
        storePick.SelectedIndexChanged += (s, e) =>
            mineDo("Storage",
            () => m_f.MineSetup(new[] { "mine-setup", "storage",
            storePick.SelectedIndex == 1 ? "stash"
            : storePick.SelectedIndex == 2 ? "vendor" : storePick.SelectedIndex == 3 ? "target" : "backpack" }));
        Button storeTo = m_storeToButton = Btn("Store to",
            () => { string? w = m_f.MineStoreToTarget(); m_circSetupNote.Text = w == null
            ? "Target where the ingots go: a tile, a bag, a pile, the floor or a pack animal." : "Store to: " + w; });
        m_tipper.SetToolTip(storeTo,
            "Where the Targeted place storage puts the ingots: a tile (they pile there), a bag, a pile, the floor, or "
            + "a pack animal.");
        m_storeToWords = Words(m_f.MineStoreToText());
        mine.Line("Store to", Strip(storeTo, m_storeToWords));
        // The lumberjack's At my feet and collection for the ore (MineGather).
        Button oreTo = m_oreToButton = Btn("Ore to",
            () => { string? w = m_f.MineOreToTarget(); m_circSetupNote.Text = w == null
            ? "Target where the ore piles are collected: a tile, a bag or a pile." : "Ore to: " + w; });
        m_tipper.SetToolTip(oreTo,
            "Where the collection carries the ore piles left at your feet: a tile (they pile there), a bag or a pile, "
            + "not a pack animal.");
        CheckBox oreFeet = new()
        {
            Text = "At my feet",
            AutoSize = true,
            Checked = m_f.MineAtFeet
        };
        oreFeet.CheckedChanged += (s, e) => { m_f.MineAtFeet = oreFeet.Checked; m_circSetupNote.Text = m_f.MineOreText()
            + "."; };
        m_tipper.SetToolTip(oreFeet,
            "Each full pack's ore goes on a tile beside you, onto an ore pile there if one lies there, and the digging "
            + "goes on: no smelting and no storage. At Collect at, and when every chunk is empty, the collection "
            + "carries the piles to Ore to. Unticked, a full pack is smelted at the forge and stored.");
        m_oreWords = Words(m_f.MineOreText());
        mine.Line("Ore to", Strip(oreTo, oreFeet, m_oreWords));
        mine.Field("Collect at (ore)", Num(1, 60000, m_f.MineCollectAt, v => m_f.MineCollectAt = v),
            "With At my feet, the ore dropped in piles that starts the collection to Ore to; then the digging goes "
            + "on.");
        Button oreCollectNow = Btn("Collect now",
            () => { string? w = m_f.MineCollectNow(); m_circSetupNote.Text = w
            ?? (m_f.CircuitRunning ? "Collecting the ore at the next step, then digging."
            : "Collecting the ore piles."); });
        m_tipper.SetToolTip(oreCollectNow,
            "Collect the ore piles at feet now, whatever Collect at says: during cave mining at its next step, once "
            + "the digs in flight are answered, then the digging goes on; with no circuit running, the collection "
            + "alone.");
        mine.Line("Collection", Strip(oreCollectNow));
        mine.Field("Vendor price (gp an ingot)", Num(0, 1000, m_f.MineVendorPrice, v => m_f.MineVendorPrice = v),
            "The house vendor storage's price per ingot; a pile of n ingots is priced n times this. 0: not set, and "
            + "the circuit refuses to start with vendor storage.");
        Button stashSet = m_stashButton = Btn("Ingot stash",
            () => { string? w = m_f.StashTarget(); m_circSetupNote.Text = w == null
            ? ("Target the floor, an ingot pile or a bag inside the house, or an ingot pile or a container in your "
            + "open bank box.") : "Ingot stash: " + w; });
        m_tipper.SetToolTip(stashSet,
            "Where the Ingot stash storage puts the ingots, and where they go when the vendor takes no more: target "
            + "the floor, an ingot pile or a bag inside the house, or an ingot pile or a container in your bank box "
            + "(open the bank box first). The floor: piled on that tile. A pile is stacked onto up to 60,000, then a "
            + "new pile beside it (in its container when it is in one). Unset: the house floor by the east wall. A "
            + "stash in the bank box that takes no more sends the rest to the house floor.");
        m_stashWords = Words(m_f.StashText());
        mine.Line("Ingot stash",
            Strip(stashSet, Btn("Clear", () => { m_f.StashClear(); m_circSetupNote.Text = m_f.StashStatus; }),
            m_stashWords));
        mine.Field("Carry shovels", Num(1, 50, m_f.MineCarry, v => m_f.MineCarry = v),
            "Shovels bought at the tinker shops of the bank rune's town, up to this many and the pack's weight, with "
            + "14 gp each taken from the bank first.");
        // When the ingots go to storage (Damian, 2026-10-18: "should allow configure of store amount trigger").
        mine.Field("Store at (ingots)", Num(1, 60000, m_f.MineStoreAt, v => m_f.MineStoreAt = v),
            "Ingots in the pack that send a trip to the storage (the ingot stash or the vendor); a full pack goes "
            + "sooner.");
        Button forgeSet = Btn("Forge", () => mineDo("Forge", () => m_f.MineSetup(new[] { "mine-setup", "forge" })));
        m_mineForgeButton = forgeSet;
        m_tipper.SetToolTip(forgeSet, "Records the forge you can see (within 12 tiles); stand where it is in view.");
        // The bank rune by a target cursor (Damian, 2026-10-18: "bank rune should be a targeting selection").
        Button bankRune = Btn(m_f.MineBankRune.Length > 0 ? "Reset bank rune: " + m_f.MineBankRune : "Bank rune: pick",
            () => { string? w = m_f.BankRuneTarget(); m_circSetupNote.Text = w == null
            ? "Target the rune in your pack that recalls to the bank." : "Bank rune: " + w; });
        m_tipper.SetToolTip(bankRune,
            "Target the rune in your pack that recalls to the bank town; its label is read and kept. An ingot stash "
            + "in the bank box and buying shovels both travel by it.");
        m_bankRuneButton = bankRune;
        Button mineShow = Btn("Show", () => mineDo("Mining setup", () => m_f.MineSetup(new[] { "mine-setup" })));
        m_tipper.SetToolTip(mineShow, "The mining setup as it stands.");
        mine.Line("Set", Strip(forgeSet, bankRune, mineShow));
        m_caveList = Pick(130);
        m_caveList.SelectedIndexChanged += (s, e) => { if (m_fillingCaves) return; m_f.PickedCave = m_caveList.SelectedIndex
            <= 0
            ? ""
            : (string?)m_caveList.SelectedItem; m_caveRune.Text = CaveRuneText(); FillCircuit(); };
        m_tipper.SetToolTip(m_caveList,
            "The cave the circuit mines. Where I start: within 24 tiles of where you press Go, nothing recorded.");
        TextBox caveName = new()
        {
            Width = 70
        };
        m_tipper.SetToolTip(caveName, "A name for the cave you stand in.");
        Button caveMark = Btn("Mark cave here",
            () => { string? name = caveName.Text.Trim().Length > 0 ? caveName.Text.Trim()
            : m_f.PickedCave; mineDo("Mark cave", () => m_f.MarkCave(name!, 0)); });
        m_tipper.SetToolTip(caveMark,
            "Records the cave here under the name typed (or moves the picked cave here), with who is near. Its seams "
            + "and visitors are recorded while the circuit mines it.");
        // The cave's rune by a target cursor (Damian, 2026-10-18: "rune to cave area should be target invoking
        // button").
        m_caveRune = Btn(CaveRuneText(),
            () => { string? w = m_f.CaveRuneTarget(); m_circSetupNote.Text = w == null
            ? "Target the rune back to the cave " + m_f.PickedCave + " (in your pack or the open bank box)."
            : "Cave rune: " + w; });
        m_tipper.SetToolTip(m_caveRune,
            "Target the rune back to the picked cave, in your pack or the open bank box; its label is read and kept. "
            + "The circuit walks into the cave from where the rune lands.");
        Button caveForget = Btn("Forget",
            () => { if (m_f.PickedCave!.Length
            > 0) { m_circSetupNote.Text = m_f.ForgetCave(m_f.PickedCave); FillCaves(); } });
        m_tipper.SetToolTip(caveForget, "Removes the picked cave and its records from the list.");
        // Three lines, the rune's label cut to the button (Damian, 2026-10-20: "the cave details row underflows
        // available screen space on the right side").
        m_caveRune.AutoSize = false;
        m_caveRune.AutoEllipsis = true;
        m_caveRune.Size = new Size(240, 26);
        mine.Line("Cave", Strip(m_caveList, caveForget));
        mine.Line("New cave", Strip(caveName, caveMark));
        mine.Line("Cave rune", m_caveRune);
        FillCaves();
        // The picked cave's seams, one row per 8x8 chunk dug (Damian, 2026-10-18: "keep track of each harvest region in
        // a grid in the ui and show last dig, last empty message and likely respawn based on evidence").
        m_mineZones = Own("mine",
            Add(g, new SortList { View = View.Details, Height = 130, FullRowSelect = true, GridLines = true,
            AutoSort = false }));
        foreach (string c in new[] { "Chunk:70", "Ore:40", "Out:40", "Last dig:70", "Back:70", "Last empty:150",
            "Evidence:150" })
        {
            string[] a = c.Split(':');
            m_mineZones.Columns.Add(a[0], int.Parse(a[1]));
        }
        m_tipper.SetToolTip(m_mineZones,
            "Each 8x8 chunk of the picked cave: its ore digs, how often it ran out, the last dig and \"no metal\" "
            + "answer, and when it likely has metal again from the regrowth seen in this cave.");
        // The lumberjack's setup (Damian, 2026-10-22: "a button is needed to say where the logs go, an axe in bag or
        // hand").
        FieldGrid lumber = Own("lumber", Grid(g));
        GatherRow lumberSet = GatherSet(lumber, "Axe", "Target the axe in your pack or your hand.",
            "Target the axe that chops, in your pack or your hand: any axe. It goes into your hand to chop, and "
            + "whatever you hold goes into the pack.", m_f.LumberAxeTarget,
            "Logs to", "Target where the logs go: a tile, a bag, a pile, the floor or a pack animal.",
            "Target where the logs go: a tile (they pile there), a bag, a pile, the floor, or a pack animal. Before "
            + "the pack is full the circuit walks there and puts every log down; a pack animal beside you takes them "
            + "where you stand.", m_f.LumberLogsTarget, m_f.LumberLogsText,
            () => m_f.LumberAtFeet, v => m_f.LumberAtFeet = v,
            ": each full pack goes on a pile where you stand, and the collection carries the piles to Logs to.",
            "Each full pack goes on a tile beside you, onto a log pile there if one lies there; no walk (the server "
            + "puts a drop on your own tile back in the pack). The piles are kept, and at Collect at the collection "
            + "hops them to the Logs to place: a tile, a bag or a pile, not a pack animal.");
        m_axeButton = lumberSet.Tool;
        m_logsButton = lumberSet.Place;
        m_lumberWords = lumberSet.Words;
        Button fromSet = Btn("Chop from",
            () => { string? w = m_f.LumberFromTarget(); m_circSetupNote.Text = w == null
            ? "Target where the chopping starts from: the line to Logs to is chopped first." : "Chop from: " + w; });
        m_tipper.SetToolTip(fromSet,
            "Target the far end of the chopping: the trees on the line from here to the Logs to place are chopped "
            + "first, then oval by oval outward around both, a chunk's width at a time, the nearest tree to you first "
            + "in each. Unset, the circuit starts from where you stand.");
        Button fromClear = Btn("Here",
            () => { m_f.LumberFrom = ""; m_circSetupNote.Text = m_f.LumberFromText() + "."; });
        m_tipper.SetToolTip(fromClear, "Chop from where you stand when the circuit starts.");
        Button emptyClear = Btn("Clear empties",
            () => { int n = m_f.LumberEmptyClear(); m_circSetupNote.Text = n
            + " chunks found empty forgotten: they are chopped again."; });
        m_tipper.SetToolTip(emptyClear,
            "A chunk that answered \"not enough wood\" is kept empty across runs and restarts. The first one is the "
            + "sentinel: every resume and every finished collection chops it first; wood there is a new day and every "
            + "chunk is chopped again, none means they all stay empty and the path starts from the sentinel. This "
            + "forgets them and the sentinel now.");
        lumber.Line("Order", Strip(fromSet, fromClear));
        lumber.Field("Range (tiles)", Num(1, 200, m_f.LumberRadius, v => m_f.LumberRadius = v),
            "How far out from the line between Chop from and Logs to the ovals reach, across its middle (with no Logs "
            + "to place, a circle of this radius round Chop from), read before every tree it picks: a change takes "
            + "effect during the circuit.");
        lumber.Field("Collect at (logs)", Num(1, 60000, m_f.LumberCollectAt, v => m_f.LumberCollectAt = v),
            "With At my feet, the logs dropped in piles that start the collection: each pile hopped along its way to "
            + "the Logs to place, lifted and put down 2 tiles ahead while you stand, the piles merging on the "
            + "shortest network to it; then chopping goes on.");
        Button collectNow = Btn("Collect now",
            () => { string? w = m_f.CollectNow(); m_circSetupNote.Text = w
            ?? (m_f.CircuitRunning ? "Collecting at the next step, then chopping." : "Collecting the piles."); });
        m_tipper.SetToolTip(collectNow,
            "Collect the piles at feet now, whatever Collect at says: during Lumberjacking at its next step, then "
            + "chopping goes on; with no circuit running, the collection alone.");
        Button collectHere = Btn("Reset collection point",
            () => { string? w = m_f.LumberRecollectHere(); m_circSetupNote.Text = w
            ?? m_f.LumberLogsText() + ": the next collection carries the piles here."; });
        m_tipper.SetToolTip(collectHere,
            "Logs to becomes the tile you stand on; nothing starts. The next collection (Collect at, or Collect now) "
            + "carries the piles here, with a log pile in view at the old Logs to place.");
        lumber.Line("Collection", Strip(collectHere));
        jobRow.Add(collectNow);
        m_circLaunch = m_circLaunch.Set("Collect now", collectNow);
        // The chunks chopped, one row per 8x8 chunk (Damian, 2026-10-22: "lets add the chunks display").
        // On the Circuit activity card below the job (Damian, 2026-10-24: running data "to a section at the bottom
        // under the Job card, above the totals card, like is done with buy reagents"); added there.
        m_lumberZones = new SortList { View = View.Details, Height = 130, FullRowSelect = true, GridLines = true,
            AutoSort = false };
        foreach (string c in new[] { "Chunk:70", "Chops:45", "Logs:40", "Failed:45", "Empty:45", "Last chop:65",
            "Last empty:70", "Back:65" })
        {
            string[] a = c.Split(':');
            m_lumberZones.Columns.Add(a[0], int.Parse(a[1]));
        }
        m_tipper.SetToolTip(m_lumberZones,
            "Each 8x8 chunk chopped this session: chops, chops that gave logs, failed chops, how often it ran out of "
            + "wood, the last chop and \"not enough wood\"; and every chunk found empty in any run, kept empty until "
            + "Clear empties.");
        // The feather harvest's setup (AssistHarvest.md stage 3): the lumberjack's place, At my feet and collection,
        // with a blade for the axe and a bird's range for the trees'.
        FieldGrid feathers = Own("feathers", Grid(g));
        GatherRow featherSet = GatherSet(feathers, "Blade",
            "Target the blade in your pack or your hand: a knife, a dagger, a sword or an axe.",
            "Target the blade that fights and carves, in your pack or your hand: any knife, cleaver, dagger, sword, "
            + "spear or axe. It goes into your hand for the fight, and whatever you hold goes into the pack.",
            m_f.FeatherBladeTarget,
            "Feathers to", "Target where the feathers and the meat go: a tile, a bag, a pile or the floor.",
            "Target where the feathers and the meat go: a tile (they pile there), a bag or a pile. Before the pack is "
            + "full the circuit walks there and puts them down; with At my feet the collection carries the piles "
            + "there, and the meat rides in the pack to it.", m_f.FeathersToTarget, m_f.FeathersText,
            () => m_f.FeatherAtFeet, v => m_f.FeatherAtFeet = v,
            ": each full pack's feathers go on a pile where you stand, and the collection carries the piles to "
            + "Feathers to.",
            "Each full pack's feathers go on a tile beside you, onto a feather pile there if one lies there. At "
            + "Collect at the collection hops the piles to Feathers to, and the meat in the pack goes down there with "
            + "them.");
        m_bladeButton = featherSet.Tool;
        m_feathersToButton = featherSet.Place;
        m_featherWords = featherSet.Words;
        feathers.Field("Range (tiles)", Num(1, AssistState.GroundReach, m_f.FeatherRadius, v => m_f.FeatherRadius = v),
            "How far from where you stand a bird is hunted, read before every bird it picks: a change takes effect "
            + "during the circuit. The nearest bird first; none left in range ends the circuit, after the piles are "
            + "collected.");
        feathers.Field("Leave a fight under (% hits)", Num(0, 99, m_f.FeatherMinHits, v => m_f.FeatherMinHits = v),
            "Your hits, in percent of your most, under which a fight ends the circuit and you are told: an eagle "
            + "fights back.");
        feathers.Field("Collect at (feathers)", Num(1, 60000, m_f.FeatherCollectAt, v => m_f.FeatherCollectAt = v),
            "With At my feet, the feathers dropped in piles that start the collection to Feathers to; then the hunt "
            + "goes on.");
        // The reagent harvest's setup: how far from you a reagent pile is walked to; the pickup's reagents are Item
        // pickup's.
        FieldGrid reagentsSet = Own("reagents", Grid(g));
        reagentsSet.Field("Range (tiles)", Num(1, 1000, m_f.ReagentRange, v => m_f.ReagentRange = v),
            "How far from where you stand a reagent pile the map shows is walked to, nearest first. Item pickup lifts "
            + "each pile in reach (its reagent list is the Actions tab's Item pickup); a pile it leaves is left for "
            + "the run.");
        // Refresh's loadout bag picked from the Circuit tab (Damian, 2026-10-18: "for refresh setup add a button to do
        // the selection of loadout bag").
        Button loadPick = Btn("Pick loadout bag, then storage",
            () => { m_f.LoadoutTarget(); m_circSetupNote.Text = "Target the loadout bag in your pack, then the bag it "
            + "refills from."; });
        m_tipper.SetToolTip(loadPick,
            "Two target cursors: first the bag in your pack that holds the reagent loadout, then the bag it refills "
            + "from (in the bank box).");
        Button noStore = Btn("No storage bag", () => m_circSetupNote.Text = m_f.LoadoutNoStorage());
        m_tipper.SetToolTip(noStore,
            "Refresh refills the loadout from every bag in the bank box, and buys what the bank lacks in the town's "
            + "mage shops (exactly the shortfall), then banks the change.");
        Own("refresh", Grid(g)).Line("Loadout", Strip(loadPick, noStore));
        // The smithing circuit's setup: where its ingots come from, and the tinker menus for a sledge hammer.
        FieldGrid smith = Own("smith", Grid(g));
        ComboBox ingotFrom = Pick(130, "the bank", "a smith", "the mine's storage", "run mining circuit");
        ingotFrom.SelectedIndex = m_f.SmithIngots == "buy" ? 1
            : m_f.SmithIngots == "mine" ? 2 : m_f.SmithIngots == "harvest" ? 3 : 0;
        ingotFrom.SelectedIndexChanged += (s, e) =>
            m_f.SmithIngots = ingotFrom.SelectedIndex == 1 ? "buy"
            : ingotFrom.SelectedIndex == 2 ? "mine" : ingotFrom.SelectedIndex == 3 ? "harvest" : "bank";
        m_tipper.SetToolTip(ingotFrom,
            "The bank: the ingots at a time set beside this from the city's bank box, the gold banked on the same "
            + "visit. A smith: bought from a smith in reach whose stock lists iron ingots. The mine's storage: where "
            + "Cave mining stores its ingots (its Storage setting): an ingot stash in the house walked to and lifted "
            + "from, the bank box for a stash in it, else the pack. Run mining circuit: when out of ingots, the Cave "
            + "mining circuit runs at the picked cave until the pack is full of ingots, then the bank rune brings you "
            + "back and the smithing goes on.");
        smith.Field("Ingots from", ingotFrom);
        smith.Field("At a time", Num(1, 9999, m_f.SmithIngotLoad, v => m_f.SmithIngotLoad = v),
            "Ingots the circuit carries from each bank visit or purchase. The pack's weight limit is the ceiling.");
        TextBox tinkerBox = smith.Field("Sledge menus", new TextBox { Width = 130, Text = m_f.SmithTinker },
            "The tinker's menu entries to a sledge hammer, each the start of an entry's text, separated by |.");
        tinkerBox.Leave += (s, e) => { if (tinkerBox.Text.Trim().Length > 0) m_f.SmithTinker = tinkerBox.Text.Trim(); };
        // What the smithing circuit builds for: skill, profit, or an order kept for people at its best NPC prices.
        ComboBox goal = smith.Field("Build for", Pick(130, "skill", "profit", "order"),
            "Skill: your ladder until Blacksmithing reads 100.0. Profit: the item of the most gold per ingot the "
            + "Maker tab knows, sold to the smiths. Order: the order list, built extra and the best kept for people "
            + "by what the smith would pay.");
        goal.SelectedItem = m_f.SmithGoal;
        goal.SelectedIndexChanged += (s, e) => m_f.SmithGoal = (string)goal.SelectedItem;
        smith.Field("Build x", Num(1, 10, m_f.SmithPickOf, v => m_f.SmithPickOf = v),
            "Each piece of the order is built this many times; the smith's offers pick the best, the rest are sold to "
            + "it.");
        TextBox orderBox = new()
        {
            Width = 210,
            Text = m_f.SmithOrder
        };
        orderBox.Leave += (s, e) => m_f.SmithOrder = orderBox.Text.Trim();
        m_tipper.SetToolTip(orderBox,
            "The order: item@count separated by ; (katana@5;spear@5). Names as the game names them.");
        ComboBox orderPreset = Pick(95, "Presets...");
        foreach (string[] op in AssistFeatures.SmithOrderPresets)
            orderPreset.Items.Add(op[0]);
        orderPreset.SelectedIndex = 0;
        orderPreset.SelectedIndexChanged += (s, e) => { if (orderPreset.SelectedIndex
            <= 0) return; orderBox.Text = AssistFeatures.SmithOrderPresets[orderPreset.SelectedIndex
            - 1][1]; m_f.SmithOrder = orderBox.Text; orderPreset.SelectedIndex = 0; };
        smith.Line("Order", Strip(orderBox, orderPreset));
        // The ingot-buying circuit's count and highest price.
        FieldGrid ingots = Own("ingots", Grid(g));
        ingots.Field("Ingots to buy", Num(1, 60000, m_f.IngotBuyCount, v => m_f.IngotBuyCount = v),
            "Iron ingots to buy in all, at the shops ticked below, else the town's smith shops; they go into the bank "
            + "box's container already holding the most of them.");
        ingots.Field("At most (gp)", Num(1, 999, m_f.IngotBuyPrice, v => m_f.IngotBuyPrice = v),
            "The highest price paid for one ingot.");
        // The advertiser's ad, its places and its books per place (Damian, 2026-10-18: "make it configurable for which
        // shops/banks/other localles, how many books per bank, and the message of course").
        FieldGrid advert = Own("advert", Grid(g));
        TextBox adTitle = advert.Line("Ad title", new TextBox { Width = 220, Text = m_f.AdTitle },
            "The book's title, shown when the book is clicked on the ground; also how the circuit knows its ad books "
            + "in the pack.");
        adTitle.Leave += (s, e) => m_f.AdTitle = adTitle.Text;
        advert.Field("Books a place", Num(1, 20, m_f.AdPer, v => m_f.AdPer = v),
            "Books put down at each place; a book this session put down that still lies there counts.");
        advert.Field("At most (gp)", Num(1, 999, m_f.AdPrice, v => m_f.AdPrice = v),
            "The highest price paid for a blank book at the town's provisioners and scribes.");
        // The places as a grid with type and town filters, and the message under it as a wrapping box that grows with
        // its lines (Damian, 2026-10-18: "message needs to be a wrapping textbox with expanding lines, under the places
        // control, which needs to be a grid with types and cities filter combos").
        m_adType = Pick(120);
        m_adType.SelectedIndexChanged += (s, e) => { if (!m_fillingAd) FillAdPlaces(); };
        m_adTown = Pick(120);
        m_adTown.SelectedIndexChanged += (s, e) => { if (!m_fillingAd) FillAdPlaces(); };
        m_tipper.SetToolTip(m_adType, "Shows only the places of this type: a bank, a spot, or a shop kind.");
        m_tipper.SetToolTip(m_adTown, "Shows only the places in this town.");
        Button adHere = Btn("Add where I stand",
            () => { m_circSetupNote.Text = "Advertise: " + m_f.AdAddHere(); FillAdPlaces(); });
        m_tipper.SetToolTip(adHere,
            "Adds the place you stand at: the bank within 12 tiles, else the shop you stand in, else this spot.");
        m_adCount = Words("");
        advert.Line("Places", Strip(m_adType, m_adTown, adHere, m_adCount));
        m_adGrid = Own("advert",
            Add(g, new DataGridView { Height = 160, AllowUserToAddRows = false, AllowUserToDeleteRows = false,
            RowHeadersVisible = false, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            BackgroundColor = SystemColors.Window, SelectionMode = DataGridViewSelectionMode.FullRowSelect }));
        m_adGrid.Columns.Add(new DataGridViewCheckBoxColumn { HeaderText = "Use", FillWeight = 8 });
        foreach (string c in new[] { "Order:8", "Type:18", "Place:46", "Town:20", "Key:0" })
        {
            string[] a = c.Split(':');
            DataGridViewTextBoxColumn col = new()
            {
                HeaderText = a[0],
                ReadOnly = true,
                FillWeight = Math.Max(1, int.Parse(a[1]))
            };
            if (a[0] == "Key")
                col.Visible = false;
            m_adGrid.Columns.Add(col);
        }
        m_tipper.SetToolTip(m_adGrid,
            "Tick the places the ad goes to; they are visited in the order shown, ticked ones first. Each is reached "
            + "by the bank's rune for its town.");
        m_adGrid.CurrentCellDirtyStateChanged += (s, e) => { if (m_adGrid.IsCurrentCellDirty) m_adGrid.CommitEdit(DataGridViewDataErrorContexts.Commit); };
        m_adGrid.CellValueChanged += (s, e) =>
        {
            if (m_fillingAd || e.ColumnIndex != 0 || e.RowIndex < 0)
                return;
            DataGridViewRow row = m_adGrid.Rows[e.RowIndex];
            string? key = ((string?)row.Cells[5].Value)!;
            bool on = row.Cells[0].Value is bool && (bool)row.Cells[0].Value!;
            List<string> l = m_f.AdPlaces();
            l.Remove(key!);
            if (on)
                l.Add(key!);
            m_f.SetAdPlaces(l);
            BeginInvoke(new Action(FillAdPlaces));
        };
        Own("advert",
            Add(g, new Label { Height = 18, AutoSize = false, Text = "Message (wraps; Enter starts a new line):" }));
        TextBox adText = new()
        {
            Multiline = true,
            WordWrap = true,
            ScrollBars = ScrollBars.None,
            AcceptsReturn = true,
            Height = 40,
            Text = (m_f.AdText ?? "").Replace("|", Environment.NewLine)
        };
        adText.Leave += (s, e) => m_f.AdText = adText.Text.Replace("\r\n", "|").Replace("\n", "|");
        adText.TextChanged += (s, e) => { int lines = Math.Max(2,
            adText.GetLineFromCharIndex(Math.Max(0, adText.TextLength)) + 2); int h = Math.Min(260,
            lines * adText.Font.Height + 8); if (adText.Height != h) { adText.Height = h; Fit(); } };
        Own("advert", Add(g, adText));
        // The books the advertiser buys, by their pictures from the client's art (Damian, 2026-10-18: "i need a picker
        // for book type with graphics from the game files that match books sold at the vendors"); each toggles.
        List<Control> bookPicks = new();
        for (int bi = 0; bi < AssistFeatures.BookArts.Length; bi++)
        {
            int art = AssistFeatures.BookArts[bi];
            string bname = AssistFeatures.BookNames[bi];
            CheckBox cb = new()
            {
                Appearance = Appearance.Button,
                Width = 74,
                Height = 40,
                Text = bname.Replace(" book", ""),
                TextImageRelation = TextImageRelation.ImageBeforeText,
                ImageAlign = ContentAlignment.MiddleLeft,
                Checked = Array.IndexOf(m_f.AdBookArts, art) >= 0
            };
            m_tipper.SetToolTip(cb,
                (AssistFeatures.BookWritable1998(art) ? "Buy "
                : "1998 refuses to write into it (\"Cannot write into that book.\"); buy ") + "the " + bname + " ("
                + art.ToString("X4") + ") for the ad, sold by provisioners and scribes.");
            cb.CheckedChanged += (s, e) => { List<int> l = new(); foreach (Tuple<CheckBox, int> p in m_adBookPicks) if (p.Item1.Checked) l.Add(p.Item2); m_f.AdBookArts = l.ToArray(); };
            m_adBookPicks.Add(Tuple.Create(cb, art));
            bookPicks.Add(cb);
        }
        Own("advert", Grid(g)).Line("Ad books", Strip(bookPicks.ToArray()));
        // The rune marker's class of place, its runes a place and its highest rune price (Damian, 2026-10-18: "mark a
        // rune to a spot at each of a class like banks, tailors, etc.").
        FieldGrid mark = Own("mark", Grid(g));
        m_markClass = mark.Field("Mark at every", Pick(120),
            "The class of place: every bank, or every shop of a kind (as the region file names it), in every town.");
        m_markClass.SelectedIndexChanged += (s, e) => { if (m_markClass.SelectedItem
            != null) m_f.MarkClass = (string)m_markClass.SelectedItem; };
        mark.Field("Runes a place", Num(1, 20, m_f.MarkPer, v => m_f.MarkPer = v),
            "Runes marked at each place, each on its own spot spread apart from every spot marked there before.");
        mark.Field("At most (gp)", Num(1, 999, m_f.MarkPrice, v => m_f.MarkPrice = v),
            "The highest price for a blank recall rune at the town's mage shops.");
        // The spellbook maker's books, its inscription floor and its highest prices.
        FieldGrid spell = Own("spellbook", Grid(g));
        spell.Field("Stock books", Num(1, 50, m_f.SpellbookCount, v => m_f.SpellbookCount = v),
            "Stock books to fill: the pack's spellbooks other than your own (the one of the most spells), and as many "
            + "more bought.");
        CheckBox spInscribe = new()
        {
            Text = "Inscribe at (%)",
            AutoSize = true,
            Checked = m_f.SpellbookInscribe
        };
        spInscribe.CheckedChanged += (s, e) => m_f.SpellbookInscribe = spInscribe.Checked;
        spell.Pair(spInscribe, Num(1, 100, m_f.SpellbookChance, v => m_f.SpellbookChance = v),
            "Inscribe the spells the vendors did not have: only spells in your own book, a circle your Magery can "
            + "cast and your mana holds, and only while the 1998 chance is at least this percent (a failure ruins the "
            + "scroll and gains nothing).");
        spell.Field("Book at most (gp)", Num(1, 9999, m_f.SpellbookPrice, v => m_f.SpellbookPrice = v),
            "The highest price for a spellbook.");
        spell.Field("Scroll at most (gp)", Num(1, 9999, m_f.ScrollPrice, v => m_f.ScrollPrice = v),
            "The highest price for a spell scroll.");
        m_tipper.SetToolTip(adText,
            "The ad's text. The book's lines are broken at " + AssistFeatures.AdLineWidth + " characters, "
            + AssistFeatures.AdPageLines + " lines a page, " + AssistFeatures.AdPagesMax
            + " pages at most; a new line here starts a new line in the book.");
        m_circSetupNote = Add(g, new Label { Height = 18, AutoSize = false });
        // Tailoring: what is sold and its lowest prices, and what is sewn.
        m_sellGrid = Own("tailor",
            Add(g, new DataGridView { Height = 110, AllowUserToAddRows = true, AllowUserToDeleteRows = true,
            RowHeadersVisible = true, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            BackgroundColor = SystemColors.Window }));
        foreach (string c in new[] { "Item to sell", "Arts (hex)", "Lowest price" })
            m_sellGrid.Columns.Add(c, c);
        m_sellGrid.CellEndEdit += (s, e) => SaveSellGrid();
        m_sellGrid.UserDeletedRow += (s, e) => SaveSellGrid();
        m_tipper.SetToolTip(m_sellGrid,
            "What the circuit sells and the lowest price for each. Arts are the item's graphics in hex, comma "
            + "separated. Delete a row to drop it.");
        FieldGrid tailor = Own("tailor", Grid(g));
        TextBox menuBox = tailor.Field("Sewing menus", new TextBox { Width = 160, Text = m_f.ProductMenu },
            "The sewing menus' picks by their text, separated by |: shirts|Fancy shirt.");
        menuBox.Leave += (s, e) => m_f.ProductMenu = menuBox.Text.Trim();
        // The thread overflow (Damian, 2026-10-20: "a configurable thread overflow procedure").
        Button ovRune = Btn(m_f.OverflowRune != 0 ? "Reset house rune" : "Set house rune",
            () => { string? w = m_f.OverflowTarget(false); if (w
            != null) m_circSetupNote.Text = "Thread overflow: " + w; });
        m_overflowRuneButton = ovRune;
        m_tipper.SetToolTip(ovRune,
            "Open your bank box, then target the rune to your house. It stays in the box; the circuit recalls from "
            + "the bank.");
        Button ovPile = Btn(m_f.OverflowPileSet ? "Reset pile" : "Set pile",
            () => { string? w = m_f.OverflowTarget(true); if (w
            != null) m_circSetupNote.Text = "Thread overflow: " + w; });
        m_overflowPileButton = ovPile;
        m_tipper.SetToolTip(ovPile,
            "Standing in the house, target the thread pile on the floor (or its tile). Only thread on that one tile "
            + "is taken.");
        tailor.Line("Thread overflow", Strip(ovRune, ovPile));
        tailor.Field("Overflow take", Num(0, 2000, m_f.OverflowTake, v => m_f.OverflowTake = v, 50),
            "The thread taken from the pile at a time, as far as the carry limit allows.");
        tailor.Field("Overflow below", Num(0, 2000, m_f.OverflowBelow, v => m_f.OverflowBelow = v, 10),
            "A round of all the shops that buys fewer thread than this, with nothing left to sew, sends the circuit "
            + "to the pile (the vendor ramp-up; 120 is a fresh shard's 12 shops).");
        CheckBox homespun = new()
        {
            Text = "Homespun: sew at the house, sell at the shops",
            AutoSize = true,
            Checked = m_f.Homespun
        };
        CheckBox resumeShops = new()
        {
            Text = "Resume the shop order",
            AutoSize = true,
            Checked = m_f.ShopsResume
        };
        tailor.Wide(Strip(homespun, resumeShops));
        // The steps in place of night and day (Damian, 2026-10-24): each ticked step runs, the most finished first.
        CheckBox stepBuy = new()
        {
            Text = "Buy thread",
            AutoSize = true,
            Checked = m_f.StepBuyThread
        };
        CheckBox stepWeave = new()
        {
            Text = "Weave",
            AutoSize = true,
            Checked = m_f.StepWeave
        };
        CheckBox stepBolts = new()
        {
            Text = "Sew from bolts",
            AutoSize = true,
            Checked = m_f.StepBolts
        };
        CheckBox stepCloth = new()
        {
            Text = "Sew from cut cloth",
            AutoSize = true,
            Checked = m_f.StepCloth
        };
        tailor.Line("Steps", Strip(stepBuy, stepWeave, stepBolts, stepCloth));
        m_tipper.SetToolTip(stepBuy, "Buy the thread the shops have; at home it goes on the thread pile.");
        m_tipper.SetToolTip(stepWeave,
            "Weave the thread pile at the loom, only when no ticked cloth or bolts are left to sew.");
        m_tipper.SetToolTip(stepBolts, "Sew from the house floor's bolts, after its cut cloth.");
        m_tipper.SetToolTip(stepCloth, "Sew from the house floor's cut cloth first, the most finished.");
        stepBuy.CheckedChanged += (s, e) => m_f.StepBuyThread = stepBuy.Checked;
        stepWeave.CheckedChanged += (s, e) => m_f.StepWeave = stepWeave.Checked;
        stepBolts.CheckedChanged += (s, e) => m_f.StepBolts = stepBolts.Checked;
        stepCloth.CheckedChanged += (s, e) => m_f.StepCloth = stepCloth.Checked;
        m_tipper.SetToolTip(resumeShops,
            "Where Go starts. On: at the shop after the last one visited. Off: fresh at the order's first shop. "
            + "Within a circuit every bag of shirts carries on from the shop after the last one, round the order.");
        resumeShops.CheckedChanged += (s, e) => m_f.ShopsResume = resumeShops.Checked;
        m_tipper.SetToolTip(homespun,
            "At home: the thread carried put on the house pile; shirts sewn by the ticked steps from the floor where "
            + "it lies (never picked up), cut cloth then bolts, the loom when none is left; the left-over cloth put "
            + "down whenever the pack is nine tenths full, until it is nine tenths full of shirts. Out through the "
            + "door, the key back on the vendor, then the shops in order from Moonglow's: shirts sold, thread bought, "
            + "the bank as due. Home when the shirts are gone or every shop is visited, and again.");
        homespun.CheckedChanged += (s, e) => m_f.Homespun = homespun.Checked;
        // Buy reagents: the shopping list, each with a count and the highest price, and the purse.
        FieldGrid buy = Own("buy", Grid(g));
        // One item added as one line: the item, its count, its highest price, Add.
        ComboBox preset = Pick(120);
        foreach (string[] p in AssistFeatures.BuyPresets)
            preset.Items.Add(p[0]);
        preset.SelectedIndex = 0;
        NumericUpDown count = Num(1, 9999, 100, _ => { });
        m_tipper.SetToolTip(count, "How many of the item to buy this run.");
        NumericUpDown price = Num(1, 9999, 5, _ => { });
        m_tipper.SetToolTip(price, "The highest price paid for one; a shop asking more is passed over for that item.");
        Button add = Btn("Add",
            () => { string[] p = AssistFeatures.BuyPresets[preset.SelectedIndex]; m_f.SetBuyItem(p[0], p[1],
            (int)count.Value, (int)price.Value); FillCircuitSetup(); });
        m_tipper.SetToolTip(add,
            "Adds the item to the shopping list, or changes its count and price. For anything not in the list, type a "
            + "row into the grid below.");
        buy.Line("Add item", Strip(preset, Words("count"), count, Words("at most (gp)"), price, add));
        buy.Field("Purse", Num(0, 60000, m_f.BuyPurse, v => m_f.BuyPurse = v, 500),
            "The gold carried from each bank: topped up from, and banked onto, the bank box's own gold pile, never a "
            + "bag in it.");
        m_buyGrid = Own("buy",
            Add(g, new DataGridView { Height = 110, AllowUserToAddRows = true, AllowUserToDeleteRows = true,
            RowHeadersVisible = true, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            BackgroundColor = SystemColors.Window }));
        foreach (string c in new[] { "Item to buy", "Arts (hex)", "Count", "Highest price" })
            m_buyGrid.Columns.Add(c, c);
        m_buyGrid.CellEndEdit += (s, e) => SaveBuyGrid();
        m_buyGrid.UserDeletedRow += (s, e) => SaveBuyGrid();
        m_tipper.SetToolTip(m_buyGrid,
            "The shopping list: how many of each to buy this run and the highest price. The name is matched against "
            + "the shops' stock lists. Delete a row to drop it.");
        // Survey town: the town to visit.
        m_surveyTown = Own("survey", Grid(g)).Field("Town", Pick(170, HereTown),
            "The town to survey. Where I stand surveys the town you are in; any other town is reached by its rune in "
            + "the bank.");
        m_surveyTown.SelectedIndexChanged += (s, e) => { if (m_fillingShops) return; m_f.SurveyTown = m_surveyTown.SelectedIndex
            <= 0
            ? ""
            : m_surveyTown.SelectedItem!.ToString(); FillCircuitSetup(); };
        // The shops, by name, in visiting order.
        Label shopsLabel = Add(g,
            new Label { Text = "Shops to visit (ticked), top to bottom:", Height = 18, AutoSize = false });
        m_shopList = Add(g, new CheckedListBox { Height = 170, CheckOnClick = true, IntegralHeight = false });
        m_shopList.ItemCheck += (s, e) => BeginInvoke(new Action(SaveShops));
        m_tipper.SetToolTip(m_shopList,
            "The circuit visits only the ticked shops; the buy circuit visits them in this order. Untick a shop to "
            + "keep the circuit away from it.");
        List<Control> shopButtons = new();
        foreach (string bt in new[] { "Up", "Down", "All", "None" })
        {
            string which = bt;
            Button b = Btn(bt);
            shopButtons.Add(b);
            b.Click += (s, e) =>
            {
                int i = m_shopList.SelectedIndex;
                if (which == "All" || which == "None")
                {
                    for (int j = 0; j < m_shopList.Items.Count; j++)
                        m_shopList.SetItemChecked(j, which == "All");
                }
                else if (i >= 0)
                {
                    int to = which == "Up" ? i - 1 : i + 1;
                    if (to >= 0 && to < m_shopList.Items.Count)
                    {
                        object it = m_shopList.Items[i];
                        bool on = m_shopList.GetItemChecked(i);
                        m_shopList.Items.RemoveAt(i);
                        m_shopList.Items.Insert(to, it);
                        m_shopList.SetItemChecked(to, on);
                        m_shopList.SelectedIndex = to;
                    }
                }
                SaveShops();
            };
            m_tipper.SetToolTip(b,
                which == "Up" || which == "Down" ? "Moves the selected shop earlier or later in the visiting order."
                : which == "All" ? "Ticks every shop." : "Unticks every shop.");
        }
        FieldGrid shopRow = Grid(g);
        shopRow.Line(null, Strip(shopButtons.ToArray()));
        m_circShopsUi = new Control[] { shopsLabel, m_shopList, shopRow };
        m_circPick.SelectedIndexChanged += (s, e) => { m_f.Cfg.Set("circuit.selected",
            CircuitPicked); CircuitButtons(); FillCircuitSetup(); FillCircuit(); Fit(); };
        CircuitButtons();
        FillCircuitSetup();

        // The map of the circuit picked (Damian, 2026-10-24: "embed the map into the circuit tab between the circuit
        // settings and the status ... show all the relevant details for the selected circuit. so logs feathers, etc.
        // ... so i don't have to switch back and forth when I am monitoring a chop or slaughter"): the circuits' piles,
        // places and chunks here, off the Map tab.
        g = Group("Circuit map");
        Button cmz = Btn("+");
        Button cmo = Btn("-");
        Button cmCentre = Btn("Center on me", () => { m_circMap.OffX = m_circMap.OffY = 0; m_circMap.Invalidate(); });
        Button cmClear = Btn("Clear drops", () => { m_f.LumberPilesClear(); m_circMap.Invalidate(); });
        m_tipper.SetToolTip(cmClear, "Forget the log piles dropped for a collection.");
        m_circMapInfo = Words("");
        Grid(g).Line(null, Strip(cmCentre, cmz, cmo, cmClear, m_circMapInfo));
        m_circMap = Add(g,
            new MapView { Height = CircMapMin,
            Zoom = MapView.ZoomSteps.IndexOf(m_f.Cfg.GetInt("circuit.map.zoom", 2)) >= 0
            ? m_f.Cfg.GetInt("circuit.map.zoom", 2) : 2 });
        Action<int> cmZoom = d => { int at = MapView.ZoomSteps.IndexOf(m_circMap.Zoom); if (at
            < 0) at = 1; m_circMap.Zoom = MapView.ZoomSteps[Math.Max(0,
            Math.Min(MapView.ZoomSteps.Length - 1, at + d))]; m_f.Cfg.Set("circuit.map.zoom",
            m_circMap.Zoom.ToString()); m_circMap.Invalidate(); };
        cmz.Click += (s, e) => cmZoom(1);
        cmo.Click += (s, e) => cmZoom(-1);
        m_circMap.MouseWheel += (s, e) => cmZoom(e.Delta > 0 ? 1 : -1);
        m_circMap.Trail = () => { lock (m_trail) return m_trail.ToArray(); };
        m_circMap.Course = () => m_f.Course();
        m_circMap.Kills = () => m_f.KillMarks();
        m_circMap.Corpses = () => m_f.Corpses();
        m_circMap.Route = () => m_f.RouteShown();
        m_circMap.Pins = () => LoadPins().ToArray();
        m_circMap.Marks = MapMarks;
        m_circMap.Decor = m_f.DecorIn;
        m_circMap.DecorSeq = () => m_f.DecorSeq;
        m_circMap.ShowRegions = () => false;
        m_circMap.OwnHouses = OwnHouses;
        m_circMap.Caption = () => { int x = m_circMap.CenterX, y = m_circMap.CenterY; return (x | y) == 0 ? ""
            : CircuitMapWhat() + "  You: " + x + "," + y; };
        m_circMap.Chunks = () => CircuitMapChunks() ? m_f.HarvestChunks() : null;
        m_circMap.Drops = () => CircuitPicked == "lumber" ? m_f.LumberMapPiles() : null;
        m_circMap.Spots = () => CircuitPicked == "lumber" ? m_f.LumberSpots() : null;
        m_circMap.Resources = () => { int kind = CircuitMapKind(); return kind < 0 ? null
            : m_f.ResourceSpots().FindAll(r => r[2] == kind); };
        m_circMap.Hover = (x, y) => { string t = x < 0 ? "" : x + "," + y + "  " + Sextant(x, y); if (x >= 0
            && CircuitMapChunks()) { string? ct = m_f.ChunkText(x, y); if (ct
            != null) t += "  " + ct; } m_circMapInfo.Text = t; };
        m_circMap.Picked = s => ShowNpc(m_mapDetail, null!, s);
        Summarize(g, () => CircuitMapWhat());

        g = Group("Circuit job");
        m_circJobGroup = g;
        FieldGrid jobGrid = Grid(g);
        jobGrid.Line("Run", Strip(jobRow.ToArray()));
        jobGrid.Line("Suggest", suggestLine);
        m_circLight = Add(g, Light());
        Grid(g).Cell(TimingsTag(() => TaskTimingRegistry.ForCircuit(CircuitPicked)));
        m_circJob = Add(g, new Label { AutoSize = false, Height = 4 });
        m_growers = m_growers.Add(m_circJob);
        Summarize(g, () => FirstLine(m_circJob.Text));
        // What the running circuit does, apart from its setup: the vendors of the circuits that visit them, picked in
        // the setup; Lumberjacking's chunks.
        g = Group("Circuit activity");
        Own("lumber", Grid(g)).Line("Chunks", Strip(emptyClear));
        Own("lumber", Add(g, m_lumberZones));
        m_circVendors = Add(g,
            new SortList { View = View.Details, Height = 180, FullRowSelect = true, GridLines = true,
            AutoSort = true });
        ContextMenuStrip vendorMenu = new();
        ToolStripMenuItem vendorOff = new("Take out of the thread round");
        vendorMenu.Items.Add(vendorOff);
        m_circVendors.MouseDown += (s, e) =>
        {
            if (e.Button != MouseButtons.Right)
                return;
            ListViewItem? it = m_circVendors.GetItemAt(e.X, e.Y);
            if (it != null)
                it.Selected = true;
            bool row = it != null && it.Tag is uint;
            vendorOff.Enabled = row;
            vendorOff.Text = row && m_f.IsThreadOff((uint)it!.Tag!) ? "Put back in the thread round"
                : "Take out of the thread round";
        };
        vendorOff.Click += (s, e) =>
        {
            ListViewItem? it = m_circVendors.SelectedItems.Count > 0 ? m_circVendors.SelectedItems[0] : null;
            if (it == null || !(it.Tag is uint))
                return;
            uint serial = (uint)it.Tag;
            m_f.ThreadOffSerial(serial, it.Text, !m_f.IsThreadOff(serial));
        };
        m_circVendors.ContextMenuStrip = vendorMenu;
        CircCols(CircuitPicked == "buy" ? s_buyCols : s_tailorCols);
        Summarize(g,
            () => CircuitPicked == "lumber" ? m_lumberZones.Items.Count + " chunks"
            : m_circVendors.Items.Count + " vendors");
        g = Group("Circuit totals");
        // The gold made, by the big gold pile (Damian, 2026-10-17: "use that big gold pile graphic in the displays of
        // gold made").
        m_goldRow = Add(g, new FlowLayoutPanel { Height = 36, WrapContents = false });
        m_goldPic = new PictureBox { Width = 32, Height = 32, SizeMode = PictureBoxSizeMode.Zoom,
            Margin = new Padding(0, 2, 6, 2) };
        m_goldRow.Controls.Add(m_goldPic);
        m_goldLine = new Label { AutoSize = true, Margin = new Padding(0, 10, 0, 0),
            Font = new Font(Font.FontFamily, Font.Size + 1, FontStyle.Bold) };
        m_goldRow.Controls.Add(m_goldLine);
        m_circTotals = Add(g, new Label { AutoSize = false, Height = 4, Font = new Font(Font, FontStyle.Bold) });
        m_growers = m_growers.Add(m_circTotals);
        m_circAll = Add(g, new Label { AutoSize = false, Height = 4 });
        m_growers = m_growers.Add(m_circAll);
        Summarize(g, () => FirstLine(m_circTotals.Text));
    }

    // A gathering circuit's Set line (AssistHarvest.md stage 4), one builder for Lumberjacking and Feathers: the tool's
    // target button, the place's, At my feet and the place said in words. ask: what each button's cursor asks for;
    // feetOn: what ticking At my feet adds to the place's words.
    sealed class GatherRow
    {
        public Button Tool = null!, Place = null!; public Label Words = null!;
    }
    GatherRow GatherSet(FieldGrid grid, string tool, string toolAsk, string toolTip, Func<string?> toolTarget,
        string place, string placeAsk, string placeTip, Func<string?> placeTarget, Func<string> placeText,
        Func<bool> feet, Action<bool> setFeet, string feetOn, string feetTip)
    {
        GatherRow r = new();
        r.Tool = Btn(tool,
            () => { string? w = toolTarget(); m_circSetupNote.Text = w == null ? toolAsk : tool + ": " + w; });
        m_tipper.SetToolTip(r.Tool, toolTip);
        r.Place = Btn(place,
            () => { string? w = placeTarget(); m_circSetupNote.Text = w == null ? placeAsk : place + ": " + w; });
        m_tipper.SetToolTip(r.Place, placeTip);
        r.Words = Words(placeText());
        CheckBox atFeet = new()
        {
            Text = "At my feet",
            AutoSize = true,
            Checked = feet()
        };
        atFeet.CheckedChanged += (s, e) => { setFeet(atFeet.Checked); m_circSetupNote.Text = placeText()
            + (atFeet.Checked ? feetOn : "."); };
        m_tipper.SetToolTip(atFeet, feetTip);
        grid.Line("Set", Strip(r.Tool, r.Place, atFeet, r.Words));
        return r;
    }

    MapView m_circMap = null!; Label m_circMapInfo = null!; const int CircMapMin = 220;

    // What the circuit picked shows on its map: the resource kind (AssistFeatures.ResKinds index; -1 none) and whether
    // its harvest chunks are drawn.
    int CircuitMapKind()
    {
        switch (CircuitPicked)
        {
            case "lumber":
                return 0;
            case "mine":
                return 1;
            case "feathers":
                return 2;
            case "reagents":
                return 4;
            default:
                return -1;
        }
    }
    bool CircuitMapChunks()
    {
        return CircuitPicked == "lumber" || CircuitPicked == "mine";
    }
    string CircuitMapWhat()
    {
        int kind = CircuitMapKind();
        return kind < 0 ? "the circuit's walks"
            : AssistFeatures.ResKinds[kind] + (CircuitMapChunks() ? " and harvest chunks" : "")
            + (CircuitPicked == "lumber" ? ", drops and the logger's places" : "");
    }


    Button m_circuitTab;
    Label m_circJob = null!, m_circTotals = null!;
    SortList m_circVendors = null!;
    string m_circShape = "", m_circMemo = ""; readonly DateTime m_windowStart = DateTime.Now;
    FieldGrid m_setupGrid = null!; CheckBox m_trainingBox = null!; Button m_mineForgeButton = null!;
    ComboBox m_circPick = null!, m_surveyTown = null!;
    Label m_circSetupNote = null!;
    DataGridView m_sellGrid = null!, m_buyGrid = null!;
    CheckedListBox m_shopList = null!; Control[] m_circShopsUi = new Control[0];
    Map<string, Button> m_circLaunch = Map<string, Button>.Empty; bool m_fillingShops;
    const string HereTown = "(where I stand)";
    // The circuits by key, in the picker's order; the ones that deal with vendors show the vendor cards.
    static readonly ImmutableArray<string> s_circuitKeys = [ "tailor", "buy", "survey", "refresh", "mine", "smith",
        "ingots",
        "advert", "spellbook", "mark", "lumber", "feathers", "reagents" ];
    static readonly Set<string> s_vendorCircuits = Set<string>.Of("tailor", "buy", "survey");
    // The circuits that pick no shops: the shop picker is hidden for them.
    // The tailoring circuits' shops are the tailor runes bag's (Damian, 2026-10-20: "lets just use the runes in the bag
    // as the list of player approved shops"), so no picker.
    static readonly Set<string> s_shoplessCircuits = Set<string>.Of("mine", "smith", "advert", "spellbook", "mark",
        "tailor", "lumber", "feathers", "reagents");
    string CircuitPicked => m_circPick == null || m_circPick.SelectedIndex < 0
        || m_circPick.SelectedIndex >= s_circuitKeys.Length ? "tailor" : s_circuitKeys[m_circPick.SelectedIndex];
    // The totals card of a circuit with no vendor cards: its last run.
    string? CircuitLast(string c)
    {
        switch (c)
        {
            case "smith":
                return m_f.SmithStatus.Length > 0 ? "Last smithing: " + m_f.SmithStatus : "No smithing run yet.";
            case "mark":
                return m_f.MarkStatus.Length > 0 ? "Last marking: " + m_f.MarkStatus : "No marking run yet.";
            case "spellbook":
                return m_f.SpellbookStatus.Length > 0 ? "Last spellbooks: " + m_f.SpellbookStatus
                    : "No spellbook run yet.";
            case "advert":
                return m_f.AdStatus.Length > 0 ? "Last advertising: " + m_f.AdStatus : "No advertising run yet.";
            case "ingots":
                {
                    string? ie = m_f.CircuitEnds["Buy ingots"];
                    return ie != null ? "Last ingot buy: " + ie : "No ingot buy yet.";
                }
            case "refresh":
                return m_f.RefreshStatus.Length > 0 ? "Last refresh: " + m_f.RefreshStatus : "No refresh run yet.";
        }
        return null;
    }
    ComboBox m_caveList = null!; Button m_caveRune = null!; bool m_fillingCaves;
    // A setting button set says Reset (Damian, 2026-10-22: "setting buttons should aways say "Reset" after they are
    // set").
    static string SetText(string name, bool set)
    {
        return set ? "Reset " + name : name;
    }
    static void SetButton(Button b, string text)
    {
        if (b != null && b.Text != text)
            b.Text = text;
    }
    string CaveRuneText()
    {
        string r = m_f.CaveRuneOf(m_f.PickedCave!);
        return r.Length > 0 ? "Reset cave rune: " + r : "Cave rune: pick";
    }
    SortList m_mineZones = null!;
    Button m_bankRuneButton = null!, m_axeButton = null!, m_logsButton = null!, m_storeToButton = null!,
        m_stashButton = null!, m_oreToButton = null!;
    string m_zonesShape = "";
    Label m_stashWords = null!, m_lumberWords = null!, m_storeToWords = null!, m_oreWords = null!;
    string m_stashShown = "", m_lumberShown = "", m_lumberShape = ""; SortList m_lumberZones = null!;
    Button m_bladeButton = null!, m_feathersToButton = null!; Label m_featherWords = null!; string m_featherShown = "";
    // The cave list: where the circuit starts, then every cave marked, the picked one selected.
    void FillCaves()
    {
        if (m_caveList == null)
            return;
        m_fillingCaves = true;
        try
        {
            m_caveList.Items.Clear();
            m_caveList.Items.Add("(where I start)");
            foreach (string n in m_f.CaveNames())
                m_caveList.Items.Add(n);
            int at = m_caveList.Items.IndexOf(m_f.PickedCave);
            m_caveList.SelectedIndex = at > 0 ? at : 0;
            m_caveRune.Text = CaveRuneText();
        }
        finally { m_fillingCaves = false; }
    }
    // Each circuit's own setup controls (CircuitKeys), shown only while it is picked.
    Map<string, List<Control>> m_circOwn = Map<string, List<Control>>.Empty;
    T Own<T>(string circuit, T c) where T : Control
    {
        List<Control>? l;
        if (!m_circOwn.TryGet(circuit, out l))
            m_circOwn = m_circOwn.Set(circuit, l = new List<Control>());
        l.Add(c);
        return c;
    }
    void CircuitButtons()
    {
        string c = CircuitPicked;
        Button? b;
        foreach (string n in new[] { "Sell only", "Tour", "Buy thread", "Keep thread" })
            if (m_circLaunch.TryGet(n, out b))
                b.Visible = c == "tailor";
        if (m_circLaunch.TryGet("Resume", out b))
            b.Visible = c == "lumber" || c == "feathers";
        if (m_circLaunch.TryGet("New chop", out b))
            b.Visible = c == "lumber";
        if (m_circLaunch.TryGet("Collect now", out b))
            b.Visible = c == "lumber";
        if (m_circLaunch.TryGet("New hunt", out b))
            b.Visible = c == "feathers";
        if (m_circLaunch.TryGet("Go", out b))
            b.Visible = c != "lumber" && c != "feathers";
        foreach (KeyValuePair<string, List<Control>> kv in m_circOwn)
            foreach (Control o in kv.Value)
                o.Visible = kv.Key == c;
    }

    readonly List<Tuple<CheckBox, int>> m_adBookPicks = new();
    ComboBox m_markClass = null!;
    ComboBox m_adType = null!, m_adTown = null!;
    Label m_adCount = null!; DataGridView m_adGrid = null!; bool m_fillingAd;
    // The advertiser's places: every town's bank and shops from the region file, and the spots picked; the ticked ones
    // first in their visiting order, then the rest, each through the type and town filters.
    void FillAdPlaces()
    {
        if (m_adGrid == null)
            return;
        List<string> picked = m_f.AdPlaces();
        List<string[]> rows = new();
        foreach (string c in UoRegions.Cities())
        {
            if (UoRegions.BanksIn(c, 0, 0).Count > 0)
                rows.Add(new[] { "bank:" + c, "bank", "Bank of " + c, c });
            foreach (UoRegions.Shop s in UoRegions.AllShopsIn(c))
                rows.Add(new[] { "shop:" + s.Key, UoRegions.ShopKind(s.CX, s.CY) ?? "shop", s.Text, c });
        }
        foreach (string p in picked)
        {
            if (p.StartsWith("spot:"))
            {
                string[] xy = p.Substring(5).Split(',');
                int x, y;
                string town = xy.Length == 2 && int.TryParse(xy[0].Trim(), out x) && int.TryParse(xy[1].Trim(), out y)
                    ? UoRegions.City(x, y) ?? "" : "";
                rows.Add(new[] { p, "spot", p.Substring(5), town });
            }
        }

        m_fillingAd = true;
        try
        {
            if (m_adType.Items.Count <= 1 && rows.Count > 0)
            {
                m_adType.Items.Clear();
                m_adType.Items.Add("All types");
                List<string> kinds = new();
                foreach (string[] r in rows)
                    if (!kinds.Contains(r[1]))
                        kinds.Add(r[1]);
                kinds.Sort(StringComparer.Ordinal);
                foreach (string kd in kinds)
                    m_adType.Items.Add(kd);
                m_adType.SelectedIndex = 0;
                m_adTown.Items.Clear();
                m_adTown.Items.Add("All towns");
                foreach (string c in UoRegions.Cities())
                    m_adTown.Items.Add(c);
                m_adTown.SelectedIndex = 0;
            }
            string? ty = m_adType.SelectedIndex > 0 ? (string?)m_adType.SelectedItem
                : null, tw = m_adTown.SelectedIndex > 0 ? (string?)m_adTown.SelectedItem : null;
            Func<string[], bool> shown = r => (ty == null || r[1] == ty) && (tw == null || r[3] == tw);
            m_adGrid.Rows.Clear();
            for (int i = 0; i < picked.Count; i++)
            {
                string[] r = rows.Find(x => x[0] == picked[i])
                    ?? new[] { picked[i], picked[i].Split(':')[0], picked[i], "" };
                if (shown(r))
                    m_adGrid.Rows.Add(true, (i + 1).ToString(), r[1], r[2], r[3], r[0]);
            }
            foreach (string[] r in rows)
                if (!picked.Contains(r[0]) && shown(r))
                    m_adGrid.Rows.Add(false, "", r[1], r[2], r[3], r[0]);
        }
        finally { m_fillingAd = false; }
        m_adCount.Text = picked.Count + " picked";
    }
    // The shops the picker lists for the circuit: the survey's are the town's, every trade; the mining circuit picks
    // none.
    List<UoRegions.Shop> PickerShops(string circuit)
    {
        return circuit == "mine" || circuit == "smith" || circuit == "lumber" || circuit == "feathers"
            || circuit == "reagents" ? new List<UoRegions.Shop>()
            : circuit == "survey" ? UoRegions.AllShopsIn(m_f.SurveyTarget()!) : AssistFeatures.AllShops(circuit);
    }
    sealed class ShopEntry
    {
        public UoRegions.Shop Shop = null!; public override string ToString()
        {
            return UoRegions.Display(Shop) + "  (" + Shop.CX + "," + Shop.CY + ")";
        }
    }
    // The setup shown for the picked circuit: its lists, its shops (the picked ones first, in order), its launch modes.
    void FillCircuitSetup()
    {
        if (m_circPick == null)
            return;
        string circuit0 = CircuitPicked;
        foreach (Control c in m_circShopsUi)
            c.Visible = !s_shoplessCircuits.Contains(circuit0);
        if (m_circVendors != null)
            m_circVendors.Visible = circuit0 != "lumber";
        if (m_setupGrid != null && m_trainingBox != null)
            m_setupGrid.Show(m_trainingBox, circuit0 == "tailor");
        CircuitButtons();
        // The town list fills once the region file is read (after login).
        if (m_surveyTown.Items.Count <= 1)
        {
            m_fillingShops = true;
            try
            {
                foreach (string c in UoRegions.Cities())
                    m_surveyTown.Items.Add(c);
            }
            finally { m_fillingShops = false; }
        }
        m_fillingShops = true;
        try
        {
            int ti = m_f.SurveyTown!.Length == 0 ? 0 : m_surveyTown.Items.IndexOf(m_f.SurveyTown);
            m_surveyTown.SelectedIndex = ti < 0 ? 0 : ti;
        }
        finally { m_fillingShops = false; }
        m_sellGrid.Rows.Clear();
        foreach (AssistFeatures.SellItem i in m_f.SellList())
            m_sellGrid.Rows.Add(i.Name, string.Join(",", Array.ConvertAll(i.Arts, a => a.ToString("X4"))),
                i.Floor.ToString());
        m_buyGrid.Rows.Clear();
        foreach (AssistFeatures.BuyItem i in m_f.BuyList())
            m_buyGrid.Rows.Add(i.Name!, string.Join(",", Array.ConvertAll(i.Arts, a => a.ToString("X4"))),
                i.Count.ToString(), i.MaxPrice.ToString());
        m_fillingShops = true;
        try
        {
            m_shopList.Items.Clear();
            string circuit = CircuitPicked;
            List<UoRegions.Shop> all = PickerShops(circuit);
            List<string> keys = m_f.CircuitShopKeys(circuit);
            // The survey's ticks are the town's ticked shops; none of them ticked is every shop of the town.
            if (circuit == "survey" && !all.Exists(s => keys.Contains(s.Key)))
                keys = new List<string>();
            all.Sort((a, b) => { int c = string.Compare(UoRegions.City(a.CX, a.CY), UoRegions.City(b.CX, b.CY),
                StringComparison.Ordinal); return c != 0 ? c
                : string.Compare(a.Text, b.Text, StringComparison.Ordinal); });
            foreach (string key in keys)
            {
                UoRegions.Shop? s = all.Find(x => x.Key == key);
                if (s != null)
                    m_shopList.Items.Add(new ShopEntry { Shop = s }, true);
            }
            foreach (UoRegions.Shop s in all)
                if (!keys.Contains(s.Key))
                    m_shopList.Items.Add(new ShopEntry { Shop = s },
                        circuit != "buy" && circuit != "ingots" && keys.Count == 0);
        }
        finally { m_fillingShops = false; }
        if (circuit0 == "advert")
            FillAdPlaces();
        // The classes once the region file is read.
        if (circuit0 == "mark" && m_markClass != null && m_markClass.Items.Count <= 1)
        {
            m_markClass.Items.Clear();
            foreach (string mc in AssistFeatures.MarkClasses())
                m_markClass.Items.Add(mc);
            int mi = m_markClass.Items.IndexOf(m_f.MarkClass);
            m_markClass.SelectedIndex = mi >= 0 ? mi : 0;
        }
        m_circSetupNote.Text = SetupNote(circuit0);
    }
    // The setup note under the circuit's rows: what it does with the settings as they stand.
    string SetupNote(string circuit)
    {
        switch (circuit)
        {
            case "mark":
                return "Mark runes: every " + m_f.MarkClass + ", " + m_f.MarkPer
                    + " a place; blank runes in the pack (or bought here), a Mark scroll or the spell.";
            case "spellbook":
                return "Spellbooks: " + m_f.SpellbookCount + " stock books from the town's mage shops"
                    + (m_f.SpellbookInscribe ? ", inscribing at " + m_f.SpellbookChance + "% or better" : "")
                    + "; start in a town with a mage shop and a bank.";
            case "advert":
                return "Advertise: \"" + m_f.AdTitle + "\", " + m_f.AdPer + " a place at " + m_f.AdPlaces().Count
                    + " places; start in a town with a provisioner or scribe.";
            case "smith":
                return "Smithing: stand by an anvil and a forge with a smith's hammer, then Go. Ladder: "
                    + m_f.SmithLadderMode + ".";
            case "mine":
                return "Cave mining: set the forge, the storage and the bank rune, pick or mark the cave, then Go.";
            case "lumber":
                return "Lumberjacking: set the axe and where the logs go, stand among trees, then Go; it chops until "
                    + "stopped.";
            case "reagents":
                return "Reagents: walks to every reagent pile the map shows within the range, nearest first, for Item "
                    + "pickup to lift, and runs from threats; it ends when none is left.";
            case "feathers":
                return "Feathers: set the blade and where the feathers and meat go, stand among birds, then Resume; "
                    + "it hunts every bird in range, carves it and ends when none is left.";
            case "refresh":
                return "Refresh: " + m_shopList.Items.Count + " food shops; the loadout "
                    + (m_f.Cfg.Get("loadout.storage", "").Length > 0 ? "from its storage bag"
                    : "from every bag in the bank box, the shortfall bought") + ".";
            case "buy":
                return "Buy reagents: " + m_f.BuyList().Count + " items on the list, purse " + m_f.BuyPurse + ".";
            case "ingots":
                return "Buy ingots: " + m_f.IngotBuyCount + " at " + m_f.IngotBuyPrice + " gp at most, purse "
                    + m_f.BuyPurse + ".";
            case "survey":
                return "Survey: " + (m_f.SurveyTarget() ?? "no town") + ", " + m_shopList.Items.Count + " shops.";
        }
        return "Tailoring: sells " + m_f.SellList().Count + " items.";
    }
    void SaveShops()
    {
        if (m_fillingShops || m_shopList == null)
            return;
        List<string> keys = new();
        for (int i = 0; i < m_shopList.Items.Count; i++)
            if (m_shopList.GetItemChecked(i))
                keys.Add(((ShopEntry)m_shopList.Items[i]).Shop.Key);
        // Every tailor shop ticked is the default of every tailor shop, so a shop added to the region file is not left
        // out.
        if (CircuitPicked != "buy" && CircuitPicked != "ingots" && keys.Count == m_shopList.Items.Count)
            keys.Clear();
        m_f.SetCircuitShopKeys(CircuitPicked, keys);
    }
    static readonly ImmutableArray<string> s_tailorCols = [ "Vendor:90", "Town:80", "Last seen thread:110",
        "Last thread try:180",
        "Last sale try:180", "Bought out:75", "Restock:80", "Restocks seen:110", "Thread bought:95", "Shirts sold:90",
        "Shirt price:70", "Wants shirts:90" ];
    static readonly ImmutableArray<string> s_buyCols = [ "Vendor:90", "Town:80", "Item:110", "Bought:70", "Gold:70",
        "Last price:70",
        "Last bought:120" ];
    string m_circCols = "";
    void CircCols(IReadOnlyList<string> spec)
    {
        string key = string.Join("|", spec);
        if (key == m_circCols)
            return;
        m_circCols = key;
        m_circShape = "";
        m_circVendors.BeginUpdate();
        m_circVendors.Items.Clear();
        m_circVendors.Columns.Clear();
        foreach (string c in spec)
        {
            string[] a = c.Split(':');
            m_circVendors.Columns.Add(a[0], int.Parse(a[1]));
        }
        m_circVendors.EndUpdate();
    }
    bool IsBuyItem(string item)
    {
        string s = item.ToLowerInvariant();
        return m_f.BuyList().Exists(i => s.Contains(i.Name!.ToLowerInvariant())
            || s.Contains(UoTiles.Name(i.Arts[0]).ToLowerInvariant()));
    }
    // The buy circuit's cards: each vendor and item on the shopping list bought from, and the totals by item, this
    // session and on record.
    void FillBuyCards(List<AtlasTrade> trades)
    {
        CircCols(s_buyCols);
        Map<string, int[]> sums = Map<string, int[]>.Empty;
        Map<string, AtlasTrade> last = Map<string, AtlasTrade>.Empty;
        List<string> order = new();
        foreach (AtlasTrade t in trades)
        {
            if (t.Kind != "buy" || !IsBuyItem(t.Item))
                continue;
            string key = t.VendorSerial.ToString("X") + "|" + t.Item.ToLowerInvariant();
            int[]? s;
            if (!sums.TryGet(key, out s))
            {
                sums = sums.Set(key, s = new int[2]);
                order.Add(key);
            }
            s[0] += t.Amount;
            s[1] += t.Amount * t.Price;
            last = last.Set(key, t);
        }
        List<string[]> rows = new();
        foreach (string key in order)
        {
            AtlasTrade t = last[key]!;
            int[] s = sums[key]!;
            rows.Add(new[] { t.Vendor, UoRegions.City(t.X, t.Y) ?? "", t.Item, s[0].ToString(), s[1].ToString(),
                t.Price.ToString(), Ago(t.At) });
        }
        string shape = string.Join("|", rows.ConvertAll(r => string.Join(",", r)));
        if (shape != m_circShape)
        {
            m_circShape = shape;
            m_circVendors.BeginUpdate();
            m_circVendors.Items.Clear();
            foreach (string[] r in rows)
                m_circVendors.Items.Add(new ListViewItem(r));
            m_circVendors.EndUpdate();
        }
        string Sum(Predicate<AtlasTrade> when)
        {
            Map<string, int[]> by = Map<string, int[]>.Empty;
            List<string> names = new();
            foreach (AtlasTrade t in trades)
            {
                if (t.Kind != "buy" || !IsBuyItem(t.Item) || !when(t))
                    continue;
                string n = t.Item.ToLowerInvariant();
                int[]? s;
                if (!by.TryGet(n, out s))
                {
                    by = by.Set(n, s = new int[2]);
                    names.Add(n);
                }
                s[0] += t.Amount;
                s[1] += t.Amount * t.Price;
            }
            if (names.Count == 0)
                return "nothing on the shopping list bought";
            return string.Join("; ", names.ConvertAll(n => n + " " + by[n]![0] + " for " + by[n]![1] + " gold"));
        }
        List<string> left = m_f.BuyList().ConvertAll(i => i.Count + " " + i.Name);
        double hours = (DateTime.Now - m_windowStart).TotalHours;
        m_circTotals.Text = "This session (" + (hours < 1 ? (int)(hours * 60) + " min" : hours.ToString("0.0") + " h")
            + "): " + Sum(t => t.At >= m_windowStart) + "\nOn record: " + Sum(t => true) + "\nOn the shopping list: "
            + (left.Count > 0 ? string.Join(", ", left) : "nothing");
    }
    static readonly ImmutableArray<string> s_surveyCols = [ "Vendor:90", "Shop:170", "Items listed:85", "Stock:260",
        "Last seen:120" ];
    // The survey's cards: every keeper the atlas knows in the town, its shop and its stock list, and how many are read.
    void FillSurveyCards()
    {
        CircCols(s_surveyCols);
        string? town = m_f.SurveyTarget();
        List<string[]> rows = new();
        int listed = 0;
        if (m_f.Atlas != null && town != null)
        {
            foreach (AtlasNpc n in m_f.Atlas.Shops())
            {
                if (UoRegions.City(n.X, n.Y) != town)
                    continue;
                if (n.Items.Count > 0)
                    listed++;
                rows.Add(new[] { AssistFeatures.Untitled(n.Name), UoRegions.Where(n.X, n.Y) ?? "",
                    n.Items.Count.ToString(),
                    string.Join(", ", n.Items.GetRange(0, Math.Min(6, n.Items.Count)))
                    + (n.Items.Count > 6 ? ", ..." : ""), Ago(n.Last) });
            }
        }

        string shape = string.Join("|", rows.ConvertAll(r => string.Join(",", r)));
        if (shape != m_circShape)
        {
            m_circShape = shape;
            m_circVendors.BeginUpdate();
            m_circVendors.Items.Clear();
            foreach (string[] r in rows)
                m_circVendors.Items.Add(new ListViewItem(r));
            m_circVendors.EndUpdate();
        }
        m_circTotals.Text = (town ?? "No town") + ": " + rows.Count + " shopkeepers known, " + listed
            + " with a stock list read."
            + (m_f.TourReport.Length > 0 ? "\nLast report: " + m_f.TourReport.Replace("\r\n", " | ") : "");
    }
    static string Cell(DataGridViewRow r, int i)
    {
        object? v = r.Cells[i].Value;
        return v == null ? "" : v.ToString()!.Trim();
    }
    void SaveSellGrid()
    {
        List<string> l = new();
        foreach (DataGridViewRow r in m_sellGrid.Rows)
        {
            if (r.IsNewRow)
                continue;
            int floor;
            if (Cell(r, 0).Length > 0 && Cell(r, 1).Length > 0 && int.TryParse(Cell(r, 2), out floor))
                l.Add(Cell(r, 0) + "|" + Cell(r, 1) + "|" + floor);
        }
        m_f.Cfg.Set("circuit.sell", string.Join(";", l));
    }
    void SaveBuyGrid()
    {
        List<string> l = new();
        foreach (DataGridViewRow r in m_buyGrid.Rows)
        {
            if (r.IsNewRow)
                continue;
            int n, gp;
            if (Cell(r, 0).Length > 0 && Cell(r, 1).Length > 0 && int.TryParse(Cell(r, 2), out n)
                && int.TryParse(Cell(r, 3), out gp) && n > 0)
                l.Add(Cell(r, 0) + "|" + Cell(r, 1) + "|" + n + "|" + gp);
        }
        m_f.Cfg.Set("circuit.buy.list", string.Join(";", l));
    }
    static bool IsThread(string item)
    {
        string s = item.ToLowerInvariant();
        return s.Contains("thread") || s.Contains("shuttle") || s.Contains("spool");
    }
    bool IsShirt(string item)
    {
        string s = item.ToLowerInvariant();
        return m_f.SellList().Exists(i => s.Contains(i.Name.ToLowerInvariant()));
    }
    static string Ago(DateTime t)
    {
        int m = (int)(DateTime.Now - t).TotalMinutes;
        return t.ToString("HH:mm") + (m < 120 ? " (" + m + " min ago)" : "");
    }
    Label m_circAll = null!;
    // "Circuit ended (why): bought 0 thread, sewed 5 shirts, ..." without the parts whose count is 0, and without the
    // colon when every part is.
    static string NoZeros(string s)
    {
        int at = s.LastIndexOf("): ");
        if (at < 0)
            return s;
        List<string> keep = new();
        foreach (string part in s.Substring(at + 3).TrimEnd('.').Split(new[] { ", " }, StringSplitOptions.None))
        {
            bool zero = false;
            foreach (string w in part.Split(' '))
            {
                int n;
                if (int.TryParse(w, out n))
                {
                    zero = n == 0;
                    break;
                }
            }
            if (!zero)
                keep.Add(part);
        }
        return s.Substring(0, at + 1) + (keep.Count > 0 ? ": " + string.Join(", ", keep) + "." : "");
    }
    // Whether a control's own Visible can be read: it reads false while any parent is hidden (a collapsed card), so a
    // toggle compared against it set it again and called Fit on every tick, and the Circuits tab blinked from the map
    // down twice a second with "Circuit totals" collapsed and tailoring picked (2026-10-24).
    static bool Readable(Control c)
    {
        return c.Parent == null || c.Parent.Visible;
    }
    void FillCircuit()
    {
        if (m_circVendors == null)
            return;
        // Cave mining deals with no vendors (Damian, 2026-10-18: "when cave mining is picked, there is no need to show
        // circuit vendors for tailoring").
        Control? vbox = m_circVendors.Parent != null ? m_circVendors.Parent.Parent : null;
        // Refresh deals with food shops only (Damian: "same with like refresh: no need to show the vendors").
        if (vbox != null && m_page == "circuit")
        {
            bool show = s_vendorCircuits.Contains(CircuitPicked);
            if (Readable(vbox) && vbox.Visible != show)
            {
                vbox.Visible = show;
                Fit();
            }
        }
        // Every circuit's last end, under the totals.
        List<string> ends = new();
        foreach (KeyValuePair<string, string> kv in m_f.CircuitEnds.Ordered(StringComparer.Ordinal))
            ends.Add(kv.Key + ": " + kv.Value);
        string all = ends.Count == 0 ? "All circuits: none ended this session."
            : "All circuits, last end:\n" + string.Join("\n", ends);
        if (m_circAll.Text != all)
            m_circAll.Text = all;
        // The job card for every circuit, and the tailoring gold row only for tailoring (Damian, 2026-10-18: "the
        // circuit totals still lists tailoring details for the cave and other circuits").
        List<string> notes = new();
        lock (m_f.Notes)
            foreach (string n in m_f.Notes)
                if (n.Contains("Circuit:") || n.Contains("Buy:") || n.Contains("Smithing:") || n.Contains("Tools:")
                    || n.Contains("Toolbag:") || n.Contains("Refresh:") || n.Contains("Mine:") || n.Contains("Lumber:")
                    || n.Contains("Feathers:"))
                    notes.Add(n);
        // No inventory line and no zero counts (Damian, 2026-10-18: "in circuit job no need to show summary for
        // inventory or zeros").
        // A running tailoring circuit's live lines first, zeros shown (Damian, 2026-10-20: "i definitely want more live
        // information like we have with loops").
        string live = m_f.CircuitRunning && CircuitPicked == "tailor"
            ? "Now: " + m_f.CircuitStatus + "\nNext: " + m_f.CircuitNext + "\nPack: " + m_f.CircuitCounts() + "\n" : "";
        m_circJob.Text = (live.Length > 0 ? live.TrimEnd('\n') : NoZeros(m_f.CircuitStatus))
            + (notes.Count > 0
            ? "\n" + string.Join("\n", notes.GetRange(Math.Max(0, notes.Count - 6), Math.Min(6, notes.Count))) : "");
        if (Readable(m_goldRow) && m_goldRow.Visible != (CircuitPicked == "tailor"))
        {
            m_goldRow.Visible = CircuitPicked == "tailor";
            Fit();
        }
        if (CircuitPicked == "lumber")
        {
            string lt = m_f.LumberLogsText() + "\n" + m_f.LumberFromText() + "; " + m_f.LumberSentinelText()
                + (m_f.LumberStatus.Length > 0 ? "\nLumber: " + m_f.LumberStatus : "")
                + (m_f.LumberPiles().Count > 0
                ? "\nAt feet: " + m_f.LumberDropped() + " logs in " + m_f.LumberPiles().Count + " piles"
                + (m_f.LumberWild() > 0 ? " (" + m_f.LumberWild() + " wild)" : "") + ", the collection at "
                + m_f.LumberCollectAt : "")
                + (m_f.CollectStatus.Length > 0 ? "\nCollection: " + m_f.CollectStatus : "");
            if (m_circTotals.Text != lt)
                m_circTotals.Text = lt;
            string lw = m_f.LumberLogsText();
            if (m_lumberWords != null && m_lumberWords.Text != lw)
                m_lumberWords.Text = lw;
            SetButton(m_axeButton, SetText("Axe", m_f.LumberAxe != 0));
            SetButton(m_logsButton, SetText("Logs to", m_f.LumberLogsTo.Length > 0));
            if (m_lumberZones != null)
            {
                List<string[]> lr = m_f.LumberZoneRows();
                string lz = string.Join("|", lr.ConvertAll(r => string.Join(",", r)));
                if (lz != m_lumberShape)
                {
                    m_lumberShape = lz;
                    m_lumberZones.BeginUpdate();
                    m_lumberZones.Items.Clear();
                    foreach (string[] r in lr)
                        m_lumberZones.Items.Add(new ListViewItem(r));
                    m_lumberZones.EndUpdate();
                }
            }
            // A target taken is said where the button's ask was.
            string ls = m_f.LumberStatus;
            if (ls != m_lumberShown)
            {
                m_lumberShown = ls;
                if (ls.Length > 0)
                    m_circSetupNote.Text = ls;
            }
            return;
        }
        if (CircuitPicked == "reagents")
        {
            int known = 0;
            foreach (int[] r in m_f.ResourceSpots())
                if (r[2] == AssistFeatures.ReagentKind)
                    known++;
            string rt = known + " reagent piles known; Item pickup "
                + (m_f.Pickup ? "on" : "off (the circuit turns it on)")
                + (m_f.ReagentStatus.Length > 0 ? "\nReagents: " + m_f.ReagentStatus : "");
            if (m_circTotals.Text != rt)
                m_circTotals.Text = rt;
            return;
        }
        if (CircuitPicked == "feathers")
        {
            string ft = m_f.FeathersText() + (m_f.FeatherStatus.Length > 0 ? "\nFeathers: " + m_f.FeatherStatus : "")
                + (m_f.FeatherPileCount() > 0
                ? "\nAt feet: " + m_f.FeatherDropped() + " feathers in " + m_f.FeatherPileCount()
                + " piles, the collection at " + m_f.FeatherCollectAt : "")
                + (m_f.CollectStatus.Length > 0 ? "\nCollection: " + m_f.CollectStatus : "");
            if (m_circTotals.Text != ft)
                m_circTotals.Text = ft;
            string fw = m_f.FeathersText();
            if (m_featherWords != null && m_featherWords.Text != fw)
                m_featherWords.Text = fw;
            SetButton(m_bladeButton, SetText("Blade", m_f.FeatherBlade != 0));
            SetButton(m_feathersToButton, SetText("Feathers to", m_f.FeathersTo.Length > 0));
            string fs = m_f.FeatherStatus;
            if (fs != m_featherShown)
            {
                m_featherShown = fs;
                if (fs.Length > 0)
                    m_circSetupNote.Text = fs;
            }
            return;
        }
        if (CircuitPicked == "mine")
        {
            string mt = m_f.CaveText(m_f.PickedCave!) + (m_f.ToolbagStatus.Length > 0 ? "\n" + m_f.ToolbagStatus : "")
                + (m_f.MineStatus.Length > 0 ? "\nMining: " + m_f.MineStatus : "");
            if (m_circTotals.Text != mt)
                m_circTotals.Text = mt;
            SetButton(m_bankRuneButton,
                m_f.MineBankRune.Length > 0 ? "Reset bank rune: " + m_f.MineBankRune : "Bank rune: pick");
            SetButton(m_storeToButton, SetText("Store to", m_f.MineStoreTo.Length > 0));
            SetButton(m_oreToButton, SetText("Ore to", m_f.MineOreTo.Length > 0));
            string ow = m_f.MineOreText();
            if (m_oreWords != null && m_oreWords.Text != ow)
                m_oreWords.Text = ow;
            SetButton(m_stashButton, SetText("Ingot stash", m_f.StashSet));
            string ms = m_f.LumberStatus;
            if (ms != m_lumberShown)
            {
                m_lumberShown = ms;
                if (ms.StartsWith("Store to") || ms.StartsWith("Ore to"))
                    m_circSetupNote.Text = ms;
            }
            string sw = m_f.StashText();
            if (m_stashWords != null && m_stashWords.Text != sw)
                m_stashWords.Text = sw;
            string tw = m_f.MineStoreToText();
            if (m_storeToWords != null && m_storeToWords.Text != tw)
                m_storeToWords.Text = tw;
            string ss = m_f.StashStatus;
            if (ss != m_stashShown)
            {
                m_stashShown = ss;
                if (ss.Length > 0)
                    m_circSetupNote.Text = ss;
            }
            // Damian, 2026-10-20: "for cave mining, if the forge is set, say "Reset Forge" there".
            string fb = m_f.Cfg.Get(m_f.Server + "mine.forge", "").Length > 0 ? "Reset Forge" : "Forge";
            if (m_mineForgeButton != null && m_mineForgeButton.Text != fb)
                m_mineForgeButton.Text = fb;
            string cr = CaveRuneText();
            if (m_caveRune != null && m_caveRune.Text != cr)
                m_caveRune.Text = cr;
            if (m_mineZones != null)
            {
                List<string[]> zr = m_f.CaveZoneRows(m_f.PickedCave!);
                string zs = string.Join("|", zr.ConvertAll(r => string.Join(",", r)));
                if (zs != m_zonesShape)
                {
                    m_zonesShape = zs;
                    m_mineZones.BeginUpdate();
                    m_mineZones.Items.Clear();
                    foreach (string[] r in zr)
                        m_mineZones.Items.Add(new ListViewItem(r));
                    m_mineZones.EndUpdate();
                }
            }
            return;
        }
        // The vendors and totals of the circuit picked (Damian, 2026-10-17: "the circuit totals card on the circuit tab
        // is tailor specific").
        string? last = CircuitLast(CircuitPicked);
        if (last != null)
        {
            if (m_circTotals.Text != last)
                m_circTotals.Text = last;
            return;
        }
        // The trades are read again only when what the cards show can have changed: a trade, a stock list, a circuit
        // vendor's stock, the lists, the town, or the minute the ages and the session time count in.
        string memo = CircuitPicked + "|"
            + (m_f.Atlas != null
            ? Interlocked.Read(ref m_f.Atlas.TradesSeq) + "|" + Interlocked.Read(ref m_f.Atlas.StockedSeq) + "|"
            + (CircuitPicked == "survey" ? Interlocked.Read(ref m_f.Atlas.ChangeSeq) : 0) : "")
            + "|" + Interlocked.Read(ref m_f.CircuitVendorsSeq) + "|" + DateTime.Now.ToString("HH:mm") + "|"
            + m_f.Cfg.Get("circuit.sell", "") + "|" + m_f.Cfg.Get("circuit.buy.list", "") + "|" + m_f.SurveyTarget()
            + "|" + m_f.TourReport + "|" + m_f.Cfg.Get(m_f.Server + "thread.off", "");
        if (memo == m_circMemo)
            return;
        m_circMemo = memo;
        List<AssistFeatures.CircuitVendor> vendors = m_f.CircuitVendors();
        List<AtlasTrade> trades = m_f.Atlas != null ? m_f.Atlas.Trades(0) : new List<AtlasTrade>();
        if (CircuitPicked == "buy")
        {
            FillBuyCards(trades);
            return;
        }
        if (CircuitPicked == "survey")
        {
            FillSurveyCards();
            return;
        }
        CircCols(s_tailorCols);
        {
            int spent = 0, made = 0;
            foreach (AtlasTrade t in trades)
                if (t.At >= m_windowStart)
                {
                    if (t.Kind == "buy" && IsThread(t.Item))
                        spent += t.Amount * t.Price;
                    else if (t.Kind == "sell" && IsShirt(t.Item))
                        made += t.Amount * t.Price;
                }
            if (m_goldPic.Image == null && UoArt.Folder != null)
                m_goldPic.Image = UoArt.Icon(GoldPileArt);
            string g = (made - spent) + " gold net this session (" + made + " made, " + spent + " on thread)";
            if (m_goldLine.Text != g)
                m_goldLine.Text = g;
        }
        // Every vendor the circuit bought thread from, and every vendor that bought shirts.
        List<uint> serials = new();
        foreach (AssistFeatures.CircuitVendor v in vendors)
            serials.Add(v.Serial);
        foreach (AtlasTrade t in trades)
            if ((t.Kind == "buy" && IsThread(t.Item) || t.Kind == "sell" && IsShirt(t.Item))
                && !serials.Contains(t.VendorSerial))
                serials.Add(t.VendorSerial);
        foreach (uint s0 in m_f.AttemptVendors())
            if (!serials.Contains(s0) && vendors.Exists(x => x.Serial == s0))
                serials.Add(s0);
        List<string[]> rows = new();
        List<uint> rowSerials = new();
        foreach (uint s in serials)
        {
            AssistFeatures.CircuitVendor? v = vendors.Find(x => x.Serial == s);
            List<AtlasTrade> mine = trades.FindAll(t => t.VendorSerial == s);
            string name = v != null && v.Name.Length > 0 ? v.Name
                : mine.Count > 0 ? mine[mine.Count - 1].Vendor : s.ToString("X");
            int x = v != null ? v.X : mine.Count > 0 ? mine[mine.Count - 1].X : 0, y = v != null ? v.Y
                : mine.Count > 0 ? mine[mine.Count - 1].Y : 0;
            int tb = 0, tg = 0, ss = 0, sg = 0, lastPrice = 0;
            foreach (AtlasTrade t in mine)
            {
                if (t.Kind == "buy" && IsThread(t.Item))
                {
                    tb += t.Amount;
                    tg += t.Amount * t.Price;
                }
                else if (t.Kind == "sell" && IsShirt(t.Item))
                {
                    ss += t.Amount;
                    sg += t.Amount * t.Price;
                    lastPrice = t.Price;
                }
            }
            string restock = "", restocks = "", stockText = "", boughtOut = "";
            if (v != null)
            {
                if (v.Stock >= 0)
                    stockText = v.Stock + " at " + v.StockAt.ToString("HH:mm");
                if (v.BoughtOutAt > DateTime.MinValue.AddDays(1))
                {
                    boughtOut = v.BoughtOutAt.ToString("HH:mm");
                    DateTime due = m_f.ThreadDueAt(v.Serial);
                    int left = (int)Math.Ceiling((due - DateTime.Now).TotalMinutes);
                    restock = v.Stock > 0 && v.StockAt > v.BoughtOutAt ? "has stock"
                        : left > 0 ? "in " + left + " min" : "due";
                }
                List<string> r = new();
                foreach (int[] a in v.Restocks)
                    r.Add(a[0].ToString());
                restocks = string.Join(", ", r);
            }
            List<AssistFeatures.SellItem> sl = m_f.SellList();
            string wants = m_f.Tailor != null && sl.Count > 0 ? m_f.Tailor.WantAt(name, sl[0].Name) : "";
            if (m_f.IsThreadOff(s))
                restock = "off";
            rowSerials.Add(s);
            rows.Add(new[] { AssistFeatures.Untitled(name), (UoRegions.City(x, y) ?? ""), stockText,
                m_f.LastAttempt(s, "buy"), m_f.LastAttempt(s, "sell"), boughtOut, restock, restocks,
                tb > 0 ? tb + " for " + tg : "", ss > 0 ? ss + " for " + sg : "",
                lastPrice > 0 ? lastPrice.ToString() : "", wants });
        }
        string shape = string.Join("|", rows.ConvertAll(r => string.Join(",", r)));
        if (shape != m_circShape)
        {
            m_circShape = shape;
            m_circVendors.BeginUpdate();
            m_circVendors.Items.Clear();
            for (int i = 0; i < rows.Count; i++)
                m_circVendors.Items.Add(new ListViewItem(rows[i]) { Tag = rowSerials[i] });
            m_circVendors.EndUpdate();
        }
        // Totals: this session (since the window opened) and every trade on record.
        string Sum(Predicate<AtlasTrade> when)
        {
            int tb = 0, tg = 0, ss = 0, sg = 0;
            foreach (AtlasTrade t in trades)
                if (when(t))
                {
                    if (t.Kind == "buy" && IsThread(t.Item))
                    {
                        tb += t.Amount;
                        tg += t.Amount * t.Price;
                    }
                    else if (t.Kind == "sell" && IsShirt(t.Item))
                    {
                        ss += t.Amount;
                        sg += t.Amount * t.Price;
                    }
                }
            return "thread bought " + tb + " for " + tg + " gold; sold " + ss + " for " + sg + " gold; net "
                + (sg - tg) + " gold";
        }
        double hours = (DateTime.Now - m_windowStart).TotalHours;
        m_circTotals.Text = "This session (" + (hours < 1 ? (int)(hours * 60) + " min" : hours.ToString("0.0") + " h")
            + "): " + Sum(t => t.At >= m_windowStart) + "\nOn record: " + Sum(t => true);
    }
}
