# xHCI Service Model -- What the Spec Guarantees, and What Can Legally Break It

**Source: `docs/Reference/xHCI_Specification.pdf` -- Intel "eXtensible Host
Controller Interface for Universal Serial Bus (xHCI) Requirements
Specification", Revision 1.2, May 2019, 645 pages. Retrieved 2026-08-03 from
the Wayback Machine snapshot (20260211194053) of Intel's canonical URL
(intel.com serves 403 to non-browser agents). Full extracted text in
`xHCI_Specification.txt` for Grep; page numbers below are PDF pages.**

This note exists because the driver, the probes, and the codex-vm model were
all written without this document on disk, and every gap between what they
assumed and what the spec says was discovered on metal at the cost of a
flight. Every claim below carries its section and page. Do not extend the
driver or the vm model from memory of this note; Grep the spec text.

## The normative guarantee our board violates

Section 4.14.3 (p. 266): "If an interrupt transfer ring has been idle, the
maximum time between the xHC receiving a doorbell ring for the endpoint and
scheduling the first associated interrupt transaction on USB for the first TD
posted to Transfer Ring shall be equal to IST + ESIT."

For a full-speed keyboard with Interval 6 (8 ms ESIT), that is roughly a
10 ms ceiling. The ASUS Intel xHC (8086:a12f) has delivered nothing in 90 s
across three flights. Therefore at least one PRECONDITION of that sentence
fails on this board, or the part is non-conformant. The preconditions are
finite, and each has an instrument:

| # | Precondition (spec words) | Cite | Already excluded? | Instrument |
|---|---|---|---|---|
| 1 | "the xHC receiving a doorbell ring" -- writes are internally recorded; ignored only for Disabled slot / Disabled, Halted, Error endpoint | 4.7 p. 158-160 | Partly: Stop Endpoint on the same slot completes, so the slot is live and DB array writes reach the xHC. EP state was 1 (Running) at configure | FSE presence (below); second-stop dq |
| 2 | Endpoint in Running state | 4.8.3 p. 162-165 | Yes: est=1 flown v11/v12; Stop honored (est=3) proves the xHC parses this context | -- |
| 3 | Port carrying traffic: PLS = U0. A U3 port is suspended; "the xHC shall not automatically transition a root hub port from the Resume or U3 state to U0" -- software must | 4.15 p. 277-278; PLS values Table 5-27 p. 408 | No. Enumeration proves U0 at configure TIME; nothing has read PLS at pump time. Spec-wise nothing we did should suspend it (PLS=3 write needs LWS, 4.15.1 p. 278; USB2 HW LPM needs software-enabled HLE, 4.23.5) -- but this is exactly the class of assumption that has been wrong before | SCHEDX PORTSC/PLS read at pump time |
| 4 | Frames exist: MFINDEX advancing. "If all Root Hub ports are in the Disconnected, Disabled, Training, or Powered-off state the MFINDEX counting action may be stopped" (EU3S optionally adds U3) | 4.14.2 p. 260 | No. With a connected enabled port it must tick -- measure, do not assume | SCHEDX MFINDEX sampled twice across a PIT delay |
| 5 | Bandwidth admitted: Configure Endpoint would have failed with Bandwidth Error otherwise | 4.14.1.1 p. 257 | Yes: configure returned Success on metal | -- |
| 6 | Context parameters schedulable: Interval, Max ESIT Payload, Average TRB Length ("accuracy of this parameter is particularly important for periodic endpoints") | 4.14.1.1 p. 257, 6.2.3 | Yes by inspection + bed: driver writes Interval 6, ESIT 8, ATL 8 (`GopXhci.codex` xhci-ictx-ep); vm periodic check MATCHes descriptor-derived values | -- |
| 7 | Device answers: a NAKing Interrupt IN is retried every ESIT forever; CErr only decrements on errors, not NAKs | 4.14.3.1 p. 267 | Yes for the device side: phase-2 firmware handback delivers keys through the same port and silicon. NOT excluded: OUR schedule never issuing the IN at all | dq alone cannot separate never-issued from NAK-loop (see below); PLS/MFINDEX/FSE narrow it |

## What Stop Endpoint actually promises (and what v13 measured)

- On stop, the xHC "shall write the final value of the endpoint's Dequeue
  Pointer to the TR Dequeue Pointer field and CCS flag to the DCS field of
  the Output Endpoint Context" BEFORE generating the Command Completion
  Event (4.6.9 p. 133). This is the only moment the controller's true
  position is published. v13 metal: dq = ring base. The scheduler never
  completed -- and note, never STARTED -- a TD.

- **A NAK loop also parks dq at the same TRB** (the TD is in progress or
  pending retry every ESIT, 4.14.3.1 p. 267), so "dq parked" by itself does
  not separate "never scheduled" from "scheduled and NAKed forever". On this
  board the separation comes from phase 2: the device demonstrably answers
  polls from the firmware driver. Do not re-conflate these.

