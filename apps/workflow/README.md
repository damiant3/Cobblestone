# Codex Workflow

A generic long-running business process engine with a state-machine executor, server-side HTML UI, and pre-built process templates targeting the title insurance, auto claims, and mortgage industries.

## Modules

- **WorkflowTypes** -- ProcessDef (directed graph of steps), ProcessInstance (running case with data bag, history, documents, participants), 10 StepTypes (HumanTask, AutomatedAction, ApprovalGate, DocumentGate, SubProcess, ParallelSplit/Join, TimerWait, ConditionalBranch, Notification), conditional transitions with boolean algebra, 15 FieldInputTypes, AssignmentRule (7 strategies), SLA/escalation
- **WorkflowEngine** -- DB schema (6 tables via Codex.Data), process definition versioning, start-process lifecycle, complete-step with transition evaluation and automatic advance, condition evaluation, document gate checking, SLA breach/warning detection
- **WorkflowTemplates** -- Full Title Insurance (8 steps, 4 forms, 5 roles); full Auto Claim (10 steps, fraud flag, coverage denial path); Mortgage Origination (10 steps, credit webhook, wire transfer); generic simple-approval factory
- **WorkflowHtml** -- Server-side HTML rendering: task inbox, process tracker with visual step flow, data table, document list, timeline audit trail, complete form rendering for all 15 field types, alert panel, dashboard

## Completeness

85% -- The engine, type system, templates, and HTML are production-quality and highly detailed. Missing: no HTTP server/router wiring (HTML generated but no request handler); ParallelSplit/Join execution not implemented (types only); TimerWait has no scheduler; SubProcess nesting absent; DB persistence schema defined but not wired to instances.

## Codex Conformance

Full -- Written entirely in Codex. HTML output targets a browser plug for delivery. DB persistence targets Codex.Data. No foreign stubs.