- **Mandatory Force Stopped Event.** In ALL timing scenarios of a Stop
  Endpoint -- including "executed between TDs", i.e. an idle ring -- the xHC
  "shall" generate a Transfer Event on the stopped endpoint (Completion Code
  Stopped / Stopped - Length Invalid / Stopped - Short Packet) BEFORE the
  Command Completion Event (4.6.9 p. 134). An xHC that answers the command
  but emits no Stopped Transfer Event is not executing the transfer-side
  half of the command. The v13 probe latches transfer events for its own
  slot during the command wait, so the FSE (if any) is already recorded on
  the flown glass as LATCH/code -- read it from the photo before flying v14.

- **EP State write-back is lazy, with one forcing rule**: on a
  Stopped-to-Running doorbell transition the xHC shall update the output EP
  State to Running "before any Transfer Events are generated" (4.8.3
  p. 165), and the spec explicitly warns EP State "may not reflect the
  current state"; software "shall maintain an internal variable ... and not
  depend on EP State" (4.8.3 p. 165-166). Therefore v13's re=3 means
  exactly: NO transfer event has occurred since the restart. It does NOT
  mean the restart was refused. Any instrument that reads est while an
  endpoint is nominally running measures the last forced write-back, not
  the present.

- Stop Endpoint received in Halted or Error state: no effect, Command
  Completion with Context State Error (4.8.3 p. 164). The vm arm must
  refuse from those states, not park the endpoint.

## Doorbell rules the driver and vm must honor (4.7 p. 158-160)

- DB Target = DCI; EP1 IN = DCI 3 (4.8.1, Figure 4-4 p. 160). Doorbell 0 is
  the command ring only.
- The xHC internally records ALL doorbell writes; no clearing, reads return
  nothing.
- Writes referencing a Disabled slot or Disabled endpoint: xHC should post
  a Transfer Event with Endpoint Not Enabled Error; may ignore.
- "The xHC shall ignore doorbell references to endpoints in the Halted or
  Error state" (p. 160). A stopped endpoint's doorbell restarts it at the
  saved dequeue (4.8.3 p. 165, 4.6.9 p. 134 option 1).

## PLS quick table (Table 5-27, p. 408; PORTSC bits 8:5, valid only if PP=1)

0 U0 (active), 1 U1, 2 U2, 3 U3 (suspended), 4 Disabled, 5 RxDetect,
6 Inactive, 7 Polling, 8 Recovery, 9 Hot Reset, 10 Compliance, 11 Test,
15 Resume.

## The device side (USB 2.0 + HID 1.11, landed 2026-08-03)

The v14 flight moved the fault off the controller (FSE code 26 twice:
the xHC has the TD in progress and is issuing INs; the device NAKs).
`USB_2_0_Specification.pdf` and `HID_1_11_Specification.pdf` are now in
this directory with `.txt` extractions for Grep. The clauses that
adjudicate a NAKing-but-healthy boot keyboard:

- **HID F.3 (Boot Keyboard Requirements): "The Boot Keyboard shall send
  data reports when the interrupt in pipe is polled, even when there
  are no new key events. The Set_Idle request shall override this
  behavior."** Every-poll reporting is the DEFAULT. A silent boot
  keyboard under sustained polls is only lawful if Set_Idle silenced
  it.
- **HID 7.2.4 Set_Idle: duration 0 "silences a particular report on the
  Interrupt In pipe until a new event occurs".** Our driver sent
  exactly this (duration 0) at setup; the firmware path that
  demonstrably receives keys has no reason to. Removed from
  `GopUsbKbd.codex` setup as of v15: the every-poll default is what a
  continuously re-armed ring wants, and a quirky device may over-honor
  duration 0 as never-report.
- **HID 7.2.1 Get_Report: MANDATORY for all HID devices** -- the current
  input report over EP0, bypassing the interrupt pipe entirely. EP0
  demonstrably works on the board (enumeration, SCHEDX commands), so
  GET_REPORT polling is a lawful keyboard fallback if the interrupt
  pipe stays dead. `-hid-*` beds model it as of 2026-08-03.
- **USB2 8.5.2 (Figure 8-35 discussion): an endpoint's data toggle is
  initialized to DATA0 by "any configuration event" (9.1.1.5, 9.4.5)**,
  i.e. our SET_CONFIGURATION resets the device-side toggle; toggle
  desync from the firmware era does not survive a proper
  reset-and-configure, and a single desync self-heals after one
  discarded packet in any case. Toggle theories need wire-level
  evidence before any more code.
- A NAK does not consume the endpoint error budget (interrupt IN NAK is
  retried next ESIT, xHCI 4.14.3.1); sustained est=1 across 90 s of
  polls proves the device is ACTIVELY handshaking NAK, not absent --
  timeouts would have halted the endpoint via CErr within milliseconds.

## Standing rule earned here

A bed arm is written FROM a spec section, with the section cited next to
the arm, or it is not written. An arm derived from what the driver already
does can only ever agree with the driver -- it is the
oracle-made-of-its-subject defect (L-ORACLE) wearing emulator clothes, and
it is how three mandatory behaviors (FSE, endpoint-command state checks,
MFINDEX) were absent from a model the fleet had been trusting for weeks.
